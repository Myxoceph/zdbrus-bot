using System;
using System.Collections;
using Firebot.Core;
using Firebot.Core.Tasks;
using MelonLoader;
using Logger = Firebot.Core.Logger;

namespace Firebot.Behaviors.Extra;

/// <summary>
///     Farms where the team's damage clears waves quickly instead of pushing as far as it can, using only the
///     battle screen: the DPS text, the stage bar's enemy health, and the game's own back, Fight boss and leave
///     boss buttons. The decisions live in <see cref="SmartFarmPolicy" />. While this runs, Boss Retry stands down.
/// </summary>
public class SmartFarmTask : BotTask
{
    private const string Progress = P.Battle + "/topSideUI/stageProgression";
    private const string GoBack = Progress + "/goBackStage";
    private const string WaveHp = Progress + "/stageProgressBarBg/waveEnemyTotalHp";
    private const string BossTimer = Progress + "/bossTimerProgressBarBg";
    private const string LeaveBossTop = Progress + "/leaveBossButton";
    private const string LeaveBossLeft = P.Battle + "/leftSideUI/leaveBossButton";
    private const string FightBoss = P.Battle + "/rightSideUI/fightBossButton";
    private const string DpsText = P.Battle + "/bottomLeftSideUI/dps/dpsText";

    private static readonly SmartFarmPolicy Policy = new();
    private static SmartFarmTask _instance;
    private MelonPreferences_Entry<int> _maxWaveSeconds;
    private MelonPreferences_Entry<int> _marginPercent;
    private MelonPreferences_Entry<int> _dpsFactorPercent;
    private string _lastLogged = "";
    private DateTime _nextStatusLog;

    /// <summary>Boss Retry checks this and stays idle while Smart Farm decides when to fight.</summary>
    internal static bool Active => _instance != null && _instance.IsEnabled;

    protected override void OnConfigure(MelonPreferences_Category c)
    {
        _instance = this;
        _maxWaveSeconds = c.CreateEntry("max_wave_seconds", 10, "Max Wave Seconds",
            "Go back one stage when a normal wave would take longer than this to clear (wave health / DPS).");
        _marginPercent = c.CreateEntry("boss_margin_percent", 20, "Boss Safety Margin %",
            "Fight a boss only when it would die this much faster than the 60-second timer allows.");
        _dpsFactorPercent = c.CreateEntry("dps_factor_percent", 100, "Screen DPS Factor %",
            "Scales the DPS shown on screen before every estimate. Lower it if bosses and waves turn out slower than predicted.");
    }

    public override IEnumerator Execute()
    {
        NextRunTime = DateTime.Now.AddSeconds(2);
        if (!Ui.Visible(Progress) || !Ui.Visible(DpsText)) yield break;

        var dpsRead = SmartFarmPolicy.TryParseScreenNumber(Ui.Text(DpsText), out var screenDps);
        var hpRead = SmartFarmPolicy.TryParseScreenNumber(Ui.Text(WaveHp), out var waveHp);
        if (!dpsRead || !hpRead || !PlayerStats.TryGetCurrentStage(out var stage))
        {
            LogOnce($"[Smart Farm] Battle numbers unreadable (DPS '{Ui.Text(DpsText)}', health '{Ui.Text(WaveHp)}'); waiting.");
            NextRunTime = DateTime.Now.AddSeconds(10);
            yield break;
        }

        Policy.MaxWaveSeconds = Math.Max(1, _maxWaveSeconds.Value);
        Policy.MarginPercent = Math.Max(0, _marginPercent.Value);
        var dps = screenDps * Math.Max(1, _dpsFactorPercent.Value) / 100.0;
        var bossFight = Ui.Visible(BossTimer) || Ui.Clickable(LeaveBossTop) || Ui.Clickable(LeaveBossLeft);
        var farming = !bossFight && Ui.Clickable(FightBoss);

        var action = Policy.Decide(new SmartFarmView(stage, dps, waveHp, bossFight, farming));
        switch (action)
        {
            case SmartFarmAction.GoBack:
                Logger.Info($"[Smart Farm] Going back: {Policy.LastReason}.");
                yield return Ui.Click(GoBack, 1f, "go back one stage");
                NextRunTime = DateTime.Now.AddSeconds(5);
                break;
            case SmartFarmAction.LeaveBoss:
                Logger.Info($"[Smart Farm] Leaving the boss: {Policy.LastReason}.");
                yield return Ui.Click(Ui.Clickable(LeaveBossTop) ? LeaveBossTop : LeaveBossLeft, 1f, "leave boss");
                NextRunTime = DateTime.Now.AddSeconds(5);
                break;
            case SmartFarmAction.FightBoss:
                Logger.Info($"[Smart Farm] Fighting the boss: {Policy.LastReason}.");
                yield return Ui.Click(FightBoss, 1f, $"fight boss at stage {stage}");
                NextRunTime = DateTime.Now.AddSeconds(5);
                break;
            default:
                if (DateTime.Now >= _nextStatusLog)
                {
                    _nextStatusLog = DateTime.Now.AddSeconds(30);
                    Logger.Debug($"[Smart Farm] {Policy.LastReason}.");
                }
                break;
        }
    }

    private void LogOnce(string message)
    {
        if (message == _lastLogged) return;
        _lastLogged = message;
        Logger.Warning(message);
    }
}
