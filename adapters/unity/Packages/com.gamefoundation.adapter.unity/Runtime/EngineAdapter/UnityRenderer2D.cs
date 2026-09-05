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
// 高度偏移契约缺口的落地（对应 presentation/render/core/SpriteViewBase.cs 顶部"契约缺口"
// 注释）：SetShaderParam 收到参数名 HeightOffsetShaderParam（"height_offset_px"）时，
// 按"纵向像素偏移"解释——只平移 LayersRoot 子物体的本地 Y 坐标（像素值 / PixelsPerUnit 换算成
// 世界单位），不改变根物体的位置/sortY/sortingOrder，因为 02 文档明确"height_offset_px"只是
// 纯视觉工作绕，不代表任何真实的第三根世界坐标轴（同 UnityCamera.cs 顶部判断记录）。
// 其余参数名一律尝试用 MaterialPropertyBlock 按同名 float 属性下发给全部纸娃娃层子渲染器，
// 目标 Shader 没有该属性时 SetFloat 是无操作，不会报错。
//
// 资源解析与占位：CreateSpriteInstance/SetLayers 用到的资源 id 一律经 UnityResourceLoader
// 解析；解析不到（未加载或加载失败）时使用一个运行期生成的纯色方块精灵占位，并
// Debug.LogWarning 一次（按 id 去重，避免刷屏），不抛异常——保证游戏在资源缺失时仍可运行，
// 只是画面上看到占位方块。
//
// 粒子：EmitParticle/StopParticle 用 ParticleSystem 对象池。02 的 ResourceKind 枚举里没有
// "粒子/特效预制体"这一种类（已知契约缺口，见包 README），因此 effectId 目前不解析到任何
// 具体制作的粒子资产，一律播放一个内建的通用爆发粒子效果；parameters 里的
// "duration"/"start_lifetime"/"start_speed"/"start_size" 四个已知键会覆盖对应模块参数，
// 其余键被忽略。
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

        public void SetTransform(SpriteHandle handle, Vec2 position, double sortY, int layer, double rotation, double scale, bool flipX)
        {
            var instance = EnsureAlive(handle);

            instance.Root.transform.localPosition = new Vector3((float)position.X, (float)position.Y, 0f);
            instance.Root.transform.localRotation = Quaternion.Euler(0f, 0f, (float)(rotation * Mathf.Rad2Deg));
            instance.Root.transform.localScale = new Vector3((float)scale, (float)scale, 1f);

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

            if (string.Equals(paramName, "height_offset_px", StringComparison.Ordinal))
            {
                var worldOffset = (float)(value / Math.Max(PixelsPerUnit, 0.0001));
                var local = instance.LayersRoot.localPosition;
                instance.LayersRoot.localPosition = new Vector3(local.x, worldOffset, local.z);
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
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                PixelsPerUnit);
            _placeholderSprite.name = "placeholder.sprite";
            return _placeholderSprite;
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
