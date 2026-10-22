using Microsoft.Extensions.Logging;
using System;
using System.Text.RegularExpressions;

namespace BeaverBuddies.WebRtc
{
    /**
     * SIPSorcery's own log, in the game's: its warnings and errors always, and
     * the rest, which is a lot, with "Always trace" on. Its classes each take a
     * logger once, as they're first used, so this is set as the libraries are
     * (see Libraries.Install), before any connection. Addresses are left out of
     * what it says: players post their logs.
     */
    internal class SipLog : ILoggerFactory, ILogger
    {
        private readonly string category;

        private SipLog(string category)
        {
            this.category = category;
        }

        public static void Install() => SIPSorcery.LogFactory.Set(new SipLog("SIPSorcery"));

        public ILogger CreateLogger(string categoryName) => new SipLog(categoryName.Substring(categoryName.LastIndexOf('.') + 1));

        public void AddProvider(ILoggerProvider provider) { }

        public void Dispose() { }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

        // As loaded too: a host's connections are set up as its game loads (see Settings.AlwaysTraceLoaded)
        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= LogLevel.Warning || (logLevel >= LogLevel.Debug && (Settings.Debug || Settings.AlwaysTraceLoaded));

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            string message = Redact($"[SIPSorcery {category}] {formatter(state, exception)}{(exception == null ? "" : ": " + exception.Message)}");
            if (logLevel >= LogLevel.Warning) Plugin.LogWarning(message);
            else Plugin.Log(message);
        }

        // IPv4 addresses, and IPv6 ones written out or with :: (times, with two colons at most, stay)
        private static readonly Regex Addresses = new Regex(
            @"\b\d{1,3}(?:\.\d{1,3}){3}\b|(?:[0-9a-fA-F]{1,4}:){4,7}[0-9a-fA-F]{1,4}|(?:[0-9a-fA-F]{1,4}(?::[0-9a-fA-F]{1,4})*)?::(?:[0-9a-fA-F]{1,4}(?::[0-9a-fA-F]{1,4})*)?",
            RegexOptions.Compiled);

        internal static string Redact(string text) => Addresses.Replace(text, "<address>");
    }
}
