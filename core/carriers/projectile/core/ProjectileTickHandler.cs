using System;
using Core.Foundation.SimLoop;

namespace Core.Carriers.Projectile
{
    /// <summary>
    /// 挂在 <see cref="TickPhase.MovementAndNavigation"/> 阶段（紧随
    /// <c>Core.Carriers.Unit.MovementTickHandler</c> 之后注册，见
    /// <c>Core.Carriers.Assembly.CarriersAssembly</c> 判断记录"tick 阶段挂载"）的投射物飞行推进器：
    /// 每 tick 调用一次 <see cref="ProjectileHost.Advance"/>。
    /// <para>
    /// 判断记录（挂载阶段：移动步之后、战斗结算步之前）：03 第 4.2 节八步固定顺序里没有一个专门的
    /// "投射物飞行"阶段，任务拍板"移动与导航步之后、战斗结算步之前"——03 的
    /// <see cref="TickPhase"/> 枚举本身没有能表达"两个阶段之间"的插槽，只能选择挂到已有阶段之一。
    /// 本类选择挂到 <see cref="TickPhase.MovementAndNavigation"/>（而不是新建阶段，那属于"新增
    /// 原语"级别的改动）并在该阶段内排在 <c>MovementTickHandler</c> 之后（同一阶段内多个处理器按
    /// 注册顺序执行，见 <see cref="IWorldSim.RegisterPhaseHandler"/> 注释）——这样投射物用的是
    /// "本 tick 全部单位已完成移动之后"的最新位置做命中判定，且整个 <c>MovementAndNavigation</c>
    /// 阶段本身天然排在 <see cref="TickPhase.CombatResolution"/> 之前，同时满足"移动步之后"（阶段
    /// 内顺序）与"战斗结算步之前"（阶段间顺序）两个要求。
    /// </para>
    /// </summary>
    public sealed class ProjectileTickHandler : ITickPhaseHandler
    {
        private readonly ProjectileHost _host;
        private readonly ProjectileOptions _options;

        public ProjectileTickHandler(ProjectileHost host, ProjectileOptions? options = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _options = options ?? new ProjectileOptions();
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            // 惯例同 MovementTickHandler：离散步下按每回合等效秒数换算飞行距离，不为离散模式另写
            // 一套推进公式（见 ProjectileOptions.DiscreteTickEquivalentSeconds 判断记录）。
            var dt = step.Kind == SimStepKind.Continuous ? step.Dt : _options.DiscreteTickEquivalentSeconds;
            _host.Advance(dt);
        }
    }
}
