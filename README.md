# Living Lore Dialogue

Living Lore Dialogue is a SMAPI mod scaffold for Stardew Valley 1.6. It uses a local SQLite lore database and the OpenAI API to generate short, lore-consistent NPC dialogue.

The project also includes a localhost-only ASP.NET Core dashboard for managing the SQLite lore database outside the game.

## Setup

1. Install Stardew Valley 1.6 and SMAPI 4.x.
2. Install the .NET SDK needed for Stardew Valley 1.6 mod builds.
3. Set your OpenAI API key in your shell or system environment:

   ```powershell
   setx OPENAI_API_KEY "your-api-key"
   ```

4. Build the SMAPI mod project:

   ```powershell
   dotnet restore
   dotnet build
   ```

   If the SMAPI build package cannot find your Stardew Valley install, pass the game path explicitly:

   ```powershell
   dotnet build /p:GamePath="C:\Program Files (x86)\Steam\steamapps\common\Stardew Valley"
   ```

5. Copy the build output folder into your Stardew Valley `Mods` folder, or let the SMAPI mod build package deploy it to your game path.
6. Start the game through SMAPI. On first run, the mod creates `ValleyLedger.db` in the mod folder and loads the sample seed data if enabled.

## Personal Drop-In Install

This is the easiest way to use the mod locally. One folder contains everything — the SMAPI
mod plus a bundled, self-contained dashboard/API — and the dashboard starts automatically when
you launch the game. You do **not** need to run the dashboard in a separate terminal.

> Personal local use only for now. This prioritizes easy startup over production deployment.

1. **Package the mod and dashboard** (build the SMAPI mod + publish the dashboard into one
   folder named `Living Lore Dialogue`):

   ```powershell
   # Portable build (no Stardew install needed for the output location):
   ./package-local-mod.ps1 -OutputFolder "$env:USERPROFILE\Documents\LivingLoreBuild"

   # Or install straight into your Mods folder:
   ./package-local-mod.ps1 -StardewModsFolder "C:\path\to\Stardew Valley\Mods"
   ```

2. **Copy the generated `Living Lore Dialogue` folder into `Stardew Valley/Mods`** (skip this if
   you used `-StardewModsFolder`, which already copies it there).

3. **Launch Stardew Valley through SMAPI.**

4. **The dashboard starts automatically** as a localhost-only child process (controlled by
   `EnableLocalDashboardAutoStart` in `config.json`). The mod waits for its
   `GET /api/health` endpoint, then uses it for dialogue. If the dashboard can't start, the mod
   logs a warning and the game continues normally.

5. **Open the dashboard manually if you want to view it:** <http://localhost:5077>.

The packaged folder looks like:

```text
Living Lore Dialogue/
├── manifest.json
├── LivingLoreDialogue.dll
├── config.json
├── ValleyLedger.db
├── Data/
│   ├── schema.sql
│   └── seed.sql
├── Dashboard/                     # self-contained dashboard/API (auto-started)
│   ├── LivingLoreDialogue.Web.exe
│   ├── appsettings.json
│   └── (published runtime files)
└── (dependency DLLs + runtimes/)  # SMAPI loads mod dependencies from the mod root
```

Relevant `config.json` options:

| Option | Default | Purpose |
| --- | --- | --- |
| `EnableLocalDashboardAutoStart` | `true` | Start the dashboard automatically on game launch. |
| `LocalDashboardPort` | `5077` | Localhost port the dashboard binds to. |
| `LocalDashboardRelativePath` | `Dashboard/LivingLoreDialogue.Web.exe` | Dashboard exe path, relative to the mod folder. |
| `LocalWebApiBaseUrl` | `http://localhost:5077` | Base URL the mod calls for dialogue + health. |
| `DashboardStartupTimeoutSeconds` | `10` | How long to wait for the health endpoint. |
| `OpenDashboardBrowserOnLaunch` | `false` | Open the dashboard in a browser on launch. |

Notes:

- The dashboard binds to **localhost only** and the OpenAI API key is never sent to the browser.
  The local `openai-api-key.txt` key file is supported for personal development and is copied
  into `Dashboard/` during packaging; it is not deleted, and environment variables are not
  required.
- If port `5077` is already serving a healthy Living Lore dashboard, the mod reuses it instead
  of starting a second one. If the port is held by a different app, the mod logs a clear error
  and continues without auto-starting.
- SMAPI loads a mod's dependency DLLs from the mod folder root, so the SQLite/runtime DLLs live
  in the root rather than a `Dependencies/` subfolder.

## Local Personal Install

For day-to-day local use, the repo includes `package-local-mod.ps1`, which builds the
SMAPI mod and copies **only the required runtime files** into your Stardew Valley `Mods`
folder. It never copies source files, `.git`, `.claude`, `obj`/`bin` source trees, or the
web dashboard source.

The web dashboard stays a **separate localhost app**. The in-game mod talks to it over
HTTP using the `LocalWebApiBaseUrl` setting in `config.json` (default
`http://localhost:5077`). Do not put the dashboard inside your Stardew `Mods` folder.

Typical workflow:

1. **Start the dashboard** (separate localhost app, leave it running):

   ```powershell
   dotnet run --project LivingLoreDialogue.Web
   ```

2. **Package the SMAPI mod.** Pass exactly one destination mode:

   **Portable package** — copy the build into any folder (e.g. Documents). The output
   target does not need to be a Stardew Valley install, game folder, or Mods folder:

   ```powershell
   ./package-local-mod.ps1 -OutputFolder "$env:USERPROFILE\Documents\LivingLoreBuild"
   ```

   **Direct install** — copy straight into your Stardew Valley Mods folder:

   ```powershell
   ./package-local-mod.ps1 -StardewModsFolder "C:\path\to\Stardew Valley\Mods"
   ```

   Either way it builds in Release, then copies into `<target>\Living Lore Dialogue`:
   `LivingLoreDialogue.dll`, `manifest.json`, `config.json`, `Data\schema.sql`,
   `Data\seed.sql`, `ValleyLedger.db` (if present), and the dependency DLLs / native
   SQLite runtime libraries from the build output.

   Re-running the script preserves your existing `config.json` and `ValleyLedger.db`.
   To overwrite them, pass the matching flag:

   ```powershell
   ./package-local-mod.ps1 -StardewModsFolder "C:\...\Mods" -ResetConfig -ResetDatabase
   ```

   If the build can't auto-detect your game install, add
   `-GamePath "C:\path\to\Stardew Valley"`. Note: compiling the mod still references the
   SMAPI/Stardew assemblies, so the game must be installed *somewhere* to build — portable
   mode only frees the *output* location, not the build's reference assemblies.

3. **Launch Stardew Valley through SMAPI.**

4. **Generate dialogue from the SMAPI console:**

   ```text
   livinglore_dialogue Lance general
   ```

   (Usage: `livinglore_dialogue <npcName> [topic]`.) The mod forwards the request to the
   running dashboard at `LocalWebApiBaseUrl`, so make sure step 1 is still running.

## Local Web Dashboard

The dashboard lives in `LivingLoreDialogue.Web` and binds only to `localhost` on port `5077`.

Run it with:

```powershell
dotnet run --project .\LivingLoreDialogue.Web\LivingLoreDialogue.Web.csproj
```

Then open:

```text
http://localhost:5077
```

The dashboard uses the same `ValleyLedger.db` file by default. Its configuration is in `LivingLoreDialogue.Web/appsettings.json`:

- `DatabasePath`: SQLite database path.
- `OpenAiApiKeyEnvironmentVariable`: environment variable that contains the OpenAI API key.
- `OpenAiModel`: model used for test dialogue generation.
- `ModsFolderPath`: Stardew Valley `Mods` folder to scan from the web UI.
- `EnableLiveInGameDialogueGeneration`: local setting surfaced for the mod workflow.

The browser never receives the API key. Set the key in your environment before running the dashboard:

```powershell
$env:OPENAI_API_KEY = "your-api-key"
dotnet run --project .\LivingLoreDialogue.Web\LivingLoreDialogue.Web.csproj
```

The dashboard provides pages for:

- Dashboard status, character counts, detected mods, recent lore changes, and recent generated dialogue.
- Characters, character details, and user lore overrides.
- Memories, including create/edit and importance marking.
- Relationships, including create/edit and strength.
- Mods and local folder scans.
- Lore conflicts, with manual reviewed marking.
- Dialogue testing, including prompt display and persisted dialogue history.
- Dialogue Context, showing scanned dialogue sources and source summaries for a canonical character.
- Override Review, for approving generated dialogue candidates and exporting them as a local Content Patcher pack.
- Local settings.

The dashboard manual scan and the SMAPI startup scan use the same pipeline:

1. `ModScanCoordinator.RunScanAsync(triggerSource)`
2. `ModScannerService.ScanAsync(modsFolderPath)`
3. `CharacterValidationService.Validate(...)`
4. `CanonicalCharacterRepository.ResolveCandidateAsync(...)`
5. `CharacterSyncService.SyncAsync(scanResult)`
6. `ScanHistoryRepository.AddAsync(...)`

There is no separate dashboard scanner. Characters are never deleted by this workflow. Missing mod characters are marked inactive, and reappearing characters are reactivated.

The same scan also reads existing mod dialogue sources into `DialogueSources` and maintains per-character `DialogueSourceSummaries`. The prompt builder uses those scanned lines as examples, but treats them as protected source material: generated dialogue can extend the character, but should not rewrite or delete mod-authored dialogue.

Scanned NPCs resolve into `CanonicalCharacters`, with individual detected records and source mods attached through `Characters` and `CharacterSources`. Extension mods that target the same NPC merge into the existing canonical profile instead of creating duplicate profiles. For example, Stardew Valley Expanded's `Lance` and Homewrecker Lance both resolve to one canonical `Lance` profile with multiple source rows.

Uncertain matches go to the `Merge Review` dashboard page. User actions there are persisted as `CharacterMergeRules` with `CreatedBy = "User"`.

## REST API

The localhost API includes:

- `GET /api/characters`
- `GET /api/characters/{id}`
- `POST /api/characters/{id}/overrides`
- `GET /api/memories`
- `POST /api/memories`
- `PUT /api/memories/{id}`
- `GET /api/relationships`
- `POST /api/relationships`
- `PUT /api/relationships/{id}`
- `GET /api/mods`
- `POST /api/mods/scan`
- `GET /api/conflicts`
- `POST /api/dialogue/test`
- `POST /api/dialogue/generate`
- `POST /api/dialogue-sources/scan`
- `GET /api/dialogue/context/{canonicalId}`
- `GET /api/dialogue/overrides`
- `POST /api/dialogue/overrides/{id}/approve`
- `POST /api/dialogue/overrides/{id}/enable`
- `POST /api/dialogue/export`

## Dialogue Source And Override Workflow

Dialogue generation now builds context from the canonical character profile, active character sources, scanned mod dialogue, user overrides, memories, relationships, recent dialogue history, lore events, and the current request/save context that SMAPI or the dashboard provides.

Priority order:

1. User overrides.
2. User-confirmed merge rules.
3. Extension mods.
4. Base character mod.
5. Vanilla data.
6. AI-generated filler.

Generated dialogue is stored in `GeneratedDialogueHistory` and also saved as a disabled, unapproved candidate in `GeneratedDialogueOverrides`. Use the `Override Review` page to approve and enable individual candidates. Export writes a local Content Patcher-compatible `content.json` under `DialogueOverrideContentPack` next to the configured database path. The API key remains server-side and is never returned to the browser.

The save context is read-only and is used only to shape prompts. It can include season, weather, location, friendship level, relationship context, player/farm names, spouse or dating context, seen events, quests, community progress, date, and special-day state when those values are supplied by the game integration.

## Player Lore Profiles

Player Profiles let you store multiple farmer/player lore characters (one per save or roleplay) and
weave the selected one into dialogue generation, so NPCs reference your player's backstory,
personality, relationships, and memories — while still sounding like the NPC.

Open the **Player Profiles** page to create profiles (name, farmer/farm name, linked save file,
description, backstory, personality, roleplay style, preferred tone, important history, current
goals, relationship notes, custom lore). Open a profile's detail page to add **character-specific
relationship notes** (e.g. "Lance: married, playful rivalry, trusts him completely") and **player
memories** (e.g. "Player married Lance in Year 2"), and to **link save files**.

Player-context priority during generation:

1. Current save file state (wins on any conflict — e.g. marriage).
2. Linked `PlayerProfile` (auto-selected by save file, else the active/default profile).
3. `PlayerProfileRelationships` for the target character.
4. `PlayerProfileMemories` for the target character.
5. `UserLoreOverrides`.
6. Existing character dialogue sources.
7. AI-generated filler.

Profile resolution order: an explicitly selected profile (Dialogue Test / Simulation dropdown) →
the profile linked to the current save file → the active/default profile → none (generation still
works). Player lore shapes *what* the NPC references, never the NPC's own voice, and never
overrides the current save state. Profiles are archived (deactivated) rather than hard-deleted
unless you explicitly confirm a delete. The **Dialogue Explanation** page shows the Player Profile,
relationship notes, memories, and save-file link used for each line.

### Manual test steps

1. On **Player Profiles**, create a profile named `Rhea` (set personality/backstory/custom lore).
2. Link it to a save name (real or fake, e.g. `Rhea_123`) via the profile detail page, and **Set Active**.
3. On Rhea's detail page, add a **Lance** relationship note: type `married`, description "flirtatious, trusted field partner".
4. Add a **Magnus** relationship note: type `rival`, description "professional tension, distrust".
5. On **Dialogue Test**, pick character `Lance`, topic `spouse`, Player Profile `Rhea`, and generate.
6. Confirm the line references the relationship naturally (and check **Player Lore Used**).
7. Generate dialogue for `Magnus` with the same profile.
8. Confirm the output reflects the tension/distrust.
9. Archive the profile (or pick "None") and generate dialogue with no player profile.
10. Confirm the system still generates dialogue normally with no errors.

## Testing In Game

Use the SMAPI console command:

```text
livinglore_dialogue Lance marriage
```

The first argument is the NPC name. The optional second argument is a topic.

By default, `config.json` can route dialogue generation through the local dashboard API:

```json
{
  "ModsFolderPath": "",
  "UseLocalWebApiForDialogue": true,
  "LocalWebApiBaseUrl": "http://localhost:5077",
  "EnableLiveInGameDialogueGeneration": true
}
```

Keep the dashboard running while playing if `UseLocalWebApiForDialogue` is enabled. Set it to `false` to let the SMAPI mod call OpenAI directly.

If `ModsFolderPath` is empty in the SMAPI config, the mod uses the parent folder of its own install folder, which is normally Stardew Valley's `Mods` directory.

## Database Files

- `Data/schema.sql` defines the lore tables.
- `Data/seed.sql` includes sample data for Lance and a few lore anchors.
- `ValleyLedger.db` is created at runtime next to the mod files.

## Dynamic Mod Detection

On game launch, the mod scans installed SMAPI mods and content packs for NPC-related JSON data, including Content Patcher targets like `Data/Characters`, `Data/NPCDispositions`, and `Characters/Dialogue`.

The database is a persistent memory layer, not the source of truth for whether a character currently exists. Characters are never deleted automatically. If a source mod or NPC disappears, the character is marked inactive and all memories, relationships, voice rules, dialogue examples, history, and user overrides remain in place. If the character is found again later, the sync process reactivates the record.

Lore priority for prompt generation:

1. `UserLoreOverrides`
2. Stored `Memories`
3. Current scanned mod data
4. Vanilla/default data

Mod updates create rows in `CharacterHistory` and `LoreChangeLog` so you can review what changed without losing prior lore.

## Output Shape

The OpenAI service asks for JSON in this shape:

```json
{
  "character": "Lance",
  "dialogue": "...",
  "emotion": "happy",
  "topic": "marriage"
}
```

Generated dialogue is stored as a memory so the system can reference prior conversations.

When generated through the web API, dialogue is also stored in `GeneratedDialogueHistory` with the prompt used, scene context, returned text, emotion, and timestamp.

## Manual Scan Tests

Use these checks after changes to scanning:

1. Run the dashboard, set `ModsFolderPath`, click `Scan Mods Folder`, and verify the scan summary and history update.
2. Start Stardew through SMAPI and verify a `SMAPI Startup` scan appears in `ScanHistory`.
3. Change a test mod character's metadata, scan again, and verify memories, relationships, dialogue history, and user overrides remain.
4. Remove or disable a test mod, scan again, and verify its characters are marked inactive rather than deleted.
5. Restore the test mod, scan again, and verify those characters are reactivated.
6. Confirm scan history contains both `Dashboard` and `SMAPI Startup` trigger sources.
7. Open `Dialogue Context`, select Lance or another canonical character, and verify existing dialogue examples and summaries appear after a scan.
8. Generate test dialogue, then confirm `Override Review` shows a disabled, unapproved candidate with the prompt/save context retained.
9. Approve and enable one candidate, export overrides, and verify `DialogueOverrideContentPack/content.json` is written without overwriting the original mod files.
