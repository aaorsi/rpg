using System;
using System.Collections.Generic;
using Rpg.Npc;
using UnityEngine;

namespace Rpg.Dialogue
{
    /// <summary>
    /// Persists per-NPC persona JSON under <see cref="Application.persistentDataPath"/>.
    /// Persona records use stable persona IDs derived from npcId.
    /// </summary>
    public sealed class NpcPersonaRepository
    {
        const int SchemaVersion = 1;
        readonly JsonFileStore _store;

        public NpcPersonaRepository(string rootDirectory = null)
        {
            var rootDir = string.IsNullOrWhiteSpace(rootDirectory)
                ? System.IO.Path.Combine(Application.persistentDataPath, "RpgNpcPersonas")
                : rootDirectory;
            _store = new JsonFileStore(rootDir, "NpcPersonaRepository");
        }

        public NpcPersona Load(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId))
                npcId = "npc_unknown";

            return _store.Load<NpcPersona>(npcId, () => null, persona => NormalizePersona(persona, npcId, null));
        }

        public NpcPersona LoadOrCreate(NpcDefinition definition, string npcType = "normal")
        {
            if (definition == null)
                return null;

            var npcId = string.IsNullOrWhiteSpace(definition.npcId) ? "npc_unknown" : definition.npcId.Trim();
            NpcPersona result = null;
            _store.Update<NpcPersona>(
                npcId,
                () => null,
                persona =>
                {
                    if (persona != null)
                    {
                        result = NormalizePersona(persona, npcId, npcType);
                        return result;
                    }

                    var created = BuildFallback(definition, npcType);
                    result = NormalizePersona(created, npcId, npcType);
                    return result;
                },
                persona => NormalizePersona(persona, npcId, npcType),
                persona => NormalizePersona(persona, npcId, npcType));

            return result;
        }

        public void Save(NpcPersona persona)
        {
            if (persona == null || string.IsNullOrWhiteSpace(persona.npcId))
                return;

            _store.Save(persona.npcId, NormalizePersona(persona, persona.npcId, null));
        }

        NpcPersona NormalizePersona(NpcPersona persona, string npcId, string npcTypeHint)
        {
            var normalizedNpcId = string.IsNullOrWhiteSpace(npcId) ? "npc_unknown" : npcId.Trim();
            persona ??= new NpcPersona();
            persona.schemaVersion = SchemaVersion;
            persona.npcId = normalizedNpcId;
            persona.personaId = BuildStablePersonaId(normalizedNpcId);
            persona.personality = string.IsNullOrWhiteSpace(persona.personality) ? "practical" : persona.personality.Trim();
            persona.socialTraits ??= new Dictionary<string, string>();
            persona.goals = NarrativePhantomReferenceFilter.SanitizeGoalList(persona.goals);
            persona.capabilities ??= BuildDefaultCapabilities(npcTypeHint);
            if (persona.capabilities.Count == 0)
                persona.capabilities = BuildDefaultCapabilities(npcTypeHint);
            EnsureSocialLevel(persona.socialTraits, "helpfulness");
            EnsureSocialLevel(persona.socialTraits, "skepticism");
            EnsureSocialLevel(persona.socialTraits, "patience");
            EnsureSocialLevel(persona.socialTraits, "trickery");
            persona.updatedUtc = string.IsNullOrWhiteSpace(persona.updatedUtc) ? DateTime.UtcNow.ToString("o") : persona.updatedUtc;
            return persona;
        }

        public static NpcPersona BuildFallback(NpcDefinition definition, string npcType = "normal")
        {
            var npcId = definition != null && !string.IsNullOrWhiteSpace(definition.npcId)
                ? definition.npcId.Trim()
                : "npc_unknown";
            var role = definition?.roleSummary ?? string.Empty;
            var text = role.ToLowerInvariant();
            var personality = text.Contains("guard") || text.Contains("watch")
                ? "vigilant"
                : text.Contains("merchant") || text.Contains("trade")
                    ? "savvy"
                    : text.Contains("friendly") || text.Contains("welcome")
                        ? "friendly"
                        : "practical";

            var goals = new List<string>
            {
                "Support village stability and help with practical local needs."
            };
            if (text.Contains("trade") || text.Contains("merchant"))
                goals.Add("Exchange useful items and information when trust is established.");
            else
                goals.Add("Share nearby rumors and guidance when asked.");

            return new NpcPersona
            {
                schemaVersion = SchemaVersion,
                personaId = BuildStablePersonaId(npcId),
                npcId = npcId,
                personality = personality,
                socialTraits = new Dictionary<string, string>
                {
                    { "helpfulness", "medium" },
                    { "skepticism", "medium" },
                    { "patience", "medium" },
                    { "trickery", "low" }
                },
                goals = goals,
                capabilities = BuildDefaultCapabilities(npcType),
                updatedUtc = DateTime.UtcNow.ToString("o")
            };
        }

        static List<string> BuildDefaultCapabilities(string npcType)
        {
            var type = string.IsNullOrWhiteSpace(npcType) ? "normal" : npcType.Trim().ToLowerInvariant();
            if (type == "sidekick")
                return new List<string> { "dialogue", "follow_hero", "give", "trade" };
            if (type == "ghoul")
                return new List<string> { "dialogue" };
            return new List<string> { "dialogue", "give", "trade", "guide_to_location", "refer_to_npc" };
        }

        static void EnsureSocialLevel(Dictionary<string, string> traits, string key)
        {
            if (traits == null)
                return;

            if (!traits.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                traits[key] = "medium";
                return;
            }

            var normalized = value.Trim().ToLowerInvariant();
            if (normalized != "low" && normalized != "medium" && normalized != "high")
                normalized = "medium";
            traits[key] = normalized;
        }

        static string BuildStablePersonaId(string npcId)
        {
            var safeNpcId = string.IsNullOrWhiteSpace(npcId) ? "npc_unknown" : npcId.Trim().ToLowerInvariant();
            return $"persona_{safeNpcId.Replace(' ', '_')}";
        }

    }
}
