using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ModSettings
{
    // The sentences of unlock messages, in the words and order of the game's own. This file uses
    // nothing from the game, so a test harness can build it on its own.
    internal static class UnlockText
    {
        internal const string Win = "win";
        // The game's word for quiz, before the number in quiz pages' names; <quiz> in comments.
        internal const string QuizWord = "riddle";
        internal const string Unlock = "unlock";
        internal const string AndWord = "and";
        // The words every win count page of the game starts its lines with, before what unlocks.
        internal const string YouNowUnlock = "you now unlock";

        // One term of a gate. A count term passes once Count of the chapter's quizzes are won, as
        // Global.GetWin reads "<chapter>-<quiz>-<n>"; any other term names a page that must be won.
        internal sealed class Term
        {
            internal string Gate;
            // For a count term: the chapter's name as its quizzes' names start, with spaces.
            internal string Chapter;
            internal int Count;
            internal bool IsCount => Chapter != null;
        }

        private static readonly Regex CountTerm = new Regex(@"^(.+?)-" + QuizWord + "-(.+)$");

        internal static Term ReadTerm(string term)
        {
            var m = CountTerm.Match(term);
            if (m.Success && int.TryParse(m.Groups[2].Value, out var count))
            {
                return new Term { Gate = term, Chapter = m.Groups[1].Value.Replace("-", " "), Count = count };
            }
            return new Term { Gate = term };
        }

        // "win <n> <chapter> <quiz>" for a count term, as the game's explanations write it, and
        // "win <page>" for a page.
        internal static string Condition(Term term) => term.IsCount
            ? Win + " " + term.Count + " " + term.Chapter + " " + QuizWord
            : Win + " " + term.Gate.Replace("-", " ");

        // The line an explanation gets for one alternative of a gate: its terms' conditions joined
        // by "and", then "unlock" and the feature's words.
        internal static string ExplainLine(IEnumerable<Term> alternative, string feature) =>
            string.Join(" " + AndWord + " ", alternative.Select(Condition)) + " " + Unlock + " " + feature;

        // The line a win count page gets.
        internal static string PageLine(string feature) => YouNowUnlock + " " + feature;

        // A digit glyph, as the game stores numbers in its text: a digit, or a digit at a place.
        private static readonly Regex Digit = new Regex(@"^[0-6](?:e[0-9]+)?$");
        private static readonly Regex Value = new Regex(@"^\((\d+)\)number$");

        // The count of an explanation line of the form "win <n> ... unlock ...", as the game
        // stores it (its number's glyphs and the token holding the number's value), or null.
        internal static int? CountOf(IList<string> line)
        {
            if (line.Count < 2 || line[0] != Win || !line.Contains(Unlock))
            {
                return null;
            }
            int? count = null;
            for (var i = 1; i < line.Count; i++)
            {
                var m = Value.Match(line[i]);
                if (m.Success && int.TryParse(m.Groups[1].Value, out var v))
                {
                    count = v;
                }
                else if (!Digit.IsMatch(line[i]))
                {
                    break;
                }
            }
            return count;
        }

        // True for a line the game's explanations end with, telling what a count of wins unlocks.
        internal static bool IsUnlockLine(IList<string> line) => line.Count > 0 && line[0] == Win && line.Contains(Unlock);

        // Adds lines to an explanation's lines. The explanation's own unlock lines at its end, and
        // the added lines, end up in order of count; at the same count, the explanation's own come
        // first, then the added lines in the order given. A line with no count, as one for a gate
        // that counts no quizzes of this chapter, sorts after every count.
        internal static List<List<string>> Merge(List<List<string>> lines, IEnumerable<(int? Count, List<string> Line)> added)
        {
            var start = lines.Count;
            while (start > 0 && IsUnlockLine(lines[start - 1]))
            {
                start--;
            }
            var own = lines.Skip(start).Select(l => (Key: CountOf(l) ?? int.MaxValue, Line: l));
            var theirs = added.Select((a, i) => (Key: a.Count ?? int.MaxValue, Index: i, Line: a.Line));
            // The game's own lines keep their order among themselves, as one with a count written
            // as a word has none to sort by.
            var result = lines.Take(start).ToList();
            var queue = theirs.OrderBy(t => t.Key).ThenBy(t => t.Index).ToList();
            foreach (var o in own)
            {
                while (queue.Count > 0 && o.Key != int.MaxValue && queue[0].Key < o.Key)
                {
                    result.Add(queue[0].Line);
                    queue.RemoveAt(0);
                }
                result.Add(o.Line);
            }
            result.AddRange(queue.Select(q => q.Line));
            return result;
        }
    }
}
