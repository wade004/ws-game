using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Assembly;
using Core.Carriers.Creature;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Numbers.PowerSet;
using Core.Rules.Combat;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Assembly
{
    /// <summary>
    /// [ADR-0107](../../../../architecture/adr/0107-脱战判定的交战范围.md)（消费方第五十六批反馈，
    /// 生产装配级复现）：<see cref="Core.Rules.Combat.CombatHost"/> 的脱战判定
    /// （<c>HasLivingHostileThreatSource</c>，<c>Update</c> 唯一读取）修复前完全不限距离——打伤一个
    /// 敌对单位但不将其击杀，随后跑开任意距离、等待数倍 <see cref="CombatOptions.LeaveCombatDelay"/>，
    /// <see cref="ICombatHost.IsInCombat"/> 仍恒为 <c>true</c>。用真实 <see cref="CarriersAssembly"/>
    /// 装配（真实 <see cref="WorldUnitAccess"/> 位置 + 真实 <see cref="Core.Rules.Combat.ThreatTable"/> +
    /// 真实 <see cref="Core.Rules.Combat.CombatHost"/>，<c>world.Tick</c> 驱动脱战判定），不手工模拟
    /// <c>CombatHost.Update</c>。
    /// </summary>
    public class ADR0107_ThreatSourceRangeTests
    {
        private static readonly Id MapId = new Id("map.threat_range_test");
        private static readonly Id AttackerId = new Id("unit.threat_range_attacker");
        private static readonly Id AttackerFaction = new Id("fac.threat_range_attacker");
        private static readonly Id ArchetypeId = new Id("arch.class.threat_range_sample");
        private static readonly Id HostileFaction = new Id("fac.threat_range_hostile");
        private static readonly Id DefenderTemplateId = new Id("creature.threat_range_defender");
        private static readonly Id TierId = new Id("creature.tier.threat_range_normal");

        private sealed class Fixture
        {
            public WorldSim World = null!;
            public WorldUnitAccess Units = null!;
            public CarriersAssembly Assembly = null!;
            public Id DefenderId;
            public List<CombatLeftEvent> LeftEvents = null!;
        }

        /// <summary>
        /// 生产装配夹具：一名玩家单位（攻击方，代表消费方场景里"跑开的玩家"）+ 一只敌对生物
        /// （防御方，非致命伤害后仍存活），双方经 <see cref="ICombatHost.NotifyCombatEvent"/> +
        /// <see cref="IThreatTable.AddThreat"/> 摆入一次战斗——与真实伤害结算等价的最小构造（惯例同
        /// <c>SummonCombatRechaseTests</c>/<c>SummonIdleChaseShareThreatTests</c> 既有夹具"与消费方
        /// 最小构造等价"注释），只把仇恨记到防御方自己的表上（<c>AddThreat(DefenderId, AttackerId, ...)</c>），
        /// 不记到攻击方表上——真实场景里"打伤对方但未被反击"正是这个仇恨表形状：攻击方自己的仇恨表
        /// 为空，只作为来源挂在防御方的表里（<see cref="CombatHost.HasLivingHostileThreatSource"/>
        /// 判断记录"方向二"）。
        /// </summary>
        private static Fixture Build(double threatSourceRange, double leaveCombatDelay = 2.0)
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();

            source.Add("item.budget_curve",
                "{\"table\": \"item.budget_curve\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"item.budget.threat_range_default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}" +
                "]}");
            source.Add("combat.hit_table_config",
                "{\"table\": \"combat.hit_table_config\", \"schema_version\": 1, \"rows\": []}");
            source.Add("combat.resist_curve",
                "{\"table\": \"combat.resist_curve\", \"schema_version\": 1, \"rows\": []}");

            source.Add("stat.definition",
                "{\"table\": \"stat.definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"stat.threat_range_max_health\", \"name_key\": \"l10n.stat.threat_range_max_health.name\", " +
                "\"group\": \"primary\", \"default_base\": 0}" +
                "]}");
            // id 必须是 WellKnownPowers.Health 本身（PowerHost.RegisterUnit 按精确 id 匹配已登记的
            // 资源类型定义，惯例同 SummonIdleChaseShareThreatTests）。
            source.Add("arch.power_type",
                "{\"table\": \"arch.power_type\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + WellKnownPowers.Health + "\", \"name_key\": \"l10n.power.threat_range_health.name\", " +
                "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.threat_range_max_health\"}, " +
                "\"regen_in_combat\": 0, \"regen_out_of_combat\": 0, \"decay_out_of_combat\": 0, " +
                "\"refill_on_leave_combat\": false, \"start_full\": true, \"allow_overflow\": false, \"min\": 0}" +
                "]}");

            source.Add("fac.faction",
                "{\"table\": \"fac.faction\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + AttackerFaction + "\", \"name_key\": \"l10n.fac.threat_range_attacker.name\", \"default_reaction\": \"hostile\"}," +
                "{\"id\": \"" + HostileFaction + "\", \"name_key\": \"l10n.fac.threat_range_hostile.name\", \"default_reaction\": \"hostile\"}" +
                "]}");
            source.Add("fac.reaction_matrix", "{\"table\": \"fac.reaction_matrix\", \"schema_version\": 1, \"rows\": []}");

            source.Add(CreatureSchemas.TierDefinition.Name,
                "{\"table\": \"" + CreatureSchemas.TierDefinition.Name + "\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + TierId + "\", \"name_key\": \"l10n.creature.tier.threat_range_normal.name\", \"stat_multiplier\": 1}" +
                "]}");
            source.Add(CreatureSchemas.Template.Name,
                "{\"table\": \"" + CreatureSchemas.Template.Name + "\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + DefenderTemplateId + "\", \"name_key\": \"l10n.creature.threat_range_defender.name\", " +
                "\"level\": 1, \"tier\": \"" + TierId + "\", \"base_stats\": {\"stat.threat_range_max_health\": 100}, " +
                "\"faction_id\": \"" + HostileFaction + "\", \"display_ref\": \"display.threat_range_defender\"}" +
                "]}");

            var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false });
            CarriersSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var units = new WorldUnitAccess(world);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);

            var combatOptions = new CombatOptions
            {
                LeaveCombatDelay = leaveCombatDelay,
                ThreatSourceRange = threatSourceRange,
            };

            var assembly = new CarriersAssembly(
                bus, registry, rng, world, spatial, navigation: null, spatialSyncKinds: null,
                worldFlags: null, lootRoller: null, statOptions: null, combatOptions: combatOptions,
                skillOptions: null, targetingOptions: null, aiOptions: null, inventoryOptions: null,
                itemOptions: null, creatureOptions: null, summonOptions: null);

            var attacker = new PlayerUnit(AttackerId, MapId, AttackerFaction, ArchetypeId) { Position = Vec2.Zero };
            world.AddEntity(attacker);
            assembly.Rules.Stats.RegisterUnit(AttackerId);
            assembly.Rules.Powers.RegisterUnit(AttackerId, new[] { WellKnownPowers.Health });

            var defenderId = assembly.Creatures.Spawn(DefenderTemplateId, MapId, Vec2.Zero, facing: 0);

            var leftEvents = new List<CombatLeftEvent>();
            bus.Subscribe(RulesEventKeys.CombatLeft, e =>
            {
                if (e is CombatLeftEvent left) leftEvents.Add(left);
            });

            return new Fixture
            {
                World = world,
                Units = units,
                Assembly = assembly,
                DefenderId = defenderId,
                LeftEvents = leftEvents,
            };
        }

        /// <summary>攻击方对防御方造成一次非致命伤害的最小等价构造（见 <see cref="Build"/> 判断
        /// 记录）：双方各触发一次 <c>NotifyCombatEvent</c>（等价于伤害结算的既有惯例，见
        /// <see cref="Core.Rules.Combat.CombatHost.NotifyCombatEvent"/> 判断记录"对结算双方各调用
        /// 一次"），仇恨只记到防御方自己的表上，防御方存活（非致命）。</summary>
        private static void DealNonLethalDamage(Fixture f)
        {
            f.Assembly.Rules.Combat.NotifyCombatEvent(AttackerId, f.DefenderId);
            f.Assembly.Rules.Combat.NotifyCombatEvent(f.DefenderId, AttackerId);
            f.Assembly.Rules.Combat.GetThreatTable(f.DefenderId).AddThreat(f.DefenderId, AttackerId, 10);

            Assert.True(f.Assembly.Rules.Combat.IsInCombat(AttackerId));
            Assert.True(f.Assembly.Rules.Combat.IsInCombat(f.DefenderId));
            Assert.True(f.Units.IsAlive(f.DefenderId));
        }

        private static void AdvanceBy(Fixture f, double totalSeconds, double dt = 0.5)
        {
            var elapsed = 0.0;
            while (elapsed < totalSeconds)
            {
                f.World.Tick(SimStep.Continuous(dt));
                elapsed += dt;
            }
        }

        // -----------------------------------------------------------------
        // 主复现（修复前红）：受一次非致命伤害的防御方仍存活、仍敌对，攻击方跑到
        // ThreatSourceRange + 1 之外，推进 3×LeaveCombatDelay 后应正常脱战。
        // 修复前的失败断言原文：Assert.False(f.Assembly.Rules.Combat.IsInCombat(AttackerId))
        // ——实测修复前恒为 True（全图范围，防御方存活且敌对，不论攻击方跑多远都判定"仍在交战"）。
        // -----------------------------------------------------------------
        [Fact]
        public void Update_ThreatSourceOutsideRange_LeavesCombat_EvenThoughSourceStillAliveAndHostile()
        {
            const double range = 10.0;
            const double delay = 2.0;
            var f = Build(threatSourceRange: range, leaveCombatDelay: delay);

            DealNonLethalDamage(f);

            // 攻击方（"玩家"）跑到 range(10) + 1 = 11 之外。
            f.Units.SetPosition(AttackerId, new Vec2(range + 1.0, 0));

            AdvanceBy(f, totalSeconds: delay * 3);

            Assert.False(f.Assembly.Rules.Combat.IsInCombat(AttackerId));

            var attackerLeftCount = f.LeftEvents.Count(e => e.UnitId.Equals(AttackerId));
            Assert.Equal(1, attackerLeftCount);
        }

        // -----------------------------------------------------------------
        // 不变量①：同样场景，攻击方留在范围内——旧行为保持（仍在战）。
        // -----------------------------------------------------------------
        [Fact]
        public void Update_ThreatSourceWithinRange_StaysInCombat()
        {
            const double range = 10.0;
            const double delay = 2.0;
            var f = Build(threatSourceRange: range, leaveCombatDelay: delay);

            DealNonLethalDamage(f);

            f.Units.SetPosition(AttackerId, new Vec2(range - 1.0, 0));

            AdvanceBy(f, totalSeconds: delay * 3);

            Assert.True(f.Assembly.Rules.Combat.IsInCombat(AttackerId));
        }

        // -----------------------------------------------------------------
        // 不变量②：ThreatSourceRange <= 0 表示不限范围——即便跑得很远也仍在战（向后兼容旧行为）。
        // -----------------------------------------------------------------
        [Fact]
        public void Update_ThreatSourceRangeUnlimited_StaysInCombat_RegardlessOfDistance()
        {
            const double delay = 2.0;
            var f = Build(threatSourceRange: 0.0, leaveCombatDelay: delay);

            DealNonLethalDamage(f);

            f.Units.SetPosition(AttackerId, new Vec2(10_000.0, 0));

            AdvanceBy(f, totalSeconds: delay * 3);

            Assert.True(f.Assembly.Rules.Combat.IsInCombat(AttackerId));
        }

        // -----------------------------------------------------------------
        // 不变量③：方向二（攻击方作为攻击来源，挂在防御方的仇恨表里，攻击方自己的仇恨表为空）
        // 同样受范围限制——即主复现本身：IsInCombat 的判定对象是 AttackerId，仇恨记录挂在
        // DefenderId 的表上（AddThreat(DefenderId, AttackerId, ...)），命中的正是
        // CombatHost.HasLivingHostileThreatSource 的"方向二"分支。本用例额外验证方向一
        // （DefenderId 自己的仇恨表里有 AttackerId 这条记录）在同一次移动下同样受限——两个方向
        // 用的是同一段距离（Vec2.Distance 对称），移动攻击方同时验证了两个方向。
        // -----------------------------------------------------------------
        [Fact]
        public void Update_Direction1_UnitsOwnThreatTable_SourceOutsideRange_LeavesCombat()
        {
            const double range = 10.0;
            const double delay = 2.0;
            var f = Build(threatSourceRange: range, leaveCombatDelay: delay);

            DealNonLethalDamage(f);
            // 方向一断言前提：DefenderId 自己的仇恨表里确实有 AttackerId 这条记录。
            Assert.NotEmpty(f.Assembly.Rules.Combat.GetThreatTable(f.DefenderId).GetAll(f.DefenderId));

            f.Units.SetPosition(AttackerId, new Vec2(range + 1.0, 0));

            AdvanceBy(f, totalSeconds: delay * 3);

            Assert.False(f.Assembly.Rules.Combat.IsInCombat(f.DefenderId));
        }

        // -----------------------------------------------------------------
        // 边界：距离恰等于 ThreatSourceRange 视为范围内，仍在战。
        // -----------------------------------------------------------------
        [Fact]
        public void Update_DistanceExactlyEqualsThreatSourceRange_IsWithinRange_StaysInCombat()
        {
            const double range = 10.0;
            const double delay = 2.0;
            var f = Build(threatSourceRange: range, leaveCombatDelay: delay);

            DealNonLethalDamage(f);

            f.Units.SetPosition(AttackerId, new Vec2(range, 0));

            AdvanceBy(f, totalSeconds: delay * 3);

            Assert.True(f.Assembly.Rules.Combat.IsInCombat(AttackerId));
        }

        // -----------------------------------------------------------------
        // D12（测试覆盖梳理 2026-10-01；设计决定，见 ADR-0125）：ThreatSourceRange 只影响脱战判定，
        // 不修剪仇恨表。防御方同时被一个范围外（仇恨更高）和一个范围内的来源攻击：范围外来源这条
        // 记录原样留在表里，仍参与 GetTopThreat（AI 目标选择据此可能继续追击已"脱战"的一方）；
        // 防御方因范围内来源而仍在战，范围外来源自己则按判定脱战。
        // -----------------------------------------------------------------
        [Fact]
        public void Update_SourceOutsideRange_ThreatEntryIsNotPruned_AndStillTopThreat_WhileOtherSourceKeepsUnitInCombat()
        {
            const double range = 10.0;
            const double delay = 2.0;
            var f = Build(threatSourceRange: range, leaveCombatDelay: delay);
            var nearId = new Id("unit.threat_range_near_attacker");
            var near = new PlayerUnit(nearId, MapId, AttackerFaction, ArchetypeId) { Position = Vec2.Zero };
            f.World.AddEntity(near);
            f.Assembly.Rules.Stats.RegisterUnit(nearId);
            f.Assembly.Rules.Powers.RegisterUnit(nearId, new[] { WellKnownPowers.Health });

            DealNonLethalDamage(f); // AttackerId 对 DefenderId 记 10 点仇恨
            f.Assembly.Rules.Combat.NotifyCombatEvent(nearId, f.DefenderId);
            f.Assembly.Rules.Combat.NotifyCombatEvent(f.DefenderId, nearId);
            var threatTable = f.Assembly.Rules.Combat.GetThreatTable(f.DefenderId);
            const double nearThreat = 10.0;
            const double farExtraThreat = 50.0;
            threatTable.AddThreat(f.DefenderId, nearId, nearThreat);
            threatTable.AddThreat(f.DefenderId, AttackerId, farExtraThreat); // 远处来源仇恨更高
            var farThreatBefore = threatTable.GetThreat(f.DefenderId, AttackerId);
            Assert.True(farThreatBefore > nearThreat);

            f.Units.SetPosition(AttackerId, new Vec2(range + 1.0, 0));   // 超出范围
            f.Units.SetPosition(nearId, new Vec2(range - 1.0, 0));       // 范围内

            AdvanceBy(f, totalSeconds: delay * 3);

            // 脱战判定：范围外来源不再计入，范围内来源让防御方仍在战。
            Assert.True(f.Assembly.Rules.Combat.IsInCombat(f.DefenderId));
            Assert.False(f.Assembly.Rules.Combat.IsInCombat(AttackerId));

            // 现行为（设计决定）：范围外来源的条目没有被修剪，数值不变，且仍是最高仇恨目标。
            Assert.Equal(farThreatBefore, threatTable.GetThreat(f.DefenderId, AttackerId));
            Assert.Contains(threatTable.GetAll(f.DefenderId), e => e.source.Equals(AttackerId));
            Assert.Equal(AttackerId, threatTable.GetTopThreat(f.DefenderId));
        }
    }
}
