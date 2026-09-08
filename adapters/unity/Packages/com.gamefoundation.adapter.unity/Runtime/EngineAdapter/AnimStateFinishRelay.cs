#nullable enable
// AnimStateFinishRelay：PR150-02 根治新增（architecture/落地计划/audit-3224ca1-20260908/AUDIT_REPORT.md
// PR150-02"动画在首次检测前已经自动退出时，仍漏发 finished"——见 UnityRenderer3D.Tick/
// IsAnimatorStateFinished 判断记录"漏发窗口"：旧实现完全依赖 UnityRenderer3D.Tick 外部轮询采样
// AnimatorStateInfo，若目标状态的自动过渡（进入+自动退出）整个发生在两次 Tick 之间，采样永远看不到
// 过渡本身，anim_event.finished 因此彻底漏发）。
//
// 本类型作为 StateMachineBehaviour 挂在 AnimatorController 的具体状态上（占位内容由
// Editor/GeneratePlaceholderModelAssets.cs 生成时预置到 idle/attack/cast/hit/test_autoexit 五个状态；
// 具体游戏内容需要对"依赖 finished 事件的状态"按同一惯例自行在 AnimatorController 里挂上本行为，同
// 09 第 4.4 节 UnityRenderer3D.AnimEventFunctionName 一贯的"游戏内容需遵循的引擎适配层接线惯例"——
// 未挂本行为的状态完全退回既有采样判定，不受影响，也不报错），把 Unity 动画系统自己驱动的
// OnStateEnter/OnStateExit（在 Animator.Update 内部同步触发，不依赖 UnityRenderer3D.Tick 的外部轮询
// 节奏）转发回所属 UnityRenderer3D 实例（经 UnityRenderer3D.OnAnimStateEvent），使"本次播放确实进入过
// 目标状态、又确实离开了目标状态"这一信号不再依赖 Tick 恰好采样到过渡窗口。
//
// 判断记录（为什么 UnityRenderer3D.AttachVisual 用 GetBehaviours<T>() 而不是 GetBehaviour<T>()）：
// AnimatorController 可以在多个状态上各自挂一份本行为，Unity 按"每个（Animator 实例, 挂了该行为的
// 状态）组合"各自克隆一份行为对象——同一个 Animator 实例因此可能同时持有多个 AnimStateFinishRelay
// 克隆（如 idle 一份、attack 一份），AttachVisual 把 Owner/HandleValue 广播给全部克隆；每个克隆只会在
// "它自己所挂的那个状态"进入/离开时收到回调，靠 UnityRenderer3D.OnAnimStateEvent 内部按
// stateHash/layerIndex 与当前正在追踪的目标状态比对，天然过滤掉与当前播放无关的状态转换。
//
// public——AnimatorState.AddStateMachineBehaviour<T>() 是 Editor-only API，调用方
// Editor/GeneratePlaceholderModelAssets.cs 编译进独立的 Editor 程序集，需要本类型跨程序集可见；
// Bind 方法本身仍是 internal（同惯例见 ModelAnimEventRelay），只供同程序集内的 UnityRenderer3D 调用，
// Editor 侧脚本从不需要、也不应该调用它——生成器只负责把行为挂到状态定义上，运行时绑定完全是
// UnityRenderer3D.AttachVisual 的职责。
using UnityEngine;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class AnimStateFinishRelay : StateMachineBehaviour
    {
        private UnityRenderer3D? _owner;
        private int _handleValue;

        internal void Bind(UnityRenderer3D owner, int handleValue)
        {
            _owner = owner;
            _handleValue = handleValue;
        }

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            _owner?.OnAnimStateEvent(_handleValue, stateInfo.shortNameHash, layerIndex, entered: true, stateInfo.normalizedTime);
        }

        public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
        {
            _owner?.OnAnimStateEvent(_handleValue, stateInfo.shortNameHash, layerIndex, entered: false, stateInfo.normalizedTime);
        }
    }
}
