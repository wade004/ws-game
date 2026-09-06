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
using System;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Presentation.Common;
using Presentation.Render;
using Adapter.Unity.EngineAdapter;

namespace Adapter.Unity.Presentation
{
    public sealed class UnitySpriteView : SpriteViewBase
    {
        /// <summary>闪白强度参数名，经 <c>IRenderer2D.SetShaderParam</c> 通道传递；
        /// <c>Adapter.Unity.EngineAdapter.UnityRenderer2D</c> 把它解释为"把纸娃娃层各
        /// SpriteRenderer.color 过曝到 (1+value) 倍"（value=0 时复原为正常颜色），见该类型
        /// SetShaderParam 方法判断记录。</summary>
        public const string FlashIntensityShaderParam = "flash_intensity";

        /// <summary>淡出透明度参数名（W3b 新增，拍板 6 八个程序动画原语可视化）；经
        /// <c>UnityRenderer2D.SetShaderParam</c> 落地为纸娃娃层各 SpriteRenderer.color.a。</summary>
        public const string FadeAlphaShaderParam = "fade_alpha";

        private Direction? _lastFacing;
        private bool _layersInitialized;

        // 八个程序动画原语里 Move/Rotate/Scale/Stagger/Topple 五个需要叠加到每帧的 Transform 上
        // （Flash/Fade 已经有独立通道——Flash 经 SpriteCharacterRig 直接接 flash_intensity，
        // Fade 经本类型新增的 fade_alpha，两者都不需要经过 Transform；Trail 走 TrailRenderer，
        // 也不需要 Transform 叠加），本类型因此只为这五个原语各自累积一份可叠加的偏移量，在
        // SyncPose 阶段统一合成一次 SetTransform 调用（见该方法判断记录）。Move 与 Stagger 语义
        // 不同（一次性位移 vs 短促回弹）但形状相同（Vec2 偏移量），各自独立存储，允许同时生效
        // （如受击瞬间同时叠加一次冲刺位移与一次受击回弹，互不覆盖）。
        private Vec2 _moveOffset = Vec2.Zero;
        private Vec2 _staggerOffset = Vec2.Zero;
        private double _rotationDelta;
        private double _scaleMultiplier = 1.0;

        public UnitySpriteView(
            IRenderer2D renderer,
            IRenderConventionHost conventions,
            Core.Foundation.DisplayInfo.DisplayInfo displayInfo,
            IResourceLoader resourceLoader,
            RenderOptions? options = null,
            IFrameAnimPlayer? frameAnimPlayer = null)
            : base(renderer, conventions, displayInfo, options, resourceLoader, frameAnimPlayer: frameAnimPlayer)
        {
        }

        /// <summary>
        /// 判断记录（W3b 判断记录 2 收口——曾经的"另开通路"绕过已撤销）：本类型此前自己持有一份
        /// <c>_frameAnimPlayer</c> 字段、自己实现 <c>AttachFrameAnimPlayer</c>/<c>PlayClip</c>，原因是
        /// <see cref="Presentation.Render.SpriteViewBase"/> 构造函数曾固定省略
        /// <c>frameAnimPlayer</c>/<c>RenderOptions</c> 两个参数去构造内部 <c>SpriteCharacterRig</c>，
        /// 导致 <c>Rig.PlayClip</c>/命中帧同步结构性 no-op。<see cref="SpriteViewBase"/> 现已支持
        /// 构造期注入（构造函数新增的 <c>frameAnimPlayer</c> 可选参数，见该类型构造函数判断记录）与
        /// 构造后补接线（<see cref="SpriteViewBase.AttachFrameAnimPlayer"/>），本类型改为
        /// 经这两条路径统一走 <c>Rig</c>：构造函数把可选的 <paramref name="frameAnimPlayer"/> 直接转交
        /// 基类；拿不到（组装代码需要先定位到 <see cref="EngineHandle"/> 对应的精灵根节点才能挂载
        /// <see cref="UnityFrameAnimPlayer"/> 组件，天然晚于构造）的调用方改调本类型继承自基类的
        /// <c>AttachFrameAnimPlayer</c> 方法补接线（未重写，直接沿用基类实现）。<see cref="PlayClip"/>
        /// 保留为本类型的便捷转发（改经 <c>Rig.PlayClip</c>），调用方无需改写既有调用形状。
        /// </summary>
        public void PlayClip(Id clipId, bool loop = false, double speed = 1.0) => Rig.PlayClip(clipId, loop, speed);

        /// <summary>本 View 挂接的引擎侧精灵句柄，供 <c>UnityViewFactory</c> 一类同属引擎适配层的
        /// 协作代码取用（如定位精灵根节点挂载 <see cref="UnityFrameAnimPlayer"/>/
        /// <see cref="UnityEngine.TrailRenderer"/>），不是 <see cref="Presentation.Common.IView"/>
        /// 契约的一部分。</summary>
        public SpriteHandle EngineHandle => Handle;

        public override void SyncPose(Vec2 pos, Direction facing, double height)
        {
            base.SyncPose(pos, facing, height);

            // 纸娃娃层未声明（如 gobj 箱子/门，见 data/_sample/README.md"判断记录"：占位美术不是
            // 按方向组织，没有可用的层数据）时跳过合成，保持无可见精灵层，如实记录不代为发明命名
            // 规则（见包 README"契约缺口"）。
            var layers = DisplayInfo.Sprite!.PaperdollLayers;
            if (layers.Count != 0)
            {
                if (!_layersInitialized || _lastFacing == null || !_lastFacing.Value.Equals(facing))
                {
                    // 每个新解析出的层资源 id 的加载请求已由基类 SetPaperdollLayers 经
                    // ResourceReferenceTracker 负责（见类型顶部注释），本类不再重复调用 LoadAsync。
                    SetPaperdollLayers(layers, facing);
                    _lastFacing = facing;
                    _layersInitialized = true;
                }
            }

            // W3b 新增（八个程序动画原语可视化，拍板 6）：Move/Stagger/Rotate/Scale 四类原语的
            // 当前采样值叠加到 base.SyncPose 已经算好的"逻辑位置/朝向"结果之上，重新调用一次
            // SetTransform——base.SyncPose 内部固定传 rotation=0、scale=DisplayInfo.Scale（见
            // SpriteViewBase 源码），无法从外部参数化，本类型只能在它调用完之后再补一次带原语偏移
            // 的调用，同一帧内两次 SetTransform 的后一次覆盖前一次，不产生可见的中间态闪烁（同一
            // 引擎帧内两次写同一份 Transform 数据，画面只在这一帧渲染时读取最终值一次）。sortY/
            // layer/flipX 的重算逻辑与 base.SyncPose 保持一致（同一份 Conventions/DisplayInfo）。
            if (_moveOffset != Vec2.Zero || _staggerOffset != Vec2.Zero || _rotationDelta != 0.0 || _scaleMultiplier != 1.0)
            {
                var sortY = Conventions.ComputeSortY(pos, DisplayInfo.SortOffset);
                var (_, flipX) = Conventions.ResolveDirectionSlot(facing, DisplayInfo.Sprite!);
                var heightPixels = Conventions.HeightOffsetToPixels(height, Options.PixelsPerUnit);
                var effectivePos = pos + _moveOffset + _staggerOffset;
                var effectiveScale = DisplayInfo.Scale * _scaleMultiplier;
                Renderer.SetTransform(Handle, effectivePos, heightPixels, sortY, RenderLayers.Units, _rotationDelta, effectiveScale, flipX);
            }
        }

        // ------------------------------------------------------------------
        // 程序动画原语 onSample 落地（W3b 新增，拍板 6）：供调用方触发
        // Rig.ProceduralAnim.Move/Rotate/Scale/Trail/Stagger/Topple/Fade 时把本类型对应方法作为
        // onSample 传入（Flash 已由 SpriteCharacterRig 内置接好，不需要本类型参与，见该类型注释）。
        // ------------------------------------------------------------------

        public void ApplyMoveSample(Vec2 offset) => _moveOffset = offset;

        public void ApplyStaggerSample(Vec2 offset) => _staggerOffset = offset;

        public void ApplyRotateSample(double radians) => _rotationDelta = radians;

        public void ApplyToppleSample(double radians) => _rotationDelta = radians;

        public void ApplyScaleSample(double multiplier) => _scaleMultiplier = multiplier;

        public void ApplyFadeSample(double alpha) => Renderer.SetShaderParam(Handle, FadeAlphaShaderParam, alpha);

        /// <summary>Trail 原语（09 第 4.1 节"拖尾（快速位移的视觉延迟）"）：落地为一个挂在精灵根
        /// 节点上的 <see cref="UnityEngine.TrailRenderer"/>（任务书"简单残影……或 TrailRenderer"，
        /// 二选一，本类型选后者——TrailRenderer 是 Unity 内建组件，不需要本模块自己管理对象池/
        /// 淡出定时器，复杂度更低）。<paramref name="root"/> 由调用方经
        /// <c>UnityRenderer2D.GetSpriteRoot(EngineHandle)</c> 取得并传入（本类型不持有
        /// <c>UnityRenderer2D</c> 具体类型引用，见构造函数只接口不接具体类型的既有惯例）。
        /// <see cref="ApplyTrailSample"/> 只按"当前是否仍在拖尾窗口内"置
        /// <see cref="UnityEngine.TrailRenderer.emitting"/>，不逐帧改其外观参数——09 原文只要求
        /// "视觉延迟"这一定性效果，不要求逐帧强度曲线。</summary>
        public void EnsureTrailRenderer(UnityEngine.GameObject root, double durationSeconds)
        {
            // 判断记录：不用 ?? 惰性获取/创建 UnityEngine.Object，见
            // Adapter.Unity.EngineAdapter.EffectSequencePlayer.Renderer 同名判断记录（同一处已知
            // Unity 陷阱，本处一并按同一套显式 if 写法修正）。
            var trail = root.GetComponent<UnityEngine.TrailRenderer>();
            if (trail == null)
            {
                trail = root.AddComponent<UnityEngine.TrailRenderer>();
            }
            trail.time = (float)Math.Max(durationSeconds, 0.01);
            trail.emitting = true;
        }

        public static void StopTrailRenderer(UnityEngine.GameObject root)
        {
            var trail = root.GetComponent<UnityEngine.TrailRenderer>();
            if (trail != null)
            {
                trail.emitting = false;
            }
        }

        // ------------------------------------------------------------------
        // 触发端便捷封装（W3b 新增）：把 Rig.ProceduralAnim.X(...) 与上面对应的 Apply*Sample 预先
        // 绑好，调用方（反馈绑定/测试）不需要每次都手写 onSample 委托。各方法是否在结束时把状态
        // 归零，严格对齐各 Params 类型注释里记录的曲线终态（见该类型注释）：Move/Rotate/Topple/Fade
        // 结束后停留在目标值（不回弹），Stagger/Scale 由 ProceduralAnimSequencer 自身的对称曲线在
        // 最后一次 onSample 回调时已经带回初始值（0/1），本类型不需要在 onComplete 里另行归零。
        // ------------------------------------------------------------------

        public void PlayMove(MoveParams parameters) => Rig.ProceduralAnim.Move(parameters, onSample: ApplyMoveSample);

        public void PlayStagger(StaggerParams parameters) => Rig.ProceduralAnim.Stagger(parameters, onSample: ApplyStaggerSample);

        public void PlayRotate(RotateParams parameters) => Rig.ProceduralAnim.Rotate(parameters, onSample: ApplyRotateSample);

        public void PlayTopple(ToppleParams parameters) => Rig.ProceduralAnim.Topple(parameters, onSample: ApplyToppleSample);

        public void PlayScale(ScaleParams parameters) => Rig.ProceduralAnim.Scale(parameters, onSample: ApplyScaleSample);

        public void PlayFade(FadeParams parameters) => Rig.ProceduralAnim.Fade(parameters, onSample: ApplyFadeSample);

        /// <summary>触发一次 Trail 原语并落地为 <see cref="EnsureTrailRenderer"/>；
        /// <paramref name="root"/> 见该方法参数说明。播完（<c>onComplete</c>）自动停止拖尾发射。</summary>
        public void PlayTrail(UnityEngine.GameObject root, TrailParams parameters) =>
            Rig.ProceduralAnim.Trail(parameters,
                onSample: _ => EnsureTrailRenderer(root, parameters.DurationSeconds),
                onComplete: () => StopTrailRenderer(root));

        /// <summary>触发一次过曝白色 hit-flash（见类型注释）；调用方（<c>FlashReceiver</c>）负责
        /// 在闪白持续时间结束后调用 <see cref="ClearFlash"/> 复原。</summary>
        public void SetFlash(double intensity) => Renderer.SetShaderParam(Handle, FlashIntensityShaderParam, intensity);

        public void ClearFlash() => Renderer.SetShaderParam(Handle, FlashIntensityShaderParam, 0.0);
    }
}
