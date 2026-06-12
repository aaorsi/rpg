using System;
using System.Collections;
using System.Collections.Generic;
using Rpg.Core;
using Rpg.Dialogue;
using Rpg.GameState;
using Rpg.Gameplay;
using Rpg.Npc;
using Rpg.Player;
using Rpg.UI;
using Unity.AI.Navigation;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Rpg.Core
{
    /// <summary>
    /// Builds a visible slice (primitives + built-in default materials) in the Scene view using
    /// <see cref="ExecuteAlways"/> so you can see layout before Play. Saves the scene to persist it.
    /// At runtime, wires dialogue systems and rebakes the NavMesh.
    /// </summary>
    [ExecuteAlways]
    public sealed class RuntimeLevelBootstrap : MonoBehaviour
    {
        public const string ContentRootName = "_SliceContent";
        const string MainTerrainObjectName = "Terrain";
        const float PlayerSpawnDropHeight = 200f;
        const float MinCharacterHeightMeters = 1.5f;
        const float MaxCharacterHeightMeters = 2.15f;
        const float TerrainReferenceWidthMeters = 2500f;
        const int IslandNpcSpawnCount = 5;
        const float IslandSpawnRadiusFromCenter = 500f;
        const float HumanoidNpcSpawnRadiusFromHeroMeters = 50f;
        const float DialogueInteractionDistanceMeters = 5f;
        const int AnimalsPerSpeciesCount = 20;
        const int TigersSpawnCount = 10;
        const float AnimalSpawnRadiusFromHeroMeters = 200f;
        const float TigerSpawnMinSeparationMeters = 8f;
        const string NpcInventoryItemRootName = "_NpcInventoryItems";
        const string BookOneSceneName = "Book 1";
        const string BookTwoSceneName = "Book 2";
        const string BookThreeSceneName = "Book 3";
        const string BookOneItemId = "book_1";
        const string BookTwoItemId = "book_2";
        const string BookThreeItemId = "book_3";
        const float HeroSpawnRadiusFromTerrainCenterMeters = 500f;
        const float NpcHouseOffsetMinMeters = 10f;
        const float NpcHouseOffsetMaxMeters = 20f;
        const string HouseNamePrefix = "House";
        const int ExpectedHouseCount = 10;
        const int ExpectedNpcCount = 10;
        const int PreferredHeroHouseIndex = 6;
        const int VillageNpcDefinitionIndexBase = 1000;
        const string VillageRuntimeRootName = "_VillageRuntime";
        /// <summary>Max graph hops between dialogue anchor and CharacterController for one authored NPC (sibling rigs, etc.).</summary>
        const int MaxNpcRigAnchorToCharacterControllerHops = 32;
        const string TerrainStatuesRootName = "_TerrainStatues";
        const int OwlStatueCount = 4;
        const int LionStatueCount = 5;
        const float StatueTerrainHeightPercentile = 0.85f;
        const int StatueTerrainHeightSampleCount = 8192;
        const int StatueMaxSpawnAttemptsPerStatue = 350;
        const float StatueMinSeparationMeters = 22f;
        const float StatueHeightVsHeroMultiplier = 10f;
        const string SidekickNpcRootName = "_SidekickNpcs";
        const float SidekickOwlOffsetMinMeters = 10f;
        const float SidekickOwlOffsetMaxMeters = 20f;
        /// <summary>npcId / definition index base so sidekicks do not collide with authored NPC indices.</summary>
        const int SidekickNpcDefinitionIndexBase = 500;

        const int AmbientAuthoredChickenPerHouseMin = 3;
        const int AmbientAuthoredChickenPerHouseMax = 10;
        const float AmbientAuthoredChickenRadiusMeters = 10f;
        const int AmbientAuthoredHorsePerHouseMin = 2;
        const int AmbientAuthoredHorsePerHouseMax = 5;
        const float AmbientAuthoredHorseRadiusMeters = 50f;
        const float AmbientAuthoredNpcPetRadiusMeters = 10f;
        const float AmbientAuthoredMinSeparationMeters = 2f;

        static readonly string[] OwlStatuePrefabAssetPaths =
        {
            "Assets/AK Studio Art/Owl Statue/Prefabs/Owl Statue 1.prefab",
            "Assets/AK Studio Art/Owl Statue/Prefabs/Owl Statue 2.prefab",
            "Assets/AK Studio Art/Owl Statue/Prefabs/Owl Statue 3.prefab",
            "Assets/AK Studio Art/Owl Statue/Prefabs/Owl Statue 4.prefab",
        };

        static readonly string[] LionStatuePrefabAssetPaths =
        {
            "Assets/AK Studio Art/Sitting Lion Statue/Prefabs/Seated Lion Metal.prefab",
            "Assets/AK Studio Art/Sitting Lion Statue/Prefabs/Seated Lion Stone.prefab",
            "Assets/AK Studio Art/Sitting Lion Statue/Prefabs/Seated Lion Oxidized Copper.prefab",
            "Assets/AK Studio Art/Sitting Lion Statue/Prefabs/Seated Lion Marble.prefab",
            "Assets/AK Studio Art/Sitting Lion Statue/Prefabs/Seated Lion Copper.prefab",
        };

        static readonly string[] VillagerNamePool =
        {
            "Alden", "Brina", "Cato", "Daria", "Elric", "Faye",
            "Galen", "Helia", "Ivor", "Jora", "Kael", "Lina",
            "Marek", "Nora", "Orin", "Pia", "Quin", "Risa",
            "Soren", "Talia", "Ulric", "Vera", "Wren", "Xara",
            "Yoren", "Zara", "Bastian", "Celine", "Damon", "Esme"
        };

        [SerializeField]
        [Tooltip("Optional. Four owl statue prefabs; when set, used in builds instead of editor asset paths.")]
        GameObject[] owlStatuePrefabsOverride;

        [SerializeField]
        [Tooltip("Optional. Five lion statue prefabs; when set, used in builds instead of editor asset paths.")]
        GameObject[] lionStatuePrefabsOverride;

        [SerializeField]
        [Tooltip("Optional. When set, used as the player prefab and session randomization is skipped for the player.")]
        GameObject playerCharacterPrefab;

        [SerializeField]
        [Tooltip(
            "Optional extra hero prefabs for player builds (merged after Resources/StylizedCharacterPack/Characters "
            + "and Resources/Bootstrap/StandaloneAvatarLineup). Use when you need overrides beyond the default lineup asset.")]
        GameObject[] standaloneAvatarSelectionPrefabs;

        [SerializeField]
        [Tooltip("Optional. When set, used as the NPC prefab and session randomization is skipped for the NPC.")]
        GameObject npcCharacterPrefab;

        [SerializeField]
        [Tooltip("When playing with no prefab overrides, destroy any existing _SliceContent first so a new random layout is built.")]
        bool rebuildSessionSliceOnPlay = false;

        [SerializeField]
        [Tooltip("Optional. Lightning aura on the player during intro fall; when unset, uses Resources/Vfx/LightningAura if present.")]
        GameObject playerIntroLightningAuraPrefab;

        [SerializeField]
        [Tooltip("Optional. Magic circle under the selected hero during lineup selection; editor builds default from Hovl path if unset.")]
        GameObject playerSelectionMagicCirclePrefab;

        [SerializeField]
        [Tooltip("Optional. Sparks on confirm during lineup selection; editor loads default from Hovl path if unset.")]
        GameObject playerSelectionSparksPrefab;

        [SerializeField]
        [Tooltip("Optional absolute path for the startup title image. If empty, uses StreamingAssets/IslandTitle.png, then a file named IslandTitle.png next to the player Data folder.")]
        string startupTitleImagePath = string.Empty;

        [SerializeField]
        [Tooltip("Title text shown on the startup title screen.")]
        string startupGameTitle = "RPG Island";

        [SerializeField]
        [Tooltip("Optional absolute path to the folder containing Opening/Ambient/Victory mp3 files. If empty, uses <project-root>/Music.")]
        string musicFolderPath = string.Empty;

        [SerializeField]
        [Tooltip("Optional. Hero defensive spell VFX for K-cast AoE. Editor fallback uses Red energy explosion prefab path.")]
        GameObject playerDefensiveSpellVfxPrefab;

        [SerializeField]
        [Tooltip("Optional. Hero defensive spell VFX for level 2. Editor fallback uses Meteors AOE prefab path.")]
        GameObject playerDefensiveSpellLevel2VfxPrefab;

        [SerializeField]
        [Tooltip("Optional. Hero defensive spell VFX for level 3. Editor fallback uses Laser AOE prefab path.")]
        GameObject playerDefensiveSpellLevel3VfxPrefab;

        [SerializeField]
        [Tooltip("Optional. Hero defensive spell VFX for level 4. Editor fallback uses Sparks explode green prefab path.")]
        GameObject playerDefensiveSpellLevel4VfxPrefab;

        [SerializeField]
        [Min(0.1f)]
        [Tooltip("Uniform scale for the bootstrap NPC prefab so small animals read better in the slice.")]
        float bootstrapNpcUniformScale = 1f;

        [SerializeField]
        [Tooltip("Planar speed reference for the NPC animator driver (match wander speed order-of-magnitude).")]
        float npcAnimatorReferenceSpeed = 1.05f;

        [SerializeField]
        [Tooltip("CharacterController height when the player is a casual humanoid prefab.")]
        float humanCharacterControllerHeight = 1.85f;

        [SerializeField]
        [Tooltip("CharacterController radius when the player is a casual humanoid prefab.")]
        float humanCharacterControllerRadius = 0.28f;

        [SerializeField]
        [Tooltip("CharacterController center Y when the player is a casual humanoid prefab.")]
        float humanCharacterControllerCenterY = 0.92f;

        [SerializeField]
        [Tooltip("Use the currently opened scene geometry/camera instead of building a flat bootstrap floor/light/camera.")]
        bool useExistingSceneEnvironment = true;

        [SerializeField]
        [Tooltip("Building name used as scale/placement anchor in existing scene environments.")]
        string sceneAnchorBuildingName = "rpgpp_lt_building_01";

        [SerializeField]
        [Tooltip("World object name used as preferred spawn anchor for the player.")]
        string playerSpawnAnchorName = "rpgpp_lt_hill_small_02";

        [SerializeField]
        [Tooltip("World object name used as preferred spawn anchor for the NPC.")]
        string npcSpawnAnchorName = "rpgpp_lt_wagon_01";

        [SerializeField]
        [Min(0.1f)]
        [Tooltip("Target human visual height as a ratio of anchor building height.")]
        float humanToBuildingHeightRatio = 0.75f;

        [SerializeField]
        [Min(0.1f)]
        [Tooltip("Offset in front of anchor building on local world Z axis.")]
        float anchorPlacementOffsetZ = 2.0f;

        [SerializeField]
        [Min(0.1f)]
        [Tooltip("Horizontal spacing between player and NPC around the anchor.")]
        float anchorPlacementSeparationX = 2.2f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Additional planar offset for player spawn relative to player anchor.")]
        float playerAnchorPlanarOffset = 2.0f;

        [SerializeField]
        [Min(1)]
        [Tooltip("How many NPCs are spawned for this slice.")]
        int npcCount = 5;

        [SerializeField]
        [Tooltip("When enabled, spawn NPCs from all StylizedCharacterPack character prefabs.")]
        bool useStylizedCharacterPackNpcs;

        [SerializeField]
        [Min(0.5f)]
        [Tooltip("How far from building bounds NPCs should spawn.")]
        float npcSpawnRadiusFromBuilding = 2f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Small feet clearance above ground to avoid sinking visuals.")]
        float spawnFootClearance = 0.03f;

        [Header("Village Runtime Spawn")]
        [SerializeField]
        [Min(1)]
        [Tooltip("Numbered house anchor used as village reference (e.g. 4 => House 4).")]
        int villageReferenceHouseIndex = 4;

        [SerializeField]
        [Min(0)]
        [Tooltip("How many additional houses to spawn around the village reference house.")]
        int villageExtraHouseCount = 20;

        [SerializeField]
        [Min(0)]
        [Tooltip("How many villagers to spawn around the village reference house.")]
        int villageVillagerCount = 30;

        [SerializeField]
        [Min(0.1f)]
        [Tooltip("Inner radius for village annulus spawn.")]
        float villageSpawnMinRadiusMeters = 36f;

        [SerializeField]
        [Min(0.1f)]
        [Tooltip("Outer radius for village annulus spawn.")]
        float villageSpawnMaxRadiusMeters = 84f;

        [SerializeField]
        [Min(0.1f)]
        [Tooltip("Minimum planar separation between villager spawn positions.")]
        float villageMinVillagerSeparationMeters = 1.8f;

        [SerializeField]
        [Min(0.1f)]
        [Tooltip("Minimum planar separation between extra spawned houses.")]
        float villageMinHouseSeparationMeters = 6f;

        [SerializeField]
        [Tooltip("When true, villagers also check physics occupancy before accepting a sampled point.")]
        bool villageCheckPhysicsForVillagers = false;

        [SerializeField]
        [Tooltip(
            "Mixamo clip name for villager idle (exact or substring match under Resources/Mixamo/Animations). "
            + "Default Idle1. Clear to use catalog heuristics only.")]
        string villagerIdleClipNameOverride = "Idle1";

        [SerializeField]
        [Tooltip(
            "Mixamo clip name for villager walk (exact or substring match). Default Walking. Clear to use catalog heuristics only.")]
        string villagerWalkClipNameOverride = "Walking";

        [SerializeField]
        [Min(0.1f)]
        [Tooltip("Additional scale multiplier for StylizedCharacterPack characters.")]
        float stylizedCharacterScaleFactor = 2.25f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("If > 0, hero ground XZ is placed within this planar distance of one tiger and the first runtime tiger is spawned at that anchor (island slice + authored tigers). Use 0 to disable.")]
        float testSpawnHeroNearTigerRadiusMeters = 20f;

        [SerializeField]
        [Tooltip("When enabled, places the hero on the ground (with intro sky height) at a random horizontal distance between min and max meters from the scene Castle object.")]
        bool spawnHeroNearCastle = false;

        [SerializeField]
        [Tooltip("Scene object name used as the spawn anchor (case-insensitive).")]
        string castleObjectName = "Castle";

        [SerializeField]
        [Min(1f)]
        [Tooltip("Planar distance from Castle anchor (meters); random spawn uses annulus [min, max].")]
        float heroSpawnMinDistanceFromCastleMeters = 25f;

        [SerializeField]
        [Min(1f)]
        [Tooltip("Upper bound for planar distance from Castle (meters); keep ≤200 to stay near the castle for testing.")]
        float heroSpawnMaxDistanceFromCastleMeters = 200f;

        [SerializeField]
        [Tooltip("When enabled, hero spawn first tries House 6 area before castle/random fallbacks.")]
        bool spawnHeroNearHouseSix = false;

        [SerializeField]
        [Tooltip("When enabled, the hero always spawns at the fixed world position below (disables castle/house ring spawn).")]
        bool useFixedHeroSpawnPosition = true;

        [SerializeField]
        [Tooltip("Fixed hero spawn world position.")]
        Vector3 fixedHeroSpawnWorldPosition = new Vector3(5534.91f, -2425.1f, 1334.289f);

        [SerializeField]
        [Min(0.5f)]
        [Tooltip("Minimum horizontal distance from House 6 for preferred hero spawn.")]
        float heroSpawnMinDistanceFromHouseMeters = 6f;

        [SerializeField]
        [Min(0.5f)]
        [Tooltip("Maximum horizontal distance from House 6 for preferred hero spawn.")]
        float heroSpawnMaxDistanceFromHouseMeters = 12f;

        [SerializeField]
        [Min(2f)]
        [Tooltip("Legacy radius field (unused when global environment colliders are enabled).")]
        float environmentColliderProvisionRadius = 36f;

        [Header("Play-mode bootstrap phases (BootstrapPlayModeRoutine only)")]
        [SerializeField]
        [Tooltip("When true, runs yield return RunStartupTitleScreen().")]
        bool runPhaseStartupTitleScreen = true;

        [SerializeField]
        [Tooltip("When true, runs yield return RunAvatarSelectionIfNeeded().")]
        bool runPhaseAvatarSelection = true;

        [SerializeField]
        [Tooltip("When true, calls MusicDirector.Instance.FadeAfterCharacterSelection() after avatar flow.")]
        bool runPhaseMusicFadeAfterCharacterSelection = true;

        [SerializeField]
        [Tooltip("When true, calls EnsureSliceContentPresent().")]
        bool runPhaseEnsureSliceContentPresent = true;

        [SerializeField]
        [Tooltip("When true, calls RandomizeCharactersOnIslandTerrain().")]
        bool runPhaseRandomizeCharactersOnIslandTerrain = true;

        [SerializeField]
        [Tooltip("When true, calls EnsureSliceFollowCameraWired().")]
        bool runPhaseSliceFollowCameraWired = true;

        [SerializeField]
        [Tooltip("When true, calls RebakeNavMeshIfPossible().")]
        bool runPhaseRebakeNavMeshIfPossible = true;

        [SerializeField]
        [Tooltip(
            "When true, runs late setup: EnsureWarehouseDialogueAnchor, BuildManagersAndUi, EnsurePlayerAuxiliaryRuntimeComponents, "
            + "EnsureIslandEscapePortal, TrySpawnHeroNearCastleOnTaggedPlayer (twice with one frame between), EnsureGameplayIntroOverlay.")]
        bool runPhasePostWorldServices = true;

        GameObject _sessionSelectedPlayerPrefab;
        bool _playerChosenFromLineup;
        bool _playBootstrapStarted;
        readonly List<GameObject> _sidekickLineupPrefabs = new List<GameObject>(4);

        void OnEnable()
        {
            if (!Application.isPlaying)
            {
                EnsureSliceContentPresent();
                return;
            }

            if (_playBootstrapStarted)
                return;
            _playBootstrapStarted = true;
            StartCoroutine(BootstrapPlayModeRoutine());
        }

        IEnumerator BootstrapPlayModeRoutine()
        {
            OllamaStartupSelection.ResetPlaySession();
            EnsureMusicDirector();
            EnsureAudioListenerPresent();
            if (runPhaseStartupTitleScreen)
                yield return RunStartupTitleScreen();
            if (runPhaseAvatarSelection)
                yield return RunAvatarSelectionIfNeeded();
            if (runPhaseMusicFadeAfterCharacterSelection && MusicDirector.Instance != null)
                MusicDirector.Instance.FadeAfterCharacterSelection();

            // Preserve manually positioned player/NPC instances across play sessions.
            // Rebuild can still be triggered manually from the context menu in editor.
            if (runPhaseEnsureSliceContentPresent)
                EnsureSliceContentPresent();
            if (runPhaseRandomizeCharactersOnIslandTerrain)
                RandomizeCharactersOnIslandTerrain();
            if (runPhaseSliceFollowCameraWired)
                EnsureSliceFollowCameraWired();
            if (runPhaseRebakeNavMeshIfPossible)
                RebakeNavMeshIfPossible();
            if (runPhasePostWorldServices)
            {
                EnsureWarehouseDialogueAnchor();
                BuildManagersAndUi();
                EnsurePlayerAuxiliaryRuntimeComponents();
                EnsureIslandEscapePortal();
                TrySpawnHeroNearCastleOnTaggedPlayer();
                EnsureGameplayIntroOverlay();
                yield return null;
                TrySpawnHeroNearCastleOnTaggedPlayer();
            }
        }

        void TrySpawnHeroNearCastleOnTaggedPlayer()
        {
            var p = GameObject.FindGameObjectWithTag(GameConstants.PlayerTag);
            if (useFixedHeroSpawnPosition && p != null)
            {
                SetPlayerWorldPositionForSpawn(p, fixedHeroSpawnWorldPosition);
                return;
            }
            if (spawnHeroNearHouseSix && TrySpawnHeroNearHouseIndex(p, PreferredHeroHouseIndex))
                return;
            TrySpawnHeroNearCastle(p);
        }

        bool TrySpawnHeroNearHouseIndex(GameObject playerGo, int houseIndex)
        {
            if (!Application.isPlaying || playerGo == null)
                return false;
            var houses = CollectNumberedHouseTransforms();
            if (houses == null || houses.Count == 0 || houseIndex < 1 || houseIndex > houses.Count)
                return false;
            var house = houses[houseIndex - 1];
            if (house == null || house.gameObject == null)
                return false;

            var minR = Mathf.Min(heroSpawnMinDistanceFromHouseMeters, heroSpawnMaxDistanceFromHouseMeters);
            var maxR = Mathf.Max(heroSpawnMinDistanceFromHouseMeters, heroSpawnMaxDistanceFromHouseMeters);
            minR = Mathf.Max(0.5f, minR);
            maxR = Mathf.Max(minR + 0.25f, maxR);
            var anchor = house.position;
            var minGroundY = ResolveMinimumGroundYFromWater();

            if (TryGetPrimaryTerrain(out var terrain) && terrain.terrainData != null)
            {
                EnsureTerrainCollider(terrain);
                var td = terrain.terrainData;
                var origin = terrain.transform.position;
                var margin = Mathf.Min(30f, Mathf.Max(10f, Mathf.Min(td.size.x, td.size.z) * 0.02f));
                for (var attempt = 0; attempt < 180; attempt++)
                {
                    var theta = Random.Range(0f, Mathf.PI * 2f);
                    var r = Mathf.Sqrt(Random.Range(minR * minR, maxR * maxR));
                    var x = anchor.x + Mathf.Cos(theta) * r;
                    var z = anchor.z + Mathf.Sin(theta) * r;
                    if (x < origin.x + margin || x > origin.x + td.size.x - margin
                        || z < origin.z + margin || z > origin.z + td.size.z - margin)
                        continue;
                    var y = terrain.SampleHeight(new Vector3(x, origin.y, z)) + origin.y;
                    if (y < minGroundY)
                        continue;
                    SetPlayerWorldPositionForSpawn(playerGo, new Vector3(x, y, z) + Vector3.up * Mathf.Max(PlayerSpawnDropHeight, 200f));
                    return true;
                }
            }

            for (var attempt = 0; attempt < 80; attempt++)
            {
                var theta = Random.Range(0f, Mathf.PI * 2f);
                var r = Mathf.Sqrt(Random.Range(minR * minR, maxR * maxR));
                var x = anchor.x + Mathf.Cos(theta) * r;
                var z = anchor.z + Mathf.Sin(theta) * r;
                var grounded = SnapToSceneGroundWorld(new Vector3(x, anchor.y + 500f, z), anchor.y);
                if (grounded.y < minGroundY)
                    continue;
                SetPlayerWorldPositionForSpawn(playerGo, grounded + Vector3.up * Mathf.Max(PlayerSpawnDropHeight, 200f));
                return true;
            }

            return false;
        }

        /// <summary>
        /// Picks a random point on the primary terrain in a horizontal annulus around the <see cref="castleObjectName"/>
        /// root, then places the hero at ground height plus intro sky-drop offset (same pattern as authored random spawn).
        /// </summary>
        bool TrySpawnHeroNearCastle(GameObject playerGo)
        {
            if (!Application.isPlaying || !spawnHeroNearCastle || playerGo == null)
                return false;
            if (string.IsNullOrWhiteSpace(castleObjectName))
                return false;
            var castle = FindSceneObjectByNameCaseInsensitive(castleObjectName);
            if (castle == null)
            {
                Debug.LogWarning(
                    $"[{nameof(RuntimeLevelBootstrap)}] spawnHeroNearCastle is enabled but no scene object named '{castleObjectName}' was found.");
                return false;
            }

            var anchor = castle.transform.position;
            var minR = Mathf.Min(heroSpawnMinDistanceFromCastleMeters, heroSpawnMaxDistanceFromCastleMeters);
            var maxR = Mathf.Max(heroSpawnMinDistanceFromCastleMeters, heroSpawnMaxDistanceFromCastleMeters);
            if (maxR < 1f)
                maxR = 100f;
            if (minR < 0.5f)
                minR = 0.5f;
            if (minR > maxR)
                (minR, maxR) = (maxR, minR);

            var minGroundY = ResolveMinimumGroundYFromWater();
            if (TryGetPrimaryTerrain(out var terrain) && terrain.terrainData != null)
            {
                EnsureTerrainCollider(terrain);
                var td = terrain.terrainData;
                var origin = terrain.transform.position;
                var margin = Mathf.Min(30f, Mathf.Max(10f, Mathf.Min(td.size.x, td.size.z) * 0.02f));
                for (var attempt = 0; attempt < 200; attempt++)
                {
                    var theta = Random.Range(0f, Mathf.PI * 2f);
                    var r = Mathf.Sqrt(Random.Range(minR * minR, maxR * maxR));
                    var x = anchor.x + Mathf.Cos(theta) * r;
                    var z = anchor.z + Mathf.Sin(theta) * r;
                    if (x < origin.x + margin || x > origin.x + td.size.x - margin
                        || z < origin.z + margin || z > origin.z + td.size.z - margin)
                        continue;
                    var y = terrain.SampleHeight(new Vector3(x, origin.y, z)) + origin.y;
                    if (y < minGroundY)
                        continue;
                    var ground = new Vector3(x, y, z);
                    var spawnPos = ground + Vector3.up * Mathf.Max(PlayerSpawnDropHeight, 200f);
                    SetPlayerWorldPositionForSpawn(playerGo, spawnPos);
                    return true;
                }

                Debug.LogWarning(
                    $"[{nameof(RuntimeLevelBootstrap)}] Could not sample terrain near '{castleObjectName}' in annulus {minR:F0}–{maxR:F0} m.");
                return false;
            }

            for (var attempt = 0; attempt < 120; attempt++)
            {
                var theta = Random.Range(0f, Mathf.PI * 2f);
                var r = Mathf.Sqrt(Random.Range(minR * minR, maxR * maxR));
                var x = anchor.x + Mathf.Cos(theta) * r;
                var z = anchor.z + Mathf.Sin(theta) * r;
                var grounded = SnapToSceneGroundWorld(new Vector3(x, anchor.y + 500f, z), anchor.y);
                var spawnPos = grounded + Vector3.up * Mathf.Max(PlayerSpawnDropHeight, 200f);
                SetPlayerWorldPositionForSpawn(playerGo, spawnPos);
                return true;
            }

            return false;
        }

        static void SetPlayerWorldPositionForSpawn(GameObject playerGo, Vector3 worldPosition)
        {
            if (playerGo == null)
                return;
            var cc = playerGo.GetComponent<CharacterController>();
            if (cc != null)
                cc.enabled = false;
            playerGo.transform.SetPositionAndRotation(worldPosition, Quaternion.identity);
            if (cc != null)
                cc.enabled = true;
        }

        static GameObject FindSceneObjectByNameCaseInsensitive(string wanted)
        {
            if (string.IsNullOrWhiteSpace(wanted))
                return null;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (t == null || t.gameObject == null)
                    continue;
                if (string.Equals(t.gameObject.name, wanted, StringComparison.OrdinalIgnoreCase))
                    return t.gameObject;
            }

            return null;
        }

        void RandomizeCharactersOnIslandTerrain()
        {
            if (!Application.isPlaying)
                return;
            // Authored scene mode: keep authored planar placement, but still ground characters safely.
            if (useExistingSceneEnvironment)
            {
                GroundAuthoredCharactersInExistingEnvironment();
                return;
            }
            if (!TryGetPrimaryTerrain(out var terrain))
                return;
            EnsureTerrainCollider(terrain);
            var minGroundY = ResolveMinimumGroundYFromWater();

            var playerGo = GameObject.FindGameObjectWithTag(GameConstants.PlayerTag);
            if (playerGo == null)
                return;
            var islandScale = Mathf.Max(0.2f, terrain.terrainData.size.x / TerrainReferenceWidthMeters);

            var heroSpawnGroundResolved = false;
            Vector3? forcedFirstTigerGround = null;
            if (useFixedHeroSpawnPosition)
            {
                SetPlayerWorldPositionForSpawn(playerGo, fixedHeroSpawnWorldPosition);
                heroSpawnGroundResolved = true;
            }
            else if (spawnHeroNearCastle && TrySpawnHeroNearCastle(playerGo))
            {
                heroSpawnGroundResolved = true;
                if (!_playerChosenFromLineup)
                    ApplyTargetCharacterHeight(playerGo, Random.Range(MinCharacterHeightMeters, MaxCharacterHeightMeters) * islandScale);
            }
            else
            {
                Vector3 playerGroundPos = default;
                if (testSpawnHeroNearTigerRadiusMeters > 0.01f
                    && TryPickBuddyTigerTestSpawnPositions(
                        terrain,
                        minGroundY,
                        testSpawnHeroNearTigerRadiusMeters,
                        out var tigerAnchor,
                        out playerGroundPos))
                {
                    heroSpawnGroundResolved = true;
                    forcedFirstTigerGround = tigerAnchor;
                }

                if (!heroSpawnGroundResolved)
                {
                    heroSpawnGroundResolved = TryGetRandomTerrainPoint(
                        terrain,
                        out playerGroundPos,
                        occupied: null,
                        minDistance: 0f,
                        centerRadiusLimit: IslandSpawnRadiusFromCenter,
                        minimumGroundY: minGroundY);
                }

                if (heroSpawnGroundResolved)
                {
                    var playerSpawnPos = playerGroundPos + Vector3.up * PlayerSpawnDropHeight;
                    var cc = playerGo.GetComponent<CharacterController>();
                    if (cc != null)
                        cc.enabled = false;
                    playerGo.transform.position = playerSpawnPos;
                    playerGo.transform.rotation = Quaternion.identity;
                    if (_playerChosenFromLineup)
                        LiftCharacterFeetAboveGround(playerGo, Mathf.Max(spawnFootClearance, 0.12f));
                    else
                        ApplyTargetCharacterHeight(playerGo, Random.Range(MinCharacterHeightMeters, MaxCharacterHeightMeters) * islandScale);
                    if (cc != null)
                        cc.enabled = true;
                }
            }
            EnsureAnimatedCharacter(playerGo, forceIdle: false);
            EnsureDialogueInteractionRangeForPlayer(playerGo, DialogueInteractionDistanceMeters);

            var npcs = CollectAllNpcRoots();
            // Keep manually placed NPCs exactly where they are in the scene.
            if (npcs.Count == 0)
                npcs = EnsureIslandNpcsExist(playerGo, IslandNpcSpawnCount, islandScale);
            var occupied = new List<Vector3> { playerGo.transform.position };
            foreach (var npc in npcs)
            {
                if (npc == null)
                    continue;
                // Preserve transform from authored scene placement exactly (no bootstrap reposition/grounding).
                EnsureAnimatedCharacter(npc, forceIdle: false);
                EnsureDialogueInteractionRangeForNpc(npc, DialogueInteractionDistanceMeters);
                occupied.Add(npc.transform.position);
            }

            SpawnNpcCarriedItemsNearNpcs(npcs, terrain, minGroundY);

            SpawnAnimalsOnIslandTerrain(
                terrain,
                islandScale,
                occupied,
                playerGo.transform.position,
                heroSpawnGroundResolved,
                forcedFirstTigerGround);
        }

        void GroundAuthoredCharactersInExistingEnvironment()
        {
            var minGroundY = ResolveMinimumGroundYFromWater();
            var playerGo = GameObject.FindGameObjectWithTag(GameConstants.PlayerTag);
            if (playerGo != null)
            {
                if (useFixedHeroSpawnPosition)
                {
                    SetPlayerWorldPositionForSpawn(playerGo, fixedHeroSpawnWorldPosition);
                }
                else if (spawnHeroNearHouseSix && TrySpawnHeroNearHouseIndex(playerGo, PreferredHeroHouseIndex))
                {
                    // Player placed near preferred house anchor.
                }
                else if (spawnHeroNearCastle && TrySpawnHeroNearCastle(playerGo))
                {
                    // Player placed near Castle (see TrySpawnHeroNearCastle).
                }
                else if (TryGetPrimaryTerrain(out var terrain)
                    && TryGetRandomTerrainPoint(
                        terrain,
                        out var playerGroundPos,
                        occupied: null,
                        minDistance: 0f,
                        centerRadiusLimit: HeroSpawnRadiusFromTerrainCenterMeters,
                        minimumGroundY: minGroundY,
                        maxSampleAttempts: 120))
                {
                    var cc = playerGo.GetComponent<CharacterController>();
                    if (cc != null)
                        cc.enabled = false;
                    // At least 200 m above terrain at this XZ (intro / sky spawn).
                    playerGo.transform.position = playerGroundPos + Vector3.up * Mathf.Max(PlayerSpawnDropHeight, 200f);
                    playerGo.transform.rotation = Quaternion.identity;
                    if (cc != null)
                        cc.enabled = true;
                }
                else
                {
                    var p = playerGo.transform.position;
                    var grounded = SnapToSceneGroundWorld(new Vector3(p.x, p.y + 1f, p.z), p.y);
                    playerGo.transform.position = grounded + Vector3.up * Mathf.Max(PlayerSpawnDropHeight, 200f);
                }
                // Do not call LiftCharacterFeetAboveGround here — that would snap a sky-spawned hero down to the ground.
                EnsureAnimatedCharacter(playerGo, forceIdle: false);
                EnsureDialogueInteractionRangeForPlayer(playerGo, DialogueInteractionDistanceMeters);
            }

            var houses = CollectNumberedHouseTransforms();
            Debug.Log(
                $"[{nameof(RuntimeLevelBootstrap)}] Unique numbered house anchors: {houses.Count} (indices 1–{ExpectedHouseCount}; duplicates merged).");
            if (houses.Count != ExpectedHouseCount)
                Debug.LogWarning(
                    $"[{nameof(RuntimeLevelBootstrap)}] Expected {ExpectedHouseCount} distinct house indices " +
                    $"\"{HouseNamePrefix} 1\" … \"{HouseNamePrefix} {ExpectedHouseCount}\" but found {houses.Count}.");

            var npcs = CollectAllNpcRoots();
            if (npcs.Count != ExpectedNpcCount)
                Debug.LogWarning(
                    $"[{nameof(RuntimeLevelBootstrap)}] Expected {ExpectedNpcCount} NPC roots but found {npcs.Count}.");

            if (houses.Count == 0)
            {
                Debug.LogWarning(
                    $"[{nameof(RuntimeLevelBootstrap)}] No numbered '{HouseNamePrefix} <n>' houses found; NPCs will only be grounded in place.");
                foreach (var npc in npcs)
                {
                    if (npc == null)
                        continue;
                    var p = npc.transform.position;
                    var grounded = SnapToSceneGroundWorld(new Vector3(p.x, p.y + 1f, p.z), p.y);
                    npc.transform.position = grounded;
                    LiftCharacterFeetAboveGround(npc, Mathf.Max(spawnFootClearance, 0.08f));
                    EnsureAnimatedCharacter(npc, forceIdle: false);
                    EnsureDialogueInteractionRangeForNpc(npc, DialogueInteractionDistanceMeters);
                }
                SpawnVillageAroundReferenceHouse(transform);
                PlaceAkStudioStatuesOnHighTerrain(playerGo);
                PlaceSidekickNpcsNearOwlStatues();
                SpawnAuthoredAmbientWildlife(playerGo);
                EnsureAuthoredSceneRuntimeTigers(playerGo);
                EnsureAuthoredSceneRuntimeSpiders();
                EnsureAuthoredBookPickups();
                return;
            }

            TryGetPrimaryTerrain(out var terrainForNpcY);
            const string allocTag = nameof(RuntimeLevelBootstrap) + ".HouseNpcAllocation";
            ShuffleList(npcs);
            LogHouseNpcShuffleOrder(allocTag, npcs);
            var pairCount = Mathf.Min(houses.Count, npcs.Count);
            var successAllocations = 0;
            for (var i = 0; i < pairCount; i++)
            {
                var house = houses[i];
                var npc = npcs[i];
                var houseLabel = house != null ? house.gameObject.name : "(null)";
                if (!TryParseNumberedHouseIndex(houseLabel, out var houseIndex))
                    houseIndex = i + 1;

                if (npc == null)
                {
                    Debug.LogWarning(
                        $"[{allocTag}] Slot {i + 1}/{pairCount} FAILED: house `{houseLabel}` (#{houseIndex}) → NPC is null.");
                    continue;
                }

                var npcLabel = FormatNpcAllocationLabel(npc);
                var housePos = house.position;
                var angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                var dist = Random.Range(NpcHouseOffsetMinMeters, NpcHouseOffsetMaxMeters);
                var offset = new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
                var x = housePos.x + offset.x;
                var z = housePos.z + offset.z;
                float yWorld;
                if (terrainForNpcY != null && terrainForNpcY.terrainData != null)
                    yWorld = terrainForNpcY.SampleHeight(new Vector3(x, 0f, z)) + terrainForNpcY.transform.position.y;
                else
                    yWorld = SnapToSceneGroundWorld(new Vector3(x, housePos.y + 50f, z), housePos.y).y;
                // Do not call LiftCharacterFeetAboveGround here: it raycasts down from render bounds and near houses
                // often hits roofs/walls or wrong colliders, vertically snapping the NPC away from the assigned XZ.
                // Y is already from terrain (or SnapToSceneGroundWorld). Teleport with CC disabled so the move sticks.
                var foot = Mathf.Max(spawnFootClearance, 0.08f);
                TeleportNpcRootToWorldPositionPreservingRotation(npc, new Vector3(x, yWorld + foot, z));
                EnsureAnimatedCharacter(npc, forceIdle: false);
                EnsureDialogueInteractionRangeForNpc(npc, DialogueInteractionDistanceMeters);
                successAllocations++;
                Debug.Log(
                    $"[{allocTag}] Slot {i + 1}/{pairCount} OK: house `{houseLabel}` (#{houseIndex}) → {npcLabel}, offset={dist:F1}m.");
            }

            LogHouseNpcAllocationOutcome(allocTag, houses.Count, npcs.Count, pairCount, successAllocations);

            for (var j = pairCount; j < npcs.Count; j++)
            {
                if (npcs[j] == null)
                    continue;
                Debug.LogWarning(
                    $"[{allocTag}] No house slot for NPC {FormatNpcAllocationLabel(npcs[j])} " +
                    $"(pairCount={pairCount}; houses={houses.Count}, npcRoots={npcs.Count}).");
            }

            SpawnVillageAroundReferenceHouse(transform);
            PlaceAkStudioStatuesOnHighTerrain(playerGo);
            PlaceSidekickNpcsNearOwlStatues();
            SpawnAuthoredAmbientWildlife(playerGo);
            EnsureAuthoredSceneRuntimeTigers(playerGo);
            EnsureAuthoredSceneRuntimeSpiders();
            EnsureAuthoredBookPickups();
        }

        static void EnsureAuthoredBookPickups()
        {
            EnsureAuthoredBookPickup(BookOneSceneName, BookOneItemId);
            EnsureAuthoredBookPickup(BookTwoSceneName, BookTwoItemId);
            EnsureAuthoredBookPickup(BookThreeSceneName, BookThreeItemId);
        }

        static void EnsureAuthoredBookPickup(string sceneObjectName, string itemId)
        {
            if (string.IsNullOrWhiteSpace(sceneObjectName) || string.IsNullOrWhiteSpace(itemId))
                return;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null || t.gameObject == null)
                    continue;
                if (!string.Equals(t.gameObject.name, sceneObjectName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var go = t.gameObject;
                var pickup = go.GetComponent<ItemPickup>();
                if (pickup == null)
                    pickup = go.AddComponent<ItemPickup>();
                pickup.Configure(itemId);
                if (go.GetComponentInChildren<Collider>(true) == null)
                {
                    var mf = go.GetComponentInChildren<MeshFilter>(true);
                    if (mf != null && mf.sharedMesh != null)
                    {
                        var mc = go.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                        mc.convex = true;
                    }
                    else
                    {
                        go.AddComponent<SphereCollider>();
                    }
                }
            }
        }

        static string FormatNpcAllocationLabel(GameObject npc)
        {
            if (npc == null)
                return "(null)";
            var b = npc.GetComponent<NpcDialogueBinding>();
            var npcId = b != null && b.Definition != null ? b.Definition.npcId : null;
            if (!string.IsNullOrEmpty(npcId))
                return $"`{npc.name}` (npcId={npcId})";
            return $"`{npc.name}`";
        }

        /// <summary>
        /// Teleports the NPC movement root. Temporarily disables <see cref="CharacterController"/> so Unity
        /// applies the world position; leaving it enabled can prevent the transform from sticking after a large move.
        /// </summary>
        static void TeleportNpcRootToWorldPositionPreservingRotation(GameObject characterRoot, Vector3 worldPosition)
        {
            if (characterRoot == null)
                return;
            var cc = characterRoot.GetComponent<CharacterController>();
            var had = cc != null && cc.enabled;
            if (cc != null)
                cc.enabled = false;
            var rot = characterRoot.transform.rotation;
            characterRoot.transform.SetPositionAndRotation(worldPosition, rot);
            if (cc != null)
                cc.enabled = had;
        }

        void SpawnVillageAroundReferenceHouse(Transform root)
        {
            if (!Application.isPlaying || root == null)
                return;

            var existing = root.Find(VillageRuntimeRootName);
            if (existing != null)
                Destroy(existing.gameObject);

            if (!TryGetReferenceHouseTransform(villageReferenceHouseIndex, out var refHouse))
            {
                Debug.LogWarning(
                    $"[{nameof(RuntimeLevelBootstrap)}] Village spawn skipped: could not find `{HouseNamePrefix} {villageReferenceHouseIndex}`.");
                return;
            }

            var villageRoot = new GameObject(VillageRuntimeRootName).transform;
            villageRoot.SetParent(root, false);
            villageRoot.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            if (villageSpawnMaxRadiusMeters <= villageSpawnMinRadiusMeters)
                villageSpawnMaxRadiusMeters = villageSpawnMinRadiusMeters + 1f;

            var refPos = refHouse.position;
            TryGetPrimaryTerrain(out var terrain);
            var occupied = new List<Vector3>(villageExtraHouseCount + villageVillagerCount + 1) { refPos };

            SpawnVillageExtraHouses(villageRoot, refHouse, refPos, terrain, occupied);
            SpawnVillagers(villageRoot, refPos, terrain, occupied);
        }

        void SpawnVillageExtraHouses(Transform villageRoot, Transform refHouse, Vector3 refPos, Terrain terrain, List<Vector3> occupied)
        {
            if (villageRoot == null || refHouse == null)
                return;

            var placed = 0;
            for (var i = 0; i < villageExtraHouseCount; i++)
            {
                if (!TrySampleVillagePoint(
                        refPos,
                        villageSpawnMinRadiusMeters,
                        villageSpawnMaxRadiusMeters,
                        terrain,
                        refPos.y,
                        occupied,
                        villageMinHouseSeparationMeters,
                        checkPhysicsOccupancy: false,
                        out var pos))
                    continue;

                var houseGo = Instantiate(refHouse.gameObject, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), villageRoot);
                houseGo.name = $"Village House {i + 1}";
                occupied.Add(pos);
                placed++;
            }

            if (placed < villageExtraHouseCount)
                Debug.LogWarning(
                    $"[{nameof(RuntimeLevelBootstrap)}] Village houses requested={villageExtraHouseCount}, placed={placed}. Try widening spawn radius.");
        }

        void SpawnVillagers(Transform villageRoot, Vector3 refPos, Terrain terrain, List<Vector3> occupied)
        {
            var casualPrefabs = ResolveVillageVillagerPrefabPool();
            if (casualPrefabs.Count == 0)
            {
                Debug.LogWarning(
                    $"[{nameof(RuntimeLevelBootstrap)}] Village spawn skipped: no prefabs in Resources/{GameConstants.NpcCasualCharactersResourcesFolder}.");
                return;
            }

            var npcTemplate = Resources.Load<NpcDefinition>("NpcDefs/default");
            if (npcTemplate == null)
                Debug.LogWarning($"[{nameof(RuntimeLevelBootstrap)}] Village NPC template not found; villagers will use BuildNpcSingle defaults.");

            var shuffledNames = new List<string>(VillagerNamePool);
            ShuffleList(shuffledNames);

            var spawned = 0;
            for (var i = 0; i < villageVillagerCount; i++)
            {
                if (!TrySampleVillagePoint(
                        refPos,
                        villageSpawnMinRadiusMeters,
                        villageSpawnMaxRadiusMeters,
                        terrain,
                        refPos.y,
                        occupied,
                        villageMinVillagerSeparationMeters,
                        checkPhysicsOccupancy: villageCheckPhysicsForVillagers,
                        out var pos))
                {
                    // Fallback to permissive sampling so villagers still appear in dense scenes.
                    if (!TrySampleVillagePoint(
                            refPos,
                            villageSpawnMinRadiusMeters,
                            villageSpawnMaxRadiusMeters,
                            terrain,
                            refPos.y,
                            occupied: null,
                            minSeparation: 0.1f,
                            checkPhysicsOccupancy: false,
                            out pos))
                        continue;
                }

                var prefab = casualPrefabs[Random.Range(0, casualPrefabs.Count)];
                var npcName = $"Villager_{i + 1:00}";
                var npcIndex = VillageNpcDefinitionIndexBase + i;
                BuildNpcSingle(
                    villageRoot,
                    prefab,
                    npcName,
                    npcIndex,
                    pos,
                    uniformScaleWhenPrefab: 1f,
                    npcAnimRefSpeed: 4.5f,
                    useExistingEnv: true,
                    buildingName: $"House {villageReferenceHouseIndex}",
                    humanToBuildingRatio: humanToBuildingHeightRatio,
                    footClearance: spawnFootClearance,
                    stylizedScaleFactor: stylizedCharacterScaleFactor,
                    skipFootLiftTrustWorldSpawn: true,
                    casualIdleClipNameOverride: string.IsNullOrWhiteSpace(villagerIdleClipNameOverride)
                        ? null
                        : villagerIdleClipNameOverride,
                    casualWalkClipNameOverride: string.IsNullOrWhiteSpace(villagerWalkClipNameOverride)
                        ? null
                        : villagerWalkClipNameOverride);

                var spawnedNpc = villageRoot.Find(npcName);
                if (spawnedNpc != null)
                {
                    var binding = spawnedNpc.GetComponent<NpcDialogueBinding>();
                    if (binding != null)
                    {
                        var displayName = i < shuffledNames.Count ? shuffledNames[i] : $"Villager {i + 1}";
                        if (npcTemplate != null)
                            binding.SetDefinition(CreateVillagerRuntimeDefinition(npcTemplate, displayName, npcIndex, villageReferenceHouseIndex));
                    }

                    if (spawnedNpc.GetComponent<VillagerAmbientRoutine>() == null)
                        spawnedNpc.gameObject.AddComponent<VillagerAmbientRoutine>();
                    if (spawnedNpc.TryGetComponent<NpcCasualLocomotionPlayableDriver>(out var villagerAnim))
                        villagerAnim.SetLocomotionReferenceSpeed(planarReferenceSpeed: 2.2f, maxWalkSpeed: 1.35f, deadZone: 0.02f);
                }

                occupied.Add(pos);
                spawned++;
            }

            if (spawned < villageVillagerCount)
                Debug.LogWarning(
                    $"[{nameof(RuntimeLevelBootstrap)}] Villagers requested={villageVillagerCount}, spawned={spawned}. " +
                    $"(annulus={villageSpawnMinRadiusMeters:F1}-{villageSpawnMaxRadiusMeters:F1}m, physicsCheck={villageCheckPhysicsForVillagers})");
            else
                Debug.Log($"[{nameof(RuntimeLevelBootstrap)}] Spawned village around House {villageReferenceHouseIndex}: villagers={spawned}, houses={villageExtraHouseCount}.");
        }

        static List<GameObject> ResolveVillageVillagerPrefabPool()
        {
            var pool = new List<GameObject>();
            var filtered = BootstrapCasualCharacterResources.GetAllCharacterPrefabs();
            if (filtered != null)
            {
                for (var i = 0; i < filtered.Count; i++)
                {
                    var p = filtered[i];
                    if (p != null)
                        pool.Add(p);
                }
            }

            if (pool.Count > 0)
                return pool;

            // Fallback: if naming filters miss assets, still allow village spawn from the casual resources folder.
            var raw = Resources.LoadAll<GameObject>(GameConstants.NpcCasualCharactersResourcesFolder);
            if (raw != null)
            {
                for (var i = 0; i < raw.Length; i++)
                {
                    var p = raw[i];
                    if (p == null)
                        continue;
                    if (p.GetComponentInChildren<Renderer>(true) == null)
                        continue;
                    pool.Add(p);
                }
            }

            return pool;
        }

        static bool TryGetReferenceHouseTransform(int houseIndex, out Transform house)
        {
            house = null;
            var houses = CollectNumberedHouseTransforms();
            foreach (var h in houses)
            {
                if (h == null || h.gameObject == null)
                    continue;
                if (!TryParseNumberedHouseIndex(h.gameObject.name, out var idx))
                    continue;
                if (idx != houseIndex)
                    continue;
                house = h;
                return true;
            }
            return false;
        }

        static bool TrySampleVillagePoint(
            Vector3 center,
            float minRadius,
            float maxRadius,
            Terrain terrain,
            float fallbackY,
            List<Vector3> occupied,
            float minSeparation,
            bool checkPhysicsOccupancy,
            out Vector3 point)
        {
            point = default;
            var minR = Mathf.Max(0.1f, Mathf.Min(minRadius, maxRadius));
            var maxR = Mathf.Max(minR + 0.1f, Mathf.Max(minRadius, maxRadius));
            var minSepSqr = Mathf.Max(0.1f, minSeparation) * Mathf.Max(0.1f, minSeparation);

            for (var attempt = 0; attempt < 72; attempt++)
            {
                var angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                var radius = Mathf.Sqrt(Random.Range(minR * minR, maxR * maxR));
                var candidate = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                candidate = ResolveGroundPoint(candidate, terrain, fallbackY);

                var tooClose = false;
                if (occupied != null)
                {
                    for (var i = 0; i < occupied.Count; i++)
                    {
                        var d = occupied[i] - candidate;
                        d.y = 0f;
                        if (d.sqrMagnitude < minSepSqr)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                }
                if (tooClose)
                    continue;

                if (checkPhysicsOccupancy && !IsSpawnSpaceFree(candidate, 0.5f))
                    continue;

                point = candidate;
                return true;
            }

            return false;
        }

        static Vector3 ResolveGroundPoint(Vector3 candidate, Terrain terrain, float fallbackY)
        {
            if (terrain != null && terrain.terrainData != null)
            {
                var tpos = terrain.transform.position;
                var ts = terrain.terrainData.size;
                if (candidate.x >= tpos.x && candidate.x <= tpos.x + ts.x &&
                    candidate.z >= tpos.z && candidate.z <= tpos.z + ts.z)
                {
                    var y = terrain.SampleHeight(candidate) + tpos.y;
                    return new Vector3(candidate.x, y, candidate.z);
                }
            }
            return SnapToSceneGroundWorld(candidate + Vector3.up * 2f, fallbackY);
        }

        static NpcDefinition CreateVillagerRuntimeDefinition(NpcDefinition template, string displayName, int npcIndex, int referenceHouseIndex)
        {
            var def = Object.Instantiate(template);
            var cleanName = string.IsNullOrWhiteSpace(displayName) ? $"Villager {npcIndex}" : displayName.Trim();
            def.npcId = $"villager_{npcIndex}_{cleanName.ToLowerInvariant().Replace(" ", "_")}";
            def.displayName = cleanName;
            def.roleSummary =
                $"{cleanName} is a local villager living near House {referenceHouseIndex}. " +
                "Friendly, practical, and focused on daily life in the village.";
            def.openingLine = $"Hello, I am {cleanName}. Welcome to our village.";
            def.fallbackLines = new[]
            {
                "Village life is simple, but never boring.",
                "I can share what I know about people and places nearby."
            };
            return def;
        }

        static void LogHouseNpcShuffleOrder(string allocTag, List<GameObject> npcs)
        {
            if (npcs == null || npcs.Count == 0)
            {
                Debug.Log($"[{allocTag}] NPC queue: (empty).");
                return;
            }
            var sb = new System.Text.StringBuilder(npcs.Count * 32);
            for (var k = 0; k < npcs.Count; k++)
            {
                if (k > 0)
                    sb.Append(" | ");
                sb.Append(k + 1).Append(": ").Append(FormatNpcAllocationLabel(npcs[k]));
            }
            Debug.Log($"[{allocTag}] NPC queue order ({npcs.Count} roots): {sb}");
        }

        static void LogHouseNpcAllocationOutcome(string allocTag, int houseCount, int npcCount, int pairCount, int successAllocations)
        {
            var expected = ExpectedHouseCount;
            Debug.Log(
                $"[{allocTag}] Outcome: successAllocations={successAllocations}, pairCount={pairCount}, " +
                $"houseAnchors={houseCount}, npcRoots={npcCount} (want {expected} each for full grid).");

            if (successAllocations != pairCount)
                Debug.LogError(
                    $"[{allocTag}] Internal mismatch: {successAllocations} OK placements vs pairCount={pairCount} (null NPC in paired range?).");

            if (houseCount == expected && npcCount == expected && successAllocations == expected)
            {
                Debug.Log($"[{allocTag}] All {expected} house↔NPC allocations completed successfully.");
                return;
            }

            if (houseCount != expected)
                Debug.LogError(
                    $"[{allocTag}] Expected {expected} house anchors but found {houseCount}. " +
                    "Unmatched houses never receive an NPC (see missing-index warning from house scan).");
            if (npcCount != expected)
                Debug.LogError(
                    $"[{allocTag}] Expected {expected} NPC movement roots but found {npcCount}. " +
                    "Check duplicate CharacterController warnings and NpcDialogueBinding coverage.");
            if (successAllocations < expected && houseCount >= expected && npcCount >= expected)
                Debug.LogError(
                    $"[{allocTag}] Expected {expected} successful placements but only {successAllocations} ran OK " +
                    $"(pairCount={pairCount}).");
        }

        static void ShuffleList<T>(IList<T> list)
        {
            if (list == null || list.Count < 2)
                return;
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        static bool TryParseNumberedHouseIndex(string objectName, out int index)
        {
            index = -1;
            if (string.IsNullOrWhiteSpace(objectName))
                return false;
            var n = objectName.Trim();
            const string cloneSuffix = "(Clone)";
            if (n.EndsWith(cloneSuffix, StringComparison.Ordinal))
                n = n.Substring(0, n.Length - cloneSuffix.Length).TrimEnd();
            if (!n.StartsWith(HouseNamePrefix, StringComparison.OrdinalIgnoreCase))
                return false;
            var suffix = n.Substring(HouseNamePrefix.Length).TrimStart();
            // "House 5" and "House_5" / "House-5" / "House.5" — int.TryParse("_5") fails and those anchors were skipped.
            var i = 0;
            while (i < suffix.Length && (char.IsWhiteSpace(suffix[i]) || suffix[i] == '_' || suffix[i] == '-' || suffix[i] == '.' || suffix[i] == '#'))
                i++;
            var digitStart = i;
            while (i < suffix.Length && char.IsDigit(suffix[i]))
                i++;
            if (i == digitStart)
                return false;
            if (!int.TryParse(suffix.Substring(digitStart, i - digitStart), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out index))
                return false;
            return index >= 1 && index <= ExpectedHouseCount;
        }

        static int TransformDepthFromRoot(Transform t)
        {
            var d = 0;
            for (var c = t; c != null; c = c.parent)
                d++;
            return d;
        }

        /// <summary>
        /// Prefer a single anchor per house index. Multiple transforms can share the same name (root + child mesh);
        /// without dedupe, <see cref="Mathf.Min"/> pairing leaves higher indices with no NPC.
        /// </summary>
        static bool PreferAsHouseAnchor(Transform candidate, Transform existing)
        {
            var dc = TransformDepthFromRoot(candidate);
            var de = TransformDepthFromRoot(existing);
            if (dc != de)
                return dc < de;
            return candidate.GetInstanceID() < existing.GetInstanceID();
        }

        static List<Transform> CollectNumberedHouseTransforms()
        {
            var byIndex = new Dictionary<int, Transform>();
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null || t.gameObject == null)
                    continue;
                if (!TryParseNumberedHouseIndex(t.gameObject.name, out var index))
                    continue;
                if (!byIndex.TryGetValue(index, out var existing))
                {
                    byIndex[index] = t;
                    continue;
                }
                if (PreferAsHouseAnchor(t, existing))
                    byIndex[index] = t;
            }
            var missingIndices = new List<int>();
            for (var hi = 1; hi <= ExpectedHouseCount; hi++)
            {
                if (!byIndex.ContainsKey(hi))
                    missingIndices.Add(hi);
            }
            if (missingIndices.Count > 0)
                Debug.LogWarning(
                    $"[{nameof(RuntimeLevelBootstrap)}] No transform matched numbered house index(es): " +
                    $"{string.Join(", ", missingIndices)}. " +
                    $"Expected names like \"{HouseNamePrefix} 7\" or \"{HouseNamePrefix}_7\" (1–{ExpectedHouseCount}). " +
                    $"Only {byIndex.Count} anchor(s) were found — NPCs beyond that cannot be paired to a house.");

            var list = new List<Transform>(ExpectedHouseCount);
            for (var i = 1; i <= ExpectedHouseCount; i++)
            {
                if (byIndex.TryGetValue(i, out var tr))
                    list.Add(tr);
            }
            return list;
        }

        void SpawnNpcCarriedItemsNearNpcs(List<GameObject> npcs, Terrain terrain, float minimumGroundY)
        {
            if (!Application.isPlaying || npcs == null || npcs.Count == 0)
                return;
            var root = transform.Find(NpcInventoryItemRootName);
            if (root != null)
                Destroy(root.gameObject);
            var itemsRootGo = new GameObject(NpcInventoryItemRootName);
            itemsRootGo.transform.SetParent(transform, false);
            var itemsRoot = itemsRootGo.transform;

            var library = new NarrativeContentLibrary();
            var inventory = new InventoryService(library);
            var catalog = library.LoadObjectArtifactCatalog();
            var idToPrefabHint = BuildItemPrefabHintMap(catalog);
            var idToLabel = BuildItemLabelMap(catalog);

            foreach (var npc in npcs)
            {
                if (npc == null)
                    continue;
                var binding = npc.GetComponent<NpcDialogueBinding>();
                var npcId = binding != null && binding.Definition != null ? binding.Definition.npcId : npc.name;
                if (string.IsNullOrWhiteSpace(npcId))
                    continue;
                inventory.EnsureSeededNpc(npcId);
                var entries = inventory.GetInventoryView(npcId);
                if (entries == null || entries.Count == 0)
                    continue;
                var basePos = npc.transform.position + npc.transform.right * 1.3f;
                var row = 0;
                foreach (var entry in entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.itemId) || entry.quantity <= 0)
                        continue;
                    var maxCount = Mathf.Clamp(entry.quantity, 1, 3);
                    for (var i = 0; i < maxCount; i++)
                    {
                        var candidate = basePos + npc.transform.forward * (row * 0.65f) + npc.transform.right * (i * 0.55f);
                        if (terrain != null)
                        {
                            var y = terrain.SampleHeight(candidate) + terrain.transform.position.y;
                            candidate.y = Mathf.Max(y, minimumGroundY) + 0.02f;
                        }
                        var itemGo = TryInstantiateInventoryItemVisual(entry.itemId, idToPrefabHint, idToLabel, candidate, itemsRoot);
                        if (itemGo == null)
                            continue;
                        itemGo.name = $"Item_{npcId}_{entry.itemId}_{row}_{i}";
                    }
                    row++;
                }
            }
        }

        public void RefreshNpcInventoryItemVisuals()
        {
            if (!Application.isPlaying)
                return;
            if (!TryGetPrimaryTerrain(out var terrain))
                return;
            var minGroundY = ResolveMinimumGroundYFromWater();
            var npcs = CollectAllNpcRoots();
            SpawnNpcCarriedItemsNearNpcs(npcs, terrain, minGroundY);
        }

        static Transform LowestCommonAncestor(Transform a, Transform b)
        {
            if (a == null || b == null)
                return null;
            var seen = new HashSet<Transform>();
            for (var x = a; x != null; x = x.parent)
                seen.Add(x);
            for (var y = b; y != null; y = y.parent)
            {
                if (seen.Contains(y))
                    return y;
            }
            return null;
        }

        static int HierarchyDistanceBetween(Transform a, Transform b)
        {
            var lca = LowestCommonAncestor(a, b);
            if (lca == null)
                return int.MaxValue;
            var da = 0;
            for (var x = a; x != null && x != lca; x = x.parent)
                da++;
            var db = 0;
            for (var y = b; y != null && y != lca; y = y.parent)
                db++;
            return da + db;
        }

        /// <summary>Steps from <paramref name="ancestor"/> down to <paramref name="t"/>; MaxValue if <paramref name="t"/> is not under <paramref name="ancestor"/>.</summary>
        static int TransformDepthUnderAncestor(Transform ancestor, Transform t)
        {
            if (ancestor == null || t == null)
                return int.MaxValue;
            if (t == ancestor)
                return 0;
            if (!t.IsChildOf(ancestor))
                return int.MaxValue;
            var d = 0;
            for (var x = t; x != null && x != ancestor; x = x.parent)
                d++;
            return d;
        }

        /// <summary>
        /// Resolves the object that should be moved for gameplay. Bindings are often on a sibling of the
        /// <see cref="CharacterController"/>; <see cref="GetComponentInParent{T}"/> misses that, so NPCs 5–8 (or any
        /// split prefab) stayed at authored positions while an empty anchor moved.
        /// </summary>
        static CharacterController PickBestCharacterControllerForAnchor(Transform anchor, CharacterController[] ccs)
        {
            if (anchor == null || ccs == null || ccs.Length == 0)
                return null;
            if (ccs.Length == 1)
                return ccs[0];
            CharacterController best = null;
            var bestPath = int.MaxValue;
            var bestDepth = int.MaxValue;
            var bestHeight = -1f;
            foreach (var cc in ccs)
            {
                if (cc == null)
                    continue;
                if (string.Equals(cc.gameObject.name, "InteractTrigger", StringComparison.Ordinal))
                    continue;
                var path = HierarchyDistanceBetween(anchor, cc.transform);
                var depthUnder = TransformDepthUnderAncestor(anchor, cc.transform);
                var h = cc.height;
                if (path < bestPath
                    || (path == bestPath && depthUnder < bestDepth)
                    || (path == bestPath && depthUnder == bestDepth && h > bestHeight))
                {
                    bestPath = path;
                    bestDepth = depthUnder;
                    bestHeight = h;
                    best = cc;
                }
            }
            if (best == null)
            {
                foreach (var cc in ccs)
                {
                    if (cc != null)
                        return cc;
                }
            }
            return best;
        }

        static GameObject ResolveDialogueNpcMovementRoot(Transform t)
        {
            if (t == null)
                return null;
            if (t.GetComponent<CharacterController>() != null)
                return t.gameObject;
            var childCcs = t.GetComponentsInChildren<CharacterController>(true);
            if (childCcs != null && childCcs.Length > 0)
            {
                var pick = PickBestCharacterControllerForAnchor(t, childCcs);
                if (pick != null)
                    return pick.gameObject;
            }
            var inParent = t.GetComponentInParent<CharacterController>();
            if (inParent != null)
                return inParent.gameObject;

            for (var cur = t.parent; cur != null; cur = cur.parent)
            {
                var ccs = cur.GetComponentsInChildren<CharacterController>(true);
                if (ccs == null || ccs.Length == 0)
                    continue;
                var pick = PickBestCharacterControllerForAnchor(t, ccs);
                if (pick != null)
                    return pick.gameObject;
            }
            return t.gameObject;
        }

        static bool SourceHasDialogueAnchor(GameObject sourceGo)
        {
            if (sourceGo == null)
                return false;
            if (sourceGo.GetComponent<NpcDialogueBinding>() != null)
                return true;
            if (sourceGo.GetComponentInParent<NpcDialogueBinding>(true) != null)
                return true;
            if (sourceGo.GetComponentInChildren<NpcDialogueBinding>(true) != null)
                return true;
            if (sourceGo.GetComponent<NpcInteractable>() != null)
                return true;
            if (sourceGo.GetComponentInParent<NpcInteractable>(true) != null)
                return true;
            return sourceGo.GetComponentInChildren<NpcInteractable>(true) != null;
        }

        static bool SourceAnchorsSameNpcRig(Transform source, Transform movement)
        {
            if (source == null || movement == null)
                return false;
            if (source == movement)
                return true;
            if (movement.IsChildOf(source))
                return true;
            if (source.IsChildOf(movement))
                return true;
            var d = HierarchyDistanceBetween(source, movement);
            return d >= 0 && d <= MaxNpcRigAnchorToCharacterControllerHops;
        }

        /// <summary>
        /// Village house grid expects exactly <see cref="ExpectedNpcCount"/> civilian NPC roots. Bosses like the
        /// scene Ghoul carry <see cref="NpcDialogueBinding"/> for story dialogue and must not consume a house slot.
        /// </summary>
        static bool IsExcludedFromHouseNpcPlacement(Transform t)
        {
            if (t == null)
                return false;
            if (t.GetComponentInParent<GhoulMenaceController>(true) != null)
                return true;
            if (t.GetComponentInChildren<GhoulMenaceController>(true) != null)
                return true;
            var b = t.GetComponent<NpcDialogueBinding>() ?? t.GetComponentInParent<NpcDialogueBinding>(true);
            if (b?.Definition != null && GhoulMenaceController.IsGhoulStoryNpcId(b.Definition.npcId))
                return true;
            return false;
        }

        static List<GameObject> CollectAllNpcRoots()
        {
            var list = new List<GameObject>();
            var seen = new HashSet<GameObject>();
            var duplicateMovementSkips = new List<(string sourceName, string movementName)>();
            var playerGo = GameObject.FindGameObjectWithTag(GameConstants.PlayerTag);

            void tryAdd(GameObject sourceGo)
            {
                if (sourceGo == null)
                    return;
                if (!SourceHasDialogueAnchor(sourceGo))
                    return;
                if (IsExcludedFromHouseNpcPlacement(sourceGo.transform))
                    return;
                var movementRoot = ResolveDialogueNpcMovementRoot(sourceGo.transform);
                if (movementRoot == null || movementRoot == playerGo)
                    return;
                if (IsExcludedFromHouseNpcPlacement(movementRoot.transform))
                    return;
                if (!SourceAnchorsSameNpcRig(sourceGo.transform, movementRoot.transform))
                    return;
                if (seen.Add(movementRoot))
                    list.Add(movementRoot);
                else
                    duplicateMovementSkips.Add((sourceGo.name, movementRoot.name));
            }

            foreach (var binding in Object.FindObjectsByType<NpcDialogueBinding>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (binding == null || binding.gameObject == null)
                    continue;
                tryAdd(binding.gameObject);
            }

            foreach (var inter in Object.FindObjectsByType<NpcInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (inter == null || inter.gameObject == null)
                    continue;
                // Standard NPC rig: interactable lives on child "InteractTrigger" and resolves to the same CC root as
                // NpcDialogueBinding on the parent — only creates duplicate skip noise.
                if (string.Equals(inter.gameObject.name, "InteractTrigger", StringComparison.Ordinal))
                    continue;
                tryAdd(inter.gameObject);
            }

            foreach (var cc in Object.FindObjectsByType<CharacterController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (cc == null || cc.gameObject == null)
                    continue;
                tryAdd(cc.gameObject);
            }

            if (duplicateMovementSkips.Count > 0)
            {
                var detail = string.Join("; ", duplicateMovementSkips.ConvertAll(s => $"{s.sourceName}→`{s.movementName}`"));
                Debug.LogWarning(
                    $"[{nameof(RuntimeLevelBootstrap)}] {duplicateMovementSkips.Count} dialogue object(s) share the same " +
                    $"CharacterController root as another NPC and were skipped for house placement (only one position per CC): {detail}");
            }

            return list;
        }

        static Dictionary<string, string> BuildItemPrefabHintMap(ObjectArtifactCatalogDoc catalog)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (catalog == null)
                return map;
            void add(List<CatalogEntry> list)
            {
                if (list == null)
                    return;
                foreach (var e in list)
                {
                    if (e == null || string.IsNullOrWhiteSpace(e.id))
                        continue;
                    map[e.id.Trim()] = e.prefabHint ?? string.Empty;
                }
            }
            add(catalog.objects);
            add(catalog.artifacts);
            return map;
        }

        static Dictionary<string, string> BuildItemLabelMap(ObjectArtifactCatalogDoc catalog)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (catalog == null)
                return map;
            void add(List<CatalogEntry> list)
            {
                if (list == null)
                    return;
                foreach (var e in list)
                {
                    if (e == null || string.IsNullOrWhiteSpace(e.id))
                        continue;
                    map[e.id.Trim()] = string.IsNullOrWhiteSpace(e.label) ? e.id.Trim() : e.label.Trim();
                }
            }
            add(catalog.objects);
            add(catalog.artifacts);
            return map;
        }

        static GameObject TryInstantiateInventoryItemVisual(
            string itemId,
            Dictionary<string, string> prefabHints,
            Dictionary<string, string> labels,
            Vector3 worldPos,
            Transform parent)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return null;
            var prefab = ResolveItemPrefab(itemId, prefabHints, labels);
            GameObject go;
            if (prefab != null)
            {
                go = Instantiate(prefab, parent, false);
                go.transform.position = worldPos;
                go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                var col = go.GetComponentInChildren<Collider>();
                if (col == null)
                {
                    var mf = go.GetComponentInChildren<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                    {
                        var mc = go.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                        mc.convex = true;
                    }
                }
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.SetParent(parent, false);
                go.transform.position = worldPos;
                go.transform.localScale = new Vector3(0.25f, 0.2f, 0.25f);
            }

            var pickup = go.GetComponent<ItemPickup>();
            if (pickup == null)
                pickup = go.AddComponent<ItemPickup>();
            pickup.Configure(NormalizePickupItemId(itemId));
            return go;
        }

        static string HintedCatalogPathToResourcesKey(string hintedAssetPath)
        {
            var p = hintedAssetPath.Trim().Replace('\\', '/');
            if (p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                p = p.Substring("Assets/".Length);
            if (p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                p = p.Substring(0, p.Length - ".prefab".Length);
            var idx = p.IndexOf("Resources/", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return p.Substring(idx + "Resources/".Length);
            return p;
        }

        static string NormalizePickupItemId(string rawItemId)
        {
            if (string.IsNullOrWhiteSpace(rawItemId))
                return string.Empty;
            var key = rawItemId.Trim();
            if (string.Equals(key, "book_v1", StringComparison.OrdinalIgnoreCase))
                return BookOneItemId;
            if (string.Equals(key, "book_v2", StringComparison.OrdinalIgnoreCase))
                return BookTwoItemId;
            return key;
        }

        static GameObject ResolveItemPrefab(
            string itemId,
            Dictionary<string, string> prefabHints,
            Dictionary<string, string> labels)
        {
            var candidates = new List<string>();
            string hintedAssetPath = null;
            if (prefabHints != null && prefabHints.TryGetValue(itemId, out var hint) && !string.IsNullOrWhiteSpace(hint))
            {
                var normalized = hint.Replace('\\', '/');
                hintedAssetPath = normalized;
                var filename = System.IO.Path.GetFileNameWithoutExtension(normalized);
                if (!string.IsNullOrWhiteSpace(filename))
                    candidates.Add(filename);
            }
            if (!string.IsNullOrWhiteSpace(hintedAssetPath))
            {
                var resKey = HintedCatalogPathToResourcesKey(hintedAssetPath);
                if (!string.IsNullOrWhiteSpace(resKey))
                {
                    var fromResources = Resources.Load<GameObject>(resKey);
                    if (fromResources != null)
                        return fromResources;
                }
            }
#if UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(hintedAssetPath))
            {
                var editorPath = hintedAssetPath;
                if (editorPath.StartsWith("Assets/Medieval props/", StringComparison.OrdinalIgnoreCase))
                    editorPath = "Assets/Resources/Medieval props/" + editorPath.Substring("Assets/Medieval props/".Length);
                if (editorPath.StartsWith("Assets/Books/", StringComparison.OrdinalIgnoreCase))
                    editorPath = "Assets/Resources/Books/" + editorPath.Substring("Assets/Books/".Length);
                var direct = AssetDatabase.LoadAssetAtPath<GameObject>(editorPath);
                if (direct != null)
                    return direct;
            }
#endif
            if (labels != null && labels.TryGetValue(itemId, out var label) && !string.IsNullOrWhiteSpace(label))
                candidates.Add(label);
            candidates.Add(itemId);
            var normalizedTarget = NormalizeIdLike(itemId);

            for (var i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (string.IsNullOrWhiteSpace(c))
                    continue;
                var trimmed = c.Trim();
                var r1 = Resources.Load<GameObject>(trimmed);
                if (r1 != null)
                    return r1;
                var r2 = Resources.Load<GameObject>("Medieval props/Prefabs/" + trimmed);
                if (r2 != null)
                    return r2;
                var r3 = Resources.Load<GameObject>("Prefabs/" + trimmed);
                if (r3 != null)
                    return r3;
#if UNITY_EDITOR
                if (!string.IsNullOrWhiteSpace(hintedAssetPath))
                {
                    var byName = AssetDatabase.FindAssets(trimmed + " t:prefab");
                    if (byName != null && byName.Length > 0)
                    {
                        var p = AssetDatabase.GUIDToAssetPath(byName[0]);
                        var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                        if (loaded != null)
                            return loaded;
                    }
                }
                // Broad editor fallback: scan medieval prefabs by normalized id/name.
                var allPrefabs = AssetDatabase.FindAssets("t:prefab", new[] { "Assets/Resources/Medieval props/Prefabs", "Assets" });
                if (allPrefabs != null && allPrefabs.Length > 0)
                {
                    for (var ai = 0; ai < allPrefabs.Length; ai++)
                    {
                        var path = AssetDatabase.GUIDToAssetPath(allPrefabs[ai]);
                        if (string.IsNullOrWhiteSpace(path))
                            continue;
                        var file = System.IO.Path.GetFileNameWithoutExtension(path);
                        if (string.IsNullOrWhiteSpace(file))
                            continue;
                        var nf = NormalizeIdLike(file);
                        if (nf == normalizedTarget || nf == NormalizeIdLike(trimmed) || nf.Contains(normalizedTarget, StringComparison.OrdinalIgnoreCase))
                        {
                            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                            if (loaded != null)
                                return loaded;
                        }
                    }
                }
#endif

                foreach (var existing in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (existing == null || existing.gameObject == null)
                        continue;
                    var n = existing.gameObject.name;
                    if (string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase))
                        return existing.gameObject;
                    if (NormalizeIdLike(n) == NormalizeIdLike(trimmed))
                        return existing.gameObject;
                    if (NormalizeIdLike(n).Contains(NormalizeIdLike(trimmed), StringComparison.OrdinalIgnoreCase))
                        return existing.gameObject;
                }
            }
            Debug.LogWarning($"[RuntimeLevelBootstrap] Item prefab unresolved for itemId='{itemId}'. Tried candidates: {string.Join(", ", candidates)}");
            return null;
        }

        static string NormalizeIdLike(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return string.Empty;
            var src = s.Trim();
            var arr = new System.Text.StringBuilder(src.Length);
            for (var i = 0; i < src.Length; i++)
            {
                var c = src[i];
                if (char.IsLetterOrDigit(c))
                    arr.Append(char.ToLowerInvariant(c));
            }
            return arr.ToString();
        }

        static float ResolveMinimumGroundYFromWater()
        {
            var minGroundY = float.NegativeInfinity;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (t == null || t.gameObject == null)
                    continue;
                var n = t.gameObject.name;
                if (n.IndexOf("water surface", System.StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("water", System.StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("ocean", System.StringComparison.OrdinalIgnoreCase) < 0
                    && n.IndexOf("river", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                minGroundY = Mathf.Max(minGroundY, t.position.y + 1.0f);
            }
            return minGroundY;
        }

        bool TryPickBuddyTigerTestSpawnPositions(
            Terrain terrain,
            float minGroundY,
            float maxPlanarHeroFromTigerMeters,
            out Vector3 tigerBuddyAnchor,
            out Vector3 heroBuddyGround)
        {
            tigerBuddyAnchor = default;
            heroBuddyGround = default;
            if (maxPlanarHeroFromTigerMeters <= 0.01f)
                return false;
            if (!TryGetRandomTerrainPoint(
                    terrain,
                    out tigerBuddyAnchor,
                    occupied: null,
                    minDistance: 0f,
                    centerRadiusLimit: 0f,
                    minimumGroundY: minGroundY,
                    maxSampleAttempts: 200))
                return false;
            var buddyOccupied = new List<Vector3> { tigerBuddyAnchor };
            return TryGetRandomTerrainPointInPlanarDisc(
                terrain,
                tigerBuddyAnchor,
                maxPlanarHeroFromTigerMeters,
                out heroBuddyGround,
                buddyOccupied,
                minDistance: 0.5f,
                minimumGroundY: minGroundY,
                maxSampleAttempts: 240);
        }

        void SpawnAnimalsOnIslandTerrain(
            Terrain terrain,
            float islandScale,
            List<Vector3> occupied,
            Vector3 heroWorldPosition,
            bool heroSpawnGroundResolved,
            Vector3? forcedFirstTigerGroundPosition = null)
        {
            if (terrain == null)
                return;
            if (!heroSpawnGroundResolved)
                return;
            var root = transform.Find(ContentRootName) ?? transform;
            ClearExistingAnimalNpcs(root);
            var minGroundY = ResolveMinimumGroundYFromWater();

            BootstrapAnimalResources.RefreshCache();
            var animalPrefabs = BootstrapAnimalResources.GetAllAnimalPrefabs();
            if (animalPrefabs == null || animalPrefabs.Count == 0)
                return;

            foreach (var animalPrefab in animalPrefabs)
            {
                if (animalPrefab == null)
                    continue;
                var prefabNameForCount = animalPrefab.name ?? "";
                var isTigerPrefab = prefabNameForCount.IndexOf("tiger", System.StringComparison.OrdinalIgnoreCase) >= 0;
                var countForPrefab = isTigerPrefab
                    ? TigersSpawnCount
                    : AnimalsPerSpeciesCount;
                for (var i = 0; i < countForPrefab; i++)
                {
                    Vector3 animalGroundPos;
                    bool placed;
                    if (isTigerPrefab
                        && forcedFirstTigerGroundPosition.HasValue
                        && i == 0)
                    {
                        animalGroundPos = forcedFirstTigerGroundPosition.Value;
                        placed = true;
                    }
                    else if (isTigerPrefab)
                    {
                        placed = TryGetRandomTerrainPoint(
                            terrain,
                            out animalGroundPos,
                            occupied,
                            minDistance: TigerSpawnMinSeparationMeters,
                            centerRadiusLimit: 0f,
                            minimumGroundY: minGroundY,
                            maxSampleAttempts: 400);
                    }
                    else
                    {
                        placed = TryGetRandomTerrainPointInPlanarDisc(
                            terrain,
                            heroWorldPosition,
                            AnimalSpawnRadiusFromHeroMeters,
                            out animalGroundPos,
                            occupied,
                            minDistance: 6f,
                            minimumGroundY: minGroundY,
                            maxSampleAttempts: 160);
                    }

                    if (!placed)
                        continue;

                    var registerChicken = !isTigerPrefab
                        && prefabNameForCount.IndexOf("chicken", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    InstantiateConfiguredAnimalOnIsland(
                        animalPrefab,
                        root,
                        islandScale,
                        animalGroundPos,
                        i + 1,
                        isTigerPrefab,
                        occupied,
                        registerChicken);
                }
            }
        }

        void InstantiateConfiguredAnimalOnIsland(
            GameObject animalPrefab,
            Transform root,
            float islandScale,
            Vector3 animalGroundPos,
            int indexOneBased,
            bool isTiger,
            List<Vector3> occupied,
            bool registerLiveChickenInventoryPickup = false)
        {
            var animal = Instantiate(animalPrefab, root, false);
            animal.name = $"Animal_{animalPrefab.name}_{indexOneBased:00}";
            ThirdPartyAnimalRig.StripForNavMeshAuthoring(animal);
            if (animal.GetComponent<AnimalNpc>() == null)
                animal.AddComponent<AnimalNpc>();

            var cc = animal.GetComponent<CharacterController>();
            if (cc == null)
                cc = animal.AddComponent<CharacterController>();
            var targetHeight = Random.Range(MinCharacterHeightMeters, MaxCharacterHeightMeters) * islandScale;
            ApplyTargetCharacterHeight(animal, targetHeight);
            cc.stepOffset = 0.15f;
            cc.minMoveDistance = 0f;
            cc.skinWidth = 0.05f;

            animal.transform.position = animalGroundPos;
            animal.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            LiftCharacterFeetAboveGround(animal, Mathf.Max(spawnFootClearance, 0.08f));

            if (isTiger)
            {
                if (animal.GetComponent<TigerNpcWanderAi>() == null)
                    animal.AddComponent<TigerNpcWanderAi>();
            }
            else
            {
                if (animal.GetComponent<AnimalNpcWanderAi>() == null)
                    animal.AddComponent<AnimalNpcWanderAi>();
            }

            PackAnimalAnimatorDriver.TryAddForAnimalLocomotion(animal);
            if (registerLiveChickenInventoryPickup)
            {
                var pick = animal.GetComponent<ItemPickup>() ?? animal.AddComponent<ItemPickup>();
                pick.Configure(GameConstants.LiveChickenItemId);
                EnsureChickenPickupProxyCollider(animal);
            }

            occupied.Add(animal.transform.position);
        }

        /// <summary>
        /// Large trigger collider so raycasts (with <see cref="QueryTriggerInteraction.Collide"/>) reliably hit chickens
        /// before terrain or tiny rig colliders; does not affect NavMesh or root physics.
        /// </summary>
        static void EnsureChickenPickupProxyCollider(GameObject animal)
        {
            if (animal == null || animal.transform.Find("ItemPickupProxy") != null)
                return;
            var proxy = new GameObject("ItemPickupProxy");
            proxy.transform.SetParent(animal.transform, false);
            proxy.transform.localPosition = new Vector3(0f, 0.85f, 0f);
            var sc = proxy.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 2.4f;
        }

        /// <summary>
        /// Authored scenes: chickens/horses per numbered house, one kitty and one dog near each dialogue NPC (tigers stay separate).
        /// </summary>
        void SpawnAuthoredAmbientWildlife(GameObject playerGo)
        {
            if (!Application.isPlaying)
                return;

            var parent = transform.Find(ContentRootName) ?? transform;
            var existingBucket = parent.Find("_AmbientWildlife");
            if (existingBucket != null)
            {
                if (Application.isPlaying)
                    Destroy(existingBucket.gameObject);
                else
                    DestroyImmediate(existingBucket.gameObject);
            }

            var bucketGo = new GameObject("_AmbientWildlife");
            bucketGo.transform.SetParent(parent, false);
            var bucket = bucketGo.transform;

            BootstrapAnimalResources.RefreshCache();
            TryGetPrimaryTerrain(out var terrain);
            if (terrain != null && terrain.terrainData != null)
                EnsureTerrainCollider(terrain);
            var islandScale = terrain != null && terrain.terrainData != null
                ? Mathf.Max(0.2f, terrain.terrainData.size.x / TerrainReferenceWidthMeters)
                : 1f;
            var minGroundY = ResolveMinimumGroundYFromWater();

            var occupied = new List<Vector3>(256);
            if (playerGo != null)
                occupied.Add(playerGo.transform.position);
            var ambientNpcAnchors = CollectAmbientNpcAnchorsExcludingVillagers();
            foreach (var n in ambientNpcAnchors)
            {
                if (n != null)
                    occupied.Add(n.transform.position);
            }

            var chickenPrefab = FindAuthoredAmbientAnimalPrefab("chicken");
            var horsePrefab = FindAuthoredAmbientAnimalPrefab("horse");
            var kittyPrefab = FindAuthoredAmbientAnimalPrefab("kitty");
            var dogPrefab = FindAuthoredAmbientAnimalPrefab("dog");

            var spawnSeq = 0;
            var houses = CollectNumberedHouseTransforms();
            if (houses.Count > 0)
            {
                if (chickenPrefab != null)
                {
                    foreach (var house in houses)
                    {
                        if (house == null)
                            continue;
                        var nCh = Random.Range(AmbientAuthoredChickenPerHouseMin, AmbientAuthoredChickenPerHouseMax + 1);
                        for (var i = 0; i < nCh; i++)
                        {
                            if (!TrySampleDiscGroundPosition(
                                    house.position,
                                    AmbientAuthoredChickenRadiusMeters,
                                    terrain,
                                    minGroundY,
                                    occupied,
                                    AmbientAuthoredMinSeparationMeters,
                                    out var pos,
                                    48))
                                continue;
                            InstantiateConfiguredAnimalOnIsland(
                                chickenPrefab,
                                bucket,
                                islandScale,
                                pos,
                                ++spawnSeq,
                                isTiger: false,
                                occupied,
                                registerLiveChickenInventoryPickup: true);
                        }
                    }
                }
                else
                    Debug.LogWarning($"[{nameof(RuntimeLevelBootstrap)}] Ambient chickens skipped: no Chicken_* prefab in Resources/{GameConstants.AnimalsFreeResourcesFolder}.");

                if (horsePrefab != null)
                {
                    foreach (var house in houses)
                    {
                        if (house == null)
                            continue;
                        var nH = Random.Range(AmbientAuthoredHorsePerHouseMin, AmbientAuthoredHorsePerHouseMax + 1);
                        for (var i = 0; i < nH; i++)
                        {
                            if (!TrySampleDiscGroundPosition(
                                    house.position,
                                    AmbientAuthoredHorseRadiusMeters,
                                    terrain,
                                    minGroundY,
                                    occupied,
                                    AmbientAuthoredMinSeparationMeters,
                                    out var pos,
                                    72))
                                continue;
                            InstantiateConfiguredAnimalOnIsland(
                                horsePrefab,
                                bucket,
                                islandScale,
                                pos,
                                ++spawnSeq,
                                isTiger: false,
                                occupied,
                                registerLiveChickenInventoryPickup: false);
                        }
                    }
                }
                else
                    Debug.LogWarning($"[{nameof(RuntimeLevelBootstrap)}] Ambient horses skipped: no Horse_* prefab in Resources/{GameConstants.AnimalsFreeResourcesFolder}.");
            }

            if (kittyPrefab != null || dogPrefab != null)
            {
                foreach (var npc in ambientNpcAnchors)
                {
                    if (npc == null)
                        continue;
                    var center = npc.transform.position;
                    if (kittyPrefab != null
                        && TrySampleDiscGroundPosition(
                            center,
                            AmbientAuthoredNpcPetRadiusMeters,
                            terrain,
                            minGroundY,
                            occupied,
                            AmbientAuthoredMinSeparationMeters,
                            out var kPos,
                            64))
                    {
                        InstantiateConfiguredAnimalOnIsland(
                            kittyPrefab,
                            bucket,
                            islandScale,
                            kPos,
                            ++spawnSeq,
                            isTiger: false,
                            occupied,
                            registerLiveChickenInventoryPickup: false);
                    }

                    if (dogPrefab != null
                        && TrySampleDiscGroundPosition(
                            center,
                            AmbientAuthoredNpcPetRadiusMeters,
                            terrain,
                            minGroundY,
                            occupied,
                            AmbientAuthoredMinSeparationMeters,
                            out var dPos,
                            64))
                    {
                        InstantiateConfiguredAnimalOnIsland(
                            dogPrefab,
                            bucket,
                            islandScale,
                            dPos,
                            ++spawnSeq,
                            isTiger: false,
                            occupied,
                            registerLiveChickenInventoryPickup: false);
                    }
                }
            }

            if (kittyPrefab == null)
                Debug.LogWarning($"[{nameof(RuntimeLevelBootstrap)}] Ambient kitty skipped: no Kitty_* prefab in Resources/{GameConstants.AnimalsFreeResourcesFolder}.");
            if (dogPrefab == null)
                Debug.LogWarning($"[{nameof(RuntimeLevelBootstrap)}] Ambient dog skipped: no Dog_* prefab in Resources/{GameConstants.AnimalsFreeResourcesFolder}.");

            Debug.Log(
                $"[{nameof(RuntimeLevelBootstrap)}] Ambient wildlife spawned under `{bucket.name}` " +
                $"(houses={houses.Count}, npcAnchors={ambientNpcAnchors.Count}, seq={spawnSeq}).");
        }

        static List<GameObject> CollectAmbientNpcAnchorsExcludingVillagers()
        {
            var list = new List<GameObject>(64);
            foreach (var n in CollectAllNpcRoots())
            {
                if (n == null || IsVillageRuntimeNpc(n))
                    continue;
                list.Add(n);
            }
            return list;
        }

        static bool IsVillageRuntimeNpc(GameObject npcRoot)
        {
            if (npcRoot == null)
                return false;
            if (npcRoot.transform.parent != null
                && string.Equals(npcRoot.transform.parent.name, VillageRuntimeRootName, StringComparison.Ordinal))
                return true;
            if (npcRoot.name.StartsWith("Villager_", StringComparison.OrdinalIgnoreCase))
                return true;
            var b = npcRoot.GetComponent<NpcDialogueBinding>();
            var id = b != null && b.Definition != null ? b.Definition.npcId : null;
            return !string.IsNullOrWhiteSpace(id)
                && id.StartsWith("villager_", StringComparison.OrdinalIgnoreCase);
        }

        static GameObject FindAuthoredAmbientAnimalPrefab(string nameContains)
        {
            if (string.IsNullOrWhiteSpace(nameContains))
                return null;
            foreach (var p in BootstrapAnimalResources.GetAllAnimalPrefabs())
            {
                if (p == null)
                    continue;
                var nm = p.name ?? string.Empty;
                if (nm.IndexOf("tiger", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (nm.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                    return p;
            }

            return null;
        }

        bool TrySampleDiscGroundPosition(
            Vector3 centerWorld,
            float outerRadiusMeters,
            Terrain terrain,
            float minimumGroundY,
            List<Vector3> occupied,
            float minSeparationMeters,
            out Vector3 groundWorld,
            int maxAttempts)
        {
            groundWorld = default;
            if (outerRadiusMeters <= 0.01f)
                return false;
            if (terrain != null && terrain.terrainData != null)
                return TryGetRandomTerrainPointInPlanarDisc(
                    terrain,
                    centerWorld,
                    outerRadiusMeters,
                    out groundWorld,
                    occupied,
                    minSeparationMeters,
                    minimumGroundY,
                    maxAttempts);

            for (var a = 0; a < maxAttempts; a++)
            {
                var u = Random.value;
                var v = Random.value;
                var r = outerRadiusMeters * Mathf.Sqrt(u);
                var theta = Mathf.PI * 2f * v;
                var x = centerWorld.x + Mathf.Cos(theta) * r;
                var z = centerWorld.z + Mathf.Sin(theta) * r;
                var guess = new Vector3(x, centerWorld.y + 120f, z);
                var p = SnapToSceneGroundWorld(guess, centerWorld.y);
                if (p.y < minimumGroundY)
                    continue;
                if (occupied != null && minSeparationMeters > 0f)
                {
                    var bad = false;
                    foreach (var o in occupied)
                    {
                        if ((o - p).sqrMagnitude < minSeparationMeters * minSeparationMeters)
                        {
                            bad = true;
                            break;
                        }
                    }

                    if (bad)
                        continue;
                }

                groundWorld = p;
                return true;
            }

            return false;
        }

        /// <summary>
        /// When using an authored scene layout, island animal spawn is skipped; still place tigers on the primary terrain.
        /// </summary>
        void EnsureAuthoredSceneRuntimeTigers(GameObject playerGo)
        {
            if (!Application.isPlaying || playerGo == null)
                return;
            if (!TryGetPrimaryTerrain(out var terrain) || terrain.terrainData == null)
                return;
            EnsureTerrainCollider(terrain);
            var islandScale = Mathf.Max(0.2f, terrain.terrainData.size.x / TerrainReferenceWidthMeters);
            var minGroundY = ResolveMinimumGroundYFromWater();
            var root = transform.Find(ContentRootName) ?? transform;

            foreach (var tw in Object.FindObjectsByType<TigerNpcWanderAi>(
                         FindObjectsInactive.Exclude,
                         FindObjectsSortMode.None))
            {
                if (tw != null)
                    Destroy(tw.gameObject);
            }

            BootstrapAnimalResources.RefreshCache();
            GameObject tigerPrefab = null;
            foreach (var p in BootstrapAnimalResources.GetAllAnimalPrefabs())
            {
                if (p == null)
                    continue;
                var nm = p.name ?? "";
                if (nm.IndexOf("tiger", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    tigerPrefab = p;
                    break;
                }
            }

            if (tigerPrefab == null)
                return;

            Vector3? forcedBuddyTiger = null;
            if (!spawnHeroNearCastle
                && testSpawnHeroNearTigerRadiusMeters > 0.01f
                && TryPickBuddyTigerTestSpawnPositions(
                    terrain,
                    minGroundY,
                    testSpawnHeroNearTigerRadiusMeters,
                    out var buddyAnchor,
                    out var heroBuddyGround))
            {
                forcedBuddyTiger = buddyAnchor;
                var cc = playerGo.GetComponent<CharacterController>();
                var keepY = playerGo.transform.position.y;
                if (cc != null)
                    cc.enabled = false;
                playerGo.transform.position = new Vector3(heroBuddyGround.x, keepY, heroBuddyGround.z);
                if (cc != null)
                    cc.enabled = true;
            }

            var occupied = new List<Vector3>(32) { playerGo.transform.position };
            foreach (var npc in CollectAllNpcRoots())
            {
                if (npc != null)
                    occupied.Add(npc.transform.position);
            }

            for (var i = 0; i < TigersSpawnCount; i++)
            {
                Vector3 pos;
                bool placed;
                if (forcedBuddyTiger.HasValue && i == 0)
                {
                    pos = forcedBuddyTiger.Value;
                    placed = true;
                }
                else
                {
                    placed = TryGetRandomTerrainPoint(
                        terrain,
                        out pos,
                        occupied,
                        minDistance: TigerSpawnMinSeparationMeters,
                        centerRadiusLimit: 0f,
                        minimumGroundY: minGroundY,
                        maxSampleAttempts: 400);
                }

                if (!placed)
                    continue;

                InstantiateConfiguredAnimalOnIsland(
                    tigerPrefab,
                    root,
                    islandScale,
                    pos,
                    i + 1,
                    isTiger: true,
                    occupied,
                    registerLiveChickenInventoryPickup: false);
            }
        }

        /// <summary>
        /// Ensures scene-authored spider_* roots behave like tigers (predator approach + locomotion driver).
        /// </summary>
        static void EnsureAuthoredSceneRuntimeSpiders()
        {
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (t == null || t.gameObject == null)
                    continue;
                var n = t.gameObject.name ?? string.Empty;
                if (!n.StartsWith("spider_", StringComparison.OrdinalIgnoreCase))
                    continue;

                var go = t.gameObject;
                if (go.GetComponent<AnimalNpc>() == null)
                    go.AddComponent<AnimalNpc>();
                if (go.GetComponent<CharacterController>() == null)
                    go.AddComponent<CharacterController>();
                if (go.GetComponent<SpiderNpcWanderAi>() == null)
                    go.AddComponent<SpiderNpcWanderAi>();

                // Correct any authored floating placement before AI/gravity takes over.
                var cc = go.GetComponent<CharacterController>();
                var hadCc = cc != null && cc.enabled;
                if (cc != null)
                    cc.enabled = false;
                var p = go.transform.position;
                var grounded = SnapToSceneGroundWorld(new Vector3(p.x, p.y + 2f, p.z), p.y);
                go.transform.position = grounded;
                LiftCharacterFeetAboveGround(go, 0.08f);
                if (cc != null)
                    cc.enabled = hadCc;

                if (go.TryGetComponent<AnimalNpcWanderAi>(out var genericWander))
                    genericWander.enabled = false;
                if (go.TryGetComponent<NpcAmbientDrift>(out var drift))
                    drift.enabled = false;

                PackAnimalAnimatorDriver.TryAddForAnimalLocomotion(go);
            }
        }

        static void ClearExistingAnimalNpcs(Transform root)
        {
            if (root == null)
                return;
            var animals = root.GetComponentsInChildren<AnimalNpc>(true);
            foreach (var animal in animals)
            {
                if (animal == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(animal.gameObject);
                else
                    DestroyImmediate(animal.gameObject);
            }
        }

        static void EnsureDialogueInteractionRangeForPlayer(GameObject playerGo, float rangeMeters)
        {
            if (playerGo == null)
                return;
            if (playerGo.TryGetComponent<PlayerInteractor>(out var interactor))
                interactor.SetInteractionDistance(rangeMeters);
        }

        static void EnsureDialogueInteractionRangeForNpc(GameObject npcGo, float rangeMeters)
        {
            if (npcGo == null)
                return;
            var trigger = npcGo.GetComponentInChildren<NpcInteractable>(true);
            if (trigger == null)
                return;
            var sphere = trigger.GetComponent<SphereCollider>();
            if (sphere == null)
                return;
            sphere.radius = Mathf.Max(0.1f, rangeMeters);
        }

        List<GameObject> EnsureIslandNpcsExist(GameObject playerGo, int requiredCount, float islandScale)
        {
            var npcs = CollectIslandNpcs(playerGo, requiredCount);
            if (npcs.Count >= requiredCount)
                return npcs;

            var root = transform.Find(ContentRootName) ?? transform;
            var playerPrefab = ResolvePlayerPrefabForNewSlice();
            var npcPrefabs = ResolveNpcPrefabsForNewSlice(playerPrefab, requiredCount);
            var missing = Mathf.Min(requiredCount - npcs.Count, npcPrefabs.Count);
            for (var i = 0; i < missing; i++)
            {
                var prefab = npcPrefabs[i];
                if (prefab == null)
                    continue;
                var npc = Instantiate(prefab, root, false);
                npc.name = $"NPC_Island_{npcs.Count + 1:00}";
                if (IsAnimalsFreeSourcePrefab(prefab))
                    ThirdPartyAnimalRig.StripForNavMeshAuthoring(npc);
                npc.transform.localScale = Vector3.one;
                if (npc.GetComponent<CharacterController>() == null)
                    npc.AddComponent<CharacterController>();
                var cc = npc.GetComponent<CharacterController>();
                cc.height = Mathf.Clamp(Random.Range(MinCharacterHeightMeters, MaxCharacterHeightMeters) * islandScale * 0.92f, 1.2f, 4.8f);
                cc.radius = Mathf.Clamp(cc.height * 0.18f, 0.2f, 0.75f);
                cc.center = new Vector3(0f, cc.height * 0.5f, 0f);
                cc.stepOffset = 0.15f;
                cc.minMoveDistance = 0f;
                cc.skinWidth = 0.05f;
                if (npc.GetComponent<NpcDialogueBinding>() == null)
                    npc.AddComponent<NpcDialogueBinding>();
                if (npc.GetComponent<NpcAmbientDrift>() == null)
                    npc.AddComponent<NpcAmbientDrift>();
                EnsureAnimatedCharacter(npc, forceIdle: false);
                npcs.Add(npc);
                if (npcs.Count >= requiredCount)
                    break;
            }

            return npcs;
        }

        static void EnsureAnimatedCharacter(GameObject characterRoot, bool forceIdle)
        {
            if (characterRoot == null)
                return;
            if (!StylizedNpcAnimatorDriver.TryAdd(characterRoot, forceIdle))
                CityPeopleLocomotionDriver.TryAdd(characterRoot);
        }

        /// <summary>
        /// Dialogue <c>move_to_location</c> for id <c>warehouse</c> needs a scene object named <c>Warehouse</c> (see location_catalog.json).
        /// If the level has no such root, create an empty anchor on the main terrain so guides can resolve the target.
        /// </summary>
        static void EnsureWarehouseDialogueAnchor()
        {
            if (!Application.isPlaying)
                return;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (t == null || t.gameObject == null || t.parent != null)
                    continue;
                if (string.Equals(t.gameObject.name, "Warehouse", StringComparison.OrdinalIgnoreCase))
                    return;
            }
            if (!TryGetPrimaryTerrain(out var terrain))
                return;
            if (!TryGetRandomTerrainPoint(terrain, out var point, null, 0f, 0f, float.NegativeInfinity, 80))
                return;
            var go = new GameObject("Warehouse");
            go.transform.SetPositionAndRotation(point, Quaternion.identity);
        }

#if UNITY_EDITOR
        static GameObject[] LoadStatuePrefabsFromAssetPathsEditor(string[] paths, int requiredCount)
        {
            var list = new List<GameObject>(paths.Length);
            foreach (var p in paths)
            {
                if (string.IsNullOrWhiteSpace(p))
                    continue;
                var g = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (g != null)
                    list.Add(g);
            }
            return list.Count >= requiredCount ? list.ToArray() : null;
        }
#endif

        GameObject[] ResolveOwlStatuePrefabsForSpawn()
        {
            if (owlStatuePrefabsOverride != null && owlStatuePrefabsOverride.Length >= OwlStatueCount)
            {
                var a = new GameObject[OwlStatueCount];
                Array.Copy(owlStatuePrefabsOverride, a, OwlStatueCount);
                return a;
            }
#if UNITY_EDITOR
            return LoadStatuePrefabsFromAssetPathsEditor(OwlStatuePrefabAssetPaths, OwlStatueCount);
#else
            return null;
#endif
        }

        GameObject[] ResolveLionStatuePrefabsForSpawn()
        {
            if (lionStatuePrefabsOverride != null && lionStatuePrefabsOverride.Length >= LionStatueCount)
            {
                var a = new GameObject[LionStatueCount];
                Array.Copy(lionStatuePrefabsOverride, a, LionStatueCount);
                return a;
            }
#if UNITY_EDITOR
            return LoadStatuePrefabsFromAssetPathsEditor(LionStatuePrefabAssetPaths, LionStatueCount);
#else
            return null;
#endif
        }

        void PlaceAkStudioStatuesOnHighTerrain(GameObject playerGo)
        {
            if (!Application.isPlaying)
                return;
            if (!TryGetPrimaryTerrain(out var terrain) || terrain.terrainData == null)
                return;

            var owls = ResolveOwlStatuePrefabsForSpawn();
            var lions = ResolveLionStatuePrefabsForSpawn();
            if (owls == null || owls.Length < OwlStatueCount || lions == null || lions.Length < LionStatueCount)
            {
                Debug.LogWarning(
                    $"[{nameof(RuntimeLevelBootstrap)}] AK Studio statues skipped: assign `{nameof(owlStatuePrefabsOverride)}` " +
                    $"({OwlStatueCount}) and `{nameof(lionStatuePrefabsOverride)}` ({LionStatueCount}) on this bootstrap, " +
                    "or run in the Editor so prefabs load from asset paths.");
                return;
            }

            var existingRoot = GameObject.Find(TerrainStatuesRootName);
            if (existingRoot != null)
                Destroy(existingRoot);

            var root = new GameObject(TerrainStatuesRootName);
            var heroHeight = ResolveHeroVisualHeightMeters(playerGo);
            if (!TryComputeTerrainHeightThresholdAtPercentile(
                    terrain,
                    StatueTerrainHeightPercentile,
                    StatueTerrainHeightSampleCount,
                    out var minTerrainWorldY))
            {
                Debug.LogWarning($"[{nameof(RuntimeLevelBootstrap)}] Statues skipped: could not sample terrain heights.");
                Destroy(root);
                return;
            }

            var planarOccupied = new List<Vector3>();
            for (var li = 0; li < LionStatueCount; li++)
            {
                TryPlaceSingleStatueInstance(
                    lions[li],
                    root.transform,
                    terrain,
                    heroHeight,
                    minTerrainWorldY,
                    planarOccupied,
                    $"lionstatue {li + 1}");
            }

            for (var oi = 0; oi < OwlStatueCount; oi++)
            {
                TryPlaceSingleStatueInstance(
                    owls[oi],
                    root.transform,
                    terrain,
                    heroHeight,
                    minTerrainWorldY,
                    planarOccupied,
                    $"owlstatue {oi + 1}");
            }

            Debug.Log(
                $"[{nameof(RuntimeLevelBootstrap)}] Placed {LionStatueCount} lion + {OwlStatueCount} owl statues " +
                $"(terrain height > P{StatueTerrainHeightPercentile * 100f:F0} ≈ {minTerrainWorldY:F1}m; " +
                $"hero visual height {heroHeight:F2}m → statue height {heroHeight * StatueHeightVsHeroMultiplier:F2}m).");
        }

        void PlaceSidekickNpcsNearOwlStatues()
        {
            if (!Application.isPlaying || !useExistingSceneEnvironment)
                return;
            if (_sidekickLineupPrefabs == null || _sidekickLineupPrefabs.Count == 0)
                return;
            if (!TryGetPrimaryTerrain(out var terrain) || terrain.terrainData == null)
                return;
            var statuesRootGo = GameObject.Find(TerrainStatuesRootName);
            if (statuesRootGo == null)
                return;

            var prefabOrder = new List<GameObject>(_sidekickLineupPrefabs);
            ShuffleList(prefabOrder);
            var pairCount = Mathf.Min(OwlStatueCount, prefabOrder.Count);
            if (pairCount == 0)
                return;

            var oldSk = GameObject.Find(SidekickNpcRootName);
            if (oldSk != null)
                Destroy(oldSk);
            var skRoot = new GameObject(SidekickNpcRootName).transform;
            var origin = terrain.transform.position;

            for (var owl = 1; owl <= pairCount; owl++)
            {
                var owlTr = FindDirectChildIgnoreCase(statuesRootGo.transform, $"owlstatue {owl}");
                if (owlTr == null)
                {
                    Debug.LogWarning(
                        $"[{nameof(RuntimeLevelBootstrap)}] Sidekick spawn: missing child `owlstatue {owl}` under `{TerrainStatuesRootName}`.");
                    continue;
                }
                var prefab = prefabOrder[owl - 1];
                if (prefab == null)
                    continue;
                var owlPos = owlTr.position;
                var angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                var dist = Random.Range(SidekickOwlOffsetMinMeters, SidekickOwlOffsetMaxMeters);
                var x = owlPos.x + Mathf.Cos(angle) * dist;
                var z = owlPos.z + Mathf.Sin(angle) * dist;
                var y = terrain.SampleHeight(new Vector3(x, origin.y, z)) + origin.y;
                var foot = Mathf.Max(spawnFootClearance, 0.08f);
                var world = new Vector3(x, y + foot, z);

                BuildNpcSingle(
                    skRoot,
                    prefab,
                    $"NPC_Sidekick_{owl:00}",
                    SidekickNpcDefinitionIndexBase + owl,
                    world,
                    bootstrapNpcUniformScale,
                    npcAnimatorReferenceSpeed,
                    useExistingSceneEnvironment,
                    sceneAnchorBuildingName,
                    humanToBuildingHeightRatio,
                    spawnFootClearance,
                    stylizedCharacterScaleFactor,
                    skipFootLiftTrustWorldSpawn: true);

                var sidekick = FindDirectChild(skRoot, $"NPC_Sidekick_{owl:00}");
                if (sidekick != null)
                {
                    if (sidekick.GetComponent<SidekickCompanion>() == null)
                        sidekick.gameObject.AddComponent<SidekickCompanion>();
                    if (sidekick.GetComponent<SidekickFollowHeroController>() == null)
                    {
                        var follow = sidekick.gameObject.AddComponent<SidekickFollowHeroController>();
                        follow.enabled = false;
                    }
                }
            }

            Debug.Log(
                $"[{nameof(RuntimeLevelBootstrap)}] Placed {pairCount} sidekick NPC(s) near owl statues " +
                $"(offset {SidekickOwlOffsetMinMeters}–{SidekickOwlOffsetMaxMeters}m).");
        }

        static float ResolveHeroVisualHeightMeters(GameObject playerGo)
        {
            if (playerGo != null)
            {
                var h = ComputeVisualHeight(playerGo);
                if (h > 0.05f)
                    return h;
                if (playerGo.TryGetComponent<CharacterController>(out var cc))
                    return Mathf.Max(0.1f, cc.height * playerGo.transform.lossyScale.y);
            }
            return 1.85f;
        }

        static bool TryComputeTerrainHeightThresholdAtPercentile(
            Terrain terrain,
            float percentile,
            int sampleCount,
            out float thresholdWorldY)
        {
            thresholdWorldY = 0f;
            if (terrain == null || terrain.terrainData == null || sampleCount < 64)
                return false;
            var td = terrain.terrainData;
            var origin = terrain.transform.position;
            var margin = Mathf.Min(30f, Mathf.Max(10f, Mathf.Min(td.size.x, td.size.z) * 0.02f));
            var heights = new float[sampleCount];
            var p = Mathf.Clamp01(percentile);
            for (var i = 0; i < sampleCount; i++)
            {
                var x = Random.Range(origin.x + margin, origin.x + td.size.x - margin);
                var z = Random.Range(origin.z + margin, origin.z + td.size.z - margin);
                heights[i] = terrain.SampleHeight(new Vector3(x, origin.y, z)) + origin.y;
            }
            Array.Sort(heights);
            var idx = Mathf.Clamp(Mathf.FloorToInt((sampleCount - 1) * p), 0, sampleCount - 1);
            thresholdWorldY = heights[idx];
            return true;
        }

        static bool TryFindRandomHighTerrainPoint(
            Terrain terrain,
            float minTerrainWorldYExclusive,
            List<Vector3> planarOccupied,
            float minHorizontalSeparation,
            int maxAttempts,
            out float x,
            out float z)
        {
            x = 0f;
            z = 0f;
            if (terrain == null || terrain.terrainData == null)
                return false;
            var td = terrain.terrainData;
            var origin = terrain.transform.position;
            var margin = Mathf.Min(30f, Mathf.Max(10f, Mathf.Min(td.size.x, td.size.z) * 0.02f));
            var attempts = Mathf.Max(1, maxAttempts);
            for (var a = 0; a < attempts; a++)
            {
                var tx = Random.Range(origin.x + margin, origin.x + td.size.x - margin);
                var tz = Random.Range(origin.z + margin, origin.z + td.size.z - margin);
                var y = terrain.SampleHeight(new Vector3(tx, origin.y, tz)) + origin.y;
                if (y <= minTerrainWorldYExclusive)
                    continue;
                if (planarOccupied != null && minHorizontalSeparation > 0.01f)
                {
                    var tooClose = false;
                    foreach (var o in planarOccupied)
                    {
                        var dx = tx - o.x;
                        var dz = tz - o.z;
                        if (dx * dx + dz * dz < minHorizontalSeparation * minHorizontalSeparation)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                    if (tooClose)
                        continue;
                }
                x = tx;
                z = tz;
                return true;
            }
            return false;
        }

        void TryPlaceSingleStatueInstance(
            GameObject prefab,
            Transform parent,
            Terrain terrain,
            float heroHeightMeters,
            float minTerrainWorldYExclusive,
            List<Vector3> planarOccupied,
            string instanceName)
        {
            if (prefab == null)
                return;
            if (!TryFindRandomHighTerrainPoint(
                    terrain,
                    minTerrainWorldYExclusive,
                    planarOccupied,
                    StatueMinSeparationMeters,
                    StatueMaxSpawnAttemptsPerStatue,
                    out var x,
                    out var z))
            {
                Debug.LogWarning(
                    $"[{nameof(RuntimeLevelBootstrap)}] No terrain spot (P{StatueTerrainHeightPercentile * 100f:F0}+, " +
                    $"{StatueMinSeparationMeters}m spacing) for `{instanceName}` after {StatueMaxSpawnAttemptsPerStatue} tries.");
                return;
            }

            var go = Instantiate(prefab, parent);
            go.name = instanceName;
            go.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            go.transform.localScale = Vector3.one;
            var unitVisualHeight = Mathf.Max(0.05f, ComputeVisualHeight(go));
            var uniform = Mathf.Clamp(StatueHeightVsHeroMultiplier * heroHeightMeters / unitVisualHeight, 0.01f, 200f);
            go.transform.localScale = Vector3.one * uniform;
            SnapStatueFeetToTerrainSurface(go, terrain, x, z);
            planarOccupied.Add(new Vector3(x, 0f, z));
        }

        static void SnapStatueFeetToTerrainSurface(GameObject root, Terrain terrain, float x, float z)
        {
            if (root == null || terrain == null || terrain.terrainData == null)
                return;
            var origin = terrain.transform.position;
            var yTerrain = terrain.SampleHeight(new Vector3(x, origin.y, z)) + origin.y;
            root.transform.position = new Vector3(x, yTerrain + 100f, z);
            if (!TryGetRenderBounds(root, out var b))
            {
                root.transform.position = new Vector3(x, yTerrain, z);
                return;
            }
            var delta = yTerrain - b.min.y;
            root.transform.position += Vector3.up * delta;
        }

        static bool TryGetPrimaryTerrain(out Terrain terrain)
        {
            terrain = null;
            foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (t == null || t.gameObject == null)
                    continue;
                if (string.Equals(t.gameObject.name, MainTerrainObjectName, System.StringComparison.OrdinalIgnoreCase))
                {
                    terrain = t;
                    return true;
                }
            }
            return false;
        }

        static void EnsureTerrainCollider(Terrain terrain)
        {
            if (terrain == null)
                return;
            if (terrain.GetComponent<TerrainCollider>() == null)
                terrain.gameObject.AddComponent<TerrainCollider>();
        }

        static bool TryGetRandomTerrainPoint(
            Terrain terrain,
            out Vector3 point,
            List<Vector3> occupied = null,
            float minDistance = 0f,
            float centerRadiusLimit = 0f,
            float minimumGroundY = float.NegativeInfinity,
            int maxSampleAttempts = 60)
        {
            point = default;
            if (terrain == null || terrain.terrainData == null)
                return false;
            var td = terrain.terrainData;
            var origin = terrain.transform.position;
            var terrainCenter = origin + new Vector3(td.size.x * 0.5f, 0f, td.size.z * 0.5f);
            var margin = Mathf.Min(30f, Mathf.Max(10f, Mathf.Min(td.size.x, td.size.z) * 0.02f));
            var attempts = Mathf.Max(1, maxSampleAttempts);
            for (var i = 0; i < attempts; i++)
            {
                var x = Random.Range(origin.x + margin, origin.x + td.size.x - margin);
                var z = Random.Range(origin.z + margin, origin.z + td.size.z - margin);
                var y = terrain.SampleHeight(new Vector3(x, origin.y, z)) + origin.y;
                var candidate = new Vector3(x, y, z);
                if (candidate.y < minimumGroundY)
                    continue;
                if (centerRadiusLimit > 0f)
                {
                    var flatDelta = new Vector2(candidate.x - terrainCenter.x, candidate.z - terrainCenter.z);
                    if (flatDelta.sqrMagnitude > centerRadiusLimit * centerRadiusLimit)
                        continue;
                }
                if (occupied != null && minDistance > 0f)
                {
                    var tooClose = false;
                    foreach (var p in occupied)
                    {
                        if ((p - candidate).sqrMagnitude < minDistance * minDistance)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                    if (tooClose)
                        continue;
                }
                point = candidate;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Samples a uniform random point on the terrain inside a horizontal disc around <paramref name="planarCenterWorld"/>.
        /// </summary>
        static bool TryGetRandomTerrainPointInPlanarDisc(
            Terrain terrain,
            Vector3 planarCenterWorld,
            float discRadiusMeters,
            out Vector3 point,
            List<Vector3> occupied,
            float minDistance,
            float minimumGroundY,
            int maxSampleAttempts)
        {
            point = default;
            if (terrain == null || terrain.terrainData == null || discRadiusMeters <= 0f)
                return false;
            var td = terrain.terrainData;
            var origin = terrain.transform.position;
            var margin = Mathf.Min(30f, Mathf.Max(10f, Mathf.Min(td.size.x, td.size.z) * 0.02f));
            var cx = planarCenterWorld.x;
            var cz = planarCenterWorld.z;
            var rMax = Mathf.Max(0.01f, discRadiusMeters);
            var attempts = Mathf.Max(1, maxSampleAttempts);
            for (var i = 0; i < attempts; i++)
            {
                var u = Random.value;
                var v = Random.value;
                var r = rMax * Mathf.Sqrt(u);
                var theta = Mathf.PI * 2f * v;
                var x = cx + Mathf.Cos(theta) * r;
                var z = cz + Mathf.Sin(theta) * r;
                if (x < origin.x + margin || x > origin.x + td.size.x - margin
                    || z < origin.z + margin || z > origin.z + td.size.z - margin)
                    continue;
                var y = terrain.SampleHeight(new Vector3(x, origin.y, z)) + origin.y;
                var candidate = new Vector3(x, y, z);
                if (candidate.y < minimumGroundY)
                    continue;
                if (occupied != null && minDistance > 0f)
                {
                    var tooClose = false;
                    foreach (var p in occupied)
                    {
                        if ((p - candidate).sqrMagnitude < minDistance * minDistance)
                        {
                            tooClose = true;
                            break;
                        }
                    }

                    if (tooClose)
                        continue;
                }

                point = candidate;
                return true;
            }

            return false;
        }

        static List<GameObject> CollectIslandNpcs(GameObject playerGo, int maxCount)
        {
            var npcs = new List<GameObject>(maxCount);
            var seen = new HashSet<GameObject>();
            foreach (var binding in Object.FindObjectsByType<NpcDialogueBinding>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (binding == null || binding.gameObject == null)
                    continue;
                var go = binding.gameObject;
                if (go == playerGo || !seen.Add(go))
                    continue;
                npcs.Add(go);
                if (npcs.Count >= maxCount)
                    return npcs;
            }

            foreach (var cc in Object.FindObjectsByType<CharacterController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (cc == null || cc.gameObject == null)
                    continue;
                var go = cc.gameObject;
                if (go == playerGo || !seen.Add(go))
                    continue;
                npcs.Add(go);
                if (npcs.Count >= maxCount)
                    break;
            }

            return npcs;
        }

        static void ApplyTargetCharacterHeight(GameObject characterRoot, float targetHeightMeters)
        {
            if (characterRoot == null)
                return;
            targetHeightMeters *= 2f;
            var currentHeight = ComputeVisualHeight(characterRoot);
            if (currentHeight > 0.05f)
            {
                var scaleMultiplier = Mathf.Clamp(targetHeightMeters / currentHeight, 0.1f, 4f);
                characterRoot.transform.localScale *= scaleMultiplier;
            }

            var cc = characterRoot.GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.height = Mathf.Clamp(targetHeightMeters * 0.92f, 1.2f, 4.8f);
                cc.radius = Mathf.Clamp(cc.height * 0.18f, 0.2f, 0.75f);
                cc.center = new Vector3(0f, cc.height * 0.5f, 0f);
            }
        }

        IEnumerator RunAvatarSelectionIfNeeded()
        {
            _playerChosenFromLineup = false;
            if (playerCharacterPrefab != null)
            {
                _sessionSelectedPlayerPrefab = playerCharacterPrefab;
                _sidekickLineupPrefabs.Clear();
                yield break;
            }

            BootstrapStylizedCharacterResources.RefreshCache();
            BootstrapStylizedCharacterResources.AppendPrefabs(standaloneAvatarSelectionPrefabs);
            var choices = BootstrapStylizedCharacterResources.GetAllCharacterPrefabs();
            if (choices.Count == 0)
            {
                Debug.LogError(
                    "No player candidates found in StylizedCharacterPack prefabs.");
                _sidekickLineupPrefabs.Clear();
                yield break;
            }

            var lineup = BuildAvatarLineupPrefabs(choices, maxCount: 5);
            if (lineup.Count == 0)
            {
                Debug.LogError(
                    "No valid character prefabs to build a lineup.");
                _sidekickLineupPrefabs.Clear();
                yield break;
            }

            var host = new GameObject("PlayerCharacterSelectionHost");
            host.transform.SetParent(transform, false);
            var stage = host.AddComponent<PlayerCharacterSelectionStage>();
            stage.ConfigureVfx(playerSelectionMagicCirclePrefab, playerSelectionSparksPrefab, this);
            yield return StartCoroutine(stage.RunSelection(lineup));
            _sessionSelectedPlayerPrefab = stage.SelectedPrefab != null
                ? stage.SelectedPrefab
                : lineup[0];
            RebuildSidekickLineupPrefabs(lineup, _sessionSelectedPlayerPrefab);
            _playerChosenFromLineup = true;
            Destroy(host);
        }

        IEnumerator RunStartupTitleScreen()
        {
            if (MusicDirector.Instance != null)
                MusicDirector.Instance.PlayOpeningMusic();
            var host = new GameObject("StartupTitleScreenHost");
            host.transform.SetParent(transform, false);
            var stage = host.AddComponent<StartupTitleScreenStage>();
            yield return StartCoroutine(stage.Run(startupGameTitle, startupTitleImagePath));
            Destroy(host);
        }

        void EnsureMusicDirector()
        {
            var director = MusicDirector.Instance;
            if (director == null)
            {
                var go = new GameObject("MusicDirector");
                director = go.AddComponent<MusicDirector>();
            }
            director.Configure(musicFolderPath);
        }

        /// <summary>
        /// Standalone scenes often ship a Main Camera without an AudioListener; music/SFX then play inaudibly.
        /// </summary>
        void EnsureAudioListenerPresent()
        {
            var listeners = Object.FindObjectsByType<AudioListener>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            if (listeners != null && listeners.Length > 0)
                return;

            var mainCam = Camera.main;
            if (mainCam != null)
            {
                mainCam.gameObject.AddComponent<AudioListener>();
                return;
            }

            foreach (var cam in Object.FindObjectsByType<Camera>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None))
            {
                if (cam != null && cam.CompareTag("MainCamera"))
                {
                    cam.gameObject.AddComponent<AudioListener>();
                    return;
                }
            }

            if (MusicDirector.Instance != null)
                MusicDirector.Instance.gameObject.AddComponent<AudioListener>();
        }

        static List<GameObject> BuildAvatarLineupPrefabs(IReadOnlyList<GameObject> choices, int maxCount)
        {
            var lineup = new List<GameObject>(maxCount);
            if (choices == null)
                return lineup;
            for (var i = 0; i < choices.Count && lineup.Count < maxCount; i++)
            {
                if (choices[i] != null)
                    lineup.Add(choices[i]);
            }
            return lineup;
        }

        void RebuildSidekickLineupPrefabs(IReadOnlyList<GameObject> lineupChoices, GameObject selectedPrefab)
        {
            _sidekickLineupPrefabs.Clear();
            if (lineupChoices == null || selectedPrefab == null)
                return;
            foreach (var c in lineupChoices)
            {
                if (c == null || c == selectedPrefab)
                    continue;
                _sidekickLineupPrefabs.Add(c);
            }
        }

        void EnsurePlayerAuxiliaryRuntimeComponents()
        {
            var playerGo = GameObject.FindGameObjectWithTag(GameConstants.PlayerTag);
            if (playerGo == null)
                return;
            PackAnimalAnimatorDriver.TryAddForAnimalLocomotion(playerGo);
            EnsurePlayerIntroLightningAura(playerGo);
            if (playerGo.GetComponent<PlayerUnderwaterDeathController>() == null)
                playerGo.AddComponent<PlayerUnderwaterDeathController>();
            if (playerGo.GetComponent<PlayerTigerProximityDeathController>() == null)
                playerGo.AddComponent<PlayerTigerProximityDeathController>();
            if (playerGo.GetComponent<PlayerItemPickupInteractor>() == null)
                playerGo.AddComponent<PlayerItemPickupInteractor>();
            EnsurePlayerDefensiveSpellAttack(playerGo);
            if (playerGo.GetComponent<HeroHealth>() == null)
                playerGo.AddComponent<HeroHealth>();
            if (playerGo.GetComponent<HeroHunger>() == null)
                playerGo.AddComponent<HeroHunger>();
        }

        void EnsurePlayerIntroLightningAura(GameObject playerGo)
        {
            if (playerGo == null)
                return;
            if (playerGo.GetComponent<PlayerSpawnLightningAura>() != null)
                return;
            var resourceAura = playerIntroLightningAuraPrefab == null
                ? Resources.Load<GameObject>("Vfx/LightningAura")
                : null;
            if (playerIntroLightningAuraPrefab == null && resourceAura == null)
                return;
            var aura = playerGo.AddComponent<PlayerSpawnLightningAura>();
            if (playerIntroLightningAuraPrefab != null)
                aura.Configure(playerIntroLightningAuraPrefab);
        }

        void EnsurePlayerDefensiveSpellAttack(GameObject playerGo)
        {
            if (playerGo == null)
                return;
            var spell = playerGo.GetComponent<PlayerDefensiveSpellAttack>();
            if (spell == null)
                spell = playerGo.AddComponent<PlayerDefensiveSpellAttack>();
            spell.Configure(
                ResolveHeroDefensiveSpellVfxPrefab(),
                ResolveHeroDefensiveSpellLevel2VfxPrefab(),
                ResolveHeroDefensiveSpellLevel3VfxPrefab(),
                ResolveHeroDefensiveSpellLevel4VfxPrefab());
        }

        GameObject ResolveHeroDefensiveSpellVfxPrefab()
        {
            if (playerDefensiveSpellVfxPrefab != null)
                return playerDefensiveSpellVfxPrefab;
#if UNITY_EDITOR
            const string defaultPath = "Assets/Hovl Studio/Magic effects pack/Prefabs/AoE effects/Red energy explosion.prefab";
            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(defaultPath);
            if (loaded != null)
                return loaded;
#endif
            return null;
        }

        GameObject ResolveHeroDefensiveSpellLevel2VfxPrefab()
        {
            if (playerDefensiveSpellLevel2VfxPrefab != null)
                return playerDefensiveSpellLevel2VfxPrefab;
#if UNITY_EDITOR
            const string defaultPath = "Assets/Hovl Studio/Magic effects pack/Prefabs/AoE effects/Meteors AOE.prefab";
            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(defaultPath);
            if (loaded != null)
                return loaded;
#endif
            return null;
        }

        GameObject ResolveHeroDefensiveSpellLevel3VfxPrefab()
        {
            if (playerDefensiveSpellLevel3VfxPrefab != null)
                return playerDefensiveSpellLevel3VfxPrefab;
#if UNITY_EDITOR
            const string defaultPath = "Assets/Hovl Studio/Magic effects pack/Prefabs/AoE effects/Laser AOE.prefab";
            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(defaultPath);
            if (loaded != null)
                return loaded;
#endif
            return null;
        }

        GameObject ResolveHeroDefensiveSpellLevel4VfxPrefab()
        {
            if (playerDefensiveSpellLevel4VfxPrefab != null)
                return playerDefensiveSpellLevel4VfxPrefab;
#if UNITY_EDITOR
            const string defaultPath = "Assets/Hovl Studio/Magic effects pack/Prefabs/Sparks/Sparks explode green.prefab";
            var loaded = AssetDatabase.LoadAssetAtPath<GameObject>(defaultPath);
            if (loaded != null)
                return loaded;
#endif
            return null;
        }

        void EnsureSliceContentPresent()
        {
            var existing = transform.Find(ContentRootName);
            if (existing != null && !IsSliceComplete(existing))
            {
                // In edit mode, avoid deleting selected objects during inspector refresh.
                // This can trigger SerializedObjectNotCreatableException in TransformInspector.
                if (!Application.isPlaying)
                    return;
                // In authored scene mode, never destroy existing content at runtime just because the
                // bootstrap completeness heuristic doesn't match the scene hierarchy exactly.
                // This preserves manually placed NPCs and other hand-authored slice objects.
                if (useExistingSceneEnvironment)
                {
                    BuildLightingAndCamera(existing);
                    return;
                }
                DestroySliceObject(existing.gameObject);
            }

            existing = transform.Find(ContentRootName);
            if (existing != null)
            {
                // Even when slice already exists (edit-mode persisted), ensure a runtime gameplay camera/light.
                BuildLightingAndCamera(existing);
                return;
            }

            var root = new GameObject(ContentRootName).transform;
            root.SetParent(transform);
            root.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.localScale = Vector3.one;

            if (!useExistingSceneEnvironment)
            {
                BuildLightingAndCamera(root);
                BuildFloorAndNavMesh(root);
            }
            var playerPf = ResolvePlayerPrefabForNewSlice();
            var npcPfs = ResolveNpcPrefabsForNewSlice(playerPf, npcCount);
            BuildPlayer(
                root,
                playerPf,
                humanCharacterControllerHeight,
                humanCharacterControllerRadius,
                humanCharacterControllerCenterY,
                useExistingSceneEnvironment,
                playerSpawnAnchorName,
                playerSpawnAnchorName,
                humanToBuildingHeightRatio,
                anchorPlacementOffsetZ,
                anchorPlacementSeparationX,
                playerAnchorPlanarOffset,
                spawnFootClearance,
                stylizedCharacterScaleFactor);
            BuildNpcs(
                root,
                npcPfs,
                bootstrapNpcUniformScale,
                npcAnimatorReferenceSpeed,
                useExistingSceneEnvironment,
                playerSpawnAnchorName,
                humanToBuildingHeightRatio,
                npcSpawnRadiusFromBuilding,
                spawnFootClearance,
                stylizedCharacterScaleFactor);
            PlacePlayerNearRandomNpc(root, maxDistanceMeters: 20f, footClearance: spawnFootClearance);

            if (useExistingSceneEnvironment)
            {
                EnsureEnvironmentCollidersForWholeScene(root);
                EnsureFallbackGroundCollider(root);
            }

            if (!useExistingSceneEnvironment)
                RebakeNavMeshIfPossible();
        }

        static void PlacePlayerNearRandomNpc(Transform root, float maxDistanceMeters, float footClearance)
        {
            if (root == null)
                return;
            var player = FindDirectChild(root, "Player");
            if (player == null)
                return;

            var npcCandidates = new List<Transform>();
            for (var i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i);
                if (c == null || c == player)
                    continue;
                if (c.GetComponent<CharacterController>() == null)
                    continue;
                npcCandidates.Add(c);
            }
            if (npcCandidates.Count == 0)
                return;

            var npc = npcCandidates[Random.Range(0, npcCandidates.Count)];
            var basePos = npc.position;
            var angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            var distance = Random.Range(Mathf.Min(4f, maxDistanceMeters), Mathf.Max(4f, maxDistanceMeters));
            var offset = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
            var grounded = SnapToSceneGroundWorld(basePos + offset, basePos.y);
            player.position = grounded;
            LiftCharacterFeetAboveGround(player.gameObject, footClearance);
        }

        static bool IsCityPeopleSourcePrefab(GameObject prefab) =>
            prefab != null
            && BootstrapCityPeopleResources.IsKnownPrefab(prefab);

        static bool IsNpcCasualSourcePrefab(GameObject prefab) =>
            prefab != null
            && BootstrapCasualCharacterResources.IsKnownPrefab(prefab);

        static bool IsStylizedSourcePrefab(GameObject prefab) =>
            prefab != null
            && BootstrapStylizedCharacterResources.IsKnownPrefab(prefab);

        static bool UseHumanSizedCharacterController(GameObject prefab) =>
            prefab != null && IsCityPeopleSourcePrefab(prefab);

        static bool IsAnimalsFreeSourcePrefab(GameObject prefab) =>
            prefab != null && !IsCityPeopleSourcePrefab(prefab) && !IsStylizedSourcePrefab(prefab);

        GameObject ResolvePlayerPrefabForNewSlice()
        {
            if (_sessionSelectedPlayerPrefab != null)
                return _sessionSelectedPlayerPrefab;
            if (playerCharacterPrefab != null)
            {
                if (IsStylizedSourcePrefab(playerCharacterPrefab))
                    return playerCharacterPrefab;
                Debug.LogWarning(
                    $"Ignoring non-stylized player override '{playerCharacterPrefab.name}'. " +
                    "Use a prefab from StylizedCharacterPack.");
            }

            BootstrapStylizedCharacterResources.RefreshCache();
            var pool = BootstrapStylizedCharacterResources.GetAllCharacterPrefabs();
            if (pool.Count > 0)
                return pool[Random.Range(0, pool.Count)];
            Debug.LogError(
                "No StylizedCharacterPack player prefabs found.");
            return null;
        }

        List<GameObject> ResolveNpcPrefabsForNewSlice(GameObject playerPrefab, int desiredCount)
        {
            var result = new List<GameObject>(Mathf.Max(1, desiredCount));
            if (npcCharacterPrefab != null)
            {
                if (IsCityPeopleSourcePrefab(npcCharacterPrefab) || IsNpcCasualSourcePrefab(npcCharacterPrefab))
                {
                    result.Add(npcCharacterPrefab);
                    return result;
                }
                Debug.LogWarning(
                    $"Ignoring unsupported NPC override '{npcCharacterPrefab.name}'. " +
                    $"Use a prefab from Resources/{GameConstants.CityPeopleCharactersResourcesFolder} " +
                    $"or Resources/{GameConstants.NpcCasualCharactersResourcesFolder}.");
            }

            BootstrapCityPeopleResources.RefreshCache();
            var list = BootstrapCityPeopleResources.GetAllCharacterPrefabs();
            if (list.Count == 0)
            {
                BootstrapCasualCharacterResources.RefreshCache();
                var casual = BootstrapCasualCharacterResources.GetAllCharacterPrefabs();
                if (casual.Count == 0)
                {
                    Debug.LogError(
                        $"No NPC candidates found in Resources/{GameConstants.CityPeopleCharactersResourcesFolder} " +
                        $"or Resources/{GameConstants.NpcCasualCharactersResourcesFolder}.");
                    return result;
                }

                foreach (var p in casual)
                {
                    if (p != null)
                        result.Add(p);
                    if (result.Count >= desiredCount)
                        break;
                }

                if (result.Count < desiredCount)
                    Debug.LogWarning($"Requested {desiredCount} NPCs but only {result.Count} casual prefabs are available.");
                return result;
            }

            var pname = playerPrefab != null ? playerPrefab.name : string.Empty;
            var candidatePool = new List<GameObject>(list.Count);
            foreach (var go in list)
            {
                if (go == null)
                    continue;
                if (!string.IsNullOrEmpty(pname) && go.name == pname)
                    continue;
                candidatePool.Add(go);
            }
            var target = Mathf.Max(1, desiredCount);
            var cityToAdd = Mathf.Clamp(target - result.Count, 0, candidatePool.Count);
            for (var i = 0; i < cityToAdd; i++)
                result.Add(candidatePool[i]);
            if (result.Count < desiredCount)
                Debug.LogWarning($"Requested {desiredCount} NPCs but only {result.Count} unique CityPeople prefabs are available.");
            return result;
        }

        static bool IsSliceComplete(Transform root)
        {
            var floor = FindDirectChild(root, "Floor");
            var player = FindDirectChild(root, "Player");
            if (player == null)
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null)
                        continue;
                    if (string.Equals(t.name, "Player", StringComparison.Ordinal))
                    {
                        player = t;
                        break;
                    }
                }
            }
            var hasNpc = false;
            foreach (var cc in root.GetComponentsInChildren<CharacterController>(true))
            {
                if (cc == null || cc.transform == null)
                    continue;
                var t = cc.transform;
                if (player != null && (t == player || t.IsChildOf(player)))
                    continue;
                hasNpc = true;
                break;
            }
            var hasFloor = floor != null || Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude).Length > 0;
            return hasFloor && hasNpc && player != null && player.GetComponent<CharacterController>() != null;
        }

        static Transform FindDirectChild(Transform parent, string name)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name == name)
                    return c;
            }

            return null;
        }

        static Transform FindDirectChildIgnoreCase(Transform parent, string name)
        {
            if (parent == null || string.IsNullOrEmpty(name))
                return null;
            for (var i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c != null && string.Equals(c.gameObject.name, name, StringComparison.OrdinalIgnoreCase))
                    return c;
            }
            return null;
        }

        static void DestroySliceObject(GameObject go)
        {
            if (go == null)
                return;
            // In Play Mode, Object.Destroy is deferred to end-of-frame. EnsureSliceContentPresent runs in the
            // same OnEnable and would still Find() this object and return early, then the hierarchy is gone
            // with nothing rebuilt — e.g. "Display 1 No cameras rendering".
            Object.DestroyImmediate(go);
        }

        static void BuildLightingAndCamera(Transform root)
        {
            if (!HasMainGameplayCamera())
            {
                var camGo = new GameObject("Main Camera");
                camGo.transform.SetParent(root, false);
                camGo.tag = "MainCamera";
                camGo.AddComponent<AudioListener>();
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.Skybox;
                camGo.transform.SetPositionAndRotation(new Vector3(0f, 11f, -13f), Quaternion.Euler(28f, 0f, 0f));
            }

            if (Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude).Length == 0
                || !AnyDirectionalLight())
            {
                var lightGo = new GameObject("Directional Light");
                lightGo.transform.SetParent(root, false);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.05f;
                lightGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(50f, -35f, 0f));
            }
        }

        static bool AnyDirectionalLight()
        {
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
            {
                if (l.type == LightType.Directional)
                    return true;
            }

            return false;
        }

        static bool HasMainGameplayCamera()
        {
            if (Camera.main != null)
                return true;

            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
            {
                if (cam != null && cam.CompareTag("MainCamera"))
                    return true;
            }

            return false;
        }

        static void BuildFloorAndNavMesh(Transform root)
        {
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Floor";
            plane.transform.SetParent(root, false);
            plane.transform.localScale = new Vector3(4f, 1f, 4f);
            var gl = LayerMask.NameToLayer("Ground");
            if (gl >= 0)
                plane.layer = gl;

            var surface = plane.GetComponent<NavMeshSurface>();
            if (surface == null)
                surface = plane.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.BuildNavMesh();
        }

        void BuildPlayer(
            Transform root,
            GameObject characterPrefab,
            float humanCcHeight,
            float humanCcRadius,
            float humanCcCenterY,
            bool useExistingEnv,
            string buildingName,
            string spawnAnchorName,
            float humanToBuildingRatio,
            float anchorZOffset,
            float separationX,
            float anchorPlanarOffset,
            float footClearance,
            float stylizedScaleFactor)
        {
            GameObject player;
            if (characterPrefab != null)
            {
                player = Object.Instantiate(characterPrefab, root, false);
                player.name = "Player";
                if (IsAnimalsFreeSourcePrefab(characterPrefab))
                    ThirdPartyAnimalRig.StripForNavMeshAuthoring(player);
                ApplyStylizedPlayerScaleIfNeeded(player, characterPrefab, stylizedScaleFactor);
            }
            else
            {
                player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                player.name = "Player";
                player.transform.SetParent(root, false);
                DestroyBuiltInCollider(player);
            }

            player.tag = GameConstants.PlayerTag;
            if (characterPrefab != null && UseHumanSizedCharacterController(characterPrefab) && useExistingEnv)
                ScaleCharacterAgainstAnchorBuilding(player, buildingName, humanToBuildingRatio);
            var playerPos = ResolveAnchoredSpawnPosition(
                root,
                useExistingEnv,
                buildingName,
                spawnAnchorName,
                isNpc: false,
                zOffset: anchorZOffset,
                separationX: separationX,
                anchorPlanarOffset: anchorPlanarOffset);
            player.transform.position = playerPos;
            player.transform.rotation = Quaternion.identity;
            LiftCharacterFeetAboveGround(player, footClearance);

            ConfigurePlayerLocomotionAndInput(player, characterPrefab, humanCcHeight, humanCcRadius, humanCcCenterY, footClearance);
        }

        public float StylizedPlayerScaleMultiplier => stylizedCharacterScaleFactor;

        public void ApplyStylizedPlayerScaleIfNeeded(GameObject player, GameObject characterPrefab, float stylizedScaleFactor)
        {
            if (characterPrefab != null && IsStylizedSourcePrefab(characterPrefab))
                player.transform.localScale *= stylizedScaleFactor;
        }

        /// <summary>
        /// Replaces an existing Player after lineup selection: scale, feet lift, CC, input, stylized materials, intro aura.
        /// Call after <see cref="GameObject.transform"/> position/rotation are set.
        /// </summary>
        public void WirePlayerAfterCharacterSelection(GameObject newPlayer, GameObject chosenPrefab)
        {
            if (newPlayer == null || chosenPrefab == null)
                return;
            ApplyStylizedPlayerScaleIfNeeded(newPlayer, chosenPrefab, stylizedCharacterScaleFactor);
            if (!spawnHeroNearCastle)
                LiftCharacterFeetAboveGround(newPlayer, spawnFootClearance);
            ConfigurePlayerLocomotionAndInput(
                newPlayer,
                chosenPrefab,
                humanCharacterControllerHeight,
                humanCharacterControllerRadius,
                humanCharacterControllerCenterY,
                spawnFootClearance);
            EnsurePlayerIntroLightningAura(newPlayer);
            if (newPlayer.GetComponent<PlayerUnderwaterDeathController>() == null)
                newPlayer.AddComponent<PlayerUnderwaterDeathController>();
            if (newPlayer.GetComponent<PlayerTigerProximityDeathController>() == null)
                newPlayer.AddComponent<PlayerTigerProximityDeathController>();
            if (newPlayer.GetComponent<PlayerItemPickupInteractor>() == null)
                newPlayer.AddComponent<PlayerItemPickupInteractor>();
            if (newPlayer.GetComponent<HeroHealth>() == null)
                newPlayer.AddComponent<HeroHealth>();
            if (newPlayer.GetComponent<HeroHunger>() == null)
                newPlayer.AddComponent<HeroHunger>();
            if (useFixedHeroSpawnPosition)
                SetPlayerWorldPositionForSpawn(newPlayer, fixedHeroSpawnWorldPosition);
            else if (spawnHeroNearCastle)
                TrySpawnHeroNearCastle(newPlayer);
        }

        /// <summary>Character controller + player input/locomotion used by <see cref="BuildPlayer"/> and character selection confirm.</summary>
        public void ConfigurePlayerLocomotionAndInput(
            GameObject player,
            GameObject characterPrefab,
            float humanCcHeight,
            float humanCcRadius,
            float humanCcCenterY,
            float footClearance)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc == null)
                cc = player.AddComponent<CharacterController>();
            if (characterPrefab != null)
            {
                if (UseHumanSizedCharacterController(characterPrefab))
                {
                    cc.height = humanCcHeight;
                    cc.radius = humanCcRadius;
                    cc.center = new Vector3(0f, humanCcCenterY, 0f);
                }
                else if (IsStylizedSourcePrefab(characterPrefab))
                {
                    var visualHeight = Mathf.Max(1f, ComputeVisualHeight(player));
                    cc.height = Mathf.Clamp(visualHeight * 0.9f, 1.4f, 4.8f);
                    cc.radius = Mathf.Clamp(cc.height * 0.18f, 0.22f, 0.75f);
                    cc.center = new Vector3(0f, cc.height * 0.5f, 0f);
                }
                else
                {
                    cc.height = 1.15f;
                    cc.radius = 0.38f;
                    cc.center = new Vector3(0f, 0.58f, 0f);
                }
            }
            else
            {
                cc.height = 2f;
                cc.radius = 0.35f;
                cc.center = new Vector3(0f, 1f, 0f);
            }

            cc.stepOffset = 0.3f;
            cc.minMoveDistance = 0f;
            cc.skinWidth = 0.05f;
            LiftCharacterFeetAboveGround(player, Mathf.Max(footClearance, 0.12f));

            if (player.GetComponent<PlayerClickMove>() == null)
                player.AddComponent<PlayerClickMove>();
            if (player.GetComponent<PlayerInteractor>() == null)
                player.AddComponent<PlayerInteractor>();
            PackAnimalAnimatorDriver.TryAddForAnimalLocomotion(player);
            if (characterPrefab != null && IsCityPeopleSourcePrefab(characterPrefab))
                CityPeopleLocomotionDriver.TryAdd(player);
            else if (characterPrefab != null && IsStylizedSourcePrefab(characterPrefab))
            {
                StylizedNpcAnimatorDriver.TryAdd(player, forceIdle: false);
                EnsureStylizedMaterialsCompatible(player);
            }
        }

        static void BuildNpcs(
            Transform root,
            List<GameObject> characterPrefabs,
            float uniformScaleWhenPrefab,
            float npcAnimRefSpeed,
            bool useExistingEnv,
            string buildingName,
            float humanToBuildingRatio,
            float spawnRadiusFromBuildings,
            float footClearance,
            float stylizedScaleFactor)
        {
            if (characterPrefabs == null || characterPrefabs.Count == 0)
                return;
            var npcPositions = ResolveNpcSpawnPositions(root, useExistingEnv, buildingName, characterPrefabs.Count, spawnRadiusFromBuildings);
            for (var i = 0; i < characterPrefabs.Count; i++)
            {
                var pos = i < npcPositions.Count ? npcPositions[i] : SnapToSceneGround(root, new Vector3(3f + i * 1.5f, 0f, 3f), 0f);
                BuildNpcSingle(
                    root,
                    characterPrefabs[i],
                    $"NPC_Guide_{i + 1:00}",
                    i,
                    pos,
                    uniformScaleWhenPrefab,
                    npcAnimRefSpeed,
                    useExistingEnv,
                    buildingName,
                    humanToBuildingRatio,
                    footClearance,
                    stylizedScaleFactor,
                    skipFootLiftTrustWorldSpawn: false);
            }
        }

        static void BuildNpcSingle(
            Transform root,
            GameObject characterPrefab,
            string npcName,
            int npcIndex,
            Vector3 worldSpawn,
            float uniformScaleWhenPrefab,
            float npcAnimRefSpeed,
            bool useExistingEnv,
            string buildingName,
            float humanToBuildingRatio,
            float footClearance,
            float stylizedScaleFactor,
            bool skipFootLiftTrustWorldSpawn = false,
            string casualIdleClipNameOverride = null,
            string casualWalkClipNameOverride = null)
        {
            GameObject npcRoot;
            if (characterPrefab != null)
            {
                npcRoot = Object.Instantiate(characterPrefab, root, false);
                npcRoot.name = npcName;
                if (IsAnimalsFreeSourcePrefab(characterPrefab))
                    ThirdPartyAnimalRig.StripForNavMeshAuthoring(npcRoot);
                npcRoot.transform.localScale = IsCityPeopleSourcePrefab(characterPrefab)
                    ? Vector3.one
                    : Vector3.one * uniformScaleWhenPrefab;
                if (IsStylizedSourcePrefab(characterPrefab))
                    npcRoot.transform.localScale *= stylizedScaleFactor;
                if (UseHumanSizedCharacterController(characterPrefab) && useExistingEnv)
                    ScaleCharacterAgainstAnchorBuilding(npcRoot, buildingName, humanToBuildingRatio);
            }
            else
            {
                npcRoot = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                npcRoot.name = npcName;
                npcRoot.transform.SetParent(root, false);
            }

            npcRoot.transform.position = worldSpawn;
            npcRoot.transform.rotation = Quaternion.identity;
            if (!skipFootLiftTrustWorldSpawn)
                LiftCharacterFeetAboveGround(npcRoot, footClearance);

            if (npcRoot.GetComponent<NpcDialogueBinding>() == null)
                npcRoot.AddComponent<NpcDialogueBinding>();
            var binding = npcRoot.GetComponent<NpcDialogueBinding>();
            var def = Resources.Load<NpcDefinition>(GameConstants.DefaultNpcResource);
            if (def == null)
                Debug.LogError($"Missing Resources asset '{GameConstants.DefaultNpcResource}.asset'.");
            else
                binding.SetDefinition(CreateRuntimeNpcDefinition(def, characterPrefab != null ? characterPrefab.name : npcName, npcIndex));

            var trig = new GameObject("InteractTrigger");
            trig.transform.SetParent(npcRoot.transform, false);
            trig.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            var sphere = trig.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = DialogueInteractionDistanceMeters;
            if (trig.GetComponent<NpcInteractable>() == null)
                trig.AddComponent<NpcInteractable>();

            var ccNpc = npcRoot.GetComponent<CharacterController>();
            if (ccNpc == null)
                ccNpc = npcRoot.AddComponent<CharacterController>();
            if (characterPrefab != null)
            {
                if (UseHumanSizedCharacterController(characterPrefab))
                {
                    ccNpc.height = 1.85f;
                    ccNpc.radius = 0.28f;
                    ccNpc.center = new Vector3(0f, 0.92f, 0f);
                }
                else
                {
                    ccNpc.height = 1f;
                    ccNpc.radius = 0.32f;
                    ccNpc.center = new Vector3(0f, 0.5f, 0f);
                }
            }
            else
            {
                DestroyBuiltInCollider(npcRoot);
                ccNpc.height = 2f;
                ccNpc.radius = 0.35f;
                ccNpc.center = new Vector3(0f, 1f, 0f);
            }

            ccNpc.stepOffset = 0.15f;
            ccNpc.minMoveDistance = 0f;
            ccNpc.skinWidth = 0.05f;

            if (npcRoot.GetComponent<NpcAmbientDrift>() == null)
                npcRoot.AddComponent<NpcAmbientDrift>();
            var hasAnimalDriver = PackAnimalAnimatorDriver.TryAddForAnimalLocomotion(npcRoot);
            if (hasAnimalDriver && npcRoot.TryGetComponent<PackAnimalAnimatorDriver>(out var animDriver))
                animDriver.SetLocomotionReferenceSpeed(npcAnimRefSpeed, npcAnimRefSpeed * 0.92f);
            else if (IsCityPeopleSourcePrefab(characterPrefab))
                CityPeopleLocomotionDriver.TryAdd(npcRoot);
            else if (IsNpcCasualSourcePrefab(characterPrefab))
                NpcCasualLocomotionPlayableDriver.TryAddForCasualPrefab(
                    npcRoot,
                    characterPrefab != null ? characterPrefab.name : string.Empty,
                    casualIdleClipNameOverride,
                    casualWalkClipNameOverride);
            else if (IsStylizedSourcePrefab(characterPrefab))
            {
                StylizedNpcAnimatorDriver.TryAdd(npcRoot, forceIdle: true);
                EnsureStylizedMaterialsCompatible(npcRoot);
            }

            if (skipFootLiftTrustWorldSpawn)
                TeleportNpcRootToWorldPositionPreservingRotation(npcRoot, worldSpawn);
        }

        static void EnsureStylizedMaterialsCompatible(GameObject root)
        {
            if (root == null)
                return;
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            var standard = Shader.Find("Standard");
            var fallback = urpLit != null ? urpLit : standard;
            if (fallback == null)
                return;

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;
                Material[] mats;
                if (Application.isPlaying)
                    mats = renderer.materials;
                else
                    mats = renderer.sharedMaterials;
                for (var i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat == null)
                        continue;
                    var tex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap")
                        : mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex")
                        : null;
                    if (mat.shader != fallback)
                        mat.shader = fallback;
                    if (tex != null)
                    {
                        if (mat.HasProperty("_BaseMap"))
                            mat.SetTexture("_BaseMap", tex);
                        if (mat.HasProperty("_MainTex"))
                            mat.SetTexture("_MainTex", tex);
                    }
                    mats[i] = mat;
                }
                if (Application.isPlaying)
                    renderer.materials = mats;
                else
                    renderer.sharedMaterials = mats;
            }
        }

        static NpcDefinition CreateRuntimeNpcDefinition(NpcDefinition template, string sourceName, int npcIndex)
        {
            var def = Object.Instantiate(template);
            var displayName = PickNpcDisplayName(sourceName, npcIndex);
            var traits = GuessNpcTraits(sourceName, npcIndex);
            def.npcId = $"npc_{npcIndex + 1}_{displayName.ToLowerInvariant().Replace(" ", "_")}";
            def.displayName = displayName;
            def.roleSummary =
                $"{displayName} is a {traits.genderPresentation} {traits.occupation} with a {traits.personality} personality. " +
                $"Interests: {traits.interests}.";
            def.openingLine = $"Hey, I am {displayName}. I work as a {traits.occupation}. Want to chat about {traits.interests}?";
            def.fallbackLines = new[]
            {
                $"I am mostly focused on {traits.interests} today.",
                $"People say I am {traits.personality}. What do you think?"
            };
            return def;
        }

        static string PickNpcDisplayName(string sourceName, int npcIndex)
        {
            var names = new[]
            {
                "Mara", "Dorian", "Lucia", "Enzo", "Nadia",
                "Iris", "Rafael", "Selene", "Tomas", "Bianca"
            };
            var seed = Mathf.Abs((sourceName ?? "npc").GetHashCode() + npcIndex * 73);
            return names[seed % names.Length];
        }

        static (string personality, string genderPresentation, string occupation, string interests) GuessNpcTraits(string sourceName, int npcIndex)
        {
            var key = (sourceName ?? string.Empty).ToLowerInvariant();
            var personalities = new[] { "friendly", "stoic", "curious", "cheerful", "witty", "reserved" };
            var occupations = new[] { "merchant", "blacksmith", "courier", "gardener", "guard", "artisan" };
            var interests = new[] { "city gossip", "local trade", "crafting", "music", "exploration", "food" };
            var gender = key.Contains("female") || key.Contains("_f") ? "woman"
                : key.Contains("male") || key.Contains("_m") ? "man"
                : "person";
            var seed = Mathf.Abs(key.GetHashCode() + npcIndex * 127);
            return (
                personalities[seed % personalities.Length],
                gender,
                occupations[(seed / 3) % occupations.Length],
                interests[(seed / 5) % interests.Length]);
        }

        [ContextMenu("Rebuild Slice Content (Editor)")]
        void RebuildSliceContentInEditor()
        {
            var existing = transform.Find(ContentRootName);
            if (existing != null)
                DestroySliceObject(existing.gameObject);
            EnsureSliceContentPresent();
        }

        static void DestroyBuiltInCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(col);
            else
                Object.DestroyImmediate(col);
        }

        static Vector3 SnapToSceneGround(Transform root, Vector3 localDesired, float fallbackY)
        {
            var worldDesired = root.TransformPoint(localDesired);
            return SnapToSceneGroundWorld(worldDesired, fallbackY);
        }

        static Vector3 SnapToSceneGroundWorld(Vector3 worldDesired, float fallbackY)
        {
            var origin = new Vector3(worldDesired.x, worldDesired.y + 50f, worldDesired.z);
            if (Physics.Raycast(origin, Vector3.down, out var hit, 120f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point;
            worldDesired.y = fallbackY;
            return worldDesired;
        }

        static Vector3 ResolveAnchoredSpawnPosition(
            Transform root,
            bool useExistingEnv,
            string buildingName,
            string preferredAnchorName,
            bool isNpc,
            float zOffset,
            float separationX,
            float anchorPlanarOffset = 0f)
        {
            if (useExistingEnv && TryGetObjectBounds(preferredAnchorName, out var anchorBounds))
            {
                var offset = isNpc
                    ? new Vector3(separationX * 0.5f, 2f, 0f)
                    // Player should stand on the preferred anchor (e.g. top of hill), not in front of it.
                    : new Vector3(0f, 2f, 0f);
                return SnapToSceneGroundWorld(anchorBounds.center + offset, fallbackY: anchorBounds.min.y);
            }

            if (useExistingEnv && TryGetAnchorBounds(buildingName, out var b))
            {
                var x = b.center.x + (isNpc ? 0.5f : -0.5f) * separationX;
                var z = b.min.z - zOffset;
                return SnapToSceneGroundWorld(new Vector3(x, b.max.y + 2f, z), fallbackY: b.min.y);
            }

            return SnapToSceneGround(root, isNpc ? new Vector3(3f, 0f, 3f) : new Vector3(-3f, 0f, -3f), fallbackY: 0f);
        }

        static bool TryGetAnchorBounds(string buildingName, out Bounds bounds)
            => TryGetObjectBounds(buildingName, out bounds);

        static List<Vector3> ResolveNpcSpawnPositions(
            Transform root,
            bool useExistingEnv,
            string buildingName,
            int desiredCount,
            float spawnRadiusFromBuildings)
        {
            var positions = new List<Vector3>(desiredCount);
            if (!useExistingEnv)
                return positions;
            var buildings = CollectBuildingBounds(buildingName);
            for (var i = buildings.Count - 1; i > 0; i--)
            {
                var j = Random.Range(0, i + 1);
                (buildings[i], buildings[j]) = (buildings[j], buildings[i]);
            }

            foreach (var b in buildings)
            {
                if (positions.Count >= desiredCount)
                    break;
                var point = FindPointNearBuilding(b, spawnRadiusFromBuildings);
                if (point.HasValue)
                    positions.Add(point.Value);
            }

            return positions;
        }

        static List<Bounds> CollectBuildingBounds(string preferredName)
        {
            var list = new List<Bounds>(16);
            if (TryGetObjectBounds(preferredName, out var preferred))
                list.Add(preferred);
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (r == null || r.gameObject == null)
                    continue;
                var n = r.gameObject.name.ToLowerInvariant();
                if (!n.Contains("building"))
                    continue;
                list.Add(r.bounds);
            }
            return list;
        }

        static Vector3? FindPointNearBuilding(Bounds b, float radius)
        {
            for (var i = 0; i < 10; i++)
            {
                var angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                var outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                var baseRadius = Mathf.Max(b.extents.x, b.extents.z) + radius;
                var candidate = b.center + outward * baseRadius + Vector3.up * 3f;
                var grounded = SnapToSceneGroundWorld(candidate, b.min.y);
                if (!b.Contains(grounded) && IsSpawnSpaceFree(grounded, 0.45f))
                    return grounded;
            }
            return null;
        }

        static bool IsSpawnSpaceFree(Vector3 position, float radius)
        {
            var hits = Physics.OverlapSphere(position + Vector3.up * radius, radius, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit == null || hit.isTrigger)
                    continue;
                var n = hit.gameObject.name.ToLowerInvariant();
                if (n.Contains("ground") || n.Contains("terrain"))
                    continue;
                if (n.Contains("building") || n.Contains("wall"))
                    return false;
            }
            return true;
        }

        static bool TryGetObjectBounds(string objectName, out Bounds bounds)
        {
            bounds = default;
            if (string.IsNullOrWhiteSpace(objectName))
                return false;
            if (TryFindSceneObjectByName(objectName, out var go) && TryGetBoundsFromGameObject(go, out bounds))
                return true;
            var hit = false;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (r == null || r.gameObject == null)
                    continue;
                if (r.gameObject.name.IndexOf(objectName, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (!hit)
                {
                    bounds = r.bounds;
                    hit = true;
                }
                else
                    bounds.Encapsulate(r.bounds);
            }

            return hit;
        }

        static bool TryFindSceneObjectByName(string objectName, out GameObject found)
        {
            found = null;
            if (string.IsNullOrWhiteSpace(objectName))
                return false;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (t == null || t.gameObject == null)
                    continue;
                if (t.gameObject.name.IndexOf(objectName, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                found = t.gameObject;
                return true;
            }
            return false;
        }

        static bool TryGetBoundsFromGameObject(GameObject go, out Bounds bounds)
        {
            bounds = default;
            if (go == null)
                return false;
            var has = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled)
                    continue;
                if (!has)
                {
                    bounds = r.bounds;
                    has = true;
                }
                else
                    bounds.Encapsulate(r.bounds);
            }
            if (has)
                return true;
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
            {
                if (c == null || !c.enabled)
                    continue;
                if (!has)
                {
                    bounds = c.bounds;
                    has = true;
                }
                else
                    bounds.Encapsulate(c.bounds);
            }
            return has;
        }

        static void ScaleCharacterAgainstAnchorBuilding(GameObject characterRoot, string buildingName, float targetRatio)
        {
            if (characterRoot == null || targetRatio <= 0f)
                return;
            if (!TryGetAnchorBounds(buildingName, out var b))
                return;
            var buildingHeight = Mathf.Max(0.1f, b.size.y);
            var targetHeight = buildingHeight * targetRatio;
            var currentHeight = ComputeVisualHeight(characterRoot);
            if (currentHeight <= 0.01f)
                return;
            var scale = Mathf.Clamp(targetHeight / currentHeight, 0.05f, 20f);
            characterRoot.transform.localScale = Vector3.one * scale;
        }

        static float ComputeVisualHeight(GameObject root)
        {
            if (root == null)
                return 0f;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var has = false;
            var b = default(Bounds);
            foreach (var r in renderers)
            {
                if (r == null)
                    continue;
                if (!has)
                {
                    b = r.bounds;
                    has = true;
                }
                else
                    b.Encapsulate(r.bounds);
            }

            return has ? b.size.y : 0f;
        }

        public static void LiftCharacterFeetAboveGround(GameObject characterRoot, float footClearance)
        {
            if (characterRoot == null)
                return;
            if (!TryGetRenderBounds(characterRoot, out var rb))
                return;
            var groundProbe = new Vector3(rb.center.x, rb.max.y + 1.5f, rb.center.z);
            if (!TryGetGroundHitForCharacter(characterRoot, groundProbe, 12f, out var hit))
                return;
            var deltaY = (hit.point.y + footClearance) - rb.min.y;
            if (Mathf.Abs(deltaY) < 0.0001f)
                return;
            characterRoot.transform.position += Vector3.up * deltaY;
        }

        static bool TryGetGroundHitForCharacter(GameObject characterRoot, Vector3 origin, float maxDistance, out RaycastHit bestHit)
        {
            bestHit = default;
            if (characterRoot == null)
                return false;
            var hits = Physics.RaycastAll(origin, Vector3.down, maxDistance, ~0, QueryTriggerInteraction.Ignore);
            var found = false;
            var bestDistance = float.MaxValue;
            foreach (var hit in hits)
            {
                var c = hit.collider;
                if (c == null || c.transform == null)
                    continue;
                if (c.transform.IsChildOf(characterRoot.transform))
                    continue;
                var n = c.gameObject.name.ToLowerInvariant();
                if (n.Contains("grass") || n.Contains("foliage") || n.Contains("leaf")
                    || n.Contains("flower") || n.Contains("bush") || n.Contains("plant")
                    || n.Contains("water") || n.Contains("ocean") || n.Contains("river"))
                    continue;
                if (hit.distance >= bestDistance)
                    continue;
                bestDistance = hit.distance;
                bestHit = hit;
                found = true;
            }

            return found;
        }

        static bool TryGetRenderBounds(GameObject root, out Bounds bounds)
        {
            bounds = default;
            if (root == null)
                return false;
            var has = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled)
                    continue;
                if (!has)
                {
                    bounds = r.bounds;
                    has = true;
                }
                else
                    bounds.Encapsulate(r.bounds);
            }

            return has;
        }

        static void EnsureFallbackGroundCollider(Transform root)
        {
            var player = FindDirectChild(root, "Player");
            var npcs = root.GetComponentsInChildren<CharacterController>(true);
            var npcAny = false;
            var npcPos = Vector3.zero;
            foreach (var cc in npcs)
            {
                if (cc == null || cc.gameObject == player?.gameObject)
                    continue;
                npcAny = true;
                npcPos = cc.transform.position;
                if (!HasGroundUnder(cc.transform.position))
                    break;
            }
            if (player == null && !npcAny)
                return;
            var needGround = false;
            if (player != null && !HasGroundUnder(player.position))
                needGround = true;
            if (npcAny && !HasGroundUnder(npcPos))
                needGround = true;
            if (!needGround)
                return;

            var existing = FindDirectChild(root, "_BootstrapGround");
            if (existing != null)
                return;

            var p = player != null ? player.position : npcPos;
            var n = npcAny ? npcPos : player.position;
            var ground = new GameObject("_BootstrapGround");
            ground.transform.SetParent(root, false);
            ground.transform.position = new Vector3((p.x + n.x) * 0.5f, Mathf.Min(p.y, n.y) - 0.75f, (p.z + n.z) * 0.5f);
            var bc = ground.AddComponent<BoxCollider>();
            bc.size = new Vector3(80f, 1.5f, 80f);
        }

        static bool HasGroundUnder(Vector3 worldPos)
        {
            var origin = worldPos + Vector3.up * 2f;
            return Physics.Raycast(origin, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
        }

        static void EnsureEnvironmentCollidersForWholeScene(Transform root)
        {
            foreach (var terrain in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude))
            {
                if (terrain == null)
                    continue;
                if (terrain.GetComponent<TerrainCollider>() == null)
                    terrain.gameObject.AddComponent<TerrainCollider>();
            }

            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude))
            {
                if (mf == null || mf.sharedMesh == null || mf.transform == null)
                    continue;
                var go = mf.gameObject;
                if (!go.activeInHierarchy)
                    continue;
                if (go.transform.IsChildOf(root))
                    continue;
                if (go.GetComponent<Collider>() != null)
                    continue;
                // Avoid adding colliders to non-environment utility meshes.
                var lname = go.name ?? string.Empty;
                if (!(lname.IndexOf("rpgpp_lt_", StringComparison.OrdinalIgnoreCase) >= 0
                      || lname.IndexOf("terrain", StringComparison.OrdinalIgnoreCase) >= 0
                      || lname.IndexOf("ground", StringComparison.OrdinalIgnoreCase) >= 0
                      || lname.IndexOf("building", StringComparison.OrdinalIgnoreCase) >= 0
                      || lname.IndexOf("well", StringComparison.OrdinalIgnoreCase) >= 0
                      || lname.IndexOf("wagon", StringComparison.OrdinalIgnoreCase) >= 0
                      || lname.IndexOf("fence", StringComparison.OrdinalIgnoreCase) >= 0
                      || lname.IndexOf("veg", StringComparison.OrdinalIgnoreCase) >= 0
                      || lname.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0
                      || lname.IndexOf("bush", StringComparison.OrdinalIgnoreCase) >= 0
                      || lname.IndexOf("plant", StringComparison.OrdinalIgnoreCase) >= 0
                      || lname.IndexOf("rock", StringComparison.OrdinalIgnoreCase) >= 0
                      || lname.IndexOf("grass", StringComparison.OrdinalIgnoreCase) >= 0
                      || lname.IndexOf("foliage", StringComparison.OrdinalIgnoreCase) >= 0
                      || lname.IndexOf("leaf", StringComparison.OrdinalIgnoreCase) >= 0
                      || lname.IndexOf("hedge", StringComparison.OrdinalIgnoreCase) >= 0))
                    continue;
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;
            }
        }

        static void EnsureSliceFollowCameraWired()
        {
            var playerGo = GameObject.FindGameObjectWithTag(GameConstants.PlayerTag);
            if (playerGo == null)
                return;
            var cam = Camera.main;
            if (cam == null)
                return;
            var f = cam.GetComponent<SliceFollowCamera>();
            if (f == null)
                f = cam.gameObject.AddComponent<SliceFollowCamera>();
            if (cam.GetComponent<PlayerVisibilityRange>() == null)
                cam.gameObject.AddComponent<PlayerVisibilityRange>();
            f.SetTarget(playerGo.transform);
        }

        void RebakeNavMeshIfPossible()
        {
            var content = transform.Find(ContentRootName);
            if (content == null)
                return;
            var floor = content.Find("Floor");
            if (floor == null)
                return;
            var surface = floor.GetComponent<NavMeshSurface>();
            surface?.BuildNavMesh();
        }

        static void EnsureIslandEscapePortal()
        {
            if (!Application.isPlaying)
                return;
            const string portalName = "Portal green";
            var go = GameObject.Find(portalName);
            if (go == null)
            {
                go = new GameObject(portalName);
                go.transform.position = new Vector3(4605.223f, -2465.77f, 2967.15f);
            }

            if (go.GetComponent<Rigidbody>() == null)
            {
                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            if (!go.TryGetComponent<BoxCollider>(out var box))
                box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            if (box.size.sqrMagnitude < 10f)
                box.size = new Vector3(8f, 10f, 8f);

            if (go.GetComponent<IslandEscapePortalTrigger>() == null)
                go.AddComponent<IslandEscapePortalTrigger>();
        }

        void BuildManagersAndUi()
        {
            if (GameObject.Find("Managers") != null)
                return;

            var managers = new GameObject("Managers");
            var world = managers.AddComponent<WorldStateService>();
            var dialogue = managers.AddComponent<DialogueManager>();

            var uiGo = new GameObject("UICanvas");
            var ui = uiGo.AddComponent<DialogueUIController>();
            uiGo.AddComponent<GameOverController>();
            uiGo.AddComponent<HeroHealthBarHud>();

            var policy = Resources.Load<DialoguePolicy>("DefaultDialoguePolicy");
            dialogue.Wire(world, ui, policy);

            var ollamaAsset = Resources.Load<OllamaSettings>(GameConstants.DefaultOllamaSettingsResource);
            if (ollamaAsset == null)
            {
                Debug.LogError(
                    $"Missing Resources asset '{GameConstants.DefaultOllamaSettingsResource}.asset'. Dialogue will not function until it exists.");
                return;
            }

            OllamaStartupSelection.EnsureDefaultsIfTitleSkipped(ollamaAsset);
            var ollamaRuntime = Instantiate(ollamaAsset);
            ollamaRuntime.name = ollamaAsset.name + "_Runtime";
            OllamaStartupSelection.ApplyToRuntimeClone(ollamaRuntime);
            dialogue.ConfigureRuntime(ollamaRuntime, null);
            var npcIds = CollectNpcIdsForNarrativeGeneration();
            _ = dialogue.GenerateNarrativeCanonAsync(dialogue.RuntimeGenerationSeed, npcIds);
        }

        void EnsureGameplayIntroOverlay()
        {
            if (!Application.isPlaying)
                return;
            var existing = Object.FindFirstObjectByType<GameplayIntroOverlay>();
            var overlay = existing != null ? existing : new GameObject("GameplayIntroOverlay").AddComponent<GameplayIntroOverlay>();
            overlay.PlayForCurrentRun();
        }

        static List<string> CollectNpcIdsForNarrativeGeneration()
        {
            var ids = new List<string>();
            foreach (var b in FindObjectsByType<NpcDialogueBinding>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (b == null || b.Definition == null || string.IsNullOrWhiteSpace(b.Definition.npcId))
                    continue;
                if (!ids.Contains(b.Definition.npcId))
                    ids.Add(b.Definition.npcId);
            }

            return ids;
        }

        [ContextMenu("Generate Narrative Bundle (Offline)")]
        void GenerateNarrativeBundleOffline()
        {
            var settings = Resources.Load<OllamaSettings>(GameConstants.DefaultOllamaSettingsResource);
            var client = settings != null ? new OllamaClient(settings) : null;
            var service = new NarrativeGenerationService(
                new NarrativeContentLibrary(),
                client,
                settings,
                new NarrativeSessionStore());
            var ids = CollectNpcIdsForNarrativeGeneration();
            var seed = DateTime.UtcNow.ToString("o").GetHashCode();
            // Offline context path: produce a deterministic fallback immediately, then try LLM upgrade if possible.
            var fallback = service.BuildFallback(seed, ids);
            new NarrativeSessionStore().Save(fallback);
            if (settings != null)
                _ = service.GenerateOrFallbackAsync(seed, ids, default);
            Debug.Log("[RuntimeLevelBootstrap] Narrative bundle generation requested.");
        }
    }
}
