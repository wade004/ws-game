using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Gameplay.AreaTrigger;
using Core.Gameplay.ProgressionBridge;
using Core.Gameplay.WorldState;
using WorldStateHost = Core.Gameplay.WorldState.WorldState;
using Xunit;
using static Tests.Gameplay.ProgressionBridge.ProgressionBridgeTestSupport;

namespace Tests.Gameplay.ProgressionBridge
{
    /// <summary>
    /// T-N4-3（ADR-0033 决策 3；06 第 2.5 节）：<see cref="AreaTriggerDiscoveryXpListener"/> 探索经验
    /// 一次性发放——直接构造 <see cref="AreaTriggerEnteredEvent"/> 经 <see cref="IEventBus"/> 派发驱动
    /// 本监听器，不经过 <c>AreaTriggerHost.Evaluate</c> 的真实范围判定管线（惯例同
    /// <see cref="CreatureDeathXpListenerTests"/>，本模块测试只关心监听器自身的经验/一次性标志逻辑）。
    /// </summary>
    public sealed class AreaTriggerDiscoveryXpListenerTests
    {
        private const string DiscoverySourceRows = @"[
            { ""id"": ""prog.xp_source.discovery"", ""kind"": ""discovery"", ""base_xp"": 1,
              ""base_curve_ref"": ""prog.xp_base_curve.pb"" }
        ]";

        private const string DiscoverySourceWithCustomOnceKeyRows = @"[
            { ""id"": ""prog.xp_source.discovery"", ""kind"": ""discovery"", ""base_xp"": 1,
              ""base_curve_ref"": ""prog.xp_base_curve.pb"", ""once_key"": ""prog.explore.custom_pb"" }
        ]";

        private const string KillOnlySourceRows = @"[
            { ""id"": ""prog.xp_source.kill"", ""kind"": ""kill"", ""base_xp"": 1,
              ""base_curve_ref"": ""prog.xp_base_curve.pb"" }
        ]";

        private static readonly Id TriggerId = new Id("area.pb_grove");
        private static readonly Id DefaultOnceFlagKey = new Id("world.prog.explore.area.pb_grove");

        /// <summary>首次进入：baseAmount=Evaluate(9)=900，探索不接 Δ、<c>equivalent</c> 缺省按 1
        /// 处理，granted=1×900=900（ADR-0033 决策 3）；同时写入一次性标志。</summary>
        [Fact]
        public void OnTriggerEntered_FirstEntry_GrantsBaseAmountForRegionLevel_AndSetsOnceFlag()
        {
            var bus = NewEventBus();
            var registry = MakeProgressionRegistry(bus, DiscoverySourceRows);
            var host = MakeProgressionHost(registry, bus);
            var worldState = new WorldStateHost(bus);

            var playerId = new Id("unit.pb_explorer_1");
            host.RegisterUnit(playerId, new Id("prog.curve.pb"), startLevel: 1);

            var world = NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            AddPlayer(world, playerId, new Id("map.pb"), new Vec2(0, 0));

            AreaDiscoveryLevelResolver resolver = triggerId => triggerId.Equals(TriggerId) ? 9 : (int?)null;

            _ = new AreaTriggerDiscoveryXpListener(bus, host, units, worldState, registry, resolver);

            bus.Enqueue(new AreaTriggerEnteredEvent(TriggerId, playerId));
            bus.DispatchPending();

            Assert.Equal(900, host.GetXp(playerId));
            Assert.True(worldState.Has(DefaultOnceFlagKey));
        }

        /// <summary>验收标准"再次进入不发放"：同一进程内第二次进入同一个触发器，经验不再累加。</summary>
        [Fact]
        public void OnTriggerEntered_SecondEntry_SameTrigger_DoesNotGrantAgain()
        {
            var bus = NewEventBus();
            var registry = MakeProgressionRegistry(bus, DiscoverySourceRows);
            var host = MakeProgressionHost(registry, bus);
            var worldState = new WorldStateHost(bus);

            var playerId = new Id("unit.pb_explorer_2");
            host.RegisterUnit(playerId, new Id("prog.curve.pb"), startLevel: 1);

            var world = NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            AddPlayer(world, playerId, new Id("map.pb"), new Vec2(0, 0));

            AreaDiscoveryLevelResolver resolver = _ => 9;
            _ = new AreaTriggerDiscoveryXpListener(bus, host, units, worldState, registry, resolver);

            bus.Enqueue(new AreaTriggerEnteredEvent(TriggerId, playerId));
            bus.DispatchPending();
            Assert.Equal(900, host.GetXp(playerId));

            bus.Enqueue(new AreaTriggerEnteredEvent(TriggerId, playerId));
            bus.DispatchPending();

            Assert.Equal(900, host.GetXp(playerId));
        }

        /// <summary>验收标准"读档后标志仍在 → 不重复"：新构造一份 <see cref="WorldState"/>，预先写入
        /// 与首次进入时完全相同的标志键（模拟"存档里已经记着这个区域探索过"，读档后 <see
        /// cref="IWorldState"/> 随存档段一起恢复，见本模块 README 判断记录），再用它构造一个全新的
        /// 监听器实例——即便这是该监听器实例第一次收到这个事件，标志已经存在，仍然不发放。</summary>
        [Fact]
        public void OnTriggerEntered_FlagAlreadySetFromLoadedSave_DoesNotGrant()
        {
            var bus = NewEventBus();
            var registry = MakeProgressionRegistry(bus, DiscoverySourceRows);
            var host = MakeProgressionHost(registry, bus);
            var worldState = new WorldStateHost(bus);
            worldState.Set(DefaultOnceFlagKey, Core.Foundation.Expr.ExprValue.OfBool(true), new Id("system.pb_fixture_setup"));

            var playerId = new Id("unit.pb_explorer_3");
            host.RegisterUnit(playerId, new Id("prog.curve.pb"), startLevel: 1);

            var world = NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            AddPlayer(world, playerId, new Id("map.pb"), new Vec2(0, 0));

            AreaDiscoveryLevelResolver resolver = _ => 9;
            _ = new AreaTriggerDiscoveryXpListener(bus, host, units, worldState, registry, resolver);

            bus.Enqueue(new AreaTriggerEnteredEvent(TriggerId, playerId));
            bus.DispatchPending();

            Assert.Equal(0, host.GetXp(playerId));
        }

        /// <summary>只服务玩家单位本人（本模块判断记录"不做召唤物/生物的探索归属"）：生物单位进入
        /// 探索奖励区域不发放，也不写一次性标志（留给真正的玩家进入时才点亮）。</summary>
        [Fact]
        public void OnTriggerEntered_NonPlayerUnit_DoesNotGrant_AndDoesNotSetFlag()
        {
            var bus = NewEventBus();
            var registry = MakeProgressionRegistry(bus, DiscoverySourceRows);
            var host = MakeProgressionHost(registry, bus);
            var worldState = new WorldStateHost(bus);

            var creatureId = new Id("unit.pb_wandering_creature");
            host.RegisterUnit(creatureId, new Id("prog.curve.pb"), startLevel: 1);

            var world = NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            AddCreature(world, creatureId, new Id("map.pb"), new Id("creature.pb_wanderer_tpl"), new Vec2(0, 0));

            AreaDiscoveryLevelResolver resolver = _ => 9;
            _ = new AreaTriggerDiscoveryXpListener(bus, host, units, worldState, registry, resolver);

            bus.Enqueue(new AreaTriggerEnteredEvent(TriggerId, creatureId));
            bus.DispatchPending();

            Assert.Equal(0, host.GetXp(creatureId));
            Assert.False(worldState.Has(DefaultOnceFlagKey));
        }

        /// <summary>解析器对该触发器返回 <c>null</c>（未配置为探索奖励区域）：不发放、不写标志。</summary>
        [Fact]
        public void OnTriggerEntered_ResolverReturnsNull_TriggerNotConfigured_DoesNotGrant()
        {
            var bus = NewEventBus();
            var registry = MakeProgressionRegistry(bus, DiscoverySourceRows);
            var host = MakeProgressionHost(registry, bus);
            var worldState = new WorldStateHost(bus);

            var playerId = new Id("unit.pb_explorer_4");
            host.RegisterUnit(playerId, new Id("prog.curve.pb"), startLevel: 1);

            var world = NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            AddPlayer(world, playerId, new Id("map.pb"), new Vec2(0, 0));

            // 未提供 resolver：默认恒返回 null（见类型注释"零成本退化"）。
            _ = new AreaTriggerDiscoveryXpListener(bus, host, units, worldState, registry);

            bus.Enqueue(new AreaTriggerEnteredEvent(TriggerId, playerId));
            bus.DispatchPending();

            Assert.Equal(0, host.GetXp(playerId));
            Assert.False(worldState.Has(DefaultOnceFlagKey));
        }

        /// <summary>判断记录"来源自身 once_key 优先于 ProgressionOptions.DefaultOnceKeyPrefix"：
        /// 来源记录登记了 <c>once_key: "prog.explore.custom_pb"</c>，标志键应落在
        /// <c>world.prog.explore.custom_pb.&lt;triggerId&gt;</c>，不是默认前缀那一条。</summary>
        [Fact]
        public void OnTriggerEntered_SourceHasCustomOnceKey_UsesSourceOnceKeyPrefix()
        {
            var bus = NewEventBus();
            var registry = MakeProgressionRegistry(bus, DiscoverySourceWithCustomOnceKeyRows);
            var host = MakeProgressionHost(registry, bus);
            var worldState = new WorldStateHost(bus);

            var playerId = new Id("unit.pb_explorer_5");
            host.RegisterUnit(playerId, new Id("prog.curve.pb"), startLevel: 1);

            var world = NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            AddPlayer(world, playerId, new Id("map.pb"), new Vec2(0, 0));

            AreaDiscoveryLevelResolver resolver = _ => 9;
            _ = new AreaTriggerDiscoveryXpListener(bus, host, units, worldState, registry, resolver);

            bus.Enqueue(new AreaTriggerEnteredEvent(TriggerId, playerId));
            bus.DispatchPending();

            var customKey = new Id("world.prog.explore.custom_pb.area.pb_grove");
            Assert.True(worldState.Has(customKey));
            Assert.False(worldState.Has(DefaultOnceFlagKey));
        }

        /// <summary>硬性规则衍生的防御性验收（同 <see
        /// cref="CreatureDeathXpListenerTests.OnUnitDied_KillXpSourceNotRegistered_DoesNotThrow_SkipsSilently"/>）：
        /// <c>prog.xp_source.discovery</c> 未登记时 <c>HasXpSource</c> 返回 <c>false</c>，本监听器
        /// 据此跳过 <c>GrantXp</c> 调用（T-N4-4 附带任务起改为显式查询，不再依赖
        /// <see cref="System.ArgumentException"/> 的 try/catch）——但一次性标志仍然写入
        /// （判断记录"处理过一次"与"是否真的发了经验"分离），避免来源 id 配置补上之后被"追发"。</summary>
        [Fact]
        public void OnTriggerEntered_DiscoveryXpSourceNotRegistered_DoesNotThrow_StillSetsFlag()
        {
            var bus = NewEventBus();
            var registry = MakeProgressionRegistry(bus, KillOnlySourceRows);
            var host = MakeProgressionHost(registry, bus);
            var worldState = new WorldStateHost(bus);

            var playerId = new Id("unit.pb_explorer_6");
            host.RegisterUnit(playerId, new Id("prog.curve.pb"), startLevel: 1);

            var world = NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            AddPlayer(world, playerId, new Id("map.pb"), new Vec2(0, 0));

            AreaDiscoveryLevelResolver resolver = _ => 9;
            _ = new AreaTriggerDiscoveryXpListener(bus, host, units, worldState, registry, resolver);

            bus.Enqueue(new AreaTriggerEnteredEvent(TriggerId, playerId));
            bus.DispatchPending();

            Assert.Equal(0, host.GetXp(playerId));
            Assert.True(worldState.Has(DefaultOnceFlagKey));
        }
    }
}
