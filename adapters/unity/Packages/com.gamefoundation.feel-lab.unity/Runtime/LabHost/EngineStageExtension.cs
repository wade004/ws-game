#nullable enable
// EngineStageExtension：实验室引擎舞台的公开扩展点（ADR-0160）。
//
// 判断记录（为什么有它）：舞台（EngineLabStage）原先内置了"演示场景"的一整套挂接——外形登记换成真实美术、场景道具与地面、命中特效、广告牌投影、
// 单位朝向换算。拆分后演示场景搬去样板仓库，只能建立在实验室的公开接口上；把这些挂接做成一个稳定的扩展类，舞台只在固定的几个点上调它，
// 样板（以及任何游戏自己的演示/试玩场景）继承它即可，不需要对实验室程序集开放内部可见性。所有挂接都只改引擎侧呈现，不得回流逻辑：
// 舞台在扩展点里读到的都是只读的逻辑事件与位姿。缺省实现全部是空操作，没有扩展时舞台与此前逐位一致。
// 调用次序（一次试玩运行内）：CreateDisplayRegistry（建视图工厂前）→ BuildScene（试玩模式、建完特效/音效后；返回 true 则舞台不建缺省地面网格）→
// 每个新视图的 OnDisplayBound → 每个固定步末尾对新逻辑事件调 OnLogicEvent → 精灵关键帧/模型命中帧到达时 OnSwing → 每个渲染帧 OnStep →
// 相机推进后每次摆广告牌之后 OnBillboardsApplied → 视图每次同步位姿经 FacingFor 换算朝向 → 舞台释放时 Dispose。
using System;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Lab;
using Presentation.Common;
using UnityEngine;

namespace FeelLab.Unity
{
    /// <summary>
    /// 固定俯角相机下的"呈现投影"：把精灵与特效摆成与相机平行的直立广告牌（地面与阴影仍躺在地上）。
    /// 舞台读它来摆每个单位的整身渲染根；实现由扩展提供（见 <see cref="EngineStageExtension.Projection"/>）。
    /// </summary>
    public interface IStageProjection
    {
        /// <summary>为真 = 直立广告牌模式（2.5D / 3D）；假 = 平面（2D 正交俯视），舞台不摆广告牌。</summary>
        bool Upright { get; }

        /// <summary>"向上"的世界方向（沿它抬高单位的渲染根）。</summary>
        Vector3 Up { get; }

        /// <summary>广告牌的朝向：相机姿态叠加屏幕平面内的旋转（度；取单位根当前的 Z 欧拉角）。</summary>
        Quaternion Facing(float screenDegrees);

        /// <summary>环绕镜头打开时是否按相机到物体的细粒度深度排序（舞台在环绕镜头开关时写入）。</summary>
        bool FineDepthSort { set; }
    }

    /// <summary>舞台交给扩展的场景装配上下文（只读的一组句柄，建场景用）。</summary>
    public sealed class StageSceneContext
    {
        internal StageSceneContext(
            Transform root, int isolationLayer, UnityResourceLoader loader, LabHostContext host, Camera? camera,
            bool cameraAppliesPitch, bool isModelForm, double? orbitFloorSize, bool orbitEnabled, Action<Id> flash)
        {
            Root = root;
            IsolationLayer = isolationLayer;
            Loader = loader;
            Host = host;
            Camera = camera;
            CameraAppliesPitch = cameraAppliesPitch;
            IsModelForm = isModelForm;
            OrbitFloorSize = orbitFloorSize;
            OrbitEnabled = orbitEnabled;
            Flash = flash;
        }

        /// <summary>舞台根（场景物体都挂在它下面，舞台释放时一并销毁）。</summary>
        public Transform Root { get; }

        /// <summary>舞台的专用渲染层；场景物体必须放在这一层（舞台相机只渲染这一层）。</summary>
        public int IsolationLayer { get; }

        /// <summary>舞台共用的资源加载器（建场景时加载地面、道具等美术用）。</summary>
        public UnityResourceLoader Loader { get; }

        /// <summary>内核宿主上下文（只读：世界、数据登记、格子、玩家 id）。</summary>
        public LabHostContext Host { get; }

        /// <summary>舞台相机。</summary>
        public Camera? Camera { get; }

        /// <summary>舞台相机是否带俯仰（格子相机模式 fixed_pitch 且选项按格子取用）。</summary>
        public bool CameraAppliesPitch { get; }

        /// <summary>格子是否是模型型外形（3D）。</summary>
        public bool IsModelForm { get; }

        /// <summary>环绕镜头的地面尺寸（世界单位）；舞台没有环绕镜头时为 null。</summary>
        public double? OrbitFloorSize { get; }

        /// <summary>环绕镜头当前是否打开。</summary>
        public bool OrbitEnabled { get; }

        /// <summary>某个单位的受击闪白（同样受试玩面板的"闪白"效果开关管：关 = 不闪）。</summary>
        public Action<Id> Flash { get; }
    }

    /// <summary>
    /// 舞台扩展基类（公开扩展点）：继承并重写需要的挂接，经 <see cref="EngineLabOptions.StageExtension"/> 交给舞台。
    /// 舞台在释放时调用 <see cref="Dispose"/>（扩展自己的物体放在 <see cref="StageSceneContext.Root"/> 下的会被舞台一并销毁，其余自己清理）。
    /// </summary>
    public abstract class EngineStageExtension : IDisposable
    {
        /// <summary>
        /// 外形登记：返回非 null 时舞台用它给视图工厂解析逻辑 id（例如把单位种类映射成真实美术外形）；返回 null 用舞台缺省的合成登记。
        /// <paramref name="core"/> 是数据集自己的外形登记，<paramref name="form"/> 是格子的外形（<c>sprite</c> / <c>model</c>）。
        /// </summary>
        public virtual IDisplayInfoRegistry? CreateDisplayRegistry(IDisplayInfoRegistry core, string form) => null;

        /// <summary>
        /// 武器风格行的 id 前缀：舞台取 <c>display.weapon_style.&lt;前缀&gt;(sprite|model)</c> 这一行（数据里没有就不指定）。缺省 <c>lab_</c>。
        /// </summary>
        public virtual string WeaponStylePrefix => "lab_";

        /// <summary>
        /// 建试玩场景（只在试玩模式调用一次）：返回 true = 扩展自己建了场景（地面、道具、特效），舞台不再建缺省地面网格；
        /// 返回 false = 舞台照旧建缺省地面。扩展若需要广告牌投影，在这里创建并赋给 <see cref="Projection"/>。
        /// </summary>
        public virtual bool BuildScene(StageSceneContext scene) => false;

        /// <summary>扩展提供的呈现投影；舞台在 <see cref="BuildScene"/> 之后读取一次（null = 不摆广告牌）。</summary>
        public IStageProjection? Projection { get; protected set; }

        /// <summary>每个新视图创建时调用：<paramref name="isPlayer"/> 为真表示这是玩家单位（扩展可据此给玩家换外形）。</summary>
        public virtual void OnDisplayBound(Id entityId, Id displayId, bool isPlayer)
        {
        }

        /// <summary>固定步末尾，对这一步新产生的每个逻辑事件调用一次（只读）。</summary>
        public virtual void OnLogicEvent(IEvent evt, int tick)
        {
        }

        /// <summary>精灵关键帧（命中帧/施放点）或模型命中帧到达：该单位的出手动画走到了出手点。</summary>
        public virtual void OnSwing(Id entity)
        {
        }

        /// <summary>每个渲染帧调用（<paramref name="dt"/> 是舞台推进的模拟秒数）。</summary>
        public virtual void OnStep(double dt)
        {
        }

        /// <summary>舞台刚摆完全部单位广告牌之后调用（扩展摆自己的道具广告牌）；平面模式（<see cref="IStageProjection.Upright"/> 为假）不调用。</summary>
        public virtual void OnBillboardsApplied()
        {
        }

        /// <summary>视图收到逻辑位姿时的朝向换算（引擎视图用返回值；逻辑记录视图仍收到原朝向）。缺省原样返回。</summary>
        public virtual Direction FacingFor(Id entity, Vec2 position, Direction logicalFacing) => logicalFacing;

        public virtual void Dispose()
        {
        }
    }
}
