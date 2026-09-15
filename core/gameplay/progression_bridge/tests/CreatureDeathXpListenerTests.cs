using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Gameplay.ProgressionBridge;
using Core.Numbers.Progression;
using Core.Rules.Common;
using Xunit;
using static Tests.Gameplay.ProgressionBridge.ProgressionBridgeTestSupport;

namespace Tests.Gameplay.ProgressionBridge
{
    /// <summary>
    /// T-N4-3（ADR-0033 决策 3；06 第 2.5 节）：<see cref="CreatureDeathXpListener"/> 击杀经验发放
    /// （含召唤归属）——直接构造 <see cref="UnitDiedEvent"/> 经 <see cref="IEventBus"/> 派发驱动本
    /// 监听器，不经过 <c>CombatHost</c> 的真实死亡结算管线（惯例同
    /// <c>core/gameplay/loot/tests/T_N2_8b_CreatureDeathSourceLevelTests.cs</c>）。
    /// </summary>
    public sealed class CreatureDeathXpListenerTests
    {
        private const string KillSourceRows = @"[
            { ""id"": ""prog.xp_source.kill"", ""kind"": ""kill"", ""base_xp"": 1,
              ""base_curve_ref"": ""prog.xp_base_curve.pb"", ""level_diff_ref"": ""combat.level_diff.pb"" }
        ]";

        private const string DiscoveryOnlySourceRows = @"[
            { ""id"": ""prog.xp_source.discovery"", ""kind"": ""discovery"", ""base_xp"": 1,
              ""base_curve_ref"": ""prog.xp_base_curve.pb"" }
        ]";

        /// <summary>击杀 · 用例 1（Δ=+5，怪物比玩家高 5 级）：baseAmount=Evaluate(6)=600，
        /// xp_factor(Δ=5)=1.5，granted=600×1×1.5=900（T-N4-2 公式，见 IProgressionHost.GrantXp
        /// 判断记录）。</summary>
        [Fact]
        public void OnUnitDied_PlayerKillsCreature_GrantsBaseAmountTimesDeltaFactor()
        {
            var bus = NewEventBus();
            var registry = MakeProgressionRegistry(bus, KillSourceRows);
            var host = MakeProgressionHost(registry, bus);

            var playerId = new Id("unit.pb_player_1");
            host.RegisterUnit(playerId, new Id("prog.curve.pb"), startLevel: 1);

            var world = NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            AddPlayer(world, playerId, new Id("map.pb"), new Vec2(0, 0));

            var templates = new FakeCreatureTemplateQuery();
            var templateId = new Id("creature.pb_wolf");
            templates.Add(templateId, new Id("creature.tier.pb_normal"));
            var creatureId = new Id("unit.pb_wolf_1");
            var creature = AddCreature(world, creatureId, new Id("map.pb"), templateId, new Vec2(1, 0));
            creature.Level = 6;

            _ = new CreatureDeathXpListener(bus, host, units, templates);

            bus.Enqueue(new UnitDiedEvent(creatureId, killerId: playerId));
            bus.DispatchPending();

            Assert.Equal(900, host.GetXp(playerId));
        }

        /// <summary>击杀 · 用例 2（Δ=-5，怪物比玩家低 5 级；召唤归属）：击杀者是一个已登记召唤物，
        /// 其主人是玩家——经验记到主人身上，不是召唤物自己。baseAmount=Evaluate(3)=300，
        /// xp_factor(Δ=3-8=-5)=0.1，granted=300×1×0.1=30。</summary>
        [Fact]
        public void OnUnitDied_SummonKills_CreditsOwner_UsesOwnerLevelForDelta()
        {
            var bus = NewEventBus();
            var registry = MakeProgressionRegistry(bus, KillSourceRows);
            var host = MakeProgressionHost(registry, bus);

            var ownerId = new Id("unit.pb_owner_1");
            host.RegisterUnit(ownerId, new Id("prog.curve.pb"), startLevel: 8);

            var world = NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            AddPlayer(world, ownerId, new Id("map.pb"), new Vec2(0, 0));

            var summonId = new Id("unit.pb_summon_1");
            AddCreature(world, summonId, new Id("map.pb"), new Id("creature.pb_summon_tpl"), new Vec2(0, 1));

            var templates = new FakeCreatureTemplateQuery();
            var templateId = new Id("creature.pb_bear");
            templates.Add(templateId, new Id("creature.tier.pb_normal"));
            var creatureId = new Id("unit.pb_bear_1");
            var creature = AddCreature(world, creatureId, new Id("map.pb"), templateId, new Vec2(1, 0));
            creature.Level = 3;

            var summons = new FakeSummonHost();
            summons.SetOwner(summonId, ownerId);

            _ = new CreatureDeathXpListener(bus, host, units, templates, summons: summons);

            bus.Enqueue(new UnitDiedEvent(creatureId, killerId: summonId));
            bus.DispatchPending();

            Assert.Equal(30, host.GetXp(ownerId));
        }

        /// <summary>验收标准"击杀者非玩家（生物杀生物）不发放"：击杀者是一个普通生物（不是玩家、
        /// 不归属任何玩家的召唤物），经验不发放。</summary>
        [Fact]
        public void OnUnitDied_CreatureKillsCreature_DoesNotGrantXp()
        {
            var bus = NewEventBus();
            var registry = MakeProgressionRegistry(bus, KillSourceRows);
            var host = MakeProgressionHost(registry, bus);

            var killerId = new Id("unit.pb_killer_creature");
            host.RegisterUnit(killerId, new Id("prog.curve.pb"), startLevel: 1);

            var world = NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            AddCreature(world, killerId, new Id("map.pb"), new Id("creature.pb_killer_tpl"), new Vec2(0, 0));

            var templates = new FakeCreatureTemplateQuery();
            var templateId = new Id("creature.pb_prey");
            templates.Add(templateId, new Id("creature.tier.pb_normal"));
            var creatureId = new Id("unit.pb_prey_1");
            var creature = AddCreature(world, creatureId, new Id("map.pb"), templateId, new Vec2(1, 0));
            creature.Level = 6;

            _ = new CreatureDeathXpListener(bus, host, units, templates);

            bus.Enqueue(new UnitDiedEvent(creatureId, killerId: killerId));
            bus.DispatchPending();

            Assert.Equal(0, host.GetXp(killerId));
        }

        /// <summary>硬性规则"禁止在监听器里读掉落结果"衍生的防御性验收：<c>prog.xp_source.kill</c>
        /// 未登记（该游戏/夹具尚未配置）时，<see cref="IProgressionHost.HasXpSource"/> 返回
        /// <c>false</c>，本监听器据此跳过 <see cref="IProgressionHost.GrantXp"/> 调用（T-N4-4
        /// 附带任务起改为显式查询，不再依赖 <see cref="System.ArgumentException"/> 的
        /// try/catch，见 <see cref="CreatureDeathXpListener"/> 判断记录），不向外传播、不阻断
        /// 事件派发。</summary>
        [Fact]
        public void OnUnitDied_KillXpSourceNotRegistered_DoesNotThrow_SkipsSilently()
        {
            var bus = NewEventBus();
            var registry = MakeProgressionRegistry(bus, DiscoveryOnlySourceRows);
            var host = MakeProgressionHost(registry, bus);

            var playerId = new Id("unit.pb_player_2");
            host.RegisterUnit(playerId, new Id("prog.curve.pb"), startLevel: 1);

            var world = NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            AddPlayer(world, playerId, new Id("map.pb"), new Vec2(0, 0));

            var templates = new FakeCreatureTemplateQuery();
            var templateId = new Id("creature.pb_goat");
            templates.Add(templateId, new Id("creature.tier.pb_normal"));
            var creatureId = new Id("unit.pb_goat_1");
            var creature = AddCreature(world, creatureId, new Id("map.pb"), templateId, new Vec2(1, 0));
            creature.Level = 4;

            _ = new CreatureDeathXpListener(bus, host, units, templates);

            bus.Enqueue(new UnitDiedEvent(creatureId, killerId: playerId));
            bus.DispatchPending();

            Assert.Equal(0, host.GetXp(playerId));
        }

        /// <summary>死亡单位模板的分档 id（<c>TierId</c>）经 <see cref="XpContext.TierId"/> 传给
        /// <see cref="IProgressionHost.GrantXp"/>——本模块只登记、不消费（T-N4-4 才读取，见
        /// <c>XpContext</c> 类型注释"契约疑点上报"），用间谍宿主核对确实传对了值。</summary>
        [Fact]
        public void OnUnitDied_PassesDiedUnitTierId_IntoXpContext()
        {
            var bus = NewEventBus();
            var spy = new SpyProgressionHost();

            var world = NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            var playerId = new Id("unit.pb_player_3");
            AddPlayer(world, playerId, new Id("map.pb"), new Vec2(0, 0));

            var templates = new FakeCreatureTemplateQuery();
            var templateId = new Id("creature.pb_elite_orc");
            var tierId = new Id("creature.tier.pb_elite");
            templates.Add(templateId, tierId);
            var creatureId = new Id("unit.pb_elite_orc_1");
            var creature = AddCreature(world, creatureId, new Id("map.pb"), templateId, new Vec2(1, 0));
            creature.Level = 10;

            _ = new CreatureDeathXpListener(bus, spy, units, templates);

            bus.Enqueue(new UnitDiedEvent(creatureId, killerId: playerId));
            bus.DispatchPending();

            var call = Assert.Single(spy.Calls);
            Assert.Equal(playerId, call.UnitId);
            Assert.Equal(CreatureDeathXpListener.DefaultKillXpSourceId, call.SourceId);
            Assert.Equal(10, call.Context.SourceLevel);
            Assert.Equal(tierId, call.Context.TierId);
        }

        /// <summary>死亡单位的模板 id 未登记到 <see cref="ICreatureTemplateQuery"/> 时（同
        /// <c>CreatureDeathLootListener.OnUnitDied</c> 判断记录），<see cref="XpContext.TierId"/>
        /// 退化为 <c>null</c>，不阻断经验发放本身仍然会尝试进行（间谍宿主仍收到一次调用）。</summary>
        [Fact]
        public void OnUnitDied_UnregisteredTemplate_TierIdIsNull_ButStillGrants()
        {
            var bus = NewEventBus();
            var spy = new SpyProgressionHost();

            var world = NewWorld(bus);
            var units = new Core.Carriers.Unit.WorldUnitAccess(world);
            var playerId = new Id("unit.pb_player_4");
            AddPlayer(world, playerId, new Id("map.pb"), new Vec2(0, 0));

            var templates = new FakeCreatureTemplateQuery(); // 故意不 Add 任何模板
            var templateId = new Id("creature.pb_unregistered");
            var creatureId = new Id("unit.pb_unregistered_1");
            var creature = AddCreature(world, creatureId, new Id("map.pb"), templateId, new Vec2(1, 0));
            creature.Level = 5;

            _ = new CreatureDeathXpListener(bus, spy, units, templates);

            bus.Enqueue(new UnitDiedEvent(creatureId, killerId: playerId));
            bus.DispatchPending();

            var call = Assert.Single(spy.Calls);
            Assert.Null(call.Context.TierId);
            Assert.Equal(5, call.Context.SourceLevel);
        }
    }
}
