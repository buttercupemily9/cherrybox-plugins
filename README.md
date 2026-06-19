# CherryBox Plugins

Official plugin source for [CherryBox](https://github.com/buttercupemily9/cherrybox).

## Layout

This repository is designed to live at `plugins/` inside the main CherryBox tree (git submodule):

```
cherrybox/
  src/CherryBox.Plugins.Abstractions/
  plugins/                    ← this repo
    HelloCherryBox/
    Backup/
```

## Plugins

| Folder | Id | Description |
|--------|----|-------------|
| [HelloCherryBox](HelloCherryBox/) | `hello-cherrybox` | Sample plugin |
| [Backup](Backup/) | `backup` | Backup plugin marker (core backup logic ships in CherryBox) |

## Build

From the CherryBox repo root (with this folder checked out as `plugins/`):

```bash
dotnet build plugins/CherryBox.Plugins.slnx -c Release
```

Publish bundled plugins with the main installer:

```powershell
.\installer\publish.ps1
```

## Install

Copy a published plugin folder to:

```
%ProgramData%\CherryBox\plugins\{plugin-id}\
  plugin.json
  *.Plugin.dll
```

Reload from **Settings → Plugins** or `POST /api/v1/plugins/reload`.

## Create a plugin

1. Add a new folder with `plugin.json`, `.csproj`, and a class implementing `ICherryBoxPlugin`.
2. Reference `../../src/CherryBox.Plugins.Abstractions/CherryBox.Plugins.Abstractions.csproj`.
3. See [HelloCherryBox/HelloPlugin.cs](HelloCherryBox/HelloPlugin.cs) for a minimal example.

## License

MIT — same as CherryBox.
