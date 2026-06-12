using System.Collections;
using System.Collections.Generic;
using Rpg.Audio;
using Rpg.Dialogue;
using Rpg.Npc;
using Rpg.UI;
using UnityEngine;

namespace Rpg.Player
{
    /// <summary>
    /// Press K to cast an AoE spell around the hero.
    /// Spell level is unlocked by books (and sidekicks for level 4), then applies matching radius and VFX.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public sealed class PlayerDefensiveSpellAttack : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] float level1KillRadiusMeters = 5f;
        [SerializeField, Min(0.5f)] float level2KillRadiusMeters = 10f;
        [SerializeField, Min(0.5f)] float level3KillRadiusMeters = 15f;
        [SerializeField, Min(0.5f)] float level4KillRadiusMeters = 15f;
        [SerializeField, Min(0.05f)] float fadeToBlackSeconds = 0.5f;
        [SerializeField, Min(0.05f)] float spellCooldownSeconds = 10f;
        [SerializeField] GameObject level1SpellVfxPrefabOverride;
        [SerializeField] GameObject level2SpellVfxPrefabOverride;
        [SerializeField] GameObject level3SpellVfxPrefabOverride;
        [SerializeField] GameObject level4SpellVfxPrefabOverride;
        [SerializeField] Vector3 spellVfxLocalOffset = new Vector3(0f, 0.8f, 0f);
        [SerializeField] Vector3 spellVfxLocalScale = new Vector3(0.5f, 0.5f, 0.5f);
        [SerializeField, Min(0.1f)] float spellVfxLifetimeSeconds = 3f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        float _nextCastTime;
        InventoryService _inventory;
        AudioSource _magicSfxSource;
        AudioClip _magicCastSfx;

        struct FadeEntry
        {
            public Material Mat;
            public int PropId;
            public Color Orig;
        }

        public void Configure(GameObject level1VfxPrefab, GameObject level2VfxPrefab, GameObject level3VfxPrefab, GameObject level4VfxPrefab)
        {
            level1SpellVfxPrefabOverride = level1VfxPrefab;
            level2SpellVfxPrefabOverride = level2VfxPrefab;
            level3SpellVfxPrefabOverride = level3VfxPrefab;
            level4SpellVfxPrefabOverride = level4VfxPrefab;
        }

        void Awake()
        {
            _inventory = new InventoryService(new NarrativeContentLibrary());
            _inventory.EnsureActor(InventoryService.HeroActorId);
            _magicSfxSource = gameObject.AddComponent<AudioSource>();
            _magicSfxSource.playOnAwake = false;
            _magicSfxSource.spatialBlend = 0f;
            _magicCastSfx = RuntimeAudioClipLoader.LoadFromAssetPath("Assets/FREE SOUND PACK_TM(355)/Creatures(29)/Spectral 05.wav");
        }

        void Update()
        {
            if (GameOverController.Instance != null && GameOverController.Instance.IsGameOver)
                return;
            if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueOpen)
                return;
            if (Time.time < _nextCastTime)
                return;
            if (!Input.GetKeyDown(KeyCode.K))
                return;

            RefreshInventorySnapshot();
            var level = ResolveSpellLevel();
            if (level <= 0)
                return;

            _nextCastTime = Time.time + spellCooldownSeconds;
            CastSpell(level);
        }

        void CastSpell(int level)
        {
            if (level <= 0)
                return;
            if (_magicSfxSource != null && _magicCastSfx != null)
                _magicSfxSource.PlayOneShot(_magicCastSfx);
            SpawnSpellVfx(level);
            var radius = ResolveKillRadius(level);
            var hitSet = new HashSet<GameObject>();
            AddTargets(hitSet, Object.FindObjectsByType<TigerNpcWanderAi>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            AddTargets(hitSet, Object.FindObjectsByType<SpiderNpcWanderAi>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            if (level >= 4)
                AddTargets(hitSet, Object.FindObjectsByType<BossAi>(FindObjectsInactive.Exclude, FindObjectsSortMode.None));
            foreach (var go in hitSet)
            {
                if (go == null)
                    continue;
                var d = go.transform.position - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > radius * radius)
                    continue;
                StartCoroutine(FadeAndDestroyTarget(go));
            }
        }

        float ResolveKillRadius(int level)
        {
            switch (level)
            {
                case 4: return level4KillRadiusMeters;
                case 3: return level3KillRadiusMeters;
                case 2: return level2KillRadiusMeters;
                default: return level1KillRadiusMeters;
            }
        }

        void SpawnSpellVfx(int level)
        {
            var prefab = ResolveVfxPrefabForLevel(level);
            if (prefab == null)
                return;
            var vfx = Instantiate(prefab, transform, false);
            vfx.name = prefab.name + "_HeroSpell";
            vfx.transform.localPosition = spellVfxLocalOffset;
            vfx.transform.localRotation = Quaternion.identity;
            vfx.transform.localScale = spellVfxLocalScale;
            foreach (var ps in vfx.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps != null && !ps.isPlaying)
                    ps.Play(true);
            }
            Destroy(vfx, spellVfxLifetimeSeconds);
        }

        GameObject ResolveVfxPrefabForLevel(int level)
        {
            switch (level)
            {
                case 4:
                    return level4SpellVfxPrefabOverride != null ? level4SpellVfxPrefabOverride : level3SpellVfxPrefabOverride;
                case 3:
                    return level3SpellVfxPrefabOverride != null ? level3SpellVfxPrefabOverride : level1SpellVfxPrefabOverride;
                case 2:
                    return level2SpellVfxPrefabOverride != null ? level2SpellVfxPrefabOverride : level1SpellVfxPrefabOverride;
                default:
                    return level1SpellVfxPrefabOverride;
            }
        }

        int ResolveSpellLevel()
        {
            if (_inventory == null)
                return 0;
            var books = CountHeroBooks();
            if (books <= 0)
                return 0;
            if (books >= 3 && CountFollowingSidekicks() >= 2)
                return 4;
            return Mathf.Clamp(books, 1, 3);
        }

        void RefreshInventorySnapshot()
        {
            _inventory = new InventoryService(new NarrativeContentLibrary());
            _inventory.EnsureActor(InventoryService.HeroActorId);
        }

        int CountHeroBooks()
        {
            var total = 0;
            var rows = _inventory.GetInventoryView(InventoryService.HeroActorId);
            if (rows == null || rows.Count == 0)
                return 0;
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null || string.IsNullOrWhiteSpace(row.itemId) || row.quantity <= 0)
                    continue;
                var id = row.itemId.Trim();
                if (!id.Contains("book", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                total += row.quantity;
                if (total >= 3)
                    return 3;
            }

            return Mathf.Clamp(total, 0, 3);
        }

        static int CountFollowingSidekicks()
        {
            var count = 0;
            var followers = Object.FindObjectsByType<SidekickFollowHeroController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < followers.Length; i++)
            {
                var f = followers[i];
                if (f == null || !f.IsFollowing)
                    continue;
                count++;
            }

            return count;
        }

        IEnumerator FadeAndDestroyTarget(GameObject target)
        {
            if (target == null)
                yield break;

            DisableThreatControllers(target);
            var fades = BuildFadeEntries(target);
            var dur = Mathf.Max(0.05f, fadeToBlackSeconds);
            var t0 = Time.time;
            while (Time.time - t0 < dur)
            {
                if (target == null)
                    yield break;
                var vis = 1f - Mathf.Clamp01((Time.time - t0) / dur);
                ApplyFade(fades, vis);
                yield return null;
            }

            ApplyFade(fades, 0f);
            if (target != null)
            {
                if (target.GetComponent<BossAi>() != null && MusicDirector.Instance != null)
                    MusicDirector.Instance.OnGhoulKilled();
                Destroy(target);
            }
        }

        static void DisableThreatControllers(GameObject target)
        {
            if (target.TryGetComponent<TigerNpcWanderAi>(out var tiger))
                tiger.enabled = false;
            if (target.TryGetComponent<SpiderNpcWanderAi>(out var spider))
                spider.enabled = false;
            if (target.TryGetComponent<BossAi>(out var boss))
                boss.enabled = false;
            if (target.TryGetComponent<NpcAmbientDrift>(out var drift))
                drift.enabled = false;
            if (target.TryGetComponent<CharacterController>(out var cc))
                cc.enabled = false;
        }

        static List<FadeEntry> BuildFadeEntries(GameObject target)
        {
            var list = new List<FadeEntry>(32);
            foreach (var r in target.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r is ParticleSystemRenderer)
                    continue;
                var mats = r.materials;
                for (var i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null)
                        continue;
                    if (m.HasProperty(BaseColorId))
                    {
                        list.Add(new FadeEntry { Mat = m, PropId = BaseColorId, Orig = m.GetColor(BaseColorId) });
                    }
                    else if (m.HasProperty(ColorId))
                    {
                        list.Add(new FadeEntry { Mat = m, PropId = ColorId, Orig = m.GetColor(ColorId) });
                    }
                }
            }

            return list;
        }

        static void ApplyFade(List<FadeEntry> fades, float visibility01)
        {
            var vis = Mathf.Clamp01(visibility01);
            for (var i = 0; i < fades.Count; i++)
            {
                var e = fades[i];
                if (e.Mat == null)
                    continue;
                e.Mat.SetColor(e.PropId, Color.Lerp(Color.black, e.Orig, vis));
            }
        }

        static void AddTargets<T>(HashSet<GameObject> set, T[] arr) where T : Component
        {
            if (arr == null)
                return;
            for (var i = 0; i < arr.Length; i++)
            {
                var c = arr[i];
                if (c == null || c.gameObject == null)
                    continue;
                set.Add(c.gameObject);
            }
        }
    }
}
