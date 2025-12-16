# PromptEditor (Core System Prompt Manager) — Voxta Module

Status: **alpha**.

## What it is
Admin-only module that lets you:
- Browse and edit Scriban templates under `Resources/Prompts/Default/<lang>/...`
- Keep an **originals** backup before overwriting live prompts
- Create/apply named **prompt collections** (swap prompt sets quickly)

## Folder layout
- Live prompts: `Voxta.Server.Win.v1.2.0/Resources/Prompts/Default/`
- Module data: `Voxta.Server.Win.v1.2.0/Data/PromptEditor/`
  - Originals backup: `Data/PromptEditor/Originals/<lang>/...`
  - Collections: `Data/PromptEditor/Collections/<collection>/<lang>/...`

## Usage (current flow)
1) Pick language/category/template.
2) Save to load the selected template into the editor.
3) Edit, then save again to write changes to disk (live or collection depending on selection).
4) Use the action buttons to create/apply/restore collections.

