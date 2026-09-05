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
        }

        private readonly Transform _root;
        private readonly UnityResourceLoader _resourceLoader;
        private readonly Dictionary<int, SpriteInstance> _sprites = new Dictionary<int, SpriteInstance>();
        private readonly Dictionary<int, ParticleSystem> _particles = new Dictionary<int, ParticleSystem>();
        private readonly List<ParticleSystem> _particlePool = new List<ParticleSystem>();
        private readonly Dictionary<int, EffectSequencePlayer> _sequencePlayers = new Dictionary<int, EffectSequencePlayer>();
        private readonly List<EffectSequencePlayer> _sequencePlayerPool = new List<EffectSequencePlayer>();
        private readonly HashSet<string> _missingResourceWarned = new HashSet<string>(StringComparer.Ordinal);
        private int _nextSpriteHandle = 1;
        private int _nextParticleHandle = 1;
        private Sprite? _placeholderSprite;

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

            ApplySortingOrders(instance);
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
                var multiplier = 1f + Math.Max(0f, (float)value);
                var flashColor = new Color(multiplier, multiplier, multiplier, 1f);
                foreach (var renderer in instance.LayerRenderers)
                {
                    renderer.color = flashColor;
                }
                return;
            }

            foreach (var renderer in instance.LayerRenderers)
            {
                instance.PropertyBlock.SetFloat(paramName, (float)value);
                renderer.SetPropertyBlock(instance.PropertyBlock);
            }
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
