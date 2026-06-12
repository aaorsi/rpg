using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Rpg.Dialogue
{
    public enum AgreementType
    {
        Hire,
        Advice,
        Persuasion
    }

    public enum AgreementLifecycleState
    {
        Offered,
        Accepted,
        InProgress,
        Completed,
        Failed,
        Expired
    }

    [Serializable]
    public sealed class AgreementRecord
    {
        public string agreementId;
        public string contractType;
        public string state;
        public string npcId;
        public string payerActorId;
        public string payeeActorId;
        public int payoutCoins;
        public string summary;
        public string notes;
        public string createdUtc;
        public string updatedUtc;
        public string completedUtc;
    }

    [Serializable]
    sealed class AgreementDocument
    {
        public int schemaVersion = 1;
        public List<AgreementRecord> agreements = new List<AgreementRecord>();
    }

    public sealed class AgreementService
    {
        readonly string _filePath;
        readonly object _lock = new object();
        AgreementDocument _doc;

        public static void ClearAllForNewPlaySession(string path = null)
        {
            var p = string.IsNullOrWhiteSpace(path)
                ? Path.Combine(Application.persistentDataPath, "RpgAgreements", "agreements.json")
                : path;
            try
            {
                if (File.Exists(p))
                    File.Delete(p);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AgreementService] Could not clear agreements file '{p}': {ex.Message}");
            }
        }

        public AgreementService(string filePath = null)
        {
            _filePath = string.IsNullOrWhiteSpace(filePath)
                ? Path.Combine(Application.persistentDataPath, "RpgAgreements", "agreements.json")
                : filePath;
            _doc = LoadOrNew();
        }

        public IReadOnlyList<AgreementRecord> Snapshot()
        {
            lock (_lock)
            {
                return _doc.agreements
                    .Where(x => x != null)
                    .Select(Clone)
                    .ToList();
            }
        }

        public AgreementRecord CreateOffer(
            AgreementType type,
            string npcId,
            string payerActorId,
            string payeeActorId,
            int payoutCoins,
            string summary,
            string notes = null)
        {
            var now = DateTime.UtcNow.ToString("o");
            var rec = new AgreementRecord
            {
                agreementId = Guid.NewGuid().ToString("N"),
                contractType = ToWire(type),
                state = ToWire(AgreementLifecycleState.Offered),
                npcId = Clean(npcId),
                payerActorId = Clean(payerActorId),
                payeeActorId = Clean(payeeActorId),
                payoutCoins = Mathf.Max(0, payoutCoins),
                summary = Clean(summary),
                notes = Clean(notes),
                createdUtc = now,
                updatedUtc = now,
                completedUtc = string.Empty
            };
            lock (_lock)
            {
                _doc.agreements.Add(rec);
                SaveUnsafe();
            }

            return Clone(rec);
        }

        public bool TryAccept(string agreementId) => TryTransition(agreementId, AgreementLifecycleState.Accepted);

        public bool TryStart(string agreementId) => TryTransition(agreementId, AgreementLifecycleState.InProgress);

        public bool TryFail(string agreementId, string notes = null) =>
            TryTransition(agreementId, AgreementLifecycleState.Failed, notes);

        public bool TryExpire(string agreementId, string notes = null) =>
            TryTransition(agreementId, AgreementLifecycleState.Expired, notes);

        public bool TryComplete(string agreementId, InventoryService inventoryService, out string error)
        {
            error = string.Empty;
            lock (_lock)
            {
                var record = FindByIdUnsafe(agreementId);
                if (record == null)
                {
                    error = "agreement_not_found";
                    return false;
                }

                if (!TryParseState(record.state, out var current))
                {
                    error = "invalid_state";
                    return false;
                }

                if (current != AgreementLifecycleState.InProgress)
                {
                    error = "transition_not_allowed";
                    return false;
                }

                if (record.payoutCoins > 0)
                {
                    if (inventoryService == null)
                    {
                        error = "inventory_unavailable";
                        return false;
                    }

                    var contractType = ParseType(record.contractType);
                    var transferred = contractType == AgreementType.Hire
                        ? inventoryService.TryPayWage(record.payerActorId, record.payeeActorId, record.payoutCoins)
                        : inventoryService.TryGrantReward(record.payerActorId, record.payeeActorId, record.payoutCoins);
                    if (!transferred)
                    {
                        error = "payout_transfer_failed";
                        return false;
                    }
                }

                record.state = ToWire(AgreementLifecycleState.Completed);
                record.updatedUtc = DateTime.UtcNow.ToString("o");
                record.completedUtc = record.updatedUtc;
                SaveUnsafe();
                return true;
            }
        }

        public AgreementRecord FindMostRecentByNpcAndType(string npcId, AgreementType type, bool includeTerminal = false)
        {
            var npcKey = Clean(npcId);
            var typeWire = ToWire(type);
            lock (_lock)
            {
                for (var i = _doc.agreements.Count - 1; i >= 0; i--)
                {
                    var rec = _doc.agreements[i];
                    if (rec == null
                        || !string.Equals(rec.npcId, npcKey, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(rec.contractType, typeWire, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!includeTerminal && IsTerminal(rec.state))
                        continue;
                    return Clone(rec);
                }

                return null;
            }
        }

        public List<string> BuildActiveAgreementSummariesForNpc(string npcId)
        {
            var npcKey = Clean(npcId);
            lock (_lock)
            {
                var result = new List<string>();
                foreach (var rec in _doc.agreements)
                {
                    if (rec == null || IsTerminal(rec.state))
                        continue;
                    if (!string.Equals(rec.npcId, npcKey, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var contractType = string.IsNullOrWhiteSpace(rec.contractType) ? "unknown" : rec.contractType.Trim();
                    var payout = Mathf.Max(0, rec.payoutCoins);
                    var summary = string.IsNullOrWhiteSpace(rec.summary) ? "(no summary)" : rec.summary.Trim();
                    result.Add($"{rec.state}:{contractType}:{summary}:coins={payout}");
                }

                return result;
            }
        }

        bool TryTransition(string agreementId, AgreementLifecycleState next, string notes = null)
        {
            lock (_lock)
            {
                var record = FindByIdUnsafe(agreementId);
                if (record == null || !TryParseState(record.state, out var current))
                    return false;
                if (!CanTransition(current, next))
                    return false;

                record.state = ToWire(next);
                if (!string.IsNullOrWhiteSpace(notes))
                    record.notes = Clean(notes);
                record.updatedUtc = DateTime.UtcNow.ToString("o");
                SaveUnsafe();
                return true;
            }
        }

        AgreementRecord FindByIdUnsafe(string agreementId)
        {
            if (string.IsNullOrWhiteSpace(agreementId) || _doc == null || _doc.agreements == null)
                return null;
            return _doc.agreements.FirstOrDefault(x =>
                x != null && string.Equals(x.agreementId, agreementId.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        AgreementDocument LoadOrNew()
        {
            lock (_lock)
            {
                try
                {
                    if (!File.Exists(_filePath))
                        return new AgreementDocument();
                    var parsed = JsonConvert.DeserializeObject<AgreementDocument>(File.ReadAllText(_filePath));
                    return parsed ?? new AgreementDocument();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AgreementService] Failed to load agreements: {ex.Message}");
                    return new AgreementDocument();
                }
            }
        }

        void SaveUnsafe()
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
                Debug.LogWarning($"[AgreementService] Failed to save agreements: {ex.Message}");
            }
        }

        static bool CanTransition(AgreementLifecycleState current, AgreementLifecycleState next)
        {
            if (current == next)
                return true;
            if (current == AgreementLifecycleState.Offered)
                return next == AgreementLifecycleState.Accepted
                       || next == AgreementLifecycleState.Failed
                       || next == AgreementLifecycleState.Expired;
            if (current == AgreementLifecycleState.Accepted)
                return next == AgreementLifecycleState.InProgress
                       || next == AgreementLifecycleState.Failed
                       || next == AgreementLifecycleState.Expired;
            if (current == AgreementLifecycleState.InProgress)
                return next == AgreementLifecycleState.Completed
                       || next == AgreementLifecycleState.Failed
                       || next == AgreementLifecycleState.Expired;
            return false;
        }

        static bool IsTerminal(string state)
        {
            if (!TryParseState(state, out var parsed))
                return true;
            return parsed == AgreementLifecycleState.Completed
                   || parsed == AgreementLifecycleState.Failed
                   || parsed == AgreementLifecycleState.Expired;
        }

        static string ToWire(AgreementType type)
        {
            if (type == AgreementType.Hire)
                return "hire";
            if (type == AgreementType.Advice)
                return "advice";
            return "persuasion";
        }

        static AgreementType ParseType(string type)
        {
            var key = Clean(type).ToLowerInvariant();
            if (key == "hire")
                return AgreementType.Hire;
            if (key == "advice")
                return AgreementType.Advice;
            return AgreementType.Persuasion;
        }

        static string ToWire(AgreementLifecycleState state)
        {
            if (state == AgreementLifecycleState.InProgress)
                return "in_progress";
            return state.ToString().ToLowerInvariant();
        }

        static bool TryParseState(string raw, out AgreementLifecycleState state)
        {
            state = AgreementLifecycleState.Offered;
            if (string.IsNullOrWhiteSpace(raw))
                return false;
            var key = raw.Trim().ToLowerInvariant();
            if (key == "offered")
            {
                state = AgreementLifecycleState.Offered;
                return true;
            }

            if (key == "accepted")
            {
                state = AgreementLifecycleState.Accepted;
                return true;
            }

            if (key == "in_progress" || key == "inprogress")
            {
                state = AgreementLifecycleState.InProgress;
                return true;
            }

            if (key == "completed")
            {
                state = AgreementLifecycleState.Completed;
                return true;
            }

            if (key == "failed")
            {
                state = AgreementLifecycleState.Failed;
                return true;
            }

            if (key == "expired")
            {
                state = AgreementLifecycleState.Expired;
                return true;
            }

            return false;
        }

        static AgreementRecord Clone(AgreementRecord source)
        {
            if (source == null)
                return null;
            return new AgreementRecord
            {
                agreementId = source.agreementId,
                contractType = source.contractType,
                state = source.state,
                npcId = source.npcId,
                payerActorId = source.payerActorId,
                payeeActorId = source.payeeActorId,
                payoutCoins = source.payoutCoins,
                summary = source.summary,
                notes = source.notes,
                createdUtc = source.createdUtc,
                updatedUtc = source.updatedUtc,
                completedUtc = source.completedUtc
            };
        }

        static string Clean(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
