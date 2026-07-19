using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using Rpg.Npc;

namespace Rpg.Npc.Tests.EditMode
{
    // NOTE: runtime interaction registry tests intentionally avoid JsonFileStore dependencies.
    [TestFixture]
    public sealed class InteractionDefinitionRegistryTests
    {
        string _tempDir;
        string _seedPath;
        string _runtimeDir;

        [SetUp]
        public void SetUp()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"rpg-interactions-tests-{Guid.NewGuid():N}");
            _runtimeDir = Path.Combine(_tempDir, "runtime");
            Directory.CreateDirectory(_tempDir);
            _seedPath = Path.Combine(_tempDir, "interactions.seed.json");
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Best effort.
            }
        }

        [Test]
        public void Validator_EngageBeforeMove_ReportsIssue()
        {
            var doc = new InteractionDefinitionsDoc
            {
                interactions = new List<InteractionDefinition>
                {
                    new InteractionDefinition
                    {
                        id = "bad_order",
                        status = "active",
                        roles = new List<string> { "initiator", "target" },
                        phases = new InteractionPhases
                        {
                            start = new List<InteractionActionStep>
                            {
                                new InteractionActionStep
                                {
                                    actionType = InteractionActionTypes.EngageDialogue,
                                    actorRole = "initiator",
                                    targetRole = "target"
                                }
                            }
                        }
                    }
                }
            };

            var issues = new InteractionDefinitionValidator().Validate(doc);

            Assert.IsTrue(issues.Exists(i => i.IndexOf("before any movement", StringComparison.Ordinal) >= 0));
        }

        [Test]
        public void Validator_ValidSeed_ReturnsNoIssues()
        {
            var doc = BuildSeed();
            var validator = new InteractionDefinitionValidator();

            var issues = validator.Validate(doc);

            Assert.IsNotNull(issues);
            Assert.AreEqual(0, issues.Count);
        }

        [Test]
        public void Registry_MergesRuntimeOverrideAndAdditions()
        {
            var seed = BuildSeed();
            File.WriteAllText(_seedPath, JsonConvert.SerializeObject(seed, Formatting.Indented));
            var runtime = new InteractionDefinitionsDoc
            {
                schemaVersion = 1,
                interactions = new List<InteractionDefinition>
                {
                    new InteractionDefinition
                    {
                        id = "steal",
                        displayName = "Steal (Runtime Override)",
                        status = "active",
                        roles = new List<string> { "initiator", "target" },
                        phases = new InteractionPhases
                        {
                            start = new List<InteractionActionStep>
                            {
                                new InteractionActionStep
                                {
                                    actionType = InteractionActionTypes.MoveToNpc,
                                    actorRole = "initiator",
                                    targetRole = "target"
                                }
                            }
                        }
                    },
                    new InteractionDefinition
                    {
                        id = "custom_runtime",
                        displayName = "Custom Runtime",
                        status = "proposed",
                        roles = new List<string> { "initiator", "target" },
                        phases = new InteractionPhases
                        {
                            start = new List<InteractionActionStep>
                            {
                                new InteractionActionStep
                                {
                                    actionType = InteractionActionTypes.MoveToNpc,
                                    actorRole = "initiator",
                                    targetRole = "target"
                                },
                                new InteractionActionStep
                                {
                                    actionType = InteractionActionTypes.EngageDialogue,
                                    actorRole = "initiator",
                                    targetRole = "target"
                                }
                            }
                        }
                    }
                }
            };
            Directory.CreateDirectory(_runtimeDir);
            File.WriteAllText(Path.Combine(_runtimeDir, "interactions_runtime.json"), JsonConvert.SerializeObject(runtime, Formatting.Indented));

            var registry = new InteractionDefinitionRegistry(_seedPath, _runtimeDir);
            var effective = registry.LoadEffective(out var issues);

            Assert.IsNotNull(issues);
            Assert.AreEqual(0, issues.Count);
            Assert.IsNotNull(effective);
            Assert.IsNotNull(effective.interactions);
            Assert.IsTrue(effective.interactions.Exists(i => i != null && i.id == "custom_runtime"));
            var steal = effective.interactions.Find(i => i != null && i.id == "steal");
            Assert.IsNotNull(steal);
            Assert.AreEqual("Steal (Runtime Override)", steal.displayName);
        }

        [Test]
        public void Registry_TryAddOrUpdateProposed_PersistsValidCandidate()
        {
            var seed = BuildSeed();
            File.WriteAllText(_seedPath, JsonConvert.SerializeObject(seed, Formatting.Indented));
            var registry = new InteractionDefinitionRegistry(_seedPath, _runtimeDir);

            var ok = registry.TryAddOrUpdateProposed(
                new InteractionDefinition
                {
                    id = "runtime_bribe",
                    displayName = "Runtime Bribe",
                    roles = new List<string> { "initiator", "target" },
                    phases = new InteractionPhases
                    {
                        start = new List<InteractionActionStep>
                        {
                            new InteractionActionStep
                            {
                                actionType = InteractionActionTypes.MoveToNpc,
                                actorRole = "initiator",
                                targetRole = "target"
                            },
                            new InteractionActionStep
                            {
                                actionType = InteractionActionTypes.ExchangeCoins,
                                actorRole = "initiator",
                                targetRole = "target"
                            }
                        }
                    }
                },
                out var error);

            Assert.IsTrue(ok, error);
            var runtime = registry.LoadRuntime();
            Assert.IsNotNull(runtime);
            Assert.IsTrue(runtime.interactions.Exists(i => i != null && i.id == "runtime_bribe" && i.status == "proposed"));
        }

        [Test]
        public void Registry_TryPromoteProposedToActive_ActivatesRuntimeDefinition()
        {
            var seed = BuildSeed();
            File.WriteAllText(_seedPath, JsonConvert.SerializeObject(seed, Formatting.Indented));
            var registry = new InteractionDefinitionRegistry(_seedPath, _runtimeDir);
            Assert.IsTrue(registry.TryAddOrUpdateProposed(
                new InteractionDefinition
                {
                    id = "runtime_promote",
                    displayName = "Runtime Promote",
                    status = "proposed",
                    roles = new List<string> { "initiator", "target" },
                    phases = new InteractionPhases
                    {
                        start = new List<InteractionActionStep>
                        {
                            new InteractionActionStep
                            {
                                actionType = InteractionActionTypes.MoveToNpc,
                                actorRole = "initiator",
                                targetRole = "target"
                            }
                        }
                    }
                },
                out var addError),
                addError);
            Assert.IsTrue(registry.TryPromoteProposedToActive("runtime_promote", out var promoteError), promoteError);
            var effective = registry.LoadEffective(out var issues);
            Assert.AreEqual(0, issues.Count);
            var promoted = effective.interactions.Find(i => i != null && i.id == "runtime_promote");
            Assert.IsNotNull(promoted);
            Assert.AreEqual("active", promoted.status);
        }

        static InteractionDefinitionsDoc BuildSeed()
        {
            return new InteractionDefinitionsDoc
            {
                schemaVersion = 1,
                atomicActionTypes = new List<string>
                {
                    InteractionActionTypes.MoveToLocation,
                    InteractionActionTypes.MoveToNpc,
                    InteractionActionTypes.MoveToHero,
                    InteractionActionTypes.EngageDialogue,
                    InteractionActionTypes.ExchangeItem,
                    InteractionActionTypes.ExchangeCoins
                },
                interactions = new List<InteractionDefinition>
                {
                    new InteractionDefinition
                    {
                        id = "steal",
                        displayName = "Steal",
                        status = "active",
                        roles = new List<string> { "initiator", "target" },
                        phases = new InteractionPhases
                        {
                            start = new List<InteractionActionStep>
                            {
                                new InteractionActionStep
                                {
                                    actionType = InteractionActionTypes.MoveToNpc,
                                    actorRole = "initiator",
                                    targetRole = "target"
                                },
                                new InteractionActionStep
                                {
                                    actionType = InteractionActionTypes.ExchangeItem,
                                    actorRole = "target",
                                    targetRole = "initiator"
                                }
                            }
                        },
                        outcomes = new List<InteractionOutcome>
                        {
                            new InteractionOutcome { id = "detected", probability = 0.4f },
                            new InteractionOutcome { id = "undetected", probability = 0.6f }
                        },
                        expiry = new InteractionExpiry
                        {
                            type = "immediate_end_after_start"
                        }
                    }
                }
            };
        }
    }
}
