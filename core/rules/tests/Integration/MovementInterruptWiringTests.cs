using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Integration
{
    /// <summary>
    /// RC-08（见外部审计 architecture/落地计划/audit-b3b91ee-20260907/code-review.md RC-08）：
    /// <c>SkillHost.NotifyMoved</c>（<c>skill.def.interrupt_flags</c> 含 <c>"movement"</c> 时移动应
    /// 打断读条/引导，见 06 第 3.1 节）此前生产装配从未调用它——真实移动只发 <c>unit.moved</c>，
    /// <c>core/rules/skill</c> 完全不订阅。本测试用真实 <see cref="Core.Rules.Assembly.RulesAssembly"/>
    /// （经 <see cref="FightWorldBuilder"/>）验证 <c>unit.moved</c> 事件到达后是否真的打断了
    /// 声明 movement 中断的读条。
    /// </summary>
    public sealed class MovementInterruptWiringTests
    {
        /// <summary>
        /// 本模块（<c>core/rules</c>）不依赖 <c>core/carriers</c>（见 <c>Core.Rules.csproj</c> 只引用
        /// <c>Core.Numbers</c>，架构分层禁止 L2 反向依赖 L3），测试项目同理不引用
        /// <c>Core.Carriers</c>，因此不能直接 new 一个 <c>Core.Carriers.Common.UnitMovedEvent</c>。
        /// 本类原样复刻它的最小契约面（<c>Key</c>/<c>IExprReadableEvent.TryGetField("unitId", ...)</c>，
        /// 逐字段对照 <c>core/carriers/common/contracts/Events.cs</c> 的 <c>UnitMovedEvent</c> 实现）——
        /// <see cref="Core.Rules.Assembly.RulesAssembly"/> 的订阅回调只通过 <see cref="IExprReadableEvent"/>
        /// 这个通用接口读取 <c>unitId</c> 字段（见该组装根判断记录"RC-08 收边补齐"），不关心具体
        /// 类型，这里的替身在"生产装配实际会收到什么"这个意义上与真实事件等价。
        /// </summary>
        private sealed class FakeUnitMovedEvent : IEvent, IExprReadableEvent
        {
            public Id Key => EventKeys.UnitMoved;
            public Id UnitId { get; }
            public FakeUnitMovedEvent(Id unitId) => UnitId = unitId;

            public bool TryGetField(string name, out ExprValue value)
            {
                if (name == "unitId")
                {
                    value = ExprValue.OfId(UnitId);
                    return true;
                }

                value = default;
                return false;
            }
        }

        [Fact]
        public void UnitMovedEvent_InterruptsOngoingCast_WhenSkillDeclaresMovementInterruptFlag()
        {
            var fx = FightWorldBuilder.Build();

            var start = fx.Rules.Skill.CastSkill(
                FightWorldBuilder.PlayerId, FightWorldBuilder.SkillChannelMovementInterrupt, System.Array.Empty<Id>());
            Assert.True(start.Success);
            Assert.True(fx.Rules.Skill.IsCasting(FightWorldBuilder.PlayerId));

            // 修复前：RulesAssembly 完全不订阅 unit.moved，本次派发对 CastPipeline 没有任何影响，
            // 读条会继续进行（IsCasting 保持 true）。
            fx.Bus.Enqueue(new FakeUnitMovedEvent(FightWorldBuilder.PlayerId));
            fx.Bus.DispatchPending();

            Assert.False(fx.Rules.Skill.IsCasting(FightWorldBuilder.PlayerId));
        }

        [Fact]
        public void UnitMovedEvent_DoesNotInterrupt_WhenCastingSkillHasNoMovementInterruptFlag()
        {
            var fx = FightWorldBuilder.Build();

            var start = fx.Rules.Skill.CastSkill(FightWorldBuilder.PlayerId, FightWorldBuilder.SkillBurn, System.Array.Empty<Id>());
            Assert.True(start.Success);
            Assert.True(fx.Rules.Skill.IsCasting(FightWorldBuilder.PlayerId));

            fx.Bus.Enqueue(new FakeUnitMovedEvent(FightWorldBuilder.PlayerId));
            fx.Bus.DispatchPending();

            // skill.sample_burn 未声明 movement 中断——接线本身应只转调 NotifyMoved，不额外附加
            // 过滤/打断逻辑（过滤已经在 SkillHost.NotifyMoved/CastPipeline.NotifyMoved 内部完成，
            // 见该方法判断记录），本用例确认订阅没有"逢移动必打断"的过度实现。
            Assert.True(fx.Rules.Skill.IsCasting(FightWorldBuilder.PlayerId));
        }

        [Fact]
        public void UnitMovedEvent_ForDifferentUnit_DoesNotInterruptUnrelatedCaster()
        {
            var fx = FightWorldBuilder.Build();

            var start = fx.Rules.Skill.CastSkill(
                FightWorldBuilder.PlayerId, FightWorldBuilder.SkillChannelMovementInterrupt, System.Array.Empty<Id>());
            Assert.True(start.Success);

            fx.Bus.Enqueue(new FakeUnitMovedEvent(FightWorldBuilder.NpcId));
            fx.Bus.DispatchPending();

            Assert.True(fx.Rules.Skill.IsCasting(FightWorldBuilder.PlayerId));
        }
    }
}
