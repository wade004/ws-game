using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// ADR-0028（消费方反馈"投射物敌友关系策略与 ActiveCount 契约"）收边补齐：验证
    /// <c>relation_policy</c>/<c>pierce_order</c> 两个新增 <c>projectile</c> 效果参数经真实施法路径
    /// （<see cref="Core.Rules.Skill.SkillHost.CastSkill"/> → <c>EffectDispatcher.ApplyProjectile</c>）
    /// 原样透传进 <see cref="EffectContext.Params"/>，不被本层（L2 <c>core/rules/skill</c>）过滤、
    /// 改写或丢弃。
    /// <para>
    /// 判断记录（分层边界，同 <see cref="Tests.Rules.Skill.EffectPrimitiveDispatchTests"/>
    /// "projectile"一节"真实生成路径由 core/carriers/projectile 模块测试覆盖，不在本模块依赖范围
    /// 内"）：本文件只验证"技能数据 → <see cref="EffectContext"/>"这一段的参数透传契约，用
    /// <see cref="RecordingProjectileSpawner"/>（记录调用参数的假实现）代替真实
    /// <c>Core.Carriers.Projectile.ProjectileHost</c>——<c>core/rules/skill</c>（L2）不得依赖
    /// <c>core/carriers/projectile</c>（L3），见 01 依赖矩阵。<c>relation_policy</c> 取值本身的裁决
    /// 逻辑（施法者排除/目标锁定/关系筛选/标签筛选/穿透计数/命中效果回灌）、未注入
    /// <c>IFactionMatrix</c> 时的退化行为，均由 <c>core/carriers/projectile/tests/ProjectileHostTests.cs</c>
    /// "9. ADR-0028：敌友关系策略"一节覆盖，本文件不重复。
    /// </para>
    /// </summary>
    public sealed class C10c_ProjectileRelationPolicyParamsTests
    {
        private static readonly Id Caster = new Id("unit.c10c_caster");
        private static readonly Id Target = new Id("unit.c10c_target");

        /// <summary>记录每次 <see cref="Spawn"/> 调用收到的 <see cref="EffectContext"/>，不生成任何
        /// 真实投射物实体（本类无权访问 <c>core/carriers/projectile</c>，见类型顶部判断记录）。</summary>
        private sealed class RecordingProjectileSpawner : IProjectileSpawner
        {
            public readonly List<EffectContext> Calls = new List<EffectContext>();

            public void Spawn(EffectContext context, IEffectSink effectSink) => Calls.Add(context);
        }

        private static JsonObject ProjectileSkill(string id, params (string Key, JsonValue Value)[] projectileParams) => J.O(
            ("id", J.S(id)),
            ("school", J.S("skill.school_sample")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(0)),
            ("respects_gcd", J.B(false)),
            ("target_shape_ref", J.S("target.chain.sample")),
            ("effects", J.A(J.O(("kind", J.S("projectile")), ("params", J.O(projectileParams))))));

        [Fact]
        public void RelationPolicyAndPierceOrder_PassThroughUnchangedToEffectContextParams()
        {
            var spawner = new RecordingProjectileSpawner();
            var skill = ProjectileSkill("skill.c10c_sample_bolt",
                ("relation_policy", J.S("hostile_only")),
                ("pierce_order", J.S("hostile_first")),
                ("hit_behavior", J.S("pierce")));

            var world = new SkillWorldBuilder { ProjectileSpawner = spawner }.SkillDef(skill).Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(new Id("target.chain.sample"), Target);

            var result = world.Host.CastSkill(Caster, new Id("skill.c10c_sample_bolt"), System.Array.Empty<Id>());

            Assert.True(result.Success);
            Assert.Single(spawner.Calls);
            var @params = spawner.Calls[0].Params;
            Assert.True(@params.TryGetValue("relation_policy", out var relationPolicyValue));
            Assert.Equal("hostile_only", Assert.IsType<JsonString>(relationPolicyValue).Value);
            Assert.True(@params.TryGetValue("pierce_order", out var pierceOrderValue));
            Assert.Equal("hostile_first", Assert.IsType<JsonString>(pierceOrderValue).Value);
        }

        [Fact]
        public void RelationPolicyAndPierceOrder_OmittedInSkillDef_AbsentFromEffectContextParams()
        {
            // 未在 skill.def 里声明 relation_policy/pierce_order 时，EffectContext.Params 里不应
            // 凭空冒出这两个键（缺省值的解释权完全在 ProjectileHost 一侧，L2 不预填任何缺省值，见
            // ProjectileHost.Spawn 用 GetString(..., fallback) 兜底的既有惯例）。
            var spawner = new RecordingProjectileSpawner();
            var skill = ProjectileSkill("skill.c10c_sample_bolt_bare", ("speed", J.N(10)));

            var world = new SkillWorldBuilder { ProjectileSpawner = spawner }.SkillDef(skill).Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(new Id("target.chain.sample"), Target);

            var result = world.Host.CastSkill(Caster, new Id("skill.c10c_sample_bolt_bare"), System.Array.Empty<Id>());

            Assert.True(result.Success);
            Assert.Single(spawner.Calls);
            var @params = spawner.Calls[0].Params;
            Assert.False(@params.TryGetValue("relation_policy", out _));
            Assert.False(@params.TryGetValue("pierce_order", out _));
        }
    }
}
