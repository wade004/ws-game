using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Rules.Common;
using Core.Rules.Skill;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>
    /// W1 收边补齐（A3 审计 #13/#17）：<see cref="EffectDispatcher.ApplyEffect"/> 11 个此前无直接
    /// 测试覆盖的 Effect 原语分派/降级路径（<c>weapon_damage_pct</c>/<c>modify_cooldown</c>/
    /// <c>add_charge</c>/<c>interrupt</c>/<c>teleport</c>/<c>learn_skill</c>/<c>projectile</c>/
    /// <c>summon</c>/<c>open_lock</c>/<c>create_item</c>/<c>set_world_flag</c>/<c>script</c>）——
    /// 全部经真实施法路径（<c>CastSkill</c>）驱动，而不是直接调用 <c>EffectDispatcher</c>（那需要
    /// 引用其私有构造依赖），与本目录其余测试同一惯例。
    /// </summary>
    public sealed class EffectPrimitiveDispatchTests
    {
        private static readonly Id Caster = new Id("unit.caster");
        private static readonly Id Target = new Id("unit.target");

        private static JsonObject InstantSkill(string id, string effectKind, params (string Key, JsonValue Value)[] paramsFields) => J.O(
            ("id", J.S(id)),
            ("school", J.S("skill.school_sample")),
            ("kind", J.S("active")),
            ("range", J.N(0)),
            ("cast_time", J.N(0)),
            ("respects_gcd", J.B(false)),
            ("target_shape_ref", J.S("target.chain.sample")),
            ("effects", J.A(J.O(("kind", J.S(effectKind)), ("params", J.O(paramsFields))))));

        private static SkillWorld BuildAndTarget(JsonObject skillDef, Id targetChainTo)
        {
            var world = new SkillWorldBuilder().SkillDef(skillDef).Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(new Id("target.chain.sample"), targetChainTo);
            return world;
        }

        // -----------------------------------------------------------------
        // weapon_damage_pct
        // -----------------------------------------------------------------

        /// <summary>RC-11 收边补齐（见外部审计 architecture/落地计划/audit-b3b91ee-20260907/
        /// code-review.md RC-11）：固定返回 <see cref="BaseDamage"/> 的最小假实现，模拟
        /// <c>core/carriers/item.EquipmentHost.GetWeaponBaseDamage</c> 的效果——本模块（core/rules）
        /// 不依赖 core/carriers，不能直接用真实 EquipmentHost，见 IWeaponDamageQuery 判断记录
        /// "依赖倒置"。</summary>
        private sealed class FakeWeaponDamageQuery : IWeaponDamageQuery
        {
            public double BaseDamage { get; set; }
            public double GetWeaponBaseDamage(Id unitId) => BaseDamage;
        }

        [Fact]
        public void WeaponDamagePct_MultipliesPctByInjectedWeaponBaseDamage()
        {
            // 修复前：params.pct 本身直接当基础值（call.BaseValue == 0.75，与武器基础伤害无关，
            // 换装不会改变该技能伤害，见外部审计 RC-11）。修复后必须是 weaponBase × pct。
            var skill = InstantSkill("skill.sample_weapon_hit", "weapon_damage_pct", ("pct", J.N(0.75)));
            var builder = new SkillWorldBuilder().SkillDef(skill);
            builder.WeaponDamageQuery = new FakeWeaponDamageQuery { BaseDamage = 100 };
            var world = builder.Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(new Id("target.chain.sample"), Target);

            var result = world.Host.CastSkill(Caster, new Id("skill.sample_weapon_hit"), System.Array.Empty<Id>());

            Assert.True(result.Success);
            var call = Assert.Single(world.Combat.ResolveCalls);
            Assert.Equal(EffectKind.WeaponDamagePct, call.Kind);
            Assert.Equal(75.0, call.BaseValue, 6); // 100 × 0.75
        }

        [Fact]
        public void WeaponDamagePct_ScalesWithWeaponBaseDamage_200BaseYields150()
        {
            // 验收标准原句"100/200 基数分别得 75/150"的另一半：换一把基础伤害不同的武器，结果
            // 应线性缩放——证明确实在消费武器基础伤害，不是碰巧算对了一次。
            var skill = InstantSkill("skill.sample_weapon_hit_200", "weapon_damage_pct", ("pct", J.N(0.75)));
            var builder = new SkillWorldBuilder().SkillDef(skill);
            builder.WeaponDamageQuery = new FakeWeaponDamageQuery { BaseDamage = 200 };
            var world = builder.Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(new Id("target.chain.sample"), Target);

            var result = world.Host.CastSkill(Caster, new Id("skill.sample_weapon_hit_200"), System.Array.Empty<Id>());

            Assert.True(result.Success);
            var call = Assert.Single(world.Combat.ResolveCalls);
            Assert.Equal(150.0, call.BaseValue, 6); // 200 × 0.75
        }

        [Fact]
        public void WeaponDamagePct_NoWeaponDamageQueryInjected_TreatsAsUnarmed_ResultsInZero()
        {
            // 未装配 IWeaponDamageQuery（生产装配里对应"施法者当前没有武器"，见该接口方法注释
            // "判断记录"：06/07 未定义徒手基数，本模块按"无武器则无武器伤害贡献"处理）。
            var skill = InstantSkill("skill.sample_weapon_hit_unarmed", "weapon_damage_pct", ("pct", J.N(0.75)));
            var world = BuildAndTarget(skill, Target);

            var result = world.Host.CastSkill(Caster, new Id("skill.sample_weapon_hit_unarmed"), System.Array.Empty<Id>());

            Assert.True(result.Success);
            var call = Assert.Single(world.Combat.ResolveCalls);
            Assert.Equal(0.0, call.BaseValue, 6);
        }

        // -----------------------------------------------------------------
        // modify_cooldown
        // -----------------------------------------------------------------

        [Fact]
        public void ModifyCooldown_BySkillId_ReducesTargetSkillCooldown()
        {
            var onCooldownSkill = J.O(
                ("id", J.S("skill.sample_on_cooldown")),
                ("school", J.S("skill.school_sample")),
                ("kind", J.S("active")),
                ("range", J.N(0)),
                ("cast_time", J.N(0)),
                ("respects_gcd", J.B(false)),
                ("cooldown_duration", J.N(10)),
                ("target_shape_ref", J.S("target.chain.sample")),
                ("effects", J.A()));

            var modifySkill = InstantSkill(
                "skill.sample_modify_cd", "modify_cooldown",
                ("skill_id", J.S("skill.sample_on_cooldown")), ("delta", J.N(-10)));

            var world = new SkillWorldBuilder().SkillDef(onCooldownSkill).SkillDef(modifySkill).Build();
            world.AddUnit(Caster);
            world.Targets.SetChain(new Id("target.chain.sample"), Caster);

            Assert.True(world.Host.CastSkill(Caster, new Id("skill.sample_on_cooldown"), System.Array.Empty<Id>()).Success);
            Assert.Equal(10, world.Host.GetCooldown(Caster, new Id("skill.sample_on_cooldown")));

            Assert.True(world.Host.CastSkill(Caster, new Id("skill.sample_modify_cd"), System.Array.Empty<Id>()).Success);

            Assert.Equal(0, world.Host.GetCooldown(Caster, new Id("skill.sample_on_cooldown")));
        }

        [Fact]
        public void ModifyCooldown_ByCategory_AffectsSiblingSkillCooldown()
        {
            var a = J.O(
                ("id", J.S("skill.sample_a")), ("school", J.S("skill.school_sample")), ("kind", J.S("active")),
                ("range", J.N(0)), ("cast_time", J.N(0)), ("respects_gcd", J.B(false)),
                ("cooldown_category", J.S("skill.category.sample")), ("cooldown_duration", J.N(6)),
                ("target_shape_ref", J.S("target.chain.sample")), ("effects", J.A()));
            var b = J.O(
                ("id", J.S("skill.sample_b")), ("school", J.S("skill.school_sample")), ("kind", J.S("active")),
                ("range", J.N(0)), ("cast_time", J.N(0)), ("respects_gcd", J.B(false)),
                ("cooldown_category", J.S("skill.category.sample")), ("cooldown_duration", J.N(6)),
                ("target_shape_ref", J.S("target.chain.sample")), ("effects", J.A()));
            var modifySkill = InstantSkill(
                "skill.sample_modify_cd_category", "modify_cooldown",
                ("category", J.S("skill.category.sample")), ("delta", J.N(-6)));

            var world = new SkillWorldBuilder().SkillDef(a).SkillDef(b).SkillDef(modifySkill).Build();
            world.AddUnit(Caster);
            world.Targets.SetChain(new Id("target.chain.sample"), Caster);

            Assert.True(world.Host.CastSkill(Caster, new Id("skill.sample_a"), System.Array.Empty<Id>()).Success);
            Assert.False(world.Host.CastSkill(Caster, new Id("skill.sample_b"), System.Array.Empty<Id>()).Success);

            Assert.True(world.Host.CastSkill(Caster, new Id("skill.sample_modify_cd_category"), System.Array.Empty<Id>()).Success);

            Assert.True(world.Host.CastSkill(Caster, new Id("skill.sample_b"), System.Array.Empty<Id>()).Success);
        }

        [Fact]
        public void ModifyCooldown_MissingSkillIdAndCategory_WarnsAndDoesNotThrow()
        {
            var modifySkill = InstantSkill("skill.sample_modify_cd_missing", "modify_cooldown", ("delta", J.N(-1)));
            var world = BuildAndTarget(modifySkill, Caster);

            var result = world.Host.CastSkill(Caster, new Id("skill.sample_modify_cd_missing"), System.Array.Empty<Id>());

            Assert.True(result.Success);
            Assert.Contains(world.Diagnostics.Warnings, w => w.Contains("modify_cooldown"));
        }

        // -----------------------------------------------------------------
        // add_charge
        // -----------------------------------------------------------------

        [Fact]
        public void AddCharge_RestoresChargeBeforeNaturalRecharge()
        {
            var chargeable = J.O(
                ("id", J.S("skill.sample_chargeable")), ("school", J.S("skill.school_sample")), ("kind", J.S("active")),
                ("range", J.N(0)), ("cast_time", J.N(0)), ("respects_gcd", J.B(false)),
                ("charges", J.O(("max", J.N(2)), ("recharge_time", J.N(100)))),
                ("target_shape_ref", J.S("target.chain.sample")), ("effects", J.A()));
            var addChargeSkill = InstantSkill(
                "skill.sample_add_charge", "add_charge",
                ("skill_id", J.S("skill.sample_chargeable")), ("amount", J.N(1)));

            var world = new SkillWorldBuilder().SkillDef(chargeable).SkillDef(addChargeSkill).Build();
            world.AddUnit(Caster);
            world.Targets.SetChain(new Id("target.chain.sample"), Caster);

            Assert.True(world.Host.CastSkill(Caster, new Id("skill.sample_chargeable"), System.Array.Empty<Id>()).Success);
            Assert.True(world.Host.CastSkill(Caster, new Id("skill.sample_chargeable"), System.Array.Empty<Id>()).Success);
            var exhausted = world.Host.CastSkill(Caster, new Id("skill.sample_chargeable"), System.Array.Empty<Id>());
            Assert.False(exhausted.Success);
            Assert.Equal(CastFailureReason.NoCharges, exhausted.Reason);

            Assert.True(world.Host.CastSkill(Caster, new Id("skill.sample_add_charge"), System.Array.Empty<Id>()).Success);

            Assert.True(world.Host.CastSkill(Caster, new Id("skill.sample_chargeable"), System.Array.Empty<Id>()).Success);
        }

        [Fact]
        public void AddCharge_UnknownSkillId_WarnsAndDoesNotThrow()
        {
            var addChargeSkill = InstantSkill(
                "skill.sample_add_charge_bad", "add_charge",
                ("skill_id", J.S("skill.sample_does_not_exist")), ("amount", J.N(1)));
            var world = BuildAndTarget(addChargeSkill, Caster);

            var result = world.Host.CastSkill(Caster, new Id("skill.sample_add_charge_bad"), System.Array.Empty<Id>());

            Assert.True(result.Success);
            Assert.Contains(world.Diagnostics.Warnings, w => w.Contains("add_charge"));
        }

        // -----------------------------------------------------------------
        // interrupt（效果原语分支，区别于 CastPipeline.Interrupt 公开方法本身，
        // 见 core/rules/skill/README.md 未覆盖清单）
        // -----------------------------------------------------------------

        [Fact]
        public void InterruptEffect_StopsTargetsOngoingCast_AndLocksSchool()
        {
            var channel = J.O(
                ("id", J.S("skill.sample_cast_slow")), ("school", J.S("skill.school_sample")), ("kind", J.S("active")),
                ("range", J.N(0)), ("cast_time", J.N(5.0)), ("respects_gcd", J.B(false)),
                ("target_shape_ref", J.S("target.chain.sample")), ("effects", J.A()));
            var interruptSkill = InstantSkill(
                "skill.sample_interrupt", "interrupt",
                ("lock_school", J.S("skill.school_sample")), ("lock_duration", J.N(3)));

            var world = new SkillWorldBuilder().SkillDef(channel).SkillDef(interruptSkill).Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(new Id("target.chain.sample"), Target);

            Assert.True(world.Host.CastSkill(Target, new Id("skill.sample_cast_slow"), System.Array.Empty<Id>()).Success);
            Assert.True(world.Host.IsCasting(Target));

            Assert.True(world.Host.CastSkill(Caster, new Id("skill.sample_interrupt"), System.Array.Empty<Id>()).Success);
            world.Flush();

            Assert.False(world.Host.IsCasting(Target));
            var interrupted = world.Of<SkillCastInterruptedEvent>().Single();
            Assert.Equal(Target, interrupted.CasterId);
            Assert.Equal(new Id("skill.sample_cast_slow"), interrupted.SkillId);

            // 学派锁定生效：目标此刻应无法再次施放同学派技能（SchoolLocked，步骤 2）。
            var relock = world.Host.CastSkill(Target, new Id("skill.sample_cast_slow"), System.Array.Empty<Id>());
            Assert.False(relock.Success);
            Assert.Equal(CastFailureReason.SchoolLocked, relock.Reason);
        }

        // -----------------------------------------------------------------
        // teleport
        // -----------------------------------------------------------------

        [Fact]
        public void Teleport_MovesTargetToParamsPoint()
        {
            var skill = InstantSkill("skill.sample_teleport", "teleport", ("point", J.Vec(3, 4)));
            var world = BuildAndTarget(skill, Caster);

            Assert.True(world.Host.CastSkill(Caster, new Id("skill.sample_teleport"), System.Array.Empty<Id>()).Success);

            Assert.Equal(new Vec2(3, 4), world.Units.GetPosition(Caster));
        }

        [Fact]
        public void Teleport_MissingPoint_DefaultsToTargetsCurrentPosition_NoOp()
        {
            var skill = InstantSkill("skill.sample_teleport_noop", "teleport");
            var world = BuildAndTarget(skill, Caster);
            var before = world.Units.GetPosition(Caster);

            Assert.True(world.Host.CastSkill(Caster, new Id("skill.sample_teleport_noop"), System.Array.Empty<Id>()).Success);

            Assert.Equal(before, world.Units.GetPosition(Caster));
        }

        // -----------------------------------------------------------------
        // learn_skill（效果原语分支，区别于任务书拍板补充的 SkillHost.LearnSkill 直接调用方式）
        // -----------------------------------------------------------------

        [Fact]
        public void LearnSkillEffect_GrantsSkillToTarget()
        {
            var skill = InstantSkill("skill.sample_teach", "learn_skill", ("skill_id", J.S("skill.sample_granted")));
            var world = BuildAndTarget(skill, Target);

            Assert.False(world.Host.Knows(Target, new Id("skill.sample_granted")));

            Assert.True(world.Host.CastSkill(Caster, new Id("skill.sample_teach"), System.Array.Empty<Id>()).Success);

            Assert.True(world.Host.Knows(Target, new Id("skill.sample_granted")));
        }

        // -----------------------------------------------------------------
        // projectile（未注入 IProjectileSpawner 的降级路径——真实生成路径由
        // core/carriers/projectile 模块测试覆盖，不在本模块依赖范围内）
        // -----------------------------------------------------------------

        [Fact]
        public void Projectile_WithoutSpawnerInjected_WarnsAndDoesNotThrow()
        {
            var skill = InstantSkill("skill.sample_projectile", "projectile", ("speed", J.N(10)));
            var world = BuildAndTarget(skill, Target);

            var result = world.Host.CastSkill(Caster, new Id("skill.sample_projectile"), System.Array.Empty<Id>());

            Assert.True(result.Success);
            Assert.Contains(world.Diagnostics.Warnings, w => w.Contains("Projectile") || w.Contains("IProjectileSpawner"));
        }

        // -----------------------------------------------------------------
        // summon / open_lock / create_item / set_world_flag / script
        // （IEffectExtension 五类委托效果原语——未注入扩展点的降级路径 + 注入假扩展点后的分派路径）
        // -----------------------------------------------------------------

        [Theory]
        [InlineData("summon")]
        [InlineData("open_lock")]
        [InlineData("create_item")]
        [InlineData("set_world_flag")]
        [InlineData("script")]
        public void ExtensionKinds_WithoutExtensionInjected_WarnAndDoNotThrow(string effectKind)
        {
            var skill = InstantSkill($"skill.sample_ext_{effectKind}", effectKind);
            var world = BuildAndTarget(skill, Target);

            var result = world.Host.CastSkill(Caster, new Id($"skill.sample_ext_{effectKind}"), System.Array.Empty<Id>());

            Assert.True(result.Success);
            Assert.Contains(world.Diagnostics.Warnings, w => w.Contains("IEffectExtension"));
        }

        [Theory]
        [InlineData("summon")]
        [InlineData("open_lock")]
        [InlineData("create_item")]
        [InlineData("set_world_flag")]
        [InlineData("script")]
        public void ExtensionKinds_WithExtensionInjected_DispatchesToExtension(string effectKind)
        {
            var skill = InstantSkill($"skill.sample_ext2_{effectKind}", effectKind);
            var extension = new RecordingEffectExtension();
            var builder = new SkillWorldBuilder { Extension = extension };
            var world = builder.SkillDef(skill).Build();
            world.AddUnit(Caster);
            world.AddUnit(Target);
            world.Targets.SetChain(new Id("target.chain.sample"), Target);

            var result = world.Host.CastSkill(Caster, new Id($"skill.sample_ext2_{effectKind}"), System.Array.Empty<Id>());

            Assert.True(result.Success);
            var handled = Assert.Single(extension.Handled);
            Assert.Equal(EffectKindNames.Parse(effectKind), handled.Kind);
            Assert.Empty(world.Diagnostics.Warnings);
        }

        /// <summary>记录型 <see cref="IEffectExtension"/>：对任何 kind 都"处理成功"，记录收到的
        /// <see cref="EffectContext"/>，供断言分派确实到达扩展点。</summary>
        private sealed class RecordingEffectExtension : IEffectExtension
        {
            public readonly System.Collections.Generic.List<EffectContext> Handled = new();

            public bool TryHandle(EffectContext context, out ResolveResult result)
            {
                Handled.Add(context);
                result = new ResolveResult(HitResult.Hit, 0, 0, 0, immune: false, isHeal: false);
                return true;
            }
        }
    }
}
