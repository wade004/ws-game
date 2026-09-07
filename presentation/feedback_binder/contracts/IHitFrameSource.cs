using System;
using Core.Foundation.Common;
using Presentation.Render;

namespace Presentation.FeedbackBinder.Contracts
{
    /// <summary>
    /// 按实体注册/注销 rig 的命中帧事件（ADR-0017 决策 d）：把
    /// <c>Presentation.Render.ICharacterRig.HitFrameReached</c>（每个 rig 实例各自的 C# 事件）汇聚成
    /// 一个按实体 id 广播的统一入口，供 <see cref="Presentation.FeedbackBinder.Core.HitFrameSyncPolicy"/>
    /// 订阅——本模块（<c>feedback_binder</c>）不直接持有任何 rig 引用（rig 的生命周期归 View/
    /// ViewFactory 一侧管理，见 <c>Presentation.Render.SpriteViewBase</c>/<c>ModelCharacterRig</c>
    /// 类型注释），由持有 rig 的一侧（通常是 <c>Presentation.ViewBinding.ViewBinder</c> 或具体游戏的
    /// View 装配代码）在 View 绑定/解绑时调用 <see cref="RegisterRig"/>/<see cref="UnregisterRig"/>。
    /// </summary>
    public interface IHitFrameSource
    {
        /// <summary>登记 <paramref name="entityId"/> 当前绑定的 <see cref="Presentation.Render.ICharacterRig"/>，
        /// 订阅其 <c>HitFrameReached</c>；同一实体重复登记时替换旧登记（先注销旧 rig 的订阅）。</summary>
        void RegisterRig(Id entityId, ICharacterRig rig);

        /// <summary>注销 <paramref name="entityId"/> 的登记（View 销毁/解绑时调用，避免悬挂订阅）；
        /// 未登记过时 no-op。</summary>
        void UnregisterRig(Id entityId);

        /// <summary>该实体当前是否已登记一个 rig（供 <see cref="Presentation.FeedbackBinder.Core.HitFrameSyncPolicy"/>
        /// 判断"攻击方无 rig 时立即播放"这一分支，见该类型判断记录）。</summary>
        bool HasRig(Id entityId);

        /// <summary>某个已登记实体的 rig 到达命中帧时触发，携带该实体 id。</summary>
        event Action<Id>? HitFrameReached;
    }
}
