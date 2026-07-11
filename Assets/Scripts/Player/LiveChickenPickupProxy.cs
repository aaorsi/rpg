using Rpg.Core;
using UnityEngine;

namespace Rpg.Player
{
    /// <summary>
    /// Large trigger volume so live chickens are easy to click despite tiny rig colliders and terrain in front.
    /// </summary>
    public static class LiveChickenPickupProxy
    {
        const string ProxyChildName = "ItemPickupProxy";
        const float ProxyRadiusMeters = 4.5f;
        const float ProxyHeightMeters = 2.4f;
        const float ProxyCenterYOffset = 1.1f;

        public static void Ensure(GameObject animal)
        {
            if (animal == null)
                return;
            var pickup = animal.GetComponent<ItemPickup>();
            if (pickup == null
                || pickup.IsConsumed
                || !string.Equals(pickup.ItemId, GameConstants.LiveChickenItemId, System.StringComparison.OrdinalIgnoreCase))
                return;

            var proxyTransform = animal.transform.Find(ProxyChildName);
            GameObject proxyGo;
            if (proxyTransform != null)
            {
                proxyGo = proxyTransform.gameObject;
            }
            else
            {
                proxyGo = new GameObject(ProxyChildName);
                proxyGo.transform.SetParent(animal.transform, false);
            }

            proxyGo.transform.localPosition = new Vector3(0f, ProxyCenterYOffset, 0f);
            var capsule = proxyGo.GetComponent<CapsuleCollider>();
            if (capsule == null)
            {
                foreach (var legacy in proxyGo.GetComponents<SphereCollider>())
                    Object.Destroy(legacy);
                capsule = proxyGo.AddComponent<CapsuleCollider>();
            }

            capsule.isTrigger = true;
            capsule.direction = 1;
            capsule.radius = ProxyRadiusMeters;
            capsule.height = Mathf.Max(ProxyHeightMeters, ProxyRadiusMeters * 2f);
            capsule.center = Vector3.zero;
        }
    }
}
