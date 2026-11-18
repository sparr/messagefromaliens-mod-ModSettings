using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ModSettings
{
    // Adds a row to the game's settings page that opens a menu of mods, and gives each mod a page
    // laid out like the settings page: a row per setting, its name in glyphs, then "." and its
    // choices, the current one boxed. Settings without choice glyphs get a text field instead.
    internal static class SettingsPages
    {
        // The game's settings page, and the name of its early, shorter text.
        private const string GamePage = "modify game";
        private const string GamePageEarly = "modify game 0";
        // The settings page's last row, which resets the game's settings.
        private const string ResetLine = "anti modify";
        // This mod's menu of mods, named like the game's own settings page.
        private const string ListPage = "modify variant";
        private const string TutorialSuffix = " de tutorial";
        // The line of the home page's explanation that says what clicking a line does.
        private const string ListTutorial = "left click sentence for start function";
        // The lines of the settings page's explanation that say what its rows are, about the
        // mod's attributes instead of the game's.
        private const string ModTutorial = "space de left word be variant attribute newline left click space de right word for choose option";
        // Draw.Write lays out the page in rows of RowWidth cells, LinesPerPage lines to a page.
        // A row too long for one line continues on the next, one cell in.
        private const int RowWidth = 12;
        private const int LinesPerPage = 8;
        // A text field narrower than this goes on the line after its label.
        private const int MinFieldCells = 4;
        private const int CharacterLimit = 200;

        private sealed class Choice
        {
            internal AlienCharacter Glyph;
            internal Glyphs.Setting Setting;
            internal object Value;
        }

        private sealed class Field
        {
            internal TMP_InputField Input;
            internal Glyphs.Setting Setting;
            // Slot coordinates of the field's first and last cells.
            internal int X0, X1, Y;
        }

        private static readonly List<Choice> choices = new List<Choice>();
        private static readonly List<Field> fields = new List<Field>();
        private static List<Glyphs.Mod> mods;
        private static Glyphs.Mod shown;

        private static Draw Manager => Global.manager;

        private static bool OnGamePage(Draw d) => d != null && d.current_page == GamePage;

        private static bool OnList(Draw d) =>
            d != null && d.current_page == ListPage && Parent(d, 1) == GamePage;

        // The mod whose page is open, if any, opened from the menu of mods.
        private static Glyphs.Mod OpenMod(Draw d)
        {
            if (d == null || Parent(d, 1) != ListPage || Parent(d, 2) != GamePage)
            {
                return null;
            }
            return (mods ??= Glyphs.Mods()).FirstOrDefault(m => m.Title == d.current_page);
        }

        // The page n levels above the current one.
        private static string Parent(Draw d, int n)
        {
            var stack = d.parent_page;
            return stack != null && stack.Count > n ? stack[stack.Count - 1 - n] : null;
        }

        private static string Line(IEnumerable<string> words) => string.Join(" ", words);

        // A mod page's lines: each setting's name, then its choices or room for its text field. A
        // row that would not fit in the lines left on a page starts the next page instead, so no
        // row is split between pages. Also the setting of each line, as Draw.Write numbers lines,
        // or -1 for a line that only starts a page.
        private static (string Text, List<int> Rows) PageRows(Glyphs.Mod mod)
        {
            var text = new StringBuilder();
            var rows = new List<int>();
            var used = 0;
            for (var i = 0; i < mod.Settings.Count; i++)
            {
                var tokens = RowTokens(mod.Settings[i]);
                var lines = Lines(tokens.Count);
                if (i > 0)
                {
                    text.Append(" newline ");
                    if (used >= LinesPerPage)
                    {
                        used = 0;
                    }
                    else if (used + lines > LinesPerPage)
                    {
                        // Its own line, after the newline, so that mods splitting the text into
                        // rows at each newline, such as Interface Tweaks, see whole rows.
                        text.Append("newpage ");
                        rows.Add(-1);
                        used = 0;
                    }
                }
                text.Append(Line(tokens));
                rows.Add(i);
                used += lines;
            }
            return (text.ToString(), rows);
        }

        // A setting's row: its name, an empty cell, then its choices, or the empty cells its text
        // field covers. The field fills the rest of the line, or the next line when too little of
        // it is left.
        private static List<string> RowTokens(Glyphs.Setting setting)
        {
            var tokens = setting.Label.Append(".").ToList();
            if (setting.Choices != null)
            {
                tokens.AddRange(setting.Choices.Select(c => c.Glyph));
                return tokens;
            }
            var (line, column) = After(tokens.Count);
            if (RowWidth - column < MinFieldCells)
            {
                tokens.AddRange(Enumerable.Repeat(".", RowWidth - column + RowWidth - 1));
                line++;
                column = 1;
            }
            setting.FieldLine = line;
            setting.FieldColumn = column;
            return tokens;
        }

        // The lines a row of this many cells takes.
        private static int Lines(int cells) => cells <= RowWidth ? 1 : 2 + (cells - RowWidth - 1) / (RowWidth - 1);

        // The line and cell after a row's first cells, counted from the row's start.
        private static (int Line, int Column) After(int cells) =>
            cells <= RowWidth ? (0, cells) : (1 + (cells - RowWidth - 1) / (RowWidth - 1), 2 + (cells - RowWidth - 1) % (RowWidth - 1));

        [HarmonyPatch(typeof(Draw), nameof(Draw.ToPage))]
        private static class ToPagePatch
        {
            // Discover mods again on each visit to the menu, so the list matches what is loaded.
            private static void Prefix(string s)
            {
                if (s == ListPage)
                {
                    mods = null;
                }
            }
        }

        // The game reads a page's lines, and its explanation, from its text files by page name.
        [HarmonyPatch(typeof(Global), nameof(Global.GetInfo))]
        private static class GetInfoPatch
        {
            private static bool Prefix(string category, string s, ref string __result, out bool __state)
            {
                __state = category == "alieninfo" && (s == GamePage || s == GamePageEarly);
                if (category != "alieninfo")
                {
                    return true;
                }
                var d = Manager;
                try
                {
                    if (OnList(d))
                    {
                        if (s == ListPage)
                        {
                            __result = string.Join(" newline ", (mods ??= Glyphs.Mods()).Select(m => m.Title));
                            return false;
                        }
                        if (s == ListPage + TutorialSuffix)
                        {
                            __result = ListTutorial;
                            return false;
                        }
                    }
                    var mod = OpenMod(d);
                    if (mod != null)
                    {
                        if (s == mod.Title)
                        {
                            __result = PageRows(mod).Text;
                            return false;
                        }
                        if (s == mod.Title + TutorialSuffix)
                        {
                            __result = ModTutorial;
                            return false;
                        }
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"GetInfo prefix failed: {e}");
                }
                return true;
            }

            // The menu of mods goes just above the settings page's last row, which resets the
            // game's settings, or at the end if that row is missing.
            private static void Postfix(bool __state, ref string __result)
            {
                if (!__state || string.IsNullOrEmpty(__result))
                {
                    return;
                }
                var lines = __result.Split(new[] { " newline " }, StringSplitOptions.None).ToList();
                var reset = lines.FindLastIndex(l => l.Trim() == ResetLine);
                lines.Insert(reset >= 0 ? reset : lines.Count, ListPage);
                __result = string.Join(" newline ", lines);
            }
        }

        // Menu pages underline their lines, show the clickable cursor over them, and open them
        // when clicked.
        [HarmonyPatch(typeof(Draw), nameof(Draw.CurrentPageIsMenu))]
        private static class CurrentPageIsMenuPatch
        {
            private static void Postfix(Draw __instance, ref bool __result)
            {
                if (!__result && OnList(__instance))
                {
                    __result = true;
                }
            }
        }

        // After the game draws a page: box the current choices and place the text fields.
        [HarmonyPatch(typeof(Draw), nameof(Draw.UpdateText1))]
        private static class UpdateText1Patch
        {
            private static void Postfix(Draw __instance)
            {
                try
                {
                    Layout(__instance);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"UpdateText1 postfix failed: {e}");
                }
            }
        }

        // The page when Draw.Update began this frame. A click that changed the page there was the
        // game's; the glyph under the mouse now belongs to the new page and must not take it too.
        private static string pageAtUpdate;

        // The game sets the cursor each frame from what is under it; mark these rows clickable.
        [HarmonyPatch(typeof(Draw), "Update")]
        private static class UpdatePatch
        {
            private static void Prefix(Draw __instance) => pageAtUpdate = __instance.current_page;

            private static void Postfix(Draw __instance)
            {
                try
                {
                    if (Clickable(__instance, HitGlyph()))
                    {
                        HardwareCursor.Apply(__instance.DefaultColor(0), __instance.mousesize.x);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Update postfix failed: {e}");
                }
            }
        }

        private static void Layout(Draw d)
        {
            choices.Clear();
            var mod = OpenMod(d);
            shown = mod;
            var used = 0;
            if (mod != null && d.alien_right_text_down != null)
            {
                var rows = PageRows(mod).Rows;
                foreach (var row in d.alien_right_text_down.Where(a => a.sector == "righttextdown").GroupBy(a => a.line))
                {
                    if (row.Key < 0 || row.Key >= rows.Count || rows[row.Key] < 0)
                    {
                        continue;
                    }
                    var glyphs = row.ToList();
                    var setting = mod.Settings[rows[row.Key]];
                    var label = glyphs.Take(setting.Label.Count).ToList();
                    if (setting.Choices != null)
                    {
                        var options = glyphs.Skip(setting.Label.Count).ToList();
                        for (var i = 0; i < options.Count && i < setting.Choices.Count; i++)
                        {
                            var value = setting.Choices[i].Value;
                            choices.Add(new Choice { Glyph = options[i], Setting = setting, Value = value });
                            if (Equals(value, setting.Entry.BoxedValue))
                            {
                                d.selected.Add(d.DrawRectRenderer(1f, 1f, options[i].sp.transform.position, 2));
                            }
                        }
                    }
                    else if (label.Count > 0 && Slot(d, label[0], out var x0, out var y0))
                    {
                        // The row starts at the label's first glyph. Each line of it is one slot
                        // row further down, as Draw.Write counts them.
                        x0 -= label[0].line_x;
                        var y = y0 - setting.FieldLine;
                        var first = x0 + setting.FieldColumn;
                        var last = x0 + RowWidth - 1;
                        if (first <= last && y >= 0 && y <= d.alien_slots.GetUpperBound(1))
                        {
                            var field = used < fields.Count ? fields[used] : CreateField(d);
                            used++;
                            field.Setting = setting;
                            field.X0 = first;
                            field.X1 = last;
                            field.Y = y;
                            if (!field.Input.isFocused)
                            {
                                field.Input.text = Display(setting);
                            }
                            field.Input.gameObject.SetActive(true);
                            var a = d.alien_slots[first, y].transform.position;
                            var b = d.alien_slots[last, y].transform.position;
                            d.selected.Add(d.DrawLineRenderer(a + new Vector3(-0.45f, -0.5f), b + new Vector3(0.45f, -0.5f), 3));
                        }
                    }
                }
            }
            for (var i = used; i < fields.Count; i++)
            {
                fields[i].Setting = null;
                fields[i].Input.gameObject.SetActive(false);
            }
        }

        private static bool Slot(Draw d, AlienCharacter glyph, out int x, out int y)
        {
            var slots = d.alien_slots;
            for (x = 0; x <= slots.GetUpperBound(0); x++)
            {
                for (y = 0; y <= slots.GetUpperBound(1); y++)
                {
                    if (slots[x, y] == glyph)
                    {
                        return true;
                    }
                }
            }
            x = y = -1;
            return false;
        }

        // The value as the .cfg file writes it.
        private static string Display(Glyphs.Setting setting) =>
            TomlTypeConverter.ConvertToString(setting.Entry.BoxedValue, setting.Entry.SettingType);

        private static Field CreateField(Draw d)
        {
            var go = UnityEngine.Object.Instantiate(d.text_prefab);
            go.name = "mod setting";
            go.transform.SetParent(d.world_canva.transform);
            var input = go.GetComponent<TMP_InputField>();
            var model = d.txt_slots?[0, 0];
            if (model != null)
            {
                input.fontAsset = model.fontAsset;
            }
            input.characterLimit = CharacterLimit;
            input.richText = false;
            input.textComponent.color = d.DefaultColor(0);
            input.textComponent.alignment = TextAlignmentOptions.Left;
            input.onSelect.RemoveAllListeners();
            input.onEndEdit.RemoveAllListeners();
            var field = new Field { Input = input };
            fields.Add(field);
            input.onEndEdit.AddListener(text => Apply(field, text));
            return field;
        }

        private static void Apply(Field field, string text)
        {
            var entry = field.Setting?.Entry;
            if (entry == null)
            {
                return;
            }
            try
            {
                entry.BoxedValue = TomlTypeConverter.ConvertToValue(text, entry.SettingType);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"{entry.Definition}: \"{text}\" is not a valid value: {e.Message}");
            }
            field.Input.text = Display(field.Setting);
            Manager?.UpdateText();
        }

        private static AlienCharacter HitGlyph()
        {
            var cam = Camera.main;
            if (cam == null || !Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out var hit))
            {
                return null;
            }
            return hit.transform.gameObject.GetComponentInParent<AlienCharacter>();
        }

        private static bool Clickable(Draw d, AlienCharacter glyph)
        {
            if (glyph == null || glyph.sector != "righttextdown")
            {
                return false;
            }
            if (OnGamePage(d))
            {
                return glyph.line_str == ListPage;
            }
            return shown != null && shown == OpenMod(d) && choices.Any(c => c.Glyph == glyph);
        }

        internal static void Tick()
        {
            var d = Manager;
            if (d == null)
            {
                return;
            }
            if (Input.GetMouseButtonDown(0) && d.current_page == pageAtUpdate)
            {
                var glyph = HitGlyph();
                if (Clickable(d, glyph))
                {
                    if (OnGamePage(d))
                    {
                        d.ToPage(ListPage);
                    }
                    else
                    {
                        var choice = choices.First(c => c.Glyph == glyph);
                        choice.Setting.Entry.BoxedValue = choice.Value;
                        d.UpdateText();
                    }
                }
            }
            PositionFields(d);
        }

        // Fields cover their cells' glyph area; follow the screen size like the game's labels.
        private static void PositionFields(Draw d)
        {
            var open = shown != null && shown == OpenMod(d);
            var cam = Camera.main;
            var slots = d.alien_slots;
            if (cam == null || slots == null)
            {
                return;
            }
            var cell = (Vector2)cam.WorldToScreenPoint(slots[1, 1].transform.position) - (Vector2)cam.WorldToScreenPoint(slots[0, 0].transform.position);
            foreach (var field in fields)
            {
                if (field.Setting == null || !open)
                {
                    if (field.Input.gameObject.activeSelf)
                    {
                        if (EventSystem.current?.currentSelectedGameObject == field.Input.gameObject)
                        {
                            EventSystem.current.SetSelectedGameObject(null);
                        }
                        field.Input.gameObject.SetActive(false);
                    }
                    continue;
                }
                var corner = slots[field.X0, field.Y].transform.position + new Vector3(-0.5f, -0.5f);
                field.Input.transform.position = (Vector2)cam.WorldToScreenPoint(corner);
                ((RectTransform)field.Input.transform).sizeDelta = new Vector2(cell.x * (field.X1 - field.X0 + 1), Mathf.Abs(cell.y) / 1.5f);
                // A fixed size, so fields with short and long values match.
                field.Input.textComponent.enableAutoSizing = false;
                field.Input.textComponent.fontSize = 0.5f * cell.x;
                field.Input.textComponent.textWrappingMode = TextWrappingModes.NoWrap;
            }
        }
    }
}
