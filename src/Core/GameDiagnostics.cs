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
        if (!BotSettings.DebugMode || DateTime.Now < _next) return;
        _next = DateTime.Now.AddMinutes(1);
        Log("battle", Battle);
        Log("temple", Temple);
        Log("enemies", Enemies);
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
