using System;
using System.Linq;
using System.Security.Cryptography;

namespace BeaverBuddies.WebRtc
{
    /**
     * The code others join a hosted game by over WebRTC, new for each game
     * hosted. Six letters and digits, leaving out the ones that read alike
     * (0 and O, 1 and I), shown as ABC-123.
     */
    public static class JoinCode
    {
        private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        // Typed in the join box as rtc:CODE, or as the code alone
        public const string Prefix = "rtc:";

        public static string Current { get; private set; }

        public static string New()
        {
            // Test copies agree on a code ahead of time (see DevTools.TestHarness)
            string forced = Environment.GetEnvironmentVariable("BB_RTC_CODE");
            if (!string.IsNullOrEmpty(forced)) return Current = forced.ToUpperInvariant();
            byte[] bytes = new byte[6];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return Current = new string(bytes.Select(b => Alphabet[b % Alphabet.Length]).ToArray());
        }

        public static void Clear() => Current = null;

        public static string Display(string code) => code.Length == 6 ? code.Substring(0, 3) + "-" + code.Substring(3) : code;

        /**
         * The code in what a player typed to join: rtc:CODE, or a code as the
         * game shows it, with its dash (ABC-234). Six letters alone could be
         * a host's name on the local network.
         */
        public static bool TryParse(string address, out string code)
        {
            code = null;
            if (string.IsNullOrWhiteSpace(address)) return false;
            string text = address.Trim().ToUpperInvariant();
            if (text.StartsWith(Prefix.ToUpperInvariant()))
            {
                text = text.Substring(Prefix.Length).Replace("-", "");
                if (text.Length < 4 || text.Length > 12 || !text.All(char.IsLetterOrDigit)) return false;
            }
            else
            {
                if (text.Length != 7 || text[3] != '-') return false;
                text = text.Remove(3, 1);
                if (!text.All(c => Alphabet.IndexOf(c) >= 0)) return false;
            }
            code = text;
            return true;
        }
    }
}
