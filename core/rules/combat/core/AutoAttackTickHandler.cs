using System;
using Core.Foundation.SimLoop;

namespace Core.Rules.Combat
{
    /// <summary>
    /// ADR-0059：把 <see cref="AutoAttackHost.Update"/> 接入 sim_loop 的 tick 编排，挂在
    /// <see cref="TickPhase.CombatResolution"/>（与 <see cref="CombatTickHandler"/> 同一阶段——
    /// 普通攻击是战斗机制，随该阶段一起推进）。
    /// <para>
    /// 判断记录（离散步不驱动，惯例同 <see cref="CombatTickHandler"/> 对 ADR-0013 离散时间模型的
    /// 既有处理）："挥击计时驱动"本质是一个连续时间概念（间隔以秒计），<see cref="SimStep.Discrete"/>
    /// 步恒 <c>Dt = 0</c>、不代表"经过了多少时间"，本处理器对 Discrete 步不做任何事，不打诊断——
    /// 消费方若同时启用离散战斗模式与普通攻击，普通攻击在离散步内简单地不推进（等价于"未启用"，
    /// 不产生错误结果，也不产生攻击），本 ADR 不覆盖离散模式下普通攻击的等效换算（06/07 均未就
    /// "普通攻击在离散回合制下如何表现"给出既定结论，纯加法特性下把这个决定留给需要它的消费方，
    /// 不在本次任务里提前设计一个没有真实需求驱动的换算规则）。
    /// </para>
    /// </summary>
    public sealed class AutoAttackTickHandler : ITickPhaseHandler
    {
        private readonly AutoAttackHost _host;

        public AutoAttackTickHandler(AutoAttackHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            if (step.Kind == SimStepKind.Continuous)
            {
                _host.Update(step.Dt);
            }
        }
    }
}
