using System;

namespace ForesTycoon.Ecology
{
    /// <summary>Bounded stock operations shared by sequential process equations.</summary>
    internal static class StockFlows
    {
        internal static double Withdraw(ref double stock, double requested)
        {
            Validate(stock, requested);
            double amount = Math.Min(stock, requested);
            stock -= amount;
            return amount;
        }

        internal static double Transfer(ref double source, ref double destination, double requested)
        {
            Validate(destination, 0);
            Validate(source, requested);
            double amount = Math.Min(source, requested);
            if (!double.IsFinite(destination + amount)) throw new ArgumentOutOfRangeException(nameof(destination));
            source -= amount;
            destination += amount;
            return amount;
        }

        private static void Validate(double stock, double requested)
        {
            if (!double.IsFinite(stock) || stock < 0) throw new ArgumentOutOfRangeException(nameof(stock));
            if (!double.IsFinite(requested) || requested < 0) throw new ArgumentOutOfRangeException(nameof(requested));
        }
    }
}
