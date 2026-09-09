using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Skill
{
    /// <summary>周期效果/吸收/免疫/控制标志/驱散（见落地方案 T2-5 行、06 第 3.3 节）。</summary>
    public sealed class AuraEffectTests
    {
        [Fact]
        public void PeriodicDamage_TicksExpectedNumberOfTimes()
        {
            var aura = J.O(
                ("id", J.S("skill.aura_def.sample_dot")),
                ("duration", J.N(6)),
                ("effects", J.A(
                    J.O(("kind", J.S("periodic_damage")),
                        ("params", J.O(
                            ("interval", J.N(2)),
                            ("base_value", J.N(3)),
                            ("coefficient", J.N(0)),
                            ("school", J.S("skill.school_sample"))))))));

            var world = new SkillWorldBuilder().AuraDef(aura).Build();
            world.AddUnit(new Id("unit.target"));

            world.Host.EffectSink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_dot"), new Id("unit.source"));

            world.Host.Update(2.0);
            world.Host.Update(2.0);
            world.Host.Update(2.0);

            Assert.Equal(3, world.Combat.ResolveCalls.Count);
            Assert.All(world.Combat.ResolveCalls, ctx => Assert.Equal(EffectKind.SchoolDamage, ctx.Kind));
            Assert.All(world.Combat.ResolveCalls, ctx => Assert.True(ctx.IsPeriodic));
        }

        /// <summary>
        /// P3-04 根治（外部审计 audit-c9ff301-20260909）：<c>periodic_damage</c>/<c>periodic_heal</c>
        /// 的 <c>params.scaling_stat</c> 运行期确实被 <see cref="Core.Rules.Skill.EffectDispatcher.ApplyEffect"/>
        /// 消费（<c>periodic_damage</c> 与非周期 <c>school_damage</c> 共用同一条
        /// <c>ApplyDamageOrHeal</c> 结算路径），此前只在 <c>SkillSchemas.PeriodicParamsCase</c> 漏登记
        /// 这个字段（<c>DamageOrHealParams</c> 那份非周期登记里一直都有）。本测试端到端验证：来源
        /// 单位的属性值确实参与了周期效果的缩放计算，不只是 schema 登记本身。
        /// </summary>
        [Fact]
        public void PeriodicDamage_WithScalingStat_ScalesBySourceStat()
        {
            var aura = J.O(
                ("id", J.S("skill.aura_def.sample_dot_scaled")),
                ("duration", J.N(2)),
                ("effects", J.A(
                    J.O(("kind", J.S("periodic_damage")),
                        ("params", J.O(
                            ("interval", J.N(2)),
                            ("base_value", J.N(3)),
                            ("coefficient", J.N(2)),
                            ("school", J.S("skill.school_sample")),
                            ("scaling_stat", J.S("stat.p3_04_power"))))))));

            var world = new SkillWorldBuilder().Stat("stat.p3_04_power", defaultBase: 5).AuraDef(aura).Build();
            var source = new Id("unit.p3_04_source");
            var target = new Id("unit.p3_04_target");
            world.AddUnit(source);
            world.AddUnit(target);

            world.Host.EffectSink.ApplyAura(target, new Id("skill.aura_def.sample_dot_scaled"), source);
            world.Host.Update(2.0);

            // base_value(3) + coefficient(2) * 来源 stat.p3_04_power(5) = 13。
            Assert.Single(world.Combat.ResolveCalls);
            Assert.Equal(13, world.Combat.ResolveCalls[0].BaseValue);
        }

        // 收边任务补齐（AuraHost 自愈，见该类型 OnEntityDestroyed 判断记录）：目标单位被销毁
        // （entity.destroyed）时应立即移除其名下光环实例，此后即便继续 Update 推进，也不应再对该
        // 目标结算周期效果——此前 AuraHost 不订阅任何事件，唯一能感知"目标已消失"的时机是下一次
        // Update 命中周期计时器时，届时会对着一个只在 IWorldSim 层面消失、但 AuraHost 自己的实例表
        // 里仍然存在的目标结算，命中真实 WorldUnitAccess 实现会抛异常（本假想世界的 FakeUnitAccess
        // 不会抛，测试改为直接断言"不再产生新的 ResolveEffect 调用"，语义等价、不依赖真实抛异常
        // 路径）。
        [Fact]
        public void EntityDestroyed_RemovesAuraInstance_AndStopsPeriodicTicking()
        {
            var aura = J.O(
                ("id", J.S("skill.aura_def.sample_dot")),
                ("duration", J.N(30)),
                ("effects", J.A(
                    J.O(("kind", J.S("periodic_damage")),
                        ("params", J.O(
                            ("interval", J.N(2)),
                            ("base_value", J.N(3)),
                            ("coefficient", J.N(0)),
                            ("school", J.S("skill.school_sample"))))))));

            var world = new SkillWorldBuilder().AuraDef(aura).Build();
            world.AddUnit(new Id("unit.target"));

            world.Host.EffectSink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_dot"), new Id("unit.source"));
            Assert.True(world.Host.AuraQuery.HasAura(new Id("unit.target"), new Id("skill.aura_def.sample_dot")));

            world.Host.Update(2.0);
            Assert.Single(world.Combat.ResolveCalls); // 到期前先正常触发一次，证明光环本身在正常运作。

            world.Bus.PublishImmediate(new EntityDestroyedEvent(new Id("unit.target")));
            Assert.False(world.Host.AuraQuery.HasAura(new Id("unit.target"), new Id("skill.aura_def.sample_dot")), "entity.destroyed 后光环实例应当已被移除");

            world.Host.Update(2.0);
            world.Host.Update(2.0);
            Assert.Single(world.Combat.ResolveCalls); // 目标已消失，不应再产生新的周期结算调用。
        }

        [Fact]
        public void EntityDestroyed_UnrelatedUnit_DoesNotRemoveAura()
        {
            var aura = J.O(
                ("id", J.S("skill.aura_def.sample_dot2")),
                ("duration", J.N(30)),
                ("effects", J.A(
                    J.O(("kind", J.S("periodic_damage")),
                        ("params", J.O(
                            ("interval", J.N(2)),
                            ("base_value", J.N(3)),
                            ("coefficient", J.N(0)),
                            ("school", J.S("skill.school_sample"))))))));

            var world = new SkillWorldBuilder().AuraDef(aura).Build();
            world.AddUnit(new Id("unit.target"));

            world.Host.EffectSink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_dot2"), new Id("unit.source"));

            world.Bus.PublishImmediate(new EntityDestroyedEvent(new Id("unit.unrelated")));

            Assert.True(world.Host.AuraQuery.HasAura(new Id("unit.target"), new Id("skill.aura_def.sample_dot2")), "不相关单位销毁不应影响本目标的光环");
        }

        [Fact]
        public void Absorb_RemovesInstance_WhenPoolDepleted()
        {
            var aura = J.O(
                ("id", J.S("skill.aura_def.sample_shield")),
                ("duration", J.N(30)),
                ("effects", J.A(
                    J.O(("kind", J.S("absorb")),
                        ("params", J.O(("amount", J.N(20)), ("school", J.S("skill.school_sample"))))))));

            var world = new SkillWorldBuilder().AuraDef(aura).Build();
            world.AddUnit(new Id("unit.target"));

            world.Host.EffectSink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_shield"), new Id("unit.source"));

            var consumed1 = world.Host.AuraQuery.ConsumeAbsorb(new Id("unit.target"), new Id("skill.school_sample"), 12);
            Assert.Equal(12, consumed1);
            Assert.True(world.Host.AuraQuery.HasAura(new Id("unit.target"), new Id("skill.aura_def.sample_shield")));

            var consumed2 = world.Host.AuraQuery.ConsumeAbsorb(new Id("unit.target"), new Id("skill.school_sample"), 12);
            Assert.Equal(8, consumed2); // 池只剩 8，无法满足全部 12
            world.Flush();

            Assert.False(world.Host.AuraQuery.HasAura(new Id("unit.target"), new Id("skill.aura_def.sample_shield")));
            var removed = world.Of<AuraRemovedEvent>();
            Assert.Contains(removed, e => e.Reason == "absorb_depleted");
        }

        [Fact]
        public void Immunity_BlocksEffect_AndSkipsCombatCall()
        {
            var aura = J.O(
                ("id", J.S("skill.aura_def.sample_immune")),
                ("duration", J.N(30)),
                ("effects", J.A(
                    J.O(("kind", J.S("immunity")),
                        ("params", J.O(
                            ("schools", J.Ids("skill.school_sample")),
                            ("effect_kinds", J.A(J.S("school_damage")))))))));

            var world = new SkillWorldBuilder().AuraDef(aura).Build();
            world.AddUnit(new Id("unit.target"));

            world.Host.EffectSink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_immune"), new Id("unit.source"));

            Assert.True(world.Host.AuraQuery.IsImmune(new Id("unit.target"), new Id("skill.school_sample"), EffectKind.SchoolDamage));

            var context = new EffectContext(
                new Id("unit.source"), new Id("unit.target"), new Id("skill.sample_bolt"), EffectKind.SchoolDamage,
                new Id("skill.school_sample"), 10, 0);

            var result = world.Host.EffectSink.ApplyEffect(context);

            Assert.True(result.Immune);
            Assert.Empty(world.Combat.ResolveCalls);
        }

        [Fact]
        public void ControlFlags_AggregateAcrossActiveAuras()
        {
            var aura = J.O(
                ("id", J.S("skill.aura_def.sample_root")),
                ("duration", J.N(30)),
                ("effects", J.A(
                    J.O(("kind", J.S("control")),
                        ("params", J.O(("flags", J.A(J.S("no_move")))))))));

            var world = new SkillWorldBuilder().AuraDef(aura).Build();
            world.AddUnit(new Id("unit.target"));

            Assert.Equal(ControlFlags.None, world.Host.AuraQuery.GetControlFlags(new Id("unit.target")));

            world.Host.EffectSink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_root"), new Id("unit.source"));

            Assert.Equal(ControlFlags.NoMove, world.Host.AuraQuery.GetControlFlags(new Id("unit.target")));
        }

        [Fact]
        public void IsImmune_StaticImmunityProvider_BlocksEffect_EvenWithoutImmunityAura()
        {
            // 阶段 3 整理"事项三"：AuraHost.IsImmune 在光环免疫（本例未施加任何 immunity 类光环）
            // 之外叠加查询注入的 IStaticImmunityProvider。
            var staticImmunity = new FakeStaticImmunityProvider()
                .SetImmune(new Id("unit.target"), new Id("skill.school_sample"), EffectKind.SchoolDamage);

            var world = new SkillWorldBuilder { StaticImmunity = staticImmunity }.Build();
            world.AddUnit(new Id("unit.target"));

            Assert.True(world.Host.AuraQuery.IsImmune(new Id("unit.target"), new Id("skill.school_sample"), EffectKind.SchoolDamage));

            var context = new EffectContext(
                new Id("unit.source"), new Id("unit.target"), new Id("skill.sample_bolt"), EffectKind.SchoolDamage,
                new Id("skill.school_sample"), 10, 0);

            var result = world.Host.EffectSink.ApplyEffect(context);

            Assert.True(result.Immune);
            Assert.Empty(world.Combat.ResolveCalls);
        }

        [Fact]
        public void ApplyAura_ControlImmuneUnit_DoesNotGainControlFlags()
        {
            // 阶段 3 整理"事项三"："control_immune 生物不吃 control 光环"验收项：静态控制免疫
            // （同 CreatureImmunityProvider 里 tier.control_immune → 全部控制免疫的语义）生效时，
            // 施加 control 类光环后 GetControlFlags 仍应为 None——不是"吃到了又立刻解除"，而是
            // 从未真正置位（见 AuraHost.ApplyStaticEffects 判断记录）。
            var aura = J.O(
                ("id", J.S("skill.aura_def.sample_root2")),
                ("duration", J.N(30)),
                ("effects", J.A(
                    J.O(("kind", J.S("control")),
                        ("params", J.O(("flags", J.A(J.S("no_move")))))))));

            var staticImmunity = new FakeStaticImmunityProvider()
                .SetControlImmune(new Id("unit.target"), ControlFlags.NoMove | ControlFlags.NoCast | ControlFlags.NoAttack | ControlFlags.NoInteract);

            var world = new SkillWorldBuilder { StaticImmunity = staticImmunity }.AuraDef(aura).Build();
            world.AddUnit(new Id("unit.target"));

            world.Host.EffectSink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_root2"), new Id("unit.source"));

            Assert.Equal(ControlFlags.None, world.Host.AuraQuery.GetControlFlags(new Id("unit.target")));
        }

        [Fact]
        public void Dispel_RemovesUpToCount_OfMatchingDispelType()
        {
            var poison1 = J.O(("id", J.S("skill.aura_def.sample_poison1")), ("duration", J.N(30)),
                ("dispel_type", J.S("skill.dispel.poison")), ("effects", J.A()));
            var poison2 = J.O(("id", J.S("skill.aura_def.sample_poison2")), ("duration", J.N(30)),
                ("dispel_type", J.S("skill.dispel.poison")), ("effects", J.A()));
            var curse = J.O(("id", J.S("skill.aura_def.sample_curse")), ("duration", J.N(30)),
                ("dispel_type", J.S("skill.dispel.curse")), ("effects", J.A()));

            var world = new SkillWorldBuilder().AuraDef(poison1).AuraDef(poison2).AuraDef(curse).Build();
            world.AddUnit(new Id("unit.target"));

            var sink = world.Host.EffectSink;
            sink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_poison1"), new Id("unit.source"));
            sink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_poison2"), new Id("unit.source"));
            sink.ApplyAura(new Id("unit.target"), new Id("skill.aura_def.sample_curse"), new Id("unit.source"));

            var context = new EffectContext(
                new Id("unit.dispeller"), new Id("unit.target"), new Id("skill.sample_cleanse"), EffectKind.Dispel,
                new Id("skill.school_sample"), 0, 0,
                J.O(("category", J.S("skill.dispel.poison")), ("count", J.N(1))));

            sink.ApplyEffect(context);

            Assert.False(world.Host.AuraQuery.HasAura(new Id("unit.target"), new Id("skill.aura_def.sample_poison1")));
            Assert.True(world.Host.AuraQuery.HasAura(new Id("unit.target"), new Id("skill.aura_def.sample_poison2")));
            Assert.True(world.Host.AuraQuery.HasAura(new Id("unit.target"), new Id("skill.aura_def.sample_curse")));
        }
    }
}
