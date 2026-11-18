using System;
using System.Collections.Generic;
using UnityEngine;
#if MONO
using BreakInfinity;
#else
using Il2Cpp;
using Il2CppBreakInfinity;
#endif

namespace Firebot.Core;

/// <summary>
///     Watches enemy health on the battlefield: the damage the team really deals per second (health lost,
///     plus what is left on enemies that die) and the full health of the current wave. Health is read as
///     double, which holds values up to ~1e308.
/// </summary>
internal static class CombatProbe
{
    private const float SampleSeconds = 0.25f;
    private const float WindowSeconds = 15f;

    private static float _nextSample;
    private static Dictionary<int, double> _lastHp = new();
    private static double _lastTotalHp;
    private static readonly Queue<(float At, double Damage)> Hits = new();
    private static double _windowDamage;
    private static float _firstSample = -1f;

    /// <summary>Damage per second over the last 15 seconds; 0 until 5 seconds have been watched.</summary>
    public static double Dps
    {
        get
        {
            if (_firstSample < 0) return 0;
            var span = Math.Min(WindowSeconds, Time.realtimeSinceStartup - _firstSample);
            return span >= 5f ? _windowDamage / span : 0;
        }
    }

    /// <summary>Full health of the wave on screen, from the enemies' maximum health.</summary>
    public static double WaveHp { get; private set; }

    public static string WaveType { get; private set; } = "?";
    public static bool IsBossWave => WaveType.Contains("Boss");
    public static float WaveStartedAt { get; private set; } = -1f;

    /// <summary>Raised when a new wave appears, with the seconds the previous one lasted (-1 for the first).</summary>
    public static event Action<float> WaveStarted;

    public static void Tick()
    {
        var now = Time.realtimeSinceStartup;
        if (now < _nextSample) return;
        _nextSample = now + SampleSeconds;

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

        if (_firstSample < 0) _firstSample = now;
        double damage = 0;
        foreach (var kv in _lastHp)
        {
            if (current.TryGetValue(kv.Key, out var hp)) { if (kv.Value > hp) damage += kv.Value - hp; }
            else damage += kv.Value; // died (or despawned) with this much health left
        }

        if (damage > 0)
        {
            Hits.Enqueue((now, damage));
            _windowDamage += damage;
        }

        while (Hits.Count > 0 && Hits.Peek().At < now - WindowSeconds)
            _windowDamage -= Hits.Dequeue().Damage;
        if (Hits.Count == 0) _windowDamage = 0;

        // A wave starts when the health on screen jumps up: enemies (re)spawned, pooled or not.
        if (current.Count > 0 && totalHp > _lastTotalHp + 0.5 * totalMax)
        {
            var lasted = WaveStartedAt >= 0 ? now - WaveStartedAt : -1f;
            WaveStartedAt = now;
            WaveHp = totalMax;
            var sm = any?.battleController?.stageManager;
            WaveType = sm != null ? sm.GetCurrentWaveType().ToString() : "?";
            WaveStarted?.Invoke(lasted);
        }
        else if (current.Count > 0)
        {
            WaveHp = Math.Max(WaveHp, totalMax);
        }

        _lastHp = current;
        _lastTotalHp = totalHp;
    }
}
