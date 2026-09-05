#nullable enable
// UnitySpriteView：presentation/render/core/SpriteViewBase 的 Unity 侧具体 View。
//
// 职责边界：SpriteViewBase 本身已经把"创建精灵实例 / SyncPose 驱动 SetTransform+height_offset_px /
// Destroy 驱动 DestroySpriteInstance"三件事做完；本类补三件 SpriteViewBase 明确留给具体游戏/引擎
// 侧 View 子类决定的事：
//   1) 方向变化时重新合成纸娃娃层（SpriteViewBase.SyncPose 只更新位置/排序/镜像，不会在朝向变化
//      时自动重新调用 SetLayers——按当前方向重新解析层资源 id 是"具体游戏的 View 子类"的职责，
//      见 SpriteViewBase 类型注释判断记录）。
//   2) 按需触发资源加载（判断记录，见下）。
//   3) 反馈层"闪白"动作的落地（见 FlashReceiver.cs 顶部判断记录：09 第 6 节明确"闪白具体材质参数
//      怎么应用不属于 vfx_sfx/feedback_binder 契约范围"，本类按"过曝白色 tint" hit-flash 常见做法
//      实现，经 SpriteViewBase 已有的 SetShaderParam 命名参数通道传递，与 height_offset_px 同一套
//      机制，不发明新的 IRenderer2D 方法）。
//
// 判断记录（为什么本类要主动调用 IResourceLoader.LoadAsync）：勘察 Adapter.Unity.EngineAdapter.
// UnityRenderer2D.ResolveSprite（SetLayers 内部用来把资源 id 转成 Sprite 的私有方法）发现它只调用
// IResourceLoader.TryGetSprite 读缓存，从不主动发起 LoadAsync——这与 IResourceLoader 契约本身"谁来
// 触发首次加载"未作规定的设计是一致的（02 第 1.7 节只定义"如何查询/等待加载结果"，没有规定"渲染层
// 看到资源缺失时要不要自动帮你加载"）。若没有任何一方主动调用 LoadAsync，纸娃娃层会永远停在
// UnityRenderer2D 的洋红色占位方块上。本类型按"View 知道自己接下来要展示哪些方向/层，理应负责
// 预取这些资源"的原则，在朝向变化、需要重新合成纸娃娃层时顺带对本次朝向用到的每个层资源 id 发起
// 一次 LoadAsync（去重、幂等，已加载/已请求过的不重复发起）。
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Presentation.Common;
using Presentation.Render;

namespace Adapter.Unity.Presentation
{
    public sealed class UnitySpriteView : SpriteViewBase
    {
        /// <summary>闪白强度参数名，经 <see cref="SpriteViewBase.HeightOffsetShaderParam"/> 同一条
        /// <c>IRenderer2D.SetShaderParam</c> 通道传递；<c>Adapter.Unity.EngineAdapter.UnityRenderer2D</c>
        /// 把它解释为"把纸娃娃层各 SpriteRenderer.color 过曝到 (1+value) 倍"（value=0 时复原为
        /// 正常颜色），见该类型 SetShaderParam 方法判断记录。</summary>
        public const string FlashIntensityShaderParam = "flash_intensity";

        private readonly IResourceLoader _resourceLoader;
        private readonly HashSet<Id> _requestedLoads = new HashSet<Id>();

        private Direction? _lastFacing;
        private bool _layersInitialized;

        public UnitySpriteView(
            IRenderer2D renderer,
            IRenderConventionHost conventions,
            Core.Foundation.DisplayInfo.DisplayInfo displayInfo,
            IResourceLoader resourceLoader,
            RenderOptions? options = null)
            : base(renderer, conventions, displayInfo, options)
        {
            _resourceLoader = resourceLoader ?? throw new System.ArgumentNullException(nameof(resourceLoader));
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
                var placements = Conventions.ComposeSpriteLayers(layers, DisplayInfo.Sprite!, facing);
                RequestLoads(placements);
                SetPaperdollLayers(layers, facing);
                _lastFacing = facing;
                _layersInitialized = true;
            }
        }

        private void RequestLoads(IReadOnlyList<SpriteLayerPlacement> placements)
        {
            for (var i = 0; i < placements.Count; i++)
            {
                var id = ResolveLayerResourceId(placements[i]);
                if (_requestedLoads.Add(id) && !_resourceLoader.IsLoaded(id))
                {
                    _resourceLoader.LoadAsync(id, ResourceKind.Image, (_, __) => { });
                }
            }
        }

        /// <summary>触发一次过曝白色 hit-flash（见类型注释）；调用方（<c>FlashReceiver</c>）负责
        /// 在闪白持续时间结束后调用 <see cref="ClearFlash"/> 复原。</summary>
        public void SetFlash(double intensity) => Renderer.SetShaderParam(Handle, FlashIntensityShaderParam, intensity);

        public void ClearFlash() => Renderer.SetShaderParam(Handle, FlashIntensityShaderParam, 0.0);
    }
}
