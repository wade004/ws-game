using Core.Foundation.Common;
using Core.Foundation.EventBus;

namespace Presentation.Common
{
    /// <summary>
    /// View 绑定协议（见 09_表现层.md 第 2 节、03_运行时骨架.md 第 5 节）：每个逻辑对象进入可见
    /// 范围时由表现层创建一个与之绑定的 View，View 销毁时机由世界事件驱动，不由表现层自行判断
    /// 生命周期。<c>Bind</c> 只建立只读引用（Id），不持有逻辑对象指针/引用本体；<c>SyncPose</c>
    /// 由主循环在插值阶段调用，View 内部据此驱动渲染变换，不自行外推物理；<c>OnEvent</c> 是 View
    /// 接收战斗日志风格事件并驱动反馈的唯一入口。
    /// </summary>
    public interface IView
    {
        /// <summary>本 View 绑定的实体 id；<see cref="Bind"/> 调用前为默认值，具体实现自行决定
        /// 未绑定时的读取行为。</summary>
        Id EntityId { get; }

        /// <summary>是否仍存活（已 <see cref="Bind"/> 且尚未 <see cref="Destroy"/>）。</summary>
        bool IsAlive { get; }

        /// <summary>建立只读引用，不持有逻辑对象指针/引用本体（见类型注释）。</summary>
        void Bind(Id entityId);

        /// <summary>接收战斗日志风格事件并驱动反馈的唯一入口（见 09 第 2 节）。</summary>
        void OnEvent(IEvent evt);

        /// <summary>由主循环在插值阶段调用，驱动渲染变换；不自行外推物理（见 09 第 2 节）。</summary>
        void SyncPose(Vec2 pos, Direction facing, double height);

        /// <summary>销毁本 View 持有的表现资源。</summary>
        void Destroy();
    }
}
