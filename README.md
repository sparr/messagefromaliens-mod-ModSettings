# Mod Settings

A [BepInEx 5](https://github.com/BepInEx/BepInEx) plugin for [Message From Aliens](https://store.steampowered.com/app/1858720/Message_From_Aliens/) that shows the settings of other mods in the game, in the game's glyphs.

## Installing

Install BepInEx first, if you haven't already, as described under [Requirements](#requirements). Then unzip the release zip into the game folder, the one that holds `MessageFromAliens.exe`. In Steam, right-click the game and choose Manage, then Browse local files, to open it. The zip holds `BepInEx/plugins/ModSettings/`, so the mod lands in BepInEx's plugins folder. To update, unzip a newer release over it. To uninstall, delete `BepInEx/plugins/ModSettings/`.

## Use

The game's settings page gets one more row, this mod's name in glyphs, just above its last row. Clicking it opens a menu of mods, one line per mod that names itself in glyphs and has at least one setting to show. Clicking a mod opens its page, laid out like the game's settings page: one row per setting, its name in glyphs, then an empty cell and its choices, with the current choice boxed. Clicking a choice changes the setting. Settings without a glyph for each value, such as most number and text settings, appear as text fields instead of clickable glyphs. A setting with more than 12 choices, or too many to fit in two lines after its name, shows its name, an empty cell, and its current choice; clicking that row opens a page of all its choices, 12 to a line, and clicking one sets it and goes back. Every setting's row takes one or two lines. A mod can also put settings on sub-pages opened from the mod's settings page.

Changes apply as they would from the `.cfg` file, and BepInEx saves them to it. Some settings change immediately, some only on a game restart.

## Naming a mod and its settings in glyphs

Other mods do not need to name this mod as a dependency. Just add an assembly attribute to your mod:

```csharp
[assembly: AssemblyMetadata("MessageFromAliens.Name", "alien glyph ids go here")]
```

Then tag each setting's `ConfigDescription` with a `Dictionary<string, string>`. Only settings bound in the plugin's own `Config` are shown, not ones in other `ConfigFile` objects.

```csharp
Config.Bind("Section", "Key", true, new ConfigDescription("English description", null,
    new Dictionary<string, string>
    {
        ["MessageFromAliens.Label"] = "alien glyph ids go here",
    }));
```

| Key | Meaning |
|---|---|
| `MessageFromAliens.Label` | The setting's name in glyphs. Required; settings without it are not shown. Only the first 12 glyphs are shown, so that the name fits on one line. A setting shown as choices lists them after its name when it has at most 12 and the name, an empty cell, and the choices fit in 23 cells (two lines); otherwise its choices go on a page of their own, opened from its row. |
| `MessageFromAliens.Value.<value>` | The glyph for one value, keyed by the value as written in the `.cfg` file, such as `MessageFromAliens.Value.Fast`. |
| `MessageFromAliens.Page` | The name in glyphs of the page the setting is on. Optional; settings without it are on the mod's own page. Settings with the same name share a page, in the order they were bound. The line that opens the page shows only the first 12 glyphs. |
| `MessageFromAliens.Quoted` | `true` if the `.cfg` file writes the value in double quotes, as a setting must to keep leading or trailing spaces. The text field shows and takes the value without them. |
| `MessageFromAliens.Gate` | Optional. A gate token, as the game's text files put at the start of a menu line, such as the one that opens a game: a page title with its words joined by `-`, in brackets, or several combined as described under [Gate tokens](#gate-tokens). The setting is shown only once that gate passes, as the game decides for its own menu lines, wherever the setting's row falls on its page. A mod can hide settings for parts of the game the player has not reached. A mod none of whose settings are shown is left out of the menu. |

A `bool` setting is shown as choices, and needs no value tags. An enum setting, or one with an `AcceptableValueList`, is shown as choices when every value has exactly one glyph. Any other setting gets a text field.

Examples: https://github.com/sparr/messagefromaliens-mod-CompoundTranslations/blob/60cd37f99da6bc7d56d026ba568f2ea9634ee138/src/Plugin.cs#L47C1-L95C4

## Gate tokens

A gate token is one bracketed token. The game reads two kinds of gate, called terms here:

- `[<page>]`, a page's name with its words joined by `-`, passes once that page is won.
- `[<chapter>-quiz-<n>]`, with `n` a whole number, passes once `n` of the chapter's quizzes are won. The chapter is the part before `-quiz-`, the start of its quizzes' names. A number written any other way, such as with a place (`1e1`), makes the token name one quiz page instead. Here and below, `quiz` stands for the game's own word for quiz, the word before the number in every quiz page's title; Content Packs' `DumpGameText` setting writes the game text that shows it.

Mod Settings adds two operators inside the brackets: `|` for "or" and `+` for "and". "And" binds first, and there are no parentheses, so `[a+b|c]` passes when `a` and `b` both pass, or when `c` passes. Spaces are not allowed, since the token must stay one token. `&` is not used because the game's left panel uses it in its history.

Mod Settings evaluates these in a prefix on the game's own `Global.GetWin`, sending each term through `Global.GetWin` again. So a combined token works wherever the game or a mod checks a gate: the start of a menu or explanation line in the game's text or a content pack, a setting's `MessageFromAliens.Gate`, an unlock message, and any mod that calls `Global.GetWin`. The game keeps the token whole when it parses text and in the left panel's history. Without Mod Settings, the game reads a combined token as the name of a page that does not exist, so a line under it stays hidden.

A token with an empty term, such as `[a||b]` or `[a+]`, never passes.

A count term can count at most one win for each page whose name starts with the chapter's name and `quiz`: the chapter's quizzes, and its menu if the menu is named so. Explanations are never won. A setting's gate or an unlock message with a count term above that would never pass, so it is rejected with a warning in the log, and that setting or message is left out.

## Unlock messages

A mod feature that unlocks with a gate can tell the player so in the game's own words. The game ends a chapter's explanation, the page the explain button shows on its menu, with lines such as "win 3 of this chapter's quizzes, unlock X", shown from the start. When the chapter's wins reach 3, it shows a page in the left panel saying "you now unlock X", which this README calls a win count page. A mod declares only X, the feature's words, and Mod Settings writes both sentences:

```csharp
[assembly: AssemblyMetadata("MessageFromAliens.Unlock", "[<chapter>-quiz-3] <feature words>")]
```

The value is a gate token, then the feature's words. The gate token is the same as for `MessageFromAliens.Gate`, so a setting and its message can share one.

- **Explanation lines:** for each alternative of the gate (each part between `|`), one line: "win n of the chapter's quizzes, unlock X". The terms of an alternative are joined by "and", as "win 3 of one chapter's quizzes and win 2 of another's, unlock X". A page term reads "win the page, unlock X". The line goes in the explanation of each chapter that a count term of the alternative counts. It is always shown, like the game's own. Mod Settings sorts these lines in with the game's own lines at the end of the explanation by count; at the same count, the game's come first, then mods' in the order of the mods' names and of their declarations.
- **Win count pages:** for each count term, "you now unlock X" goes on that chapter's win count page for `n`. The game shows that page when the chapter's count of wins reaches `n`, and the line shows whenever the page does, as long as the alternative's other terms pass. Lines from "and" alternatives can so show on more than one page. A win count page the game has no text for is added to the game's text, so the game shows it and other mods find it with the game's own (see [Win count pages](#win-count-pages)).
- **Pages for page terms:** for each page term of an alternative that has no count term, "you now unlock X" goes on a page named `<page> unlock`, with the page's name as the game's text files write it, its words separated by spaces. It shows once the alternative passes. See [Win count pages](#win-count-pages). So every declaration has a page that shows its line once its gate passes: a win count page, or a page term's page.
- **At the unlock:** at the win that makes a declaration's whole gate pass, "you now unlock X" shows in the left panel, as the game shows its win count pages. If the game showed a win count page for that win, the line is added to it, unless the page already has it; otherwise the line goes in a page of its own under the game's title for a win count page. Mod Settings records each declaration it has shown in the save's `dict`, under keys starting with `modsettings.announced:`, and shows it only once. When a win records a new one, Mod Settings writes the save, since the game doesn't save after every kind of win, such as reading a lesson. The game ignores these keys, and they stay in the save if Mod Settings is removed.
- **Gates passed earlier:** when a save loads, at startup and when another save slot is loaded, every declaration whose gate already passes and that the save has not recorded shows "you now unlock X" in the left panel at once, without waiting for a win. This covers a save from before Mod Settings, a newly installed or updated mod, and a player who has won every quiz. All such lines go in one page under the game's title for a win count page, as a win that unlocks several things shows them together. Mod Settings records them and writes the save right away, so they don't show again.
- **Choosing the page:** with the key `MessageFromAliens.Unlock`, every alternative must have a count term whose chapter has an explanation. To put the explanation lines somewhere else, such as for a gate with only page terms, name the page whose explanation gets them: `MessageFromAliens.Unlock.<page>`, with the page's name as the game's text files write it, its words separated by spaces. The page must have an explanation. Count terms still put lines on win count pages.
- **On a setting:** the same keys work in a setting's tags. There the gate token can be left out, and the setting's `MessageFromAliens.Gate` is used. The setting does not need a label. Use the assembly attribute for a message about a feature that has no setting, or that unlocks with a different gate than its settings.
- **Words:** game word IDs separated by spaces, as in the game's text files, at least one glyph and one line. Line and page breaks, buttons, brackets, `(` tokens, and further gate tokens are not allowed. Write it in words the player can translate by the time the gate passes. A left panel line holds 12 glyphs and continues on the next row, one cell in; "win n of the chapter's quizzes, unlock" takes 5 to 7 glyphs before X, and "you now unlock" takes 3.
- **Duplicates:** a line declared more than once, as by two settings, is added once to each page.
- **Problems** are logged as warnings, once each, and that message is left out. This includes a count term above what its chapter can reach (see [Gate tokens](#gate-tokens)).
- Mod Settings reads the declarations when the game reads its text at startup, and again if the game's text or the set of loaded plugins changes. It reads a setting's gate then; a gate changed later is not seen.

Examples:

```csharp
// "win 2 of <chapter>'s quizzes, unlock <feature>" in the chapter's explanation, and
// "you now unlock <feature>" on the chapter's win count page for 2.
[assembly: AssemblyMetadata("MessageFromAliens.Unlock", "[<chapter>-quiz-2] <feature>")]
// One line in each of three chapters' explanations, each with its own count, and a line on each
// chapter's win count page for its count.
[assembly: AssemblyMetadata("MessageFromAliens.Unlock", "[<a>-quiz-5|<b>-quiz-3|<c>-quiz-2] <feature>")]
// Lines in the explanation of the page named <page>: "win <won-page>, unlock <feature>".
[assembly: AssemblyMetadata("MessageFromAliens.Unlock.<page>", "[<won-page>] <feature>")]

// In a setting's tags, with the setting's gate.
["MessageFromAliens.Gate"] = "[<chapter>-quiz-4]",
["MessageFromAliens.Unlock"] = "<feature>",
```

`<chapter>`, `<a>`, `<b>`, `<c>`, `<page>`, `<won-page>`, and `<feature>` stand for game word IDs; in a gate token, a page's words are joined by `-`.

### Win count pages

The game names a chapter's win count page `<chapter> unlock <n>` and shows it from `Global.SetWin`, through `Draw.UnlockMessage`, when a won quiz changes the chapter's count. That count is `Global.CountWin(<chapter>)`: every won page whose name starts with the chapter's. Lessons' names do not, so the count is the chapter's won quizzes, plus one for the chapter's menu once the chapter is finished. The game records the menu as won only when every line on it is won: every lesson read and every quiz solved. So during the chapter the count equals the quizzes won, and the page for `n` shows when the `n`th quiz is won. Mod Settings' lines follow the page, so they show with the game's own lines.

Mod Settings adds each win count page that has mods' lines but no text of its own to `Global.alien_message["alieninfo"]`, in a postfix of `Global.Init`. Its text there holds all its lines; `Global.GetInfo` returns only those that show, and an empty string when none do. A mod that lists win count pages, as Interface Tweaks does, finds them by name with the game's own, and should leave out a page whose `Global.GetInfo("alieninfo", name)` is empty.

A page term's page is added to `Global.alien_message["alieninfo"]` the same way, named `<page> unlock`: the name of the page the term names, as the game's text and `Global.link_message` write it, with spaces, then a space and `unlock`, with no count after it. No game page's name ends in `unlock`. To list these pages, a mod can take each name in that category that ends in ` unlock`, find the menu in `Global.link_message` whose lines include the name without ` unlock`, and list it at the end of that menu while `Global.GetInfo("alieninfo", name)` is not empty. Mod Settings makes `Global.GetWin` true for such a page while it shows a line, so the menu can still be won, and never records it as won, since a page term that names a quiz would make its name add to the chapter's count.

## Requirements

- BepInEx 5.4.23.6 or later, `BepInEx_win_x64` build, unzipped into the game folder. Tested with Unity 6000.6.8f1.
- On Linux under Proton, set the Steam launch options to `WINEDLLOVERRIDES="winhttp=n,b" %command%`.
- .NET SDK 8 or later to build.

## Building

```sh
dotnet build -c Release
```

With `-p:DeployToGame=true`, the build also copies `ModSettings.dll` to `<game>/BepInEx/plugins/ModSettings/`.

The build looks for the game in `<SteamLibraryDir>/steamapps/common/Message From Aliens`. `SteamLibraryDir` comes from the first of these that is set:

1. `-p:SteamLibraryDir=/path/to/SteamLibrary` on the command line.
2. A `SteamLibraryDir` environment variable.
3. `GamePaths.props`, a copy of `GamePaths.props.example`. It is ignored by git.
4. Steam's default install location: `~/.local/share/Steam` on Linux, `C:\Program Files (x86)\Steam` on Windows.

For a game folder outside a Steam library, set `GameDir` to the game folder the same ways.

## Patches

| Method | Patch |
|---|---|
| `Global.GetInfo` | Prefix: supplies the lines and explanations of the menu of mods and of each mod's pages. Postfix: adds the menu's row to the settings page. A second postfix sorts mods' unlock lines into chapters' explanations, and gives the lines of win count pages and page terms' pages that show. |
| `Draw.CurrentPageIsMenu` | Postfix: true on the menu of mods, so its lines are drawn, highlighted, and clicked like a menu's. |
| `Draw.ToPage` | Prefix: finds the mods again each time the menu of mods is opened from the settings page, and opens a mod's page under a name no game page has, so the game runs none of its own code for a page of the same name. |
| `Draw.UpdateText1` | Postfix: boxes the current choices, underlines the lines that open pages, and places the text fields. |
| `Draw.Update` | Prefix: notes the page, so a click that made the game change page is not taken again by the new page's row. |
| `HardwareCursor.Apply` | Prefix: gives the cursor the clickable color over the settings page's new row, over choices, and over lines that open pages. |
| `Global.GetWin` | Prefix: false for mods' names while the menu of mods is drawn, so a mod named like a won page is not drawn as won. A second prefix, run first, evaluates gate tokens that combine terms with `\|` and `+`, checking each term through `Global.GetWin`. Postfix: true for a page term's page while it shows a line. |
| `Global.Init` | Postfix: reads mods' unlock messages and adds the win count pages the game has no text for, and page terms' pages, to its text. |
| `Global.SetWin` | Prefix: never records the menu of mods as won. A second prefix never records a page term's page as won, and records unlock messages whose gates already pass as shown. Postfix: shows "you now unlock X" in the left panel for each unlock message whose gate the win made pass, records it, and writes the save again if it recorded any. |
| `Draw.CurrentPageIsRiddle` | Postfix: false on the menu of mods and the mods' pages, whatever their names. |
| `Draw.GetHelp` | Prefix: opens a mod page's explanation, whatever the page's name. |

The plugin's `LateUpdate` handles clicks on those, since the game ignores clicks on the rows it adds and on pages it does not know. It also redraws after a win adds a line to the left panel, since the game records some wins during a draw, after the left panel is drawn. And when a save has loaded and the game's manager exists, it shows the unlock messages whose gates that save already passes (see [Unlock messages](#unlock-messages)).
