using System;
using System.Collections.Generic;
using UnityEngine;

namespace PriceSlinger
{
    /// <summary>
    /// Provides throttled and debug-conditional logging utilities
    /// to avoid log spam during bulk pricing operations.
    /// </summary>
    internal static class LogHelper
    {
        /// <summary>
        /// Tracks the next allowed log time (in
        /// <see cref="Time.realtimeSinceStartup"/>) for each throttle key.
        /// </summary>
        private static readonly Dictionary<string, float> ThrottleCooldowns =
            new Dictionary<string, float>();

        /// <summary>
        /// Logs an informational message only when debug logging is enabled
        /// in config.
        /// </summary>
        /// <param name="message">The message to log.</param>
        internal static void LogDebug(string message)
        {
            if (Plugin.DebugLogging == null || !Plugin.DebugLogging.Value)
            {
                return;
            }

            if (Plugin.Log != null)
            {
                Plugin.Log.LogInfo("[PriceSlinger] " + message);
            }
        }

        /// <summary>
        /// Logs a warning message, but only once per
        /// <paramref name="cooldownSeconds"/> for a given
        /// <paramref name="throttleKey"/>. Prevents log spam when many items
        /// or cards fail in rapid succession.
        /// </summary>
        /// <param name="throttleKey">A unique key identifying this warning
        /// source.</param>
        /// <param name="message">The warning message to log.</param>
        /// <param name="cooldownSeconds">Minimum seconds between repeated logs
        /// for the same key.</param>
        internal static void LogWarnThrottled(string throttleKey, string message,
                                              float cooldownSeconds = 15f)
        {
            if (Plugin.Log == null)
            {
                return;
            }

            try
            {
                float now = Time.realtimeSinceStartup;
                if (ThrottleCooldowns.TryGetValue(throttleKey, out float nextAllowed)
                    && now < nextAllowed)
                {
                    return;
                }

                ThrottleCooldowns[throttleKey] = now + cooldownSeconds;
                Plugin.Log.LogWarning("[PriceSlinger] " + message);
            }
            catch (Exception)
            {
                // Logging itself should never cause a crash; silently discard.
            }
        }
    }
}