using System;
using System.Collections.Generic;
using System.Globalization;

namespace Firebot.Core;

internal enum SmartFarmAction
{
    None,
    GoBack,
    FightBoss,
    LeaveBoss
}

/// <summary>What the battlefield shows right now, as read by Smart Farm.</summary>
internal readonly struct SmartFarmView
{
    public SmartFarmView(int stage, double dps, double waveHp, bool bossFight, bool farming,
        bool waveIsBoss = false, double elapsed = 0, double remainingHp = -1)
    {
        Stage = stage;
        Dps = dps;
        WaveHp = waveHp;
        BossFight = bossFight;
        Farming = farming;
        WaveIsBoss = waveIsBoss;
        Elapsed = elapsed;
        RemainingHp = remainingHp < 0 ? waveHp : remainingHp;
    }

    public int Stage { get; }

    /// <summary>Damage per second the team deals.</summary>
    public double Dps { get; }

    /// <summary>Full health of the current wave.</summary>
    public double WaveHp { get; }

    /// <summary>A boss wave is running (its timer or leave button is shown).</summary>
    public bool BossFight { get; }

    /// <summary>The game is farming the wave before the boss (the Fight boss button is shown).</summary>
    public bool Farming { get; }

    /// <summary>The wave on screen is a boss wave of any kind; its health never counts as a normal wave's.</summary>
    public bool WaveIsBoss { get; }

    /// <summary>Seconds since the current wave appeared.</summary>
    public double Elapsed { get; }

    /// <summary>Health the current wave has left.</summary>
    public double RemainingHp { get; }
}

/// <summary>
///     Farm where the team's damage clears waves quickly, and move on only when it can.
///     <para>
///         Normal waves: one that would take (health / DPS) or has already taken longer than Max Wave Seconds
///         sends the team back one stage. Bosses: one that cannot die within the 60-second timer is left at
///         once, and during the fight it is left as soon as the time spent plus the time still needed passes
///         the timer - that catches team wipes from area damage, which health and DPS cannot predict. While
///         farming, the boss is fought only when the remembered boss and the next stage's slow waves are within
///         reach. Every number is checked with the safety margin.
///     </para>
///     <para>
///         A slow wave or a boss that ended badly is remembered as "equivalent health": what the team would
///         have had to clear at its DPS then (time spent x DPS + health left). Records expire after
///         <see cref="MemoryMinutes" />, so a bad reading cannot block progress for good.
///     </para>
/// </summary>
internal sealed class SmartFarmPolicy
{
    public const double BossSeconds = 60;
    public const double MemoryMinutes = 10;

    private readonly Dictionary<int, (double Hp, DateTime At)> _bossHp = new();
    private readonly Dictionary<int, (double Hp, DateTime At)> _slowWaveHp = new();
    private int _waveStage = -1;
    private double _waveHp;

    public double MaxWaveSeconds { get; set; } = 10;
    public double MarginPercent { get; set; } = 20;

    public string LastReason { get; private set; } = "";

    public bool TryGetBossHp(int stage, DateTime now, out double hp) => TryRecall(_bossHp, stage, now, out hp);
    public bool TryGetSlowWaveHp(int stage, DateTime now, out double hp) => TryRecall(_slowWaveHp, stage, now, out hp);

    private double Margin => 1 + Math.Max(0, MarginPercent) / 100;

    private static double Seconds(double hp, double dps) => dps > 0 ? hp / dps : double.PositiveInfinity;

    public SmartFarmAction Decide(SmartFarmView view) => Decide(view, DateTime.Now);

    public SmartFarmAction Decide(SmartFarmView view, DateTime now)
    {
        LastReason = "";
        if (view.Stage <= 0 || view.Dps <= 0 || double.IsNaN(view.WaveHp) || view.WaveHp < 0)
        {
            LastReason = "battle numbers unreadable";
            return SmartFarmAction.None;
        }

        if (view.BossFight) return DecideBoss(view, now);

        if (view.Farming)
        {
            _waveStage = -1;
            if (TryRecall(_bossHp, view.Stage, now, out var bossHp) && Seconds(bossHp, view.Dps) * Margin > BossSeconds)
            {
                LastReason = $"farming: boss at stage {view.Stage} needs {Seconds(bossHp, view.Dps):0.#}s";
                return SmartFarmAction.None;
            }

            if (TryRecall(_slowWaveHp, view.Stage + 1, now, out var slowHp) &&
                Seconds(slowHp, view.Dps) * Margin > MaxWaveSeconds)
            {
                LastReason = $"farming: stage {view.Stage + 1} waves need {Seconds(slowHp, view.Dps):0.#}s";
                return SmartFarmAction.None;
            }

            LastReason = TryRecall(_bossHp, view.Stage, now, out _)
                ? $"boss at stage {view.Stage} is within reach"
                : $"boss at stage {view.Stage} not seen yet; trying it";
            return SmartFarmAction.FightBoss;
        }

        // Between a boss and the next normal wave the wave type can lag; boss health never counts here.
        if (view.WaveIsBoss)
        {
            LastReason = "waiting for a normal wave";
            return SmartFarmAction.None;
        }

        // A normal wave, while the game moves forward on its own.
        if (_waveStage != view.Stage)
        {
            _waveStage = view.Stage;
            _waveHp = 0;
        }

        _waveHp = Math.Max(_waveHp, view.WaveHp);
        var predicted = Seconds(_waveHp, view.Dps);
        if (predicted > MaxWaveSeconds)
        {
            Remember(_slowWaveHp, view.Stage, _waveHp, now);
            LastReason = $"stage {view.Stage} waves need {predicted:0.#}s of {MaxWaveSeconds:0.#}s";
            _waveStage = -1;
            return SmartFarmAction.GoBack;
        }

        // Observed: the wave is already taking too long (area damage wiping the team, regeneration, ...).
        if (view.Elapsed > MaxWaveSeconds * Margin)
        {
            var equivalent = Math.Max(_waveHp, view.Elapsed * view.Dps + view.RemainingHp);
            Remember(_slowWaveHp, view.Stage, equivalent, now);
            LastReason = $"stage {view.Stage} wave still alive after {view.Elapsed:0.#}s (predicted {predicted:0.#}s)";
            _waveStage = -1;
            return SmartFarmAction.GoBack;
        }

        LastReason = $"stage {view.Stage} waves need {predicted:0.#}s";
        return SmartFarmAction.None;
    }

    private SmartFarmAction DecideBoss(SmartFarmView view, DateTime now)
    {
        var full = Remember(_bossHp, view.Stage, view.WaveHp, now);
        var fullSeconds = Seconds(full, view.Dps);
        if (fullSeconds * Margin > BossSeconds)
        {
            LastReason = $"boss at stage {view.Stage} needs {fullSeconds:0.#}s (+{MarginPercent:0}% margin) of {BossSeconds:0}s";
            return SmartFarmAction.LeaveBoss;
        }

        // Live: time already spent plus what the boss still needs at the current DPS.
        var projected = view.Elapsed + Seconds(view.RemainingHp, view.Dps) * Margin;
        if (projected > BossSeconds)
        {
            Remember(_bossHp, view.Stage, view.Elapsed * view.Dps + view.RemainingHp, now);
            LastReason = $"boss at stage {view.Stage} falling behind: {view.Elapsed:0.#}s spent, " +
                         $"{Seconds(view.RemainingHp, view.Dps):0.#}s still needed";
            return SmartFarmAction.LeaveBoss;
        }

        LastReason = $"boss at stage {view.Stage}: {view.Elapsed:0.#}s spent, {Seconds(view.RemainingHp, view.Dps):0.#}s to go";
        return SmartFarmAction.None;
    }

    private static bool TryRecall(Dictionary<int, (double Hp, DateTime At)> map, int stage, DateTime now, out double hp)
    {
        hp = 0;
        if (!map.TryGetValue(stage, out var entry)) return false;
        if (now - entry.At > TimeSpan.FromMinutes(MemoryMinutes))
        {
            map.Remove(stage);
            return false;
        }

        hp = entry.Hp;
        return true;
    }

    /// <summary>Keeps the highest health seen (the first reading of a wave is its fullest) and refreshes its age.</summary>
    private static double Remember(Dictionary<int, (double Hp, DateTime At)> map, int stage, double hp, DateTime now)
    {
        var known = TryRecall(map, stage, now, out var old) ? Math.Max(old, hp) : hp;
        map[stage] = (known, now);
        return known;
    }

    /// <summary>
    ///     Reads numbers as the battle screen prints them: "DPS: 1.0e201", "7.5e197 HP", "12.3K".
    ///     Anything else, or a value beyond double range, is unreadable.
    /// </summary>
    public static bool TryParseScreenNumber(string text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        var colon = s.LastIndexOf(':');
        if (colon >= 0) s = s.Substring(colon + 1);
        s = s.Replace("HP", "").Replace(",", "").Trim();
        if (s.Length == 0) return false;

        double scale = 1;
        var last = char.ToUpperInvariant(s[s.Length - 1]);
        var suffix = last switch { 'K' => 1e3, 'M' => 1e6, 'B' => 1e9, 'T' => 1e12, _ => 0 };
        if (suffix > 0)
        {
            scale = suffix;
            s = s.Substring(0, s.Length - 1).Trim();
        }

        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return false;
        value = number * scale;
        return value >= 0 && !double.IsInfinity(value) && !double.IsNaN(value);
    }
}
