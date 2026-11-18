using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    // Words are the game's word IDs separated by spaces, as in its text files. Only standard
    // types are involved, so mods need no reference to this one.
    internal static class Glyphs
    {
        internal const string NameKey = "MessageFromAliens.Name";
        internal const string LabelKey = "MessageFromAliens.Label";
        internal const string ValuePrefix = "MessageFromAliens.Value.";

        internal sealed class Mod
        {
            internal string Name;
            internal string Title;
            internal List<Setting> Settings;
        }

        internal sealed class Setting
        {
            internal ConfigEntryBase Entry;
            internal List<string> Label;
            // One entry per choice glyph.
            internal List<(string Glyph, object Value)> Choices;
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
                var name = plugin.GetType().Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                    .FirstOrDefault(a => a.Key == NameKey)?.Value;
                var words = Words(name);
                if (words.Count == 0)
                {
                    continue;
                }
                var settings = new List<Setting>();
                foreach (var pair in plugin.Config)
                {
                    var setting = Read(pair.Value);
                    if (setting != null)
                    {
                        settings.Add(setting);
                    }
                }
                if (settings.Count > 0)
                {
                    mods.Add(new Mod { Name = name, Title = string.Join(" ", words), Settings = settings });
                }
            }
            return mods;
        }

        // The game's own tokens for a phrase, as it stores the lines of its text files, leaving out
        // the ones it doesn't draw: tokens starting with ( or [, such as the value token it adds
        // after a number. Each token kept is one drawn glyph.
        internal static List<string> Words(string phrase) =>
            string.IsNullOrWhiteSpace(phrase)
                ? new List<string>()
                : Global.StringToListString(phrase.Trim()).Where(w => w != "" && !w.StartsWith("(") && !w.StartsWith("[")).ToList();

        private static Setting Read(ConfigEntryBase entry)
        {
            var tags = entry.Description?.Tags?.OfType<IDictionary<string, string>>().FirstOrDefault();
            if (tags == null || !tags.TryGetValue(LabelKey, out var label) || Words(label).Count == 0)
            {
                return null;
            }
            var choices = Choices(entry, tags);
            return choices == null ? null : new Setting { Entry = entry, Label = Words(label), Choices = choices };
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
                    // Every choice needs exactly one glyph; otherwise the setting is not shown.
                    return null;
                }
                choices.Add((words[0], value));
            }
            return choices;
        }
    }
}
