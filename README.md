# PromptEditor (Core System Prompt Manager) — Voxta Module

Status: **alpha**.

## What it is
Admin-only module that lets you:
- Browse and edit Scriban templates under `Resources/Prompts/Default/<lang>/...`
- Keep an **originals** backup before overwriting live prompts
- Create/apply named **prompt collections** (swap prompt sets quickly)

## Requirements
- Voxta Server for Windows (tested with `Voxta.Server.Win.v1.2.0`)
- Admin access in the Voxta UI

## Folder layout
- Live prompts: `Voxta.Server.Win.v1.2.0/Resources/Prompts/Default/`
- Module data: `Voxta.Server.Win.v1.2.0/Data/PromptEditor/`
  - Originals backup: `Data/PromptEditor/Originals/<lang>/...`
  - Collections: `Data/PromptEditor/Collections/<collection>/<lang>/...`

## Usage
### Recommended UI (reactive)
Open: `http://localhost:5384/manage/prompt-editor` (or `https://<your-domain>/manage/prompt-editor`)

### Legacy module config UI (non-reactive)
1) Set `Editing Source` = `Live`.
2) Pick `Language` → `Category Folder` → `Template`.
3) Click **Save** to load it into the editor.
4) Edit and click **Save** again to write to disk.

Note: Voxta’s module configuration UI doesn’t live-refresh dependent dropdowns/content. After changing Language/Category/Template, click **Save** and then reload the config page to see updated lists/content.

### Using collections (recommended)
1) Click **Create Collection from Live** (set `New Collection Name`, **Save**, then click the button).
2) Set `Editing Source` = `Collection` and select the collection.
3) Edit templates inside the collection.
4) Click **Apply Collection to Live (selected language)** when you want to switch Live prompts.
5) Click **Restore Originals to Live (selected language)** to revert.
