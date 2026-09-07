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
    /// <para>
    /// PJ130-04 勘误（见 <c>architecture/adr/0017-模型型外形默认路线补齐与命中帧同步.md</c>"修订
    /// 记录"一节）：命中帧到达事件不再是本接口的强制成员——1.3.0 曾把 <c>HitFrameReached</c> 直接
    /// 加进本接口，属于契约签名的破坏性变更（<c>architecture/11_工程规范与测试.md</c> 第 156 行
    /// "契约签名变化归 MAJOR"），与仍标记为次版本号的发布不一致。命中帧改由可选接口
    /// <see cref="IHitFrameEmitter"/> 承载：<see cref="SpriteCharacterRig"/>/<see cref="ModelCharacterRig"/>
    /// 两个框架自带实现同时实现该接口，行为不变；消费方按 <c>rig is IHitFrameEmitter</c> 探测，未实现
    /// 该接口的 <see cref="ICharacterRig"/>（含继续实现旧接口形状的外部实现）视为"不参与命中帧同步"，
    /// 仍可正常编译与运行，不抛异常。
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
        /// 实现）。查不到该锚点/挂点时返回 null。
        /// <para>
        /// ADR-0017 决策 b 勘误：model 型外形调用本方法不再抛 <see cref="NotSupportedException"/>——
        /// <see cref="ModelCharacterRig"/> 按 <c>ModelInfo.Sockets</c> 判断该挂点是否声明存在，声明存在
        /// 时返回 <see cref="Vec2.Zero"/>（已知简化：model 型挂点的真实世界位置由具体
        /// <c>IRenderer3D</c> 实现的骨骼系统决定，本接口只能确认"该挂点是否声明"，不能给出精确偏移；
        /// 需要精确挂点世界坐标的调用方应改走该引擎适配层实现（若提供）自己的挂点查询能力），未声明时
        /// 返回 null，见 <see cref="ModelCharacterRig"/> 类型注释。
        /// </para>
        /// </summary>
        Vec2? ResolveAnchorLocalOffset(Id anchorId, Direction facing);

        /// <summary>按给定层名顺序与朝向合成纸娃娃层并应用（09 第 3.3.1 节"层内 z 序 = 列表顺序"）；
        /// <paramref name="resolveResourceId"/> 把每条已解析方向档位的层放置信息转换成具体引擎资源
        /// Id（见 <c>SpriteViewBase.ResolveLayerResourceId</c> 判断记录，调用方通常直接传入该虚方法
        /// 的委托，保留具体游戏重写资源命名规则的扩展点）。
        /// <para>
        /// ADR-0017 决策 b 勘误：model 型外形调用本方法不再抛 <see cref="NotSupportedException"/>，
        /// 改为 no-op（不做任何事）——纸娃娃层顺序合成不适用于 model 型，真正的装备外观改走
        /// <see cref="ModelCharacterRig.ApplyEquipVisual"/>（按 <c>display.equip_visual</c> 做
        /// <c>SetSlotMesh</c>/<c>AttachToSocket</c>），本方法留空只是保证既有"对全部 <see cref="ICharacterRig"/>
        /// 实现一视同仁调用"的调用方（如误把 model 型 rig 传进 sprite 专属装配代码）不会因此崩溃。
        /// </para>
        /// </summary>
        void ComposeAndApplyLayers(
            IReadOnlyList<string> layerNamesInOrder, Direction facing, Func<SpriteLayerPlacement, Id> resolveResourceId);

        /// <summary>直接应用一份已经解析好的层资源 Id 列表（供已有更复杂合成逻辑——如
        /// <c>SpriteViewBase.RebuildEquippedLayers</c> 的装备覆盖合并——的调用方复用本类型持有的
        /// <c>SpriteHandle</c>/<c>IRenderer2D</c> 完成最终 <c>SetLayers</c> 调用，不必自己另外持有
        /// 一份引用）。ADR-0017 决策 b 勘误：model 型外形调用本方法不再抛
        /// <see cref="NotSupportedException"/>，改为 no-op，理由同 <see cref="ComposeAndApplyLayers"/>。</summary>
        void ApplyLayers(IReadOnlyList<Id> resourceIds);

        /// <summary>推进 <see cref="ProceduralAnim"/> 的时间轴（见 <see cref="ProceduralAnimSequencer.Update"/>）。
        /// 由表现帧驱动代码（通常与 <c>ViewBinder.SyncAll</c> 同一帧节奏）每帧调用一次。</summary>
        void Update(double dt);
    }
}
