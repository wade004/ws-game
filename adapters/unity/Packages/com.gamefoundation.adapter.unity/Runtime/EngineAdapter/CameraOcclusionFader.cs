#nullable enable
// CameraOcclusionFader：第三人称镜头的遮挡淡化（ADR-0166，缺省关闭，游戏经 UnityCamera.EnableOcclusionFade 声明启用）。
//
// 问题：第三人称镜头绕角色（及锁定目标）转，树、岩石、柱子会落在"镜头 -> 主角/目标"的连线上，把画面挡死。
// 方案（ADR-0166 在"淡化"与"镜头前推"之间选了淡化）：每个固定登记的遮挡物在连线上时，把一个逐渲染物的"淡出量"属性从 1 压到 FadedOpacity，离开连线后恢复到 1；
// 镜头位置、取景与地面避让完全不动。着色器用这个属性做抖动透明（屏幕网点镂空）或半透明，框架不规定怎么画，只规定属性名与取值语义（见下）。
//
// 判断记录（判定几何）：遮挡判定 = 线段（镜头位置 -> 观察点）按 SightRadius 外扩后与遮挡物的世界轴对齐包围盒相交。
//   - 观察点 = 相机的焦点（UnityCamera 自动提供，即第三人称相机环绕的角色头部）+ 游戏登记的额外观察点（锁定目标的胸口等）；任一观察点被挡就算遮挡。
//   - 线段终点就是观察点，所以越过观察点、在角色身后的物体不会被淡化；外扩半径是角色轮廓的半宽，连线擦边而过的物体也算挡住（否则角色会被树冠边缘切掉半个身子）。
//   - 外扩用"包围盒各向外扩 SightRadius"（闵可夫斯基和取立方体而不是球），对盒角略偏保守（多淡化一点，不会少淡化）。
//   - 镜头位置落在包围盒里面（镜头钻进树冠）同样算遮挡。
//   - 包围盒在登记时取一次（遮挡物是静态布景）；遮挡物移动了调 Refresh。
// 判断记录（淡出量的取值语义）：属性 FadeProperty（缺省 "_OcclusionFade"）是浮点，1 = 完全不透明（与没启用时逐位一致），FadedOpacity（缺省 0.25）= 被挡时的目标不透明度。
//   值经渲染物的 MaterialPropertyBlock 写入，不改共享材质；恢复到 1 时清掉属性块（SetPropertyBlock(null)），渲染物重新参与合批。
//   因此：本类型拥有被登记渲染物的属性块（不要再往同一渲染物写自己的属性块）；着色器要把该属性声明在 UnityPerMaterial 常量缓冲里，
//   并在片元里按它镂空/混合（抖动透明：阈值矩阵 < 淡出量才保留像素，淡出量为 1 时一个像素都不丢）。
// 判断记录（时间）：淡出/恢复按 Update 传入的 dt 线性推进（淡出 FadeOutSeconds 走完 1 -> FadedOpacity，恢复 RestoreSeconds 走完回 1），不读墙钟，
//   测试用固定 dt 即可逐位确定；宿主一般经 UnityCamera.Tick 每帧驱动。纯表现，不回流逻辑层。
// 判断记录（只管登记过的渲染物）：地形、天空等不登记就永远不受影响；地形遮挡由 UnityCamera 的地面避让负责。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Adapter.Unity.EngineAdapter
{
    /// <summary>遮挡淡化的参数（声明期校验，非法立刻抛参数错误，不静默夹紧）。</summary>
    public sealed class CameraOcclusionOptions
    {
        /// <summary>缺省的淡出量属性名。</summary>
        public const string DefaultFadeProperty = "_OcclusionFade";

        /// <summary>被挡时的目标不透明度，[0, 1)；缺省 0.25。</summary>
        public double FadedOpacity { get; set; } = 0.25;

        /// <summary>从完全不透明淡到 <see cref="FadedOpacity"/> 所需秒数，&gt; 0；缺省 0.15。</summary>
        public double FadeOutSeconds { get; set; } = 0.15;

        /// <summary>从 <see cref="FadedOpacity"/> 恢复到完全不透明所需秒数，&gt; 0；缺省 0.3。</summary>
        public double RestoreSeconds { get; set; } = 0.3;

        /// <summary>连线外扩半径（世界单位，角色轮廓半宽），&gt;= 0；缺省 0.45。</summary>
        public double SightRadius { get; set; } = 0.45;

        /// <summary>写进渲染物属性块的浮点属性名；缺省 <see cref="DefaultFadeProperty"/>。</summary>
        public string FadeProperty { get; set; } = DefaultFadeProperty;

        internal CameraOcclusionOptions Validated()
        {
            if (double.IsNaN(FadedOpacity) || FadedOpacity < 0.0 || FadedOpacity >= 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(FadedOpacity), FadedOpacity, "被挡时的目标不透明度必须在 [0, 1) 内");
            }

            if (!(FadeOutSeconds > 0.0) || double.IsInfinity(FadeOutSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(FadeOutSeconds), FadeOutSeconds, "淡出秒数必须是正的有限数");
            }

            if (!(RestoreSeconds > 0.0) || double.IsInfinity(RestoreSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(RestoreSeconds), RestoreSeconds, "恢复秒数必须是正的有限数");
            }

            if (double.IsNaN(SightRadius) || double.IsInfinity(SightRadius) || SightRadius < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(SightRadius), SightRadius, "连线外扩半径必须是非负有限数");
            }

            if (string.IsNullOrEmpty(FadeProperty))
            {
                throw new ArgumentException("淡出量属性名不能为空", nameof(FadeProperty));
            }

            return new CameraOcclusionOptions
            {
                FadedOpacity = FadedOpacity,
                FadeOutSeconds = FadeOutSeconds,
                RestoreSeconds = RestoreSeconds,
                SightRadius = SightRadius,
                FadeProperty = FadeProperty,
            };
        }
    }

    public sealed class CameraOcclusionFader
    {
        private sealed class Entry
        {
            public Entry(Renderer renderer, Vector3 min, Vector3 max)
            {
                Renderer = renderer;
                Min = min;
                Max = max;
            }

            public Renderer Renderer;
            public Vector3 Min;
            public Vector3 Max;
            public double Opacity = 1.0;
            public bool Occluding;
        }

        /// <summary>淡出量与 1 的差小于它就当作已恢复（清属性块）。</summary>
        private const double RestoredEpsilon = 1e-6;

        private readonly List<Entry> _entries = new List<Entry>();
        private readonly Dictionary<Renderer, Entry> _byRenderer = new Dictionary<Renderer, Entry>();
        private readonly List<Vector3> _extraWatchPoints = new List<Vector3>();
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private readonly int _propertyId;

        public CameraOcclusionFader(CameraOcclusionOptions? options = null)
        {
            Options = (options ?? new CameraOcclusionOptions()).Validated();
            _propertyId = Shader.PropertyToID(Options.FadeProperty);
        }

        /// <summary>生效的参数（构造时校验并复制，之后不可变）。</summary>
        public CameraOcclusionOptions Options { get; }

        /// <summary>已登记的遮挡物数。</summary>
        public int Count => _entries.Count;

        /// <summary>当前被淡化（不透明度低于 1）的遮挡物数。</summary>
        public int FadedCount
        {
            get
            {
                var n = 0;
                for (var i = 0; i < _entries.Count; i++)
                {
                    if (_entries[i].Opacity < 1.0 - RestoredEpsilon)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        /// <summary>当前挡住连线的遮挡物数（最近一次 <see cref="Update"/> 的判定）。</summary>
        public int OccludingCount
        {
            get
            {
                var n = 0;
                for (var i = 0; i < _entries.Count; i++)
                {
                    if (_entries[i].Occluding)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        /// <summary>登记一个遮挡物（静态布景）；包围盒取登记那一刻的世界包围盒。重复登记等于 <see cref="Refresh"/>。</summary>
        public void Add(Renderer renderer)
        {
            if (renderer == null)
            {
                throw new ArgumentNullException(nameof(renderer));
            }

            var bounds = renderer.bounds;
            if (_byRenderer.TryGetValue(renderer, out var existing))
            {
                existing.Min = bounds.min;
                existing.Max = bounds.max;
                return;
            }

            var entry = new Entry(renderer, bounds.min, bounds.max);
            _entries.Add(entry);
            _byRenderer.Add(renderer, entry);
        }

        public void Add(IEnumerable<Renderer> renderers)
        {
            foreach (var renderer in renderers)
            {
                Add(renderer);
            }
        }

        /// <summary>遮挡物移动或变形后重新取包围盒；未登记的渲染物忽略。</summary>
        public void Refresh(Renderer renderer)
        {
            if (renderer != null && _byRenderer.TryGetValue(renderer, out var entry))
            {
                var bounds = renderer.bounds;
                entry.Min = bounds.min;
                entry.Max = bounds.max;
            }
        }

        /// <summary>注销一个遮挡物并立刻恢复它的不透明度；未登记返回 false。</summary>
        public bool Remove(Renderer renderer)
        {
            if (renderer == null || !_byRenderer.TryGetValue(renderer, out var entry))
            {
                return false;
            }

            _byRenderer.Remove(renderer);
            _entries.Remove(entry);
            Restore(entry);
            return true;
        }

        /// <summary>注销全部遮挡物（切图时调用），并恢复它们的不透明度。</summary>
        public void Clear()
        {
            for (var i = 0; i < _entries.Count; i++)
            {
                Restore(_entries[i]);
            }

            _entries.Clear();
            _byRenderer.Clear();
        }

        /// <summary>设置额外观察点（世界坐标，如锁定目标的胸口）；每次调用整体替换。相机焦点总是观察点，不用在这里写。</summary>
        public void SetWatchPoints(IEnumerable<Vector3>? points)
        {
            _extraWatchPoints.Clear();
            if (points != null)
            {
                _extraWatchPoints.AddRange(points);
            }
        }

        public void ClearWatchPoints() => _extraWatchPoints.Clear();

        /// <summary>某遮挡物当前的不透明度（1 = 不受影响）；未登记返回 1。</summary>
        public double GetOpacity(Renderer renderer) =>
            renderer != null && _byRenderer.TryGetValue(renderer, out var entry) ? entry.Opacity : 1.0;

        /// <summary>某遮挡物是否正挡在连线上（最近一次 <see cref="Update"/> 的判定）；未登记返回 false。</summary>
        public bool IsOccluding(Renderer renderer) =>
            renderer != null && _byRenderer.TryGetValue(renderer, out var entry) && entry.Occluding;

        /// <summary>
        /// 推进一步：对每个遮挡物判定"镜头 -> 观察点"连线是否被它挡住，按 <paramref name="deltaSeconds"/> 线性推进不透明度并写进渲染物的属性块。
        /// <paramref name="focus"/> 是相机焦点（总是观察点）；已被销毁的渲染物自动注销。
        /// </summary>
        public void Update(double deltaSeconds, Vector3 cameraPosition, Vector3 focus)
        {
            var dt = deltaSeconds > 0.0 && !double.IsNaN(deltaSeconds) ? deltaSeconds : 0.0;
            var radius = (float)Options.SightRadius;
            var fadeRate = (1.0 - Options.FadedOpacity) / Options.FadeOutSeconds;
            var restoreRate = (1.0 - Options.FadedOpacity) / Options.RestoreSeconds;
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (entry.Renderer == null)
                {
                    // 渲染物已被引擎销毁（换图拆了布景）：自动注销。字典按实例哈希，销毁后仍能用同一引用移除。
                    _entries.RemoveAt(i);
                    _byRenderer.Remove(entry.Renderer);
                    continue;
                }

                var min = entry.Min - new Vector3(radius, radius, radius);
                var max = entry.Max + new Vector3(radius, radius, radius);
                var blocked = SegmentHitsBox(cameraPosition, focus, min, max);
                for (var k = 0; !blocked && k < _extraWatchPoints.Count; k++)
                {
                    blocked = SegmentHitsBox(cameraPosition, _extraWatchPoints[k], min, max);
                }

                entry.Occluding = blocked;
                var before = entry.Opacity;
                if (blocked)
                {
                    entry.Opacity = Math.Max(Options.FadedOpacity, entry.Opacity - fadeRate * dt);
                }
                else
                {
                    entry.Opacity = Math.Min(1.0, entry.Opacity + restoreRate * dt);
                }

                if (entry.Opacity != before)
                {
                    Apply(entry);
                }
            }
        }

        private void Apply(Entry entry)
        {
            if (entry.Opacity >= 1.0 - RestoredEpsilon)
            {
                entry.Opacity = 1.0;
                entry.Renderer.SetPropertyBlock(null);
                return;
            }

            entry.Renderer.GetPropertyBlock(_block);
            _block.SetFloat(_propertyId, (float)entry.Opacity);
            entry.Renderer.SetPropertyBlock(_block);
        }

        private static void Restore(Entry entry)
        {
            entry.Occluding = false;
            if (entry.Opacity < 1.0 && entry.Renderer != null)
            {
                entry.Renderer.SetPropertyBlock(null);
            }

            entry.Opacity = 1.0;
        }

        /// <summary>线段 a-b 与轴对齐包围盒 [min, max] 是否相交（含端点在盒内；公开供测试与游戏自检）。</summary>
        public static bool SegmentHitsBox(Vector3 a, Vector3 b, Vector3 min, Vector3 max)
        {
            var tMin = 0.0;
            var tMax = 1.0;
            return Slab(a.x, b.x - a.x, min.x, max.x, ref tMin, ref tMax)
                && Slab(a.y, b.y - a.y, min.y, max.y, ref tMin, ref tMax)
                && Slab(a.z, b.z - a.z, min.z, max.z, ref tMin, ref tMax);
        }

        private static bool Slab(double origin, double delta, double lo, double hi, ref double tMin, ref double tMax)
        {
            if (Math.Abs(delta) < 1e-12)
            {
                return origin >= lo && origin <= hi;
            }

            var t1 = (lo - origin) / delta;
            var t2 = (hi - origin) / delta;
            if (t1 > t2)
            {
                var swap = t1;
                t1 = t2;
                t2 = swap;
            }

            tMin = Math.Max(tMin, t1);
            tMax = Math.Min(tMax, t2);
            return tMin <= tMax;
        }
    }
}
