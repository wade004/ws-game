using System;
using Core.Foundation.Common;

namespace Presentation.Render
{
    /// <summary>
    /// 移动呈现参数来源（手感设计/02 第 7 节，ADR-0147）：回答"该实体此刻移动剪辑应以多大速率播放、身体应前倾多少度"。
    /// 只读查询 + 变化通知；实现方（<see cref="LocomotionPresentation"/>，由 <see cref="PoseGaitFeeder"/> 喂入）纯呈现，不得反向影响判定。
    /// </summary>
    public interface ILocomotionPresentationSource
    {
        /// <summary>移动剪辑的播放速率（1 = 剪辑制作速度）；未登记的实体为 1。</summary>
        double GetStrideRate(Id entityId);

        /// <summary>身体前倾角（度，前倾为正、后仰为负）；未登记的实体为 0。</summary>
        double GetLeanDeg(Id entityId);

        /// <summary>某实体的播放速率或前倾角变化。</summary>
        event Action<Id>? Changed;
    }

    /// <summary>
    /// 视图工厂接收移动呈现参数来源（同 <see cref="IPoseContextReceiver"/> 的做法：装配根在任何视图创建之前至多调用一次）。
    /// 不实现本接口的工厂行为与此前逐位一致（剪辑恒按 1 倍速播放、没有身体倾斜）。
    /// </summary>
    public interface ILocomotionPresentationReceiver
    {
        void SetLocomotionPresentation(ILocomotionPresentationSource source);
    }

    /// <summary>
    /// 视图工厂接收起步/急停混合时长来源（ADR-0147，<see cref="LocomotionBlends"/>；同上，装配根在任何视图创建之前至多调用一次）。
    /// 只对 model 型骨骼剪辑有意义；不实现的工厂行为与此前逐位一致。
    /// </summary>
    public interface ILocomotionBlendsReceiver
    {
        void SetLocomotionBlends(ILocomotionBlendSource blends);
    }

    /// <summary>
    /// 视图工厂接收受击反应查询（ADR-0147）：装配根在世界装配了手感受击裁决时交付，工厂据此让动画状态机进入反应驱动模式
    /// （受击姿势由裁决事件驱动，见 <see cref="AnimStateMachine"/>）。不实现本接口、或装配根没有交付的工厂，受击仍由伤害落地驱动（此前行为）。
    /// </summary>
    public interface IHitReactionQueryReceiver
    {
        void SetHitReactionQuery(Core.Rules.Common.IHitReactionQuery reactions);
    }
}
