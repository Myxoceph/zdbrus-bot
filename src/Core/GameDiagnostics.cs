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

    // --- Combat probe: damage per second and wave clear times, from enemy health -----------------------------
    // Health is read as double: plenty up to ~1e308, and this is only a diagnostic.
    private const float SampleSeconds = 0.5f;
    private static float _nextSample;
    private static Dictionary<int, double> _lastHp = new();
    private static double _lastTotalHp;
    private static double _damage;
    private static float _windowStart = -1f;
    private static float _waveStart = -1f;
    private static double _lastWaveHp;
    private static readonly List<float> WaveSeconds = new();
    private static string _lastWaveType = "?";
    private static double _lastBossHp;
    private static float _bossStart = -1f;
    private static int _bossStage;
    private static readonly List<string> BossFights = new();
    private const string ScreenDps = "battleRoot/battleMain/battleCanvas/SafeArea/bottomLeftSideUI/dps/dpsText";
    private const string ScreenWaveHp =
        "battleRoot/battleMain/battleCanvas/SafeArea/topSideUI/stageProgression/stageProgressBarBg/waveEnemyTotalHp";

    private static void Sample()
    {
        var now = Time.realtimeSinceStartup;
        if (now < _nextSample) return;
        _nextSample = now + SampleSeconds;
        try
        {
            var current = new Dictionary<int, double>();
            double totalHp = 0, totalMax = 0;
            EnemyBattleLogic any = null;
            foreach (var e in UnityEngine.Object.FindObjectsOfType<EnemyBattleLogic>())
            {
                if (e == null || !e.gameObject.activeInHierarchy || e.battleCharacter == null) continue;
                var hp = ((BigDouble)e.battleCharacter.currentHealth).ToDouble();
                if (hp <= 0) continue;
                current[e.GetInstanceID()] = hp;
                totalHp += hp;
                totalMax += ((BigDouble)e.battleCharacter.maximumHealthTotal).ToDouble();
                any ??= e;
            }

            if (_windowStart < 0) _windowStart = now;
            foreach (var kv in _lastHp)
            {
                if (current.TryGetValue(kv.Key, out var hp)) { if (kv.Value > hp) _damage += kv.Value - hp; }
                else _damage += kv.Value; // died (or despawned) with this much health left
            }

            // A wave starts when the health on screen jumps up: enemies (re)spawned, pooled or not.
            if (current.Count > 0 && totalHp > _lastTotalHp + 0.5 * totalMax)
            {
                if (_waveStart >= 0) WaveSeconds.Add(now - _waveStart);
                PlayerStats.TryGetCurrentStage(out var stage);
                if (_bossStart >= 0)
                {
                    // The wave after a boss: a higher stage means it was beaten, otherwise it was lost.
                    BossFights.Add($"{_lastWaveType}@{_bossStage} {(stage > _bossStage ? "won" : "lost")} in {now - _bossStart:0.0}s " +
                                   $"(hp {_lastBossHp:0.###e0})");
                    _bossStart = -1f;
                }
                _waveStart = now;
                _lastWaveHp = totalMax;
                var sm = any?.battleController?.stageManager;
                _lastWaveType = sm != null ? sm.GetCurrentWaveType().ToString() : "?";
                if (_lastWaveType.Contains("Boss"))
                {
                    _lastBossHp = totalMax;
                    _bossStart = now;
                    _bossStage = stage;
                }
            }

            _lastHp = current;
            _lastTotalHp = totalHp;
        }
        catch (Exception e)
        {
            if (Failed.Add($"sample|{e.GetType().Name}"))
                Logger.Warning($"[Diag] combat sample failed: {e.GetType().Name} - {e.Message}");
        }
    }

    private static string Combat()
    {
        var now = Time.realtimeSinceStartup;
        var seconds = _windowStart < 0 ? 0 : now - _windowStart;
        var dps = seconds > 0 ? _damage / seconds : 0;
        var waves = WaveSeconds.Count;
        var avgWave = waves > 0 ? WaveSeconds.Average() : 0;
        var predicted = dps > 0 ? _lastWaveHp / dps : double.PositiveInfinity;
        var line = $"combat: dps {dps:0.###e0} over {seconds:0}s | waves cleared {waves}, avg {avgWave:0.0}s | " +
                   $"last wave {_lastWaveType} hp {_lastWaveHp:0.###e0} (hp/dps {predicted:0.0}s) | " +
                   $"last boss hp {_lastBossHp:0.###e0} (hp/dps {(dps > 0 ? _lastBossHp / dps : 0):0.0}s)";
        line += $" | screen '{Firebot.Behaviors.Extra.Ui.Text(ScreenDps)}', '{Firebot.Behaviors.Extra.Ui.Text(ScreenWaveHp)}'";
        if (BossFights.Count > 0) line += $" | boss fights: {string.Join("; ", BossFights)}";
        BossFights.Clear();
        _damage = 0;
        _windowStart = now;
        WaveSeconds.Clear();
        return line;
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
