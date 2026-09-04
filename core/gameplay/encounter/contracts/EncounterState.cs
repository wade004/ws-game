using System.Collections.Generic;

namespace Core.Gameplay.Encounter
{
    /// <summary>
    /// 一个遭遇实例当前状态的只读快照（供 <see cref="IEncounterHost.GetState"/>、UI/调试查询，
    /// 任务书拍板"阶段、已触发波次、活跃"三项）。
    /// </summary>
    public readonly struct EncounterState
    {
        /// <summary>当前所处阶段下标；-1 表示尚未进入任何阶段（<c>encounter.def</c> 未定义
        /// <c>phases</c>，或已定义但尚无一条 <c>enter_condition</c> 满足）。</summary>
        public int CurrentPhaseIndex { get; }

        /// <summary>按 <c>encounter.def.waves</c> 声明顺序排列，每条波次是否已触发过一次。</summary>
        public IReadOnlyList<bool> WaveTriggered { get; }

        /// <summary>该实例是否仍在进行中（尚未 <c>Won</c>/<c>Lost</c>/<see cref="IEncounterHost.Abort"/>）。</summary>
        public bool IsActive { get; }

        public EncounterState(int currentPhaseIndex, IReadOnlyList<bool> waveTriggered, bool isActive)
        {
            CurrentPhaseIndex = currentPhaseIndex;
            WaveTriggered = waveTriggered;
            IsActive = isActive;
        }
    }
}
