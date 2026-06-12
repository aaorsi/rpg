using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Rpg.Dialogue
{
    [Serializable]
    public sealed class NpcPersona
    {
        [JsonProperty("schema_version")] public int schemaVersion = 1;
        [JsonProperty("persona_id")] public string personaId = string.Empty;
        [JsonProperty("npc_id")] public string npcId = string.Empty;
        [JsonProperty("personality")] public string personality = string.Empty;
        [JsonProperty("social_traits")] public Dictionary<string, string> socialTraits = new Dictionary<string, string>();
        [JsonProperty("goals")] public List<string> goals = new List<string>();
        [JsonProperty("capabilities")] public List<string> capabilities = new List<string>();
        [JsonProperty("updated_utc")] public string updatedUtc = string.Empty;
    }
}
