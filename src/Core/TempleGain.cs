using System;
#if MONO
using BreakInfinity;
#else
using Il2Cpp;
using Il2CppBreakInfinity;
#endif

namespace Firebot.Core;

/// <summary>
///     The +% a free empower would add to gold earnings right now, read from game data without opening the
///     Temple: firestones earned this run divided by the Temple's firestones. The Temple menu caches this only
///     while it is open; the Firestones handler keeps it current.
/// </summary>
internal static class TempleGain
{
    public static bool TryReadPercent(out double percent)
    {
        percent = 0;
        try
        {
            if (!GameInitialize.HasGameLoaded()) return false;
            var f = GameInitialize.HandlerLoader?.Firestones;
            if (f == null) return false;

            BigDouble earned = f.firestonesEarnedThisRun;
            BigDouble effect = f.firestoneEffectTotal;
            BigDouble goldEarnings = f.firestoneTotalGoldEarnings;
            // goldEarnings = temple firestones x effect, so earned / temple = earned x effect / goldEarnings.
            if (goldEarnings.ToDouble() <= 0 || effect.ToDouble() <= 0) return false;
            var ratio = (earned * effect / goldEarnings).ToDouble();
            if (double.IsNaN(ratio) || double.IsInfinity(ratio) || ratio < 0) return false;
            percent = ratio * 100;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
