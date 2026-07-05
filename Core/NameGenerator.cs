using System;
using System.Collections.Generic;

namespace CustomSlimeCreator.Core
{
    /// <summary>
    /// Generates combined names for largo fusions by merging syllables of two names.
    /// Handles Spanish/English and produces readable results.
    /// </summary>
    public static class NameGenerator
    {
        private static readonly string[] Vowels = { "a", "e", "i", "o", "u" };
        private static readonly string[] Consonants = { "b", "c", "d", "f", "g", "h", "j", "k", "l", "m", "n", "p", "q", "r", "s", "t", "v", "w", "x", "y", "z" };

        /// <summary>Generates a combined name from two display names.</summary>
        public static string Combine(string nameA, string nameB)
        {
            if (string.IsNullOrEmpty(nameA)) return nameB ?? "Slime";
            if (string.IsNullOrEmpty(nameB)) return nameA;

            string a = nameA.Trim();
            string b = nameB.Trim();

            // Try different merge strategies and pick the best one
            var candidates = new List<string>();

            // Strategy 1: First half of A + last half of B
            candidates.Add(MergeHalf(a, b, 0.5f, 0.5f));
            // Strategy 2: First 1/3 of A + last 2/3 of B
            candidates.Add(MergeHalf(a, b, 0.33f, 0.66f));
            // Strategy 3: First 2/3 of A + last 1/3 of B
            candidates.Add(MergeHalf(a, b, 0.66f, 0.33f));
            // Strategy 4: First syllable of A + whole B
            candidates.Add(FirstSyllable(a) + b);
            // Strategy 5: Whole A + last syllable of B
            candidates.Add(a + LastSyllable(b));

            // Pick the shortest non-empty candidate, it's usually the most readable
            string best = null;
            foreach (var c in candidates)
            {
                if (string.IsNullOrEmpty(c) || c.Length < 3) continue;
                if (best == null || c.Length < best.Length) best = c;
            }

            return Capitalize(best ?? a + b);
        }

        private static string MergeHalf(string a, string b, float aRatio, float bRatio)
        {
            int aLen = Math.Max(1, (int)(a.Length * aRatio));
            int bLen = Math.Max(1, (int)(b.Length * bRatio));
            string partA = a.Substring(0, Math.Min(aLen, a.Length));
            string partB = b.Substring(Math.Max(0, b.Length - bLen));
            return partA + partB;
        }

        private static string FirstSyllable(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.ToLower();
            // Find first vowel
            int firstVowel = -1;
            for (int i = 0; i < s.Length; i++)
                if (IsVowel(s[i])) { firstVowel = i; break; }
            if (firstVowel <= 0) return s.Substring(0, Math.Min(2, s.Length));
            // Take up to and including the first vowel
            return s.Substring(0, Math.Min(firstVowel + 2, s.Length));
        }

        private static string LastSyllable(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 2) return s;
            s = s.ToLower();
            // Find last vowel group
            int lastVowelStart = -1;
            for (int i = s.Length - 1; i >= 0; i--)
                if (IsVowel(s[i])) { lastVowelStart = i; break; }
            if (lastVowelStart <= 0) return s;
            // Take from last vowel onwards (but skip if it would duplicate the previous name)
            return s.Substring(lastVowelStart);
        }

        private static bool IsVowel(char c) => "aeiouAEIOUáéíóúÁÉÍÓÚ".IndexOf(c) >= 0;

        private static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpper(s[0]) + s.Substring(1);
        }
    }
}
