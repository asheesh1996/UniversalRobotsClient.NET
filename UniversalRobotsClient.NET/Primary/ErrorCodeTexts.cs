using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using UniversalRobotics.Models;

namespace UniversalRobotics.Primary
{
    /// <summary>Human-readable UR error descriptions imported from the upstream v42.0.0 catalog.</summary>
    public static class ErrorCodeTexts
    {
        private static readonly Lazy<IReadOnlyDictionary<string, string>> Entries = new Lazy<IReadOnlyDictionary<string, string>>(Load);

        /// <summary>Resolves dynamic overrides, exact code/argument matches, then code-only fallback.</summary>
        public static string? GetText(uint code, uint argument)
        {
            if (code == 100)
            {
                int mode = unchecked((int)argument);
                return mode >= -1 && mode <= 8
                    ? "Robot mode changed to: " + ((RobotMode)mode).ToString()
                    : "Unknown robot mode: " + mode.ToString(CultureInfo.InvariantCulture);
            }
            ulong key = ((ulong)code << 32) | argument;
            if (Entries.Value.TryGetValue(key.ToString("X16", CultureInfo.InvariantCulture), out var exact)) return exact;
            key = ((ulong)code << 32) | uint.MaxValue;
            return Entries.Value.TryGetValue(key.ToString("X16", CultureInfo.InvariantCulture), out var fallback) ? fallback : null;
        }

        private static IReadOnlyDictionary<string, string> Load()
        {
            using var stream = typeof(ErrorCodeTexts).Assembly.GetManifestResourceStream("UniversalRobotics.error-code-texts.json")
                ?? throw new InvalidOperationException("Embedded error catalog is missing.");
            using var document = JsonDocument.Parse(stream);
            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.GetProperty("entries").EnumerateObject())
                entries.Add(property.Name, property.Value.GetString()!);
            return entries;
        }
    }
}