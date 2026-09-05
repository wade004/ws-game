#nullable enable
// UnitySpriteView：presentation/render/core/SpriteViewBase 的 Unity 侧具体 View。
//
// 职责边界：SpriteViewBase 本身已经把"创建精灵实例 / SyncPose 驱动 SetTransform+height /
// Destroy 驱动 DestroySpriteInstance / 按需触发资源加载"四件事做完（资源加载见 ADR-0016 决策 6，
// 由 SpriteViewBase 可选注入的 IResourceLoader + Presentation.Common.ResourceReferenceTracker
// 承担——此前这半件事是本类型自己用 _requestedLoads/RequestLoads 手工实现的，ADR-0016 落地后
// 基类已原生支持，本类型的重复实现已删除，构造函数改为把 resourceLoader 转交给 base）；本类补
// 两件 SpriteViewBase 明确留给具体游戏/引擎侧 View 子类决定的事：
//   1) 方向变化时重新合成纸娃娃层（SpriteViewBase.SyncPose 只更新位置/排序/镜像，不会在朝向变化
//      时自动重新调用 SetLayers——按当前方向重新解析层资源 id 是"具体游戏的 View 子类"的职责，
//      见 SpriteViewBase 类型注释判断记录）。
//   2) 反馈层"闪白"动作的落地（见 FlashReceiver.cs 顶部判断记录：09 第 6 节明确"闪白具体材质参数
//      怎么应用不属于 vfx_sfx/feedback_binder 契约范围"，本类按"过曝白色 tint" hit-flash 常见做法
//      实现，经 SpriteViewBase 已有的 SetShaderParam 命名参数通道传递，不发明新的 IRenderer2D
//      方法）。
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Presentation.Common;
using Presentation.Render;

namespace Adapter.Unity.Presentation
{
    public sealed class UnitySpriteView : SpriteViewBase
    {
        /// <summary>闪白强度参数名，经 <c>IRenderer2D.SetShaderParam</c> 通道传递；
        /// <c>Adapter.Unity.EngineAdapter.UnityRenderer2D</c> 把它解释为"把纸娃娃层各
        /// SpriteRenderer.color 过曝到 (1+value) 倍"（value=0 时复原为正常颜色），见该类型
        /// SetShaderParam 方法判断记录。</summary>
        public const string FlashIntensityShaderParam = "flash_intensity";

        private Direction? _lastFacing;
        private bool _layersInitialized;

        public UnitySpriteView(
            IRenderer2D renderer,
            IRenderConventionHost conventions,
            Core.Foundation.DisplayInfo.DisplayInfo displayInfo,
            IResourceLoader resourceLoader,
            RenderOptions? options = null)
            : base(renderer, conventions, displayInfo, options, resourceLoader)
        {
        }

        public override void SyncPose(Vec2 pos, Direction facing, double height)
        {
            base.SyncPose(pos, facing, height);

            // 纸娃娃层未声明（如 gobj 箱子/门，见 data/_sample/README.md"判断记录"：占位美术不是
            // 按方向组织，没有可用的层数据）时跳过合成，保持无可见精灵层，如实记录不代为发明命名
            // 规则（见包 README"契约缺口"）。
            var layers = DisplayInfo.Sprite!.PaperdollLayers;
            if (layers.Count == 0)
            {
                return;
            }

            if (!_layersInitialized || _lastFacing == null || !_lastFacing.Value.Equals(facing))
            {
                // 每个新解析出的层资源 id 的加载请求已由基类 SetPaperdollLayers 经
                // ResourceReferenceTracker 负责（见类型顶部注释），本类不再重复调用 LoadAsync。
                SetPaperdollLayers(layers, facing);
                _lastFacing = facing;
                _layersInitialized = true;
            }
        }

        /// <summary>触发一次过曝白色 hit-flash（见类型注释）；调用方（<c>FlashReceiver</c>）负责
        /// 在闪白持续时间结束后调用 <see cref="ClearFlash"/> 复原。</summary>
        public void SetFlash(double intensity) => Renderer.SetShaderParam(Handle, FlashIntensityShaderParam, intensity);

        public void ClearFlash() => Renderer.SetShaderParam(Handle, FlashIntensityShaderParam, 0.0);
    }
}
