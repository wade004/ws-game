using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// ADR-0023"光环叠加类别为静态校验分组"验收测试（消费方反馈"光环叠加类别契约一致性核对"，见
    /// `architecture/落地计划/消费方反馈-2026-09-10-光环叠加类别.md`）：`stack_category` 是加载期静态
    /// 内容校验分组，不是运行时叠加槽位维度——运行时按 `(target, aura_def, sourceKey)` 分槽，同一
    /// `stack_category` 下不同 `aura_def` 各自独立叠加、互不影响。四条用例对应消费方反馈"最小复现与
    /// 验收"一节列出的四条验收标准，覆盖点见各方法注释。均使用真实 `SkillHost`/`AuraHost`（经
    /// `SkillWorldBuilder.Build()` 装配），不改动任何运行时算法，只验证既有行为符合 ADR-0023 决策。
    /// </summary>
    public sealed class ADR0023_StackCategoryStaticGroupingTests
    {
        private const string Category = "skill.stack_category.adr0023_same";

        private static Core.Foundation.Common.Json.JsonObject Aura(string id, string category, int maxStacks) =>
            J.O(
                ("id", J.S(id)),
                ("stack_category", J.S(category)),
                ("max_stacks", J.N(maxStacks)),
                ("effects", J.A()));

        /// <summary>验收标准 a："静态校验允许相同类别且叠加形态一致的两个定义"（保留既有"混用形态被拒"
        /// 用例，见 <c>SkillValidationRuleTests.StackCategoryConflict_PassesWhenConsistent_FailsWhenMixed</c>；
        /// 本方法用 ADR-0023 反馈原文的两条 `max_stacks=1` 定义自包含地复现同一条结论，并在同一测试里
        /// 保留混用形态仍被拒绝的对照，确认本次措辞改动未连带放宽或收紧任何判定逻辑）。</summary>
        [Fact]
        public void ADR0023_StaticValidation_AllowsSameCategory_ConsistentStackForms_RejectsMixedForms()
        {
            var categoryA = Aura("skill.aura_def.adr0023_category_a", Category, maxStacks: 1);
            var categoryB = Aura("skill.aura_def.adr0023_category_b", Category, maxStacks: 1);
            var ok = new SkillWorldBuilder().AuraDef(categoryA).AuraDef(categoryB)
                .ValidationRule(new StackCategoryConflictRule());
            Assert.False(ok.Validate().IsBlocking);

            var stackable = Aura("skill.aura_def.adr0023_mixed_stackable", "skill.stack_category.adr0023_mixed", maxStacks: 3);
            var nonStackable = Aura("skill.aura_def.adr0023_mixed_nonstackable", "skill.stack_category.adr0023_mixed", maxStacks: 1);
            var bad = new SkillWorldBuilder().AuraDef(stackable).AuraDef(nonStackable)
                .ValidationRule(new StackCategoryConflictRule());
            var report = bad.Validate();
            Assert.True(report.IsBlocking);
            Assert.Contains(report.Issues, i => i.Check == "stack_category_conflict");
        }

        /// <summary>验收标准 b："运行时两个定义均保持激活，具有不同的 AuraInstanceRef，并各自产生应用
        /// 事件"。默认 `RefreshOnly`、`AllowMultiSourceTiming=false`（`SkillOptions` 默认值，未显式
        /// 设置），依次对同一目标施加 `category_a`/`category_b`（同 `stack_category`，各 `max_stacks=1`）。</summary>
        [Fact]
        public void ADR0023_Runtime_SameCategoryDifferentDefs_BothActive_WithDistinctInstanceRefsAndEvents()
        {
            var categoryA = Aura("skill.aura_def.adr0023_category_a", Category, maxStacks: 1);
            var categoryB = Aura("skill.aura_def.adr0023_category_b", Category, maxStacks: 1);
            var world = new SkillWorldBuilder().AuraDef(categoryA).AuraDef(categoryB).Build();
            world.AddUnit(new Id("unit.target"));

            var defA = new Id("skill.aura_def.adr0023_category_a");
            var defB = new Id("skill.aura_def.adr0023_category_b");
            var source = new Id("unit.source");

            var refA = world.Host.EffectSink.ApplyAura(new Id("unit.target"), defA, source);
            var refB = world.Host.EffectSink.ApplyAura(new Id("unit.target"), defB, source);
            world.Flush();

            Assert.True(world.Host.AuraQuery.HasAura(new Id("unit.target"), defA));
            Assert.True(world.Host.AuraQuery.HasAura(new Id("unit.target"), defB));
            Assert.Equal(1, world.Host.AuraQuery.GetStacks(new Id("unit.target"), defA));
            Assert.Equal(1, world.Host.AuraQuery.GetStacks(new Id("unit.target"), defB));
            Assert.NotEqual(refA, refB);

            var applied = world.Of<AuraAppliedEvent>().ToList();
            Assert.Equal(2, applied.Count);
            Assert.Contains(applied, e => e.AuraDefId.Equals(defA) && e.Stacks == 1);
            Assert.Contains(applied, e => e.AuraDefId.Equals(defB) && e.Stacks == 1);
        }

        /// <summary>验收标准 c："重复应用 category_a 时，溢出策略只影响 category_a 的定义/来源槽位"。
        /// `category_a` 第三次施加触发默认 `RefreshOnly` 溢出策略（`max_stacks=1`，第二次施加即超限），
        /// `category_b` 的实例句柄、层数与已发生的应用事件数量均不受影响。</summary>
        [Fact]
        public void ADR0023_Runtime_StackOverflowPolicy_OnlyAffectsMatchingDefSlot()
        {
            var categoryA = Aura("skill.aura_def.adr0023_category_a", Category, maxStacks: 1);
            var categoryB = Aura("skill.aura_def.adr0023_category_b", Category, maxStacks: 1);
            var world = new SkillWorldBuilder().AuraDef(categoryA).AuraDef(categoryB).Build();
            world.AddUnit(new Id("unit.target"));

            var defA = new Id("skill.aura_def.adr0023_category_a");
            var defB = new Id("skill.aura_def.adr0023_category_b");
            var source = new Id("unit.source");
            var target = new Id("unit.target");

            var refA = world.Host.EffectSink.ApplyAura(target, defA, source);
            var refB = world.Host.EffectSink.ApplyAura(target, defB, source);

            // 第二次施加 category_a：max_stacks=1，必然进入溢出分支（默认 RefreshOnly：保留同一
            // 实例句柄、层数不变）。
            var refA2 = world.Host.EffectSink.ApplyAura(target, defA, source);
            world.Flush();

            Assert.Equal(refA, refA2); // RefreshOnly 保留原句柄，不是新实例
            Assert.Equal(1, world.Host.AuraQuery.GetStacks(target, defA));

            // category_b 完全不受 category_a 溢出策略影响：句柄、层数、应用事件数均与溢出前一致。
            Assert.Equal(refB, world.Host.AuraQuery.TryGetInstanceRef(target, defB));
            Assert.Equal(1, world.Host.AuraQuery.GetStacks(target, defB));
            Assert.True(world.Host.AuraQuery.HasAura(target, defB));

            var appliedForB = world.Of<AuraAppliedEvent>().Count(e => e.AuraDefId.Equals(defB));
            Assert.Equal(1, appliedForB); // category_b 只在首次施加时产生过一次应用事件
            Assert.Empty(world.Of<AuraRemovedEvent>()); // RefreshOnly 不移除任何实例
        }

        /// <summary>验收标准 d 前半："默认共享计时模式下 sourceKey 为 null"——同一 `aura_def` 的多个
        /// 来源合并进同一个运行时槽位（同 <c>AuraStackingTests.MultiSource_MergesIntoOneInstance_
        /// WhenAllowMultiSourceTimingDisabled</c>，本方法在 ADR-0023 语境下自包含复现，并额外核对
        /// <see cref="IAuraQuery.GetActiveAuraDefs"/> 只列出一份槽位）。</summary>
        [Fact]
        public void ADR0023_DefaultSharedTiming_SourceKeyNull_MergesAcrossSourcesIntoOneSlot()
        {
            var categoryA = Aura("skill.aura_def.adr0023_category_a", Category, maxStacks: 5);
            var builder = new SkillWorldBuilder().AuraDef(categoryA);
            builder.Options.AllowMultiSourceTiming = false; // 显式声明默认语义，与 SkillOptions 缺省值一致
            var world = builder.Build();
            world.AddUnit(new Id("unit.target"));

            var defA = new Id("skill.aura_def.adr0023_category_a");
            var target = new Id("unit.target");

            var refFromX = world.Host.EffectSink.ApplyAura(target, defA, new Id("unit.source_x"));
            var refFromY = world.Host.EffectSink.ApplyAura(target, defA, new Id("unit.source_y"));

            Assert.Equal(refFromX, refFromY); // 同一个槽位：sourceKey 恒为 null，不按来源区分
            Assert.Equal(2, world.Host.AuraQuery.GetStacks(target, defA));
            Assert.Single(world.Host.AuraQuery.GetActiveAuraDefs(target).Where(d => d.Equals(defA)));
        }

        /// <summary>验收标准 d 后半："开启独立来源计时（AllowMultiSourceTiming）时按来源区分"——同一
        /// `aura_def` 的不同来源各自维护独立槽位，互不合并。</summary>
        [Fact]
        public void ADR0023_MultiSourceTimingEnabled_SeparatesSlotsBySource()
        {
            var categoryA = Aura("skill.aura_def.adr0023_category_a", Category, maxStacks: 1);
            var builder = new SkillWorldBuilder().AuraDef(categoryA);
            builder.Options.AllowMultiSourceTiming = true;
            var world = builder.Build();
            world.AddUnit(new Id("unit.target"));

            var defA = new Id("skill.aura_def.adr0023_category_a");
            var target = new Id("unit.target");

            var refFromX = world.Host.EffectSink.ApplyAura(target, defA, new Id("unit.source_x"));
            var refFromY = world.Host.EffectSink.ApplyAura(target, defA, new Id("unit.source_y"));
            world.Flush();

            Assert.NotEqual(refFromX, refFromY); // 两个独立槽位，各自的句柄不同
            Assert.Equal(2, world.Host.AuraQuery.GetActiveAuraDefs(target).Count(d => d.Equals(defA)));
            Assert.Equal(2, world.Of<AuraAppliedEvent>().Count(e => e.AuraDefId.Equals(defA)));
        }
    }
}
