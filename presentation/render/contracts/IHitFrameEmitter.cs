using System;
using Core.Foundation.Common;

namespace Presentation.Render
{
    /// <summary>
    /// PJ130-04 根治新增：命中帧到达的可选能力接口——从 <see cref="ICharacterRig"/> 拆出（该接口此前
    /// 在 1.3.0 直接新增 <c>HitFrameReached</c> 为强制成员，属于 <c>architecture/11_工程规范与测试.md</c>
    /// 第 156 行"契约签名变化归 MAJOR"定义下的编译级破坏性变更——外部实现继续实现旧版
    /// <see cref="ICharacterRig"/> 升级到 1.3.0 会直接编译失败，与该版本号应有的语义不一致，见
    /// <c>architecture/adr/0017-模型型外形默认路线补齐与命中帧同步.md</c>"修订记录"一节）。
    /// <para>
    /// 恢复做法：命中帧到达（09 第 4.3 节 <c>anim_keyframe_driven</c> 策略）改由本可选接口承载，
    /// <see cref="ICharacterRig"/> 本身不再要求该成员——不支持命中帧同步的 <see cref="ICharacterRig"/>
    /// 实现（含全部继续实现 1.2.0 及更早版本接口形状的外部代码）不需要新增任何成员即可保持可编译。
    /// <see cref="Presentation.Render.SpriteCharacterRig"/>/<see cref="Presentation.Render.ModelCharacterRig"/>
    /// 两个框架自带实现仍然同时实现 <see cref="ICharacterRig"/> 与本接口，行为与 1.3.0 完全一致；消费方
    /// （如 <see cref="Presentation.FeedbackBinder.Core.CharacterRigHitFrameSource"/>）按
    /// <c>rig is IHitFrameEmitter emitter</c> 探测是否支持，探测不到时视为"该 rig 不参与命中帧同步"，
    /// 不抛异常（同 09 第 1 节表现层一贯宽容策略）。
    /// </para>
    /// </summary>
    public interface IHitFrameEmitter
    {
        /// <summary>
        /// 命中帧到达（09 第 4.3 节 <c>anim_keyframe_driven</c> 策略）。<c>sprite</c> 型经
        /// <see cref="IFrameAnimPlayer.OnAnimEvent"/> 命中 <see cref="FrameAnimClip.HitFrameMarker"/>
        /// 触发（见 <see cref="SpriteCharacterRig"/> 类型注释），<c>model</c> 型经
        /// <c>IRenderer3D.OnAnimEvent</c> 命中 <see cref="ModelCharacterRig.HitFrameEventId"/> 触发
        /// （见 <see cref="ModelCharacterRig"/> 类型注释）——二者统一为本事件，供
        /// <c>Presentation.FeedbackBinder.Contracts.IHitFrameSource</c> 一类按实体订阅的消费方不必
        /// 关心外形类型。<see cref="RenderOptions.HitFrameSync"/> 为 <see cref="HitFrameSyncStrategy.LogicDriven"/>
        /// （默认）或未接入序列帧/骨骼动画事件源时恒不触发。
        /// </summary>
        event Action<Id>? HitFrameReached;
    }
}
