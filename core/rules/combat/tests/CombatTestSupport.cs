using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SimLoop;
using Core.Numbers.Faction;
using Core.Numbers.PowerSet;
using Core.Numbers.StatBlock;
using Core.Rules.Combat;
using Core.Rules.Common;

namespace Tests.Rules.Combat
{
    // -----------------------------------------------------------------
    // Fakes（任务书要求：IUnitAccess、IAuraQuery 用 Fake，其余用真实实现）
    // -----------------------------------------------------------------

    /// <summary>最小可控的 <see cref="IUnitAccess"/> 假实现：纯内存字典，供测试直接摆布
    /// 存在性/阵营/等级/存活状态，不接入任何真实的 05 <c>Unit</c>/世界模拟。</summary>
    internal sealed class FakeUnitAccess : IUnitAccess
    {
        private sealed class Rec
        {
            public bool Exists = true;
            public Vec2 Position;
            public Id Faction;
            public int Level = 1;
            public double Facing = 0.0;
            public bool Alive = true;
            public Id? TemplateId = null;
            public List<Id> Tags = new List<Id>();
        }

        private readonly Dictionary<Id, Rec> _units = new Dictionary<Id, Rec>();
        private readonly List<Id> _order = new List<Id>();

        public FakeUnitAccess Add(Id unitId, Id faction, int level = 1, bool alive = true)
        {
            if (!_units.ContainsKey(unitId))
            {
                _order.Add(unitId);
            }
            _units[unitId] = new Rec { Faction = faction, Level = level, Alive = alive };
            return this;
        }

        /// <summary>N05 收边补齐（外部审计 68c9bed）：模拟单位从世界模拟中被移除（Despawn），
        /// 与真实 <c>WorldUnitAccess.Require</c>（<c>_world.GetEntity(unitId) is Unit</c> 为假即抛
        /// 异常）语义对齐——不是简单删字典项（那样 <see cref="Exists"/> 会退化为"从未 Add 过"，
        /// 丢失"曾经存在过、现在没了"这一区分），而是保留记录但翻转 <see cref="Rec.Exists"/>，
        /// <see cref="Require"/> 现在同样检查该标志位。</summary>
        public FakeUnitAccess Despawn(Id unitId)
        {
            if (_units.TryGetValue(unitId, out var r))
            {
                r.Exists = false;
            }

            return this;
        }

        public bool Exists(Id unitId) => _units.TryGetValue(unitId, out var r) && r.Exists;

        public IReadOnlyList<Id> AllUnits
        {
            get
            {
                var list = new List<Id>(_order);
                list.Sort();
                return list;
            }
        }

        public Vec2 GetPosition(Id unitId) => Require(unitId).Position;

        public void SetPosition(Id unitId, Vec2 position) => Require(unitId).Position = position;

        public Id GetFaction(Id unitId) => Require(unitId).Faction;

        public int GetLevel(Id unitId) => Require(unitId).Level;

        public double GetFacing(Id unitId) => Require(unitId).Facing;

        public bool IsAlive(Id unitId) => _units.TryGetValue(unitId, out var r) && r.Exists && r.Alive;

        public void SetAlive(Id unitId, bool alive) => Require(unitId).Alive = alive;

        public Id? GetTemplateId(Id unitId) => Require(unitId).TemplateId;

        public IReadOnlyList<Id> GetTags(Id unitId) => Require(unitId).Tags;

        private Rec Require(Id unitId)
        {
            // N05 收边补齐：与真实 WorldUnitAccess.Require 对齐——Despawn 过的单位（Exists=false）
            // 同样应当在这里抛异常，不能因为字典里还留着记录就放行。
            if (!_units.TryGetValue(unitId, out var r) || !r.Exists)
            {
                throw new InvalidOperationException($"FakeUnitAccess: 单位 \"{unitId}\" 未 Add 或已 Despawn");
            }
            return r;
        }
    }

    /// <summary>最小可控的 <see cref="IAuraQuery"/> 假实现：免疫、吸收池均由测试显式配置，
    /// 不实现真实光环系统（skill 模块并行开发中，本模块按任务书不得引用其具体类型）。</summary>
    internal sealed class FakeAuraQuery : IAuraQuery
    {
        private readonly HashSet<(Id unit, Id school, EffectKind kind)> _immune = new HashSet<(Id, Id, EffectKind)>();
        private readonly Dictionary<Id, double> _absorb = new Dictionary<Id, double>();
        private readonly Dictionary<Id, ControlFlags> _controlFlags = new Dictionary<Id, ControlFlags>();

        public FakeAuraQuery SetImmune(Id unitId, Id school, EffectKind kind)
        {
            _immune.Add((unitId, school, kind));
            return this;
        }

        /// <summary>ADR-0070：<see cref="AutoAttackHostSwingEventTests"/> 需要摆布
        /// <see cref="ControlFlags.NoAttack"/> 门控——此前本假实现 <see cref="GetControlFlags"/> 硬
        /// 恒返回 <see cref="ControlFlags.None"/>，未提供任何配置口子。</summary>
        public FakeAuraQuery SetControlFlags(Id unitId, ControlFlags flags)
        {
            _controlFlags[unitId] = flags;
            return this;
        }

        public FakeAuraQuery SetAbsorb(Id unitId, double amount)
        {
            _absorb[unitId] = amount;
            return this;
        }

        public double RemainingAbsorb(Id unitId) => _absorb.TryGetValue(unitId, out var v) ? v : 0.0;

        public bool HasAura(Id unitId, Id auraDefId) => false;

        public int GetStacks(Id unitId, Id auraDefId) => 0;

        public ControlFlags GetControlFlags(Id unitId) => _controlFlags.TryGetValue(unitId, out var f) ? f : ControlFlags.None;

        public bool IsImmune(Id unitId, Id school, EffectKind kind) => _immune.Contains((unitId, school, kind));

        public double ConsumeAbsorb(Id unitId, Id school, double amount)
        {
            var pool = _absorb.TryGetValue(unitId, out var v) ? v : 0.0;
            var consumed = Math.Min(pool, amount);
            _absorb[unitId] = pool - consumed;
            return consumed;
        }

        public IReadOnlyList<Id> GetActiveAuraDefs(Id unitId) => Array.Empty<Id>();
    }

    /// <summary>阶段 3 整理"事项三"：最小可控的 <see cref="IStaticImmunityProvider"/> 假实现，
    /// 供 <see cref="ResolverHitTableTests"/> 验证 <see cref="Core.Rules.Combat.Resolver"/> 在光环
    /// 免疫（<see cref="FakeAuraQuery"/>）之外正确叠加内容驱动的静态免疫。</summary>
    internal sealed class FakeStaticImmunityProvider : IStaticImmunityProvider
    {
        private readonly HashSet<(Id unit, Id school, EffectKind kind)> _immune = new HashSet<(Id, Id, EffectKind)>();

        public FakeStaticImmunityProvider SetImmune(Id unitId, Id school, EffectKind kind)
        {
            _immune.Add((unitId, school, kind));
            return this;
        }

        public bool IsImmune(Id unitId, Id school, EffectKind kind) => _immune.Contains((unitId, school, kind));

        public ControlFlags GetControlImmunity(Id unitId) => ControlFlags.None;
    }

    /// <summary>T-N1-8：最小可控的 <see cref="IGearLevelOffsetProvider"/> 假实现，供
    /// <see cref="Core.Rules.Combat.CombatOptions.EffectiveLevelIncludesGearOffset"/> 策略项测试用——
    /// 按单位显式配置一个偏移量，未配置的单位恒返回 0（与
    /// <see cref="Core.Rules.Common.NullGearLevelOffsetProvider"/> 同样的缺省语义，但本类允许区分
    /// "显式配置为 0"与"从未配置"，供测试按需选用）。</summary>
    internal sealed class FakeGearLevelOffsetProvider : IGearLevelOffsetProvider
    {
        private readonly Dictionary<Id, double> _offsets = new Dictionary<Id, double>();

        public FakeGearLevelOffsetProvider SetOffset(Id unitId, double offset)
        {
            _offsets[unitId] = offset;
            return this;
        }

        public double GetGearLevelOffset(Id unitId) => _offsets.TryGetValue(unitId, out var v) ? v : 0.0;
    }

    /// <summary>
    /// 测试夹具：搭建真实 <see cref="IStatHost"/>/<see cref="IPowerHost"/>/<see cref="IRngHost"/>/
    /// <see cref="IEventBus"/>/<see cref="IFactionMatrix"/>/<see cref="IDataRegistry"/>
    /// （<see cref="InMemoryDataSource"/>）+ Fake <see cref="IUnitAccess"/>/<see cref="IAuraQuery"/>，
    /// 组装一个可用的 <see cref="Core.Rules.Combat.CombatHost"/>（见任务书"tests"一节）。
    /// </summary>
    internal static class CombatTestSupport
    {
        public static readonly Id FactionParty = new Id("fac.combat_test_party");
        public static readonly Id FactionHorde = new Id("fac.combat_test_horde");

        public static readonly Id StatArmor = new Id("stat.armor");
        public static readonly Id StatDamageDonePct = new Id("stat.damage_done_pct");
        public static readonly Id StatDamageTakenPct = new Id("stat.damage_taken_pct");
        public static readonly Id StatHealingDonePct = new Id("stat.healing_done_pct");
        public static readonly Id StatBlockValue = new Id("stat.block_value");

        /// <summary>T-N1-7：category="defense"、scope="from_player" 的目标承伤减免属性，供
        /// <see cref="CombatOptions.DamageTakenPctStats"/> 显式清单测试用——单机下只在
        /// <c>SourceKind.Player</c> 时生效。</summary>
        public static readonly Id StatResilDamageTakenFromPlayerPct = new Id("stat.resil_damage_taken_from_player_pct");

        /// <summary>T-N1-7：category="defense"、scope="any" 的目标承伤减免属性，两种来源类别均生效。</summary>
        public static readonly Id StatResilDamageTakenAnyPct = new Id("stat.resil_damage_taken_any_pct");

        /// <summary>T-N1-7：category="defense"、scope="from_player" 的被暴击减免属性，供
        /// <see cref="CombatOptions.CritTakenReductionStats"/> 显式清单测试用。</summary>
        public static readonly Id StatResilCritTakenFromPlayerPct = new Id("stat.resil_crit_taken_from_player_pct");

        /// <summary>T-N1-7：category="defense"、scope="any" 的被暴击减免属性，两种来源类别均生效。</summary>
        public static readonly Id StatResilCritTakenAnyPct = new Id("stat.resil_crit_taken_any_pct");

        /// <summary>T-N1-8：攻击者命中属性，供 <c>combat.hit_table_config</c> miss 分支的
        /// <c>hit_stat</c> 字段测试用（见 <see cref="Core.Rules.Combat.HitTableBranch.HitStat"/>）。</summary>
        public static readonly Id StatHitRating = new Id("stat.hit_rating");

        public static readonly Id SchoolPhysical = new Id("school.physical");

        private const string StatDefinitionJson = @"
        {
            ""table"": ""stat.definition"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""stat.armor"", ""name_key"": ""l10n.stat.armor.name"", ""category"": ""defense"", ""default_base"": 0 },
                { ""id"": ""stat.damage_done_pct"", ""name_key"": ""l10n.stat.damage_done_pct.name"", ""group"": ""secondary"", ""default_base"": 0 },
                { ""id"": ""stat.damage_taken_pct"", ""name_key"": ""l10n.stat.damage_taken_pct.name"", ""group"": ""secondary"", ""default_base"": 0 },
                { ""id"": ""stat.healing_done_pct"", ""name_key"": ""l10n.stat.healing_done_pct.name"", ""group"": ""secondary"", ""default_base"": 0 },
                { ""id"": ""stat.block_value"", ""name_key"": ""l10n.stat.block_value.name"", ""group"": ""secondary"", ""default_base"": 0 },
                { ""id"": ""stat.resil_damage_taken_from_player_pct"", ""name_key"": ""l10n.stat.resil_damage_taken_from_player_pct.name"", ""category"": ""defense"", ""scope"": ""from_player"", ""default_base"": 0 },
                { ""id"": ""stat.resil_damage_taken_any_pct"", ""name_key"": ""l10n.stat.resil_damage_taken_any_pct.name"", ""category"": ""defense"", ""scope"": ""any"", ""default_base"": 0 },
                { ""id"": ""stat.resil_crit_taken_from_player_pct"", ""name_key"": ""l10n.stat.resil_crit_taken_from_player_pct.name"", ""category"": ""defense"", ""scope"": ""from_player"", ""default_base"": 0 },
                { ""id"": ""stat.resil_crit_taken_any_pct"", ""name_key"": ""l10n.stat.resil_crit_taken_any_pct.name"", ""category"": ""defense"", ""scope"": ""any"", ""default_base"": 0 },
                { ""id"": ""stat.hit_rating"", ""name_key"": ""l10n.stat.hit_rating.name"", ""group"": ""secondary"", ""default_base"": 0 }
            ]
        }";

        private const string FactionJson = @"
        {
            ""table"": ""fac.faction"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""fac.combat_test_party"", ""name_key"": ""l10n.fac.party.name"", ""default_reaction"": ""friendly"" },
                { ""id"": ""fac.combat_test_horde"", ""name_key"": ""l10n.fac.horde.name"", ""default_reaction"": ""hostile"" }
            ]
        }";

        private const string ReactionMatrixJson = @"
        {
            ""table"": ""fac.reaction_matrix"",
            ""schema_version"": 1,
            ""rows"": []
        }";

        // 命中表：每一行都把结果强制成某个可预期的确定性分支——概率字段直接用 base=1（掷骰值域
        // 是 [0,1)，roll < 1 恒成立）或 base=0（恒不成立），配合 enabled 开关，覆盖任务书要求的
        // 全部分支组合，不需要为了让某个分支命中而预先探测 RngHost 的输出序列。
        private const string HitTableJson = @"
        {
            ""table"": ""combat.hit_table_config"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""combat.hit_table.default"",
                  ""miss"": {""enabled"": false, ""base"": 0}, ""dodge"": {""enabled"": false, ""base"": 0},
                  ""parry"": {""enabled"": false, ""base"": 0}, ""glancing_blow"": {""enabled"": false, ""base"": 0},
                  ""block"": {""enabled"": false, ""base"": 0}, ""crit"": {""enabled"": false, ""base"": 0},
                  ""crit_multiplier_base"": 2.0 },

                { ""id"": ""combat.hit_table.miss_forced"",
                  ""miss"": {""enabled"": true, ""base"": 1}, ""dodge"": {""enabled"": false, ""base"": 0},
                  ""parry"": {""enabled"": false, ""base"": 0}, ""glancing_blow"": {""enabled"": false, ""base"": 0},
                  ""block"": {""enabled"": false, ""base"": 0}, ""crit"": {""enabled"": false, ""base"": 0},
                  ""crit_multiplier_base"": 2.0 },

                { ""id"": ""combat.hit_table.dodge_forced"",
                  ""miss"": {""enabled"": false, ""base"": 0}, ""dodge"": {""enabled"": true, ""base"": 1},
                  ""parry"": {""enabled"": false, ""base"": 0}, ""glancing_blow"": {""enabled"": false, ""base"": 0},
                  ""block"": {""enabled"": false, ""base"": 0}, ""crit"": {""enabled"": false, ""base"": 0},
                  ""crit_multiplier_base"": 2.0 },

                { ""id"": ""combat.hit_table.parry_enabled"",
                  ""miss"": {""enabled"": false, ""base"": 0}, ""dodge"": {""enabled"": false, ""base"": 0},
                  ""parry"": {""enabled"": true, ""base"": 1}, ""glancing_blow"": {""enabled"": false, ""base"": 0},
                  ""block"": {""enabled"": false, ""base"": 0}, ""crit"": {""enabled"": false, ""base"": 0},
                  ""crit_multiplier_base"": 2.0 },

                { ""id"": ""combat.hit_table.parry_disabled"",
                  ""miss"": {""enabled"": false, ""base"": 0}, ""dodge"": {""enabled"": false, ""base"": 0},
                  ""parry"": {""enabled"": false, ""base"": 1}, ""glancing_blow"": {""enabled"": false, ""base"": 0},
                  ""block"": {""enabled"": false, ""base"": 0}, ""crit"": {""enabled"": false, ""base"": 0},
                  ""crit_multiplier_base"": 2.0 },

                { ""id"": ""combat.hit_table.glancing_enabled"",
                  ""miss"": {""enabled"": false, ""base"": 0}, ""dodge"": {""enabled"": false, ""base"": 0},
                  ""parry"": {""enabled"": false, ""base"": 0}, ""glancing_blow"": {""enabled"": true, ""base"": 1},
                  ""block"": {""enabled"": false, ""base"": 0}, ""crit"": {""enabled"": false, ""base"": 0},
                  ""crit_multiplier_base"": 2.0, ""glancing_damage_pct"": 0.5 },

                { ""id"": ""combat.hit_table.glancing_disabled"",
                  ""miss"": {""enabled"": false, ""base"": 0}, ""dodge"": {""enabled"": false, ""base"": 0},
                  ""parry"": {""enabled"": false, ""base"": 0}, ""glancing_blow"": {""enabled"": false, ""base"": 1},
                  ""block"": {""enabled"": false, ""base"": 0}, ""crit"": {""enabled"": false, ""base"": 0},
                  ""crit_multiplier_base"": 2.0, ""glancing_damage_pct"": 0.5 },

                { ""id"": ""combat.hit_table.block_enabled"",
                  ""miss"": {""enabled"": false, ""base"": 0}, ""dodge"": {""enabled"": false, ""base"": 0},
                  ""parry"": {""enabled"": false, ""base"": 0}, ""glancing_blow"": {""enabled"": false, ""base"": 0},
                  ""block"": {""enabled"": true, ""base"": 1}, ""crit"": {""enabled"": false, ""base"": 0},
                  ""crit_multiplier_base"": 2.0, ""block_value_stat"": ""stat.block_value"" },

                { ""id"": ""combat.hit_table.block_disabled"",
                  ""miss"": {""enabled"": false, ""base"": 0}, ""dodge"": {""enabled"": false, ""base"": 0},
                  ""parry"": {""enabled"": false, ""base"": 0}, ""glancing_blow"": {""enabled"": false, ""base"": 0},
                  ""block"": {""enabled"": false, ""base"": 1}, ""crit"": {""enabled"": false, ""base"": 0},
                  ""crit_multiplier_base"": 2.0, ""block_value_stat"": ""stat.block_value"" },

                { ""id"": ""combat.hit_table.crit_forced"",
                  ""miss"": {""enabled"": false, ""base"": 0}, ""dodge"": {""enabled"": false, ""base"": 0},
                  ""parry"": {""enabled"": false, ""base"": 0}, ""glancing_blow"": {""enabled"": false, ""base"": 0},
                  ""block"": {""enabled"": false, ""base"": 0}, ""crit"": {""enabled"": true, ""base"": 1},
                  ""crit_multiplier_base"": 2.0 },

                { ""id"": ""combat.hit_table.probabilistic"",
                  ""miss"": {""enabled"": true, ""base"": 0.5}, ""dodge"": {""enabled"": true, ""base"": 0.3},
                  ""parry"": {""enabled"": false, ""base"": 0}, ""glancing_blow"": {""enabled"": false, ""base"": 0},
                  ""block"": {""enabled"": false, ""base"": 0}, ""crit"": {""enabled"": true, ""base"": 0.5},
                  ""crit_multiplier_base"": 2.0 },

                { ""id"": ""combat.hit_table.glancing_and_crit_forced"",
                  ""miss"": {""enabled"": false, ""base"": 0}, ""dodge"": {""enabled"": false, ""base"": 0},
                  ""parry"": {""enabled"": false, ""base"": 0}, ""glancing_blow"": {""enabled"": true, ""base"": 1},
                  ""block"": {""enabled"": false, ""base"": 0}, ""crit"": {""enabled"": true, ""base"": 1},
                  ""crit_multiplier_base"": 3.0, ""glancing_damage_pct"": 0.4 },

                { ""id"": ""combat.hit_table.block_and_crit_forced"",
                  ""miss"": {""enabled"": false, ""base"": 0}, ""dodge"": {""enabled"": false, ""base"": 0},
                  ""parry"": {""enabled"": false, ""base"": 0}, ""glancing_blow"": {""enabled"": false, ""base"": 0},
                  ""block"": {""enabled"": true, ""base"": 1}, ""crit"": {""enabled"": true, ""base"": 1},
                  ""crit_multiplier_base"": 2.0, ""block_value_stat"": ""stat.block_value"" },

                { ""id"": ""combat.hit_table.miss_forced_with_hit_stat"",
                  ""miss"": {""enabled"": true, ""base"": 1, ""hit_stat"": ""stat.hit_rating""},
                  ""dodge"": {""enabled"": false, ""base"": 0}, ""parry"": {""enabled"": false, ""base"": 0},
                  ""glancing_blow"": {""enabled"": false, ""base"": 0}, ""block"": {""enabled"": false, ""base"": 0},
                  ""crit"": {""enabled"": false, ""base"": 0}, ""crit_multiplier_base"": 2.0 },

                { ""id"": ""combat.hit_table.level_diff_probe"",
                  ""miss"": {""enabled"": true, ""base"": 0.5}, ""dodge"": {""enabled"": false, ""base"": 0},
                  ""parry"": {""enabled"": false, ""base"": 0}, ""glancing_blow"": {""enabled"": false, ""base"": 0},
                  ""block"": {""enabled"": false, ""base"": 0}, ""crit"": {""enabled"": false, ""base"": 0},
                  ""crit_multiplier_base"": 2.0 },

                { ""id"": ""combat.hit_table.level_diff_crit_zero_base"",
                  ""miss"": {""enabled"": false, ""base"": 0}, ""dodge"": {""enabled"": false, ""base"": 0},
                  ""parry"": {""enabled"": false, ""base"": 0}, ""glancing_blow"": {""enabled"": false, ""base"": 0},
                  ""block"": {""enabled"": false, ""base"": 0}, ""crit"": {""enabled"": true, ""base"": 0},
                  ""crit_multiplier_base"": 2.0 }
            ]
        }";

        // T-N1-8：等级差规则表——横轴 Δ = 目标有效等级 − 攻击者有效等级（miss_bonus/crit_suppression）；
        // grey_line 横轴是攻击者有效等级本身。数值只服务测试（|Δ|<=2 每级加得少，>=3 陡增并封顶到
        // ±1.0，双向生效），不进框架默认——见 CombatSchemas.LevelDiffTable 判断记录。
        private const string LevelDiffTableJson = @"
        {
            ""table"": ""combat.level_diff_table"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""combat.level_diff.test_matrix"",
                  ""miss_bonus"": [
                    {""x"": -5, ""y"": -1.0}, {""x"": -3, ""y"": -1.0}, {""x"": -2, ""y"": -0.04},
                    {""x"": -1, ""y"": -0.02}, {""x"": 0, ""y"": 0.0}, {""x"": 1, ""y"": 0.02},
                    {""x"": 2, ""y"": 0.04}, {""x"": 3, ""y"": 1.0}, {""x"": 5, ""y"": 1.0}
                  ],
                  ""crit_suppression"": [
                    {""x"": -5, ""y"": -1.0}, {""x"": -3, ""y"": -1.0}, {""x"": -2, ""y"": -0.02},
                    {""x"": -1, ""y"": -0.01}, {""x"": 0, ""y"": 0.0}, {""x"": 1, ""y"": 0.01},
                    {""x"": 2, ""y"": 0.02}, {""x"": 3, ""y"": 1.0}, {""x"": 5, ""y"": 1.0}
                  ],
                  ""xp_factor"": [
                    {""x"": -5, ""y"": 0.0}, {""x"": 0, ""y"": 1.0}, {""x"": 5, ""y"": 2.0}
                  ],
                  ""grey_line"": [
                    {""x"": 1, ""y"": 2}, {""x"": 10, ""y"": 3}, {""x"": 20, ""y"": 5},
                    {""x"": 30, ""y"": 8}, {""x"": 60, ""y"": 15}
                  ]
                }
            ]
        }";

        // 手算链路：armor=300、attackerLevel=10、k=70 → reduction = 300 / (300 + 70*10) = 0.3。
        private const string ResistCurveJson = @"
        {
            ""table"": ""combat.resist_curve"",
            ""schema_version"": 1,
            ""rows"": [
                { ""id"": ""combat.resist.physical_test"", ""school"": ""school.physical"", ""kind"": ""saturation"", ""k"": 70, ""max_reduction"": 0.75 }
            ]
        }";

        public static IEventBus MakeBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(DataRegistryEventKeys.LoadCompleted, "data",
                    new[] { "tableCount", "recordCount", "errorCount", "warningCount" }),
                new EventDefinition(DataRegistryEventKeys.ValidationFailed, "data",
                    new[] { "errorCount", "warningCount" }),
                new EventDefinition(StatBlockEventKeys.StatChanged, "stat",
                    new[] { "unitId", "stat", "oldValue", "newValue" }),
                new EventDefinition(PowerEventKeys.Changed, "power",
                    new[] { "unitId", "powerType", "oldValue", "newValue" }),
                new EventDefinition(PowerEventKeys.Depleted, "power",
                    new[] { "unitId", "powerType" }),
                new EventDefinition(FactionEventKeys.RelationChanged, "faction",
                    new[] { "from", "to", "oldReaction", "newReaction" }),
                new EventDefinition(RulesEventKeys.CombatDamageDealt, "combat",
                    new[] { "sourceId", "targetId", "school", "amount", "isCrit", "hitResult" }),
                // ADR-0098：未命中/闪避/招架/免疫四类回避类结算结局统一发布，见
                // CombatAttackAvoidedEvent 判断记录。
                new EventDefinition(RulesEventKeys.CombatAttackAvoided, "combat",
                    new[] { "sourceId", "targetId", "school", "hitResult", "skillId" }),
                // ADR-0070：AutoAttackHostSwingEventTests 需要用真实 EventBus（StrictCatalog 默认
                // true）验证挥击广播，登记本键。
                new EventDefinition(RulesEventKeys.CombatAutoAttackSwing, "combat",
                    new[] { "sourceId", "targetId" }),
                new EventDefinition(RulesEventKeys.CombatHealDone, "combat",
                    new[] { "sourceId", "targetId", "amount", "isCrit" }),
                new EventDefinition(RulesEventKeys.CombatThreatChanged, "combat",
                    new[] { "unitId", "sourceId", "oldValue", "newValue" }),
                new EventDefinition(RulesEventKeys.CombatEntered, "combat", new[] { "unitId", "hostileId" }),
                new EventDefinition(RulesEventKeys.CombatLeft, "combat", new[] { "unitId" }),
                new EventDefinition(RulesEventKeys.UnitDied, "unit", new[] { "unitId", "killerId" }),
                new EventDefinition(RulesEventKeys.UnitRespawned, "unit", new[] { "unitId", "policy" }),
                // RC-02 收边补齐：CombatHost 订阅 entity.destroyed 做战斗状态清理（见该类型构造函数
                // 判断记录），测试夹具的严格事件目录（StrictCatalog 默认 true）需要登记这个 key，
                // 测试才能用 fx.Bus.Enqueue(new EntityDestroyedEvent(...)) 模拟单位销毁。
                new EventDefinition(SimEventKeys.EntityDestroyed, "entity", new[] { "entityId" }),
            });
            return new EventBus(catalog);
        }

        public static IDataRegistry MakeRegistry(IEventBus bus)
        {
            var source = new InMemoryDataSource()
                .Add("stat.definition", StatDefinitionJson)
                .Add("fac.faction", FactionJson)
                .Add("fac.reaction_matrix", ReactionMatrixJson)
                .Add("combat.hit_table_config", HitTableJson)
                .Add("combat.resist_curve", ResistCurveJson)
                .Add("combat.level_diff_table", LevelDiffTableJson);

            var registry = new DataRegistry(source, bus, new DataRegistryOptions());
            registry.RegisterSchema(StatSchemas.Definition);
            registry.RegisterSchema(FacSchemas.Faction);
            registry.RegisterSchema(FacSchemas.ReactionMatrix);
            registry.RegisterSchema(Core.Rules.Combat.CombatSchemas.HitTableConfig);
            registry.RegisterSchema(Core.Rules.Combat.CombatSchemas.ResistCurve);
            // T-N1-8：level_diff_table 随本夹具一起加载（数据始终存在），是否真正接入 Δ 计算由各
            // 测试自己的 CombatOptions.LevelDiffTableId 决定（缺省 null 不接表，见该属性判断记录）。
            registry.RegisterSchema(Core.Rules.Combat.CombatSchemas.LevelDiffTable);
            registry.RegisterValidationRule(new Core.Rules.Combat.CombatHitTableValidationRule());
            registry.RegisterValidationRule(new Core.Rules.Combat.CombatResistCurveValidationRule());

            var report = registry.LoadAll();
            if (report.IsBlocking)
            {
                throw new InvalidOperationException("CombatTestSupport 夹具数据未通过校验：" + string.Join("; ", report.Issues));
            }

            return registry;
        }

        public static IPowerHost MakePowerHost(IEventBus bus)
        {
            var health = PowerTestType();
            return new PowerHost(new[] { health }, bus);
        }

        private static PowerTypeDefinition PowerTestType()
        {
            var json = "{"
                + "\"id\": \"" + WellKnownPowers.Health + "\","
                + "\"name_key\": \"l10n.power.health.name\","
                + "\"max_source\": {\"kind\": \"fixed\", \"value\": 1000},"
                + "\"regen_in_combat\": 0, \"regen_out_of_combat\": 0, \"decay_out_of_combat\": 0,"
                + "\"refill_on_leave_combat\": false, \"start_full\": true, \"allow_overflow\": false, \"min\": 0"
                + "}";
            var obj = (Core.Foundation.Common.Json.JsonObject)Core.Foundation.Common.Json.JsonReader.Parse(json);
            var record = new DataRecord(PowerSchemas.PowerType, WellKnownPowers.Health.Value, WellKnownPowers.Health, obj);
            return new PowerTypeDefinition(record);
        }

        public sealed class Fixture
        {
            public IEventBus Bus = null!;
            public IDataRegistry Registry = null!;
            public IStatHost Stats = null!;
            public IPowerHost Powers = null!;
            public IFactionMatrix Factions = null!;
            public IRngHost Rng = null!;
            public FakeUnitAccess Units = null!;
            public FakeAuraQuery Auras = null!;
            public FakeStaticImmunityProvider StaticImmunity = null!;
            public FakeGearLevelOffsetProvider GearLevelOffset = null!;
            public InMemoryCombatDiagnostics Diagnostics = null!;
            public Core.Rules.Combat.CombatHost Host = null!;
            public List<IEvent> Events = null!;
        }

        /// <summary>组装一整套夹具；<paramref name="configureOptions"/> 供每个测试按需覆盖
        /// <see cref="Core.Rules.Combat.CombatOptions"/>（如 <c>HitTableConfigId</c>）。
        /// <paramref name="seed"/> 供确定性测试固定/复现 RngHost 序列。</summary>
        public static Fixture Build(Action<Core.Rules.Combat.CombatOptions>? configureOptions = null, ulong seed = 12345)
        {
            var bus = MakeBus();
            var events = new List<IEvent>();
            bus.Subscribe(RulesEventKeys.CombatDamageDealt, e => events.Add(e));
            bus.Subscribe(RulesEventKeys.CombatAttackAvoided, e => events.Add(e));
            bus.Subscribe(RulesEventKeys.CombatHealDone, e => events.Add(e));
            bus.Subscribe(RulesEventKeys.CombatThreatChanged, e => events.Add(e));
            bus.Subscribe(RulesEventKeys.CombatEntered, e => events.Add(e));
            bus.Subscribe(RulesEventKeys.CombatLeft, e => events.Add(e));
            bus.Subscribe(RulesEventKeys.UnitDied, e => events.Add(e));

            var registry = MakeRegistry(bus);
            var stats = new StatHost(registry, bus);
            var powers = MakePowerHost(bus);
            var factions = new FactionMatrix(registry, bus);
            var rng = new RngHost(seed);
            var units = new FakeUnitAccess();
            var auras = new FakeAuraQuery();
            var staticImmunity = new FakeStaticImmunityProvider();
            var gearLevelOffset = new FakeGearLevelOffsetProvider();
            var diagnostics = new InMemoryCombatDiagnostics();

            var options = new Core.Rules.Combat.CombatOptions
            {
                HitTableConfigId = new Id("combat.hit_table.default"),
                ArmorStat = StatArmor,
                DamageDonePctStat = StatDamageDonePct,
                DamageTakenPctStat = StatDamageTakenPct,
                HealingDonePctStat = StatHealingDonePct,
                PhysicalSchool = SchoolPhysical,
            };
            configureOptions?.Invoke(options);

            // T-N1-8：经十二参数新重载装配（见 CombatHost 该重载判断记录），全部测试夹具统一走这条
            // 装配路径，默认附带一个空配置的 FakeGearLevelOffsetProvider——未显式 SetOffset 的单位
            // 恒返回 0，与 NullGearLevelOffsetProvider 行为等价，覆盖"开启策略项但未配置真实偏移来源
            // 时不抛异常、退化为 0"这一路径。
            var host = new Core.Rules.Combat.CombatHost(
                stats, powers, units, auras, factions, rng, bus, registry, options, diagnostics,
                staticImmunity, gearLevelOffset);

            return new Fixture
            {
                Bus = bus,
                Registry = registry,
                Stats = stats,
                Powers = powers,
                Factions = factions,
                Rng = rng,
                Units = units,
                Auras = auras,
                StaticImmunity = staticImmunity,
                GearLevelOffset = gearLevelOffset,
                Diagnostics = diagnostics,
                Host = host,
                Events = events,
            };
        }

        /// <summary>注册一个单位到 Fake 单位表 + StatHost + PowerHost（Health），供多数测试复用。</summary>
        public static void RegisterUnit(Fixture fx, Id unitId, Id faction, int level = 1, bool alive = true)
        {
            fx.Units.Add(unitId, faction, level, alive);
            fx.Stats.RegisterUnit(unitId);
            fx.Powers.RegisterUnit(unitId, new[] { WellKnownPowers.Health });
        }

        /// <summary>DispatchPending 的简写，供需要真正把 Enqueue 的事件派发给订阅者的测试调用。</summary>
        public static int Dispatch(Fixture fx) => fx.Bus.DispatchPending();
    }
}
