using System;
using System.Collections;
using Firebot.Core;
using Firebot.Core.Tasks;
using MelonLoader;
using Logger = Firebot.Core.Logger;

namespace Firebot.Behaviors.Extra;

/// <summary>
///     Settings and panel section for Smart Farm. The work runs in <see cref="SmartFarm" /> once a second,
///     outside the task scheduler and independent of F7, so a boss can be left as soon as it appears.
/// </summary>
public class SmartFarmTask : BotTask
{
    private static SmartFarmTask _instance;
    private MelonPreferences_Entry<string> _dpsSource;
    private MelonPreferences_Entry<int> _maxWaveSeconds;
    private MelonPreferences_Entry<int> _marginPercent;
    private MelonPreferences_Entry<int> _dpsFactorPercent;

    /// <summary>Enabled in F1; Boss Retry checks this and stays idle while Smart Farm decides when to fight.</summary>
    internal static bool Active => _instance != null && _instance.IsEnabled;

    internal static bool UseScreenDps =>
        string.Equals(_instance?._dpsSource?.Value?.Trim(), "screen", StringComparison.OrdinalIgnoreCase);

    internal static int MaxWaveSeconds => Math.Max(1, _instance?._maxWaveSeconds?.Value ?? 10);
    internal static int MarginPercent => Math.Max(0, _instance?._marginPercent?.Value ?? 20);
    internal static int DpsFactorPercent => Math.Max(1, _instance?._dpsFactorPercent?.Value ?? 100);

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _instance = this;
        _dpsSource = c.CreateEntry("dps_source", "measured", "DPS Source",
            "measured: the damage enemies really take (default). screen: the DPS text on screen, scaled by the factor below.");
        _maxWaveSeconds = c.CreateEntry("max_wave_seconds", 10, "Max Wave Seconds",
            "Go back one stage when a normal wave would take longer than this to clear (wave health / DPS).");
        _marginPercent = c.CreateEntry("boss_margin_percent", 20, "Boss Safety Margin %",
            "Fight a boss only when it would die this much faster than the 60-second timer allows.");
        _dpsFactorPercent = c.CreateEntry("dps_factor_percent", 100, "Screen DPS Factor %",
            "With DPS Source set to screen, scales the DPS shown on screen before every estimate.");
    }

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddMinutes(10);
        yield break;
    }
}

/// <summary>
///     Farms where the team's damage clears waves quickly instead of pushing as far as it can. Reads the
///     wave's full health and the team's DPS, and uses only the game's own back, Fight boss and leave boss
///     buttons. The decisions live in <see cref="SmartFarmPolicy" />.
/// </summary>
internal static class SmartFarm
{
    private const string Progress = P.Battle + "/topSideUI/stageProgression";
    private const string GoBack = Progress + "/goBackStage";
    private const string BossTimer = Progress + "/bossTimerProgressBarBg";
    private const string LeaveBossTop = Progress + "/leaveBossButton";
    private const string LeaveBossLeft = P.Battle + "/leftSideUI/leaveBossButton";
    private const string FightBoss = P.Battle + "/rightSideUI/fightBossButton";
    private const string DpsText = P.Battle + "/bottomLeftSideUI/dps/dpsText";

    private static readonly SmartFarmPolicy Policy = new();
    private static float _nextCheck;
    private static bool _busy;
    private static DateTime _nextStatusLog;
    private static string _lastWarning = "";

    public static void Tick()
    {
        // Like AutoUpgrade: runs on its own Enable switch as soon as the game is ready, bot started or not.
        if (!SmartFarmTask.Active) return;
        try
        {
            CombatProbe.Tick();
            var now = UnityEngine.Time.realtimeSinceStartup;
            if (_busy || now < _nextCheck) return;
            _nextCheck = now + 1f;
            Check();
        }
        catch (Exception e)
        {
            Warn($"[Smart Farm] Check failed: {e.GetType().Name} - {e.Message}");
            _nextCheck = UnityEngine.Time.realtimeSinceStartup + 10f;
        }
    }

    private static void Check()
    {
        if (!Ui.Visible(Progress)) return; // a menu covers the battle

        double dps;
        if (SmartFarmTask.UseScreenDps)
        {
            if (!SmartFarmPolicy.TryParseScreenNumber(Ui.Text(DpsText), out var screen))
            {
                Warn($"[Smart Farm] Screen DPS '{Ui.Text(DpsText)}' is unreadable; waiting.");
                return;
            }
            dps = screen * SmartFarmTask.DpsFactorPercent / 100.0;
        }
        else
        {
            dps = CombatProbe.Dps;
        }

        if (dps <= 0 || CombatProbe.WaveHp <= 0 || !PlayerStats.TryGetCurrentStage(out var stage)) return;

        Policy.MaxWaveSeconds = SmartFarmTask.MaxWaveSeconds;
        Policy.MarginPercent = SmartFarmTask.MarginPercent;
        // Right after leaving a boss the probe still says "boss" until the next wave spawns; the Fight boss
        // button only shows outside a boss fight.
        var fightButton = Ui.Clickable(FightBoss);
        var bossFight = (CombatProbe.IsBossWave && !fightButton) || Ui.Visible(BossTimer) ||
                        Ui.Clickable(LeaveBossTop) || Ui.Clickable(LeaveBossLeft);
        var farming = !bossFight && fightButton;

        var action = Policy.Decide(new SmartFarmView(stage, dps, CombatProbe.WaveHp, bossFight, farming,
            CombatProbe.IsBossWave, CombatProbe.WaveElapsed, CombatProbe.RemainingHp));
        switch (action)
        {
            case SmartFarmAction.GoBack:
                Run($"Going back: {Policy.LastReason}.", GoBack, "go back one stage");
                break;
            case SmartFarmAction.LeaveBoss:
                var leave = Ui.Clickable(LeaveBossTop) ? LeaveBossTop : Ui.Clickable(LeaveBossLeft) ? LeaveBossLeft : null;
                if (leave == null)
                {
                    Warn("[Smart Farm] Wants to leave the boss, but no leave button is clickable.");
                    break;
                }
                Run($"Leaving the boss: {Policy.LastReason}.", leave, "leave boss");
                break;
            case SmartFarmAction.FightBoss:
                Run($"Fighting the boss: {Policy.LastReason}.", FightBoss, $"fight boss at stage {stage}");
                break;
            default:
                if (DateTime.Now >= _nextStatusLog)
                {
                    _nextStatusLog = DateTime.Now.AddSeconds(30);
                    Logger.Debug($"[Smart Farm] {Policy.LastReason} (dps {dps:0.##e0}).");
                }
                break;
        }
    }

    private static void Run(string message, string path, string label)
    {
        Logger.Info($"[Smart Farm] {message}");
        _busy = true;
        MelonCoroutines.Start(Click(path, label));
    }

    private static IEnumerator Click(string path, string label)
    {
        try { yield return Ui.Click(path, 1f, label); }
        finally
        {
            _busy = false;
            // Give the game a moment to switch waves before the next decision.
            _nextCheck = UnityEngine.Time.realtimeSinceStartup + 4f;
        }
    }

    private static void Warn(string message)
    {
        if (message == _lastWarning) return;
        _lastWarning = message;
        Logger.Warning(message);
    }
}
