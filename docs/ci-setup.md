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

## Local vs CI layout

| Layout | `CherryBoxRoot` resolves to |
|--------|----------------------------|
| Submodule (`cherrybox/plugins/`) | Parent cherrybox repo |
| Plugins CI | `_cherrybox/` (checked out by the workflow) |
