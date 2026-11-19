using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx.Bootstrap;
using BepInEx.Configuration;

namespace ModSettings
{
    // A mod names itself in glyphs with an assembly attribute:
    //   [assembly: AssemblyMetadata("MessageFromAliens.Name", "<words>")]
    // and shows a setting by tagging its ConfigDescription with a Dictionary<string, string>:
    //   "MessageFromAliens.Label"         the setting's name in glyphs (required)
    //   "MessageFromAliens.Value.<value>" the glyph for one value, keyed by the value as written
    //                                     in the .cfg file (optional; bool defaults to yes/no)
    //   "MessageFromAliens.Page"          a page's name in glyphs (optional): settings with the
    //                                     same page are shown on their own page, opened from one
    //                                     line on the mod's page where the first of them would be
    //   "MessageFromAliens.Quoted"        "true" if the .cfg file wraps the value in double quotes,
    //                                     which the text field leaves out (optional)
    //   "MessageFromAliens.Gate"          a gate token, as the game's text files put at the start
    //                                     of a menu line: the setting is shown only once the gate
    //                                     passes, wherever its row falls (optional)
    // Words are the game's word IDs separated by spaces, as in its text files. Only standard
    // types are involved, so mods need no reference to this one.
    internal static class Glyphs
    {
        internal const string NameKey = "MessageFromAliens.Name";
        internal const string LabelKey = "MessageFromAliens.Label";
        internal const string ValuePrefix = "MessageFromAliens.Value.";
        internal const string QuotedKey = "MessageFromAliens.Quoted";
        internal const string PageKey = "MessageFromAliens.Page";
        internal const string GateKey = "MessageFromAliens.Gate";

        internal sealed class Mod
        {
            internal string Title;
            internal List<Setting> Settings;
            // The mod's own page, whose rows are settings and links to its other pages.
            internal Page Root;
        }

        // A page of settings: the mod's own, or one of its pages opened from a line on it.
        internal sealed class Page
        {
            internal Mod Mod;
            // The page's name, as the game names pages, and as glyphs.
            internal string Title;
            internal List<string> Words;
            internal readonly List<Row> Rows = new List<Row>();
        }

        // One row of a page: a setting, or a line opening another page.
        internal sealed class Row
        {
            internal Setting Setting;
            internal Page Link;
        }

        internal sealed class Setting
        {
            internal ConfigEntryBase Entry;
            internal List<string> Label;
            // The .cfg form wraps the value in double quotes; edit it without them.
            internal bool Quoted;
            // One entry per choice glyph, or null for a setting edited as text.
            internal List<(string Glyph, object Value)> Choices;
            // Where the text field starts, in lines and cells from the start of the setting's row.
            internal int FieldLine, FieldColumn;
            // The words of the page the setting is on, or empty for the mod's own page.
            internal List<string> Page;
        }

        // Mods that name themselves in glyphs and have at least one tagged setting, by English name.
        internal static List<Mod> Mods()
        {
            var mods = new List<Mod>();
            foreach (var info in Chainloader.PluginInfos.Values.OrderBy(i => i.Metadata.Name, StringComparer.OrdinalIgnoreCase))
            {
                var plugin = info.Instance;
                if (plugin == null)
                {
                    continue;
                }
                List<string> words;
                try
                {
                    var name = plugin.GetType().Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                        .FirstOrDefault(a => a.Key == NameKey)?.Value;
                    words = Words(name);
                }
                catch (Exception e)
                {
                    Warn(info.Metadata.Name, $"{info.Metadata.Name}: its name in glyphs can't be read, so it is not listed: {e.Message}");
                    continue;
                }
                if (words.Count == 0)
                {
                    continue;
                }
                var settings = new List<Setting>();
                foreach (var pair in plugin.Config)
                {
                    Setting setting;
                    try
                    {
                        setting = Read(pair.Value);
                    }
                    catch (Exception e)
                    {
                        Warn(info.Metadata.Name + "/" + pair.Key, $"{info.Metadata.Name}: {pair.Key}'s tags can't be read, so it is not shown: {e.Message}");
                        continue;
                    }
                    if (setting != null)
                    {
                        settings.Add(setting);
                    }
                }
                if (settings.Count > 0)
                {
                    mods.Add(Build(new Mod { Title = string.Join(" ", words), Settings = settings }, words));
                }
            }
            return mods;
        }

        // Lays out a mod's pages: its own page gets each setting with no page, and one line per
        // page, where that page's first setting is.
        private static Mod Build(Mod mod, List<string> words)
        {
            mod.Root = new Page { Mod = mod, Title = mod.Title, Words = words };
            var pages = new Dictionary<string, Page>();
            foreach (var setting in mod.Settings)
            {
                if (setting.Page.Count == 0)
                {
                    mod.Root.Rows.Add(new Row { Setting = setting });
                    continue;
                }
                var title = string.Join(" ", setting.Page);
                if (!pages.TryGetValue(title, out var page))
                {
                    pages[title] = page = new Page { Mod = mod, Title = title, Words = setting.Page };
                    mod.Root.Rows.Add(new Row { Link = page });
                }
                page.Rows.Add(new Row { Setting = setting });
            }
            return mod;
        }

        // Problems are logged once each, since discovery runs again on every visit to the menu.
        private static readonly HashSet<string> warned = new HashSet<string>();

        private static void Warn(string key, string message)
        {
            if (warned.Add(key))
            {
                Plugin.Log.LogWarning(message);
            }
        }

        // A number token with an exponent this large makes the game's parser loop forever.
        private const int MaxExponent = 300;
        private static readonly Regex Exponent = new Regex(@"^-?\d+(?:\.\d+)?(?:e|E|x|A|F|DS|D)(-?\d+)");

        // The game's own tokens for a phrase, as it stores the lines of its text files, leaving out
        // the ones it doesn't draw: tokens starting with ( or [, such as the value token it adds
        // after a number. Each token kept is one drawn glyph.
        // Throws for a phrase the game's parser rejects.
        internal static List<string> Words(string phrase)
        {
            if (string.IsNullOrWhiteSpace(phrase))
            {
                return new List<string>();
            }
            foreach (var token in phrase.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var m = Exponent.Match(token);
                if (m.Success && (!int.TryParse(m.Groups[1].Value, out var k) || Math.Abs(k) > MaxExponent))
                {
                    throw new FormatException($"a number's exponent is larger than {MaxExponent}");
                }
            }
            return Global.StringToListString(phrase.Trim()).Where(w => w != "" && !w.StartsWith("(") && !w.StartsWith("[")).ToList();
        }

        private static Setting Read(ConfigEntryBase entry)
        {
            var tags = entry.Description?.Tags?.OfType<IDictionary<string, string>>().FirstOrDefault();
            if (tags == null || !tags.TryGetValue(LabelKey, out var label) || Words(label).Count == 0)
            {
                return null;
            }
            if (tags.TryGetValue(GateKey, out var gate) && !Unlocked(gate))
            {
                return null;
            }
            var quoted = tags.TryGetValue(QuotedKey, out var q) && string.Equals(q?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
            var page = tags.TryGetValue(PageKey, out var p) ? Words(p) : new List<string>();
            return new Setting { Entry = entry, Label = Words(label), Quoted = quoted, Choices = Choices(entry, tags), Page = page };
        }

        // True when this gate token passes, as the game checks the gate at the start of a menu
        // line. Like the game, only the first token counts. Unlike the game, a setting's gate
        // counts on its page's first row too. Global.GetWin takes the token without its brackets.
        private static bool Unlocked(string gate)
        {
            var token = (gate ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            var page = token.TrimStart('[').TrimEnd(']');
            return page == "" || Global.GetWin(page);
        }

        private static List<(string, object)> Choices(ConfigEntryBase entry, IDictionary<string, string> tags)
        {
            var type = entry.SettingType;
            IEnumerable<object> values;
            if (type == typeof(bool))
            {
                values = new object[] { true, false };
            }
            else if (type.IsEnum)
            {
                values = Enum.GetValues(type).Cast<object>();
            }
            else if (entry.Description.AcceptableValues?.GetType().GetProperty("AcceptableValues")?.GetValue(entry.Description.AcceptableValues) is Array list)
            {
                values = list.Cast<object>();
            }
            else
            {
                return null;
            }
            var choices = new List<(string, object)>();
            foreach (var value in values)
            {
                var key = TomlTypeConverter.ConvertToString(value, type);
                if (!tags.TryGetValue(ValuePrefix + key, out var glyph))
                {
                    glyph = value is bool b ? (b ? "yes" : "no") : null;
                }
                var words = Words(glyph);
                if (words.Count != 1)
                {
                    // Every choice needs exactly one glyph; otherwise edit the value as text.
                    return null;
                }
                choices.Add((words[0], value));
            }
            return choices;
        }
    }
}
