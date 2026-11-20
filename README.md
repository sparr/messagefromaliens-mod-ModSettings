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

## Unlock messages

A mod feature that unlocks with a gate can tell the player so, as the game's own text tells the player about the games and chapters it unlocks. Declare a message with an assembly attribute, as many as needed:

```csharp
[assembly: AssemblyMetadata("MessageFromAliens.Unlock", "[<chapter>-quiz-3] alien glyph ids go here")]
```

The value is a gate token, then the message's words. The gate token is the same as for `MessageFromAliens.Gate`, so a setting and its message can share one. Mod Settings adds the message as a line at the end of the explanation of the chapter whose quizzes the gate counts, the page the explain button shows on that chapter's menu, under the same gate token, so the game draws the line only once the gate passes. Where the gate counts the chapter's quizzes, as `[<chapter>-quiz-<n>]` with `n` a whole number does, the message is also added to the page the game shows in the left panel when the chapter's wins reach `n`, and that page is shown even for a count the game has no page for.

- **Choosing the page:** with the key `MessageFromAliens.Unlock`, the gate must name a chapter's quizzes, as `[<chapter>-quiz-...]`; the chapter is the part before `-quiz-`, with `-` read as a space. For any other gate, such as one on a page that is not a quiz, name the page whose explanation gets the line: `MessageFromAliens.Unlock.<page>`, with the page's name as the game's text files write it, its words separated by spaces. The page must have an explanation.
- **On a setting:** the same keys work in a setting's tags. There the gate token can be left out, and the setting's `MessageFromAliens.Gate` is used. The setting does not need a label. Use the assembly attribute for a message about a feature that has no setting, or that unlocks with a different gate than its settings.
- **Words:** game word IDs separated by spaces, as in the game's text files, at least one glyph. `newline` starts another line under the same gate. Page breaks, buttons, brackets, and further gate tokens are not allowed. Write it in words the player can translate by the time the gate passes.
- **Duplicates:** a line declared more than once with the same gate, as by two settings, is added once.
- **Problems** are logged as warnings, once each, and that message is left out.
- Mod Settings reads the declarations the first time the game asks for an explanation or a win count page. It reads a setting's gate then; a gate changed later is not seen.

Examples:

```csharp
// Added to the chapter's explanation, and to the page shown when its wins reach 2.
// Drawn once 2 of the chapter's quizzes are won.
[assembly: AssemblyMetadata("MessageFromAliens.Unlock", "[<chapter>-quiz-2] <word> <word> <word>")]
// Added to the explanation of the page named <page>; drawn once the page <won-page> is won.
[assembly: AssemblyMetadata("MessageFromAliens.Unlock.<page>", "[<won-page>] <word> <word>")]

// In a setting's tags, with the setting's gate.
["MessageFromAliens.Gate"] = "[<chapter>-quiz-4]",
["MessageFromAliens.Unlock"] = "<word> <word> <word>",
```

`<chapter>`, `<page>`, `<won-page>`, and `<word>` stand for game word IDs; in a gate token, a page's words are joined by `-`. A gate's quiz number is checked as the game checks menu lines: a whole number counts the chapter's won quizzes, and anything else, such as a number written with a place (`1e1`), names one quiz page.

## Requirements

- BepInEx 5.4.23.6 or later, `BepInEx_win_x64` build, unzipped into the game folder. Tested with Unity 6000.6.8f1.
- On Linux under Proton, set the Steam launch options to `WINEDLLOVERRIDES="winhttp=n,b" %command%`.
- .NET SDK 8 or later to build.

## Building

```sh
dotnet build -c Release
```

The build copies `ModSettings.dll` to `<game>/BepInEx/plugins/ModSettings/`. Disable the copy with `-p:DeployToGame=false`.

The build looks for the game in `<SteamLibraryDir>/steamapps/common/Message From Aliens`. `SteamLibraryDir` comes from the first of these that is set:

1. `-p:SteamLibraryDir=/path/to/SteamLibrary` on the command line.
2. A `SteamLibraryDir` environment variable.
3. `GamePaths.props`, a copy of `GamePaths.props.example`. It is ignored by git.
4. Steam's default install location: `~/.local/share/Steam` on Linux, `C:\Program Files (x86)\Steam` on Windows.

For a game folder outside a Steam library, set `GameDir` to the game folder the same ways.

## Patches

| Method | Patch |
|---|---|
| `Global.GetInfo` | Prefix: supplies the lines and explanations of the menu of mods and of each mod's pages. Postfix: adds the menu's row to the settings page. A second postfix adds mods' unlock messages to explanations and to the pages shown when a chapter's wins reach a count. |
| `Draw.CurrentPageIsMenu` | Postfix: true on the menu of mods, so its lines are drawn, highlighted, and clicked like a menu's. |
| `Draw.ToPage` | Prefix: finds the mods again each time the menu of mods is opened from the settings page, and opens a mod's page under a name no game page has, so the game runs none of its own code for a page of the same name. |
| `Draw.UpdateText1` | Postfix: boxes the current choices, underlines the lines that open pages, and places the text fields. |
| `Draw.Update` | Prefix: notes the page, so a click that made the game change page is not taken again by the new page's row. |
| `HardwareCursor.Apply` | Prefix: gives the cursor the clickable color over the settings page's new row, over choices, and over lines that open pages. |
| `Global.GetWin` | Prefix: false for mods' names while the menu of mods is drawn, so a mod named like a won page is not drawn as won. A second prefix, run first, evaluates gate tokens that combine terms with `\|` and `+`, checking each term through `Global.GetWin`. |
| `Global.SetWin` | Prefix: never records the menu of mods as won. |
| `Draw.CurrentPageIsRiddle` | Postfix: false on the menu of mods and the mods' pages, whatever their names. |
| `Draw.GetHelp` | Prefix: opens a mod page's explanation, whatever the page's name. |

The plugin's `LateUpdate` handles clicks on those, since the game ignores clicks on the rows it adds and on pages it does not know.
