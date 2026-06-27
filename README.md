# CherryBox Plugins

Official plugin source for [CherryBox](https://github.com/buttercupemily9/cherrybox).

## Layout

This repository is designed to live at `plugins/` inside the main CherryBox tree (git submodule):

```
cherrybox/
  src/CherryBox.Plugins.Abstractions/   ← source of truth in main repo
  plugins/                              ← this repo
    CherryBox.Plugins.Abstractions/     ← vendored copy for standalone CI/build
    HelloCherryBox/
    Backup/
    StorySites/
    Download/
    PasswordReset/
    ViewTime/
```

## Plugins

| Folder | Id | Description |
|--------|----|-------------|
| [HelloCherryBox](HelloCherryBox/) | `hello-cherrybox` | Sample plugin |
| [Backup](Backup/) | `backup` | Backup plugin marker (core backup logic ships in CherryBox) |
| [StorySites](StorySites/) | `story-sites` | Import stories from supported external sites |
| [Download](Download/) | `download` | Video downloader (yt-dlp queue, scan, metadata) |
| [Email](Email/) | `email` | Shared SMTP and email delivery |
| [PasswordReset](PasswordReset/) | `password-reset` | Password reset emails (requires Email) |
| [Newsletter](Newsletter/) | `newsletter` | Welcome and weekly digest emails (requires Email) |
| [ViewTime](ViewTime/) | `view-time` | Account view time limits, expiry, and minute requests |

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

Reload from **Settings → Plugins** or **Settings → Store**, or `POST /api/v1/plugins/reload`.

## Plugin store

CherryBox **Settings → Store** pulls the catalog and packages from this repo:

| Source | URL |
|--------|-----|
| Catalog | [`store.json` on `main`](https://raw.githubusercontent.com/buttercupemily9/cherrybox-plugins/main/store.json) |
| Packages | [Latest GitHub release](https://github.com/buttercupemily9/cherrybox-plugins/releases/latest) (`{plugin-id}.zip`) |

### Updating the catalog

**Every plugin version bump must update `store.json`.** The in-app store reads [`store.json` on `main`](https://github.com/buttercupemily9/cherrybox-plugins/blob/main/store.json), not individual `plugin.json` files.

When you change a plugin:

1. Bump `"version"` in that plugin's `plugin.json`.
2. Run `.\sync-store.ps1` (syncs version/name/folder from manifests and bumps `catalogVersion` when needed).
3. Edit the matching plugin entry's `"changelog"` in `store.json`.
4. Commit `plugin.json` and `store.json` together.
5. Merge to `main` so the live catalog URL and release ZIPs update (CI publishes packages on pushes to `main`).

CI runs `verify-store.ps1` on every push/PR and fails if any catalog version, name, or folder is out of sync with `plugin.json`.

Regenerate the catalog after adding a plugin folder:

```powershell
.\sync-store.ps1
```

## CI / releases

GitHub Actions (`.github/workflows/ci.yml`) runs on every push to `main`:

1. Checks out this repo (includes vendored `CherryBox.Plugins.Abstractions`)
2. Builds all plugins
3. Packages each plugin as `{plugin-id}.zip`
4. Updates the rolling [**latest** release](https://github.com/buttercupemily9/cherrybox-plugins/releases/latest) used by the app store

Pull requests build and upload packages as workflow artifacts only (no release).

Build packages locally:

```powershell
.\build-packages.ps1
```

When abstractions change in the main CherryBox repo, sync the vendored copy here:

```powershell
.\sync-abstractions.ps1
```

## Create a plugin

1. Add a new folder with `plugin.json`, `.csproj`, and a class implementing `ICherryBoxPlugin`.
2. Reference `../CherryBox.Plugins.Abstractions/CherryBox.Plugins.Abstractions.csproj`.
3. See [HelloCherryBox/HelloPlugin.cs](HelloCherryBox/HelloPlugin.cs) for a minimal example.

## License

MIT — same as CherryBox.
