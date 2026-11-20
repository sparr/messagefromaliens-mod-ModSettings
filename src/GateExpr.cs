using System;
using System.Collections.Generic;
using System.Linq;

namespace ModSettings
{
    // Gate tokens that combine gates: inside the brackets, "|" means "or" and "+" means "and", and
    // "and" binds first, with no parentheses. "[a+b|c]" passes when a and b both pass, or when c
    // passes. Each term is a gate as the game reads one: a page name with its words joined by "-",
    // or "<chapter>-<quiz>-<n>" for a count of the chapter's won quizzes, with <quiz> the game's
    // word for quiz. This file uses nothing from the game, so a test harness can build it on its
    // own.
    internal static class GateExpr
    {
        internal const char Or = '|';
        internal const char And = '+';

        // True when the token, with or without its brackets, combines gates.
        internal static bool IsCompound(string gate) =>
            gate != null && (gate.IndexOf(Or) >= 0 || gate.IndexOf(And) >= 0);

        // The gate's alternatives, each a list of terms that must all pass, or null when a term
        // is empty, as in "a||b" or "a+". Takes the gate with or without its brackets.
        internal static List<List<string>> Parse(string gate)
        {
            if (gate == null)
            {
                return null;
            }
            var inner = gate.StartsWith("[") && gate.EndsWith("]") ? gate.Substring(1, gate.Length - 2) : gate;
            var alternatives = new List<List<string>>();
            foreach (var alternative in inner.Split(Or))
            {
                var terms = alternative.Split(And).Select(t => t.Trim()).ToList();
                if (terms.Any(t => t.Length == 0))
                {
                    return null;
                }
                alternatives.Add(terms);
            }
            return alternatives;
        }

        // True when some alternative's terms all pass. A gate that does not parse never passes,
        // as the game's own check never passes such a token.
        internal static bool Evaluate(string gate, Func<string, bool> passes)
        {
            var alternatives = Parse(gate);
            return alternatives != null && alternatives.Any(a => a.All(passes));
        }
    }
}
