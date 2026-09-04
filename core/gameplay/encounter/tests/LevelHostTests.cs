using System;
using Core.Foundation.Common;
using Core.Gameplay.Encounter;
using Xunit;

namespace Tests.Gameplay.Encounter
{
    public class LevelHostTests
    {
        private static readonly Id MapA = new Id("map.sample_dungeon");
        private static readonly Id Player = new Id("unit.sample_player");

        private const string DefRows = "[" +
            "{\"id\": \"encounter.sample_basic\", \"units\": [{\"template_ref\": \"creature.sample_boss\", \"position\": {\"x\": 0, \"y\": 0}}], " +
            "\"victory_condition\": \"self.is_alive\", \"defeat_condition\": \"target.is_alive\"}," +
            "{\"id\": \"encounter.sample_second\", \"units\": [{\"template_ref\": \"creature.sample_boss2\", \"position\": {\"x\": 0, \"y\": 0}}], " +
            "\"victory_condition\": \"self.is_alive\", \"defeat_condition\": \"target.is_alive\"}" +
            "]";

        private const string LevelRows = "[" +
            "{\"id\": \"encounter.level.sample\", \"map_ref\": \"map.sample_dungeon\", " +
            "\"encounter_sequence\": [\"encounter.sample_basic\", \"encounter.sample_second\"], " +
            "\"entry_difficulty_options\": [\"diff.sample_normal\", \"diff.sample_hard\"]}" +
            "]";

        private static (LevelHost LevelHost, EncounterHost EncounterHost, FakeWorld World, FakeExprHostFactory ExprFactory, Core.Foundation.EventBus.IEventBus Bus) MakeHosts()
        {
            var bus = TestSupport.CreateBus();
            var registry = TestSupport.MakeRegistry(bus, DefRows, LevelRows);
            var world = new FakeWorld();
            var ai = new FakeAiHost();
            var hooks = new FakeHookRegistry();
            var rewards = new FakeRewardDispatcher();
            var exprFactory = new FakeExprHostFactory();

            var encounterHost = new EncounterHost(registry, bus, world, ai, hooks, exprFactory, rewards, world, world.SpawnFromRequester);
            var levelHost = new LevelHost(registry, encounterHost, bus);

            return (levelHost, encounterHost, world, exprFactory, bus);
        }

        [Fact]
        public void StartLevel_StartsFirstEncounterInSequence()
        {
            var (levelHost, _, world, _, _) = MakeHosts();

            levelHost.StartLevel(new Id("encounter.level.sample"), Player);

            Assert.Single(world.SpawnCalls);
            Assert.Equal(new Id("creature.sample_boss"), world.SpawnCalls[0].TemplateId);
        }

        [Fact]
        public void EncounterWon_AdvancesToNextEncounterInSequence()
        {
            var (levelHost, encounterHost, world, exprFactory, _) = MakeHosts();
            levelHost.StartLevel(new Id("encounter.level.sample"), Player);
            var firstInstanceId = Assert.Single(encounterHost.ActiveInstanceIds);

            exprFactory.Set("self.is_alive", true);
            encounterHost.Evaluate(firstInstanceId);

            Assert.Equal(2, world.SpawnCalls.Count);
            Assert.Equal(new Id("creature.sample_boss2"), world.SpawnCalls[1].TemplateId);
        }

        [Fact]
        public void LastEncounterWon_ClearsActiveRun_NoFurtherEncounterStarted()
        {
            var (levelHost, encounterHost, world, exprFactory, _) = MakeHosts();
            levelHost.StartLevel(new Id("encounter.level.sample"), Player);
            exprFactory.Set("self.is_alive", true);

            var firstInstanceId = Assert.Single(encounterHost.ActiveInstanceIds);
            encounterHost.Evaluate(firstInstanceId); // -> 第二个遭遇开始

            var secondInstanceId = Assert.Single(encounterHost.ActiveInstanceIds);
            encounterHost.Evaluate(secondInstanceId); // -> 第二个也胜利，序列结束

            Assert.Equal(2, world.SpawnCalls.Count); // 没有第三次 Start
            Assert.Empty(encounterHost.ActiveInstanceIds);
        }

        [Fact]
        public void WonEventForUnrelatedInstance_DoesNotAdvanceLevel()
        {
            var (levelHost, encounterHost, world, _, bus) = MakeHosts();
            levelHost.StartLevel(new Id("encounter.level.sample"), Player);

            // 一个与本次关卡运行无关的 Won 事件（比如另一处直接调用 EncounterHost.Start 又胜利）。
            bus.PublishImmediate(new EncounterWonEvent(new Id("encounter.inst_999")));

            Assert.Single(world.SpawnCalls); // 仍停留在第一个遭遇，未被无关事件推进
        }

        [Fact]
        public void StartLevel_UnknownLevel_Throws()
        {
            var (levelHost, _, _, _, _) = MakeHosts();

            Assert.Throws<ArgumentException>(() => levelHost.StartLevel(new Id("encounter.level.sample_nonexistent"), Player));
        }

        [Fact]
        public void GetEntryDifficultyOptions_ReturnsConfiguredList()
        {
            var (levelHost, _, _, _, _) = MakeHosts();

            var options = levelHost.GetEntryDifficultyOptions(new Id("encounter.level.sample"));

            Assert.Equal(new[] { new Id("diff.sample_normal"), new Id("diff.sample_hard") }, options);
        }

        [Fact]
        public void GetEntryDifficultyOptions_UnknownLevel_Throws()
        {
            var (levelHost, _, _, _, _) = MakeHosts();

            Assert.Throws<ArgumentException>(() => levelHost.GetEntryDifficultyOptions(new Id("encounter.level.sample_nonexistent")));
        }
    }
}
