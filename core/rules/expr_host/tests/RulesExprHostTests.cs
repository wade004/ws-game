using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;
using Core.Rules.ExprHost;
using Xunit;

namespace Tests.Rules.ExprHost
{
    /// <summary>
    /// <see cref="RulesExprHostFactory"/>/<see cref="RulesExprSchema"/> 的单元测试（落地计划
    /// T2-11 集成任务"二、L2 Expr 宿主"）：覆盖 self/target/combat/enemies/time/event 六个分组
    /// 各自的键、缺目标默认值、event 字段、extra group（world/quest/player）缺失警告。
    /// </summary>
    public sealed class RulesExprHostTests
    {
        private static readonly Id Player = new Id("unit.player");
        private static readonly Id Enemy = new Id("unit.enemy");
        private static readonly Id FactionPlayer = new Id("fac.player");
        private static readonly Id FactionEnemy = new Id("fac.enemy");
        private static readonly Id PowerHealth = new Id("arch.power.health");
        private static readonly Id PowerMana = new Id("arch.power.mana");
        private static readonly Id AuraBurn = new Id("skill.aura_def.burn");
        private static readonly Id StatStrength = new Id("stat.strength");
        private static readonly Id TagBoss = new Id("tag.boss");

        private sealed class Fixture
        {
            public FakeUnitAccess Units = new FakeUnitAccess();
            public FakeStatHost Stats = new FakeStatHost();
            public FakePowerHost Powers = new FakePowerHost();
            public FakeAuraQuery Auras = new FakeAuraQuery();
            public FakeCombatHost Combat = new FakeCombatHost();
            public StubSpatialQuery Spatial = new StubSpatialQuery();
            public FakeFactionMatrix Factions = new FakeFactionMatrix();
            public FakeSkillHost? SkillHost = new FakeSkillHost();
            public Dictionary<string, IExprGroupProvider> ExtraGroups = new Dictionary<string, IExprGroupProvider>();
            public ExprDiagnosticsRecorder Diagnostics = new ExprDiagnosticsRecorder();
            public double SimTime = 100.0;
            public double CombatStartTime = 40.0;

            public RulesExprHostFactory BuildFactory()
            {
                return new RulesExprHostFactory(
                    Units, Stats, Powers, Auras, Combat, Combat.GetThreatTable(Player), Spatial, Factions,
                    () => SimTime, _ => CombatStartTime, ExtraGroups, SkillHost, Diagnostics);
            }
        }

        private static Fixture BuildBasicWorld()
        {
            var fx = new Fixture();
            fx.Units.Add(Player, new Vec2(0, 0), FactionPlayer, level: 5, alive: true, tags: new[] { TagBoss });
            fx.Units.Add(Enemy, new Vec2(3, 4), FactionEnemy, level: 7, alive: true);
            fx.Spatial.Register(Player, new Vec2(0, 0), 0.1);
            fx.Spatial.Register(Enemy, new Vec2(3, 4), 0.1);
            fx.Factions.SetHostile(FactionPlayer, FactionEnemy);
            fx.Powers.Set(Player, PowerHealth, 50, 100).Set(Player, PowerMana, 20, 40);
            fx.Powers.Set(Enemy, PowerHealth, 80, 100);
            fx.Stats.Set(Player, StatStrength, 12.5);
            fx.Auras.Add(Player, AuraBurn, 3);
            return fx;
        }

        // -----------------------------------------------------------------
        // self / target：资源、属性、状态
        // -----------------------------------------------------------------

        [Fact]
        public void Self_Hp_HpMax_HpPct_ReadFromPowerHost()
        {
            var fx = BuildBasicWorld();
            var host = fx.BuildFactory().CreateFor(Player, Enemy, null);

            Assert.Equal(50.0, host.Query(ExprGroups.Self, "hp", Array.Empty<ExprValue>()).AsNumber);
            Assert.Equal(100.0, host.Query(ExprGroups.Self, "hp_max", Array.Empty<ExprValue>()).AsNumber);
            Assert.Equal(0.5, host.Query(ExprGroups.Self, "hp_pct", Array.Empty<ExprValue>()).AsNumber);
        }

        [Fact]
        public void Target_Power_And_PowerPct_ReadArgPowerType()
        {
            var fx = BuildBasicWorld();
            var host = fx.BuildFactory().CreateFor(Enemy, Player, null);

            var args = new[] { ExprValue.OfId(PowerMana) };
            Assert.Equal(20.0, host.Query(ExprGroups.Target, "power", args).AsNumber);
            Assert.Equal(0.5, host.Query(ExprGroups.Target, "power_pct", args).AsNumber);
        }

        [Fact]
        public void Self_Level_Faction_IsAlive()
        {
            var fx = BuildBasicWorld();
            var host = fx.BuildFactory().CreateFor(Player, null, null);

            Assert.Equal(5L, host.Query(ExprGroups.Self, "level", Array.Empty<ExprValue>()).AsInt);
            Assert.Equal(FactionPlayer, host.Query(ExprGroups.Self, "faction", Array.Empty<ExprValue>()).AsId);
            Assert.True(host.Query(ExprGroups.Self, "is_alive", Array.Empty<ExprValue>()).AsBool);
        }

        [Fact]
        public void Self_InCombat_ReflectsCombatHost()
        {
            var fx = BuildBasicWorld();
            fx.Combat.SetInCombat(Player, true);
            var host = fx.BuildFactory().CreateFor(Player, null, null);

            Assert.True(host.Query(ExprGroups.Self, "in_combat", Array.Empty<ExprValue>()).AsBool);
        }

        [Fact]
        public void Self_IsCasting_ReflectsSkillHost()
        {
            var fx = BuildBasicWorld();
            fx.SkillHost!.SetCasting(Player, true);
            var host = fx.BuildFactory().CreateFor(Player, null, null);

            Assert.True(host.Query(ExprGroups.Self, "is_casting", Array.Empty<ExprValue>()).AsBool);
        }

        [Fact]
        public void IsCasting_WithoutSkillHost_DefaultsFalse_AndWarnsOnce()
        {
            var fx = BuildBasicWorld();
            fx.SkillHost = null;
            var host = fx.BuildFactory().CreateFor(Player, null, null);

            Assert.False(host.Query(ExprGroups.Self, "is_casting", Array.Empty<ExprValue>()).AsBool);
            Assert.False(host.Query(ExprGroups.Combat, "is_casting", Array.Empty<ExprValue>()).AsBool);

            // 只警告一次：两次查询（self./combat.）共只产生一条警告。
            Assert.Single(fx.Diagnostics.Warnings);
        }

        [Fact]
        public void Self_HasAura_And_AuraStacks()
        {
            var fx = BuildBasicWorld();
            var host = fx.BuildFactory().CreateFor(Player, null, null);
            var args = new[] { ExprValue.OfId(AuraBurn) };

            Assert.True(host.Query(ExprGroups.Self, "has_aura", args).AsBool);
            Assert.Equal(3L, host.Query(ExprGroups.Self, "aura_stacks", args).AsInt);
        }

        [Fact]
        public void Self_Stat_And_HasTag()
        {
            var fx = BuildBasicWorld();
            var host = fx.BuildFactory().CreateFor(Player, null, null);

            Assert.Equal(12.5, host.Query(ExprGroups.Self, "stat", new[] { ExprValue.OfId(StatStrength) }).AsNumber);
            Assert.True(host.Query(ExprGroups.Self, "has_tag", new[] { ExprValue.OfId(TagBoss) }).AsBool);
            Assert.False(host.Query(ExprGroups.Self, "has_tag", new[] { ExprValue.OfId(new Id("tag.unknown")) }).AsBool);
        }

        [Fact]
        public void Self_PositionXY_And_DistanceToTarget()
        {
            var fx = BuildBasicWorld();
            var host = fx.BuildFactory().CreateFor(Player, Enemy, null);

            Assert.Equal(0.0, host.Query(ExprGroups.Self, "position_x", Array.Empty<ExprValue>()).AsNumber);
            Assert.Equal(0.0, host.Query(ExprGroups.Self, "position_y", Array.Empty<ExprValue>()).AsNumber);
            // Player (0,0) → Enemy (3,4)：3-4-5 直角三角形，距离 5。
            Assert.Equal(5.0, host.Query(ExprGroups.Self, "distance_to_target", Array.Empty<ExprValue>()).AsNumber, 10);
        }

        [Fact]
        public void Self_ThreatTop_ReturnsTopThreatSource()
        {
            var fx = BuildBasicWorld();
            fx.Combat.Threat.AddThreat(Player, Enemy, 10);
            var host = fx.BuildFactory().CreateFor(Player, null, null);

            Assert.Equal(Enemy, host.Query(ExprGroups.Self, "threat_top", Array.Empty<ExprValue>()).AsId);
        }

        [Fact]
        public void Self_ThreatTop_NoThreatRecorded_DefaultsToNoneId_AndWarns()
        {
            var fx = BuildBasicWorld();
            var host = fx.BuildFactory().CreateFor(Player, null, null);

            var value = host.Query(ExprGroups.Self, "threat_top", Array.Empty<ExprValue>());
            Assert.Equal(RulesExprHostFactory.NoneId, value.AsId);
            Assert.NotEmpty(fx.Diagnostics.Warnings);
        }

        // -----------------------------------------------------------------
        // target 缺目标：6.3 节默认值 + 警告
        // -----------------------------------------------------------------

        [Fact]
        public void Target_Missing_NumberKey_DefaultsToZero_AndWarns()
        {
            var fx = BuildBasicWorld();
            var host = fx.BuildFactory().CreateFor(Player, null, null);

            var value = host.Query(ExprGroups.Target, "hp", Array.Empty<ExprValue>());
            Assert.Equal(ExprValueKind.Number, value.Kind);
            Assert.Equal(0.0, value.AsNumber);
            Assert.NotEmpty(fx.Diagnostics.Warnings);
        }

        [Fact]
        public void Target_Missing_IdKey_DefaultsToNoneId()
        {
            var fx = BuildBasicWorld();
            var host = fx.BuildFactory().CreateFor(Player, null, null);

            var value = host.Query(ExprGroups.Target, "faction", Array.Empty<ExprValue>());
            Assert.Equal(RulesExprHostFactory.NoneId, value.AsId);
        }

        [Fact]
        public void Target_Missing_BoolKey_DefaultsToFalse()
        {
            var fx = BuildBasicWorld();
            var host = fx.BuildFactory().CreateFor(Player, null, null);

            Assert.False(host.Query(ExprGroups.Target, "is_alive", Array.Empty<ExprValue>()).AsBool);
        }

        // -----------------------------------------------------------------
        // combat（自身）
        // -----------------------------------------------------------------

        [Fact]
        public void Combat_InCombat_ReflectsSelf()
        {
            var fx = BuildBasicWorld();
            fx.Combat.SetInCombat(Player, true);
            var host = fx.BuildFactory().CreateFor(Player, null, null);

            Assert.True(host.Query(ExprGroups.Combat, "in_combat", Array.Empty<ExprValue>()).AsBool);
        }

        // -----------------------------------------------------------------
        // enemies
        // -----------------------------------------------------------------

        [Fact]
        public void Enemies_CountInRange_CountsHostileAliveUnitsOnly()
        {
            var fx = BuildBasicWorld();
            var host = fx.BuildFactory().CreateFor(Player, null, null);

            // Enemy 在距离 5 处，是敌对且存活：半径 10 内数得到 1，半径 1 内数不到。
            Assert.Equal(1L, host.Query(ExprGroups.Enemies, "count_in_range", new[] { ExprValue.OfNumber(10) }).AsInt);
            Assert.Equal(0L, host.Query(ExprGroups.Enemies, "count_in_range", new[] { ExprValue.OfNumber(1) }).AsInt);
        }

        [Fact]
        public void Enemies_CountInRange_ExcludesDeadUnits()
        {
            var fx = BuildBasicWorld();
            fx.Units.Add(Enemy, new Vec2(3, 4), FactionEnemy, alive: false);
            var host = fx.BuildFactory().CreateFor(Player, null, null);

            Assert.Equal(0L, host.Query(ExprGroups.Enemies, "count_in_range", new[] { ExprValue.OfNumber(10) }).AsInt);
        }

        [Fact]
        public void Enemies_NearestDistance_ReturnsClosestHostileDistance()
        {
            var fx = BuildBasicWorld();
            var host = fx.BuildFactory().CreateFor(Player, null, null);

            Assert.Equal(5.0, host.Query(ExprGroups.Enemies, "nearest_distance", Array.Empty<ExprValue>()).AsNumber, 10);
        }

        [Fact]
        public void Enemies_NearestDistance_NoEnemies_ReturnsLargeNumber()
        {
            var fx = new Fixture();
            fx.Units.Add(Player, new Vec2(0, 0), FactionPlayer);
            fx.Spatial.Register(Player, new Vec2(0, 0), 0.1);
            var host = fx.BuildFactory().CreateFor(Player, null, null);

            var distance = host.Query(ExprGroups.Enemies, "nearest_distance", Array.Empty<ExprValue>()).AsNumber;
            Assert.Equal(RulesExprHostFactory.NoEnemyDistance, distance);
        }

        // -----------------------------------------------------------------
        // time
        // -----------------------------------------------------------------

        [Fact]
        public void Time_SimTime_And_SinceCombatStart()
        {
            var fx = BuildBasicWorld();
            var host = fx.BuildFactory().CreateFor(Player, null, null);

            Assert.Equal(100.0, host.Query(ExprGroups.Time, "sim_time", Array.Empty<ExprValue>()).AsNumber);
            Assert.Equal(60.0, host.Query(ExprGroups.Time, "since_combat_start", Array.Empty<ExprValue>()).AsNumber, 10);
        }

        [Fact]
        public void Time_DiscreteModePlaceholders_AllZeroOrFalse()
        {
            var fx = BuildBasicWorld();
            var host = fx.BuildFactory().CreateFor(Player, null, null);

            Assert.Equal(0.0, host.Query(ExprGroups.Time, "day_cycle", Array.Empty<ExprValue>()).AsNumber);
            Assert.Equal(0L, host.Query(ExprGroups.Time, "turn_index", Array.Empty<ExprValue>()).AsInt);
            Assert.Equal(0L, host.Query(ExprGroups.Time, "round_index", Array.Empty<ExprValue>()).AsInt);
            Assert.False(host.Query(ExprGroups.Time, "is_my_turn", Array.Empty<ExprValue>()).AsBool);
        }

        // -----------------------------------------------------------------
        // event
        // -----------------------------------------------------------------

        [Fact]
        public void Event_Field_ReadsFromExprReadableEvent()
        {
            var fx = BuildBasicWorld();
            var evt = new CombatDamageDealtEvent(Enemy, Player, new Id("school.fire"), 42.0, true, HitResult.Crit);
            var host = fx.BuildFactory().CreateFor(Player, null, evt);

            Assert.Equal(42.0, host.Query(ExprGroups.Event, "amount", Array.Empty<ExprValue>()).AsNumber);
            Assert.True(host.Query(ExprGroups.Event, "isCrit", Array.Empty<ExprValue>()).AsBool);
            Assert.Equal(Player, host.Query(ExprGroups.Event, "targetId", Array.Empty<ExprValue>()).AsId);
        }

        [Fact]
        public void Event_NoTriggeringEvent_DefaultsToFalse_AndWarns()
        {
            var fx = BuildBasicWorld();
            var host = fx.BuildFactory().CreateFor(Player, null, null);

            var value = host.Query(ExprGroups.Event, "amount", Array.Empty<ExprValue>());
            Assert.Equal(ExprValueKind.Bool, value.Kind);
            Assert.False(value.AsBool);
            Assert.NotEmpty(fx.Diagnostics.Warnings);
        }

        [Fact]
        public void Event_UnknownField_DefaultsToFalse_AndWarns()
        {
            var fx = BuildBasicWorld();
            var evt = new CombatEnteredEvent(Player);
            var host = fx.BuildFactory().CreateFor(Player, null, evt);

            var value = host.Query(ExprGroups.Event, "no_such_field", Array.Empty<ExprValue>());
            Assert.False(value.AsBool);
            Assert.NotEmpty(fx.Diagnostics.Warnings);
        }

        // -----------------------------------------------------------------
        // world / quest / player：extraGroups
        // -----------------------------------------------------------------

        [Fact]
        public void ExtraGroup_Provided_DelegatesToProvider()
        {
            var fx = BuildBasicWorld();
            fx.ExtraGroups[ExprGroups.World] = new FakeExprGroupProvider
            {
                QueryFunc = (key, args) => key == "day_of_week" ? ExprValue.OfInt(3) : ExprValue.OfBool(false),
            };
            var host = fx.BuildFactory().CreateFor(Player, null, null);

            Assert.Equal(3L, host.Query(ExprGroups.World, "day_of_week", Array.Empty<ExprValue>()).AsInt);
        }

        [Fact]
        public void ExtraGroup_Missing_DefaultsAndWarnsOnce_AcrossMultipleQueries()
        {
            var fx = BuildBasicWorld();
            var factory = fx.BuildFactory();
            var host = factory.CreateFor(Player, null, null);

            host.Query(ExprGroups.Quest, "some_flag", Array.Empty<ExprValue>());
            host.Query(ExprGroups.Quest, "another_flag", Array.Empty<ExprValue>());

            // 同一分组缺失只记一次警告，即便查询了两个不同的 key、跨了两次 CreateFor 调用——
            // 去重发生在工厂实例级别（见 RulesExprHostFactory._warnedMissingGroups），同一个
            // factory 产出的多个 Host（不同 selfId/targetId/事件上下文）共享这一份"已警告分组"。
            var host2 = factory.CreateFor(Enemy, null, null);
            host2.Query(ExprGroups.Quest, "yet_another", Array.Empty<ExprValue>());

            Assert.Single(fx.Diagnostics.Warnings);
        }

        // -----------------------------------------------------------------
        // RulesExprSchema：解析期签名登记
        // -----------------------------------------------------------------

        [Fact]
        public void Schema_KnownKeys_ReturnPreciseSignatures()
        {
            Assert.True(RulesExprSchema.Instance.TryGetSignature(ExprGroups.Self, "hp_pct", out var sig));
            Assert.Equal(ExprValueKind.Number, sig.ReturnKind);

            Assert.True(RulesExprSchema.Instance.TryGetSignature(ExprGroups.Enemies, "count_in_range", out var sig2));
            Assert.Equal(ExprValueKind.Int, sig2.ReturnKind);
        }

        [Fact]
        public void Schema_KnownGroupUnknownKey_PermissivelyAccepted()
        {
            // event.<field> 具体字段名不逐条登记，但 event 是九个已知分组之一，仍应被接受为合法引用
            // （见 RulesExprSchema 判断记录"已知分组放行"）。
            Assert.True(RulesExprSchema.Instance.TryGetSignature(ExprGroups.Event, "any_dynamic_field", out _));
            Assert.True(RulesExprSchema.Instance.TryGetSignature(ExprGroups.World, "any_key", out _));
        }

        [Fact]
        public void Schema_UnknownGroup_Rejected()
        {
            Assert.False(RulesExprSchema.Instance.TryGetSignature("not_a_real_group", "x", out _));
        }

        [Fact]
        public void Schema_TargetSpecificSelfOnlyKeys_FallBackToPermissive()
        {
            // distance_to_target/threat_top 只在 self 分组精确登记；target 分组下同名 key 落回
            // "已知分组放行"（Permissive 签名），而不是被拒绝。
            Assert.True(RulesExprSchema.Instance.TryGetSignature(ExprGroups.Target, "distance_to_target", out var sig));
            Assert.Equal(ExprValueKind.Bool, sig.ReturnKind); // Permissive 占位签名
        }
    }
}
