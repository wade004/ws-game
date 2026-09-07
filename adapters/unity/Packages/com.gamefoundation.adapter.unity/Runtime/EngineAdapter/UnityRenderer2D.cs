#nullable enable
// UnityRenderer2D：IRenderer2D 的 Unity 引擎实现。
//
// 句柄 -> GameObject：CreateSpriteInstance 创建一个根 GameObject（挂
// UnityEngine.Rendering.SortingGroup，让其下全部纸娃娃层子 SpriteRenderer 作为一个整体参与
// 跨实体排序），根下再挂一个 "LayersRoot" 子物体承载 SetLayers 传入的纸娃娃层
// （每层一个子 SpriteRenderer，兄弟顺序 = 列表顺序 = 09 第 3.3.1 节"层内 z 序 = 列表顺序"）。
//
// 排序判断记录：sortY（同层内前后遮挡）与 layer（离散图层）一起换算成单个整数
// SpriteRenderer.sortingOrder = layer * LayerStride - round(sortY * SortYScale) + 纸娃娃层序号，
// 显式、确定性、可被测试直接断言；ProjectSetup.cs 额外把 URP 2D Renderer 的 Transparency Sort
// Axis 设成世界 Y 轴，这是同一 sortingOrder 内的次级并列时的引擎自带兜底排序（两者不冲突，
// 后者只在前者相等时才生效）。
//
// 高度偏移（ADR-0016 决策 2 已解决，取代此前借用 SetShaderParam("height_offset_px") 的工作绕）：
// SetTransform 新增的 height 参数按"纵向像素偏移"解释——只平移 LayersRoot 子物体的本地 Y 坐标
// （像素值 / PixelsPerUnit 换算成世界单位），不改变根物体的位置/sortY/sortingOrder、不平移影子
// （09 第 3.4 节）。SetShaderParam 现在只用于与位置无关的材质参数（闪白/溶解等）。
//
// U04 根治（第五轮外部审核 audit-5e779c6-20260907/AUDIT_REPORT.md）：默认序列帧动画
// （Adapter.Unity.Presentation.UnityFrameAnimPlayer，见 UnityViewFactory.AttachDefaultAnimation）
// 此前直接挂在 GetSpriteRoot 返回的根物体自身上，落地的 SpriteRenderer 既不是 LayersRoot 的子物体
// （不受 height 偏移平移）、也不在 LayerRenderers 集合里（ApplyColor 遍历不到，flash/fade 不生效）。
// 现改为：该 SpriteRenderer 改挂在 LayersRoot 下新建的子物体（GetLayersRoot 供外部定位挂载点），
// 天然随 LayersRoot 一起平移吃到 height 偏移；同时经 RegisterAnimRootRenderer 登记到
// SpriteInstance.AnimRootRenderer（与 LayerRenderers 分开维护的单一引用，SetLayers 的增删/复用逻辑
// 不感知它，不会被纸娃娃层数量变化误销毁/误复用），ApplyColor/SetTransform 的 flipX 遍历/
// ApplySortingOrders 均额外处理这一个引用——影子（ShadowRenderer）仍按原判断记录独立处理，不参与
// height/颜色遍历。
//
// 资源解析与占位：CreateSpriteInstance/SetLayers 用到的资源 id 一律经 UnityResourceLoader
// 解析；解析不到（未加载或加载失败）时使用一个运行期生成的纯色方块精灵占位，并
// Debug.LogWarning 一次（按 id 去重，避免刷屏），不抛异常——保证游戏在资源缺失时仍可运行，
// 只是画面上看到占位方块。
//
// 粒子（ADR-0016 决策 5 已解决）：EmitParticle 优先经 UnityResourceLoader.TryGetEffect(effectId)
// 解析出具体特效资产（ResourceKind.Effect 加载完成后的序列帧/粒子预制体，见 UnityResourceLoader
// 判断记录），解析不到时回退播放一个内建的通用爆发粒子效果（不抛异常、不阻断游戏运行）；
// parameters 里的 "duration"/"start_lifetime"/"start_speed"/"start_size" 四个已知键会覆盖对应
// 模块参数（仅回退路径生效，具体特效资产自带参数不经这四个键覆盖），其余键被忽略。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using UnityEngine;
using UnityEngine.Rendering;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class UnityRenderer2D : IRenderer2D
    {
        // 判断记录（数值范围）：Unity 的 SpriteRenderer/SortingGroup.sortingOrder 虽然公开签名是
        // int，但实测其可用取值范围被限制在 short 区间（-32768..32767，超出会按 16 位回绕，
        // PlayMode 测试 UnityRenderer2DTests.SetTransform_SetsPositionAndComputesSortingOrder…
        // 曾经因为用了过大的 LayerStride/SortYScale 而实测触发过这个回绕）；LayerStride=1000、
        // SortYScale=1 支持 layer 取值大致在 -32..32 之间（每个离散图层留出 ±500 的 sortY
        // 波动余量），已足够覆盖 09 表现层常见的个位数图层数量，超出范围时会跨层"溢出"到相邻
        // 图层的排序区间——这是已知的数值范围限制，游戏层应避免让 layer 或 sortY 超出上述量级。
        private static class SortingConvention
        {
            public const int LayerStride = 1000;
            public const double SortYScale = 1.0;
        }

        private sealed class SpriteInstance
        {
            public GameObject Root = null!;
            public SortingGroup SortingGroup = null!;
            public Transform LayersRoot = null!;
            public List<SpriteRenderer> LayerRenderers = new List<SpriteRenderer>();
            public int Layer;
            public double SortY;
            public bool FlipX;
            public MaterialPropertyBlock PropertyBlock = new MaterialPropertyBlock();

            // W3b 新增（八个程序动画原语可视化，拍板 6）：flash_intensity 与 fade_alpha 两个命名
            // 参数都经 SpriteRenderer.color 落地（前者乘法调色的 RGB 分量、后者是 Alpha 分量），
            // 各自独立存储、合成时才组合成一个 Color 写回，避免其中一个 SetShaderParam 调用覆盖
            // 掉另一个已经生效的分量（见 ApplyColor 判断记录）。
            public float FlashMultiplier = 1f;
            public float FadeAlpha = 1f;

            /// <summary>GP-PRES-05 新增（09 第 3.4 节"每个可见单位默认携带一个地面投影影子"）：
            /// 懒创建，<see cref="ShadowMode.None"/> 时为 null（未曾创建，或已被销毁）。直接挂在
            /// <see cref="Root"/> 下（不是 <see cref="LayersRoot"/>），因此不随 <c>height</c> 偏移
            /// 平移——height 只平移 LayersRoot 的本地 Y，见 <see cref="SetTransform"/> 判断记录
            /// "不平移影子"。</summary>
            public SpriteRenderer? ShadowRenderer;

            public ShadowMode Shadow = ShadowMode.None;

            /// <summary>U04 根治新增：默认序列帧动画（<see cref="Adapter.Unity.Presentation.UnityFrameAnimPlayer"/>）
            /// 落地的 Root 级 <see cref="SpriteRenderer"/>，经 <see cref="RegisterAnimRootRenderer"/> 登记。
            /// 与 <see cref="LayerRenderers"/> 分开维护——<see cref="SetLayers"/> 的增删/复用逻辑按
            /// <c>layers.Count</c> 索引对齐，不感知这个引用，避免纸娃娃层数量变化时把它误销毁或误当作
            /// 某个纸娃娃层复用；但仍与 <see cref="LayerRenderers"/> 同样参与 height 偏移（作为
            /// <see cref="LayersRoot"/> 的子物体，天然随其平移，不需要额外代码）、颜色（<see cref="ApplyColor"/>）
            /// 与 flipX（<see cref="SetTransform"/>）遍历。null 表示该精灵实例未挂默认动画（如
            /// item/gobj/projectile 一类不挂，见 <c>UnityViewFactory.AttachDefaultAnimation</c> 调用点
            /// 判断记录）。</summary>
            public SpriteRenderer? AnimRootRenderer;
        }

        private readonly Transform _root;
        private readonly UnityResourceLoader _resourceLoader;
        private readonly Dictionary<int, SpriteInstance> _sprites = new Dictionary<int, SpriteInstance>();
        private readonly Dictionary<int, ParticleSystem> _particles = new Dictionary<int, ParticleSystem>();
        private readonly List<ParticleSystem> _particlePool = new List<ParticleSystem>();
        private readonly Dictionary<int, EffectSequencePlayer> _sequencePlayers = new Dictionary<int, EffectSequencePlayer>();
        private readonly List<EffectSequencePlayer> _sequencePlayerPool = new List<EffectSequencePlayer>();
        private readonly HashSet<string> _missingResourceWarned = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<int> _projectedShadowWarned = new HashSet<int>();
        private int _nextSpriteHandle = 1;
        private int _nextParticleHandle = 1;
        private Sprite? _placeholderSprite;
        private Sprite? _shadowSprite;

        public float PixelsPerUnit => _resourceLoader.PixelsPerUnit;

        public UnityRenderer2D(Transform root, UnityResourceLoader resourceLoader)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
            _resourceLoader = resourceLoader ?? throw new ArgumentNullException(nameof(resourceLoader));
        }

        public SpriteHandle CreateSpriteInstance(Id spriteSetId)
        {
            var handle = _nextSpriteHandle++;
            var root = new GameObject($"Sprite_{handle}_{spriteSetId.Value}");
            root.transform.SetParent(_root, worldPositionStays: false);

            var sortingGroup = root.AddComponent<SortingGroup>();

            var layersRoot = new GameObject("LayersRoot").transform;
            layersRoot.SetParent(root.transform, worldPositionStays: false);

            var instance = new SpriteInstance
            {
                Root = root,
                SortingGroup = sortingGroup,
                LayersRoot = layersRoot
            };
            _sprites[handle] = instance;

            return new SpriteHandle(handle);
        }

        public void SetLayers(SpriteHandle handle, IReadOnlyList<Id> layers)
        {
            var instance = EnsureAlive(handle);

            while (instance.LayerRenderers.Count > layers.Count)
            {
                var last = instance.LayerRenderers[instance.LayerRenderers.Count - 1];
                instance.LayerRenderers.RemoveAt(instance.LayerRenderers.Count - 1);
                UnityEngine.Object.Destroy(last.gameObject);
            }

            for (var i = 0; i < layers.Count; i++)
            {
                SpriteRenderer renderer;
                if (i < instance.LayerRenderers.Count)
                {
                    renderer = instance.LayerRenderers[i];
                }
                else
                {
                    var child = new GameObject($"Layer_{i}");
                    child.transform.SetParent(instance.LayersRoot, worldPositionStays: false);
                    renderer = child.AddComponent<SpriteRenderer>();
                    instance.LayerRenderers.Add(renderer);
                }

                renderer.sprite = ResolveSprite(layers[i]);
                renderer.flipX = instance.FlipX;
                renderer.color = ComputeColor(instance);
            }

            ApplySortingOrders(instance);
        }

        public void SetTransform(SpriteHandle handle, Vec2 position, double height, double sortY, int layer, double rotation, double scale, bool flipX)
        {
            var instance = EnsureAlive(handle);

            instance.Root.transform.localPosition = new Vector3((float)position.X, (float)position.Y, 0f);
            instance.Root.transform.localRotation = Quaternion.Euler(0f, 0f, (float)(rotation * Mathf.Rad2Deg));
            instance.Root.transform.localScale = new Vector3((float)scale, (float)scale, 1f);

            // height：纵向绘制偏移，只平移 LayersRoot（不平移影子、不参与 sortY 排序，见
            // ADR-0016 决策 2、09 第 3.4 节）；像素值经 PixelsPerUnit 换算成世界单位。
            var worldHeightOffset = (float)(height / Math.Max(PixelsPerUnit, 0.0001));
            var layersLocal = instance.LayersRoot.localPosition;
            instance.LayersRoot.localPosition = new Vector3(layersLocal.x, worldHeightOffset, layersLocal.z);

            instance.Layer = layer;
            instance.SortY = sortY;
            instance.FlipX = flipX;

            foreach (var renderer in instance.LayerRenderers)
            {
                renderer.flipX = flipX;
            }

            if (instance.AnimRootRenderer != null)
            {
                instance.AnimRootRenderer.flipX = flipX;
            }

            ApplySortingOrders(instance);
        }

        /// <summary>
        /// GP-PRES-05 收口（09 第 3.4 节）：<see cref="ShadowMode.None"/> 销毁/不创建影子；
        /// <see cref="ShadowMode.Blob"/> 懒创建一个贴地椭圆占位精灵（半透明深色，正式游戏应替换为
        /// 真实美术资源——本适配层不接触具体游戏内容，见类型顶部"资源解析与占位"判断记录同一惯例）；
        /// <see cref="ShadowMode.Projected"/> 在纯 2D 渲染管线下没有真正的投影阴影几何（那属于
        /// <see cref="IRenderer3D.SetShadow"/> 的 model 路线），降级为 <see cref="ShadowMode.Blob"/>
        /// 并 <see cref="Debug.LogWarning"/> 一次（按句柄去重，不刷屏）——不是静默吞掉这个差异。
        /// 影子挂在 <see cref="SpriteInstance.Root"/> 下（不是 <see cref="SpriteInstance.LayersRoot"/>），
        /// 因此天然不随 <c>height</c> 偏移平移，锚定在逻辑平面坐标（见 <see cref="SetTransform"/>
        /// 判断记录）；<c>sortingOrder</c> 固定 -1，低于全部纸娃娃层（层序号从 0 起，见
        /// <see cref="ApplySortingOrders"/>），保证影子恒在角色本体之下。
        /// </summary>
        public void SetShadow(SpriteHandle handle, ShadowMode mode)
        {
            var instance = EnsureAlive(handle);

            if (mode == ShadowMode.Projected)
            {
                if (_projectedShadowWarned.Add(handle.Value))
                {
                    Debug.LogWarning(
                        $"[UnityRenderer2D] SpriteHandle {handle.Value}：sprite 路线不支持真正的投影阴影几何" +
                        "（那是 IRenderer3D.SetShadow 的 model 路线能力），降级为 Blob。");
                }

                mode = ShadowMode.Blob;
            }

            instance.Shadow = mode;

            if (mode == ShadowMode.None)
            {
                if (instance.ShadowRenderer != null)
                {
                    UnityEngine.Object.Destroy(instance.ShadowRenderer.gameObject);
                    instance.ShadowRenderer = null;
                }
                return;
            }

            // mode == Blob（含 Projected 降级而来）。
            if (instance.ShadowRenderer == null)
            {
                var shadowGo = new GameObject("Shadow");
                shadowGo.transform.SetParent(instance.Root.transform, worldPositionStays: false);
                shadowGo.transform.localPosition = Vector3.zero;
                var renderer = shadowGo.AddComponent<SpriteRenderer>();
                renderer.sprite = GetShadowSprite();
                renderer.sortingOrder = -1;
                instance.ShadowRenderer = renderer;
            }
        }

        public void SetShaderParam(SpriteHandle handle, string paramName, double value)
        {
            var instance = EnsureAlive(handle);

            // 判断记录（U2-3 反馈接收器"闪白"落地，见 Runtime/Presentation/UnitySpriteView.cs、
            // FlashReceiver.cs 顶部注释）：09 第 6 节明确"闪白具体材质参数怎么应用不属于
            // vfx_sfx/feedback_binder 契约范围"；本适配层按与 height_offset_px 完全同一套"命名
            // 参数经既有 SetShaderParam 通道传递、Unity 侧解释具体语义"的机制处理，不新增
            // IRenderer2D 契约方法。取 Adapter.Unity.Presentation.UnitySpriteView.FlashIntensityShaderParam
            // 同一个参数名字符串（"flash_intensity"），值域 [0, +∞)：0 表示复原为正常颜色，
            // 大于 0 时把纸娃娃层各 SpriteRenderer.color 过曝到 (1+value) 倍（常见的"受击闪白" hit
            // flash 手法——SpriteRenderer.color 是乘法调色，任何 Sprite 兼容 Shader 都保证支持，
            // 不要求占位/正式美术资源额外提供专用的"闪白"着色器属性）。
            if (string.Equals(paramName, "flash_intensity", StringComparison.Ordinal))
            {
                instance.FlashMultiplier = 1f + Math.Max(0f, (float)value);
                ApplyColor(instance);
                return;
            }

            // W3b 新增（八个程序动画原语可视化，拍板 6）："fade" 原语（09 第 4.1 节"淡出（消失、
            // 隐身切换）"）落地为一个新命名参数 "fade_alpha"，与 flash_intensity 同一套"经既有
            // SetShaderParam 通道传递、Unity 侧解释具体语义"机制（同 SetShaderParam 类型顶部判断
            // 记录），不新增 IRenderer2D 契约方法。值域 [0, 1]：1 表示完全不透明（默认，同精灵初始
            // 状态），0 表示完全透明。<see cref="Presentation.Render.SpriteCharacterRig"/> 的
            // Fade 原语转发经 <c>Rig.ProceduralAnim.Fade(...)</c> 触发时，其 onSample 委托（见
            // Runtime/Presentation/UnitySpriteView.cs）直接调用本参数名落地，不需要新的原语专属
            // 契约通道。
            if (string.Equals(paramName, "fade_alpha", StringComparison.Ordinal))
            {
                instance.FadeAlpha = Mathf.Clamp01((float)value);
                ApplyColor(instance);
                return;
            }

            foreach (var renderer in instance.LayerRenderers)
            {
                instance.PropertyBlock.SetFloat(paramName, (float)value);
                renderer.SetPropertyBlock(instance.PropertyBlock);
            }
        }

        /// <summary>合成 <see cref="SpriteInstance.FlashMultiplier"/>（RGB 乘法调色）与
        /// <see cref="SpriteInstance.FadeAlpha"/>（Alpha）为一个 <see cref="Color"/>，供
        /// <see cref="SetShaderParam"/> 两个分支与 <see cref="SetLayers"/>（新建/复用层渲染器时
        /// 重新套用当前已生效的闪白/淡出状态，避免方向切换重建层后短暂丢失表现状态）共用。</summary>
        private static Color ComputeColor(SpriteInstance instance) =>
            new Color(instance.FlashMultiplier, instance.FlashMultiplier, instance.FlashMultiplier, instance.FadeAlpha);

        private static void ApplyColor(SpriteInstance instance)
        {
            var color = ComputeColor(instance);
            foreach (var renderer in instance.LayerRenderers)
            {
                renderer.color = color;
            }

            if (instance.AnimRootRenderer != null)
            {
                instance.AnimRootRenderer.color = color;
            }
        }

        /// <summary>W3b 新增（八个程序动画原语可视化，拍板 6）：供
        /// <c>Adapter.Unity.Presentation.UnitySpriteView</c> 取回精灵实例的根 <see cref="GameObject"/>，
        /// 落地 Trail 原语（挂/摘 <see cref="UnityEngine.TrailRenderer"/>，见该类型判断记录）——
        /// <see cref="IRenderer2D"/> 契约本身不暴露引擎侧 GameObject（P4"具体绘制细节留给适配层"），
        /// 但 <c>UnitySpriteView</c> 与本类型同属 <c>Adapter.Unity.*</c> 命名空间、同属引擎适配层，
        /// 直接持有 <c>UnityRenderer2D</c> 具体类型（而不是 <see cref="IRenderer2D"/> 接口）向下转型
        /// 取用引擎细节，不违反表现层对逻辑层的只读铁律（P1~P4 约束的是"表现层不得绕过契约操作逻辑
        /// 层"，不禁止引擎适配层内部互相知道对方的具体类型）。查不到（已销毁/未知句柄）时返回
        /// null，调用方按"跳过本次原语的引擎侧落地，不崩溃"处理。</summary>
        public GameObject? GetSpriteRoot(SpriteHandle handle) =>
            _sprites.TryGetValue(handle.Value, out var instance) ? instance.Root : null;

        /// <summary>U04 根治新增：返回精灵实例的 <c>LayersRoot</c> 子物体——供
        /// <c>UnityViewFactory.AttachDefaultAnimation</c> 把默认序列帧动画的 <see cref="GameObject"/>
        /// 挂在这里（而不是 <see cref="GetSpriteRoot"/> 返回的根物体），使其随 height 偏移一起平移
        /// （见 <see cref="SetTransform"/> 判断记录）。查不到（已销毁/未知句柄）时返回 null，调用方
        /// 按"跳过本次接线，不崩溃"处理，同 <see cref="GetSpriteRoot"/> 一贯惯例。</summary>
        public Transform? GetLayersRoot(SpriteHandle handle) =>
            _sprites.TryGetValue(handle.Value, out var instance) ? instance.LayersRoot : null;

        /// <summary>U04 根治新增：登记一个不受 <see cref="SetLayers"/> 增删/复用逻辑管理的额外
        /// <see cref="SpriteRenderer"/>（目前唯一调用方是 <c>UnityViewFactory.AttachDefaultAnimation</c>，
        /// 登记默认序列帧动画落地的 Root 级渲染器），使其此后与 <see cref="LayerRenderers"/> 一样参与
        /// <see cref="ApplyColor"/>（flash/fade）与 <see cref="SetTransform"/> 的 flipX 遍历、
        /// <see cref="ApplySortingOrders"/> 排序——height 偏移不需要这里额外处理，只要
        /// <paramref name="renderer"/> 挂在 <see cref="GetLayersRoot"/> 返回的子树下就随 Unity 变换
        /// 层级天然继承。登记时立即套用当前已生效的 flipX/颜色/排序，不必等下一次 SetTransform/
        /// SetShaderParam 才补上。一个精灵实例至多一个（重复登记直接覆盖旧引用，调用方不应重复调用）。
        /// 句柄不存在（已销毁/未知）时静默跳过，不抛异常，同本类型一贯的防御性惯例。</summary>
        public void RegisterAnimRootRenderer(SpriteHandle handle, SpriteRenderer renderer)
        {
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));

            if (!_sprites.TryGetValue(handle.Value, out var instance))
            {
                return;
            }

            instance.AnimRootRenderer = renderer;
            renderer.flipX = instance.FlipX;
            renderer.color = ComputeColor(instance);
            ApplySortingOrders(instance);
        }

        public void DestroySpriteInstance(SpriteHandle handle)
        {
            var instance = EnsureAlive(handle);
            UnityEngine.Object.Destroy(instance.Root);
            _sprites.Remove(handle.Value);
        }

        public ParticleHandle EmitParticle(Id effectId, Vec2 position, IReadOnlyDictionary<string, double> parameters)
        {
            var handle = _nextParticleHandle++;

            // ADR-0016 决策 5：effectId 优先经 UnityResourceLoader 解析到具体特效资产（序列帧），
            // 找不到（未加载/加载失败/资源种类不是 Effect）时回退播放内建通用粒子效果，见类型
            // 顶部注释。
            if (_resourceLoader.TryGetEffect(effectId, out var effect))
            {
                var player = RentSequencePlayer();
                player.transform.position = new Vector3((float)position.X, (float)position.Y, 0f);

                var frames = new Sprite[effect.Frames.Length];
                var durations = new double[effect.Frames.Length];
                for (var i = 0; i < effect.Frames.Length; i++)
                {
                    frames[i] = effect.Frames[i].Sprite;
                    durations[i] = effect.Frames[i].Duration;
                }

                player.gameObject.SetActive(true);
                player.Play(frames, durations, effect.Loop);
                _sequencePlayers[handle] = player;

                return new ParticleHandle(handle);
            }

            var ps = RentParticleSystem();
            ps.transform.position = new Vector3((float)position.X, (float)position.Y, 0f);

            var main = ps.main;
            if (parameters.TryGetValue("duration", out var duration))
            {
                main.duration = (float)duration;
            }
            if (parameters.TryGetValue("start_lifetime", out var lifetime))
            {
                main.startLifetime = (float)lifetime;
            }
            if (parameters.TryGetValue("start_speed", out var speed))
            {
                main.startSpeed = (float)speed;
            }
            if (parameters.TryGetValue("start_size", out var size))
            {
                main.startSize = (float)size;
            }

            ps.gameObject.SetActive(true);
            ps.Play(withChildren: true);
            _particles[handle] = ps;

            return new ParticleHandle(handle);
        }

        public void StopParticle(ParticleHandle handle)
        {
            if (_sequencePlayers.TryGetValue(handle.Value, out var player))
            {
                player.StopImmediately();
                player.gameObject.SetActive(false);
                _sequencePlayers.Remove(handle.Value);
                _sequencePlayerPool.Add(player);
                return;
            }

            if (!_particles.TryGetValue(handle.Value, out var ps))
            {
                throw new InvalidOperationException($"粒子句柄 {handle.Value} 已销毁或不存在");
            }

            ps.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.gameObject.SetActive(false);
            _particles.Remove(handle.Value);
            _particlePool.Add(ps);
        }

        private void ApplySortingOrders(SpriteInstance instance)
        {
            var baseOrder = instance.Layer * SortingConvention.LayerStride
                - (int)Math.Round(instance.SortY * SortingConvention.SortYScale, MidpointRounding.AwayFromZero);
            instance.SortingGroup.sortingOrder = baseOrder;

            for (var i = 0; i < instance.LayerRenderers.Count; i++)
            {
                instance.LayerRenderers[i].sortingOrder = i;
            }

            // AnimRootRenderer 排在全部纸娃娃层之上（同一 SortingGroup 内，序号取
            // LayerRenderers.Count——纸娃娃层为空时即为 0，恒不与影子的 -1 冲突）：默认序列帧动画是
            // "没有具体游戏参与也要有得看"的兜底表现，不应被纸娃娃层（若同时存在装备覆盖层）盖住。
            if (instance.AnimRootRenderer != null)
            {
                instance.AnimRootRenderer.sortingOrder = instance.LayerRenderers.Count;
            }
        }

        private Sprite ResolveSprite(Id resourceId)
        {
            if (_resourceLoader.TryGetSprite(resourceId, out var sprite))
            {
                return sprite;
            }

            if (_missingResourceWarned.Add(resourceId.Value))
            {
                Debug.LogWarning($"[UnityRenderer2D] 精灵资源未加载或不存在，使用占位方块：{resourceId}");
            }

            return GetPlaceholderSprite();
        }

        private Sprite GetPlaceholderSprite()
        {
            if (_placeholderSprite != null) return _placeholderSprite;

            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "GameFoundationPlaceholder" };
            var pixels = new Color32[16];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 0, 220, 255); // 醒目的洋红色占位，便于在画面中一眼识别缺资源。
            }
            texture.SetPixels32(pixels);
            texture.Apply();

            _placeholderSprite = Sprite.Create(
                texture,
                new UnityEngine.Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                PixelsPerUnit);
            _placeholderSprite.name = "placeholder.sprite";
            return _placeholderSprite;
        }

        /// <summary>GP-PRES-05 收口新增：一个运行期生成的圆形半透明深色占位精灵，供
        /// <see cref="SetShadow"/> 的 <see cref="ShadowMode.Blob"/> 分支使用——同
        /// <see cref="GetPlaceholderSprite"/> 惯例（本适配层不接触具体游戏内容，不内置任何正式
        /// 美术资源），只生成一次并缓存复用（全部实例共享同一张贴图，不区分单位大小——单位大小的
        /// 差异经 <see cref="SetTransform"/> 的 <c>scale</c> 一并缩放整个精灵实例，包括本影子，
        /// 见该方法"instance.Root.transform.localScale"一行）。</summary>
        private Sprite GetShadowSprite()
        {
            if (_shadowSprite != null) return _shadowSprite;

            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "GameFoundationShadowPlaceholder" };
            var pixels = new Color32[size * size];
            var center = (size - 1) / 2f;
            var radius = size / 2f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var dist = Mathf.Sqrt(dx * dx + dy * dy);
                    // 圆内半透明黑色，圆外全透明——最朴素的贴地椭圆影子占位形状，边缘留一点点羽化
                    // 避免像素锯齿过于生硬。
                    var alpha = Mathf.Clamp01(1f - (dist - (radius - 2f)) / 2f) * 0.5f;
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)(alpha * 255));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();

            _shadowSprite = Sprite.Create(
                texture,
                new UnityEngine.Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                PixelsPerUnit);
            _shadowSprite.name = "shadow.blob_placeholder";
            return _shadowSprite;
        }

        private EffectSequencePlayer RentSequencePlayer()
        {
            if (_sequencePlayerPool.Count > 0)
            {
                var pooled = _sequencePlayerPool[_sequencePlayerPool.Count - 1];
                _sequencePlayerPool.RemoveAt(_sequencePlayerPool.Count - 1);
                return pooled;
            }

            var go = new GameObject("EffectSequence");
            go.transform.SetParent(_root, worldPositionStays: false);
            return go.AddComponent<EffectSequencePlayer>();
        }

        private ParticleSystem RentParticleSystem()
        {
            if (_particlePool.Count > 0)
            {
                var pooled = _particlePool[_particlePool.Count - 1];
                _particlePool.RemoveAt(_particlePool.Count - 1);
                return pooled;
            }

            var go = new GameObject("ParticleEffect");
            go.transform.SetParent(_root, worldPositionStays: false);
            var ps = go.AddComponent<ParticleSystem>();

            // 判断记录：ParticleSystem 默认 playOnAwake=true，AddComponent 当帧就会开始播放；
            // 若不先停止就直接改 main.duration 等字段，会撞上 Unity 的
            // "Setting the duration while system is still playing is not supported" 断言警告
            // （PlayMode 测试 UnityRenderer2DTests.EmitParticle_ThenStop_ReturnsToPoolWithoutError
            // 曾经实测触发）。先整体停止 + 关闭 playOnAwake，再配置各字段。
            ps.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.duration = 1f;
            main.startLifetime = 0.5f;
            main.startSpeed = 2f;
            main.startSize = 0.2f;
            main.loop = false;
            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12) });
            emission.rateOverTime = 0f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Sprites/Default"));

            return ps;
        }

        private SpriteInstance EnsureAlive(SpriteHandle handle)
        {
            if (!_sprites.TryGetValue(handle.Value, out var instance))
            {
                throw new InvalidOperationException($"精灵句柄 {handle.Value} 已销毁或不存在");
            }

            return instance;
        }
    }
}
