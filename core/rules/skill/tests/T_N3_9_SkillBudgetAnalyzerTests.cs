using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Rules.Ai;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// T-N3-9（[ADR-0031](../../../../architecture/adr/0031-技能数值契约与预算.md) 决策 2；06 第
    /// 3.10 节；04 第 5 节数值类校验项分级表）：<see cref="SkillBudgetAnalyzer"/> 的四种结论
    /// （<see cref="SkillBudgetVerdict"/>）、习得等级/档位反查（<see
    /// cref="SkillDefCache.TryResolveBudgetAttribution"/>）覆盖。
    /// <para>
    /// 验收标准对照（分阶段落地计划第 14 节 T-N3-9 行）："四种技能四种结果各 1 组" ——
    /// <see cref="Pass_RatioWithinBandwidth"/>/<see
    /// cref="UnconfirmedDeviation_OverBandwidth_NoBudgetNote"/>/<see
    /// cref="ConfirmedDeviation_OverBandwidth_WithBudgetNote"/>/<see
    /// cref="HardCapExceeded_OverHardCap_NoBudgetNote"/>；"怪物档 1 组" —— <see
    /// cref="MonsterTier_OnlyReferencedByCreatureTemplate_UsesMonsterBandwidth"/>；额外覆盖
    /// <see cref="NotApplicable_NoSettlementEffects"/>（不参与预算校验的技能）、<see
    /// cref="ConfirmedDeviation_OverHardCap_WithBudgetNote_NotBlocked"/>（硬性规则"禁止阻断带说明
    /// 的超模技能"，超硬上限但有说明仍只是警告不是阻断）、习得等级反查取最小值与玩家档优先于怪物档
    /// 两条判断记录的直接验证。
    /// </para>
    /// </summary>
    public sealed class T_N3_9_SkillBudgetAnalyzerTests
    {
        private sealed class FakeAnchorProvider : ISkillBudgetAnchorProvider
        {
            public double AnchorDps { get; set; } = 10.0;

            public double GetAnchorDps(int level) => AnchorDps;

            public double GetExpectedScalingStatValue(Id stat, int level) => 0.0;
        }

        private static string TableJson(string name, IReadOnlyList<JsonObject> rows)
        {
            var root = J.O(
                ("table", J.S(name)),
                ("schema_version", J.N(1)),
                ("rows", new JsonArray(rows.Cast<JsonValue>())));
            return JsonWriter.Write(root);
        }

        // -----------------------------------------------------------------
        // 最小注册表：只登记本测试文件实际用到的表，见类型顶部"最小字段集"取舍。
        // -----------------------------------------------------------------
        private sealed class Registry
        {
            private readonly List<JsonObject> _skillDefs = new List<JsonObject>();
            private readonly List<JsonObject> _books = new List<JsonObject>();
            private readonly List<JsonObject> _budgetRules = new List<JsonObject>();
            private readonly List<JsonObject> _rotations = new List<JsonObject>();
            private readonly List<JsonObject> _creatures = new List<JsonObject>();
            // 深度复审 C（测试覆盖缺口 #4）补测新增：ComputeGrantValue(isAura: true) 需要读
            // skill.aura_def，既有 9 处调用点都只测 isAura: false（技能）路径，本表此前未登记。
            private readonly List<JsonObject> _auraDefs = new List<JsonObject>();

            public Registry SkillDef(JsonObject row) { _skillDefs.Add(row); return this; }
            public Registry Book(JsonObject row) { _books.Add(row); return this; }
            public Registry BudgetRule(JsonObject row) { _budgetRules.Add(row); return this; }
            public Registry Rotation(JsonObject row) { _rotations.Add(row); return this; }
            public Registry Creature(JsonObject row) { _creatures.Add(row); return this; }
            public Registry AuraDef(JsonObject row) { _auraDefs.Add(row); return this; }

            /// <summary>T-N5-3 新增：<see cref="Build(IEnumerable{IValidationRule}?, DataRegistryStrictness)"/>
            /// 每次调用后的完整 <see cref="ValidationReport"/>——既有 9 处 <c>Build()</c> 调用点只关心
            /// 返回的 <see cref="IDataRegistryView"/>（供 <see cref="SkillBudgetAnalyzer.Analyze"/>
            /// 直接读取只读数据），不需要报告本身；只有 W1（技能预算偏离）"WarningsBlock 下不阻断"这类
            /// 需要检查 <see cref="ValidationReport.IsBlocking"/> 的新用例才会读取本属性。</summary>
            public ValidationReport LastReport { get; private set; } = null!;

            public IDataRegistryView Build() => Build(rules: null, strictness: DataRegistryStrictness.WarningsAllowed);

            /// <summary>T-N5-3 新增重载：<paramref name="rules"/>/<paramref name="strictness"/> 均省略
            /// 时与既有零参 <see cref="Build()"/> 行为完全一致（不注册额外规则、
            /// <see cref="DataRegistryStrictness.WarningsAllowed"/>）。</summary>
            public IDataRegistryView Build(IEnumerable<IValidationRule>? rules, DataRegistryStrictness strictness)
            {
                var source = new InMemoryDataSource();
                source.Add("skill.def", TableJson("skill.def", _skillDefs));
                source.Add("skill.book", TableJson("skill.book", _books));
                source.Add("skill.budget_rule", TableJson("skill.budget_rule", _budgetRules));
                source.Add("ai.rotation", TableJson("ai.rotation", _rotations));
                source.Add("creature.template", TableJson("creature.template", _creatures));
                source.Add("skill.aura_def", TableJson("skill.aura_def", _auraDefs));

                var bus = new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                    new EventBusOptions { StrictCatalog = false });
                var registry = new DataRegistry(source, bus, new DataRegistryOptions { FailOnUnknownTable = false, Strictness = strictness });
                registry.RegisterSchema(SkillSchemas.Def);
                registry.RegisterSchema(SkillSchemas.Book);
                registry.RegisterSchema(SkillSchemas.BudgetRule);
                registry.RegisterSchema(AiSchemas.Rotation);
                registry.RegisterSchema(TableSchema.Unschematized("creature.template", "id"));
                registry.RegisterSchema(SkillSchemas.AuraDef);

                if (rules != null)
                {
                    foreach (var rule in rules)
                    {
                        registry.RegisterValidationRule(rule);
                    }
                }

                var report = registry.LoadAll();
                LastReport = report;
                if (report.IsBlocking)
                {
                    throw new InvalidOperationException(
                        "测试数据未通过校验：\n" + string.Join("\n", report.Issues));
                }

                return registry;
            }
        }

        private static JsonObject SchoolDamageSkill(string id, double baseValue, string? budgetNote = null)
        {
            var fields = new List<(string, JsonValue)>
            {
                ("id", J.S(id)),
                ("school", J.S("school.physical")),
                ("kind", J.S("active")),
                ("range", J.N(30)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.n3_9_unused")),
                ("effects", J.A(J.O(
                    ("kind", J.S("school_damage")),
                    ("params", J.O(("base_value", J.N(baseValue)), ("coefficient", J.N(0)), ("school", J.S("school.physical"))))))),
            };
            if (budgetNote != null)
            {
                fields.Add(("budget_note", J.S(budgetNote)));
            }

            return J.O(fields.ToArray());
        }

        private static JsonObject NonSettlementSkill(string id) => J.O(
            ("id", J.S(id)),
            ("school", J.S("school.arcane")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(1)),
            ("respects_gcd", J.B(true)),
            ("target_shape_ref", J.S("target.chain.n3_9_unused")),
            ("effects", J.A(J.O(
                ("kind", J.S("open_lock")),
                ("params", J.O())))));

        private static JsonObject Book(string id, params (int Level, string SkillId)[] entries)
        {
            var rows = new List<JsonValue>();
            foreach (var (level, skillId) in entries)
            {
                rows.Add(J.O(("level", J.N(level)), ("skill_id", J.S(skillId))));
            }

            return J.O(("id", J.S(id)), ("entries", new JsonArray(rows)));
        }

        private static JsonObject DefaultBudgetRule() => J.O(("id", J.S("skill.budget_rule.default")), ("beat_seconds", J.N(1.0)));

        // -----------------------------------------------------------------
        // 四种技能四种结果
        // -----------------------------------------------------------------

        [Fact]
        public void Pass_RatioWithinBandwidth()
        {
            var view = new Registry()
                .SkillDef(SchoolDamageSkill("skill.n3_9_pass", baseValue: 10))
                .Book(Book("skill.book.n3_9", (1, "skill.n3_9_pass")))
                .BudgetRule(DefaultBudgetRule())
                .Build();

            var provider = new FakeAnchorProvider { AnchorDps = 10.0 };
            // 预算上限 = anchorDps(10) × T(max(cast_time=0, beat_seconds=1)=1) × 三条中性倍数(1) = 10；
            // 实际价值 = base_value = 10；比值 = 1.0，落在缺省玩家带宽 [0.8, 1.2] 内。
            var result = SkillBudgetAnalyzer.Analyze(new Id("skill.n3_9_pass"), view, options: null, provider);

            Assert.True(result.Participates);
            Assert.Equal(SkillBudgetTier.Player, result.Tier);
            Assert.Equal(1, result.Level);
            Assert.Equal(1.0, result.Ratio, 6);
            Assert.Equal(SkillBudgetVerdict.Pass, result.Verdict);
        }

        [Fact]
        public void UnconfirmedDeviation_OverBandwidth_NoBudgetNote()
        {
            var view = new Registry()
                .SkillDef(SchoolDamageSkill("skill.n3_9_unconfirmed", baseValue: 15))
                .Book(Book("skill.book.n3_9", (1, "skill.n3_9_unconfirmed")))
                .BudgetRule(DefaultBudgetRule())
                .Build();

            var provider = new FakeAnchorProvider { AnchorDps = 10.0 };
            // 比值 = 15/10 = 1.5，超出带宽上界 1.2，未超硬上限（3.0），未填 budget_note。
            var result = SkillBudgetAnalyzer.Analyze(new Id("skill.n3_9_unconfirmed"), view, options: null, provider);

            Assert.Equal(1.5, result.Ratio, 6);
            Assert.Null(result.BudgetNote);
            Assert.Equal(SkillBudgetVerdict.UnconfirmedDeviation, result.Verdict);
        }

        /// <summary>分阶段落地计划 T-N5-3（数值规则核对表 W1）：不可提升警告在
        /// <see cref="DataRegistryStrictness.WarningsBlock"/> 下不阻断——同
        /// <see cref="UnconfirmedDeviation_OverBandwidth_NoBudgetNote"/> 的数据（比值 1.5，超出玩家
        /// 带宽上界 1.2），但这次真正把 <see cref="SkillBudgetValidationRule"/>（注入真实
        /// <see cref="FakeAnchorProvider"/>——本条也是核对表"锚点依赖"三条之一，<c>anchorProvider</c>
        /// 为 <c>null</c> 时整条规则不产生任何问题，见该类型判断记录）注册进 registry、走完整的
        /// <see cref="DataRegistry.LoadAll()"/> + <see cref="ValidationReport"/> 路径（本文件其余用例
        /// 都是直接调用 <see cref="SkillBudgetAnalyzer.Analyze"/>，从不经过 <c>IValidationRule</c>
        /// 这层，看不到 <see cref="ValidationReport.IsBlocking"/>）。</summary>
        [Fact]
        public void DeviationWarning_UnderWarningsBlock_DoesNotBlock()
        {
            var registryBuilder = new Registry()
                .SkillDef(SchoolDamageSkill("skill.n3_9_deviation_warn_block", baseValue: 15))
                .Book(Book("skill.book.n3_9_deviation_warn_block", (1, "skill.n3_9_deviation_warn_block")))
                .BudgetRule(DefaultBudgetRule());

            var provider = new FakeAnchorProvider { AnchorDps = 10.0 };
            registryBuilder.Build(
                rules: new IValidationRule[] { new SkillBudgetValidationRule(provider) },
                strictness: DataRegistryStrictness.WarningsBlock);

            var report = registryBuilder.LastReport;
            var issue = Assert.Single(report.Issues, i => i.Check == SkillBudgetValidationRule.DeviationCheck);
            Assert.Equal(ValidationSeverity.Warning, issue.Severity);
            Assert.Equal(1, report.NonEscalatableWarningCount);
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
        }

        [Fact]
        public void ConfirmedDeviation_OverBandwidth_WithBudgetNote()
        {
            var view = new Registry()
                .SkillDef(SchoolDamageSkill("skill.n3_9_confirmed", baseValue: 15, budgetNote: "职业定位：单体爆发型，代价是零机动"))
                .Book(Book("skill.book.n3_9", (1, "skill.n3_9_confirmed")))
                .BudgetRule(DefaultBudgetRule())
                .Build();

            var provider = new FakeAnchorProvider { AnchorDps = 10.0 };
            var result = SkillBudgetAnalyzer.Analyze(new Id("skill.n3_9_confirmed"), view, options: null, provider);

            Assert.Equal(1.5, result.Ratio, 6);
            Assert.NotNull(result.BudgetNote);
            Assert.Equal(SkillBudgetVerdict.ConfirmedDeviation, result.Verdict);
        }

        // -----------------------------------------------------------------
        // 深度复审 C-M1 修复回归测试（2026-09-16）：SkillBudgetAnalyzer.Classify 此前只判了带宽
        // 上界（ratio <= 1+bandwidth 一律 Pass），从未判下界（ratio < 1-bandwidth）——任何写小
        // 两个数量级的效果值会静默通过校验。补齐下界后的对称区间行为回归。
        // -----------------------------------------------------------------

        [Fact]
        public void UnconfirmedDeviation_BelowBandwidthLowerBound_NoBudgetNote()
        {
            var view = new Registry()
                .SkillDef(SchoolDamageSkill("skill.n3_9_below_lower_unconfirmed", baseValue: 5))
                .Book(Book("skill.book.n3_9", (1, "skill.n3_9_below_lower_unconfirmed")))
                .BudgetRule(DefaultBudgetRule())
                .Build();

            var provider = new FakeAnchorProvider { AnchorDps = 10.0 };
            // 比值 = 5/10 = 0.5，低于缺省玩家带宽下界 0.8，未超硬上限（不适用，本身就是偏低），
            // 未填 budget_note——C-M1 修复前这里会被误判为 Pass（旧实现只判 ratio <= 1.2）。
            var result = SkillBudgetAnalyzer.Analyze(new Id("skill.n3_9_below_lower_unconfirmed"), view, options: null, provider);

            Assert.Equal(0.5, result.Ratio, 6);
            Assert.Null(result.BudgetNote);
            Assert.Equal(SkillBudgetVerdict.UnconfirmedDeviation, result.Verdict);
        }

        [Fact]
        public void ConfirmedDeviation_BelowBandwidthLowerBound_WithBudgetNote()
        {
            var view = new Registry()
                .SkillDef(SchoolDamageSkill("skill.n3_9_below_lower_confirmed", baseValue: 5,
                    budgetNote: "设计意图：削弱型技能，价值本就低于常规带宽"))
                .Book(Book("skill.book.n3_9", (1, "skill.n3_9_below_lower_confirmed")))
                .BudgetRule(DefaultBudgetRule())
                .Build();

            var provider = new FakeAnchorProvider { AnchorDps = 10.0 };
            var result = SkillBudgetAnalyzer.Analyze(new Id("skill.n3_9_below_lower_confirmed"), view, options: null, provider);

            Assert.Equal(0.5, result.Ratio, 6);
            Assert.NotNull(result.BudgetNote);
            Assert.Equal(SkillBudgetVerdict.ConfirmedDeviation, result.Verdict);
        }

        /// <summary>边界值应判定为 Pass（<c>&gt;=</c>/<c>&lt;=</c> 均取等号）——上界/下界各一组。</summary>
        [Fact]
        public void Pass_RatioExactlyAtBandwidthBoundaries()
        {
            var lowerView = new Registry()
                .SkillDef(SchoolDamageSkill("skill.n3_9_lower_boundary", baseValue: 8)) // 8/10 = 0.8
                .Book(Book("skill.book.n3_9", (1, "skill.n3_9_lower_boundary")))
                .BudgetRule(DefaultBudgetRule())
                .Build();
            var upperView = new Registry()
                .SkillDef(SchoolDamageSkill("skill.n3_9_upper_boundary", baseValue: 12)) // 12/10 = 1.2
                .Book(Book("skill.book.n3_9", (1, "skill.n3_9_upper_boundary")))
                .BudgetRule(DefaultBudgetRule())
                .Build();

            var provider = new FakeAnchorProvider { AnchorDps = 10.0 };
            var lowerResult = SkillBudgetAnalyzer.Analyze(new Id("skill.n3_9_lower_boundary"), lowerView, options: null, provider);
            var upperResult = SkillBudgetAnalyzer.Analyze(new Id("skill.n3_9_upper_boundary"), upperView, options: null, provider);

            Assert.Equal(0.8, lowerResult.Ratio, 6);
            Assert.Equal(SkillBudgetVerdict.Pass, lowerResult.Verdict);
            Assert.Equal(1.2, upperResult.Ratio, 6);
            Assert.Equal(SkillBudgetVerdict.Pass, upperResult.Verdict);
        }

        [Fact]
        public void HardCapExceeded_OverHardCap_NoBudgetNote()
        {
            var view = new Registry()
                .SkillDef(SchoolDamageSkill("skill.n3_9_hardcap", baseValue: 40))
                .Book(Book("skill.book.n3_9", (1, "skill.n3_9_hardcap")))
                .BudgetRule(DefaultBudgetRule())
                .Build();

            var provider = new FakeAnchorProvider { AnchorDps = 10.0 };
            // 比值 = 40/10 = 4.0，超过缺省玩家硬上限 3.0，未填 budget_note——阻断。
            var result = SkillBudgetAnalyzer.Analyze(new Id("skill.n3_9_hardcap"), view, options: null, provider);

            Assert.Equal(4.0, result.Ratio, 6);
            Assert.Null(result.BudgetNote);
            Assert.Equal(SkillBudgetVerdict.HardCapExceeded, result.Verdict);
        }

        /// <summary>硬性规则"禁止阻断带说明的超模技能"：即便比值超过硬上限，只要 <c>budget_note</c>
        /// 非空，结论仍是 <see cref="SkillBudgetVerdict.ConfirmedDeviation"/>（警告），不是 <see
        /// cref="SkillBudgetVerdict.HardCapExceeded"/>（阻断）。</summary>
        [Fact]
        public void ConfirmedDeviation_OverHardCap_WithBudgetNote_NotBlocked()
        {
            var view = new Registry()
                .SkillDef(SchoolDamageSkill("skill.n3_9_overcap_note", baseValue: 40, budgetNote: "Boss 专属斩杀技，靠可躲避平衡"))
                .Book(Book("skill.book.n3_9", (1, "skill.n3_9_overcap_note")))
                .BudgetRule(DefaultBudgetRule())
                .Build();

            var provider = new FakeAnchorProvider { AnchorDps = 10.0 };
            var result = SkillBudgetAnalyzer.Analyze(new Id("skill.n3_9_overcap_note"), view, options: null, provider);

            Assert.Equal(4.0, result.Ratio, 6);
            Assert.NotNull(result.BudgetNote);
            Assert.Equal(SkillBudgetVerdict.ConfirmedDeviation, result.Verdict);
        }

        [Fact]
        public void NotApplicable_NoSettlementEffects()
        {
            var view = new Registry()
                .SkillDef(NonSettlementSkill("skill.n3_9_open_lock"))
                .Book(Book("skill.book.n3_9", (1, "skill.n3_9_open_lock")))
                .BudgetRule(DefaultBudgetRule())
                .Build();

            var provider = new FakeAnchorProvider();
            var result = SkillBudgetAnalyzer.Analyze(new Id("skill.n3_9_open_lock"), view, options: null, provider);

            Assert.False(result.Participates);
            Assert.Equal(SkillBudgetVerdict.NotApplicable, result.Verdict);
        }

        // -----------------------------------------------------------------
        // 怪物档
        // -----------------------------------------------------------------

        [Fact]
        public void MonsterTier_OnlyReferencedByCreatureTemplate_UsesMonsterBandwidth()
        {
            var view = new Registry()
                .SkillDef(SchoolDamageSkill("skill.n3_9_monster_only", baseValue: 25))
                // 不登记进任何 skill.book——只被 creature.template 经 ai.rotation 引用。
                .Rotation(J.O(
                    ("id", J.S("ai.rotation.n3_9_monster")),
                    ("entries", J.A(J.O(
                        ("priority", J.N(1)),
                        ("condition", J.S("true")),
                        ("skill_id", J.S("skill.n3_9_monster_only")))))))
                .Creature(J.O(
                    ("id", J.S("creature.n3_9_boss")),
                    ("level", J.N(30)),
                    ("ai_rotation_ref", J.S("ai.rotation.n3_9_monster"))))
                .BudgetRule(DefaultBudgetRule())
                .Build();

            var provider = new FakeAnchorProvider { AnchorDps = 10.0 };
            // 比值 = 25/10 = 2.5；玩家档带宽上界仅 1.2（会报警告），但怪物档缺省带宽 5.0（上界 6.0）
            // 覆盖住 2.5——验证档位判定确实切换了实际生效的带宽。
            var result = SkillBudgetAnalyzer.Analyze(new Id("skill.n3_9_monster_only"), view, options: null, provider);

            Assert.Equal(SkillBudgetTier.Monster, result.Tier);
            Assert.Equal(30, result.Level);
            Assert.Equal(2.5, result.Ratio, 6);
            Assert.Equal(SkillBudgetVerdict.Pass, result.Verdict);
        }

        [Fact]
        public void PlayerTier_TakesMinimumLevelAcrossMultipleBookEntries()
        {
            var view = new Registry()
                .SkillDef(SchoolDamageSkill("skill.n3_9_multi_book", baseValue: 10))
                .Book(Book("skill.book.n3_9_a", (5, "skill.n3_9_multi_book")))
                .Book(Book("skill.book.n3_9_b", (2, "skill.n3_9_multi_book")))
                .BudgetRule(DefaultBudgetRule())
                .Build();

            var provider = new FakeAnchorProvider { AnchorDps = 10.0 };
            var result = SkillBudgetAnalyzer.Analyze(new Id("skill.n3_9_multi_book"), view, options: null, provider);

            Assert.Equal(SkillBudgetTier.Player, result.Tier);
            Assert.Equal(2, result.Level);
        }

        [Fact]
        public void PlayerTier_TakesPrecedenceOverMonsterTier_WhenReferencedByBoth()
        {
            var view = new Registry()
                .SkillDef(SchoolDamageSkill("skill.n3_9_both", baseValue: 10))
                .Book(Book("skill.book.n3_9", (7, "skill.n3_9_both")))
                .Rotation(J.O(
                    ("id", J.S("ai.rotation.n3_9_both")),
                    ("entries", J.A(J.O(
                        ("priority", J.N(1)),
                        ("condition", J.S("true")),
                        ("skill_id", J.S("skill.n3_9_both")))))))
                .Creature(J.O(
                    ("id", J.S("creature.n3_9_both")),
                    ("level", J.N(40)),
                    ("ai_rotation_ref", J.S("ai.rotation.n3_9_both"))))
                .BudgetRule(DefaultBudgetRule())
                .Build();

            var provider = new FakeAnchorProvider { AnchorDps = 10.0 };
            var result = SkillBudgetAnalyzer.Analyze(new Id("skill.n3_9_both"), view, options: null, provider);

            // ADR-0031 后果段"技能同时被两边引用时按玩家档"：即便怪物模板等级（40）更高，仍取玩家档
            // 习得等级（7），不是怪物档等级。
            Assert.Equal(SkillBudgetTier.Player, result.Tier);
            Assert.Equal(7, result.Level);
        }

        // -----------------------------------------------------------------
        // 深度复审 C（测试覆盖缺口 #4）补测：ComputeGrantValue(isAura: true) 对含 absorb/control
        // 效果的光环求值——既有 9 处调用点均只覆盖 periodic_damage/mod_stat 路径（经 Analyze 走
        // isAura: false 的技能分支），absorb/control 两条分支此前缺直接用例。
        // -----------------------------------------------------------------

        [Fact]
        public void ComputeGrantValue_IsAuraTrue_AbsorbEffect_ReturnsAbsorbAmount()
        {
            var aura = J.O(
                ("id", J.S("skill.aura_def.n3_9_grant_absorb")),
                ("duration", J.N(10)),
                ("effects", J.A(
                    J.O(("kind", J.S("absorb")),
                        ("params", J.O(("amount", J.N(50)), ("school", J.S("school.physical"))))))));

            var view = new Registry()
                .AuraDef(aura)
                .BudgetRule(DefaultBudgetRule())
                .Build();

            var provider = new FakeAnchorProvider { AnchorDps = 10.0 };
            var value = SkillBudgetAnalyzer.ComputeGrantValue(
                new Id("skill.aura_def.n3_9_grant_absorb"), isAura: true, view, level: 1, provider);

            // absorb 分支直接取 params.amount，不经过基础值/系数/缩放公式（见
            // ComputeAuraSettlementValue 的 AuraEffectKind.Absorb 分支）。
            Assert.Equal(50.0, value, 6);
        }

        [Fact]
        public void ComputeGrantValue_IsAuraTrue_ControlEffect_ReturnsDurationTimesCategoryWeight()
        {
            var aura = J.O(
                ("id", J.S("skill.aura_def.n3_9_grant_control")),
                ("duration", J.N(10)),
                ("effects", J.A(
                    J.O(("kind", J.S("control")),
                        ("params", J.O(
                            ("flags", J.A(J.S("no_move"))),
                            ("category", J.S("root"))))))));
            var budgetRule = J.O(
                ("id", J.S("skill.budget_rule.n3_9_grant_control")),
                ("beat_seconds", J.N(1.0)),
                ("control_category_weights", J.O(("root", J.N(3.0)))));

            var view = new Registry()
                .AuraDef(aura)
                .BudgetRule(budgetRule)
                .Build();

            var options = new SkillOptions { BudgetRuleId = new Id("skill.budget_rule.n3_9_grant_control") };
            var provider = new FakeAnchorProvider { AnchorDps = 10.0 };
            var value = SkillBudgetAnalyzer.ComputeGrantValue(
                new Id("skill.aura_def.n3_9_grant_control"), isAura: true, view, level: 1, provider, options);

            // 06 第 3.10 节"控制价值 = 时长 × 目标数 × 控制类别权重"——授予的光环没有 target_shape_ref，
            // ComputeGrantValue 按单目标处理（maxTargets: 1，见该方法判断记录），
            // = 10（duration） × 1（targetCount） × 3.0（root 类别权重）= 30。
            Assert.Equal(30.0, value, 6);
        }

        [Fact]
        public void ComputeGrantValue_IsAuraTrue_UnknownAuraDef_ReturnsZero()
        {
            var view = new Registry().BudgetRule(DefaultBudgetRule()).Build();
            var provider = new FakeAnchorProvider { AnchorDps = 10.0 };

            var value = SkillBudgetAnalyzer.ComputeGrantValue(
                new Id("skill.aura_def.n3_9_grant_missing"), isAura: true, view, level: 1, provider);

            Assert.Equal(0.0, value, 6);
        }
    }
}
