namespace Firebot.Core;

/// <summary>Which opening buttons are safe when the last chest of the last stack must stay for the daily quest.</summary>
internal static class ChestKeep
{
    /// <summary>Empty: keep the stack as it is. Otherwise the buttons to try, all of which leave one behind.</summary>
    public static string[] Options(double quantity)
    {
        if (quantity <= 1) return System.Array.Empty<string>();
        if (quantity > 10) return new[] { "openx10", "openx1" };
        return new[] { "openx1" };
    }
}
