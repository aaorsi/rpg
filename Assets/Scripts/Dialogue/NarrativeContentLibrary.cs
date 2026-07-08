using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Rpg.Dialogue
{
    public sealed class NarrativeContentLibrary
    {
        readonly string _root;

        public NarrativeContentLibrary(string streamingAssetsRoot = null)
        {
            var sa = string.IsNullOrWhiteSpace(streamingAssetsRoot) ? Application.streamingAssetsPath : streamingAssetsRoot;
            _root = Path.Combine(sa, "Dialogue");
        }

        public GlobalKnowledgeDoc LoadGlobalKnowledge()
        {
            var relativePath = "world/global_knowledge.json";
            var p = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(p))
            {
                Debug.LogWarning($"[NarrativeContentLibrary] Missing content file: {p}");
                return new GlobalKnowledgeDoc();
            }

            try
            {
                var json = File.ReadAllText(p);
                var token = JToken.Parse(json);
                if (token.Type == JTokenType.Object)
                    return token.ToObject<GlobalKnowledgeDoc>() ?? new GlobalKnowledgeDoc();

                if (token.Type == JTokenType.Array)
                {
                    var docs = token.ToObject<List<GlobalKnowledgeDoc>>();
                    if (docs != null)
                    {
                        for (var i = 0; i < docs.Count; i++)
                        {
                            if (docs[i] != null)
                                return docs[i];
                        }
                    }

                    Debug.LogWarning($"[NarrativeContentLibrary] Parsed {p} as array but found no valid world entries.");
                    return new GlobalKnowledgeDoc();
                }

                Debug.LogWarning($"[NarrativeContentLibrary] Unexpected JSON root in {p}: {token.Type}");
                return new GlobalKnowledgeDoc();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NarrativeContentLibrary] Failed to parse {p}: {ex.Message}");
                return new GlobalKnowledgeDoc();
            }
        }

        public IntroPremiseLibraryDoc LoadIntroPremises() =>
            LoadJson<IntroPremiseLibraryDoc>("world/game_intro_library.json") ?? new IntroPremiseLibraryDoc();

        public CoreProgressionDoc LoadCoreProgression() =>
            LoadJson<CoreProgressionDoc>("world/core_progression.json") ?? new CoreProgressionDoc();

        public NpcKnowledgeLibraryDoc LoadNpcKnowledge() =>
            LoadJson<NpcKnowledgeLibraryDoc>("npc/npc_unique_knowledge.json") ?? new NpcKnowledgeLibraryDoc();

        public TradeRequirementsDoc LoadTradeRequirements() =>
            LoadJson<TradeRequirementsDoc>("world/trade_requirements.json") ?? new TradeRequirementsDoc();

        public ObjectArtifactCatalogDoc LoadObjectArtifactCatalog() =>
            LoadJson<ObjectArtifactCatalogDoc>("world/object_artifact_catalog.json") ?? new ObjectArtifactCatalogDoc();

        public LocationCatalogDoc LoadLocationCatalog() =>
            LoadJson<LocationCatalogDoc>("world/location_catalog.json") ?? new LocationCatalogDoc();

        public NpcArchetypeLibraryDoc LoadNpcArchetypes() =>
            LoadJson<NpcArchetypeLibraryDoc>("npc/archetype_library.json") ?? new NpcArchetypeLibraryDoc();

        T LoadJson<T>(string relativePath) where T : class
        {
            var p = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(p))
            {
                Debug.LogWarning($"[NarrativeContentLibrary] Missing content file: {p}");
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<T>(File.ReadAllText(p));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NarrativeContentLibrary] Failed to parse {p}: {ex.Message}");
                return null;
            }
        }
    }
}
