using Core.Foundation.Common;

namespace Core.Carriers.Common
{
    /// <summary>一次 <see cref="IGameObjectHost.Interact"/> 交互被分发到的处理路径（见 07 第 3.3 节
    /// <c>on_use</c> "指向一个 <c>skill.def</c>...或一个 <c>dialog.gossip_menu</c> 动作项，二者二选一"）。
    /// </summary>
    public enum InteractOutcome
    {
        /// <summary>分发到一个 <c>skill.def</c>（见 07 第 3.3 节）。</summary>
        Skill,

        /// <summary>分发到一个 <c>dialog.gossip_menu</c> 动作项（见 07 第 3.3 节）。</summary>
        Dialog,

        /// <summary>该物件当前处于锁定状态，交互被拒绝（见 07 第 3.2 节 <c>LockDef</c>）。</summary>
        Locked,

        /// <summary>该物件当前没有可执行的 <c>on_use</c>（如 <c>sign</c> 纯文本告示牌，见 07 第 3.1
        /// 节该行"无交互副作用"）。</summary>
        NoAction,

        /// <summary>未知/无法识别的物件实例或类型。</summary>
        Unknown,
    }

    /// <summary>
    /// 一次 <c>GameObjectHost.interact</c> 请求的结果（见 07 第 3.6 节）。
    /// </summary>
    public readonly struct InteractResult
    {
        public bool Success { get; }

        public InteractOutcome Outcome { get; }

        /// <summary>被分发到的目标 id（<see cref="InteractOutcome.Skill"/> 时为 <c>skill.def</c> id，
        /// <see cref="InteractOutcome.Dialog"/> 时为 <c>dialog.gossip_menu</c>/动作项 id）；其余情形
        /// 为 null。</summary>
        public Id? DispatchedRef { get; }

        public InteractResult(bool success, InteractOutcome outcome, Id? dispatchedRef = null)
        {
            Success = success;
            Outcome = outcome;
            DispatchedRef = dispatchedRef;
        }
    }
}
