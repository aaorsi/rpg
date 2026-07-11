using System;
using Rpg.Core;
using Rpg.Dialogue;
using Rpg.Npc;
using Rpg.UI;
using UnityEngine;

namespace Rpg.Player
{
    /// <summary>
    /// Handles left-click pickup: explicit <see cref="ItemPickup"/> components, or scene meshes whose names match
    /// the object catalog (e.g. <c>BottleV1</c>, <c>TableV2</c>) via <see cref="ScenePickableCatalogResolver"/>.
    /// Uses <see cref="DialogueManager"/> inventory so pickups match the I-panel and dialogue economy.
    /// Uses <see cref="Physics.RaycastAll"/> so small colliders are not skipped when terrain or other geometry is in front.
    /// </summary>
    [DefaultExecutionOrder(9)]
    public sealed class PlayerItemPickupInteractor : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] float pickupRayDistance = 200f;
        [SerializeField] LayerMask pickupMask = ~0;

        [SerializeField]
        [Tooltip("Live chickens can only be collected within this horizontal distance (meters).")]
        float liveChickenMaxPickupPlanarMeters = 8f;

        Camera _camera;
        static int _leftClickConsumedFrame = -1;
        static ScenePickableCatalogResolver _catalogResolver;

        public static bool IsLeftClickConsumedThisFrame => _leftClickConsumedFrame == Time.frameCount;

        void Awake()
        {
            _camera = Camera.main;
        }

        public bool TryHandleLeftClickPickup()
        {
            if (!Input.GetMouseButtonDown(0))
                return false;
            if (GameOverController.Instance != null && GameOverController.Instance.IsGameOver)
                return false;
            if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueOpen)
                return false;
            if (_camera == null)
                _camera = Camera.main;
            if (_camera == null)
                return false;

            var ray = _camera.ScreenPointToRay(Input.mousePosition);
            var hits = Physics.RaycastAll(ray, pickupRayDistance, pickupMask, QueryTriggerInteraction.Collide);
            if (hits == null || hits.Length == 0)
                return false;
            Array.Sort(hits, ComparePickupRaycastHits);

            var dm = DialogueManager.Instance;
            var inv = dm != null ? dm.Inventory : null;
            if (inv == null)
                return false;

            for (var i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (hit.collider == null)
                    continue;
                var pickup = hit.collider.GetComponentInParent<ItemPickup>();
                if (pickup != null && !pickup.IsConsumed && !string.IsNullOrWhiteSpace(pickup.ItemId))
                {
                    if (!inv.IsKnownItem(pickup.ItemId))
                        continue;
                    if (IsForbiddenPickupHost(pickup))
                        continue;

                    if (string.Equals(pickup.ItemId, GameConstants.LiveChickenItemId, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!IsWithinPlanarDistance(transform.position, pickup.transform.position, liveChickenMaxPickupPlanarMeters))
                            continue;
                    }

                    if (TryCommitPickup(pickup.ItemId, pickup, inv, dm))
                        return true;
                    continue;
                }

                if (!TryResolveImplicitPickable(hit.collider.transform, out var implicitId, out var pickupRoot))
                    continue;
                if (!inv.IsKnownItem(implicitId))
                    continue;
                if (IsForbiddenImplicitWorldPickable(pickupRoot, implicitId))
                    continue;
                if (string.Equals(implicitId, GameConstants.LiveChickenItemId, StringComparison.OrdinalIgnoreCase))
                {
                    if (!IsWithinPlanarDistance(transform.position, pickupRoot.position, liveChickenMaxPickupPlanarMeters))
                        continue;
                }

                if (TryCommitPickup(implicitId, pickupRoot.gameObject, inv, dm))
                    return true;
            }

            return false;
        }

        static int ComparePickupRaycastHits(RaycastHit a, RaycastHit b)
        {
            var aPickup = a.collider != null ? a.collider.GetComponentInParent<ItemPickup>() : null;
            var bPickup = b.collider != null ? b.collider.GetComponentInParent<ItemPickup>() : null;
            var aChicken = IsLiveChickenPickup(aPickup);
            var bChicken = IsLiveChickenPickup(bPickup);
            if (aChicken != bChicken)
                return aChicken ? -1 : 1;
            if (aPickup != null && bPickup == null)
                return -1;
            if (aPickup == null && bPickup != null)
                return 1;
            return a.distance.CompareTo(b.distance);
        }

        static bool IsLiveChickenPickup(ItemPickup pickup) =>
            pickup != null
            && !pickup.IsConsumed
            && string.Equals(pickup.ItemId, GameConstants.LiveChickenItemId, StringComparison.OrdinalIgnoreCase);

        static ScenePickableCatalogResolver CatalogResolver =>
            _catalogResolver ?? (_catalogResolver = new ScenePickableCatalogResolver());

        static bool TryResolveImplicitPickable(Transform hitTransform, out string itemId, out Transform pickupRoot)
            => CatalogResolver.TryResolvePickableRoot(hitTransform, out itemId, out pickupRoot);

        bool TryCommitPickup(string itemId, ItemPickup pickup, InventoryService inv, DialogueManager dm)
        {
            var booksBefore = CountHeroBooks(inv);
            if (!inv.TryAddItem(InventoryService.HeroActorId, itemId, 1))
            {
                if (inv.IsHeroInventoryFullForDistinctPickup(itemId))
                    dm?.ShowHudMessage("inventory full");
                _leftClickConsumedFrame = Time.frameCount;
                return true;
            }

            if (pickup != null)
            {
                if (!pickup.TryConsume())
                    return false;
                var booksAfter = CountHeroBooks(inv);
                if (booksAfter > booksBefore && booksAfter >= 1 && booksAfter <= 3)
                    dm?.GenerateHeroBookDiscoveryNarration(booksAfter, itemId);
                dm?.NotifyHeroWorldInventoryChanged();
                _leftClickConsumedFrame = Time.frameCount;
                Destroy(pickup.gameObject);
                return true;
            }

            return false;
        }

        bool TryCommitPickup(string itemId, GameObject worldObject, InventoryService inv, DialogueManager dm)
        {
            var booksBefore = CountHeroBooks(inv);
            if (!inv.TryAddItem(InventoryService.HeroActorId, itemId, 1))
            {
                if (inv.IsHeroInventoryFullForDistinctPickup(itemId))
                    dm?.ShowHudMessage("inventory full");
                _leftClickConsumedFrame = Time.frameCount;
                return true;
            }

            var booksAfter = CountHeroBooks(inv);
            if (booksAfter > booksBefore && booksAfter >= 1 && booksAfter <= 3)
                dm?.GenerateHeroBookDiscoveryNarration(booksAfter, itemId);
            dm?.NotifyHeroWorldInventoryChanged();
            _leftClickConsumedFrame = Time.frameCount;
            if (worldObject != null)
                Destroy(worldObject);
            return true;
        }

        static bool IsForbiddenPickupHost(ItemPickup pickup)
        {
            var t = pickup.transform;
            if (t.GetComponentInParent<NpcDialogueBinding>(true) != null)
                return true;
            if (t.GetComponentInParent<BossAi>(true) != null)
                return true;
            if (t.GetComponentInParent<TigerNpcWanderAi>(true) != null)
                return true;
            if (t.GetComponentInParent<SpiderNpcWanderAi>(true) != null)
                return true;
            var animal = t.GetComponentInParent<AnimalNpc>(true);
            if (animal != null
                && !string.Equals(pickup.ItemId, GameConstants.LiveChickenItemId, StringComparison.OrdinalIgnoreCase))
                return true;
            return AnyAncestorIsTerrainOrKitBuildingShell(t);
        }

        static bool IsForbiddenImplicitWorldPickable(Transform pickupRoot, string itemId)
        {
            if (pickupRoot == null)
                return true;
            if (IsTerrainOrKitBuildingShellName(pickupRoot.gameObject.name))
                return true;
            if (pickupRoot.GetComponentInParent<NpcDialogueBinding>(true) != null)
                return true;
            if (pickupRoot.GetComponentInParent<BossAi>(true) != null)
                return true;
            if (pickupRoot.GetComponentInParent<TigerNpcWanderAi>(true) != null)
                return true;
            if (pickupRoot.GetComponentInParent<SpiderNpcWanderAi>(true) != null)
                return true;
            var animal = pickupRoot.GetComponentInParent<AnimalNpc>(true);
            if (animal != null
                && !string.Equals(itemId, GameConstants.LiveChickenItemId, StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        static bool AnyAncestorIsTerrainOrKitBuildingShell(Transform t)
        {
            for (var c = t; c != null; c = c.parent)
            {
                if (IsTerrainOrKitBuildingShellName(c.gameObject.name))
                    return true;
            }

            return false;
        }

        static bool IsTerrainOrKitBuildingShellName(string objectName)
        {
            var n = objectName ?? string.Empty;
            if (string.Equals(n.Trim(), "Terrain", StringComparison.OrdinalIgnoreCase))
                return true;
            return n.IndexOf("rpgpp_lt_building", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool IsWithinPlanarDistance(Vector3 from, Vector3 to, float maxPlanarMeters)
        {
            var dx = from.x - to.x;
            var dz = from.z - to.z;
            var maxSq = maxPlanarMeters * maxPlanarMeters;
            return dx * dx + dz * dz <= maxSq;
        }

        int CountHeroBooks(InventoryService inv)
        {
            var total = 0;
            var rows = inv != null ? inv.GetInventoryView(InventoryService.HeroActorId) : null;
            if (rows == null)
                return 0;
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null || string.IsNullOrWhiteSpace(row.itemId) || row.quantity <= 0)
                    continue;
                if (!row.itemId.Contains("book", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                total += row.quantity;
                if (total >= 3)
                    return 3;
            }

            return Mathf.Clamp(total, 0, 3);
        }
    }
}
