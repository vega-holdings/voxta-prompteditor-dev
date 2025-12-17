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

## Install
- Copy `Voxta.Modules.PromptEditor.dll` into:
  - `Voxta.Server.Win.v1.2.0/Modules/` (if Voxta is stopped), or
  - `Voxta.Server.Win.v1.2.0/Modules/_incoming/` (if Voxta is running)
- Restart Voxta (or wait for it to load `_incoming`), then confirm `PromptEditor` is listed in the startup “Loading Voxta modules” line.

## Folder layout
- Live prompts: `Voxta.Server.Win.v1.2.0/Resources/Prompts/Default/`
- Module data: `Voxta.Server.Win.v1.2.0/Data/PromptEditor/`
  - Originals backup: `Data/PromptEditor/Originals/<lang>/...`
  - Collections: `Data/PromptEditor/Collections/<collection>/<lang>/...`

## Usage
### Recommended UI (reactive)
Open: `http://localhost:5384/manage/prompt-editor` (or `https://<your-domain>/manage/prompt-editor`)

Tip: the Voxta Modules page shows a green **Help** button on the PromptEditor module card that opens `/manage/prompt-editor` in a new tab (this is `HelpLink` in the module definition).

#### Edit prompts
1) Pick `Editing source`:
   - `Live` edits `Resources/Prompts/Default/<lang>/...` directly.
   - `Collection` edits `Data/PromptEditor/Collections/<name>/<lang>/...`.
2) Pick `Language` → `Category folder` → `Template`.
3) Edit and click **Save** (or `Ctrl+S`).

### Module config UI (quick collection actions)
The module configuration page no longer includes template editing. It only contains quick actions for creating/applying/restoring collections (language-scoped).

Note: Voxta `InvokeAction` buttons use **saved** values only. If an action depends on inputs (collection name, language), click **Save** first, then click the button.

### Using collections (recommended)
1) In `/manage/prompt-editor`, select the language you want (default `en`), enter `New collection`, then click **Create from Live (language)**.
2) Make edits with `Editing source = Collection` selected.
3) Click **Apply collection to Live (language)** to switch Live prompts for that language.
4) Click **Restore Originals to Live (language)** to revert that language back to the first backup this module created.

## Notes / Safety
- Applying a collection restores the Originals backup first, then overlays the collection onto Live (so missing files fall back to Originals).
- The Originals backup is created once per language (first time you write/apply/restore for that language); if Live was already modified before that, the backup will reflect the modified state.
