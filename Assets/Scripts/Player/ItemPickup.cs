using Rpg.Core;
using UnityEngine;

namespace Rpg.Player
{
    /// <summary>
    /// Marks a world object as a valid inventory pickup.
    /// </summary>
    public sealed class ItemPickup : MonoBehaviour
    {
        [SerializeField] string itemId;
        [SerializeField] bool consumed;

        public string ItemId => itemId;
        public bool IsConsumed => consumed;

        void Awake()
        {
            if (!consumed
                && !string.IsNullOrWhiteSpace(itemId)
                && string.Equals(itemId, GameConstants.LiveChickenItemId, System.StringComparison.OrdinalIgnoreCase))
                LiveChickenPickupProxy.Ensure(gameObject);
        }

        public void Configure(string canonicalItemId)
        {
            itemId = string.IsNullOrWhiteSpace(canonicalItemId) ? string.Empty : canonicalItemId.Trim();
            consumed = false;
            if (string.Equals(itemId, GameConstants.LiveChickenItemId, System.StringComparison.OrdinalIgnoreCase))
                LiveChickenPickupProxy.Ensure(gameObject);
        }

        public bool TryConsume()
        {
            if (consumed || string.IsNullOrWhiteSpace(itemId))
                return false;
            consumed = true;
            return true;
        }
    }
}
