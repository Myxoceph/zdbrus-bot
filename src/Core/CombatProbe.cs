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
    // Only time with enemies on screen counts: while the next wave walks in nobody takes damage, and counting
    // that made the DPS swing 60x between samples.
    private static readonly Queue<(float At, double Damage, float Active)> Hits = new();
    private static double _windowDamage;
    private static float _windowActive;
    private static float _lastSampleAt = -1f;

    /// <summary>Damage per second of fighting time over the last 15 seconds; 0 until 3 seconds were watched.</summary>
    public static double Dps => _windowActive >= 3f ? _windowDamage / _windowActive : 0;

    /// <summary>Health the enemies on screen have left.</summary>
    public static double RemainingHp { get; private set; }

    /// <summary>Seconds since the current wave appeared.</summary>
    public static double WaveElapsed => WaveStartedAt >= 0 ? Time.realtimeSinceStartup - WaveStartedAt : 0;

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

        var dt = _lastSampleAt >= 0 ? Math.Min(now - _lastSampleAt, 1f) : 0f;
        _lastSampleAt = now;
        double damage = 0;
        foreach (var kv in _lastHp)
        {
            if (current.TryGetValue(kv.Key, out var hp)) { if (kv.Value > hp) damage += kv.Value - hp; }
            else damage += kv.Value; // died (or despawned) with this much health left
        }

        // Fighting time: enemies were on screen at both ends of the interval.
        var active = _lastHp.Count > 0 && current.Count > 0 ? dt : 0f;
        if (damage > 0 || active > 0)
        {
            Hits.Enqueue((now, damage, active));
            _windowDamage += damage;
            _windowActive += active;
        }

        while (Hits.Count > 0 && Hits.Peek().At < now - WindowSeconds)
        {
            var old = Hits.Dequeue();
            _windowDamage -= old.Damage;
            _windowActive -= old.Active;
        }
        if (Hits.Count == 0)
        {
            _windowDamage = 0;
            _windowActive = 0;
        }

        // A wave starts when the health on screen jumps up (enemies spawned, pooled or not) or when the game
        // switches wave type: a boss must never share a wave with the normal enemies before it.
        var sm = any?.battleController?.stageManager;
        var type = sm != null ? sm.GetCurrentWaveType().ToString() : WaveType;
        if (current.Count > 0 && (totalHp > _lastTotalHp + 0.5 * totalMax || type != WaveType))
        {
            var lasted = WaveStartedAt >= 0 ? now - WaveStartedAt : -1f;
            WaveStartedAt = now;
            WaveHp = totalMax;
            WaveType = type;
            WaveStarted?.Invoke(lasted);
        }
        else if (current.Count > 0)
        {
            WaveHp = Math.Max(WaveHp, totalMax);
        }

        RemainingHp = totalHp;

        _lastHp = current;
        _lastTotalHp = totalHp;
    }
}
