using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using RavenIron.Cairn;
using RavenIron.Cairn.Config;
using RavenIron.Cairn.Core;
using UnityEngine;

namespace Cairn.Tests
{
    /// <summary>
    /// Off-game harness for the pure-logic core. No test framework by design — a console
    /// program returning a nonzero exit code is enough, and adds no dependency to keep
    /// current.
    ///
    /// What it is actually for: serialization fails SILENTLY. A name containing a tab, a
    /// comma-decimal locale, or a BOM does not throw — it writes a file that reads back as
    /// plausible, wrong data on someone else's machine, weeks later. That is precisely the
    /// class of bug worth catching without launching the game.
    /// </summary>
    public static class Program
    {
        private static int _passed;
        private static int _failed;
        private static string _tempRoot;

        public static int Main()
        {
            Console.WriteLine("Cairn — core tests\n");

            ModConfig.Bind(new ConfigFile());

            _tempRoot = Path.Combine(Path.GetTempPath(), "cairn-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);

            try
            {
                LandmarkKeyTests();
                NameTests();
                EscapeTests();
                FormatParseTests();
                SignReadingTests();
                DisplayTextTests();
                HexColourTests();
                PileDetectionTests();
                BeaconSelectionTests();
                ConfigLedgerTests();
                StoneStackingTests();
                NamedListingTests();
                StoreTests();
                PersistenceTests();
            }
            finally
            {
                try { Directory.Delete(_tempRoot, recursive: true); } catch { }
            }

            Console.WriteLine($"\n{_passed} passed, {_failed} failed.");
            return _failed == 0 ? 0 : 1;
        }

        // ---- harness -------------------------------------------------------------------

        private static void Check(bool condition, string what)
        {
            if (condition) { _passed++; return; }
            _failed++;
            Console.WriteLine($"  FAIL  {what}");
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            bool ok = EqualityComparer<T>.Default.Equals(expected, actual);
            if (ok) { _passed++; return; }
            _failed++;
            Console.WriteLine($"  FAIL  {what}\n          expected [{expected}]\n          actual   [{actual}]");
        }

        private static void Section(string name) => Console.WriteLine(name);

        // ---- LandmarkKey ---------------------------------------------------------------

        private static void LandmarkKeyTests()
        {
            Section("LandmarkKey");

            var a = LandmarkKey.FromPosition(new Vector3(10.2f, 30.4f, -5.1f));
            Equal(new LandmarkKey(10, 30, -5), a, "rounds to whole metres");

            var b = LandmarkKey.FromPosition(new Vector3(10.4f, 30.4f, -5.4f));
            Check(a == b, "positions within a metre collapse to one landmark");

            var c = LandmarkKey.FromPosition(new Vector3(11.6f, 30.4f, -5.1f));
            Check(a != c, "a metre and a half apart are different landmarks");

            // Banker's rounding would send 0.5 and 1.5 both to even metres, colliding pairs
            // that ought to differ. AwayFromZero keeps the grid uniform.
            Equal(new LandmarkKey(1, 0, 0), LandmarkKey.FromPosition(new Vector3(0.5f, 0f, 0f)),
                  "0.5 rounds away from zero, not to even");
            Equal(new LandmarkKey(2, 0, 0), LandmarkKey.FromPosition(new Vector3(1.5f, 0f, 0f)),
                  "1.5 rounds away from zero, not to even");
            Equal(new LandmarkKey(-1, 0, 0), LandmarkKey.FromPosition(new Vector3(-0.5f, 0f, 0f)),
                  "negative halves round away from zero too");

            var set = new HashSet<LandmarkKey> { new LandmarkKey(1, 2, 3), new LandmarkKey(1, 2, 3) };
            Equal(1, set.Count, "equal keys hash to one slot");

            Check(new LandmarkKey(1, 2, 3) != new LandmarkKey(3, 2, 1),
                  "axes are not interchangeable in the hash");
        }

        // ---- name normalisation --------------------------------------------------------

        private static void NameTests()
        {
            Section("Landmark.NormaliseName");

            Equal("Two Rocks", Landmark.NormaliseName("Two Rocks"), "plain name survives");
            Equal("Two Rocks", Landmark.NormaliseName("  Two Rocks  "), "ends are trimmed");
            Equal("Two Rocks", Landmark.NormaliseName("Two\nRocks"), "newline becomes a space");
            Equal("Two Rocks", Landmark.NormaliseName("Two \t\r\n Rocks"), "whitespace runs collapse");
            Equal("", Landmark.NormaliseName(""), "empty stays empty");
            Equal("", Landmark.NormaliseName(null), "null is not a crash");
            Equal("", Landmark.NormaliseName("   \n\t "), "whitespace only becomes empty");

            string long_ = new string('x', Landmark.MaxNameLength + 40);
            Equal(Landmark.MaxNameLength, Landmark.NormaliseName(long_).Length, "over-long names are bounded");

            // Rich text is storage-faithful on purpose; stripping is a display decision.
            Equal("<color=red>Home</color>", Landmark.NormaliseName("<color=red>Home</color>"),
                  "rich text is preserved, not stripped, at the storage layer");
        }

        // ---- escaping ------------------------------------------------------------------

        private static void EscapeTests()
        {
            Section("escaping");

            RoundTrip("plain");
            RoundTrip("with\ttab");
            RoundTrip("with\nnewline");
            RoundTrip("with\r\ncrlf");
            RoundTrip("with\\backslash");
            RoundTrip("literal \\t that is not a tab");
            RoundTrip("");
            RoundTrip("everything \\ \t \n \r at once");

            Check(Landmark.Escape("a\tb").IndexOf('\t') < 0, "no raw tab survives escaping");
            Check(Landmark.Escape("a\nb").IndexOf('\n') < 0, "no raw newline survives escaping");
            Equal("\\keep", Landmark.Unescape("\\keep"), "an unknown escape keeps both characters");
        }

        private static void RoundTrip(string s)
        {
            Equal(s, Landmark.Unescape(Landmark.Escape(s)), $"escape round trip: [{Show(s)}]");
        }

        private static string Show(string s) =>
            (s ?? "").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");

        // ---- format / parse -------------------------------------------------------------

        private static void FormatParseTests()
        {
            Section("Landmark format/parse");

            var original = new Landmark(new LandmarkKey(-12, 34, 56), "Two Rocks", "host", 111L, 222L);
            Check(Landmark.TryParse(original.Format(), out Landmark back), "a formatted line parses");
            Equal(original.Key, back.Key, "key round trips");
            Equal("Two Rocks", back.Name, "name round trips");
            Equal("host", back.Author, "author round trips");
            Equal(111L, back.FirstSeenUtcTicks, "firstSeen round trips");
            Equal(222L, back.LastSeenUtcTicks, "lastSeen round trips");

            // The whole reason fields are escaped: a sign's text is whatever a player typed.
            var nasty = new Landmark(new LandmarkKey(1, 2, 3), "a\tb\nc\\d", "auth\tor", 1L, 2L);
            Check(Landmark.TryParse(nasty.Format(), out Landmark nastyBack),
                  "a name full of separators still parses");
            Equal("a\tb\nc\\d", nastyBack.Name, "separators in a name survive the round trip");
            Equal("auth\tor", nastyBack.Author, "separators in an author survive the round trip");

            // --- v2: the pile, the light, and the first floats to reach disk ----------------
            var lit = new Landmark(new LandmarkKey(10, 49, 27), "Two Rocks", "Steam_7656", 111L, 222L,
                                   true, new Vector3(10.25f, 51.5f, 27.75f));
            Check(Landmark.TryParse(lit.Format(), out Landmark litBack), "a v2 row parses");
            Check(litBack.HasPile, "the pile flag survives");
            Check(Math.Abs(litBack.Light.x - 10.25f) < 0.0001f, "light x round trips exactly");
            Check(Math.Abs(litBack.Light.y - 51.5f) < 0.0001f, "light y round trips exactly");
            Check(Math.Abs(litBack.Light.z - 27.75f) < 0.0001f, "light z round trips exactly");
            Equal("Two Rocks", litBack.Name, "the name still comes last and still round trips");

            var unlit = new Landmark(new LandmarkKey(1, 2, 3), "Plain", "host", 1L, 2L);
            Check(Landmark.TryParse(unlit.Format(), out Landmark unlitBack), "a pileless v2 row parses");
            Check(!unlitBack.HasPile, "no pile stays no pile");

            // --- v1 migration: a live server wrote one of these on 2026-09-02 ---------------
            const string v1 = "10\t49\t27\t639239624414309706\t639239624414309706\tSteam_76561198392625778\ttest";
            Check(Landmark.TryParse(v1, out Landmark old), "a v1 row still parses after the format bump");
            Equal("test", old.Name, "the v1 name is read from field 7, not field 11");
            Equal("Steam_76561198392625778", old.Author, "the v1 author survives");
            Equal(new LandmarkKey(10, 49, 27), old.Key, "the v1 key survives");
            Equal(639239624414309706L, old.FirstSeenUtcTicks, "the v1 history survives");
            Check(!old.HasPile, "a v1 row predates piles, so it has none");

            // --- what makes a landmark worth keeping ---------------------------------------
            Check(new Landmark(new LandmarkKey(0,0,0), "named", "host", 1L, 2L).IsWorthStoring,
                  "a named place is worth storing");
            Check(new Landmark(new LandmarkKey(0,0,0), "", "host", 1L, 2L, true, default).IsWorthStoring,
                  "an UNNAMED lit pile is worth storing — it is a waymark, not a blank");
            Check(!new Landmark(new LandmarkKey(0,0,0), "", "host", 1L, 2L).IsWorthStoring,
                  "neither named nor lit is nothing at all");

            Check(!Landmark.TryParse("", out _), "empty line is rejected");
            Check(!Landmark.TryParse(null, out _), "null line is rejected");
            Check(!Landmark.TryParse("1\t2\t3", out _), "a short line is rejected");
            Check(!Landmark.TryParse("x\t2\t3\t4\t5\tauthor\tname", out _), "a bad x is rejected");
            Check(!Landmark.TryParse("1\t2\t3\tzzz\t5\tauthor\tname", out _), "a bad timestamp is rejected");

            // A culture test is only worth having if it CAN fail.
            //
            // The first version of this used de-DE and was decoration: every field in this
            // format is an integer, and integers render identically under de-DE, so it passed
            // even with Format switched to CurrentCulture. Proven by reverting the fix, which
            // is the only reason it was caught.
            //
            // This culture's NEGATIVE SIGN is U+2212 MINUS SIGN rather than ASCII hyphen, so
            // the moment either side stops being invariant, a negative coordinate changes on
            // disk. Built by hand rather than picked from the OS: which real locale uses which
            // sign varies with the ICU version, and a test that depends on that is a test that
            // fails on someone else's machine for the wrong reason.
            var previous = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                var hostile = (System.Globalization.CultureInfo)
                    new System.Globalization.CultureInfo("de-DE").Clone();
                hostile.NumberFormat.NegativeSign = "−";
                System.Threading.Thread.CurrentThread.CurrentCulture = hostile;

                var abroad = new Landmark(new LandmarkKey(-7, 8, 9), "Nordwacht", "host", 12345L, 67890L,
                                          true, new Vector3(-7.5f, 8.25f, 9f));
                string line = abroad.Format();

                Check(line.IndexOf('−') < 0, "no locale-specific minus sign reaches disk");
                // The light is the mod's first non-integer field, so a comma-decimal machine
                // would write "8,25" into a tab-separated file: parses at home, corrupts abroad.
                Check(line.IndexOf(',') < 0, "no comma-decimal reaches disk");
                Check(line.IndexOf("8.25", StringComparison.Ordinal) >= 0,
                      "a fractional light coordinate writes a POINT whatever the machine's locale");

                Check(Landmark.TryParse("-7\t8\t9\t1\t2\thost\t1\t-7.5\t8.25\t9\tNordwacht", out Landmark read),
                      "a stored row still parses under a hostile locale");
                Equal(-7, read.Key.X, "and parses to the right value");
                Check(Math.Abs(read.Light.y - 8.25f) < 0.0001f, "including its fractional light");
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        // ---- sign reading ---------------------------------------------------------------

        private static void SignReadingTests()
        {
            Section("SignReading");

            // A blank sign is not a place. Vanilla writes nothing under the text key until
            // someone writes on one, so an unwritten sign arrives here as an empty string —
            // this is what keeps every freshly built sign out of the ledger.
            Check(!SignReading.TryRead("", "host", out _, out _), "an unwritten sign is not a landmark");
            Check(!SignReading.TryRead(null, "host", out _, out _), "a null text is not a landmark");
            Check(!SignReading.TryRead("   \n\t ", "host", out _, out _),
                  "a sign of pure whitespace is not a landmark");

            Check(SignReading.TryRead("Two Rocks", "host", out string name, out string author),
                  "a named sign is a landmark");
            Equal("Two Rocks", name, "the name comes through");
            Equal("host", author, "the author comes through");

            SignReading.TryRead("Gull\nCliff", "host", out name, out _);
            Equal("Gull Cliff", name, "a two-line sign becomes a one-line name");

            SignReading.TryRead("Two Rocks", "", out _, out author);
            Equal(SignReading.UnknownAuthor, author, "a sign with no author is attributed to nobody");

            SignReading.TryRead("Two Rocks", "   ", out _, out author);
            Equal(SignReading.UnknownAuthor, author, "a whitespace author is attributed to nobody");

            SignReading.TryRead("Two Rocks", null, out _, out author);
            Equal(SignReading.UnknownAuthor, author, "a null author does not crash the sweep");

            SignReading.TryRead("Two Rocks", "  76561198000000000  ", out _, out author);
            Equal("76561198000000000", author, "an author id is trimmed");

            SignReading.TryRead(new string('x', Landmark.MaxNameLength + 50), "host", out name, out _);
            Equal(Landmark.MaxNameLength, name.Length, "an over-long sign is bounded before it is stored");
        }

        // ---- what the raven may say -------------------------------------------------------

        private static void DisplayTextTests()
        {
            Section("DisplayText");

            // The moderation question, answered: storage stays faithful, the display strips.
            Equal("Home", DisplayText.StripRichText("<color=red>Home</color>"), "colour tags are stripped");
            Equal("Home", DisplayText.StripRichText("<b><i>Home</i></b>"), "nested tags are stripped");
            Equal("Home", DisplayText.StripRichText("<size=200>Home</size>"), "size tags are stripped");
            Equal("Two Rocks", DisplayText.StripRichText("Two Rocks"), "plain text is untouched");
            Equal("", DisplayText.StripRichText(""), "empty stays empty");
            Equal("", DisplayText.StripRichText(null), "null is not a crash");

            // Narrow on purpose: a player's punctuation must survive being spoken.
            Equal("<-- the ford", DisplayText.StripRichText("<-- the ford"),
                  "an arrow is not a tag and is kept");
            Equal("a < b", DisplayText.StripRichText("a < b"), "a lone bracket is kept");
            Equal("cost <3 stone", DisplayText.StripRichText("cost <3 stone"), "<3 is not a tag");
            Equal("<unclosed", DisplayText.StripRichText("<unclosed"), "an unclosed tag is kept as text");

            // A tag cannot span a line break, so a two-line sign cannot smuggle one.
            Equal("<a\nb>", DisplayText.StripRichText("<a\nb>"), "a newline means it was never a tag");

            // Speech: stripped, collapsed, bounded.
            Equal("Gull Cliff", DisplayText.ForSpeech("<color=red>Gull</color>\nCliff", 64),
                  "a two-line coloured sign becomes one clean line");
            Check(DisplayText.ForSpeech(new string('x', 200), 20).Length <= 21,
                  "an enormous name is bounded before it reaches the dialogue box");
            Check(DisplayText.ForSpeech(new string('x', 200), 20).EndsWith("…"),
                  "and says it was cut");
        }

        // ---- the beacon colour -------------------------------------------------------------

        private static void HexColourTests()
        {
            Section("HexColour");

            Check(HexColour.TryParse("FFB85A", out float r, out float g, out float b, out float a),
                  "a plain six-digit hex parses");
            Check(Math.Abs(r - 1f) < 0.001f, "red is full");
            Check(Math.Abs(g - 0.722f) < 0.005f, "green is 0xB8");
            Check(Math.Abs(b - 0.353f) < 0.005f, "blue is 0x5A");
            Check(Math.Abs(a - 1f) < 0.001f, "alpha defaults to opaque");

            Check(HexColour.TryParse("#FFB85A", out _, out _, out _, out _), "a leading hash is allowed");
            Check(HexColour.TryParse("  #FFB85A  ", out _, out _, out _, out _), "surrounding space is allowed");
            Check(HexColour.TryParse("ffb85a", out _, out _, out _, out _), "lower case is allowed");

            // Shorthand, because people type it.
            Check(HexColour.TryParse("F00", out r, out g, out b, out _), "three-digit shorthand parses");
            Check(Math.Abs(r - 1f) < 0.001f && g < 0.001f && b < 0.001f, "and expands correctly");

            Check(HexColour.TryParse("FFB85A80", out _, out _, out _, out a), "eight digits carry alpha");
            Check(Math.Abs(a - 0.502f) < 0.005f, "and the alpha is read");

            // The failures that matter: refusing beats painting with garbage.
            Check(!HexColour.TryParse("", out _, out _, out _, out _), "empty is refused");
            Check(!HexColour.TryParse(null, out _, out _, out _, out _), "null is refused");
            Check(!HexColour.TryParse("FFBB", out _, out _, out _, out _), "four digits are refused");
            Check(!HexColour.TryParse("GGGGGG", out _, out _, out _, out _), "non-hex letters are refused");
            Check(!HexColour.TryParse("orange", out _, out _, out _, out _), "a colour name is refused");

            // A refusal must not scribble on the caller's outputs.
            r = 0.5f;
            HexColour.TryParse("nonsense", out r, out _, out _, out _);
            Check(Math.Abs(r) < 0.001f, "a refused parse zeroes rather than half-writing");
        }

        // ---- pile detection --------------------------------------------------------------

        private static void PileDetectionTests()
        {
            Section("PileDetection");

            const float link = 2.5f, extent = 4f;
            const int min = 3, max = 12;

            // A cairn: a short tight stack.
            var stack = new List<Vector3> {
                new Vector3(0f, 0f, 0f), new Vector3(0.3f, 0.8f, 0.1f), new Vector3(0f, 1.6f, 0.2f)
            };
            List<PileDetection.Pile> piles = PileDetection.Find(stack, link, min, max, extent);
            Equal(1, piles.Count, "a tight stack of three is a cairn");
            Equal(3, piles[0].Pieces, "all three stones belong to it");
            Check(Math.Abs(piles[0].Top.y - 1.6f) < 0.001f, "the light sits at the height of the topmost stone");
            Check(Math.Abs(piles[0].Top.x - 0.1f) < 0.01f, "the light sits over the centre of the footprint");

            // Too few.
            Equal(0, PileDetection.Find(
                new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(0f, 0.8f, 0f) },
                link, min, max, extent).Count, "two stones are not a cairn");

            // A WALL. Same piece count as a cairn, spread out — footprint is what tells them
            // apart, and piece count alone never could.
            var wall = new List<Vector3> {
                new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(4f, 0f, 0f),
                new Vector3(6f, 0f, 0f), new Vector3(8f, 0f, 0f)
            };
            Equal(0, PileDetection.Find(wall, link, min, max, extent).Count,
                  "a five-piece wall is not a cairn, despite a legal piece count");

            // A house: too many pieces AND too wide.
            var house = new List<Vector3>();
            for (int x = 0; x < 5; x++)
                for (int z = 0; z < 5; z++)
                    house.Add(new Vector3(x * 2f, 0f, z * 2f));
            Equal(0, PileDetection.Find(house, link, min, max, extent).Count, "a stone house is not a cairn");

            // Two cairns far apart stay two.
            var pair = new List<Vector3> {
                new Vector3(0f, 0f, 0f), new Vector3(0.2f, 0.8f, 0f), new Vector3(0f, 1.6f, 0.2f),
                new Vector3(50f, 0f, 50f), new Vector3(50.2f, 0.8f, 50f), new Vector3(50f, 1.6f, 50.2f)
            };
            Equal(2, PileDetection.Find(pair, link, min, max, extent).Count,
                  "two distant stacks are two cairns");

            // Single-linkage transitivity: a chain of stones each within the link radius is
            // ONE cluster, so a long low chain is measured — and rejected — as one wall.
            var chain = new List<Vector3>();
            for (int i = 0; i < 6; i++) chain.Add(new Vector3(i * 2f, 0f, 0f));
            Equal(0, PileDetection.Find(chain, link, min, max, extent).Count,
                  "a chain links into one oversized cluster rather than several small ones");

            // STACKED COLUMNS (0.10.0). Three columns of eight stones, 1.5m apart — what was built
            // on Storm10 on 2026-10-16. They link into one cluster of 24 inside a 3m footprint:
            // one cairn at the shipped defaults, and nothing at all under the old cap of 12.
            var columns = new List<Vector3>();
            for (int c = 0; c < 3; c++)
                for (int s = 0; s < 8; s++)
                    columns.Add(new Vector3(c * 1.5f, s * 0.35f, 0f));
            int shippedMax = ModConfig.PileMaxPieces.Value;
            List<PileDetection.Pile> tall = PileDetection.Find(columns, ModConfig.PileLinkMeters.Value,
                ModConfig.PileMinPieces.Value, shippedMax, ModConfig.PileMaxExtentMeters.Value);
            Equal(1, tall.Count, "three stacked columns side by side are one cairn at the shipped defaults");
            if (tall.Count == 1)
            {
                Equal(24, tall[0].Pieces, "all 24 stones belong to it");
                Check(Math.Abs(tall[0].Top.y - 7 * 0.35f) < 0.001f, "the light sits on the tallest column");
            }
            Equal(0, PileDetection.Find(columns, link, min, 12, extent).Count,
                  "under the old cap of 12 the same columns were no cairn at all — the bug");
            // One column alone, taller than the old cap.
            var spire = new List<Vector3>();
            for (int s = 0; s < 15; s++) spire.Add(new Vector3(0f, s * 0.35f, 0f));
            Equal(1, PileDetection.Find(spire, ModConfig.PileLinkMeters.Value, ModConfig.PileMinPieces.Value,
                shippedMax, ModConfig.PileMaxExtentMeters.Value).Count, "a single 15-stone column is a cairn");
            // And the footprint rule still refuses a wide stone house at the new cap.
            Equal(0, PileDetection.Find(house, ModConfig.PileLinkMeters.Value, ModConfig.PileMinPieces.Value,
                shippedMax, ModConfig.PileMaxExtentMeters.Value).Count,
                  "a 25-stone house is still not a cairn at the new cap: the footprint refuses it");

            Equal(0, PileDetection.Find(null, link, min, max, extent).Count, "null input is not a crash");
            Equal(0, PileDetection.Find(new List<Vector3>(), link, min, max, extent).Count, "no stones, no cairns");
            Equal(0, PileDetection.Find(stack, 0f, min, max, extent).Count, "a zero link radius finds nothing");

            // --- pairing a pile with the sign that names it ---------------------------------
            var top = new Vector3(10f, 5f, 10f);
            var signs = new List<Vector3> { new Vector3(40f, 5f, 40f), new Vector3(12f, 1f, 10f) };
            Equal(1, PileDetection.NearestSign(top, signs, 6f), "the nearer sign names the cairn");

            // XZ-planar on purpose: a sign set into the side of a cairn sits well below its
            // top, and a 3D check would push it out of reach for no visible reason.
            Equal(1, PileDetection.NearestSign(top, signs, 3f),
                  "a sign four metres below still names the cairn it is set into");

            Equal(-1, PileDetection.NearestSign(top, new List<Vector3> { new Vector3(40f, 5f, 40f) }, 6f),
                  "a distant sign does not name it — an unnamed cairn is still a cairn");
            Equal(-1, PileDetection.NearestSign(top, null, 6f), "no signs at all is not a crash");

            // --- one sign, two cairns (review finding 3, 2026-09-24) ---------------------------
            // A cairn either side of a path and one named sign between them. Pile-picks-sign
            // gave BOTH piles the sign, so they collapsed into one landmark, one never lit, and
            // the ledger was rewritten every minute. Each sign now names only its nearest pile.
            var trail = new List<Vector3> { new Vector3(-3f, 2f, 0f), new Vector3(2f, 2f, 0f) };
            var trailSign = new List<Vector3> { new Vector3(0f, 1f, 0f) };
            int[] paired = PileDetection.PairSigns(trail, trailSign, 6f);
            Equal(2, paired.Length, "one answer per pile");
            Equal(0, paired[1], "the sign names the nearer cairn");
            Equal(-1, paired[0], "the farther cairn is left unnamed — still its own lit waymark, not merged");

            // Greedy by distance, not by pile order: pile 0 is within reach of both signs, but
            // sign 0 is much closer to pile 1, so pile 0 must end up with sign 1.
            var piles2 = new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(4f, 0f, 0f) };
            var signs2 = new List<Vector3> { new Vector3(3.8f, 0f, 0f), new Vector3(-3f, 0f, 0f) };
            int[] greedy = PileDetection.PairSigns(piles2, signs2, 6f);
            Equal(0, greedy[1], "the closest pair in the world is settled first");
            Equal(1, greedy[0], "and the other pile takes the sign left over");

            // Two signs by one cairn: it takes the nearer; the other stays a sign-only place.
            int[] twoSigns = PileDetection.PairSigns(
                new List<Vector3> { new Vector3(0f, 0f, 0f) },
                new List<Vector3> { new Vector3(5f, 0f, 0f), new Vector3(1f, 0f, 0f) }, 6f);
            Equal(1, twoSigns[0], "one cairn, two signs: the nearer names it");

            Equal(-1, PileDetection.PairSigns(trail, new List<Vector3> { new Vector3(40f, 0f, 40f) }, 6f)[0],
                  "a sign out of reach names nothing");
            Equal(-1, PileDetection.PairSigns(trail, null, 6f)[1], "no signs at all is not a crash");
            Equal(0, PileDetection.PairSigns(null, trailSign, 6f).Length, "no piles at all is not a crash");

            // Ties are settled by index, never by sort instability: equal distances, same answer.
            int[] tie = PileDetection.PairSigns(
                new List<Vector3> { new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f) },
                new List<Vector3> { new Vector3(0f, 0f, 0f) }, 6f);
            Equal(0, tie[0], "an exact tie goes to the lower pile index");
            Equal(-1, tie[1], "and only one pile gets the sign");

            // --- a drifting cairn keeps its identity ----------------------------------------
            // Live defect, 2026-09-02: an unnamed cairn is keyed on its own crown, a crown is a
            // computed centroid, and adding a stone moved it 0.8m — across a metre boundary,
            // so the landmark was pruned and re-founded with its history erased.
            var was = new LandmarkKey(52, 42, 18);
            var candidates = new List<LandmarkKey> { new LandmarkKey(51, 42, 19), new LandmarkKey(200, 42, 200) };
            Equal(0, PileDetection.NearestKey(was, candidates, 4f), "a cairn that drifted a metre is the same cairn");
            Equal(-1, PileDetection.NearestKey(was, new List<LandmarkKey> { new LandmarkKey(200, 42, 200) }, 4f),
                  "a cairn across the map is a different cairn");
            Equal(-1, PileDetection.NearestKey(was, candidates, 0f), "zero drift inherits nothing");
            Equal(-1, PileDetection.NearestKey(was, null, 4f), "no candidates is not a crash");

            // Height is excluded on purpose: a cairn grows upward as stones are added, so Y is
            // the axis most likely to move for the least meaningful reason.
            Equal(0, PileDetection.NearestKey(was, new List<LandmarkKey> { new LandmarkKey(52, 99, 18) }, 4f),
                  "a cairn that grew taller is still the same cairn");
        }

        // ---- beacon selection -------------------------------------------------------------

        private static Landmark LitAt(float x, float z, string name = "") =>
            new Landmark(LandmarkKey.FromPosition(new Vector3(x, 0f, z)), name, "", 0, 0,
                         true, new Vector3(x, 0f, z));

        private static void BeaconSelectionTests()
        {
            Section("BeaconSelection");

            // THE REPORTED CASE (2026-10-16). A hundred old cairns far to the west, then a
            // trail of new ones where the player is standing. 0.9.1 sent the first hundred in
            // ledger order — all old — and every new cairn stayed dark.
            var ledger = new List<Landmark>();
            for (int i = 0; i < 100; i++) ledger.Add(LitAt(-5000f - i * 10f, 0f));
            for (int i = 0; i < 5; i++) ledger.Add(LitAt(2000f + i * 30f, 0f, "new" + i));
            var player = new List<Vector3> { new Vector3(2050f, 0f, 0f) };

            List<Landmark> sent = BeaconSelection.Choose(ledger, player, 100);
            Equal(100, sent.Count, "over the cap, exactly the cap is sent");
            int newSent = 0;
            foreach (Landmark l in sent) if (l.Name.StartsWith("new")) newSent++;
            Equal(5, newSent, "every new cairn beside the player is sent (0.9.1 sent none)");

            // Under the cap nothing changes: same set, same order.
            var few = new List<Landmark> { LitAt(900f, 0f, "a"), LitAt(10f, 0f, "b"), LitAt(500f, 0f, "c") };
            List<Landmark> same = BeaconSelection.Choose(few, player, 100);
            Equal("a,b,c", string.Join(",", same.ConvertAll(l => l.Name)), "under the cap, ledger order is untouched");

            // Exactly at the cap is still under it.
            Equal(3, BeaconSelection.Choose(few, player, 3).Count, "at the cap, all are sent");

            // Two players far apart each keep their own nearby cairns.
            var spread = new List<Landmark> {
                LitAt(0f, 0f, "west-near"), LitAt(30f, 0f, "west-mid"), LitAt(3000f, 0f, "middle"),
                LitAt(6000f, 0f, "east-near"), LitAt(5970f, 0f, "east-mid"),
            };
            var two = new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(6000f, 0f, 0f) };
            List<Landmark> pick = BeaconSelection.Choose(spread, two, 4);
            Check(!pick.Exists(l => l.Name == "middle"), "the cairn far from both players is the one dropped");
            Equal(4, pick.Count, "two players, cap 4: four sent");

            // Nobody online: nothing to rank for, ledger order up to the cap.
            List<Landmark> nobody = BeaconSelection.Choose(spread, new List<Vector3>(), 2);
            Equal("west-near,west-mid", string.Join(",", nobody.ConvertAll(l => l.Name)), "no players: first in ledger order");

            // Height is not distance: a headland cairn 200m up beats a shore one 50m away horizontally.
            var heights = new List<Landmark> {
                new Landmark(new LandmarkKey(50, 0, 0), "shore", "", 0, 0, true, new Vector3(50f, 0f, 0f)),
                new Landmark(new LandmarkKey(10, 200, 0), "headland", "", 0, 0, true, new Vector3(10f, 200f, 0f)),
            };
            Equal("headland", BeaconSelection.Choose(heights, new List<Vector3> { new Vector3(0f, 0f, 0f) }, 1)[0].Name,
                  "distance is XZ-planar — a cairn's height does not push it out");

            // Ties keep ledger order, so the chosen set does not flicker between broadcasts.
            var ties = new List<Landmark> { LitAt(10f, 0f, "first"), LitAt(-10f, 0f, "second"), LitAt(0f, 10f, "third") };
            Equal("first", BeaconSelection.Choose(ties, new List<Vector3> { new Vector3(0f, 0f, 0f) }, 1)[0].Name,
                  "equal distances keep ledger order");

            Equal(0, BeaconSelection.Choose(spread, two, 0).Count, "a zero cap sends nothing rather than throwing");
        }

        // ---- stone stacking ---------------------------------------------------------------

        private static void NamedListingTests()
        {
            Section("NamedListing");

            var names = new List<string> { "Far Point", "", "Near Ford", "   ", "Mid Ridge", null };
            var spots = new List<Vector3> {
                new Vector3(300f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(10f, 50f, 0f),
                new Vector3(2f, 0f, 0f), new Vector3(0f, 0f, 100f), new Vector3(3f, 0f, 0f)
            };
            List<int> order = NamedListing.NamedByDistance(names, spots, new Vector3(0f, 0f, 0f));
            Equal(3, order.Count, "only the three named cairns are listed; blank, spaces and null are waymarks");
            if (order.Count == 3)
            {
                Equal(2, order[0], "nearest first: Near Ford at 10 m (its 50 m height is ignored)");
                Equal(4, order[1], "then Mid Ridge at 100 m");
                Equal(0, order[2], "then Far Point at 300 m");
            }

            List<int> tie = NamedListing.NamedByDistance(
                new List<string> { "B", "A" },
                new List<Vector3> { new Vector3(5f, 0f, 0f), new Vector3(0f, 0f, 5f) },
                new Vector3(0f, 0f, 0f));
            Check(tie.Count == 2 && tie[0] == 0 && tie[1] == 1, "equal distances keep the order the server sent");

            Equal(0, NamedListing.NamedByDistance(new List<string>(), new List<Vector3>(), new Vector3(0f, 0f, 0f)).Count,
                  "nothing sent, nothing listed");
            Equal(0, NamedListing.NamedByDistance(null, null, new Vector3(0f, 0f, 0f)).Count, "null input is not a crash");
            Equal(1, NamedListing.NamedByDistance(new List<string> { "A", "B" }, new List<Vector3> { new Vector3(0f, 0f, 0f) },
                  new Vector3(0f, 0f, 0f)).Count, "mismatched lengths list only the pairs that exist");
            Check(Math.Abs(NamedListing.DistanceXZ(new Vector3(3f, 99f, 4f), new Vector3(0f, 0f, 0f)) - 5f) < 0.0001f,
                  "distance is along the ground: 3-4-5, height ignored");
        }

        private static void StoneStackingTests()
        {
            Section("StoneStacking");

            // Names as the game actually hands them over: a placed piece carries Unity's
            // "(Clone)" suffix, the build ghost carries the bare prefab name.
            Equal("Placeable_Stone", StoneStacking.PrefabName("Placeable_Stone(Clone)"), "the instance suffix is stripped");
            Equal("Placeable_Stone", StoneStacking.PrefabName("Placeable_Stone"), "a bare prefab name is unchanged");
            Equal("", StoneStacking.PrefabName(null), "null is not a crash");
            Check(StoneStacking.IsStone("Placeable_Stone(Clone)"), "a placed stone is a stone");
            Check(!StoneStacking.IsStone("stone_pile(Clone)"), "stone_pile is not the stackable stone");
            Check(!StoneStacking.IsStone("placeable_stone(Clone)"), "prefab names are matched exactly, case included");

            // The point: a stone on a stone.
            Check(!StoneStacking.Refuse(true, "Placeable_Stone", "Placeable_Stone(Clone)"), "a stone may go on a stone");

            // The fence: nothing else on a stone.
            Check(StoneStacking.Refuse(true, "wood_wall_half", "Placeable_Stone(Clone)"), "a wooden wall may NOT stand on a stone");
            Check(StoneStacking.Refuse(true, "piece_workbench", "Placeable_Stone(Clone)"), "nor a workbench");
            Check(StoneStacking.Refuse(true, "sign", "Placeable_Stone(Clone)"), "nor a sign — a cairn's sign stands BESIDE it, on the ground");

            // Everything that is not 'onto a stone' is vanilla's business, untouched.
            Check(!StoneStacking.Refuse(true, "wood_wall_half", "stone_floor_2x2(Clone)"), "building on other stone pieces is untouched");
            Check(!StoneStacking.Refuse(true, "wood_wall_half", ""), "building on the ground is untouched");
            Check(!StoneStacking.Refuse(true, "wood_wall_half", null), "no piece under the ray is untouched");
            Check(!StoneStacking.Refuse(true, "Placeable_Stone", "wood_floor(Clone)"), "a stone on a floor is untouched");

            // The switch.
            Check(!StoneStacking.Refuse(false, "wood_wall_half", "Placeable_Stone(Clone)"),
                  "with stacking OFF the fence stands down — vanilla's own flag refuses it anyway");
            Equal(true, ModConfig.StackableStones.Value, "stacking is on by default");
        }

        // ---- config ledger ----------------------------------------------------------------

        private const string BeaconSlot = "4 - The light::BeaconMaxCount";
        private const string PileMaxSlot = "3 - Cairn piles::PileMaxPieces";

        private static Dictionary<string, string> Ini(params string[] lines) => ConfigLedger.ParseIni(lines);

        private static void ConfigLedgerTests()
        {
            Section("ConfigLedger");

            // THE REAL FILE. Written by Cairn 0.9.1 itself on a dedicated server, unstamped, with
            // BeaconMaxCount at the old shipped 100 — every existing install looks like this.
            string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "written-by-0.9.1.cfg");
            Check(File.Exists(fixture), "the 0.9.1 fixture is copied beside the harness");
            if (File.Exists(fixture))
            {
                Dictionary<string, string> real = ConfigLedger.ParseIni(File.ReadAllLines(fixture));
                Equal("100", real.TryGetValue(BeaconSlot, out string v) ? v : null, "the parser reads BeaconMaxCount out of the real file");
                Equal(0, ConfigLedger.ReadVersion(real), "a 0.9.1 file carries no stamp, so it is version 0");

                Equal("12", real.TryGetValue(PileMaxSlot, out string pm) ? pm : null, "the real file holds PileMaxPieces at 12");

                ConfigLedger.MigrationPlan p = ConfigLedger.Plan(real, 0);
                Equal(2, p.ResetToDefault.Count, "the real 0.9.1 file plans exactly two moves");
                Check(p.ResetToDefault.Contains(BeaconSlot), "one is BeaconMaxCount");
                Check(p.ResetToDefault.Contains(PileMaxSlot), "the other is PileMaxPieces");
                Equal(0, p.Kept.Count, "nothing else in the real file is touched or reported");
                Check(ConfigLedger.Describe(p).StartsWith("config: version 0 -> 2: 2 values moved to their new defaults"),
                      "the boot line names the versions and the count: " + ConfigLedger.Describe(p));

                // A 0.9.2 file (stamped 1) runs only the 0.10.0 rung.
                ConfigLedger.MigrationPlan from1 = ConfigLedger.Plan(real, 1);
                Equal(1, from1.ResetToDefault.Count, "a file stamped 1 plans only the rung it has not passed");
                Check(from1.ResetToDefault.Contains(PileMaxSlot), "and that is PileMaxPieces");
            }

            // An owner who chose their own pile cap keeps it.
            Equal(0, ConfigLedger.Plan(Ini("[0 - Meta]", "ConfigVersion = 1", "[3 - Cairn piles]", "PileMaxPieces = 20"), 1)
                         .ResetToDefault.Count, "a PileMaxPieces the owner set (20) is not moved");

            // An owner's own value is theirs.
            ConfigLedger.MigrationPlan mine = ConfigLedger.Plan(Ini("[4 - The light]", "BeaconMaxCount = 40"), 0);
            Equal(0, mine.ResetToDefault.Count, "a value the owner set (40) is not moved");
            Equal(1, mine.Kept.Count, "...and is reported as kept");
            Check(ConfigLedger.Describe(mine).Contains("kept as yours (4 - The light.BeaconMaxCount=40)"),
                  "the boot line names the kept value and what it holds");

            // Already migrated: nothing to do, ever again.
            Equal(0, ConfigLedger.Plan(Ini("[0 - Meta]", "ConfigVersion = 1", "[4 - The light]", "BeaconMaxCount = 100"),
                                       1).ResetToDefault.Count,
                  "a stamped file is never migrated twice, even if it holds 100 again");
            Equal(1, ConfigLedger.ReadVersion(Ini("[0 - Meta]", "ConfigVersion = 1")), "the stamp is read from [0 - Meta]");
            Equal(0, ConfigLedger.ReadVersion(Ini("[Meta]", "ConfigVersion = 1")),
                  "a stamp in the wrong section does not count");
            Equal(0, ConfigLedger.ReadVersion(Ini("[0 - Meta]", "ConfigVersion = banana")), "an unparseable stamp is version 0");

            // A newer build's stamp: this build has nothing to do with it.
            Equal(0, ConfigLedger.Plan(Ini("[0 - Meta]", "ConfigVersion = 7", "[4 - The light]", "BeaconMaxCount = 100"),
                                       7).ResetToDefault.Count,
                  "a file stamped by a NEWER build is left alone");

            // A negative stamp is a version-0 file, not a loop bound.
            Equal(1, ConfigLedger.Plan(Ini("[4 - The light]", "BeaconMaxCount = 100"), -2000000000).ResetToDefault.Count,
                  "a negative stamp migrates as version 0, and does so instantly");

            // BepInEx keys are ordinal and case-sensitive; the snapshot must be too.
            Equal(0, ConfigLedger.Plan(Ini("[4 - The light]", "beaconmaxcount = 100"), 0).ResetToDefault.Count,
                  "a mis-cased key is a different key, exactly as BepInEx treats it");
            Equal(0, ConfigLedger.Plan(Ini("[4 - the light]", "BeaconMaxCount = 100"), 0).ResetToDefault.Count,
                  "so is a mis-cased section");

            // Whitespace and missing keys.
            Equal(1, ConfigLedger.Plan(Ini("  [4 - The light]  ", "  BeaconMaxCount   =   100  "), 0).ResetToDefault.Count,
                  "padding around the line is not part of the value");
            Equal(0, ConfigLedger.Plan(Ini("[4 - The light]", "BeaconColour = FFAA55"), 0).ResetToDefault.Count,
                  "a file without the key plans nothing for it");
            Equal(0, ConfigLedger.Plan(new Dictionary<string, string>(), 0).ResetToDefault.Count,
                  "an empty snapshot (first install) plans nothing");

            // The table seam: one slot, one decision, even if two rungs name it.
            var twice = new Dictionary<int, ConfigLedger.Rebase[]>
            {
                { 1, new[] { new ConfigLedger.Rebase { Section = "S", Key = "K", OldDefaults = new[] { "1" } } } },
                { 2, new[] { new ConfigLedger.Rebase { Section = "S", Key = "K", OldDefaults = new[] { "1" } } } },
            };
            Equal(1, ConfigLedger.Plan(Ini("[S]", "K = 1"), 0, 2, twice).ResetToDefault.Count,
                  "a key named on two rungs is moved once");
            Equal(0, ConfigLedger.Plan(Ini("[S]", "K = 1"), 1, 2, new Dictionary<int, ConfigLedger.Rebase[]>
                {
                    { 1, new[] { new ConfigLedger.Rebase { Section = "S", Key = "K", OldDefaults = new[] { "1" } } } },
                }).ResetToDefault.Count,
                "a rung the file has already passed does not run");

            // The ledger and the bound config agree on where the stamp lives and what it binds as.
            Equal(0, ModConfig.ConfigVersion.Value, "ConfigVersion binds at 0, so an unstamped file reads as version 0");
            Equal(250, ModConfig.BeaconMaxCount.Value, "the new default the rung moves to is 250");
            Equal(100, ModConfig.PileMaxPieces.Value, "the new default the 0.10.0 rung moves to is 100");
            Equal(2, ConfigLedger.CurrentVersion, "0.10.0 is config version 2");
        }

        // ---- store ----------------------------------------------------------------------

        private static void StoreTests()
        {
            Section("LandmarkStore");

            LandmarkStore.Clear();
            Equal(0, LandmarkStore.Count, "starts empty");
            Check(!LandmarkStore.IsDirty, "an empty store is clean");

            var key = new LandmarkKey(5, 6, 7);
            Check(LandmarkStore.Upsert(key, "Two Rocks", "host", 1000L), "first sighting is a change");
            Check(LandmarkStore.IsDirty, "a new landmark marks the store dirty");
            Equal(1, LandmarkStore.Count, "one landmark stored");

            LandmarkStore.MarkClean();
            Check(!LandmarkStore.Upsert(key, "Two Rocks", "host", 2000L),
                  "an unchanged sighting is not a change");
            Check(!LandmarkStore.IsDirty,
                  "re-seeing an unchanged landmark does not dirty the store");

            LandmarkStore.TryGet(key, out Landmark seen);
            Equal(1000L, seen.FirstSeenUtcTicks, "firstSeen is never overwritten by a later sighting");
            Equal(2000L, seen.LastSeenUtcTicks, "lastSeen advances");

            Check(LandmarkStore.Upsert(key, "Three Rocks", "host", 3000L), "a rename is a change");
            Check(LandmarkStore.IsDirty, "a rename dirties the store");
            LandmarkStore.TryGet(key, out seen);
            Equal("Three Rocks", seen.Name, "the new name is stored");
            Equal(1000L, seen.FirstSeenUtcTicks, "a rename still preserves firstSeen");

            Check(LandmarkStore.Remove(key), "remove reports the removal");
            Check(!LandmarkStore.Remove(key), "removing what is gone reports nothing");
            Equal(0, LandmarkStore.Count, "store is empty again");

            // --- carrying history across a drift ---------------------------------------------
            LandmarkStore.Clear();
            var moved = new LandmarkKey(51, 42, 19);
            LandmarkStore.Upsert(moved, "", SignReading.UnknownAuthor, true, new Vector3(51.5f, 42f, 19.3f), 5000L);
            LandmarkStore.MarkClean();

            Check(LandmarkStore.CarryHistory(moved, 1000L), "an older history is carried across");
            LandmarkStore.TryGet(moved, out Landmark inherited);
            Equal(1000L, inherited.FirstSeenUtcTicks, "the cairn is as old as the one it replaced");
            Equal(5000L, inherited.LastSeenUtcTicks, "but was last seen just now");
            Check(LandmarkStore.IsDirty, "carrying history dirties the store");

            Check(!LandmarkStore.CarryHistory(moved, 9000L),
                  "a LATER history is refused — a landmark cannot be made younger");
            LandmarkStore.TryGet(moved, out inherited);
            Equal(1000L, inherited.FirstSeenUtcTicks, "and the older date survives the attempt");

            Check(!LandmarkStore.CarryHistory(new LandmarkKey(9, 9, 9), 1L), "carrying to nothing is refused");
            Check(!LandmarkStore.CarryHistory(moved, 0L), "a zero history is refused");

            LandmarkStore.Clear();
            LandmarkStore.Upsert(new LandmarkKey(1, 1, 1), "A", "host", 1L);
            List<Landmark> snap = LandmarkStore.Snapshot();
            LandmarkStore.Upsert(new LandmarkKey(2, 2, 2), "B", "host", 1L);
            Equal(1, snap.Count, "a snapshot is a copy, not a live view");
        }

        // ---- persistence ----------------------------------------------------------------

        private static void PersistenceTests()
        {
            Section("Persistence");

            string dir = Path.Combine(_tempRoot, "world");
            Directory.CreateDirectory(dir);
            Persistence.OverrideDirectory = dir;
            Persistence.OverrideWorldUid = 4242UL;

            string path = Path.Combine(dir, "cairn_landmarks_4242.dat");

            // --- fresh world -------------------------------------------------------------
            Persistence.ResetForTests();
            Persistence.Load();
            Check(Persistence.IsLoaded, "a fresh world still counts as loaded");
            Equal(0, LandmarkStore.Count, "a fresh world has no landmarks");

            Persistence.Save();
            Check(!File.Exists(path), "a clean store writes nothing");

            // --- round trip through the SHIPPING writer ----------------------------------
            LandmarkStore.Upsert(new LandmarkKey(10, 20, 30), "Two Rocks", "host", 111L);
            LandmarkStore.Upsert(new LandmarkKey(-1, -2, -3), "Gull\tCliff", "76561198000000000", 222L);
            Persistence.Save();
            Check(File.Exists(path), "a dirty store writes a file");
            Check(!File.Exists(path + ".tmp"), "no .tmp is left orphaned");

            // Assert on the bytes the mod ACTUALLY wrote. Hand-built fixtures emit no BOM
            // while Encoding.UTF8 does; that mismatch once let a suite agree with itself and
            // disagree with disk for as long as it existed.
            byte[] raw = File.ReadAllBytes(path);
            Check(!(raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF),
                  "the shipping writer emits no BOM");
            string text = Encoding.UTF8.GetString(raw);
            // Pinned to the current version deliberately, so a format bump has to be a
            // conscious edit here rather than something a loose assertion waves through.
            Check(text.StartsWith("version\t2\n", StringComparison.Ordinal),
                  "the file opens with a v2 version header");
            Check(text.IndexOf("Gull\\tCliff", StringComparison.Ordinal) >= 0,
                  "a tab inside a name is escaped on disk, not written raw");

            Persistence.ResetForTests();
            Persistence.Load();
            Equal(2, LandmarkStore.Count, "both landmarks come back");
            Check(LandmarkStore.TryGet(new LandmarkKey(-1, -2, -3), out Landmark gull), "the second is found by key");
            Equal("Gull\tCliff", gull.Name, "the tabbed name is restored exactly");
            Equal("76561198000000000", gull.Author, "the author is restored");
            Equal(222L, gull.FirstSeenUtcTicks, "firstSeen survives a save and load");
            Check(!LandmarkStore.IsDirty, "a freshly loaded store is clean");

            // --- an UNNAMED lit pile must survive a save ----------------------------------
            // The store's sparseness rule drops landmarks that are "nothing"; a pile with no
            // sign is not nothing, it is a lit waymark. Before the pile existed the rule was
            // "drop unless named", and that rule would silently delete every unnamed cairn on
            // the first autosave.
            LandmarkStore.Upsert(new LandmarkKey(70, 8, 70), "", SignReading.UnknownAuthor,
                                 true, new Vector3(70.5f, 10.25f, 70.5f), 999L);
            Persistence.Save();
            Persistence.ResetForTests();
            Persistence.Load();
            Check(LandmarkStore.TryGet(new LandmarkKey(70, 8, 70), out Landmark waymark),
                  "an unnamed lit pile is still on disk after a save and load");
            Check(waymark != null && waymark.HasPile, "and it comes back lit");
            Check(waymark != null && Math.Abs(waymark.Light.y - 10.25f) < 0.0001f,
                  "with its light where it was left");
            LandmarkStore.Remove(new LandmarkKey(70, 8, 70));
            Persistence.Save();

            // --- .bak rotation -----------------------------------------------------------
            LandmarkStore.Upsert(new LandmarkKey(10, 20, 30), "Two Rocks Renamed", "host", 333L);
            Persistence.Save();
            Check(File.Exists(path + ".bak"), "the previous file is kept as .bak");
            Check(!File.Exists(path + ".tmp"), "still no orphaned .tmp after a rotation");

            // --- world scoping -----------------------------------------------------------
            Persistence.OverrideWorldUid = 9999UL;
            Persistence.ResetForTests();
            Persistence.Load();
            Equal(0, LandmarkStore.Count, "a different world sees none of the first world's landmarks");
            LandmarkStore.Upsert(new LandmarkKey(1, 1, 1), "Other World", "host", 1L);
            Persistence.Save();
            string otherPath = Path.Combine(dir, "cairn_landmarks_9999.dat");
            Check(File.Exists(otherPath), "the second world writes its own file");
            Check(File.Exists(path), "the first world's file is untouched");

            // --- a world change in ONE process (review finding 1, 2026-09-24) -----------------
            // The plugin lives for the whole process, so leaving world A and hosting world B
            // used to find _loaded still set: Load returned early, B's ledger never loaded, and
            // the next autosave wrote A's rows into B's file. The world-end path is: flush
            // while A's path still resolves, then Unload.
            Persistence.OverrideWorldUid = 5001UL;
            Persistence.ResetForTests();
            Persistence.Load();
            LandmarkStore.Upsert(new LandmarkKey(3, 3, 3), "World A Cairn", "host", 1L);
            Persistence.Save(force: true);    // the flush at world end
            Persistence.Unload();
            Check(!Persistence.IsLoaded, "after a world ends nothing is loaded, so a joining client is not the authority");
            Equal(0, LandmarkStore.Count, "after a world ends its rows are gone from memory");

            Persistence.OverrideWorldUid = 5002UL;
            Persistence.Load();
            Check(Persistence.IsLoaded, "the next world loads its own ledger");
            Equal(0, LandmarkStore.Count, "the next world starts with none of the last world's rows");
            LandmarkStore.Upsert(new LandmarkKey(4, 4, 4), "World B Cairn", "host", 2L);
            Persistence.Save();
            string bText = File.ReadAllText(Path.Combine(dir, "cairn_landmarks_5002.dat"));
            Check(bText.IndexOf("World A Cairn", StringComparison.Ordinal) < 0,
                  "the next world's file never receives the last world's rows");
            string aText = File.ReadAllText(Path.Combine(dir, "cairn_landmarks_5001.dat"));
            Check(aText.IndexOf("World A Cairn", StringComparison.Ordinal) >= 0,
                  "the last world's final change was flushed to its own file");
            Persistence.Unload();
            Persistence.Save(force: true);
            Check(File.ReadAllText(Path.Combine(dir, "cairn_landmarks_5002.dat")).IndexOf("World B Cairn", StringComparison.Ordinal) >= 0,
                  "a save after unload writes nothing over the file");

            // --- wholly corrupt: binary garbage ------------------------------------------
            string cdir = Path.Combine(_tempRoot, "corrupt");
            Directory.CreateDirectory(cdir);
            Persistence.OverrideDirectory = cdir;
            Persistence.OverrideWorldUid = 7L;
            string cpath = Path.Combine(cdir, "cairn_landmarks_7.dat");

            File.WriteAllBytes(cpath, new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 });
            RavenIron.Cairn.Cairn.Log.Clear();
            Persistence.ResetForTests();
            Persistence.Load();
            Equal(0, LandmarkStore.Count, "a corrupt file loads no landmarks");
            Check(!File.Exists(cpath), "the corrupt file is moved out of the way");
            Check(File.Exists(cpath + ".corrupt"), "it is quarantined as .corrupt");
            Check(RavenIron.Cairn.Cairn.Log.Errors.Count > 0, "corruption is reported at error level");

            // --- header only: the quiet path ---------------------------------------------
            File.WriteAllText(cpath, "version\t1\n# x\ty\tz\tfirstSeenUtcTicks\tlastSeenUtcTicks\tauthor\tname\n");
            RavenIron.Cairn.Cairn.Log.Clear();
            Persistence.ResetForTests();
            Persistence.Load();
            Equal(0, LandmarkStore.Count, "a header-only file has no landmarks");
            Check(File.Exists(cpath), "a header-only file is NOT quarantined — nothing failed");
            Equal(0, RavenIron.Cairn.Cairn.Log.Errors.Count, "a header-only file logs no error");

            // --- partially corrupt: per-line isolation ------------------------------------
            File.WriteAllText(cpath,
                "version\t1\n" +
                "5\t6\t7\t10\t20\thost\tGood One\n" +
                "this line is not a landmark\n");
            RavenIron.Cairn.Cairn.Log.Clear();
            Persistence.ResetForTests();
            Persistence.Load();
            Equal(1, LandmarkStore.Count, "the readable line survives a bad neighbour");
            Check(File.Exists(cpath), "a partially readable file is NOT quarantined");
            Check(RavenIron.Cairn.Cairn.Log.Warnings.Count > 0, "the skipped line is reported");

            // --- version header recognised by content, not position ------------------------
            // RW's bug: skipping line 1 unconditionally swallowed the only line a short binary
            // file has, which is exactly what hid its whole-file corruption case.
            File.WriteAllText(cpath,
                "5\t6\t7\t10\t20\thost\tFirst Line Data\n" +
                "version\t1\n");
            Persistence.ResetForTests();
            Persistence.Load();
            Equal(1, LandmarkStore.Count, "a data line at the top is read, and a later header is skipped");

            Persistence.OverrideDirectory = null;
            Persistence.OverrideWorldUid = null;
            Persistence.ResetForTests();
        }
    }
}
