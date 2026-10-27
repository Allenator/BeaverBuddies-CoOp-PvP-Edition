using BeaverBuddies.Util;

namespace BeaverBuddies.Trading
{
    /** What the shared trade code needs from this mod. */
    public static class TradeText
    {
        // Outranks Archipelago's copy: with both mods on, the Trades tab shows its trade posts too
        public const int Rank = 2;

        public static string T(string key) => RegisteredLocalizationService.T(key);

        public static string T(string key, object arg) => string.Format(T(key), arg);

        public static string T(string key, object arg1, object arg2) => string.Format(T(key), arg1, arg2);
    }
}
