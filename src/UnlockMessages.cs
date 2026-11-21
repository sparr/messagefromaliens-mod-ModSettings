using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace ModSettings
{
    // Mods declare the words for a feature that unlocks with a gate:
    //   [assembly: AssemblyMetadata("MessageFromAliens.Unlock", "[<gate>] <feature>")]
    //   [assembly: AssemblyMetadata("MessageFromAliens.Unlock.<page>", "[<gate>] <feature>")]
    // or the same keys in a setting's tags, where the gate token may be left out to use the
    // setting's own gate. From these, Mod Settings writes the game's own two sentences:
    // - "win <n> <chapter> <quiz> unlock <feature>", in every chapter's explanation whose quizzes a
    //   term of the gate counts, or in <page>'s explanation, always shown, as the game's
    //   explanations end with such lines. One line per alternative of the gate; the terms of an
    //   alternative are joined by "and".
    // - "<you now unlock> <feature>" on each win count page, "<chapter> unlock <n>", for a count
    //   term. The game shows that page in the left panel when the chapter's count of wins reaches n.
    //   The line shows whenever the game would show the page, and the rest of its alternative
    //   passes. Pages the game has no text for are added to its text, so the game shows them and
    //   other mods find them as they find the game's own.
    internal static class UnlockMessages
    {
        internal const string Key = "MessageFromAliens.Unlock";
        private const string ExplainSuffix = " de tutorial";
        // The text the game reads explanations and win count pages from.
        private const string Category = "alieninfo";

        private sealed class ExplainLine
        {
            internal int? Count;
            // The line as the game stores its text's lines.
            internal string Text;
        }

        private sealed class PageLine
        {
            internal string Text;
            // The count term the page is for, and the alternative's other terms.
            internal string Chapter;
            internal int Count;
            internal List<string> Others;
        }

        // Lines for explanations and for win count pages, by page name as the game stores it.
        private static Dictionary<string, List<ExplainLine>> explain;
        private static Dictionary<string, List<PageLine>> counts;
        // Win count pages this mod added to the game's text, which has no lines of its own for them,
        // and the part of the game's text they were added to.
        private static HashSet<string> added = new HashSet<string>();
        private static Dictionary<string, string> addedTo;
        // What the lines were found from: the game's text, and the loaded plugins.
        private static object builtFor;
        private static int builtPlugins;

        // Every plugin has loaded by the time the game reads its text, so the win count pages are
        // in the game's text before anything looks for them.
        [HarmonyPatch(typeof(Global), nameof(Global.Init))]
        private static class InitPatch
        {
            private static void Postfix()
            {
                try
                {
                    Build();
                }
                catch (Exception e)
                {
                    Glyphs.Warn("unlock messages", $"Unlock messages failed: {e}");
                }
            }
        }

        [HarmonyPatch(typeof(Global), nameof(Global.GetInfo))]
        private static class GetInfoPatch
        {
            private static void Postfix(string category, string s, ref string __result)
            {
                if (category != Category || s == null || !(s.EndsWith(ExplainSuffix) || s.Contains(" " + UnlockText.Unlock + " ")))
                {
                    return;
                }
                try
                {
                    Build();
                    if (explain.Count == 0 && counts.Count == 0)
                    {
                        return;
                    }
                    var key = Global.ParseString(s, modify: false);
                    if (!string.IsNullOrEmpty(__result) && explain.TryGetValue(key, out var lines))
                    {
                        var own = Lines(__result);
                        var merged = UnlockText.Merge(own, lines.Select(l => (l.Count, Tokens(l.Text))));
                        __result = string.Join(" newline ", merged.Select(l => string.Join(" ", l)));
                    }
                    if (counts.TryGetValue(key, out var page))
                    {
                        var shown = new List<string>();
                        if (!added.Contains(key) && !string.IsNullOrEmpty(__result))
                        {
                            shown.Add(__result);
                        }
                        shown.AddRange(page.Where(Shown).Select(l => l.Text).Distinct());
                        __result = string.Join(" newline ", shown);
                    }
                }
                catch (Exception e)
                {
                    Glyphs.Warn("unlock messages", $"Unlock messages failed: {e}");
                }
            }
        }

        // The game shows a win count page when the chapter's count of wins, as Global.CountWin
        // counts them, reaches the page's count. That count takes in every won page whose name
        // starts with the chapter's: its quizzes, and its menu once every line on the menu is won.
        // Until then it equals the count of won quizzes that the gate term checks. The line follows
        // the page, as the game's own lines on the page do.
        private static bool Shown(PageLine line) =>
            Global.savedata?.win != null && Global.CountWin(line.Chapter) >= line.Count && line.Others.All(Global.GetWin);

        private static List<string> Tokens(string text) =>
            text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        private static List<List<string>> Lines(string text)
        {
            var lines = new List<List<string>> { new List<string>() };
            foreach (var token in Tokens(text))
            {
                if (token == "newline")
                {
                    lines.Add(new List<string>());
                }
                else
                {
                    lines[lines.Count - 1].Add(token);
                }
            }
            return lines;
        }

        private static void Build()
        {
            var messages = Global.alien_message;
            var plugins = Chainloader.PluginInfos.Count;
            if (explain != null && ReferenceEquals(messages, builtFor) && plugins == builtPlugins)
            {
                return;
            }
            builtFor = messages;
            builtPlugins = plugins;
            explain = new Dictionary<string, List<ExplainLine>>();
            counts = new Dictionary<string, List<PageLine>>();
            if (messages == null)
            {
                return;
            }
            var lines = 0;
            var assemblies = new HashSet<Assembly>();
            foreach (var info in Chainloader.PluginInfos.Values.OrderBy(i => i.Metadata.Name, StringComparer.OrdinalIgnoreCase))
            {
                var plugin = info.Instance;
                if (plugin == null)
                {
                    continue;
                }
                var name = info.Metadata.Name;
                var declared = new List<(string Key, string Value, string Gate, string Where)>();
                try
                {
                    if (assemblies.Add(plugin.GetType().Assembly))
                    {
                        declared.AddRange(plugin.GetType().Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                            .Where(a => IsKey(a.Key)).Select(a => (a.Key, a.Value, (string)null, "assembly")));
                    }
                    foreach (var pair in plugin.Config)
                    {
                        var tags = pair.Value.Description?.Tags?.OfType<IDictionary<string, string>>().FirstOrDefault();
                        if (tags == null)
                        {
                            continue;
                        }
                        tags.TryGetValue(Glyphs.GateKey, out var gate);
                        declared.AddRange(tags.Where(t => IsKey(t.Key)).Select(t => (t.Key, t.Value, gate, pair.Key.ToString())));
                    }
                }
                catch (Exception e)
                {
                    Glyphs.Warn("unlock " + name, $"{name}: its unlock messages can't be read: {e.Message}");
                    continue;
                }
                foreach (var (key, value, gate, where) in declared)
                {
                    try
                    {
                        lines += Add(messages, key, value, gate);
                    }
                    catch (Exception e)
                    {
                        Glyphs.Warn("unlock " + name + "/" + where + "/" + key + "/" + value, $"{name}: an unlock message in {where} is not shown: {e.Message}");
                    }
                }
            }
            AddPages(messages);
            if (lines > 0)
            {
                Plugin.Log.LogInfo($"Added {lines} unlock message lines, and {added.Count} win count pages the game has no text for");
            }
        }

        // Puts each win count page that has lines but no text in the game's text, so the game shows
        // it, and mods that list the game's win count pages list it. Its text there is every line
        // it can show; GetInfo gives only the lines shown.
        private static void AddPages(Dictionary<string, Dictionary<string, string>> messages)
        {
            if (!messages.TryGetValue(Category, out var text))
            {
                return;
            }
            if (!ReferenceEquals(text, addedTo))
            {
                addedTo = text;
                added = new HashSet<string>();
            }
            foreach (var key in added.Where(k => !counts.ContainsKey(k)).ToList())
            {
                text.Remove(key);
                added.Remove(key);
            }
            foreach (var pair in counts)
            {
                if (added.Contains(pair.Key) || !Exists(messages, pair.Key))
                {
                    text[pair.Key] = string.Join(" newline ", pair.Value.Select(l => l.Text).Distinct());
                    added.Add(pair.Key);
                }
            }
        }

        private static bool IsKey(string key) => key == Key || (key != null && key.StartsWith(Key + ".") && key.Length > Key.Length + 1);

        // Adds one declaration's lines, and returns how many were added.
        private static int Add(Dictionary<string, Dictionary<string, string>> messages, string key, string value, string settingGate)
        {
            var tokens = (value ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            string gate;
            if (tokens.Count > 0 && tokens[0].StartsWith("["))
            {
                gate = tokens[0];
                tokens.RemoveAt(0);
            }
            else
            {
                gate = settingGate?.Trim();
            }
            if (string.IsNullOrEmpty(gate))
            {
                throw new FormatException("it has no gate token, and its setting has no gate");
            }
            if (gate.Length < 3 || !gate.StartsWith("[") || !gate.EndsWith("]") || gate.Contains(" ") || gate.IndexOf(']') != gate.Length - 1)
            {
                throw new FormatException("its gate is not one token in brackets");
            }
            var alternatives = GateExpr.Parse(gate);
            if (alternatives == null)
            {
                throw new FormatException($"its gate has an empty term, around a {GateExpr.Or} or {GateExpr.And}");
            }
            CheckCounts(gate);

            string page = null;
            if (key != Key)
            {
                page = Global.ParseString(key.Substring(Key.Length + 1).Trim() + ExplainSuffix, modify: false);
                if (!Exists(messages, page))
                {
                    throw new FormatException("the page it names has no explanation");
                }
            }

            if (tokens.Any(t => t == "newline" || t == "newpage" || t.StartsWith("*") || t.StartsWith("{") || t.StartsWith("}") || t.StartsWith("[") || t.StartsWith("(")))
            {
                throw new FormatException("its words hold a line or page break, button, bracket, or second gate token");
            }
            var feature = string.Join(" ", tokens);
            if (Glyphs.Words(feature).Count == 0)
            {
                throw new FormatException("it has no words");
            }

            var count = 0;
            foreach (var alternative in alternatives)
            {
                var terms = alternative.Select(UnlockText.ReadTerm).ToList();
                // Stored as Global.Init stores a line of its text files.
                var line = Stored(UnlockText.ExplainLine(terms, feature));
                if (page != null)
                {
                    count += Put(page, terms.FirstOrDefault(t => t.IsCount)?.Count, line);
                }
                else
                {
                    var chapters = terms.Where(t => t.IsCount).Select(t => (Term: t, Page: Chapter(messages, t))).ToList();
                    if (chapters.Count == 0)
                    {
                        throw new FormatException($"an alternative of its gate counts no chapter's quizzes; name the page with {Key}.<page>");
                    }
                    var missing = chapters.FirstOrDefault(c => c.Page == null);
                    if (missing.Term != null)
                    {
                        throw new FormatException($"no chapter's explanation is named for the quizzes its gate counts; name the page with {Key}.<page>");
                    }
                    foreach (var (term, chapter) in chapters)
                    {
                        count += Put(chapter, term.Count, line);
                    }
                }
                var pageLine = Stored(UnlockText.PageLine(feature));
                for (var i = 0; i < terms.Count; i++)
                {
                    if (!terms[i].IsCount)
                    {
                        continue;
                    }
                    var winPage = Global.ParseString(terms[i].Chapter + " " + UnlockText.Unlock + " " + terms[i].Count, modify: false);
                    if (!counts.TryGetValue(winPage, out var list))
                    {
                        counts[winPage] = list = new List<PageLine>();
                    }
                    var others = alternative.Where((_, j) => j != i).ToList();
                    if (!list.Any(l => l.Text == pageLine && l.Others.SequenceEqual(others)))
                    {
                        list.Add(new PageLine { Text = pageLine, Chapter = terms[i].Chapter, Count = terms[i].Count, Others = others });
                    }
                }
            }
            return count;
        }

        // Rejects a gate with a count term above the most wins Global.GetWin can count for it: one
        // for each page whose name starts as the term's chapter and "<quiz>" do, which are the
        // chapter's quizzes, and its menu when the menu is named so. Explanations are never won.
        internal static void CheckCounts(string gate)
        {
            var alternatives = GateExpr.Parse(gate);
            if (alternatives == null || Global.alien_message == null)
            {
                return;
            }
            foreach (var term in alternatives.SelectMany(a => a).Select(UnlockText.ReadTerm).Where(t => t.IsCount))
            {
                var max = MaxCount(term.Chapter);
                if (term.Count > max)
                {
                    throw new FormatException($"a term of its gate counts {term.Count} wins of a chapter that has only {max} pages it can count, so it would never pass");
                }
            }
        }

        private static object countedFor;
        private static readonly Dictionary<string, int> maxCounts = new Dictionary<string, int>();

        private static int MaxCount(string chapter)
        {
            var messages = Global.alien_message;
            if (!ReferenceEquals(messages, countedFor))
            {
                countedFor = messages;
                maxCounts.Clear();
            }
            if (maxCounts.TryGetValue(chapter, out var max))
            {
                return max;
            }
            var prefix = chapter.Replace(" ", "-") + "-" + UnlockText.QuizWord;
            var names = new HashSet<string>(messages.Values.SelectMany(c => c.Keys));
            names.UnionWith(Global.link_message?.Keys ?? Enumerable.Empty<string>());
            names.UnionWith(Global.quiz_supply?.Keys ?? Enumerable.Empty<string>());
            max = names.Count(n => !n.EndsWith(ExplainSuffix) && n.Replace(" ", "-").StartsWith(prefix));
            maxCounts[chapter] = max;
            return max;
        }

        private static string Stored(string line) => string.Join(" ", Global.StringToListString(line));

        // Lines already there are not added again, as when two settings declare the same message.
        private static int Put(string page, int? count, string text)
        {
            if (!explain.TryGetValue(page, out var list))
            {
                explain[page] = list = new List<ExplainLine>();
            }
            if (list.Any(l => l.Text == text))
            {
                return 0;
            }
            list.Add(new ExplainLine { Count = count, Text = text });
            return 1;
        }

        // The explanation of the chapter whose quizzes a count term counts: the chapter named as the
        // quizzes' names start, or that and "<quiz>", for a chapter named so.
        private static string Chapter(Dictionary<string, Dictionary<string, string>> messages, UnlockText.Term term) =>
            new[] { term.Chapter, term.Chapter + " " + UnlockText.QuizWord }
                .Select(c => Global.ParseString(c + ExplainSuffix, modify: false))
                .FirstOrDefault(c => Exists(messages, c));

        private static bool Exists(Dictionary<string, Dictionary<string, string>> messages, string key) =>
            messages.Values.Any(c => c.TryGetValue(key, out var text) && !string.IsNullOrEmpty(text));
    }
}
