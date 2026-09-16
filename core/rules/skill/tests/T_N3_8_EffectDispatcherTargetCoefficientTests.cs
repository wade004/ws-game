using System.Linq;
using Core.Foundation.Common;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// T-N3-8（ADR-0031 决策 6、拍板 7；06 第 3.7 节 2026-09-14 修订段）：<c>EffectDispatcher</c>
    /// 按 <c>ITargetHost.ResolveWithCoefficients</c> 解析出的分配系数缩放群体效果值（<c>value ×
    /// coefficient</c>）。<see cref="FakeTargetHost.SetChainWithCoefficients"/> 手工登记"这条链应该
    /// 解析出哪些目标、各自什么系数"，只验证"目标层（<c>CastPipeline</c> 步骤 6）→
    /// <c>EffectDispatcher.ApplyDamageOrHeal</c>"这一段系数透传/缩放契约——真实
    /// <c>TargetHost</c> 按 <c>overflow_policy</c>/<c>max_targets</c> 算出系数的行为由
    /// <c>core/rules/targeting/tests/T_N3_8_TargetOverflowPolicyTests.cs</c> 覆盖，两组测试合起来
    /// 覆盖 06 第 3.7 节修订段"目标解析出系数"到"效果值按系数缩放"的完整链路。
    /// </summary>
    public sealed class T_N3_8_EffectDispatcherTargetCoefficientTests
    {
        private static Core.Foundation.Common.Json.JsonObject GroupBolt(string id, double baseValue, string chainId)
        {
            return J.O(
                ("id", J.S(id)),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(true)),
                ("target_shape_ref", J.S(chainId)),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(("base_value", J.N(baseValue)), ("coefficient", J.N(0))))))));
        }

        [Fact]
        public void GroupCast_SplitPolicyCoefficients_ScalesEachTargetValueByItsCoefficient()
        {
            var world = new SkillWorldBuilder()
                .SkillDef(GroupBolt("skill.group_bolt", baseValue: 10, chainId: "target.chain.group"))
                .Build();
            world.AddUnit(new Id("unit.caster"));
            world.AddUnit(new Id("unit.near"));
            world.AddUnit(new Id("unit.far"));

            // 手工登记（模拟真实 TargetHost 对 overflow_policy=split、命中数超出 max_targets=2 时的
            // 折算结果——系数任意取值，只验证透传/缩放本身，不重复核对 split 的系数公式，那部分由
            // targeting/tests 覆盖）：unit.near 系数 0.5，unit.far 系数 1.5。
            world.Targets.SetChainWithCoefficients(
                new Id("target.chain.group"), TargetOverflowPolicy.Split, cap: 2,
                (new Id("unit.near"), 0.5), (new Id("unit.far"), 1.5));

            var result = world.Host.CastSkill(new Id("unit.caster"), new Id("skill.group_bolt"), System.Array.Empty<Id>());
            world.Flush();

            Assert.True(result.Success);
            Assert.Equal(2, world.Combat.ResolveCalls.Count);

            var nearCall = world.Combat.ResolveCalls.Single(c => c.TargetId == new Id("unit.near"));
            var farCall = world.Combat.ResolveCalls.Single(c => c.TargetId == new Id("unit.far"));

            // outbound EffectContext.BaseValue 是 EffectDispatcher.ApplyDamageOrHeal 缩放（此例
            // coefficient=0，无 scaling 贡献，SpellMod 未注册）后的最终 value——10 × 0.5 = 5、
            // 10 × 1.5 = 15。
            Assert.Equal(5.0, nearCall.BaseValue, precision: 9);
            Assert.Equal(15.0, farCall.BaseValue, precision: 9);

            // TargetCoefficient 原样转发（见 EffectDispatcher.ApplyDamageOrHeal 判断记录），供下游/
            // 日志核对"这次结算的分配系数是多少"。
            Assert.Equal(0.5, nearCall.TargetCoefficient, precision: 9);
            Assert.Equal(1.5, farCall.TargetCoefficient, precision: 9);
        }

        [Fact]
        public void GroupCast_TruncatePolicyCoefficientOne_ValueUnchanged()
        {
            var world = new SkillWorldBuilder()
                .SkillDef(GroupBolt("skill.group_bolt_truncate", baseValue: 10, chainId: "target.chain.group_truncate"))
                .Build();
            world.AddUnit(new Id("unit.caster"));
            world.AddUnit(new Id("unit.only"));

            // 未超出 max_targets（cap=5 > 命中数 1）——系数恒为 1（见 TargetResolution 判断记录
            // "未超限时系数恒为 1"），value 逐位不变。
            world.Targets.SetChainWithCoefficients(
                new Id("target.chain.group_truncate"), TargetOverflowPolicy.Truncate, cap: 5,
                (new Id("unit.only"), 1.0));

            var result = world.Host.CastSkill(
                new Id("unit.caster"), new Id("skill.group_bolt_truncate"), System.Array.Empty<Id>());
            world.Flush();

            Assert.True(result.Success);
            var call = Assert.Single(world.Combat.ResolveCalls);
            Assert.Equal(new Id("unit.only"), call.TargetId);
            Assert.Equal(10.0, call.BaseValue, precision: 9);
            Assert.Equal(1.0, call.TargetCoefficient, precision: 9);
        }

        // -----------------------------------------------------------------
        // 深度复审 C（测试覆盖缺口 #3）补测：引导（channel）技能多次 tick 复用同一份系数字典
        // （CastPipeline.CastState.TargetCoefficients 只在读条/引导开始时经
        // ITargetHost.ResolveWithCoefficients 解析一次，全程复用，见该字段判断记录）——现有
        // T_N3_8_TargetOverflowPolicyTests.cs/本文件其余用例均只覆盖单次结算（瞬发 cast_time=0），
        // 未见验证"引导技能"这条路径。
        // -----------------------------------------------------------------

        private static Core.Foundation.Common.Json.JsonObject ChannelGroupBolt(
            string id, double baseValue, string chainId, double channelTime, double tickInterval)
        {
            return J.O(
                ("id", J.S(id)),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("channel_time", J.N(channelTime)),
                ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S(chainId)),
                ("effects", J.A(
                    J.O(("kind", J.S("school_damage")),
                        ("params", J.O(
                            ("tick_interval", J.N(tickInterval)),
                            ("base_value", J.N(baseValue)),
                            ("coefficient", J.N(0))))))));
        }

        [Fact]
        public void ChannelSkill_SplitPolicyCoefficients_ResolvedOnceAndReusedAcrossEveryTick()
        {
            var world = new SkillWorldBuilder()
                .SkillDef(ChannelGroupBolt(
                    "skill.channel_group_bolt", baseValue: 10, chainId: "target.chain.channel_group",
                    channelTime: 3, tickInterval: 1))
                .Build();
            var caster = new Id("unit.caster");
            var near = new Id("unit.near");
            var far = new Id("unit.far");
            world.AddUnit(caster);
            world.AddUnit(near);
            world.AddUnit(far);

            // 同 GroupCast_SplitPolicyCoefficients_ScalesEachTargetValueByItsCoefficient 的夹具：
            // near 系数 0.5、far 系数 1.5（overflow_policy=split，命中数超出 max_targets=2 的折算
            // 结果，本测试只关心"是否每跳都重新解析"，不重复核对 split 公式本身）。
            world.Targets.SetChainWithCoefficients(
                new Id("target.chain.channel_group"), TargetOverflowPolicy.Split, cap: 2,
                (near, 0.5), (far, 1.5));

            var result = world.Host.CastSkill(caster, new Id("skill.channel_group_bolt"), System.Array.Empty<Id>());
            Assert.True(result.Success);
            Assert.True(world.Host.IsCasting(caster));

            // channel_time=3、tick_interval=1：Update(3) 应恰好产生 3 跳，每跳各结算 near/far 两个
            // 目标，共 6 次 ResolveEffect 调用。
            world.Host.Update(3.0);
            world.Flush();

            Assert.False(world.Host.IsCasting(caster));
            Assert.Equal(6, world.Combat.ResolveCalls.Count);

            // 核心断言（C 测试覆盖缺口 #3）：目标链只应该在读条/引导开始时被解析一次，3 跳全部复用
            // 同一份系数字典，不逐跳重新调用 ITargetHost.ResolveWithCoefficients。
            Assert.Equal(1, world.Targets.ResolveWithCoefficientsCallCount);

            // 3 跳里 near/far 各自的系数应该逐跳保持一致（同一份缓存字典的取值，不会中途漂移）。
            var nearCalls = world.Combat.ResolveCalls.Where(c => c.TargetId == near).ToList();
            var farCalls = world.Combat.ResolveCalls.Where(c => c.TargetId == far).ToList();
            Assert.Equal(3, nearCalls.Count);
            Assert.Equal(3, farCalls.Count);
            Assert.All(nearCalls, c =>
            {
                Assert.Equal(0.5, c.TargetCoefficient, precision: 9);
                Assert.Equal(5.0, c.BaseValue, precision: 9); // 10 × 0.5
            });
            Assert.All(farCalls, c =>
            {
                Assert.Equal(1.5, c.TargetCoefficient, precision: 9);
                Assert.Equal(15.0, c.BaseValue, precision: 9); // 10 × 1.5
            });
        }
    }
}
