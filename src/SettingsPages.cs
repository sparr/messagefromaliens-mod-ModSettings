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
    // choices, the current one boxed, and a line for each of the mod's other pages. Settings
    // without choice glyphs get a text field instead.
    internal static class SettingsPages
    {
        // The game's settings page, and the name of its early, shorter text.
        private const string GamePage = "modify game";
        private const string GamePageEarly = "modify game 0";
        // The settings page's last row, which resets the game's settings.
        private const string ResetLine = "anti modify";
        // This mod's menu of mods, named like the game's own settings page.
        private const string ListPage = "modify variant";
        private const string ExplainSuffix = " de tutorial";
        // The line of the home page's explanation that says what clicking a line does.
        private const string ListExplanation = "left click sentence for start function";
        // The lines of the settings page's explanation that say what its rows are, reused for
        // each mod's pages.
        private const string ModExplanation = "space de left word be variant attribute newline left click space de right word for choose option";
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
        // The glyphs of each drawn line that opens another page.
        private static readonly List<(AlienCharacter Glyph, Glyphs.Page Page)> links = new List<(AlienCharacter, Glyphs.Page)>();
        private static List<Glyphs.Mod> mods;
        private static Glyphs.Page shown;

        private static Draw Manager => Global.manager;

        private static bool OnGamePage(Draw d) => d != null && d.current_page == GamePage;

        private static bool OnList(Draw d) =>
            d != null && d.current_page == ListPage && Parent(d, 1) == GamePage;

        // The settings page that is open, if any: a mod's own page, opened from the menu of mods,
        // or one of its other pages, opened from its own page.
        private static Glyphs.Page OpenPage(Draw d)
        {
            if (d == null)
            {
                return null;
            }
            if (Parent(d, 1) == ListPage && Parent(d, 2) == GamePage)
            {
                mods ??= Glyphs.Mods();
                return mods.FirstOrDefault(m => m.Title == d.current_page)?.Root;
            }
            if (Parent(d, 2) == ListPage && Parent(d, 3) == GamePage)
            {
                mods ??= Glyphs.Mods();
                var mod = mods.FirstOrDefault(m => m.Title == Parent(d, 1));
                return mod?.Root.Rows.Select(r => r.Link).FirstOrDefault(p => p != null && p.Title == d.current_page);
            }
            return null;
        }

        // The page n levels above the current one.
        private static string Parent(Draw d, int n)
        {
            var stack = d.parent_page;
            return stack != null && stack.Count > n ? stack[stack.Count - 1 - n] : null;
        }

        private static string Line(IEnumerable<string> words) => string.Join(" ", words);

        // A page's lines: a setting's name, then its choices or room for its text field, or the
        // name of a page it opens. A row that would not fit in the lines left on a page starts the
        // next page instead, so no row is split between pages. Also the page row of each line, as
        // Draw.Write numbers lines, or -1 for a line that only starts a page.
        private static (string Text, List<int> Rows) PageRows(Glyphs.Page page)
        {
            var text = new StringBuilder();
            var rows = new List<int>();
            var used = 0;
            for (var i = 0; i < page.Rows.Count; i++)
            {
                var row = page.Rows[i];
                var tokens = row.Setting == null ? row.Link.Words : RowTokens(row.Setting);
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
            // Discover mods again each time the menu is opened from the settings page, so the list
            // matches what is loaded. Going back to it from a mod's page keeps the list.
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
                        if (s == ListPage + ExplainSuffix)
                        {
                            __result = ListExplanation;
                            return false;
                        }
                    }
                    var page = OpenPage(d);
                    if (page != null)
                    {
                        // Draw.UpdateText1 asks for the page's text under its title with numbers
                        // folded into decimal, as Global.ReverseParse writes them.
                        if (s == page.Title || s == Global.MergeString(Global.ReverseParse(page.Title)))
                        {
                            __result = PageRows(page).Text;
                            return false;
                        }
                        if (s == page.Title + ExplainSuffix)
                        {
                            __result = ModExplanation;
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
                // Compared as Draw.Write builds a line's text, without gates and other tokens it
                // does not draw, which a content pack could add.
                var reset = lines.FindLastIndex(l => Line(l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => !t.StartsWith("[") && !t.StartsWith("("))) == ResetLine);
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

        // Being a menu also turns on the game's bookkeeping of wins, which checks every line and
        // records the menu won when all of its lines are. A mod's title can match a page the
        // player has won, so mod titles never count, and the menu of mods is never recorded.
        [HarmonyPatch(typeof(Global), nameof(Global.GetWin))]
        private static class GetWinPatch
        {
            private static bool Prefix(string s, ref bool __result)
            {
                if (mods != null && OnList(Manager) && mods.Any(m => m.Title == s))
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Global), nameof(Global.SetWin))]
        private static class SetWinPatch
        {
            private static bool Prefix(string s) => s != ListPage;
        }

        // The game takes any page whose name holds its quiz word for a quiz, which changes what
        // clicks do. Mod pages are never quizzes.
        [HarmonyPatch(typeof(Draw), nameof(Draw.CurrentPageIsRiddle))]
        private static class QuizPagePatch
        {
            private static void Postfix(Draw __instance, ref bool __result)
            {
                if (__result && (OnList(__instance) || OpenPage(__instance) != null))
                {
                    __result = false;
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

        [HarmonyPatch(typeof(Draw), "Update")]
        private static class UpdatePatch
        {
            private static void Prefix(Draw __instance) => pageAtUpdate = __instance.current_page;
        }

        // The game sets the cursor each frame from what is under it, in the color of nothing
        // clickable for these rows; give it the clickable color instead. Setting it again after
        // the game would change the cursor twice every frame.
        [HarmonyPatch(typeof(HardwareCursor), nameof(HardwareCursor.Apply))]
        private static class CursorPatch
        {
            private static void Prefix(ref Color c)
            {
                try
                {
                    var d = Manager;
                    if (d != null && c == d.DefaultColor(5) && Clickable(d, HitGlyph()))
                    {
                        c = d.DefaultColor(0);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Cursor prefix failed: {e}");
                }
            }
        }

        private static void Layout(Draw d)
        {
            choices.Clear();
            links.Clear();
            var page = OpenPage(d);
            shown = page;
            var used = 0;
            if (page != null && d.alien_right_text_down != null)
            {
                var rows = PageRows(page).Rows;
                foreach (var row in d.alien_right_text_down.Where(a => a.sector == "righttextdown").GroupBy(a => a.line))
                {
                    if (row.Key < 0 || row.Key >= rows.Count || rows[row.Key] < 0)
                    {
                        continue;
                    }
                    var glyphs = row.ToList();
                    var link = page.Rows[rows[row.Key]].Link;
                    if (link != null)
                    {
                        // Underlined like a menu line.
                        foreach (var glyph in glyphs)
                        {
                            links.Add((glyph, link));
                            d.selected.Add(d.DrawLineRenderer(glyph.sp.transform.position + new Vector3(-0.45f, -0.92f), glyph.sp.transform.position + new Vector3(0.45f, -0.92f), 3));
                        }
                        continue;
                    }
                    var setting = page.Rows[rows[row.Key]].Setting;
                    // Empty cells in the label take a cell but draw no glyph.
                    var drawn = setting.Label.Count(t => t != Glyphs.Gap);
                    var label = glyphs.Take(drawn).ToList();
                    if (setting.Choices != null)
                    {
                        var options = glyphs.Skip(drawn).ToList();
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
                            // The game recolors only its own labels when the color scheme changes.
                            field.Input.textComponent.color = d.DefaultColor(0);
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

        // The value as the .cfg file writes it, without the quotes of a quoted setting.
        private static string Display(Glyphs.Setting setting)
        {
            var text = TomlTypeConverter.ConvertToString(setting.Entry.BoxedValue, setting.Entry.SettingType);
            if (setting.Quoted && text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
            {
                text = text.Substring(1, text.Length - 2);
            }
            return text;
        }

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
            input.textComponent.alignment = TextAlignmentOptions.Left;
            input.onSelect.RemoveAllListeners();
            input.onEndEdit.RemoveAllListeners();
            // Arrow keys stay in the field instead of moving the selection to a nearby label.
            var nav = input.navigation;
            nav.mode = UnityEngine.UI.Navigation.Mode.None;
            input.navigation = nav;
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
            // Nothing typed, or Escape: storing the shown text again could still change the value,
            // since it is not always what the converter reads back.
            if (text != Display(field.Setting))
            {
                try
                {
                    var value = TomlTypeConverter.ConvertToValue(field.Setting.Quoted ? "\"" + text + "\"" : text, entry.SettingType);
                    var problem = Problem(entry, text, value);
                    if (problem != null)
                    {
                        throw new FormatException(problem);
                    }
                    entry.BoxedValue = value;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"{entry.Definition}: \"{text}\" is not a valid value: {e.Message}");
                }
            }
            field.Input.text = Display(field.Setting);
            // An edit that ends without a click, as on Enter or Escape, leaves the field selected,
            // so the next key would act on it. The game's labels let go the same way in
            // AlienCharacter.EndEdit.
            var es = EventSystem.current;
            if (es != null && !es.alreadySelecting && !field.Input.isFocused && es.currentSelectedGameObject == field.Input.gameObject)
            {
                es.SetSelectedGameObject(null);
            }
            Manager?.UpdateText();
        }

        // Some converters accept text they cannot read and return something else, and setting the
        // value would then quietly replace it. Null when the value is what the text says.
        private static string Problem(ConfigEntryBase entry, string text, object value)
        {
            if (entry.Description.AcceptableValues != null && !entry.Description.AcceptableValues.IsValid(value))
            {
                return "it is not one of the allowed values";
            }
            var type = entry.SettingType;
            if (type.IsEnum && !type.IsDefined(typeof(FlagsAttribute), false) && !Enum.IsDefined(type, value))
            {
                return "it is not one of the choices";
            }
            if (value is KeyboardShortcut shortcut && shortcut.MainKey == KeyCode.None && text.Trim() != "" && !text.Trim().Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                return "it is not a key or a key combination";
            }
            return null;
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
            return shown != null && shown == OpenPage(d) && (choices.Any(c => c.Glyph == glyph) || links.Any(l => l.Glyph == glyph));
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
                    else if (links.Any(l => l.Glyph == glyph))
                    {
                        d.ToPage(links.First(l => l.Glyph == glyph).Page.Title);
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
            var open = shown != null && shown == OpenPage(d);
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
