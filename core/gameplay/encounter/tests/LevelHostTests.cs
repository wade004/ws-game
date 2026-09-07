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

        // -------------------------------------------------------------
        // AbortForMap（GP-04：LeaveMap 未终止旧地图关卡运行的复现与回归）
        // -------------------------------------------------------------

        /// <summary>GP-04 复现：不终止关卡运行时，离开地图后若旧遭遇实例迟到的 Won 事件仍会被
        /// <see cref="LevelHost"/> 的订阅接住，误以为"上一遭遇打赢了"而自动开始序列里的下一个——
        /// <see cref="LevelHost.AbortForMap"/> 必须释放该订阅，之后同一个 Won 事件不再推进关卡。</summary>
        [Fact]
        public void AbortForMap_MatchingMap_DisposesSubscription_LateWonEventNoLongerAdvances()
        {
            var (levelHost, encounterHost, world, _, bus) = MakeHosts();
            levelHost.StartLevel(new Id("encounter.level.sample"), Player);
            var firstInstanceId = Assert.Single(encounterHost.ActiveInstanceIds);
            Assert.Single(world.SpawnCalls);

            levelHost.AbortForMap(MapA);

            // 模拟"迟到"的 Won 事件（真实场景里对应 GameplayAssembly.LeaveMap 已经调用了
            // EncounterHost.AbortForMap，不会再有新 Won 产生；这里直接发事件是为了单独验证
            // LevelHost 这一侧的订阅确实已经断开，不依赖 EncounterHost 那一侧的实现细节）。
            bus.PublishImmediate(new EncounterWonEvent(firstInstanceId));

            Assert.Single(world.SpawnCalls); // 没有第二次 Start——序列没有被推进
        }

        [Fact]
        public void AbortForMap_UnrelatedMap_DoesNotAffectActiveRun()
        {
            var (levelHost, encounterHost, world, exprFactory, _) = MakeHosts();
            levelHost.StartLevel(new Id("encounter.level.sample"), Player);

            levelHost.AbortForMap(new Id("map.unrelated"));

            var firstInstanceId = Assert.Single(encounterHost.ActiveInstanceIds);
            exprFactory.Set("self.is_alive", true);
            encounterHost.Evaluate(firstInstanceId);

            Assert.Equal(2, world.SpawnCalls.Count); // 关卡运行未受影响，仍能正常推进到第二个遭遇
        }

        [Fact]
        public void AbortForMap_NoActiveRun_DoesNotThrow()
        {
            var (levelHost, _, _, _, _) = MakeHosts();

            var ex = Record.Exception(() => levelHost.AbortForMap(MapA));

            Assert.Null(ex);
        }
    }
}
