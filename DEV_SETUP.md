# Developer setup (Unity + Ollama RPG)

This project is a **Unity 6.4 Built-in RP** game with a **Python policy-orchestrator** sidecar. The git repo tracks **scripts, dialogue data, and minimal Unity configs** (~2 MB). **3D/audio assets are not in git** — you must re-import them after cloning.

## Fresh-clone checklist

1. Clone the repo and open the project root in **Unity 6000.4.x** (see `ProjectSettings/ProjectVersion.txt`).
2. Let **Package Manager** resolve dependencies from `Packages/manifest.json`.
3. Import every **Asset Store package** listed in [Asset Store packages](#asset-store-packages) (all are free unless noted).
4. Download **Mixamo** characters and locomotion clips (see [Mixamo](#mixamo-free-account-required)).
5. Rebuild **`Assets/Resources/` runtime mirrors** (see [Resources mirrors](#resources-mirrors-for-player-builds)) — editor play can load vendor paths directly, but standalone builds need these copies.
6. Open **`Assets/sc2.unity`** — the shipped build scene (see [Scenes](#scenes)). Tracked in git.
7. Optionally add **`Music/`** mp3 tracks and **`Assets/StreamingAssets/IslandTitle.png`** (see [Music and title art](#music-and-title-art)).
8. Start the **Python sidecar** if using orchestrated dialogue (see [Python policy orchestrator](#python-policy-orchestrator)).
9. Install **Ollama** and pull a chat model (see [Configure the game](#configure-the-game)).
10. If the title prefab is missing, run **RPG → Build Startup Title Screen Prefab** in the Unity editor.

**Render pipeline:** Built-in only. **Project Settings → Graphics → Scriptable Render Pipeline** must be empty. Several Store packs default to URP materials; follow each pack’s Built-in conversion notes where listed below.

---

## What git tracks vs what stays local

| Tracked in git | Local only (re-import or copy) |
|----------------|--------------------------------|
| `Assets/Scripts/` | All Asset Store folders under `Assets/` |
| `Assets/StreamingAssets/Dialogue/` | `Assets/Resources/` bulk mirrors (animals, Mixamo, props, audio, …) |
| `ProjectSettings/`, `Packages/` | `Library/`, `Logs/`, `UserSettings/` |
| Minimal configs: `DefaultOllamaSettings`, `sc2.unity`, `Room_Prototype`, `App.prefab`, UI prefabs | Asset Store 3D/audio packs under `Assets/` |
| `services/policy_orchestrator/` | `services/**/.venv/`, `Music/`, `IslandTitle.png` |

---

## Asset Store packages

Package names and IDs below come from Unity **`.meta` `AssetOrigin`** fields in this project. Import via **Window → Package Manager → My Assets**, or search [assetstore.unity.com](https://assetstore.unity.com) by name/ID while logged into your Unity ID.

| Package | ID | Project folder | Role in this game |
|---------|-----|----------------|-------------------|
| [Free Island Collection](https://assetstore.unity.com/packages/3d/environments/landscapes/free-island-collection-104753) | 104753 | `Assets/Free Island Collection/` | Island terrain/sky/water baseline; `Scene 2` was the layout starting point |
| [RPG Poly Pack - Lite](https://assetstore.unity.com/packages/3d/environments/landscapes/rpg-poly-pack-lite-148410) | 148410 | `Assets/RPGPP_LT/` | Village props (`rpgpp_lt_*` anchors: buildings, wagon, hills) |
| [Medieval Castle - Modular](https://assetstore.unity.com/packages/3d/environments/fantasy/medieval-castle-modular-282498) | 282498 | `Assets/Advance Studios/Medieval Castle/` | `Castle` location mesh/prefabs |
| [Old Warehouse](https://assetstore.unity.com/packages/3d/props/industrial/old-warehouse-116767) | 116767 | `Assets/AssetsStore/Warehouse/` | `Warehouse` dialogue anchor (`location_catalog.json`) |
| [Medieval props](https://assetstore.unity.com/packages/3d/props/medieval-props-41540) | 41540 | `Assets/Resources/Medieval props/` | Interior props, pickups (`BookV1`, mugs, beds, …) |
| [Mobile Books](https://assetstore.unity.com/packages/3d/props/mobile-books-3356) | 3356 | `Assets/Resources/Books/` | Quest spell books (`book_0001a`–`d`) |
| [Treasure Set - Free Chest](https://assetstore.unity.com/packages/3d/props/treasure-set-free-chest-72345) | 72345 | `Assets/ChestFree/` | Chest props |
| [Stylized Character Pack](https://assetstore.unity.com/packages/3d/characters/stylized-character-pack-360808) | 360808 | `Assets/StylizedCharacterPack/` | Playable hero lineup (Bat, Leopard, Rabbit, SeaGull, Sloth) |
| [npc_casual_set_00](https://assetstore.unity.com/packages/3d/characters/humanoids/humans/npc-casual-set-00-326131) | 326131 | `Assets/npc_casual_set_00/` | Modular casual humanoids → village NPCs |
| [City People FREE Samples](https://assetstore.unity.com/packages/3d/characters/city-people-free-samples-260446) | 260446 | `Assets/DenysAlmaral/CityPeople/` | Additional human NPC pool |
| [Animals FREE](https://assetstore.unity.com/packages/3d/characters/animals/animals-free-animated-low-poly-3d-models-260727) | 260727 | `Assets/ithappy/Animals_FREE/` | Ambient wildlife (chicken, horse, dog, kitty, tiger, …) |
| [Necromancer Army - Ghoul](https://assetstore.unity.com/packages/3d/characters/creatures/necromancer-army-ghoul-283690) | 283690 | `Assets/SimpleAssets/Necromancers/Ghoul/` | Story boss `Ghoul` |
| [Free Fantasy Spider](https://assetstore.unity.com/packages/3d/characters/creatures/free-fantasy-spider-10104) | 10104 | `Assets/fantasySpider/` | Scene predators `spider_1` … `spider_5` |
| [Owl Statue](https://assetstore.unity.com/packages/3d/props/exterior/owl-statue-264588) | 264588 | `Assets/AK Studio Art/Owl Statue/` | Terrain owl statues + sidekick spawn anchors |
| [Sitting Lion Statue](https://assetstore.unity.com/packages/3d/props/exterior/sitting-lion-statue-260994) | 260994 | `Assets/AK Studio Art/Sitting Lion Statue/` | Terrain lion statues |
| [Magic Effects FREE](https://assetstore.unity.com/packages/vfx/particles/spells/magic-effects-free-247933) | 247933 | `Assets/Hovl Studio/Magic effects pack/` | Character-select VFX, spell bursts, lightning aura source |
| [#NVJOB Simple Water Shaders](https://assetstore.unity.com/packages/vfx/shaders/nvjob-simple-water-shaders-149916) | 149916 | `Assets/#NVJOB Water Shaders V2/` | `Water Surface Mirror` plane (underwater death logic) |
| [FREE SOUND COLLECTION](https://assetstore.unity.com/packages/audio/sound-fx/free-sound-collection-291913) | 291913 | `Assets/FREE SOUND PACK_TM(355)/` | UI/footstep/ambience SFX (see `RuntimeAudioClipLoader.cs`) |
| [Progress Bars - Customizable and Extensible](https://assetstore.unity.com/packages/tools/gui/progress-bars-customizable-and-extensible-health-bars-etc-268457) | 268457 | `Assets/InfinityPBR - Magic Pig Games/Progress Bar/` | Reference only; HUD is implemented in `HeroHealthBarHud.cs` |

### Package-specific import notes

- **City People FREE Samples** — materials import as URP by default. For Built-in, open `Assets/DenysAlmaral/CityPeople/URP&Built-in/` and import **`convert-to-BUILT-IN`** (documented in the pack readme).
- **Animals FREE** — use the **Built-in** demo/scene variant for Unity 6. Copy `*_001` prefabs into `Resources/AnimalsFree/` for runtime spawning (see below).
- **Stylized Character Pack** — Store listing targets URP; this project uses **editor-path fallback** (`Assets/StylizedCharacterPack/Prefabs/Characters`) and **`Resources/StylizedCharacterPack/Characters`** copies for builds. Copy prefabs into Resources after import.
- **Magic Effects FREE** — add **Bloom** post-processing for store-quality screenshots (pack readme). Default spell/selection paths are hard-coded in `RuntimeLevelBootstrap` / `PlayerCharacterSelectionStage` under `Assets/Hovl Studio/Magic effects pack/Prefabs/…`.
- **NVJOB water** — scene object must be named **`Water Surface Mirror`** for `PlayerUnderwaterDeathController`.
- **SUIMONO Water System** (ID 4387) — **not required**; only leftover gizmo icons exist under `Assets/Gizmos/`.

### Unity Registry packages (already in `Packages/manifest.json`)

Resolved automatically on project open:

- `com.unity.ai.navigation` — NavMesh / agents
- `com.unity.probuilder` — greybox tooling
- `com.unity.nuget.newtonsoft-json` — JSON in dialogue pipeline
- `com.unity.ugui` + **TextMesh Pro** (import TMP Essentials when prompted)
- `com.unity.postprocessing` — used by some environment packs

---

## Mixamo (free account required)

[Mixamo](https://www.mixamo.com/) provides the humanoid meshes and shared locomotion clips under `Assets/Resources/Mixamo/`. These are **not** Unity Asset Store packages.

**Characters** (`Assets/Resources/Mixamo/Characters/`) — FBX exports configured as **Humanoid** rigs. This project includes:

`Abe`, `Derek`, `Jones`, `Leonard`, `Louise`, `Maria W`, `Parasite`, `Pirate`, `Steve`, `Warrok W`, `Zlorp`

**Animations** (`Assets/Resources/Mixamo/Animations/`) — shared locomotion library used by `MixamoAnimationCatalog`, `MixamoHumanLocomotionDriver`, and villager idle/walk overrides on `RuntimeLevelBootstrap`:

`Idle1`, `Idle2`, `Walking`, `Running`, `Angry`, `Button Pushing`, `Defeated`, `Drunk Walk`, `Dying`, `Flying`, `Hit`, `jab`, `Kick`, `Opening`

After downloading from Mixamo: place FBX under the Resources paths above (keep `.meta` GUIDs if copying from an existing machine, or re-link references in the editor).

**Casual human mesh bases** (`Assets/Resources/CasualHumanMeshBases/`) — copies of `npc_csl_00_character_*.fbx` from **npc_casual_set_00** for embedded clip playback (`HumanLocomotionPlayableDriver`).

---

## Resources mirrors (for player builds)

Runtime bootstrap code loads from `Resources/` (see `GameConstants.cs` and `Bootstrap*Resources.cs`). In the **editor**, several loaders also probe vendor folders directly; **standalone builds** need the Resources copies.

| Resources path | Copy from |
|----------------|-----------|
| `AnimalsFree/*_001.prefab` | `ithappy/Animals_FREE/Prefabs/` (names ending `_001`) |
| `RpgBootstrap/Tiger_001`, `Kitty_001` | Subset of animal prefabs (defaults when bootstrap overrides are empty) |
| `NpcCasualCharacters/npc_csl_00_character_*` | `npc_casual_set_00/Prefabs/` |
| `CityPeopleCharacters/*` | `DenysAlmaral/CityPeople/Prefabs/` (humanoid character prefabs) |
| `StylizedCharacterPack/Characters/*` | `StylizedCharacterPack/Prefabs/Characters/` |
| `Bootstrap/StandaloneAvatarLineup.asset` | ScriptableObject listing the five stylized hero prefabs |
| `Mixamo/Characters/`, `Mixamo/Animations/` | Mixamo FBX (see above) |
| `CasualHumanMeshBases/` | `npc_casual_set_00` mesh FBX bases |
| `Medieval props/Prefabs/` | Already stored under Resources in this project layout |
| `Books/Prefabs/` | Mobile Books prefabs |
| `BundledAudio/**` | Subset of FREE SOUND COLLECTION wav → renamed (mapping in `RuntimeAudioClipLoader.cs`) |
| `Vfx/LightningAura.prefab` | Derived from Hovl **Magic Effects FREE** (lightning-style prefab) |
| `UI/RpgTitleUi.mat`, `RpgUiSimpleText.mat` | Small UI materials (tracked in git) |

Fastest path after import: copy the entire `Assets/Resources/` tree from a machine that already has a working project (excluding `.gitignore`’d caches), then re-open Unity.

---

## Music and title art

**Music** — `MusicDirector` reads looping tracks from the project-root **`Music/`** folder (gitignored):

- `Opening.mp3`
- `Ambient.mp3`, `Ambient 2.mp3`, `Ambient 3.mp3`
- `Victory.mp3`

Fallback: `Resources/BundledAudio/Music/*.wav` (lower-quality placeholders if mp3 folder is absent).

**Title splash image** — optional `Assets/StreamingAssets/IslandTitle.png` (gitignored). Without it, the title screen uses a procedural fallback.

---

## Scenes

| Scene | In git? | Purpose |
|-------|---------|---------|
| **`Assets/sc2.unity`** | Yes | **Primary build scene** (`EditorBuildSettings` enabled entry). Full island layout: castle, warehouse, ghoul, spiders, NPC guides, water, etc. |
| `Assets/Scenes/Room_Prototype.unity` | Yes | Dialogue/Ollama prototype slice (capsule + bootstrap floor) |
| `Assets/Scenes/App.prefab` | Yes | Shared app shell referenced by scenes |

After importing the Asset Store packs above, open **`Assets/sc2.unity`** directly. The scene references vendor prefabs by GUID — missing imports show as pink/missing prefabs until packs are installed. Key hierarchy names: `Castle`, `Warehouse`, `Ghoul`, `spider_*`, `Water Surface Mirror`, numbered houses/NPC anchors.

---

## Python policy orchestrator

Local FastAPI sidecar for dialogue policy, summaries, and narrative generation.

```bash
cd services/policy_orchestrator
python -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
uvicorn app.main:app --host 127.0.0.1 --port 8787 --reload
```

Tests: `pytest` (from the same venv).

JSON schemas under `Assets/StreamingAssets/Dialogue/schema/` are generated from Pydantic models:

```bash
python scripts/generate_schemas.py
```

Enable in **`DefaultOllamaSettings.asset`**: `usePythonPolicyOrchestrator`, `usePythonSummaryService`, `usePythonNarrativeGeneration`; default sidecar URL `http://127.0.0.1:8787`.

---

## Configure the game

### Requirements

- Unity **6000.4.x** (e.g. `6000.4.2f1` per `ProjectVersion.txt`).
- [Ollama](https://ollama.com/) installed locally.
- Example model:

```bash
ollama pull llama3.2
```

### Ollama settings

Edit **`Assets/Resources/DefaultOllamaSettings.asset`**:

- `baseUrl` — default `http://127.0.0.1:11434`
- `model` — must match a pulled Ollama model (e.g. `llama3.2` or `gemma3:4b`)

HTTP to localhost is allowed via Player Settings `insecureHttpOption`.

### Ollama connectivity test

With the dialogue panel open, click **Test Ollama** (runs `GET {baseUrl}/api/tags`). From terminal:

```bash
curl -s http://127.0.0.1:11434/api/tags | head
```

### Ollama Cloud (optional)

On the startup title screen, choose **Ollama Cloud** and paste an API key from [ollama.com/settings/keys](https://ollama.com/settings/keys). Keys are **runtime-only** (never commit them into `.asset` files). Base URL: `https://ollama.com`.

### Startup title UI

Run **RPG → Build Startup Title Screen Prefab** to regenerate `Assets/Resources/UI/StartupTitleScreenPanel.prefab` if missing.

### UI input (Unity 6)

Uses legacy **Standalone Input Module**. Set **Edit → Project Settings → Player → Active Input Handling** to **Input Manager (Old)** or **Both**.

---

## Playtest

**Full game:** open **`Assets/sc2.unity`** (once restored), press Play.

**Prototype slice:** open **`Assets/Scenes/Room_Prototype.unity`**, press Play, click floor to walk, press **E** to talk.

### Controls

- **Left click** — move (NavMesh)
- **E** — talk (`PlayerInteractor`; range check optional)
- **Enter** — send dialogue line
- **Escape** — close dialogue

---

## Authoring

- **NPC persona:** `Assets/Resources/DefaultNpc.asset` or new `NpcDefinition` assets under `Assets/ScriptableObjects/NPCs/`
- **Prompt template:** `Assets/StreamingAssets/Dialogue/npc_system_template.txt`
- **World/narrative data:** `Assets/StreamingAssets/Dialogue/world/*.json`, `npc/*.json`, `schema/*.json`

---

## Folder layout convention

| Path | Purpose |
|------|---------|
| `Assets/Scripts/` | All game code (tracked) |
| `Assets/StreamingAssets/Dialogue/` | Runtime narrative JSON + templates (tracked) |
| `Assets/Resources/` | Runtime-loadable mirrors of vendor prefabs/audio |
| `Assets/<VendorPack>/` | Raw Asset Store imports (local, gitignored) |
| `Assets/ThirdParty/`, `Assets/Art/` | Optional staging for future imports |
| `Music/` | Soundtrack mp3 (gitignored) |

Do **not** edit assets under `Library/PackageCache/` — it is regenerated.
