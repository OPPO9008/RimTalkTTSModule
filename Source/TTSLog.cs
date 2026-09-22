using Verse;

namespace RimTalk.TTS
{
    /// <summary>
    /// Centralised logging wrapper for RimTalk.TTS.
    /// Message / Warning are only emitted when the player has "Log verbose"
    /// enabled in RimWorld's developer settings so they never clutter a normal
    /// playthrough.  Error is always emitted so genuine failures are visible.
    /// </summary>
    internal static class TTSLog
    {
        /// <summary>Emits an informational log entry. Visible only in verbose-log mode.</summary>
        public static void Message(string message)
        {
            if (Prefs.LogVerbose)
                Log.Message(message);
        }

        /// <summary>Emits a warning log entry. Visible only in verbose-log mode.</summary>
        public static void Warning(string message)
        {
            Log.Warning(message);
        }

        /// <summary>Emits an error log entry. Always visible.</summary>
        public static void Error(string message)
        {
            Log.Error(message);
        }
    }
}
