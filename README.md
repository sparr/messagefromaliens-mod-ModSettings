# Mod Settings

A [BepInEx 5](https://github.com/BepInEx/BepInEx) plugin for [Message From Aliens](https://store.steampowered.com/app/1858720/Message_From_Aliens/) that shows the settings of other mods in the game, in the game's glyphs.

## Installing

Install BepInEx first, if you haven't already, as described under [Requirements](#requirements). Then unzip the release zip into the game folder, the one that holds `MessageFromAliens.exe`. In Steam, right-click the game and choose Manage, then Browse local files, to open it. The zip holds `BepInEx/plugins/ModSettings/`, so the mod lands in BepInEx's plugins folder. To update, unzip a newer release over it. To uninstall, delete `BepInEx/plugins/ModSettings/`.

## Use

The game's settings page gets one more row, this mod's name in glyphs, just above its last row. Clicking it opens a menu of mods, one line per mod that names itself in glyphs and has at least one setting to show. Clicking a mod opens its page, laid out like the game's settings page: one row per setting, its name in glyphs, then an empty cell and its choices, with the current choice boxed. Clicking a choice changes the setting.

Changes apply as they would from the `.cfg` file, and BepInEx saves them to it. Some settings change immediately, some only on a game restart.

## Naming a mod and its settings in glyphs

Other mods do not need to name this mod as a dependency. Just add an assembly attribute to your mod:

```csharp
[assembly: AssemblyMetadata("MessageFromAliens.Name", "alien glyph ids go here")]
```

Then tag each setting's `ConfigDescription` with a `Dictionary<string, string>`:

```csharp
Config.Bind("Section", "Key", true, new ConfigDescription("English description", null,
    new Dictionary<string, string>
    {
        ["MessageFromAliens.Label"] = "alien glyph ids go here",
    }));
```

| Key | Meaning |
|---|---|
| `MessageFromAliens.Label` | The setting's name in glyphs. Required; settings without it are not shown. |
| `MessageFromAliens.Value.<value>` | The glyph for one value, keyed by the value as written in the `.cfg` file, such as `MessageFromAliens.Value.Fast`. |

A `bool` setting is shown with the game's yes and no glyphs unless its tags say otherwise. An enum setting, or one with an `AcceptableValueList`, is shown as choices when every value has exactly one glyph. Any other setting is not shown.

Examples: https://github.com/sparr/messagefromaliens-mod-Calculator/blob/16f5811e0963f845ce775e9852ef7db2ac421f6f/src/Plugin.cs#L34-L82

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
| `Draw.ToPage` | Prefix: finds the mods again each time the menu of mods opens. |
| `Draw.UpdateText1` | Postfix: boxes the current choices. |
| `Draw.Update` | Prefix: notes the page, so a click that made the game change page is not taken again by the new page's row. Postfix: shows the clickable cursor over the settings page's new row and over choices. |

The plugin's `LateUpdate` handles clicks on those, since the game ignores clicks on pages it does not know.
