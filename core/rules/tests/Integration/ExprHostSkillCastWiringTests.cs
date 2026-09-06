using System;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Integration
{
    /// <summary>
    /// P2-01 收口回归（<c>architecture/落地计划/audit-20260907/foundation-rules.md</c>）：此前
    /// <see cref="Core.Rules.Assembly.RulesAssembly"/> 为 Targeting/Skill 构造并绑定的
    /// <c>RulesExprHostFactory</c> 传 <c>skillHost: null</c>（真正带 <c>skillHost: Skill</c> 的
    /// 第二份工厂只给 <c>AiHost</c> 单独用），导致 <c>self.is_casting</c>/<c>combat.is_casting</c>
    /// 在 Targeting 的 target chain filters 与 Skill 自己的 <c>ProcHost</c> 条件求值里恒为 false，
    /// 即便施法者确实在读条中。收口后 <see cref="Core.Rules.Assembly.RulesAssembly.ExprHostFactory"/>
    /// 是 Targeting/Skill/Ai 三方共享的唯一一份工厂，本用例直接验证：真实读条状态下，经这份共享工厂
    /// 求值 <c>self.is_casting</c> 能读到 true——这正是此前恒为 false 的那条路径。
    /// </summary>
    public sealed class ExprHostSkillCastWiringTests
    {
        [Fact]
        public void SharedExprHostFactory_DuringRealCast_ReflectsIsCastingTrue()
        {
            var fx = FightWorldBuilder.Build();

            // skill.sample_burn（cast_time = 1.0，见 FightWorldBuilder.SkillDefJson）：施放后在
            // cast_time 窗口内 Skill.IsCasting(caster) 应为 true。cast_time > 0 的技能不会在
            // CastSkill 调用内同步结算完毕，因此调用返回后立即查询即可观察到"读条中"状态。
            var castResult = fx.Rules.Skill.CastSkill(
                FightWorldBuilder.PlayerId, FightWorldBuilder.SkillBurn, Array.Empty<Id>());
            Assert.True(castResult.Success, castResult.Reason.ToString());

            // 先用 ISkillHost 本身核实"确实在读条"，排除"技能瞬发/立即结算"这类夹具配置问题。
            Assert.True(fx.Rules.Skill.IsCasting(FightWorldBuilder.PlayerId));

            // 核心断言：Targeting/Skill/Ai 共享的同一份 RulesAssembly.ExprHostFactory（P2-01 收口
            // 之前恒 skillHost: null 的那份）现在应该读到真实的读条状态，不再恒为 false。
            var exprHost = fx.Rules.ExprHostFactory.CreateFor(FightWorldBuilder.PlayerId, null, null);
            Assert.True(exprHost.Query(ExprGroups.Self, "is_casting", Array.Empty<ExprValue>()).AsBool);
            Assert.True(exprHost.Query(ExprGroups.Combat, "is_casting", Array.Empty<ExprValue>()).AsBool);
        }

        [Fact]
        public void SharedExprHostFactory_WhenNotCasting_IsCastingFalse()
        {
            var fx = FightWorldBuilder.Build();

            var exprHost = fx.Rules.ExprHostFactory.CreateFor(FightWorldBuilder.PlayerId, null, null);
            Assert.False(exprHost.Query(ExprGroups.Self, "is_casting", Array.Empty<ExprValue>()).AsBool);
        }
    }
}
