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

        public void Configure(string canonicalItemId)
        {
            itemId = string.IsNullOrWhiteSpace(canonicalItemId) ? string.Empty : canonicalItemId.Trim();
            consumed = false;
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
