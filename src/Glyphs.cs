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
    //                                     in the .cfg file (optional for bool)
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
        // Draw.Write draws no glyph and takes no cell for a token starting with [. Only one that
        // starts a line after a newline is a gate, so at the start of a page name it is inert.
        internal const string KeyTag = "[modsettings]";
        // Pages that list one setting's choices get their own tag, so they never share a key with
        // a page of settings of the same name.
        internal const string ChoicesKeyTag = "[modsettings-choices]";

        internal sealed class Mod
        {
            internal List<Setting> Settings;
            // The mod's own page, whose rows are settings and links to its other pages, and whose
            // title is the mod's name.
            internal Page Root;
        }

        // A page of settings: the mod's own, or one of its pages opened from a line on it.
        internal sealed class Page
        {
            // The page's name, as the game names pages, and as glyphs (for pages opened from a line).
            internal string Title;
            internal List<string> Words;
            // The name the page is opened under: its title after a token the game does not draw.
            // The game runs its own code for some pages by name, and no game page has this one.
            internal string Key => (Chooser == null ? KeyTag : ChoicesKeyTag) + " " + Title;
            internal readonly List<Row> Rows = new List<Row>();
            // The setting whose choices this page lists, instead of rows, when they do not fit on
            // the setting's row.
            internal Setting Chooser;
        }

        // One row of a page: a setting, a line opening another page, or both, for a setting whose
        // choices are on a page of their own.
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
            // The words of the page the setting is on, or empty for the mod's own page.
            internal List<string> Page;
        }

        // Mods that name themselves in glyphs and have at least one setting to show, by English name.
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
                    // Plugins that name themselves alike, such as two in one assembly, share a
                    // page, since pages are found by their names.
                    var title = string.Join(" ", words);
                    var mod = mods.FirstOrDefault(m => m.Root.Title == title);
                    if (mod == null)
                    {
                        mods.Add(mod = new Mod { Root = new Page { Title = title }, Settings = new List<Setting>() });
                    }
                    mod.Settings.AddRange(settings);
                }
            }
            foreach (var mod in mods)
            {
                Build(mod);
            }
            return mods;
        }

        // Lays out a mod's pages: its own page gets each setting with no page, and one line per
        // page, where that page's first setting is.
        private static void Build(Mod mod)
        {
            var pages = new Dictionary<string, Page>();
            foreach (var setting in mod.Settings)
            {
                var row = new Row { Setting = setting };
                if (setting.Choices != null && (setting.Choices.Count > MaxLabel || setting.Label.Count + 1 + setting.Choices.Count > MaxRowCells))
                {
                    row.Link = new Page { Title = string.Join(" ", setting.Label), Words = setting.Label, Chooser = setting };
                }
                if (setting.Page.Count == 0)
                {
                    mod.Root.Rows.Add(row);
                    continue;
                }
                var title = string.Join(" ", setting.Page);
                if (!pages.TryGetValue(title, out var page))
                {
                    pages[title] = page = new Page { Title = title, Words = setting.Page };
                    mod.Root.Rows.Add(new Row { Link = page });
                }
                page.Rows.Add(row);
            }
        }

        // Problems are logged once each, since discovery runs again on every visit to the menu.
        private static readonly HashSet<string> warned = new HashSet<string>();

        internal static void Warn(string key, string message)
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
        // the ones it doesn't draw: tokens starting with ( or [, such as the value token it puts
        // before a number's last glyph. Each token kept takes one cell, and each but the empty
        // cell draws one glyph.
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
            // A ( token anywhere in a line turns off the game's number parsing for all of it.
            var kept = string.Join(" ", phrase.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Where(t => !t.StartsWith("(")));
            return kept == ""
                ? new List<string>()
                : Global.StringToListString(kept).Where(w => w != "" && !w.StartsWith("(") && !w.StartsWith("[")).ToList();
        }

        // The cells of one line of a page, as Draw.Write lays it out. A label is cut to one line.
        internal const int MaxLabel = 12;
        // The cells of a row two lines long: a row too long for one line continues on the next,
        // one cell in. A setting with more choices than fit on a line, or whose label, empty cell,
        // and choices take more than two lines, lists its choices on a page of their own.
        internal const int MaxRowCells = 2 * MaxLabel - 1;

        // The empty cell: it takes a cell but draws no glyph.
        internal const string Gap = ".";

        // Tokens Draw.Write draws as something other than one cell's glyph, which a setting's row
        // could not be laid out with: line and page breaks, buttons, and underline brackets.
        private static bool Unsupported(string token) =>
            token == "newline" || token == "newpage" || token.StartsWith("*") || token.StartsWith("{") || token.StartsWith("}");

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
            var words = Words(label);
            if (words.Any(Unsupported))
            {
                throw new FormatException("its label holds a line break, button, or bracket token");
            }
            var quoted = tags.TryGetValue(QuotedKey, out var q) && string.Equals(q?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
            var page = tags.TryGetValue(PageKey, out var p) ? Words(p) : new List<string>();
            var choices = Choices(entry, tags);
            // Every label is cut to one line, so each setting's row takes at most two lines.
            if (words.Count > MaxLabel)
            {
                words = words.Take(MaxLabel).ToList();
            }
            return new Setting { Entry = entry, Label = words, Quoted = quoted, Choices = choices, Page = page };
        }

        // True when this gate token passes, as the game checks the gate at the start of a menu
        // line. Like the game, only the first token counts. Unlike the game, a setting's gate
        // counts on its page's first row too. Global.GetWin takes the token without its brackets.
        // A gate that could never pass, with a count above what its chapter can reach, is an error.
        private static bool Unlocked(string gate)
        {
            var token = (gate ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            var page = token.TrimStart('[').TrimEnd(']');
            UnlockMessages.CheckCounts(page);
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
                // A flags value can hold several flags at once, which single choices cannot show.
                if (type.IsDefined(typeof(FlagsAttribute), false))
                {
                    return null;
                }
                // Members that share a value are one choice.
                values = Enum.GetValues(type).Cast<object>().Distinct();
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
                // An enum value's tag may use any of the names that share the value.
                var keys = type.IsEnum
                    ? Enum.GetNames(type).Where(n => Equals(Enum.Parse(type, n), value)).Prepend(TomlTypeConverter.ConvertToString(value, type))
                    : new[] { TomlTypeConverter.ConvertToString(value, type) };
                string glyph = null;
                if (!keys.Any(k => tags.TryGetValue(ValuePrefix + k, out glyph)))
                {
                    glyph = value is bool b ? (b ? "yes" : "no") : null;
                }
                var words = Words(glyph);
                if (words.Count != 1 || words[0] == Gap || Unsupported(words[0]))
                {
                    // Every choice needs exactly one drawn glyph; otherwise edit the value as text.
                    return null;
                }
                choices.Add((words[0], value));
            }
            return choices;
        }
    }
}
