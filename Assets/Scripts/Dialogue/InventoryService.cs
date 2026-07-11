using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Rpg.Core;
using UnityEngine;

namespace Rpg.Dialogue
{
    [Serializable]
    public sealed class InventoryEntry
    {
        public string itemId;
        public int quantity;
    }

    [Serializable]
    public sealed class ActorInventory
    {
        public string actorId;
        public List<InventoryEntry> entries = new List<InventoryEntry>();
    }

    [Serializable]
    public sealed class InventoryViewEntry
    {
        public string itemId;
        public string displayName;
        public int quantity;
    }

    [Serializable]
    sealed class InventoryDocument
    {
        public int schemaVersion = 1;
        public List<ActorInventory> actors = new List<ActorInventory>();
    }

    public sealed class InventoryService
    {
        const int HeroInventoryMaxSlots = 5;
        readonly string _filePath;
        readonly object _lock = new object();
        readonly ObjectArtifactCatalogDoc _catalog;
        InventoryDocument _doc;

        public const string HeroActorId = "hero";
        public const string CoinItemId = "coin";
        const int LiveChickenHeroMaxCount = 2;
        static readonly string[] CoinAliases = { "coin", "coins", "gold_coin", "gold_coins", "gold" };

        public static void ClearAllForNewPlaySession(string path = null)
        {
            var p = string.IsNullOrWhiteSpace(path)
                ? Path.Combine(Application.persistentDataPath, "RpgInventory", "inventories.json")
                : path;
            try
            {
                if (File.Exists(p))
                    File.Delete(p);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[InventoryService] Could not clear inventories file '{p}': {ex.Message}");
            }
        }

        public InventoryService(NarrativeContentLibrary library, string filePath = null)
        {
            _filePath = string.IsNullOrWhiteSpace(filePath)
                ? Path.Combine(Application.persistentDataPath, "RpgInventory", "inventories.json")
                : filePath;
            _catalog = library != null ? library.LoadObjectArtifactCatalog() : new ObjectArtifactCatalogDoc();
            _doc = LoadOrNew();
        }

        InventoryDocument LoadOrNew()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(_filePath))
                        return new InventoryDocument();
                    var parsed = JsonConvert.DeserializeObject<InventoryDocument>(File.ReadAllText(_filePath));
                    return parsed ?? new InventoryDocument();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[InventoryService] Failed to load inventories: {ex.Message}");
                    return new InventoryDocument();
                }
            }
        }

        void Save()
        {
            lock (_lock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(_filePath);
                    if (!string.IsNullOrWhiteSpace(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllText(_filePath, JsonConvert.SerializeObject(_doc, Formatting.Indented));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[InventoryService] Failed to save inventories: {ex.Message}");
                }
            }
        }

        public void EnsureSeededHero(int startingPurseCoins = 0)
        {
            EnsureActor(HeroActorId);
            if (GetActor(HeroActorId).entries.Count > 0)
            {
                EnsureHeroStartingPurse(startingPurseCoins);
                return;
            }
            var ids = AllKnownItemIds().Take(3).ToArray();
            if (ids.Length == 0)
                return;
            AddItem(HeroActorId, ids[0], 2);
            if (ids.Length > 1) AddItem(HeroActorId, ids[1], 1);
            if (ids.Length > 2) AddItem(HeroActorId, ids[2], 1);
            EnsureHeroStartingPurse(startingPurseCoins);
        }

        public void EnsureSeededNpc(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                return;
            EnsureActor(npcId);
            var actor = GetActor(npcId);
            if (HasNonCoinInventory(actor))
                return;
            var all = AllKnownItemIds().Where(id => !IsCoinAlias(id)).ToArray();
            if (all.Length == 0)
                return;
            var seed = npcId.GetHashCode();
            var rng = new System.Random(seed);
            var item = all[rng.Next(all.Length)];
            AddItem(npcId, item, 1);
        }

        public int GetCoinBalance(string actorId)
        {
            if (string.IsNullOrWhiteSpace(actorId))
                return 0;
            var actor = GetActorOrNull(actorId);
            if (actor == null || actor.entries == null)
                return 0;
            var entry = actor.entries.FirstOrDefault(x =>
                x != null && string.Equals(x.itemId, CoinItemId, StringComparison.OrdinalIgnoreCase));
            return entry != null ? Mathf.Max(0, entry.quantity) : 0;
        }

        public void EnsureVillageNpcWallets(IReadOnlyList<string> npcIds, int wealthyNpcCount = 5)
        {
            if (npcIds == null || npcIds.Count == 0)
                return;
            var candidates = new List<string>();
            for (var i = 0; i < npcIds.Count; i++)
            {
                var id = npcIds[i];
                if (string.IsNullOrWhiteSpace(id)
                    || string.Equals(id.Trim(), HeroActorId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!candidates.Contains(id.Trim()))
                    candidates.Add(id.Trim());
            }

            if (candidates.Count == 0)
                return;

            var rng = new System.Random(BuildWalletSeed(candidates));
            var wealthy = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var shuffled = candidates.OrderBy(_ => rng.Next()).ToList();
            var wealthySlots = Mathf.Clamp(wealthyNpcCount, 0, shuffled.Count);
            for (var i = 0; i < wealthySlots; i++)
                wealthy.Add(shuffled[i]);

            for (var i = 0; i < candidates.Count; i++)
            {
                var npcId = candidates[i];
                EnsureActor(npcId);
                if (GetCoinBalance(npcId) > 0)
                    continue;
                var coins = wealthy.Contains(npcId) ? rng.Next(50, 76) : rng.Next(0, 11);
                if (coins > 0)
                    TryAddItem(npcId, CoinItemId, coins);
            }
        }

        public bool TryStealRandomItem(string fromActorId, string toActorId, out string stolenItemId, out string stolenDisplayName)
        {
            stolenItemId = string.Empty;
            stolenDisplayName = string.Empty;
            if (string.IsNullOrWhiteSpace(fromActorId) || string.IsNullOrWhiteSpace(toActorId))
                return false;
            var actor = GetActorOrNull(fromActorId);
            if (actor?.entries == null || actor.entries.Count == 0)
                return false;
            var stealable = actor.entries
                .Where(e => e != null
                    && !string.IsNullOrWhiteSpace(e.itemId)
                    && e.quantity > 0
                    && !IsCoinAlias(e.itemId))
                .ToList();
            if (stealable.Count == 0)
                return false;
            var seed = (fromActorId + "|" + toActorId).GetHashCode();
            var pick = stealable[new System.Random(seed).Next(stealable.Count)];
            stolenItemId = pick.itemId;
            stolenDisplayName = GetItemDisplayName(stolenItemId);
            return TryTransfer(fromActorId, toActorId, stolenItemId, 1);
        }

        public IReadOnlyList<InventoryViewEntry> GetStealableItems(string actorId)
        {
            var rows = GetInventoryView(actorId);
            return rows.Where(e => e != null
                    && !string.IsNullOrWhiteSpace(e.itemId)
                    && e.quantity > 0
                    && !IsCoinAlias(e.itemId))
                .ToList();
        }

        static int BuildWalletSeed(IReadOnlyList<string> npcIds)
        {
            unchecked
            {
                var hash = 17;
                for (var i = 0; i < npcIds.Count; i++)
                {
                    var id = npcIds[i];
                    if (string.IsNullOrWhiteSpace(id))
                        continue;
                    hash = hash * 31 + id.Trim().ToLowerInvariant().GetHashCode();
                }

                return hash;
            }
        }

        static bool HasNonCoinInventory(ActorInventory actor)
        {
            if (actor?.entries == null)
                return false;
            for (var i = 0; i < actor.entries.Count; i++)
            {
                var entry = actor.entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.itemId) || entry.quantity <= 0)
                    continue;
                if (!IsCoinAlias(entry.itemId))
                    return true;
            }

            return false;
        }

        public void EnsureActor(string actorId)
        {
            if (string.IsNullOrWhiteSpace(actorId))
                return;
            if (GetActorOrNull(actorId) != null)
                return;
            _doc.actors.Add(new ActorInventory { actorId = actorId.Trim() });
            Save();
        }

        ActorInventory GetActor(string actorId)
        {
            var a = GetActorOrNull(actorId);
            if (a != null)
                return a;
            a = new ActorInventory { actorId = actorId.Trim() };
            _doc.actors.Add(a);
            return a;
        }

        ActorInventory GetActorOrNull(string actorId)
        {
            if (string.IsNullOrWhiteSpace(actorId))
                return null;
            return _doc.actors.FirstOrDefault(a => a != null && string.Equals(a.actorId, actorId.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public bool IsKnownItem(string itemId)
        {
            var normalized = NormalizeItemId(itemId);
            if (string.IsNullOrWhiteSpace(normalized))
                return false;
            return AllKnownItemIds().Contains(normalized, StringComparer.OrdinalIgnoreCase);
        }

        public bool HasAtLeast(string actorId, string itemId, int qty)
        {
            var normalizedItemId = NormalizeItemId(itemId);
            if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(normalizedItemId) || qty <= 0)
                return false;
            var actor = GetActorOrNull(actorId);
            if (actor == null || actor.entries == null)
                return false;
            var e = actor.entries.FirstOrDefault(x => string.Equals(x.itemId, normalizedItemId, StringComparison.OrdinalIgnoreCase));
            return e != null && e.quantity >= qty;
        }

        public IReadOnlyList<string> GetAllKnownItemIds() => AllKnownItemIds().ToList();

        IEnumerable<string> AllKnownItemIds()
        {
            IEnumerable<string> obj = _catalog?.objects?.Where(x => !string.IsNullOrWhiteSpace(x.id)).Select(x => x.id.Trim()) ?? Array.Empty<string>();
            IEnumerable<string> art = _catalog?.artifacts?.Where(x => !string.IsNullOrWhiteSpace(x.id)).Select(x => x.id.Trim()) ?? Array.Empty<string>();
            return obj.Concat(art).Concat(new[] { CoinItemId }).Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public void AddItem(string actorId, string itemId, int qty)
        {
            TryAddItem(actorId, itemId, qty);
        }

        /// <summary>
        /// True when the hero cannot accept one more pickup of <paramref name="itemId"/> because all five slots are used
        /// and this id would need a new row (non-stackable or not yet present).
        /// </summary>
        public bool IsHeroInventoryFullForDistinctPickup(string itemId)
        {
            var trimmed = NormalizeItemId(itemId);
            if (string.IsNullOrWhiteSpace(trimmed) || !IsKnownItem(trimmed))
                return false;
            var actor = GetActor(HeroActorId);
            var e = actor.entries.FirstOrDefault(x => string.Equals(x.itemId, trimmed, StringComparison.OrdinalIgnoreCase));
            if (e != null && IsStackable(trimmed))
                return false;
            return !HasCapacityForNewEntry(HeroActorId, actor);
        }

        public bool TryAddItem(string actorId, string itemId, int qty)
        {
            var normalizedItemId = NormalizeItemId(itemId);
            if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(normalizedItemId) || qty <= 0)
                return false;
            if (!IsKnownItem(normalizedItemId))
                return false;
            if (string.Equals(actorId.Trim(), HeroActorId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(normalizedItemId, GameConstants.LiveChickenItemId, StringComparison.OrdinalIgnoreCase))
            {
                var actor = GetActor(actorId);
                var existing = actor.entries.FirstOrDefault(x =>
                    string.Equals(x.itemId, GameConstants.LiveChickenItemId, StringComparison.OrdinalIgnoreCase));
                var have = existing != null ? existing.quantity : 0;
                if (have >= LiveChickenHeroMaxCount)
                    return false;
                qty = Mathf.Min(qty, LiveChickenHeroMaxCount - have);
                if (qty <= 0)
                    return false;
            }

            var actor2 = GetActor(actorId);
            var e = actor2.entries.FirstOrDefault(x => string.Equals(x.itemId, normalizedItemId, StringComparison.OrdinalIgnoreCase));
            var stackable = IsStackable(normalizedItemId);
            if (e == null && !HasCapacityForNewEntry(actorId, actor2))
                return false;
            if (e == null || !stackable)
                actor2.entries.Add(new InventoryEntry { itemId = normalizedItemId, quantity = stackable ? qty : 1 });
            else
                e.quantity += qty;
            Save();
            return true;
        }

        public bool RemoveItem(string actorId, string itemId, int qty)
        {
            var normalizedItemId = NormalizeItemId(itemId);
            if (string.IsNullOrWhiteSpace(actorId) || string.IsNullOrWhiteSpace(normalizedItemId) || qty <= 0)
                return false;
            var actor = GetActorOrNull(actorId);
            if (actor == null)
                return false;
            var e = actor.entries.FirstOrDefault(x => string.Equals(x.itemId, normalizedItemId, StringComparison.OrdinalIgnoreCase));
            if (e == null || e.quantity < qty)
                return false;
            e.quantity -= qty;
            if (e.quantity <= 0)
                actor.entries.Remove(e);
            Save();
            return true;
        }

        public bool TryTransfer(string fromActorId, string toActorId, string itemId, int qty)
        {
            var normalizedItemId = NormalizeItemId(itemId);
            if (qty <= 0)
                qty = 1;
            if (!RemoveItem(fromActorId, normalizedItemId, qty))
                return false;
            if (TryAddItem(toActorId, normalizedItemId, qty))
                return true;
            TryAddItem(fromActorId, normalizedItemId, qty);
            return false;
        }

        public bool TryTrade(string actorAId, string actorBId, string actorAItemId, int actorAQty, string actorBItemId, int actorBQty)
        {
            var actorAItemNormalized = NormalizeItemId(actorAItemId);
            var actorBItemNormalized = NormalizeItemId(actorBItemId);
            var aQty = Mathf.Max(1, actorAQty);
            var bQty = Mathf.Max(1, actorBQty);
            if (!HasAtLeast(actorAId, actorAItemNormalized, aQty) || !HasAtLeast(actorBId, actorBItemNormalized, bQty))
                return false;
            if (!RemoveItem(actorAId, actorAItemNormalized, aQty))
                return false;
            if (!RemoveItem(actorBId, actorBItemNormalized, bQty))
            {
                AddItem(actorAId, actorAItemNormalized, aQty);
                return false;
            }
            if (!TryAddItem(actorAId, actorBItemNormalized, bQty))
            {
                TryAddItem(actorAId, actorAItemNormalized, aQty);
                TryAddItem(actorBId, actorBItemNormalized, bQty);
                return false;
            }

            if (!TryAddItem(actorBId, actorAItemNormalized, aQty))
            {
                RemoveItem(actorAId, actorBItemNormalized, bQty);
                TryAddItem(actorAId, actorAItemNormalized, aQty);
                TryAddItem(actorBId, actorBItemNormalized, bQty);
                return false;
            }

            return true;
        }

        public void EnsureHeroStartingPurse(int startingPurseCoins)
        {
            var targetCoins = Mathf.Max(0, startingPurseCoins);
            if (targetCoins <= 0)
                return;
            EnsureActor(HeroActorId);
            var actor = GetActor(HeroActorId);
            var current = actor.entries.FirstOrDefault(x => string.Equals(x.itemId, CoinItemId, StringComparison.OrdinalIgnoreCase));
            var currentCoins = current != null ? Mathf.Max(0, current.quantity) : 0;
            if (currentCoins >= targetCoins)
                return;
            TryAddItem(HeroActorId, CoinItemId, targetCoins - currentCoins);
        }

        public bool TryPayWage(string payerActorId, string recipientActorId, int coins)
        {
            var amount = Mathf.Max(1, coins);
            return TryTransfer(payerActorId, recipientActorId, CoinItemId, amount);
        }

        public bool TryGrantReward(string granterActorId, string recipientActorId, int coins)
        {
            var amount = Mathf.Max(1, coins);
            return TryTransfer(granterActorId, recipientActorId, CoinItemId, amount);
        }

        public string BuildPromptBlock(string heroActorId, string npcActorId)
        {
            var redactLiveChicken = DialogueManager.Instance != null
                && DialogueManager.Instance.ShouldRedactLiveChickenInPromptForNpc(npcActorId);
            var sb = new StringBuilder();
            sb.AppendLine("HERO_INVENTORY:");
            sb.AppendLine(DescribeInventory(heroActorId, omitLiveChicken: redactLiveChicken));
            sb.AppendLine();
            sb.AppendLine("NPC_INVENTORY:");
            sb.AppendLine(DescribeInventory(npcActorId));
            if (redactLiveChicken)
            {
                sb.AppendLine();
                sb.AppendLine(
                    "NOTE: The hero may be carrying a live animal not listed here; only the theft-victim NPC " +
                    "(if any) should treat it as stolen property from that NPC. Other NPCs must not accuse the hero of stealing chickens from them.");
            }

            return sb.ToString().TrimEnd();
        }

        public string DescribeInventory(string actorId, bool omitLiveChicken = false)
        {
            var actor = GetActorOrNull(actorId);
            if (actor == null || actor.entries == null || actor.entries.Count == 0)
                return "(empty)";
            var ordered = actor.entries
                .OrderByDescending(e => IsQuestCritical(e.itemId))
                .ThenByDescending(e => e.quantity)
                .ThenBy(e => e.itemId, StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            var any = false;
            foreach (var e in ordered)
            {
                if (omitLiveChicken
                    && string.Equals(e.itemId?.Trim(), GameConstants.LiveChickenItemId, StringComparison.OrdinalIgnoreCase))
                    continue;
                any = true;
                var v = GetTradeValue(e.itemId);
                var marker = IsQuestCritical(e.itemId) ? " [quest]" : string.Empty;
                var display = GetItemDisplayName(e.itemId);
                sb.AppendLine($"- {display} x{Mathf.Max(0, e.quantity)} (v:{v}){marker}");
            }

            return any ? sb.ToString().TrimEnd() : "(empty)";
        }

        public List<InventoryViewEntry> GetInventoryView(string actorId)
        {
            var actor = GetActorOrNull(actorId);
            var rows = new List<InventoryViewEntry>();
            if (actor == null || actor.entries == null || actor.entries.Count == 0)
                return rows;
            foreach (var e in actor.entries.OrderByDescending(x => x.quantity).ThenBy(x => x.itemId, StringComparer.OrdinalIgnoreCase))
            {
                if (e == null || string.IsNullOrWhiteSpace(e.itemId) || e.quantity <= 0)
                    continue;
                rows.Add(new InventoryViewEntry
                {
                    itemId = e.itemId,
                    displayName = GetItemDisplayName(e.itemId),
                    quantity = Mathf.Max(0, e.quantity)
                });
            }

            return rows;
        }

        public string GetItemDisplayName(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return "item";
            if (IsCoinAlias(itemId))
                return "coins";
            var entry = FindCatalogEntry(itemId);
            if (entry != null && !string.IsNullOrWhiteSpace(entry.label))
                return InferShortNameFromItemId(entry.label);
            return InferShortNameFromItemId(itemId);
        }

        bool IsQuestCritical(string itemId)
        {
            var entry = FindCatalogEntry(itemId);
            return entry != null && entry.questCritical;
        }

        bool IsStackable(string itemId)
        {
            if (IsCoinAlias(itemId))
                return true;
            var entry = FindCatalogEntry(itemId);
            return entry == null || entry.stackable;
        }

        int GetTradeValue(string itemId)
        {
            if (IsCoinAlias(itemId))
                return 1;
            var entry = FindCatalogEntry(itemId);
            return entry != null ? Mathf.Max(0, entry.tradeValue) : 0;
        }

        CatalogEntry FindCatalogEntry(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return null;
            var key = NormalizeItemId(itemId);
            return _catalog?.objects?.FirstOrDefault(x => string.Equals(x.id, key, StringComparison.OrdinalIgnoreCase))
                   ?? _catalog?.artifacts?.FirstOrDefault(x => string.Equals(x.id, key, StringComparison.OrdinalIgnoreCase));
        }

        static string NormalizeItemId(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return string.Empty;
            var normalized = itemId.Trim();
            return IsCoinAlias(normalized) ? CoinItemId : normalized;
        }

        static bool IsCoinAlias(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return false;
            var trimmed = itemId.Trim();
            foreach (var alias in CoinAliases)
            {
                if (string.Equals(trimmed, alias, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        static string InferShortNameFromItemId(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return "item";
            var raw = itemId.Trim().Replace('_', ' ').Replace('-', ' ');
            var parts = raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var kept = new List<string>();
            foreach (var p in parts)
            {
                var t = StripEmbeddedVersionSuffix(p.Trim());
                if (t.Length == 0)
                    continue;
                var lower = t.ToLowerInvariant();
                if (lower == "smooth" || lower == "rough" || lower == "mesh" || lower == "prefab")
                    continue;
                var isVersionToken = lower.Length >= 2 && lower[0] == 'v';
                if (isVersionToken)
                {
                    var allDigits = true;
                    for (var i = 1; i < lower.Length; i++)
                    {
                        if (!char.IsDigit(lower[i]))
                        {
                            allDigits = false;
                            break;
                        }
                    }
                    if (allDigits)
                        continue;
                }
                kept.Add(t);
            }

            var joined = kept.Count > 0 ? string.Join(" ", kept) : raw;
            return NormalizeHumanName(joined);
        }

        static string StripEmbeddedVersionSuffix(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return string.Empty;
            var t = token.Trim();
            var cut = t.Length;
            for (var i = 1; i < t.Length; i++)
            {
                if ((t[i] == 'v' || t[i] == 'V') && char.IsDigit(t[i - 1]))
                {
                    var onlyDigitsAfter = true;
                    for (var j = i + 1; j < t.Length; j++)
                    {
                        if (!char.IsDigit(t[j]))
                        {
                            onlyDigitsAfter = false;
                            break;
                        }
                    }
                    if (onlyDigitsAfter)
                    {
                        cut = i - 1;
                        break;
                    }
                }
            }
            if (cut <= 0)
                return t;
            return t.Substring(0, Mathf.Clamp(cut, 1, t.Length)).Trim('_', '-', ' ');
        }

        static string NormalizeHumanName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "item";
            var s = value.Trim();
            var sb = new StringBuilder(s.Length + 4);
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (i > 0 && char.IsUpper(c) && char.IsLetter(s[i - 1]) && !char.IsUpper(s[i - 1]))
                    sb.Append(' ');
                sb.Append(c);
            }
            var words = sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < words.Length; i++)
                words[i] = words[i].ToLowerInvariant();
            return string.Join(" ", words);
        }

        static bool IsHeroActor(string actorId)
            => string.Equals(actorId?.Trim(), HeroActorId, StringComparison.OrdinalIgnoreCase);

        static bool HasCapacityForNewEntry(string actorId, ActorInventory actor)
        {
            if (!IsHeroActor(actorId))
                return true;
            if (actor == null || actor.entries == null)
                return true;
            var nonEmptyEntries = 0;
            foreach (var entry in actor.entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.itemId) || entry.quantity <= 0)
                    continue;
                nonEmptyEntries++;
                if (nonEmptyEntries >= HeroInventoryMaxSlots)
                    return false;
            }

            return true;
        }
    }
}
