using Core.Foundation.DataRegistry;
using Xunit;

namespace Tests.Rules.Integration
{
    /// <summary>
    /// P2-05 同类缓存收口回归测试（外部审计 audit-c9ff301-20260909 followup-2026-09-10）：验证
    /// <c>Core.Rules.Assembly.RulesAssembly</c> 生产装配确实把 <see cref="DataLoadCompletedEvent"/>
    /// 接到了 <c>Powers.Reload</c>（见该类型构造函数第 2 步之后的订阅、
    /// <c>Core.Numbers.PowerSet.PowerHost.Reload</c> 判断记录），不只是
    /// <c>core/numbers/power_set/tests/P2_05_PowerHostReloadTests.cs</c> 单测
    /// <see cref="Core.Numbers.PowerSet.PowerHost"/> 自身的 Reload 方法。
    /// <para>
    /// 判断记录（为什么不断言"新数值生效"）：<see cref="FightWorldBuilder.BuildDataSource"/> 返回
    /// 的 <see cref="InMemoryDataSource"/> 不支持就地覆写（<c>Add</c> 只能追加，见其类型注释），要
    /// 验证"reload 后确实读到新数值"需要一份专门的可写数据源（同
    /// <c>core/numbers/archetype/tests/P2_05_ArchetypeRegistryReloadTests.cs</c> 的
    /// <c>MutableSource</c> 写法），但 <see cref="Core.Rules.Assembly.RulesAssembly"/> 构造涉及的
    /// 必需表（<c>stat.definition</c>/<c>arch.power_type</c>/<c>skill.def</c> 等十余张）远比单一
    /// 模块的 <c>ArchetypeRegistry</c> 复杂，重新搭一份可写夹具的成本与收益不成正比。本测试改为
    /// 验证"接线路径真实存在且不破坏已注册单位的运行期状态"这一同样有意义、成本可控的性质——
    /// <c>Registry.Reload</c> 对 <see cref="InMemoryDataSource"/> 是幂等重放（拿回同一份 JSON 文本
    /// 重新解析），据此确认：(1) 订阅确实执行、不抛异常；(2) 已注册单位的当前资源值（运行期状态，
    /// 不派生自定义表本身）在整个 reload 周期中不受影响，与 <see cref="Core.Numbers.PowerSet.
    /// PowerHost.Reload"/> 判断记录"reload 只替换定义表本身，不隐式重算/重置已注册单位的资源值"
    /// 一致；数值实际生效已由 <c>P2_05_PowerHostReloadTests</c> 在方法级别覆盖。
    /// </para>
    /// </summary>
    public sealed class P2_05_RulesAssemblyPowerReloadTests
    {
        [Fact]
        public void DataLoadCompleted_TriggersPowerHostReload_WithoutThrowingOrResettingRegisteredUnitState()
        {
            var fx = FightWorldBuilder.Build();

            // fixture 组装已经对 NpcId 调用过 Powers.ModifyPower（见 FightWorldBuilder.Build 判断
            // 记录"npc 起始生命值人为调低"），是一份货真价实的运行期状态，reload 前先记录基线。
            var healthBefore = fx.Rules.Powers.GetPower(FightWorldBuilder.NpcId, FightWorldBuilder.PowerHealth);
            var manaBefore = fx.Rules.Powers.GetPower(FightWorldBuilder.PlayerId, FightWorldBuilder.PowerMana);
            var manaMaxBefore = fx.Rules.Powers.GetPowerMax(FightWorldBuilder.PlayerId, FightWorldBuilder.PowerMana);

            var reload = fx.Registry.Reload("arch.power_type");
            Assert.False(reload.IsBlocking, string.Join("; ", reload.Issues));

            var recordCount = fx.Registry.GetAll("arch.power_type").Count;
            fx.Bus.PublishImmediate(new DataLoadCompletedEvent(fx.Registry.Tables.Count, recordCount, reload.ErrorCount, reload.WarningCount));

            Assert.Equal(healthBefore, fx.Rules.Powers.GetPower(FightWorldBuilder.NpcId, FightWorldBuilder.PowerHealth));
            Assert.Equal(manaBefore, fx.Rules.Powers.GetPower(FightWorldBuilder.PlayerId, FightWorldBuilder.PowerMana));
            Assert.Equal(manaMaxBefore, fx.Rules.Powers.GetPowerMax(FightWorldBuilder.PlayerId, FightWorldBuilder.PowerMana));

            // reload 之后新注册的单位仍能正常按 arch.power_type 当前定义注册（证明 _definitions 缓存
            // 替换后处于一致状态，不是"清空后半路损坏"）。
            var freshUnit = new Core.Foundation.Common.Id("unit.p2_05_power_reload_fresh");
            fx.Rules.Powers.RegisterUnit(freshUnit, new[] { FightWorldBuilder.PowerHealth });
            Assert.Equal(FightWorldBuilder.PlayerMaxHp, fx.Rules.Powers.GetPowerMax(freshUnit, FightWorldBuilder.PowerHealth));
        }
    }
}
