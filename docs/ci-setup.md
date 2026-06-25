# Plugin CI setup

The **cherrybox-plugins** workflow builds against the main [cherrybox](https://github.com/buttercupemily9/cherrybox) repository (for `CherryBox.Core`, `CherryBox.Data`, and the Download plugin). That repo is private, so the default `GITHUB_TOKEN` cannot check it out.

## Required secret

On **buttercupemily9/cherrybox-plugins** → **Settings** → **Secrets and variables** → **Actions**, add:

| Name | Value |
|------|--------|
| `CHERRYBOX_REPO_TOKEN` | Fine-grained PAT with **Read** access to `buttercupemily9/cherrybox` |

### Creating the PAT

1. GitHub → **Settings** → **Developer settings** → **Fine-grained tokens**
2. **Generate new token**
3. Resource owner: your account
4. Repository access: **Only select repositories** → `cherrybox`
5. Permissions: **Contents** → **Read-only**
6. Copy the token and paste it into the `CHERRYBOX_REPO_TOKEN` secret

Re-run the failed workflow after saving the secret.

## Push order for cross-repo changes

Some plugin changes depend on new APIs in the main **cherrybox** repo (for example unified site logins in `CherryBox.Core` and updated contracts in `CherryBox.Plugins.Abstractions`).

When a change spans both repositories:

1. Push **cherrybox** `dev` first.
2. Push **cherrybox-plugins** `dev` second (or re-run the plugins workflow).

The CI workflow verifies that the checked-out cherrybox tree includes the APIs the plugins expect. If you see a compatibility error, update cherrybox `dev` and re-run the workflow.

## Local vs CI layout

| Layout | Theme source |
|--------|----------------|
| Submodule (`cherrybox/plugins/`) | `../web/public/skins` (auto-detected) |
| Plugins CI | `_cherrybox/web/public/skins` (checked out by the workflow) |

`build-packages.ps1` accepts `-ThemeSource` when the theme folder is not auto-detected.
