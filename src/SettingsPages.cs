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
    // without choice glyphs get a text field instead, and settings with too many choices for two
    // lines a page of their choices, opened from their row.
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

        private sealed class Field
        {
            internal TMP_InputField Input;
            internal Glyphs.Setting Setting;
            // Slot coordinates of the field's first and last cells.
            internal int X0, X1, Y;
        }

        private static readonly List<Field> fields = new List<Field>();
        // What clicking each clickable glyph of the drawn page does: set a choice, or open a page.
        private static readonly Dictionary<AlienCharacter, Action> clicks = new Dictionary<AlienCharacter, Action>();
        private static List<Glyphs.Mod> mods;
        private static Glyphs.Page shown;

        private static Draw Manager => Global.manager;

        private static bool OnGamePage(Draw d) => d != null && d.current_page == GamePage;

        // Whether the current page is one this mod draws clickable rows on.
        private static bool OnDrawnPage(Draw d) => OnGamePage(d) || (shown != null && shown == OpenPage(d));

        private static bool OnList(Draw d) =>
            d != null && d.current_page == ListPage && Parent(d, 1) == GamePage;

        // The settings page that is open, if any: a mod's own page, opened from the menu of mods,
        // or a page opened from a line on one of its pages, following the pages opened since.
        private static Glyphs.Page OpenPage(Draw d)
        {
            var stack = d?.parent_page;
            if (stack == null || stack.Count == 0 || stack[stack.Count - 1] != d.current_page)
            {
                return null;
            }
            var list = stack.LastIndexOf(ListPage);
            if (list < 1 || stack[list - 1] != GamePage || list + 1 >= stack.Count)
            {
                return null;
            }
            mods ??= Glyphs.Mods();
            var page = mods.FirstOrDefault(m => m.Root.Key == stack[list + 1])?.Root;
            for (var i = list + 2; page != null && i < stack.Count; i++)
            {
                page = page.Rows.Select(r => r.Link).FirstOrDefault(p => p != null && p.Key == stack[i]);
            }
            return page;
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
        // next page instead, so no row is split between pages. Also, for each line as Draw.Write
        // numbers lines, the page row it starts, or -1 for a line that only starts a page, and
        // where the row's text field starts, if it has one.
        // A page of one setting's choices has a line for each RowWidth of them instead, and its
        // rows are those lines.
        private static (string Text, List<(int Row, (int Line, int Column)? Field)> Rows) PageRows(Glyphs.Page page)
        {
            var text = new StringBuilder();
            var rows = new List<(int Row, (int Line, int Column)? Field)>();
            if (page.Chooser != null)
            {
                var glyphs = page.Chooser.Choices.Select(c => c.Glyph).ToList();
                for (var i = 0; i * RowWidth < glyphs.Count; i++)
                {
                    text.Append(i > 0 ? " newline " : "").Append(Line(glyphs.Skip(i * RowWidth).Take(RowWidth)));
                    rows.Add((i, null));
                }
                return (text.ToString(), rows);
            }
            var used = 0;
            for (var i = 0; i < page.Rows.Count; i++)
            {
                var row = page.Rows[i];
                var (tokens, field) = row.Setting == null ? (row.Link.Words.Take(Glyphs.MaxLabel).ToList(), null) : RowTokens(row);
                var lines = After(tokens.Count).Line + 1;
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
                        rows.Add((-1, null));
                        used = 0;
                    }
                }
                text.Append(Line(tokens));
                rows.Add((i, field));
                used += lines;
            }
            return (text.ToString(), rows);
        }

        // A setting's row: its name, an empty cell, then its choices, the current one of choices on
        // a page of their own, or the empty cells its text field covers. The field fills the rest
        // of the line, or the next line when too little of it is left. Also where the field starts,
        // in lines and cells from the start of the row.
        private static (List<string> Tokens, (int Line, int Column)? Field) RowTokens(Glyphs.Row row)
        {
            var setting = row.Setting;
            var tokens = setting.Label.Append(".").ToList();
            if (row.Link != null)
            {
                tokens.AddRange(setting.Choices.Where(c => Equals(c.Value, setting.Entry.BoxedValue)).Take(1).Select(c => c.Glyph));
                return (tokens, null);
            }
            if (setting.Choices != null)
            {
                tokens.AddRange(setting.Choices.Select(c => c.Glyph));
                return (tokens, null);
            }
            var (line, column) = After(tokens.Count);
            if (RowWidth - column < MinFieldCells)
            {
                tokens.AddRange(Enumerable.Repeat(".", RowWidth - column + RowWidth - 1));
                line++;
                column = 1;
            }
            return (tokens, (line, column));
        }

        // The line and cell after a row's first cells, counted from the row's start. The row takes
        // the lines up to and including that line.
        private static (int Line, int Column) After(int cells) =>
            cells <= RowWidth ? (0, cells) : (1 + (cells - RowWidth - 1) / (RowWidth - 1), 2 + (cells - RowWidth - 1) % (RowWidth - 1));

        [HarmonyPatch(typeof(Draw), nameof(Draw.ToPage))]
        private static class ToPagePatch
        {
            // Discover mods again each time the menu is opened from the settings page, so the list
            // matches what is loaded. Going back to it from a mod's page keeps the list.
            // The game opens a clicked menu line by its drawn words, which are a mod's title; the
            // mod's page is opened under its key instead.
            private static void Prefix(Draw __instance, ref string s)
            {
                if (s == ListPage)
                {
                    mods = null;
                    return;
                }
                if (mods != null && OnList(__instance))
                {
                    var title = s;
                    var mod = mods.FirstOrDefault(m => m.Root.Title == title);
                    if (mod != null)
                    {
                        s = mod.Root.Key;
                    }
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
                            __result = string.Join(" newline ", (mods ??= Glyphs.Mods()).Select(m => m.Root.Title));
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
                        // Draw.UpdateText1 asks for the page's text under its name with numbers
                        // folded into decimal, as Global.ReverseParse writes them.
                        if (s == page.Key || s == Global.MergeString(Global.ReverseParse(page.Key)))
                        {
                            __result = PageRows(page).Text;
                            return false;
                        }
                        if (s == page.Key + ExplainSuffix)
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
                if (mods != null && OnList(Manager) && mods.Any(m => m.Root.Title == s))
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

        // The explain button also tests the page's name for the quiz word. A mod page opens its own
        // explanation, as the game's last case would.
        [HarmonyPatch(typeof(Draw), nameof(Draw.GetHelp))]
        private static class GetHelpPatch
        {
            private static bool Prefix(Draw __instance)
            {
                try
                {
                    if (OpenPage(__instance) == null)
                    {
                        return true;
                    }
                    __instance.PushLeftMessage(__instance.current_page + ExplainSuffix);
                    return false;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"GetHelp prefix failed: {e}");
                    return true;
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
        // the game would change the cursor twice every frame. The raycast runs only on pages this
        // mod draws.
        [HarmonyPatch(typeof(HardwareCursor), nameof(HardwareCursor.Apply))]
        private static class CursorPatch
        {
            private static void Prefix(ref Color c)
            {
                try
                {
                    var d = Manager;
                    if (d != null && c == d.DefaultColor(5) && OnDrawnPage(d) && Clickable(d, HitGlyph()))
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
            clicks.Clear();
            var page = OpenPage(d);
            shown = page;
            var used = 0;
            if (page != null && d.alien_right_text_down != null)
            {
                var rows = PageRows(page).Rows;
                foreach (var row in d.alien_right_text_down.Where(a => a.sector == "righttextdown").GroupBy(a => a.line))
                {
                    if (row.Key < 0 || row.Key >= rows.Count || rows[row.Key].Row < 0)
                    {
                        continue;
                    }
                    var (rowIndex, fieldStart) = rows[row.Key];
                    var glyphs = row.ToList();
                    if (page.Chooser != null)
                    {
                        for (var i = 0; i < glyphs.Count; i++)
                        {
                            var choice = rowIndex * RowWidth + i;
                            if (choice < page.Chooser.Choices.Count)
                            {
                                AddChoice(d, glyphs[i], page.Chooser, page.Chooser.Choices[choice].Value, true);
                            }
                        }
                        continue;
                    }
                    var link = page.Rows[rowIndex].Link;
                    var setting = page.Rows[rowIndex].Setting;
                    // Empty cells in the label take a cell but draw no glyph.
                    var drawn = setting == null ? glyphs.Count : setting.Label.Count(t => t != Glyphs.Gap);
                    var label = glyphs.Take(drawn).ToList();
                    if (link != null)
                    {
                        // The whole row opens the page. Its name is underlined like a menu line.
                        foreach (var glyph in glyphs)
                        {
                            clicks[glyph] = () => d.ToPage(link.Key);
                        }
                        foreach (var glyph in label)
                        {
                            d.selected.Add(d.DrawLineRenderer(glyph.sp.transform.position + new Vector3(-0.45f, -0.92f), glyph.sp.transform.position + new Vector3(0.45f, -0.92f), 3));
                        }
                        continue;
                    }
                    if (setting.Choices != null)
                    {
                        var options = glyphs.Skip(drawn).ToList();
                        for (var i = 0; i < options.Count && i < setting.Choices.Count; i++)
                        {
                            AddChoice(d, options[i], setting, setting.Choices[i].Value, false);
                        }
                    }
                    else if (fieldStart is { } start && label.Count > 0 && Slot(d, label[0], out var x0, out var y0))
                    {
                        // The row starts at the label's first glyph. Each line of it is one slot
                        // row further down, as Draw.Write counts them.
                        x0 -= label[0].line_x;
                        var y = y0 - start.Line;
                        var first = x0 + start.Column;
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

        // A clickable choice glyph, boxed when it is the current value. On a page of one setting's
        // choices, choosing goes back to the page before.
        private static void AddChoice(Draw d, AlienCharacter glyph, Glyphs.Setting setting, object value, bool back)
        {
            clicks[glyph] = () =>
            {
                setting.Entry.BoxedValue = value;
                if (back)
                {
                    d.ToParentPage();
                }
                else
                {
                    d.UpdateText();
                }
            };
            if (Equals(value, setting.Entry.BoxedValue))
            {
                d.selected.Add(d.DrawRectRenderer(1f, 1f, glyph.sp.transform.position, 2));
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
            return shown != null && shown == OpenPage(d) && clicks.ContainsKey(glyph);
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
                        clicks[glyph]();
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
