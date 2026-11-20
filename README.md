# Mod Settings

A [BepInEx 5](https://github.com/BepInEx/BepInEx) plugin for [Message From Aliens](https://store.steampowered.com/app/1858720/Message_From_Aliens/) that shows the settings of other mods in the game, in the game's glyphs.

## Installing

Install BepInEx first, if you haven't already, as described under [Requirements](#requirements). Then unzip the release zip into the game folder, the one that holds `MessageFromAliens.exe`. In Steam, right-click the game and choose Manage, then Browse local files, to open it. The zip holds `BepInEx/plugins/ModSettings/`, so the mod lands in BepInEx's plugins folder. To update, unzip a newer release over it. To uninstall, delete `BepInEx/plugins/ModSettings/`.

## Use

The game's settings page gets one more row, this mod's name in glyphs, just above its last row. Clicking it opens a menu of mods, one line per mod that names itself in glyphs and has at least one setting to show. Clicking a mod opens its page, laid out like the game's settings page: one row per setting, its name in glyphs, then an empty cell and its choices, with the current choice boxed. Clicking a choice changes the setting. Settings without a glyph for each value, such as most number and text settings, appear as text fields instead of clickable glyphs. A mod can also put settings on sub-pages opened from the mod's settings page.

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
| `MessageFromAliens.Label` | The setting's name in glyphs. Required; settings without it are not shown. A setting shown as a text field shows only the first 12 glyphs, so that the name fits on one line. |
| `MessageFromAliens.Value.<value>` | The glyph for one value, keyed by the value as written in the `.cfg` file, such as `MessageFromAliens.Value.Fast`. |
| `MessageFromAliens.Page` | The name in glyphs of the page the setting is on. Optional; settings without it are on the mod's own page. Settings with the same name share a page, in the order they were bound. |
| `MessageFromAliens.Quoted` | `true` if the `.cfg` file writes the value in double quotes, as a setting must to keep leading or trailing spaces. The text field shows and takes the value without them. |
| `MessageFromAliens.Gate` | Optional. A gate token, as the game's text files put at the start of a menu line, such as the one that opens a game: a page title with its words joined by `-`, in brackets. The setting is shown only once that gate passes, as the game decides for its own menu lines, wherever the setting's row falls on its page. A mod can hide settings for parts of the game the player has not reached. A mod none of whose settings are shown is left out of the menu. |

A `bool` setting is shown as choices, and needs no value tags. An enum setting, or one with an `AcceptableValueList`, is shown as choices when every value has exactly one glyph. Any other setting gets a text field.

Examples: https://github.com/sparr/messagefromaliens-mod-CompoundTranslations/blob/60cd37f99da6bc7d56d026ba568f2ea9634ee138/src/Plugin.cs#L47C1-L95C4

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
| `Global.GetInfo` | Prefix: supplies the lines and explanations of the menu of mods and of each mod's pages. Postfix: adds the menu's row to the settings page. |
| `Draw.CurrentPageIsMenu` | Postfix: true on the menu of mods, so its lines are drawn, highlighted, and clicked like a menu's. |
| `Draw.ToPage` | Prefix: finds the mods again each time the menu of mods is opened from the settings page, and opens a mod's page under a name no game page has, so the game runs none of its own code for a page of the same name. |
| `Draw.UpdateText1` | Postfix: boxes the current choices, underlines the lines that open pages, and places the text fields. |
| `Draw.Update` | Prefix: notes the page, so a click that made the game change page is not taken again by the new page's row. |
| `HardwareCursor.Apply` | Prefix: gives the cursor the clickable color over the settings page's new row, over choices, and over lines that open pages. |
| `Global.GetWin` | Prefix: false for mods' names while the menu of mods is drawn, so a mod named like a won page is not drawn as won. |
| `Global.SetWin` | Prefix: never records the menu of mods as won. |
| `Draw.CurrentPageIsRiddle` | Postfix: false on the menu of mods and the mods' pages, whatever their names. |
| `Draw.GetHelp` | Prefix: opens a mod page's explanation, whatever the page's name. |

The plugin's `LateUpdate` handles clicks on those, since the game ignores clicks on the rows it adds and on pages it does not know.
