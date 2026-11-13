using System;

namespace RavenIron.Cairn.Core
{
    /// <summary>
    /// PURE. The colour a cairn's builder wrote into its sign, if any (0.12.0).
    ///
    /// Valheim's signs already understand rich text, so `&lt;color=#4A9EFF&gt;Home&lt;/color&gt;`
    /// shows the word in blue on the sign itself. Cairn reads the FIRST colour tag in the name
    /// and lights that cairn the same colour, for everyone, because the builder chose it. The
    /// name is stored exactly as typed and already reaches every player's game with the beacon,
    /// so this needs no packet change and no server update.
    ///
    /// Accepted: `#RGB`, `#RGBA`, `#RRGGBB`, `#RRGGBBAA`, with or without quotes, and the colour
    /// names TextMeshPro's own tag accepts (red, lightblue, blue, grey, black, green, white,
    /// orange, purple, yellow). Anything else is no colour, and the cairn keeps the player's
    /// own `BeaconColour`. Each player can ignore sign colours with `UseSignColours = false`.
    /// </summary>
    public static class SignColour
    {
        public static bool TryRead(string name, out float r, out float g, out float b, out float a)
        {
            r = g = b = 0f; a = 1f;
            if (string.IsNullOrEmpty(name)) return false;

            int at = name.IndexOf("<color", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return false;

            int eq = at + "<color".Length;
            while (eq < name.Length && name[eq] == ' ') eq++;
            if (eq >= name.Length || name[eq] != '=') return false;

            int close = name.IndexOf('>', eq);
            if (close < 0) return false;

            string value = name.Substring(eq + 1, close - eq - 1).Trim().Trim('"', '\'').Trim();
            if (value.Length == 0) return false;

            if (value[0] == '#') return TryHex(value.Substring(1), out r, out g, out b, out a);
            return TryNamed(value, out r, out g, out b);
        }

        private static bool TryHex(string hex, out float r, out float g, out float b, out float a)
        {
            r = g = b = 0f; a = 1f;
            int n = hex.Length;
            if (n != 3 && n != 4 && n != 6 && n != 8) return false;

            var v = new int[n];
            for (int i = 0; i < n; i++)
            {
                int d = Digit(hex[i]);
                if (d < 0) return false;
                v[i] = d;
            }

            if (n <= 4)
            {
                // Shorthand: each digit doubles, so F80 is FF8800.
                r = v[0] * 17 / 255f; g = v[1] * 17 / 255f; b = v[2] * 17 / 255f;
                a = n == 4 ? v[3] * 17 / 255f : 1f;
            }
            else
            {
                r = (v[0] * 16 + v[1]) / 255f; g = (v[2] * 16 + v[3]) / 255f; b = (v[4] * 16 + v[5]) / 255f;
                a = n == 8 ? (v[6] * 16 + v[7]) / 255f : 1f;
            }
            return true;
        }

        private static int Digit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        private static bool TryNamed(string name, out float r, out float g, out float b)
        {
            r = g = b = 0f;
            switch (name.ToLowerInvariant())
            {
                case "red":       r = 1f;     g = 0f;     b = 0f;     return true;
                case "lightblue": r = 0.68f;  g = 0.85f;  b = 0.90f;  return true;
                case "blue":      r = 0f;     g = 0f;     b = 1f;     return true;
                case "grey":      r = 0.5f;   g = 0.5f;   b = 0.5f;   return true;
                case "black":     r = 0f;     g = 0f;     b = 0f;     return true;
                case "green":     r = 0f;     g = 1f;     b = 0f;     return true;
                case "white":     r = 1f;     g = 1f;     b = 1f;     return true;
                case "orange":    r = 1f;     g = 0.5f;   b = 0f;     return true;
                case "purple":    r = 0.63f;  g = 0.13f;  b = 0.94f;  return true;
                case "yellow":    r = 1f;     g = 1f;     b = 0f;     return true;
                default: return false;
            }
        }
    }
}
