using System;
using Core.Foundation.SimLoop;

namespace Tests.Foundation.SimLoop
{
    /// <summary>测试专用：把一个委托适配成 <see cref="ITickPhaseHandler"/>，
    /// 避免为每个测试用例单独定义一个处理器类。</summary>
    internal sealed class DelegatePhaseHandler : ITickPhaseHandler
    {
        private readonly Action<SimStep, IWorldSim> _execute;

        public DelegatePhaseHandler(Action<SimStep, IWorldSim> execute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        public void Execute(SimStep step, IWorldSim world) => _execute(step, world);
    }
}
