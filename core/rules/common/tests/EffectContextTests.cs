using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Common
{
    /// <summary>
    /// T-N1-6（[ADR-0030](../../../../architecture/adr/0030-属性系统派生换算与来源类别.md)
    /// 决策 5；06 第 4.1 节 2026-09-14 修订段）：<see cref="EffectContext.SourceKind"/> 新字段与承载
    /// 它的十七参数构造函数重载。验收标准"新增用例：EffectContext 旧 15/16 参构造得到的 SourceKind
    /// 为默认值"对应的用例见 <see cref="FifteenParamConstructor_SourceKind_DefaultsToUnknown"/>/
    /// <see cref="SixteenParamConstructor_SourceKind_DefaultsToUnknown"/>。
    /// </summary>
    public class EffectContextTests
    {
        private static readonly Id Source = new Id("unit.hero");
        private static readonly Id Target = new Id("unit.dummy");
        private static readonly Id Skill = new Id("skill.fireball");
        private static readonly Id School = new Id("school.fire");

        [Fact]
        public void FifteenParamConstructor_SourceKind_DefaultsToUnknown()
        {
            // 既有十五参数构造函数（不带 sourceKind，ABI 规则禁止改动其签名）——同旧迁移前既有调用点
            // 逐字节不变的调用方式，只传到 tags，triggerChainDepth/attackInstanceId 走可选参数默认值。
            var context = new EffectContext(
                Source, Target, Skill, EffectKind.SchoolDamage, School,
                baseValue: 10, coefficient: 0.5);

            Assert.Equal(SourceKind.Unknown, context.SourceKind);
        }

        [Fact]
        public void SixteenParamConstructor_SourceKind_DefaultsToUnknown()
        {
            // 既有十六参数构造函数（ADR-0027 补充 groundPoint，同样不带 sourceKind、同样禁止改动）。
            var context = new EffectContext(
                Source, Target, Skill, EffectKind.SchoolDamage, School,
                baseValue: 10, coefficient: 0.5, @params: null,
                auraInstanceId: null, isPeriodic: false, canCrit: true, canMiss: true,
                tags: null, triggerChainDepth: 0, attackInstanceId: null,
                groundPoint: new Vec2(3, 4));

            Assert.Equal(SourceKind.Unknown, context.SourceKind);
            // GroundPoint 本身不受本任务改动，顺带核对既有十六参数构造函数行为不变。
            Assert.Equal(new Vec2(3, 4), context.GroundPoint);
        }

        [Fact]
        public void SeventeenParamConstructor_RoundTripsSourceKind_ForBothNonUnknownValues()
        {
            var playerContext = new EffectContext(
                Source, Target, Skill, EffectKind.SchoolDamage, School,
                baseValue: 10, coefficient: 0.5, @params: null,
                auraInstanceId: null, isPeriodic: false, canCrit: true, canMiss: true,
                tags: null, triggerChainDepth: 0, attackInstanceId: null,
                groundPoint: null, sourceKind: SourceKind.Player);

            var creatureContext = new EffectContext(
                Source, Target, Skill, EffectKind.SchoolDamage, School,
                baseValue: 10, coefficient: 0.5, @params: null,
                auraInstanceId: null, isPeriodic: false, canCrit: true, canMiss: true,
                tags: null, triggerChainDepth: 0, attackInstanceId: null,
                groundPoint: null, sourceKind: SourceKind.Creature);

            Assert.Equal(SourceKind.Player, playerContext.SourceKind);
            Assert.Equal(SourceKind.Creature, creatureContext.SourceKind);
        }
    }
}
