using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.AreaTrigger;
using Core.Gameplay.Assembly;
using Core.Gameplay.Encounter;
using Xunit;

namespace Tests.Gameplay.Assembly
{
    /// <summary>
    /// ADR-0068 运行时验收（消费方反馈——游戏接入方第十一批）：经真实 <see cref="GameplayAssembly"/>
    /// 装配、真实 <c>area.trigger_def</c>（<c>encounter_start</c>，<c>one_shot: false</c>）与真实
    /// <c>encounter.def</c>，复现并验证"玩家在遭遇触发圈边缘来回穿越，同一遭遇不再出现多个并存的
    /// 进行中实例"——不脱离装配根、不绕过 <see cref="AreaTriggerHost"/> 分发到
    /// <see cref="AreaTriggerOptions.EncounterStartRequested"/> 默认委托再转调
    /// <see cref="IEncounterHost.Start"/> 这条真实生产路径（<see cref="GameplayAssembly"/> 装配第 15
    /// 步）。用直接调用 <see cref="AreaTriggerHost.Evaluate"/> 模拟位置变化——与生产环境下
    /// <c>AreaTriggerTickHandler</c> 在 <c>TickPhase.TriggerEvaluation</c> 阶段驱动的是同一个方法，
    /// 只是跳过了 <c>WorldSim.Tick</c> 的连续步推进外壳，不影响本方法内部真实分发链路的真实性。
    /// </summary>
    public sealed class ADR0068_EncounterStartIdempotentAreaTriggerTests
    {
        private static readonly Id PlayerId = new Id("unit.adr0068_smoke_player");
        private static readonly Id PlayerFactionId = new Id("fac.adr0068_smoke_player");
        private static readonly Id ArchetypeSample = new Id("arch.class.adr0068_smoke_sample");
        private static readonly Id MapId = new Id("world.adr0068_smoke_map");
        private static readonly Id EncounterId = new Id("encounter.adr0068_smoke");
        private static readonly Id TriggerId = new Id("area.adr0068_smoke_trigger");
        private static readonly Id BossTemplateId = new Id("creature.adr0068_smoke_boss");

        // 触发圈：圆心 (0,0) 半径 5。InsidePos 落在圈内，OutsidePos 远在圈外。
        private static readonly Vec2 InsidePos = new Vec2(0, 0);
        private static readonly Vec2 OutsidePos = new Vec2(100, 100);

        private static string Envelope(string table, string rowsJson) =>
            "{\"table\": \"" + table + "\", \"schema_version\": 1, \"rows\": " + rowsJson + "}";

        private sealed class Fixture
        {
            public IEventBus Bus = null!;
            public WorldSim World = null!;
            public GameplayAssembly Gameplay = null!;
        }

        /// <summary>经真实 <c>Carriers.Units</c>（<see cref="Core.Rules.Common.IUnitAccess"/>）统计
        /// 当前世界里模板为 <see cref="BossTemplateId"/> 的单位数量——用于验证"初始单位没有因为重复
        /// 进圈被重复生成"。</summary>
        private static int CountBossUnits(Fixture fx)
        {
            var count = 0;
            foreach (var unitId in fx.Gameplay.Carriers.Units.AllUnits)
            {
                if (fx.Gameplay.Carriers.Units.GetTemplateId(unitId) == BossTemplateId)
                {
                    count++;
                }
            }
            return count;
        }

        private static Fixture Build()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var triggerRows = "[{\"id\": \"" + TriggerId.Value + "\", \"map_id\": \"" + MapId.Value + "\", " +
                "\"shape\": {\"kind\": \"circle\", \"radius\": 5, \"center\": {\"x\": 0, \"y\": 0}}, " +
                "\"trigger_type\": \"encounter_start\", \"one_shot\": false, " +
                "\"params\": {\"encounter_ref\": \"" + EncounterId.Value + "\"}}]";

            var encounterRows = "[{\"id\": \"" + EncounterId.Value + "\", " +
                "\"units\": [{\"template_ref\": \"" + BossTemplateId.Value + "\", \"position\": {\"x\": 1, \"y\": 2}}], " +
                "\"victory_condition\": \"self.is_alive\", \"defeat_condition\": \"target.is_alive\"}]";

            var tierRows = "[{\"id\": \"creature.tier.adr0068_smoke\", \"name_key\": \"l10n.creature.tier.adr0068_smoke.name\"}]";

            var templateRows = "[{\"id\": \"" + BossTemplateId.Value + "\", " +
                "\"name_key\": \"l10n.creature.adr0068_smoke_boss.name\", \"level\": 1, " +
                "\"tier\": \"creature.tier.adr0068_smoke\", \"base_stats\": {}, " +
                "\"faction_id\": \"fac.adr0068_smoke_monster\", \"display_ref\": \"display.adr0068_smoke_boss\"}]";

            // 真实 Carriers.Creatures.Spawn（生产 CreatureFactory，不是本模块单测用的 FakeWorld）在
            // 没有任何 arch.power_type 登记时会回落到"构造期解析好的全部资源类型 id"，空集合会让
            // PowerHost.RegisterUnit 直接抛 ArgumentException（见 CreatureFactory.ResolvePowerTypes
            // 判断记录）——补一条最小的 stat.definition + arch.power_type，让真实 Spawn 走得通。
            var statDefinitionRows =
                "[{\"id\": \"stat.adr0068_smoke_health\", \"name_key\": \"l10n.stat.adr0068_smoke_health.name\", " +
                "\"group\": \"primary\", \"default_base\": 100}]";
            var powerTypeRows =
                "[{\"id\": \"arch.power.adr0068_smoke_health\", \"name_key\": \"l10n.power.adr0068_smoke_health.name\", " +
                "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.adr0068_smoke_health\"}, \"start_full\": true}]";

            var source = new InMemoryDataSource()
                .Add("item.budget_curve",
                    "{\"table\": \"item.budget_curve\", \"schema_version\": 1, \"rows\": " +
                    "[{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}]}")
                .Add("stat.definition", Envelope("stat.definition", statDefinitionRows))
                .Add("arch.power_type", Envelope("arch.power_type", powerTypeRows))
                .Add("combat.hit_table_config", Envelope("combat.hit_table_config", "[]"))
                .Add("combat.resist_curve", Envelope("combat.resist_curve", "[]"))
                .Add(AreaTriggerSchemas.TriggerDef.Name, Envelope(AreaTriggerSchemas.TriggerDef.Name, triggerRows))
                .Add(EncounterSchemas.Def.Name, Envelope(EncounterSchemas.Def.Name, encounterRows))
                .Add(Core.Carriers.Creature.CreatureSchemas.TierDefinition.Name,
                    Envelope(Core.Carriers.Creature.CreatureSchemas.TierDefinition.Name, tierRows))
                .Add(Core.Carriers.Creature.CreatureSchemas.Template.Name,
                    Envelope(Core.Carriers.Creature.CreatureSchemas.Template.Name, templateRows));

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            GameplaySchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var saveSystem = new SaveSystem(new StubFileSystem(), new SaveSystemOptions(new Id("game.adr0068_smoke")));

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => PlayerId,
                playerFactionId: PlayerFactionId);

            // 玩家实体先落在圈外，真实注册进 IWorldSim——GameplayAssembly 第 15 步默认
            // EncounterStartRequested 委托按 world.GetEntity(unitId)?.MapId 解析地图，实体必须真实
            // 存在才能走到真实 mapId（同 GameplayAssemblyTeleportTests.Build 惯例）。
            var player = new PlayerUnit(PlayerId, MapId, PlayerFactionId, ArchetypeSample) { Position = OutsidePos };
            world.AddEntity(player);
            bus.DispatchPending();

            gameplay.EnterMap(MapId, PlayerId);

            return new Fixture { Bus = bus, World = world, Gameplay = gameplay };
        }

        /// <summary>核心验收场景：进圈开始遭遇 → 出圈再进圈（穿越触发圈边缘）→ 进行中实例仍只有
        /// 一个、初始单位没有重复刷、开始事件只发过一次；遭遇结束后再进圈 → 新实例正常开始。</summary>
        [Fact]
        public void PlayerCrossingEncounterStartTrigger_RepeatedEnter_DoesNotDuplicateInstance_ThenRestartsAfterEncounterEnds()
        {
            var fx = Build();
            var startedCount = 0;
            fx.Bus.Subscribe<EncounterStartedEvent>(EncounterEventKeys.Started, _ => startedCount++);

            // 1) 进圈：真正开始一次遭遇，初始单位（1 个 template_ref）真实生成一次。
            fx.Gameplay.AreaTrigger.Evaluate(PlayerId, InsidePos);
            Assert.Equal(1, startedCount);
            Assert.Single(fx.Gameplay.Encounter.ActiveInstanceIds);
            Assert.Equal(1, CountBossUnits(fx));
            var firstInstanceId = fx.Gameplay.Encounter.ActiveInstanceIds[0];

            // 2) 出圈：离开触发范围，不影响已经在进行中的遭遇实例。
            fx.Gameplay.AreaTrigger.Evaluate(PlayerId, OutsidePos);
            Assert.Equal(1, startedCount);
            Assert.Single(fx.Gameplay.Encounter.ActiveInstanceIds);

            // 3) 再进圈：ADR-0068 幂等化——命中已在进行中的同一实例，不新建、不重复发布
            //    EncounterStartedEvent、不重复刷初始单位。
            fx.Gameplay.AreaTrigger.Evaluate(PlayerId, InsidePos);
            Assert.Equal(1, startedCount); // 开始事件仍然只发过一次
            Assert.Equal(1, CountBossUnits(fx)); // 初始单位没有被重复刷
            var activeAfterReentry = fx.Gameplay.Encounter.ActiveInstanceIds;
            Assert.Single(activeAfterReentry); // 仍然只有一个进行中实例，没有并存出第二个
            Assert.Equal(firstInstanceId, activeAfterReentry[0]); // 命中的是同一个实例

            // 4) 再来回穿越几次，进一步证伪"多次穿越会累积出多个实例"。
            fx.Gameplay.AreaTrigger.Evaluate(PlayerId, OutsidePos);
            fx.Gameplay.AreaTrigger.Evaluate(PlayerId, InsidePos);
            fx.Gameplay.AreaTrigger.Evaluate(PlayerId, OutsidePos);
            fx.Gameplay.AreaTrigger.Evaluate(PlayerId, InsidePos);
            Assert.Equal(1, startedCount);
            Assert.Equal(1, CountBossUnits(fx));
            Assert.Single(fx.Gameplay.Encounter.ActiveInstanceIds);
            Assert.Equal(firstInstanceId, fx.Gameplay.Encounter.ActiveInstanceIds[0]);

            // 5) 遭遇结束（Abort 模拟胜利/失败终结，不依赖具体胜负条件求值细节——本用例只关心
            //    "已结束的实例不再挡住重新开始"这一 ADR-0068 决策点）后再进圈：应该正常开出一个
            //    全新实例，而不是永远卡在第一个实例上。
            fx.Gameplay.Encounter.Abort(firstInstanceId);
            fx.Gameplay.AreaTrigger.Evaluate(PlayerId, OutsidePos); // 先真正"离开"一次，清掉 _inside 记录
            fx.Gameplay.AreaTrigger.Evaluate(PlayerId, InsidePos);

            Assert.Equal(2, startedCount); // 新实例真正发布了第二次开始事件
            Assert.Equal(2, CountBossUnits(fx)); // 新实例的初始单位真实生成了第二份（旧实例的单位未被清理，符合 Abort 既有语义：不销毁参战单位）
            Assert.Single(fx.Gameplay.Encounter.ActiveInstanceIds);
            var secondInstanceId = fx.Gameplay.Encounter.ActiveInstanceIds[0];
            Assert.NotEqual(firstInstanceId, secondInstanceId);
        }

        /// <summary><see cref="IEncounterHost.TryStart"/> 两种结果各一例，经真实
        /// <see cref="GameplayAssembly.Encounter"/>（<c>EncounterHost</c> 显式接口实现）。</summary>
        [Fact]
        public void TryStart_ViaRealEncounterHost_FirstStarted_SecondAlreadyActive()
        {
            var fx = Build();
            IEncounterHost host = fx.Gameplay.Encounter;

            var first = host.TryStart(EncounterId, MapId, PlayerId, out var firstInstanceId);
            Assert.Equal(EncounterStartResult.Started, first);

            var second = host.TryStart(EncounterId, MapId, PlayerId, out var secondInstanceId);
            Assert.Equal(EncounterStartResult.AlreadyActive, second);
            Assert.Equal(firstInstanceId, secondInstanceId);
        }
    }
}
