using System;
using System.Linq;
using Core.Carriers.Common;
using Core.Carriers.Item;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Gameplay.Common;
using Core.Gameplay.Encounter;
using Xunit;

namespace Tests.Gameplay.Encounter
{
    public class EncounterHostTests
    {
        private static readonly Id MapA = new Id("map.sample_dungeon");
        private static readonly Id MapB = new Id("map.sample_other_dungeon");
        private static readonly Id Player = new Id("unit.sample_player");

        private const string DefRows = "[" +
            "{\"id\": \"encounter.sample_basic\", \"units\": [{\"template_ref\": \"creature.sample_boss\", \"position\": {\"x\": 1, \"y\": 2}}], " +
            "\"victory_condition\": \"self.is_alive\", \"defeat_condition\": \"target.is_alive\", \"rewards\": {\"xp\": 100}}," +

            "{\"id\": \"encounter.sample_spawnref\", \"units\": [{\"spawn_ref\": \"spawn.sample_group\"}], " +
            "\"victory_condition\": \"self.is_alive\", \"defeat_condition\": \"target.is_alive\"}," +

            "{\"id\": \"encounter.sample_waves\", \"units\": [{\"template_ref\": \"creature.sample_boss\", \"position\": {\"x\": 0, \"y\": 0}}], " +
            "\"waves\": [{\"trigger_condition\": \"self.in_combat\", \"spawn_refs\": [\"spawn.sample_add\"]}], " +
            "\"victory_condition\": \"self.is_alive\", \"defeat_condition\": \"target.is_alive\"}," +

            "{\"id\": \"encounter.sample_phases_template\", \"units\": [{\"template_ref\": \"creature.sample_boss\", \"position\": {\"x\": 0, \"y\": 0}}], " +
            "\"phases\": [" +
            "{\"enter_condition\": \"combat.in_combat\", \"ai_rotation_override\": {\"creature.sample_boss\": \"ai.rotation.phase2\"}, \"on_enter_hook\": \"hook.sample_phase2\"}," +
            "{\"enter_condition\": \"combat.is_casting\", \"ai_rotation_override\": {}}" +
            "], \"victory_condition\": \"self.is_alive\", \"defeat_condition\": \"target.is_alive\"}," +

            "{\"id\": \"encounter.sample_phases_unit\", \"units\": [{\"template_ref\": \"creature.sample_boss\", \"position\": {\"x\": 0, \"y\": 0}}], " +
            "\"phases\": [{\"enter_condition\": \"combat.in_combat\", \"ai_rotation_override\": {\"creature.inst_1\": \"ai.rotation.phase2\"}}], " +
            "\"victory_condition\": \"self.is_alive\", \"defeat_condition\": \"target.is_alive\"}," +

            "{\"id\": \"encounter.sample_arena\", \"units\": [{\"template_ref\": \"creature.sample_boss\", \"position\": {\"x\": 0, \"y\": 0}}], " +
            "\"arena_rules\": {\"bounds_shape\": {\"kind\": \"circle\", \"origin\": {\"x\": 0, \"y\": 0}, \"radius\": 10}, \"reset_if_leave\": true}, " +
            "\"victory_condition\": \"self.is_alive\", \"defeat_condition\": \"target.is_alive\"}" +
            "]";

        private static EncounterHost MakeHost(
            out FakeWorld world,
            out FakeAiHost ai,
            out FakeHookRegistry hooks,
            out FakeRewardDispatcher rewards,
            out FakeExprHostFactory exprFactory,
            out Core.Foundation.EventBus.IEventBus bus)
        {
            bus = TestSupport.CreateBus();
            var registry = TestSupport.MakeRegistry(bus, DefRows);
            world = new FakeWorld();
            ai = new FakeAiHost();
            hooks = new FakeHookRegistry();
            rewards = new FakeRewardDispatcher();
            exprFactory = new FakeExprHostFactory();

            return new EncounterHost(
                registry, bus, world, ai, hooks, exprFactory, rewards, world,
                world.SpawnFromRequester);
        }

        // -------------------------------------------------------------
        // Start
        // -------------------------------------------------------------

        [Fact]
        public void Start_TemplateUnits_SpawnsAndPublishesStarted()
        {
            var host = MakeHost(out var world, out _, out _, out _, out _, out var bus);
            EncounterStartedEvent? captured = null;
            bus.Subscribe<EncounterStartedEvent>(EncounterEventKeys.Started, evt => captured = evt);

            var instanceId = host.Start(new Id("encounter.sample_basic"), MapA, Player);

            Assert.Single(world.SpawnCalls);
            Assert.Equal(new Id("creature.sample_boss"), world.SpawnCalls[0].TemplateId);
            Assert.Equal(new Vec2(1, 2), world.SpawnCalls[0].Position);
            Assert.NotNull(captured);
            Assert.Equal(instanceId, captured!.EncounterId);
        }

        [Fact]
        public void Start_SpawnRefUnits_UsesSpawnRequester()
        {
            var host = MakeHost(out var world, out _, out _, out _, out _, out _);

            host.Start(new Id("encounter.sample_spawnref"), MapA, Player);

            Assert.Single(world.SpawnRequesterCalls);
            Assert.Equal(new Id("spawn.sample_group"), world.SpawnRequesterCalls[0].SpawnId);
            Assert.Equal(MapA, world.SpawnRequesterCalls[0].MapId);
        }

        [Fact]
        public void Start_UnknownEncounter_Throws()
        {
            var host = MakeHost(out _, out _, out _, out _, out _, out _);

            Assert.Throws<ArgumentException>(() => host.Start(new Id("encounter.sample_nonexistent"), MapA, Player));
        }

        [Fact]
        public void Start_ReturnsDistinctIncrementingInstanceIds()
        {
            var host = MakeHost(out _, out _, out _, out _, out _, out _);

            var first = host.Start(new Id("encounter.sample_basic"), MapA, Player);
            var second = host.Start(new Id("encounter.sample_basic"), MapA, Player);

            Assert.NotEqual(first, second);
        }

        [Fact]
        public void ActiveInstanceIds_IncludesStartedInstance()
        {
            var host = MakeHost(out _, out _, out _, out _, out _, out _);

            var instanceId = host.Start(new Id("encounter.sample_basic"), MapA, Player);

            Assert.Contains(instanceId, host.ActiveInstanceIds);
        }

        // -------------------------------------------------------------
        // 波次
        // -------------------------------------------------------------

        [Fact]
        public void Evaluate_WaveTriggerTrue_SpawnsWave_PublishesWaveSpawned_OnlyOnce()
        {
            var host = MakeHost(out var world, out _, out _, out _, out var exprFactory, out var bus);
            var instanceId = host.Start(new Id("encounter.sample_waves"), MapA, Player);
            var waveSpawnedCount = 0;
            bus.Subscribe<EncounterWaveSpawnedEvent>(EncounterEventKeys.WaveSpawned, _ => waveSpawnedCount++);
            exprFactory.Set("self.in_combat", true);

            host.Evaluate(instanceId);
            host.Evaluate(instanceId); // 第二次求值不应重复触发同一波次

            Assert.Equal(1, waveSpawnedCount);
            // 初始 1 个 template 单位 + 波次生成 1 个 spawn_ref 单位。
            Assert.Single(world.SpawnRequesterCalls);
        }

        [Fact]
        public void Evaluate_WaveTriggerFalse_DoesNotSpawn()
        {
            var host = MakeHost(out var world, out _, out _, out _, out _, out _);
            var instanceId = host.Start(new Id("encounter.sample_waves"), MapA, Player);

            host.Evaluate(instanceId);

            Assert.Empty(world.SpawnRequesterCalls);
        }

        // -------------------------------------------------------------
        // 阶段：换 Rotation + 钩子 + 事件
        // -------------------------------------------------------------

        [Fact]
        public void Evaluate_PhaseEnterTrue_TemplateKey_SetsRotationForAllMatchingUnits_InvokesHook_PublishesPhaseChanged()
        {
            var host = MakeHost(out _, out var ai, out var hooks, out _, out var exprFactory, out var bus);
            var instanceId = host.Start(new Id("encounter.sample_phases_template"), MapA, Player);
            EncounterPhaseChangedEvent? captured = null;
            bus.Subscribe<EncounterPhaseChangedEvent>(EncounterEventKeys.PhaseChanged, evt => captured = evt);
            exprFactory.Set("combat.in_combat", true);

            host.Evaluate(instanceId);

            Assert.Single(ai.RotationChanges);
            Assert.Equal(new Id("creature.inst_1"), ai.RotationChanges[0].UnitId);
            Assert.Equal(new Id("ai.rotation.phase2"), ai.RotationChanges[0].RotationId);
            Assert.Single(hooks.InvokedHookIds);
            Assert.Equal(new Id("hook.sample_phase2"), hooks.InvokedHookIds[0]);
            Assert.NotNull(captured);
            Assert.Equal(-1, captured!.OldPhase);
            Assert.Equal(0, captured.NewPhase);
        }

        [Fact]
        public void Evaluate_PhaseEnterTrue_ExactUnitIdKey_SetsRotationForThatUnit()
        {
            var host = MakeHost(out _, out var ai, out _, out _, out var exprFactory, out _);
            var instanceId = host.Start(new Id("encounter.sample_phases_unit"), MapA, Player);
            exprFactory.Set("combat.in_combat", true);

            host.Evaluate(instanceId);

            Assert.Single(ai.RotationChanges);
            Assert.Equal(new Id("creature.inst_1"), ai.RotationChanges[0].UnitId);
        }

        [Fact]
        public void Evaluate_PhaseAdvance_AtMostOnePerCall()
        {
            var host = MakeHost(out _, out _, out _, out _, out var exprFactory, out _);
            var instanceId = host.Start(new Id("encounter.sample_phases_template"), MapA, Player);
            // 两个阶段条件同时为真。
            exprFactory.Set("combat.in_combat", true);
            exprFactory.Set("combat.is_casting", true);

            host.Evaluate(instanceId);
            Assert.Equal(0, host.GetState(instanceId).CurrentPhaseIndex);

            host.Evaluate(instanceId);
            Assert.Equal(1, host.GetState(instanceId).CurrentPhaseIndex);
        }

        [Fact]
        public void Evaluate_PhaseNotYetSatisfied_StaysAtInitialIndex()
        {
            var host = MakeHost(out _, out _, out _, out _, out _, out _);
            var instanceId = host.Start(new Id("encounter.sample_phases_template"), MapA, Player);

            host.Evaluate(instanceId);

            Assert.Equal(-1, host.GetState(instanceId).CurrentPhaseIndex);
        }

        // -------------------------------------------------------------
        // 胜负
        // -------------------------------------------------------------

        [Fact]
        public void Evaluate_VictoryTrue_GrantsRewards_PublishesWon_Deactivates()
        {
            var host = MakeHost(out _, out _, out _, out var rewards, out var exprFactory, out var bus);
            var instanceId = host.Start(new Id("encounter.sample_basic"), MapA, Player);
            EncounterWonEvent? captured = null;
            bus.Subscribe<EncounterWonEvent>(EncounterEventKeys.Won, evt => captured = evt);
            exprFactory.Set("self.is_alive", true);

            host.Evaluate(instanceId);

            Assert.Single(rewards.Grants);
            Assert.Equal(Player, rewards.Grants[0].UnitId);
            Assert.Equal(100, rewards.Grants[0].Bundle.Xp);
            Assert.NotNull(captured);
            Assert.False(host.GetState(instanceId).IsActive);
        }

        [Fact]
        public void Evaluate_DefeatTrue_PublishesLost_NoRewards()
        {
            var host = MakeHost(out _, out _, out _, out var rewards, out var exprFactory, out var bus);
            var instanceId = host.Start(new Id("encounter.sample_basic"), MapA, Player);
            EncounterLostEvent? captured = null;
            bus.Subscribe<EncounterLostEvent>(EncounterEventKeys.Lost, evt => captured = evt);
            exprFactory.Set("target.is_alive", true);

            host.Evaluate(instanceId);

            Assert.Empty(rewards.Grants);
            Assert.NotNull(captured);
            Assert.False(host.GetState(instanceId).IsActive);
        }

        /// <summary>
        /// C04 复现与根治（architecture/落地计划/audit-7e63d66-20260907/code-review.md）：胜负判定
        /// 成立但物品奖励因背包已满（<c>InventoryFullPolicy.Reject</c>）而 <c>Grant</c> 返回 false
        /// ——旧实现仍然先置 <c>IsActive=false</c>、发布 <see cref="EncounterWonEvent"/>，奖励永久
        /// 丢失、玩家没有补领入口。根治后：Grant 失败时实例保持 <c>IsActive=true</c>、不发布
        /// <see cref="EncounterWonEvent"/>；下一次 <see cref="EncounterHost.Evaluate"/>（胜负条件
        /// 不变，仍为真）会自动重试——玩家清出背包空间后，下次 Evaluate 恰好成功发放一次奖励并
        /// 正式终结实例。用真实 <see cref="Core.Carriers.Item.InventoryHost"/>
        /// （<c>MaxSlots=1</c>、<c>FullPolicy=Reject</c>，先塞满一个不同模板的物品占掉唯一格子）+
        /// 真实 <see cref="RewardDispatcher"/>，不用 <see cref="FakeRewardDispatcher"/>（后者恒
        /// 成功，测不出这个问题）。
        /// </summary>
        [Fact]
        public void Evaluate_VictoryTrue_RewardGrantFails_KeepsActiveAndRetries_GrantsExactlyOnceAfterRoomFreed()
        {
            const string RewardEncounterId = "encounter.sample_item_reward";
            var defRows = "[{\"id\": \"" + RewardEncounterId + "\", " +
                "\"units\": [{\"template_ref\": \"creature.sample_boss\", \"position\": {\"x\": 0, \"y\": 0}}], " +
                "\"victory_condition\": \"self.is_alive\", \"defeat_condition\": \"target.is_alive\", " +
                "\"rewards\": {\"items\": [{\"itemId\": \"item.sample_reward\", \"count\": 1}]}}]";

            var bus = TestSupport.CreateBus();
            var registry = TestSupport.MakeRegistry(bus, defRows);
            var world = new FakeWorld();
            var ai = new FakeAiHost();
            var hooks = new FakeHookRegistry();
            var exprFactory = new FakeExprHostFactory();

            var itemBus = new EventBus(EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(CarriersEventKeys.ItemAdded, "item", new[] { "unitId", "itemInstanceId", "itemTemplateId", "count" }),
                new EventDefinition(CarriersEventKeys.ItemRemoved, "item", new[] { "unitId", "itemInstanceId", "count", "reason" }),
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data", new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data", new[] { "errorCount", "warningCount" }),
            }));
            string Table(string name, string rows) => "{\"table\":\"" + name + "\",\"schema_version\":1,\"rows\":" + rows + "}";
            var itemSource = new InMemoryDataSource()
                .Add("item.slot_definition", Table("item.slot_definition", "[{\"id\":\"item.slot.consumable\",\"name_key\":\"l10n.slot.consumable\"}]"))
                .Add("item.quality_definition", Table("item.quality_definition", "[{\"id\":\"item.quality.common\",\"name_key\":\"l10n.quality.common\"}]"))
                .Add("item.template", Table("item.template", "["
                    + "{\"id\":\"item.filler\",\"slot\":\"item.slot.consumable\",\"quality\":\"item.quality.common\",\"item_level\":1,\"display_ref\":\"display.item.filler\",\"stack_size\":1,\"name_key\":\"l10n.item.filler\"},"
                    + "{\"id\":\"item.sample_reward\",\"slot\":\"item.slot.consumable\",\"quality\":\"item.quality.common\",\"item_level\":1,\"display_ref\":\"display.item.sample_reward\",\"stack_size\":1,\"name_key\":\"l10n.item.sample_reward\"}]"));
            var itemRegistry = new DataRegistry(itemSource, itemBus);
            itemRegistry.RegisterSchema(ItemSchemas.Template);
            itemRegistry.RegisterSchema(ItemSchemas.SlotDefinition);
            itemRegistry.RegisterSchema(ItemSchemas.QualityDefinition);
            var itemReport = itemRegistry.LoadAll();
            Assert.False(itemReport.IsBlocking, string.Join(";", itemReport.Issues.Select(i => i.ToString())));

            var inventory = new InventoryHost(itemRegistry, itemBus, new InventoryOptions { MaxSlots = 1, FullPolicy = InventoryFullPolicy.Reject });
            inventory.AddItem(Player, new Id("item.filler"), 1); // 唯一格子被占满，奖励物品完全放不下。
            var rewardDispatcher = new RewardDispatcher(inventory: inventory);

            var host = new EncounterHost(registry, bus, world, ai, hooks, exprFactory, rewardDispatcher, world, world.SpawnFromRequester);
            var instanceId = host.Start(new Id(RewardEncounterId), MapA, Player);
            var wonCount = 0;
            bus.Subscribe<EncounterWonEvent>(EncounterEventKeys.Won, _ => wonCount++);
            exprFactory.Set("self.is_alive", true);

            // 第一次 Evaluate：胜负条件成立，但 Grant 因背包已满失败——实例应保持活跃，不发 Won。
            host.Evaluate(instanceId);
            Assert.True(host.GetState(instanceId).IsActive);
            Assert.Equal(0, wonCount);
            Assert.Equal(0, inventory.CountOf(Player, new Id("item.sample_reward")));

            // 再来一次（背包状态未变）：仍然失败，仍然保持活跃——不会"半途"终结或重复扣状态。
            host.Evaluate(instanceId);
            Assert.True(host.GetState(instanceId).IsActive);
            Assert.Equal(0, wonCount);

            // 玩家清出空间：移除占位物品。
            var fillerInstanceId = inventory.ListItems(Player).Single(i => i.TemplateId.Equals(new Id("item.filler"))).InstanceId;
            inventory.RemoveItem(Player, fillerInstanceId, 1);

            // 下一次 Evaluate：Grant 成功，实例恰好终结一次、恰好发一次 Won、恰好拿到一次奖励物品。
            host.Evaluate(instanceId);
            Assert.False(host.GetState(instanceId).IsActive);
            Assert.Equal(1, wonCount);
            Assert.Equal(1, inventory.CountOf(Player, new Id("item.sample_reward")));

            // 再评价已结束的实例：不应重复发放/重复发事件（Evaluate 对非活跃实例是空操作）。
            host.Evaluate(instanceId);
            Assert.Equal(1, wonCount);
            Assert.Equal(1, inventory.CountOf(Player, new Id("item.sample_reward")));
        }

        [Fact]
        public void Evaluate_InactiveInstance_IsNoOp()
        {
            var host = MakeHost(out _, out _, out _, out var rewards, out var exprFactory, out _);
            var instanceId = host.Start(new Id("encounter.sample_basic"), MapA, Player);
            exprFactory.Set("self.is_alive", true);
            host.Evaluate(instanceId); // Won -> inactive
            rewards.Grants.Clear();

            host.Evaluate(instanceId); // 不应再次结算

            Assert.Empty(rewards.Grants);
        }

        // -------------------------------------------------------------
        // 场地规则：reset_if_leave
        // -------------------------------------------------------------

        [Fact]
        public void Evaluate_PlayerLeavesArenaBounds_DespawnsAndRespawnsUnits_ResetsPhaseAndWaves()
        {
            var host = MakeHost(out var world, out _, out _, out _, out _, out _);
            var instanceId = host.Start(new Id("encounter.sample_arena"), MapA, Player);
            var spawnedUnitId = new Id("creature.inst_1");
            world.SetPlayerPosition(Player, new Vec2(100, 100)); // 半径 10 的圆外

            host.Evaluate(instanceId);

            Assert.Contains(world.DespawnCalls, c => c.EntityId.Equals(spawnedUnitId));
            Assert.Equal(2, world.SpawnCalls.Count); // 初始 1 次 + 复位后重新 Start 1 次
            Assert.Equal(-1, host.GetState(instanceId).CurrentPhaseIndex);
        }

        [Fact]
        public void Evaluate_PlayerInsideArenaBounds_NoReset()
        {
            var host = MakeHost(out var world, out _, out _, out _, out _, out _);
            var instanceId = host.Start(new Id("encounter.sample_arena"), MapA, Player);
            world.SetPlayerPosition(Player, new Vec2(1, 1)); // 半径 10 的圆内

            host.Evaluate(instanceId);

            Assert.Empty(world.DespawnCalls);
            Assert.Single(world.SpawnCalls);
        }

        // -------------------------------------------------------------
        // Abort / GetState
        // -------------------------------------------------------------

        [Fact]
        public void Abort_MarksInactive_SubsequentEvaluateIsNoOp()
        {
            var host = MakeHost(out _, out _, out _, out var rewards, out var exprFactory, out _);
            var instanceId = host.Start(new Id("encounter.sample_basic"), MapA, Player);
            exprFactory.Set("self.is_alive", true);

            host.Abort(instanceId);
            host.Evaluate(instanceId);

            Assert.False(host.GetState(instanceId).IsActive);
            Assert.Empty(rewards.Grants);
        }

        // -------------------------------------------------------------
        // AbortForMap（GP-04：LeaveMap 未终止旧地图遭遇的复现与回归）
        // -------------------------------------------------------------

        /// <summary>GP-04 复现的核心断言：不按地图终止时，A 图的遭遇会在切到 B 图后继续被
        /// <see cref="EncounterHost.Evaluate"/> 求值（例如误发奖励）；<see cref="EncounterHost.AbortForMap"/>
        /// 必须只终止目标地图的实例，同图/异图的其它实例不受影响。</summary>
        [Fact]
        public void AbortForMap_MarksOnlyMatchingMapInstancesInactive_LeavesOthersUntouched()
        {
            var host = MakeHost(out _, out _, out _, out var rewards, out var exprFactory, out _);
            var instanceOnA = host.Start(new Id("encounter.sample_basic"), MapA, Player);
            var otherOnA = host.Start(new Id("encounter.sample_spawnref"), MapA, Player);
            var instanceOnB = host.Start(new Id("encounter.sample_basic"), MapB, Player);

            var aborted = host.AbortForMap(MapA);

            Assert.Equal(new[] { instanceOnA, otherOnA }, aborted);
            Assert.False(host.GetState(instanceOnA).IsActive);
            Assert.False(host.GetState(otherOnA).IsActive);
            Assert.True(host.GetState(instanceOnB).IsActive);
            Assert.Contains(instanceOnB, host.ActiveInstanceIds);
            Assert.DoesNotContain(instanceOnA, host.ActiveInstanceIds);
            Assert.DoesNotContain(otherOnA, host.ActiveInstanceIds);

            // 复现 GP-04 的直接后果：终止之后，即使 victory_condition 变为 true，
            // 已终止的 A 图实例也不应该再触发 Evaluate 副作用（发奖励/发事件）。
            exprFactory.Set("self.is_alive", true);
            host.Evaluate(instanceOnA);
            Assert.Empty(rewards.Grants);
        }

        [Fact]
        public void AbortForMap_UnknownMap_ReturnsEmptyAndDoesNotThrow()
        {
            var host = MakeHost(out _, out _, out _, out _, out _, out _);
            host.Start(new Id("encounter.sample_basic"), MapA, Player);

            var aborted = host.AbortForMap(new Id("map.never_used"));

            Assert.Empty(aborted);
            Assert.Single(host.ActiveInstanceIds);
        }

        [Fact]
        public void AbortForMap_CalledTwice_SecondCallReturnsEmpty()
        {
            var host = MakeHost(out _, out _, out _, out _, out _, out _);
            host.Start(new Id("encounter.sample_basic"), MapA, Player);

            var first = host.AbortForMap(MapA);
            var second = host.AbortForMap(MapA);

            Assert.Single(first);
            Assert.Empty(second);
        }

        [Fact]
        public void GetState_UnknownInstance_Throws()
        {
            var host = MakeHost(out _, out _, out _, out _, out _, out _);

            Assert.Throws<ArgumentException>(() => host.GetState(new Id("encounter.inst_999")));
        }

        [Fact]
        public void ActiveInstanceIds_ExcludesWonInstance()
        {
            var host = MakeHost(out _, out _, out _, out _, out var exprFactory, out _);
            var instanceId = host.Start(new Id("encounter.sample_basic"), MapA, Player);
            exprFactory.Set("self.is_alive", true);

            host.Evaluate(instanceId);

            Assert.DoesNotContain(instanceId, host.ActiveInstanceIds);
        }
    }
}
