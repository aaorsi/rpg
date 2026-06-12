# Developer setup (Unity + Ollama slice)

## Requirements

- Unity **6.4** (project version file targets **6000.4.x**; use the same minor line as your installed editor, e.g. `6000.4.2f1`).
- [Ollama](https://ollama.com/) installed and running locally.
- A small chat model pulled, for example:

```bash
ollama pull llama3.2
```

## Configure the game

1. Open this folder as a Unity project.
2. Wait for Package Manager to resolve (`com.unity.ai.navigation`, `com.unity.probuilder`, `com.unity.nuget.newtonsoft-json`, `com.unity.ugui`).
3. Edit **[Assets/Resources/DefaultOllamaSettings.asset](Assets/Resources/DefaultOllamaSettings.asset)** if needed:
   - `baseUrl` — default `http://127.0.0.1:11434`
   - `model` — must match an installed Ollama model name (e.g. `llama3.2`)

HTTP to localhost is allowed in **Player Settings** via `insecureHttpOption` for this prototype.

## Ollama connectivity (from the game)

With the dialogue panel open, click **Test Ollama** (top-right). That runs **`GET {baseUrl}/api/tags`** (same host as chat) and prints one line in the log:

Implementation note: HTTP completion is driven by **`UnityWebRequestAsyncOperation.completed`** and marshalled with **`SynchronizationContext`** so results are applied on the **main thread** (polling with `await Task.Yield()` can leave the main thread and break both UnityWebRequest and UI updates).

- **Reachable** — Ollama answered; the message also says whether your configured **model** name appears in the tag list (if not, run `ollama pull <name>` or fix **DefaultOllamaSettings**).
- **Cannot reach** — wrong URL, firewall, or Ollama not running (`ollama serve` / the desktop app).

From a terminal you can double-check:

```bash
curl -s http://127.0.0.1:11434/api/tags | head
```

## Startup title UI (TMP prefab)

After cloning or if the title screen is missing, open the project in Unity and run **RPG → Build Startup Title Screen Prefab**. That writes `Assets/Resources/UI/StartupTitleScreenPanel.prefab` (TextMeshPro + layout). Without it, Play shows a small fallback screen that continues with local Ollama defaults.

## Ollama Cloud (optional)

On the startup **title** screen, choose **Ollama Cloud** instead of **This machine** if you want inference on [ollama.com](https://ollama.com/) without a local daemon. Create an API key at [ollama.com/settings/keys](https://ollama.com/settings/keys). The game uses base URL `https://ollama.com` and sends `Authorization: Bearer <key>` on `/api/tags` and `/api/chat` (same JSON as local). The model field defaults to `gemma3:4b` when you switch to cloud; use **Refresh model list** to query tags with your current settings. When the title phase is disabled in the bootstrap, the game keeps **DefaultOllamaSettings** (local URL and model) as before.

## UI typing / clicks (Unity 6)

This slice uses the legacy **Standalone Input Module** on the generated **EventSystem** (no `com.unity.inputsystem` dependency — avoids version mismatches with some Unity 6000.4 editor builds).

Set **Edit → Project Settings → Player → Active Input Handling** to **Input Manager (Old)** or **Both**. If it is **Input System Package** only, the UI may not receive mouse/keyboard until you switch to **Both** or **Old**. Remove duplicate **EventSystem** objects if clicks still do nothing.

## Scene view (edit mode)

Opening **Room_Prototype** should spawn a child **`_SliceContent`** under **App**: floor (built-in plane mesh), player and NPC (built-in capsule meshes), default lighting, and a main camera. That uses **`[ExecuteAlways]`** on `RuntimeLevelBootstrap` so primitives appear **without** pressing Play.

Use **File → Save** (or Save Project) once so `_SliceContent` stays in the scene on disk. To force a rebuild, delete the **`_SliceContent`** object under **App** and save; it will be recreated the next time the scene loads.

## Playtest

1. Open **[Assets/Scenes/Room_Prototype.unity](Assets/Scenes/Room_Prototype.unity)**.
2. Press **Play**.
3. **Click** the floor to walk. Move near the NPC capsule.
4. Press **E** to talk. Introduce yourself; then ask what year it is.
5. The NPC must answer with the canonical year from **WorldStateService** (see Hierarchy `Managers` at runtime). If Ollama is down, you should see a fallback line.

## Controls

- **Left click** on the floor — move (NavMesh); optional for dialogue in the current test setup.
- **E** — start talking. **`PlayerInteractor`** defaults to **not** requiring trigger range: **E** finds the first `NpcDialogueBinding` in the scene so you can test dialogue from anywhere. Enable **Require In Trigger Range** on the Player’s `PlayerInteractor` component when you want proximity-only interaction again.
- **Enter** — send the line in the chat box (when the dialogue panel is open; hold **Shift+Enter** for a newline if you switch the input to multi-line later).
- **Escape** — close dialogue

## Authoring

- **NPC persona**: [Assets/Resources/DefaultNpc.asset](Assets/Resources/DefaultNpc.asset) or create new `NpcDefinition` assets under `Assets/ScriptableObjects/NPCs/`.
- **Prompt template**: [Assets/StreamingAssets/Dialogue/npc_system_template.txt](Assets/StreamingAssets/Dialogue/npc_system_template.txt)

## Built-in geometry, Unity Registry packages, and free content

This project uses the **Built-in Render Pipeline** (see **Project Settings → Graphics → Scriptable Render Pipeline** is empty). Prefer assets and samples that work with **Built-in**; URP/HDRP-only materials need conversion or a pipeline change.

### Folder layout

These folders exist in the repo (placeholders via `.gitkeep`); Unity adds `.meta` files on import:

- **[Assets/ThirdParty/](Assets/ThirdParty/)** — raw imports from the **Asset Store** or `.unitypackage` drops (do not hand-edit vendor files here).
- **[Assets/Art/](Assets/Art/)** — prefabs, materials, and meshes you **reference from scenes and scripts** (curated copies or variants of Store content).

### Unity Registry packages (ProBuilder)

**ProBuilder** (`com.unity.probuilder`, **6.0.9**) is listed in **[Packages/manifest.json](Packages/manifest.json)**. After the editor resolves packages, use it for greybox meshes, rooms, and collision-friendly geometry.

If your Unity minor version cannot resolve **6.0.9**, change the version string in `manifest.json` to one shown in **Window → Package Manager** for ProBuilder, or install ProBuilder from the Registry UI and let Unity rewrite the manifest.

- After resolve: **Tools → ProBuilder → ProBuilder Window** (Unity 6).
- **Built-in RP:** default ProBuilder materials work out of the box. If you later switch to URP/HDRP, use **Window → Package Manager → ProBuilder → Samples** and import the matching **shader support** sample for that pipeline.

To add **more** official tools later: **Window → Package Manager**, set the dropdown to **Unity Registry**, pick a package (e.g. **Terrain Tools**, **Polybrush**), click **Install**. Prefer versions listed as compatible with your editor (6000.4.x).

### Package samples (copy into `Assets/` before customizing)

1. **Window → Package Manager** → select the package (e.g. ProBuilder).
2. Expand **Samples** at the bottom of the package details.
3. Click **Import** on a sample. Unity usually places content under **`Assets/Samples/<Package>/<version>/`**.
4. For long-term edits, **duplicate** assets into **`Assets/Art/`** as prefab variants or copies so upgrades to the package do not overwrite your work.

Do **not** edit assets under **`Library/PackageCache/`** — they are regenerated.

### Built-in menu assets (no install)

Use **GameObject → 3D Object** (Cube, Capsule, Plane, Terrain, etc.) and **Component → Physics** as starting points. The current slice already uses primitives from code in `RuntimeLevelBootstrap`; you can place additional built-in objects in the scene for blocking and props.

**Terrain:** **GameObject → 3D Object → Terrain** uses the built-in terrain system. Walkable area must be included in **NavMesh** baking (e.g. **NavMeshSurface** on the terrain or a child object, or mark objects static and bake from the Navigation window — match how your scene is set up).

### Unity Asset Store (free packs)

1. **Window → Asset Store** (or browser → [assetstore.unity.com](https://assetstore.unity.com)) while logged into your Unity ID.
2. Download/import into the project; keep unpacked files under **`Assets/ThirdParty/<PackName>/`** when possible.
3. Check each asset’s **license** and **render pipeline** (Built-in vs URP) before relying on it in builds.
