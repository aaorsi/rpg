using System.Collections.Generic;
using NUnit.Framework;
using Rpg.Dialogue;

namespace Rpg.Editor.Tests.EditMode
{
    public sealed class NarrativePhantomReferenceFilterTests
    {
        [Test]
        public void ContainsPhantomReference_detects_retired_artifacts()
        {
            Assert.IsTrue(NarrativePhantomReferenceFilter.ContainsPhantomReference("Find the magic diamond near the warehouse."));
            Assert.IsTrue(NarrativePhantomReferenceFilter.ContainsPhantomReference("portal_core"));
            Assert.IsFalse(NarrativePhantomReferenceFilter.ContainsPhantomReference("Trade book_v1 for knife_v1."));
        }

        [Test]
        public void SanitizeGoalList_drops_phantom_goals_and_keeps_valid_ones()
        {
            var goals = new List<string>
            {
                "Secure better tools through barter.",
                "Recover the magic diamond for the cult leader.",
                "Share nearby rumors when asked."
            };

            var sanitized = NarrativePhantomReferenceFilter.SanitizeGoalList(goals);

            Assert.AreEqual(2, sanitized.Count);
            Assert.IsFalse(NarrativePhantomReferenceFilter.ContainsPhantomReference(sanitized[0]));
            Assert.IsFalse(NarrativePhantomReferenceFilter.ContainsPhantomReference(sanitized[1]));
        }

        [Test]
        public void SanitizeCanon_filters_invalid_trade_requirements()
        {
            var canon = new NarrativeSessionCanon
            {
                tradeRequirements = new List<TradeRequirementEntry>
                {
                    new TradeRequirementEntry
                    {
                        id = "trade_valid",
                        ownerNpcId = "npc_1_mara",
                        givesItemId = "book_v1",
                        wantsItemId = "knife_v1"
                    },
                    new TradeRequirementEntry
                    {
                        id = "trade_phantom",
                        ownerNpcId = "npc_2_dorian",
                        givesItemId = "portal_core",
                        wantsItemId = "lantern_v2"
                    }
                }
            };

            var catalog = new ObjectArtifactCatalogDoc
            {
                objects = new List<CatalogEntry>
                {
                    new CatalogEntry { id = "book_v1" },
                    new CatalogEntry { id = "knife_v1" },
                    new CatalogEntry { id = "lantern_v2" }
                }
            };
            var refs = new NarrativeReferenceValidator(
                new[] { "npc_1_mara", "npc_2_dorian" },
                new[] { "book_v1", "knife_v1", "lantern_v2" },
                new[] { "warehouse", "castle" },
                new[] { "m_trade_chain" });

            NarrativePhantomReferenceFilter.SanitizeCanon(
                canon,
                refs,
                new[] { "npc_1_mara", "npc_2_dorian" },
                catalog);

            Assert.AreEqual(1, canon.tradeRequirements.Count);
            Assert.AreEqual("trade_valid", canon.tradeRequirements[0].id);
        }
    }
}
