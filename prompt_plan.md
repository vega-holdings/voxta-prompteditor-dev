# Voxta Prompt Editor - Architecture & Plan

## Purpose

**Native SillyTavern Preset Integration for Voxta**

Instead of using a proxy to inject instructions at runtime, this tool:
1. Converts SillyTavern presets to Scriban include files
2. Lets users edit Voxta's core templates to add `{{ include 'Presets/X/Main' }}`
3. Instructions become native to Voxta - no proxy needed

## Voxta Template Structure

Templates live in: `Resources/Prompts/Default/en/`

Key templates for text generation:
- `TextGen/ChatInstructSystemMessage.scriban` - Main system message
- `TextGen/ChatInstructUserMessage.scriban` - User message with character profiles, scenario, etc.
- `TextGen/Includes/` - Reusable components (Intro, MainCharacterProfile, etc.)

Converted presets go to:
- `TextGen/Includes/Presets/[PresetName]/` - Individual prompt files
- `TextGen/Includes/Presets/[PresetName]/Main.scriban` - Master include that pulls all prompts

## Workflow

1. **Import** SillyTavern preset JSON (Presets tab)
2. **Convert** to Scriban (Converter tab) - creates files in `Includes/Presets/`
3. **Edit** core template (Templates tab) - add `{{ include 'Presets/[name]/Main' }}`
4. **Save** - instructions are now native to Voxta

## Architecture

### Backend: StaticFileServer.cs (Sidecar HTTP Server)

Runs on random port, proxied through Voxta at `/manage/prompt-editor`

**API Endpoints:**
- `GET /languages` - List available languages (en, etc.)
- `GET /collections` - List template backup collections
- `GET /categories` - List template categories (TextGen, ActionInference, etc.)
- `GET /templates` - List templates in a category
- `GET /template` - Get template content
- `PUT /template` - Save template content
- `POST /collections/create` - Create backup collection from Live
- `POST /collections/apply` - Apply collection back to Live
- `POST /originals/restore` - Restore original templates
- `GET /presets` - List imported SillyTavern presets
- `GET /presets/:name` - Get preset data
- `POST /presets` - Import new preset
- `PUT /presets/:name` - Update preset
- `DELETE /presets/:name` - Delete preset
- `POST /presets/:name/convert` - Convert preset to Scriban
- `GET /converted-presets` - List converted presets (directories in Includes/Presets/)

**Key Paths:**
- `_liveRoot` = Resources/Prompts/Default/en/ (actual Voxta templates)
- `_collectionsRoot` = Data/PromptCollections/ (backups)
- `_presetsRoot` = Data/Presets/ (imported SillyTavern JSON files)

### Frontend: public/js/app.js

Three tabs:
1. **Templates** - Browse and edit actual Voxta templates
   - Source: Live (actual templates) or Collection (backups)
   - Category/Template selectors
   - **Insert Preset Include** helper - easily add `{{ include 'Presets/X/Main' }}`
   - Save/Reload buttons

2. **Presets** - Manage SillyTavern presets
   - Import/Export JSON
   - Edit prompts, enable/disable
   - Sampling parameters
   - Delete preset

3. **Converter** - Convert presets to Scriban
   - Select preset, target collection
   - Preview conversion
   - Convert & Save

## SillyTavern to Scriban Conversion

### Macro Mapping:
- `{{char}}`, `{{charname}}` -> `{{ char }}`
- `{{user}}`, `{{username}}` -> `{{ user }}`
- `{{personality}}` -> `{{ char_personality | join_newlines }}`
- `{{description}}` -> `{{ char_description | join_newlines }}`
- `{{scenario}}` -> `{{ scenario }}`
- `{{persona}}` -> `{{ user_description }}`
- `{{group}}` -> `{{ other_chars | array.join ", " }}`
- `<BOT>`, `<USER>` -> `{{ char }}`, `{{ user }}`

### Special Handling:
- `{{setvar::name::value}}` -> Extract value only (the actual content)
- `{{getvar::name}}` -> `{{ name | default: "" }}`
- `{{// comment }}` -> Removed
- `{{trim}}` -> Removed
- `{{newline}}` -> `\n`

### Main.scriban Generation:
- Creates master include that pulls all prompt files in order
- **Skips "Main"** prompts to avoid infinite recursion
- **Skips duplicates** (e.g., `<tag>` and `</tag>` that resolve to same filename)

## File Naming

`SanitizeName()` function:
1. Remove ALL non-ASCII characters first (`[^\x00-\x7F]`)
2. Keep only alphanumeric, spaces, hyphens, underscores
3. Replace spaces with hyphens
4. Collapse multiple hyphens
5. Trim leading/trailing hyphens

Example: `✎ Game-Master` -> `Game-Master`

## Voxta Scriban Variables Reference

From char object:
- `{{ char }}` - Character name
- `{{ char_description | join_newlines }}` - Character description
- `{{ char_personality | join_newlines }}` - Character personality
- `{{ char_message_examples | join_newlines }}` - Example messages

From user:
- `{{ user }}` - User name
- `{{ user_description }}` - User description/persona

Context:
- `{{ scenario }}` - Current scenario
- `{{ summary }}` - Conversation summary
- `{{ other_chars | array.join ", " }}` - Other characters in group
- `{{ now }}` - Current date/time

Messages:
- `{{ messages }}` - Chat history array
- `{{ include 'Messages' messages }}` - Render message history

## Deployment to Voxta.DesktopApp

**IMPORTANT**: Voxta loads modules from `Modules\Voxta.Modules.PromptEditor.dll` (parent folder), NOT from the subfolder!

Copy files to TWO locations:
1. `Voxta.DesktopApp.v1.3.0\Modules\Voxta.Modules.PromptEditor.dll` ← **Main DLL (required)**
2. `Voxta.DesktopApp.v1.3.0\Modules\Voxta.Modules.PromptEditor\` ← **Public folder and assets**

Build and deploy commands:
```bash
# Stop Voxta first
powershell -command "Stop-Process -Name 'Voxta.Server' -Force -ErrorAction SilentlyContinue"

# Build
dotnet build Voxta.Modules.PromptEditor.csproj -c Debug

# Copy DLL to parent Modules folder (where Voxta loads it from)
copy bin\Debug\net10.0\Voxta.Modules.PromptEditor.dll ..\Voxta.DesktopApp.v1.3.0\Modules\

# Copy public folder and other assets to subfolder
xcopy /E /Y bin\Debug\net10.0\* ..\Voxta.DesktopApp.v1.3.0\Modules\Voxta.Modules.PromptEditor\

# Restart Voxta from its directory
cd ..\Voxta.DesktopApp.v1.3.0 && Voxta.Server.exe
```

## Version

Current: 2024-12-30-v6

## Changelog

### v6 (2024-12-30)
- Added **Active Presets** tab as the primary interface (like proxy app)
- Toggle prompts on/off with visual feedback (strikethrough, gray toggle)
- Auto-regenerate `Main.scriban` when toggling prompts
- Store enabled state in `_config.json` per converted preset
- Click to expand prompts and edit content inline
- Copy include statement to clipboard
- Enable All / Disable All buttons
- Search filtering for prompts
