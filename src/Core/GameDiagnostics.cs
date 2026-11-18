using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if MONO
using BreakInfinity;
#else
using Il2Cpp;
using Il2CppBreakInfinity;
#endif

namespace Firebot.Core;

/// <summary>
///     Debug Mode only: once a minute, logs battle and temple values read straight from game data, so they can
///     be compared with what the screen shows before features are built on them.
/// </summary>
internal static class GameDiagnostics
{
    private const int MaxEnemiesListed = 6;
    private static DateTime _next;
    private static readonly HashSet<string> Failed = new();

    public static void Tick()
    {
        if (!BotSettings.DebugMode) return;
        Sample();
        if (DateTime.Now < _next) return;
        _next = DateTime.Now.AddMinutes(1);
        Log("battle", Battle);
        Log("temple", Temple);
        Log("firestones", FirestonesData);
        Log("enemies", Enemies);
        Log("combat", Combat);
    }

    private static void Log(string part, Func<string> read)
    {
        try
        {
            Logger.Info($"[Diag] {read()}");
        }
        catch (Exception e)
        {
            if (Failed.Add($"{part}|{e.GetType().Name}"))
                Logger.Warning($"[Diag] {part} read failed: {e.GetType().Name} - {e.Message}");
        }
    }

    // --- Combat: what CombatProbe sees, next to what the screen prints ----------------------------------------
    private static bool _subscribed;
    private static readonly List<float> WaveSeconds = new();
    private static readonly List<string> BossFights = new();
    private static string _waveType = "?";
    private static double _waveHp;
    private static int _waveStage;
    private const string ScreenDps = "battleRoot/battleMain/battleCanvas/SafeArea/bottomLeftSideUI/dps/dpsText";
    private const string ScreenWaveHp =
        "battleRoot/battleMain/battleCanvas/SafeArea/topSideUI/stageProgression/stageProgressBarBg/waveEnemyTotalHp";

    private static void Sample()
    {
        try
        {
            if (!_subscribed)
            {
                _subscribed = true;
                CombatProbe.WaveStarted += OnWaveStarted;
            }
            CombatProbe.Tick();
        }
        catch (Exception e)
        {
            if (Failed.Add($"sample|{e.GetType().Name}"))
                Logger.Warning($"[Diag] combat sample failed: {e.GetType().Name} - {e.Message}");
        }
    }

    private static void OnWaveStarted(float lasted)
    {
        PlayerStats.TryGetCurrentStage(out var stage);
        if (lasted >= 0)
        {
            WaveSeconds.Add(lasted);
            // The wave after a boss: a higher stage means it was beaten, otherwise it was lost or left.
            if (_waveType.Contains("Boss"))
                BossFights.Add($"{_waveType}@{_waveStage} {(stage > _waveStage ? "won" : "not won")} in {lasted:0.0}s (hp {_waveHp:0.###e0})");
        }
        _waveType = CombatProbe.WaveType;
        _waveHp = CombatProbe.WaveHp;
        _waveStage = stage;
    }

    private static string Combat()
    {
        var dps = CombatProbe.Dps;
        var waves = WaveSeconds.Count;
        var avg = waves > 0 ? WaveSeconds.Average() : 0;
        var hp = CombatProbe.WaveHp;
        var line = $"combat: dps {dps:0.###e0} (15s) | waves cleared {waves}, avg {avg:0.0}s | " +
                   $"wave {CombatProbe.WaveType} hp {hp:0.###e0} (hp/dps {(dps > 0 ? hp / dps : double.PositiveInfinity):0.0}s)" +
                   $" | screen '{Firebot.Behaviors.Extra.Ui.Text(ScreenDps)}', '{Firebot.Behaviors.Extra.Ui.Text(ScreenWaveHp)}'";
        if (BossFights.Count > 0) line += $" | boss fights: {string.Join("; ", BossFights)}";
        WaveSeconds.Clear();
        BossFights.Clear();
        return line;
    }

    private static string Battle()
    {
        var b = GameInitialize.HandlerLoader?.MainBattle;
        if (b == null) return "battle: unavailable";
        return $"battle: stage {b.CurrentStageNumber} wave {b.CurrentStageWave} | max stage {b.MaxStageNumber} " +
               $"wave {b.MaxStageWave} | max stage this prestige {b.MaxStageNumberOnPrestige} | farming wave {b.FarmingWave}";
    }

    private static string Temple()
    {
        var m = GameInitialize.MenuLoader?.templeOfEternalsMech;
        if (m == null) return "temple: not loaded";
        return $"temple: prestigeRainingGold {Big(m.prestigeRainingGold)} | prestigeFirestones {Big(m.prestigeFirestones)} | " +
               $"epicPrestigeRainingGold {Big(m.epicPrestigeRainingGold)} | epicPrestigeFirestones {Big(m.epicPrestigeFirestones)}";
    }

    private static string FirestonesData()
    {
        var f = GameInitialize.HandlerLoader?.Firestones;
        if (f == null) return "firestones: unavailable";
        return $"firestones: earnedThisRun {Big(f.firestonesEarnedThisRun)} | increaseInFirestones {Big(Firestones.increaseInFirestones)} (static) | " +
               $"increaseInMainGameGold {Big(Firestones.increaseInMainGameGold)} (static) | effectTotal {Big(f.firestoneEffectTotal)} | " +
               $"totalGoldEarnings {Big(f.firestoneTotalGoldEarnings)} | bonusMultiplier {f.firestonesBonusMultiplier}";
    }

    private static string Enemies()
    {
        var enemies = UnityEngine.Object.FindObjectsOfType<EnemyBattleLogic>()
            .Where(e => e != null && e.gameObject.activeInHierarchy && e.battleCharacter != null).ToList();
        if (enemies.Count == 0) return "enemies: none active";
        var listed = enemies.Take(MaxEnemiesListed).Select(e =>
            $"{e.name} {Big(e.battleCharacter.currentHealth)}/{Big(e.battleCharacter.maximumHealthTotal)}");
        return $"enemies ({enemies.Count}): {string.Join(" | ", listed)}";
    }

    // The game keeps these numbers in anti-cheat ObscuredBigDouble, whose ToString shows only the type name.
    private static string Big(BigDouble value) => value.ToString();
}
