using Core.Foundation.Common;

namespace Core.Carriers.Common
{
    /// <summary>
    /// GameObject 交互接口（见 07 第 3.6 节）。由 <c>core/carriers/gobj</c> 实现（见 01 L3 模块表
    /// <c>gobj</c> 行契约接口名）。
    /// </summary>
    public interface IGameObjectHost
    {
        /// <summary>交互的统一入口，按 <c>on_use</c> 类型分发到技能/对话系统（见 07 第 3.3、3.6
        /// 节），成功时发出 <c>gobj.interacted</c>。</summary>
        InteractResult Interact(Id unitId, Id gobjInstanceId);

        /// <summary>开锁尝试的独立入口，按 <see cref="LockRequirement"/> 三种变体校验（见 07 第
        /// 3.2、3.6 节），供未直接经 <see cref="Interact"/> 触发的开锁场景（如 <c>open_lock</c> 效果）
        /// 复用；开锁成功时该物件状态变化经 <c>gobj.state_changed</c> 通知（见 07 第 3.4 节）。</summary>
        bool TryUnlock(Id unitId, Id gobjInstanceId);
    }
}
