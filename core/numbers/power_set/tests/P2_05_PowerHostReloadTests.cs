using Core.Foundation.Common;
using Core.Numbers.PowerSet;
using Xunit;

namespace Tests.Numbers.PowerSet
{
    /// <summary>
    /// P2-05 同类缓存收口回归测试（外部审计 audit-c9ff301-20260909 followup-2026-09-10）：<see
    /// cref="PowerHost"/> 的资源类型定义缓存（见 <see cref="PowerHost.Reload"/> 判断记录）与
    /// <c>Core.Rules.Skill.SkillDefCache</c>/<c>Core.Numbers.Archetype.ArchetypeRegistry</c>
    /// 同一类"构造期一次性建索引、之后只读"模式，此前
    /// 没有任何刷新入口。本类型是 L1，刻意不持有 <c>IDataRegistryView</c>，构造签名只接收预解析的
    /// <c>powerTypes</c> 列表——新增 <see cref="PowerHost.Reload"/> 走与 <c>QuestHost.Reload</c>/
    /// <c>DialogHost.Reload</c> 相同的"新增可选能力，不改变既有构造签名"处理方式；生产装配的接线
    /// 见 <c>core/rules/assembly.RulesAssembly</c> 构造函数、端到端验证见
    /// <c>core/rules/tests/Integration/P2_05_RulesAssemblyPowerReloadTests.cs</c>。本测试只验证
    /// <see cref="PowerHost"/> 自身的 <see cref="PowerHost.Reload"/> 行为，不经过 DataRegistry。
    /// </summary>
    public sealed class P2_05_PowerHostReloadTests
    {
        private static readonly Id Hero = new Id("unit.p2_05_power_reload");
        private static readonly Id Rage = new Id("arch.power.p2_05_rage");

        [Fact]
        public void Reload_ThenRegisterNewUnit_SeesUpdatedFixedMaxValue()
        {
            var bus = PowerTestSupport.CreateBus();
            var rageV1 = PowerTestSupport.FixedType("arch.power.p2_05_rage", maxValue: 100);
            var host = new PowerHost(new[] { rageV1 }, bus);

            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.p2_05_rage"));
            Assert.Equal(100, host.GetPowerMax(Hero, Rage));

            // 修复前：Reload 不存在，无法在不重建 host 的情况下让 _definitions 缓存看到新数据（见
            // PowerHost.Reload 判断记录，同 SkillDefCache/ArchetypeRegistry 缺口的同一类问题）。
            // 判断记录（为什么不对 Hero 调 RecomputeMax 验证）：RecomputeMax 只重算
            // MaxSourceKind==Stat 的资源（见其实现"MaxSourceKind != Stat 时 continue"）——Fixed 上限
            // 本就是"注册时定档，之后不随定义变化自动漂移"的既有设计（同本类型既有惯例，非本次
            // 改动引入），因此用一个 reload 之后才注册的新单位验证缓存已经替换，而不是对已注册单位
            // 断言 Fixed 上限会隐式变化（那样断言的是本类型从未有过的行为，不是 P2-05 的修复目标）。
            var rageV2 = PowerTestSupport.FixedType("arch.power.p2_05_rage", maxValue: 250);
            host.Reload(new[] { rageV2 });

            var newHero = new Id("unit.p2_05_power_reload_new");
            host.RegisterUnit(newHero, PowerTestSupport.Ids("arch.power.p2_05_rage"));
            Assert.Equal(250, host.GetPowerMax(newHero, Rage));
        }

        [Fact]
        public void Reload_DoesNotResetAlreadyRegisteredUnitCurrentValue()
        {
            var bus = PowerTestSupport.CreateBus();
            var rageV1 = PowerTestSupport.FixedType("arch.power.p2_05_rage", maxValue: 100, startFull: false, min: 0);
            var host = new PowerHost(new[] { rageV1 }, bus);

            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.p2_05_rage"));
            host.ModifyPower(Hero, Rage, 42, sourceId: new Id("test.source"));
            Assert.Equal(42, host.GetPower(Hero, Rage));

            // reload 只应影响定义缓存，绝不能触碰 _units 运行期状态（同 ArchetypeRegistry/
            // StatHost/LootHost/EconomyHost 系列修复的同一条原则："reload 只刷新定义表本身，不
            // 倒退/不重置已经存在的运行期状态"）。
            var rageV2 = PowerTestSupport.FixedType("arch.power.p2_05_rage", maxValue: 100, startFull: false, min: 0);
            host.Reload(new[] { rageV2 });

            Assert.Equal(42, host.GetPower(Hero, Rage));
        }

        [Fact]
        public void Reload_SupportsAddingNewPowerType_NotPresentBeforeReload()
        {
            var bus = PowerTestSupport.CreateBus();
            var rage = PowerTestSupport.FixedType("arch.power.p2_05_rage", maxValue: 100);
            var host = new PowerHost(new[] { rage }, bus);

            var mana = PowerTestSupport.FixedType("arch.power.p2_05_mana", maxValue: 30);
            host.Reload(new[] { rage, mana });

            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.p2_05_rage", "arch.power.p2_05_mana"));
            Assert.Equal(30, host.GetPowerMax(Hero, new Id("arch.power.p2_05_mana")));
        }

        // -----------------------------------------------------------------
        // ADR-0125 第三批探针缺陷：Reload 原子性（先 Clear 后逐个加入，中途失败旧表已被清空）
        // -----------------------------------------------------------------

        private static System.Collections.Generic.IEnumerable<PowerTypeDefinition> ThrowingSequence(
            PowerTypeDefinition first)
        {
            yield return first;
            throw new System.InvalidOperationException("boom-reload");
        }

        /// <summary>复现：新列表里有重复 id，Reload 抛 <see cref="System.ArgumentException"/>，
        /// 此前旧的资源类型表已被 Clear——此后为旧类型注册单位会抛"未知资源类型"。不变量：失败后旧表
        /// 逐项不变（旧类型仍可注册并读到旧上限，新列表里已解析的类型不漏进去）。</summary>
        [Fact]
        public void Reload_DuplicateIdInNewList_Throws_AndKeepsOldDefinitions()
        {
            var bus = PowerTestSupport.CreateBus();
            var rageV1 = PowerTestSupport.FixedType("arch.power.p2_05_rage", maxValue: 100);
            var host = new PowerHost(new[] { rageV1 }, bus);
            var mana = PowerTestSupport.FixedType("arch.power.p2_05_mana", maxValue: 30);
            var rageDup1 = PowerTestSupport.FixedType("arch.power.p2_05_rage", maxValue: 250);
            var rageDup2 = PowerTestSupport.FixedType("arch.power.p2_05_rage", maxValue: 260);

            Assert.Throws<System.ArgumentException>(() => host.Reload(new[] { mana, rageDup1, rageDup2 }));

            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.p2_05_rage"));
            Assert.Equal(100, host.GetPowerMax(Hero, Rage)); // 旧定义原样
            var other = new Id("unit.p2_05_power_reload_other");
            Assert.ThrowsAny<System.Exception>(() =>
                host.RegisterUnit(other, PowerTestSupport.Ids("arch.power.p2_05_mana"))); // 新列表里解析过的 mana 不得漏入
        }

        [Fact]
        public void Reload_NullElementOrNullList_Throws_AndKeepsOldDefinitions()
        {
            var bus = PowerTestSupport.CreateBus();
            var rageV1 = PowerTestSupport.FixedType("arch.power.p2_05_rage", maxValue: 100);
            var host = new PowerHost(new[] { rageV1 }, bus);
            var mana = PowerTestSupport.FixedType("arch.power.p2_05_mana", maxValue: 30);

            Assert.Throws<System.ArgumentException>(() => host.Reload(new PowerTypeDefinition[] { mana, null! }));
            Assert.Throws<System.ArgumentNullException>(() => host.Reload(null!));

            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.p2_05_rage"));
            Assert.Equal(100, host.GetPowerMax(Hero, Rage));
        }

        /// <summary>枚举新定义时来源本身抛异常（例如数据层懒加载失败）：异常原样向上抛，旧表不变。</summary>
        [Fact]
        public void Reload_SequenceThrowsMidway_PropagatesAndKeepsOldDefinitions()
        {
            var bus = PowerTestSupport.CreateBus();
            var rageV1 = PowerTestSupport.FixedType("arch.power.p2_05_rage", maxValue: 100);
            var host = new PowerHost(new[] { rageV1 }, bus);
            var mana = PowerTestSupport.FixedType("arch.power.p2_05_mana", maxValue: 30);

            var ex = Assert.Throws<System.InvalidOperationException>(() => host.Reload(ThrowingSequence(mana)));
            Assert.Equal("boom-reload", ex.Message);

            host.RegisterUnit(Hero, PowerTestSupport.Ids("arch.power.p2_05_rage"));
            Assert.Equal(100, host.GetPowerMax(Hero, Rage));
        }
    }
}
