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

/// <summary>What the battle screen shows right now, as read by the Smart Farm task.</summary>
internal readonly struct SmartFarmView
{
    public SmartFarmView(int stage, double dps, double waveHp, bool bossFight, bool farming)
    {
        Stage = stage;
        Dps = dps;
        WaveHp = waveHp;
        BossFight = bossFight;
        Farming = farming;
    }

    public int Stage { get; }

    /// <summary>Screen DPS already scaled by the DPS factor setting.</summary>
    public double Dps { get; }

    /// <summary>The enemy health the stage bar shows.</summary>
    public double WaveHp { get; }

    /// <summary>A boss wave is running (its timer or leave button is shown).</summary>
    public bool BossFight { get; }

    /// <summary>The game is farming the wave before the boss (the Fight boss button is shown).</summary>
    public bool Farming { get; }
}

/// <summary>
///     Farm where the team's damage clears waves quickly, and move on only when it can. All numbers come from
///     the battle screen: the DPS text and the stage bar's enemy health.
///     <para>
///         A normal wave slower than the limit sends the team back one stage. A boss that cannot die within the
///         boss timer is left at once instead of being fought to the end. While farming, the boss is fought
///         only when the known boss health and the next stage's slow wave (if one was seen) are both within
///         reach, with the safety margin. A boss never seen yet is tried: leaving it costs a moment.
///     </para>
/// </summary>
internal sealed class SmartFarmPolicy
{
    public const double BossSeconds = 60;

    // Highest health seen per stage: the bar shows what is left, so the first reading is the largest.
    private readonly Dictionary<int, double> _bossHp = new();
    private readonly Dictionary<int, double> _slowWaveHp = new();
    private int _waveStage = -1;
    private double _waveHp;

    public double MaxWaveSeconds { get; set; } = 10;
    public double MarginPercent { get; set; } = 20;

    public string LastReason { get; private set; } = "";

    public bool TryGetBossHp(int stage, out double hp) => _bossHp.TryGetValue(stage, out hp);
    public bool TryGetSlowWaveHp(int stage, out double hp) => _slowWaveHp.TryGetValue(stage, out hp);

    private double Margin => 1 + Math.Max(0, MarginPercent) / 100;

    private static double Seconds(double hp, double dps) => dps > 0 ? hp / dps : double.PositiveInfinity;

    public SmartFarmAction Decide(SmartFarmView view)
    {
        LastReason = "";
        if (view.Stage <= 0 || view.Dps <= 0 || double.IsNaN(view.WaveHp) || view.WaveHp < 0)
        {
            LastReason = "battle numbers unreadable";
            return SmartFarmAction.None;
        }

        if (view.BossFight)
        {
            var bossHp = Remember(_bossHp, view.Stage, view.WaveHp);
            var seconds = Seconds(bossHp, view.Dps);
            if (seconds * Margin > BossSeconds)
            {
                LastReason = $"boss at stage {view.Stage} needs {seconds:0.#}s (+{MarginPercent:0}% margin) of {BossSeconds:0}s";
                return SmartFarmAction.LeaveBoss;
            }

            LastReason = $"boss at stage {view.Stage} needs {seconds:0.#}s";
            return SmartFarmAction.None;
        }

        if (view.Farming)
        {
            _waveStage = -1;
            if (_bossHp.TryGetValue(view.Stage, out var bossHp) && Seconds(bossHp, view.Dps) * Margin > BossSeconds)
            {
                LastReason = $"farming: boss at stage {view.Stage} needs {Seconds(bossHp, view.Dps):0.#}s";
                return SmartFarmAction.None;
            }

            if (_slowWaveHp.TryGetValue(view.Stage + 1, out var slowHp) &&
                Seconds(slowHp, view.Dps) * Margin > MaxWaveSeconds)
            {
                LastReason = $"farming: stage {view.Stage + 1} waves need {Seconds(slowHp, view.Dps):0.#}s";
                return SmartFarmAction.None;
            }

            LastReason = _bossHp.ContainsKey(view.Stage)
                ? $"boss at stage {view.Stage} is within reach"
                : $"boss at stage {view.Stage} not seen yet; trying it";
            return SmartFarmAction.FightBoss;
        }

        // A normal wave, while the game moves forward on its own.
        if (_waveStage != view.Stage)
        {
            _waveStage = view.Stage;
            _waveHp = 0;
        }

        _waveHp = Math.Max(_waveHp, view.WaveHp);
        var waveSeconds = Seconds(_waveHp, view.Dps);
        if (waveSeconds > MaxWaveSeconds)
        {
            Remember(_slowWaveHp, view.Stage, _waveHp);
            LastReason = $"stage {view.Stage} waves need {waveSeconds:0.#}s of {MaxWaveSeconds:0.#}s";
            _waveStage = -1;
            return SmartFarmAction.GoBack;
        }

        LastReason = $"stage {view.Stage} waves need {waveSeconds:0.#}s";
        return SmartFarmAction.None;
    }

    private static double Remember(Dictionary<int, double> map, int stage, double hp)
    {
        var known = map.TryGetValue(stage, out var old) ? Math.Max(old, hp) : hp;
        map[stage] = known;
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
