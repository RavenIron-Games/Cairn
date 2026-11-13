using System;
using System.Collections.Generic;

namespace RavenIron.Cairn.Core
{
    /// <summary>
    /// PURE. A cairn takes the colour of what it holds (0.13.0): an item stand on or beside the
    /// stones with a surtling core on it burns the cairn red, a greydwarf eye blue.
    ///
    /// The list is config (`HeldColours`), read on the SERVER, which decides each cairn's colour
    /// once and sends it with the light, so every player sees the same colour. Entries are
    /// `ItemPrefab=RRGGBB` or `ItemPrefab=RRGGBBAA`, comma-separated; `:` works in place of `=`.
    /// An item stand records what it holds as the hash of the item's prefab name, so these are
    /// PREFAB names (`SurtlingCore`), not the names shown in game ("Surtling core").
    ///
    /// A colour is stored and sent as eight upper-case hex digits, RRGGBBAA, or "" for none.
    /// </summary>
    public static class HeldColour
    {
        /// <summary>
        /// Read the config list. Returns item prefab name → RRGGBBAA. Entries that cannot be read
        /// are listed in <paramref name="rejected"/> so the caller can say so in the log; a
        /// repeated name keeps its LAST colour, as a person editing the list would expect.
        /// </summary>
        public static Dictionary<string, string> Parse(string csv, List<string> rejected = null)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(csv)) return map;

            foreach (string raw in csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string entry = raw.Trim();
                if (entry.Length == 0) continue;

                int sep = entry.IndexOf('=');
                if (sep < 0) sep = entry.IndexOf(':');
                if (sep <= 0 || sep == entry.Length - 1) { rejected?.Add(entry); continue; }

                string item = entry.Substring(0, sep).Trim();
                string hex = Normalise(entry.Substring(sep + 1));
                if (item.Length == 0 || hex.Length == 0) { rejected?.Add(entry); continue; }

                map[item] = hex;
            }
            return map;
        }

        /// <summary>
        /// `RRGGBB` or `RRGGBBAA`, '#' optional, any case → `RRGGBBAA` upper-case. Anything else
        /// → "". Shorthand (`F80`) is refused here on purpose: in a list of item colours it is far
        /// more likely a typo than a choice, and a wrong colour on every cairn is a quiet failure.
        /// </summary>
        public static string Normalise(string hex)
        {
            if (hex == null) return "";
            hex = hex.Trim();
            if (hex.StartsWith("#")) hex = hex.Substring(1);
            if (hex.Length != 6 && hex.Length != 8) return "";

            foreach (char c in hex)
            {
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!ok) return "";
            }

            hex = hex.ToUpperInvariant();
            return hex.Length == 6 ? hex + "FF" : hex;
        }
    }
}
