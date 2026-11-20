using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace ModSettings
{
    // Mods declare a line of glyphs that tells the player a feature has unlocked, with the gate
    // token that unlocks it:
    //   [assembly: AssemblyMetadata("MessageFromAliens.Unlock", "[<gate>] <words>")]
    //   [assembly: AssemblyMetadata("MessageFromAliens.Unlock.<page>", "[<gate>] <words>")]
    // or the same keys in a setting's tags, where the gate token may be left out to use the
    // setting's own gate. The line is added to the end of the explanation of the chapter whose
    // quizzes the gate counts, or of <page>, under its gate token, so the game draws it only once
    // the gate passes. A gate that counts a chapter's quizzes also adds the line, without its gate,
    // to the page the game shows in the left panel when the chapter's wins reach that count.
    internal static class UnlockMessages
    {
        internal const string Key = "MessageFromAliens.Unlock";
        private const string ExplainSuffix = " de tutorial";
        // The game's word for quiz, before the number in quiz pages' names; <quiz> in comments.
        private const string QuizWord = "riddle";
        private const string UnlockWord = "unlock";

        // "<chapter>-<quiz>-<n>", which Global.GetWin passes once n of the chapter's quizzes are won.
        private static readonly Regex CountGate = new Regex(@"^(.+)-" + QuizWord + @"-(\d+)$");

        private sealed class Message
        {
            internal string Gate;
            // The line as the game stores its text's lines, without the gate token.
            internal string Text;
        }

        // Lines for explanations and for win count pages, by page name as the game stores it.
        private static Dictionary<string, List<Message>> explain;
        private static Dictionary<string, List<Message>> counts;
        // What the lines were found from: the game's text, and the loaded plugins.
        private static object builtFor;
        private static int builtPlugins;

        [HarmonyPatch(typeof(Global), nameof(Global.GetInfo))]
        private static class GetInfoPatch
        {
            private static void Postfix(string category, string s, ref string __result)
            {
                if (category != "alieninfo" || s == null || !(s.EndsWith(ExplainSuffix) || s.Contains(" " + UnlockWord + " ")))
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
                    // A line after the first that starts with a gate token is drawn only once it
                    // passes, so these always follow the explanation's own lines.
                    if (!string.IsNullOrEmpty(__result) && explain.TryGetValue(key, out var lines))
                    {
                        __result += string.Concat(lines.Select(m => " newline " + m.Gate + " " + m.Text));
                    }
                    // A win count page shows when the count is reached, which its gate checks too.
                    if (counts.TryGetValue(key, out lines))
                    {
                        var won = lines.Where(m => Global.GetWin(m.Gate.Substring(1, m.Gate.Length - 2))).Select(m => m.Text).ToList();
                        if (won.Count > 0)
                        {
                            __result = string.Join(" newline ", (string.IsNullOrEmpty(__result) ? won : won.Prepend(__result)));
                        }
                    }
                }
                catch (Exception e)
                {
                    Glyphs.Warn("unlock messages", $"Unlock messages failed: {e}");
                }
            }
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
            explain = new Dictionary<string, List<Message>>();
            counts = new Dictionary<string, List<Message>>();
            if (messages == null)
            {
                return;
            }
            var added = 0;
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
                        added += Add(messages, key, value, gate);
                    }
                    catch (Exception e)
                    {
                        Glyphs.Warn("unlock " + name + "/" + where + "/" + key + "/" + value, $"{name}: an unlock message in {where} is not shown: {e.Message}");
                    }
                }
            }
            if (added > 0)
            {
                Plugin.Log.LogInfo($"Added {added} unlock message lines");
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
                throw new FormatException($"its gate is not one token in brackets");
            }
            var inner = gate.Substring(1, gate.Length - 2);

            string page;
            if (key == Key)
            {
                page = Chapter(messages, inner);
                if (page == null)
                {
                    throw new FormatException($"its gate counts no chapter's quizzes; name the page with {Key}.<page>");
                }
            }
            else
            {
                page = key.Substring(Key.Length + 1).Trim();
                if (!Exists(messages, Global.ParseString(page + ExplainSuffix, modify: false)))
                {
                    throw new FormatException("the page it names has no explanation");
                }
            }

            var lines = new List<List<string>> { new List<string>() };
            foreach (var token in tokens)
            {
                if (token == "newline")
                {
                    lines.Add(new List<string>());
                    continue;
                }
                if (token == "newpage" || token.StartsWith("*") || token.StartsWith("{") || token.StartsWith("}") || token.StartsWith("["))
                {
                    throw new FormatException("it holds a page break, button, bracket, or second gate token");
                }
                lines[lines.Count - 1].Add(token);
            }
            if (lines.Any(l => Glyphs.Words(string.Join(" ", l)).Count == 0))
            {
                throw new FormatException("it has an empty line");
            }

            var count = CountGate.Match(inner);
            var countPage = count.Success ? count.Groups[1].Value.Replace("-", " ") + " " + UnlockWord + " " + count.Groups[2].Value : null;
            var added = 0;
            foreach (var line in lines)
            {
                // Stored as Global.Init stores a line of its text files.
                var message = new Message { Gate = gate, Text = string.Join(" ", Global.StringToListString(string.Join(" ", line))) };
                added += Put(explain, Global.ParseString(page + ExplainSuffix, modify: false), message);
                if (countPage != null)
                {
                    Put(counts, Global.ParseString(countPage, modify: false), message);
                }
            }
            return added;
        }

        // Lines already there are not added again, as when two settings declare the same message.
        private static int Put(Dictionary<string, List<Message>> into, string key, Message message)
        {
            if (!into.TryGetValue(key, out var list))
            {
                into[key] = list = new List<Message>();
            }
            if (list.Any(m => m.Gate == message.Gate && m.Text == message.Text))
            {
                return 0;
            }
            list.Add(message);
            return 1;
        }

        // The chapter whose quizzes a gate counts or names: the part before "-<quiz>-", whose
        // quizzes are named for it, or that and "<quiz>", for a chapter named so.
        private static string Chapter(Dictionary<string, Dictionary<string, string>> messages, string gate)
        {
            var at = gate.IndexOf("-" + QuizWord + "-", StringComparison.Ordinal);
            if (at <= 0)
            {
                return null;
            }
            var prefix = gate.Substring(0, at).Replace("-", " ");
            return new[] { prefix, prefix + " " + QuizWord }
                .FirstOrDefault(c => Exists(messages, Global.ParseString(c + ExplainSuffix, modify: false)));
        }

        private static bool Exists(Dictionary<string, Dictionary<string, string>> messages, string key) =>
            messages.Values.Any(c => c.TryGetValue(key, out var text) && !string.IsNullOrEmpty(text));
    }
}
