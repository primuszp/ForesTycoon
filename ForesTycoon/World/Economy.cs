namespace ForesTycoon
{
    /// <summary>
    /// Money feeding back into the world: new investment needs funds within the credit line, and the sale price follows a
    /// tunable (formula-driven) market factor. With the default unlimited credit and factor 1 the game plays as before.
    /// </summary>
    internal static class Economy
    {
        internal const double UnlimitedCredit = 1e9;
        /// <summary>Building and repairs start only while the balance has not passed the negative credit line.</summary>
        internal static bool CanInvest(double balance, double creditLimit) => balance >= -creditLimit;
        internal const string NoFunds = "Nincs fedezet: az egyenleg elérte a hitelkeretet. Előbb adj el faanyagot.";
    }
}
