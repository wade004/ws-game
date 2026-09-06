using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.Common;

namespace Presentation.Render
{
    /// <summary>
    /// CharacterRig 契约（见 09_表现层.md 第 4.1 节"CharacterRig（原 SpriteRig）……按外形类型分派到
    /// sprite 或 model 两套实现"）：归并"把一个逻辑单位画成一个可动的角色"的四项职责——层/槽位管理、
    /// 锚点/挂点查询、动画状态机驱动、程序动画原语调用（见该节职责表）。<see cref="Presentation.Render.SpriteCharacterRig"/>
    /// 是 <c>sprite</c> 型实现，<see cref="Presentation.Render.ModelCharacterRig"/> 是 <c>model</c> 型
    /// 占位实现（见其类型注释）。
    /// <para>
    /// 判断记录（"动画状态机驱动"职责在本接口的落地形状）：<see cref="AnimStateMachine"/>（09 §4.2）
    /// 只回答"当前该处于哪个 <see cref="AnimState"/>"，不知道"这个状态该播哪个具体剪辑"——后者依赖
    /// 09 第 4.4 节武器表现档案（同一 <c>cast</c> 状态装备双手剑和法杖播放不同美术），是内容相关的
    /// 查表逻辑，不属于本接口应该固化的行为。本接口因此把"驱动"拆成两个独立方法：
    /// <see cref="SetAnimState"/>（纯记账，记录"当前视觉状态是什么"，供查询/诊断）与
    /// <see cref="PlayClip"/>（转发到已解析好的具体剪辑 id，解析工作留给调用方——通常是持有
    /// <c>WeaponStyleResolver</c> 的组装层代码，按 09 第 4.4 节查表得到 clipId 后再调用）。
    /// </para>
    /// </summary>
    public interface ICharacterRig
    {
        Id EntityId { get; }

        /// <summary>当前记录的动画状态（见 <see cref="SetAnimState"/> 判断记录），初始
        /// <see cref="AnimState.Idle"/>。</summary>
        AnimState CurrentAnimState { get; }

        /// <summary>程序动画原语入口（09 第 4.1 节"提供……动画能力，供反馈绑定与动画状态机调用"）。</summary>
        IProceduralAnim ProceduralAnim { get; }

        /// <summary>记录当前动画状态（不触发任何绘制/播放，见类型注释判断记录）；供
        /// <see cref="AnimStateMachine.StateChanged"/> 的消费方在决定好这一次要不要真的换一个具体
        /// 剪辑之后统一调用，保持 <see cref="CurrentAnimState"/> 与状态机同步。</summary>
        void SetAnimState(AnimState state);

        /// <summary>播放一个已解析好的具体动画剪辑（09 第 4.5 节 <c>FrameAnimPlayer.play</c>，
        /// <c>model</c> 型经 <c>IRenderer3D.playAnim</c>，见 09 第 4.1 节职责表）。未注入
        /// <see cref="IFrameAnimPlayer"/>（或 model 型未接 <c>IRenderer3D</c>）时静默跳过——本方法只是
        /// 转发通道，不是必须依赖，纯程序动画驱动（不使用序列帧资源）的游戏可以完全不调用/不注入。</summary>
        void PlayClip(Id clipId, bool loop = false, double speed = 1.0);

        /// <summary>查询锚点（sprite 型，09 第 3.3.1 节）/挂点（model 型，09 第 3.3.2 节）相对该角色
        /// 局部原点的偏移（未加上实体世界坐标——世界坐标合成是调用方的职责，见
        /// <c>Presentation.ViewBinding.ViewBinder.GetAnchorWorldPosition</c> 同一套镜像判定的独立
        /// 实现）。查不到该锚点/挂点时返回 null。model 型外形调用本方法抛
        /// <see cref="NotSupportedException"/>（挂点查询走 <c>IRenderer3D</c> 的具体实现，不归本接口，
        /// 见 <see cref="ModelCharacterRig"/> 类型注释）。</summary>
        Vec2? ResolveAnchorLocalOffset(Id anchorId, Direction facing);

        /// <summary>按给定层名顺序与朝向合成纸娃娃层并应用（09 第 3.3.1 节"层内 z 序 = 列表顺序"）；
        /// <paramref name="resolveResourceId"/> 把每条已解析方向档位的层放置信息转换成具体引擎资源
        /// Id（见 <c>SpriteViewBase.ResolveLayerResourceId</c> 判断记录，调用方通常直接传入该虚方法
        /// 的委托，保留具体游戏重写资源命名规则的扩展点）。model 型外形调用本方法抛
        /// <see cref="NotSupportedException"/>（层管理走槽位网格，见 <see cref="ModelCharacterRig"/>）。</summary>
        void ComposeAndApplyLayers(
            IReadOnlyList<string> layerNamesInOrder, Direction facing, Func<SpriteLayerPlacement, Id> resolveResourceId);

        /// <summary>直接应用一份已经解析好的层资源 Id 列表（供已有更复杂合成逻辑——如
        /// <c>SpriteViewBase.RebuildEquippedLayers</c> 的装备覆盖合并——的调用方复用本类型持有的
        /// <c>SpriteHandle</c>/<c>IRenderer2D</c> 完成最终 <c>SetLayers</c> 调用，不必自己另外持有
        /// 一份引用）。model 型外形调用本方法抛 <see cref="NotSupportedException"/>。</summary>
        void ApplyLayers(IReadOnlyList<Id> resourceIds);

        /// <summary>推进 <see cref="ProceduralAnim"/> 的时间轴（见 <see cref="ProceduralAnimSequencer.Update"/>）。
        /// 由表现帧驱动代码（通常与 <c>ViewBinder.SyncAll</c> 同一帧节奏）每帧调用一次。</summary>
        void Update(double dt);
    }
}
