using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Presentation.VfxSfx.Contracts;

namespace Presentation.VfxSfx.Core
{
    /// <summary>
    /// <see cref="IVfxPlayer"/> 的默认实现（见 09_表现层.md 第 5.3 节）：查 <c>vfx.def</c> 得到
    /// <see cref="VfxAttachMode"/>，据此解释调用方传入的 <see cref="VfxAttach"/>，经
    /// <see cref="IRenderer2D.EmitParticle"/> 播放（表现层铁律 P4：只经 L-1 接口绘制）。
    /// <c>vfx.def.resource_ref</c> 首次被引用（<see cref="Spawn"/> 首次用到某个 <c>resourceRef</c>）
    /// 时以 <see cref="ResourceKind.Effect"/> 触发一次 <see cref="IResourceLoader.LoadAsync"/>
    /// （ADR-0016 决策 5、6："谁首次引用谁加载"，见 <see cref="Presentation.Common.ResourceReferenceTracker"/>）。
    /// </summary>
    public sealed class VfxPlayer : IVfxPlayer
    {
        private readonly IRenderer2D _renderer2D;
        private readonly ICamera _camera;
        private readonly IReadOnlyDictionary<Id, VfxDef> _catalog;
        private readonly VfxOptions _options;
        private readonly AnchorResolver? _anchorResolver;
        private readonly EntityPositionResolver? _entityPositionResolver;
        private readonly IPresentationDiagnostics _diagnostics;
        private readonly VfxPool _pool;
        private readonly Presentation.Common.ResourceReferenceTracker? _resourceTracker;

        /// <summary>句柄 → 所属 <c>vfx.def.category</c>，供 <see cref="Stop"/> 时同步从
        /// <see cref="_pool"/> 摘除记录（<see cref="VfxPool.Untrack"/> 需要遍历全部分类，这里
        /// 反向索引一份避免每次 Stop 都线性扫描全部池）。</summary>
        private readonly Dictionary<ParticleHandle, string> _handleCategory = new Dictionary<ParticleHandle, string>();

        /// <summary><paramref name="resourceLoader"/> 可选：未注入时不主动触发任何资源加载
        /// （沿用注入前的行为，供不接 <see cref="IResourceLoader"/> 的最小测试/集成场景使用）。</summary>
        public VfxPlayer(
            IRenderer2D renderer2D,
            ICamera camera,
            IReadOnlyDictionary<Id, VfxDef> catalog,
            VfxOptions? options = null,
            AnchorResolver? anchorResolver = null,
            EntityPositionResolver? entityPositionResolver = null,
            IPresentationDiagnostics? diagnostics = null,
            IResourceLoader? resourceLoader = null)
        {
            _renderer2D = renderer2D ?? throw new ArgumentNullException(nameof(renderer2D));
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _options = options ?? new VfxOptions();
            _anchorResolver = anchorResolver;
            _entityPositionResolver = entityPositionResolver;
            _diagnostics = diagnostics ?? new PresentationDiagnosticsRecorder();
            _pool = new VfxPool(_options, StopInternal);
            _resourceTracker = resourceLoader != null ? new Presentation.Common.ResourceReferenceTracker(resourceLoader) : null;
        }

        public ParticleHandle? Spawn(Id vfxId, VfxAttach at, IReadOnlyDictionary<string, double>? parameters)
        {
            if (!_catalog.TryGetValue(vfxId, out var def))
            {
                _diagnostics.Warn($"vfx.def 未登记 id=\"{vfxId}\"，跳过播放");
                return null;
            }

            if (def.AttachMode != at.Mode)
            {
                _diagnostics.Warn(
                    $"vfx \"{vfxId}\" 声明 attach_mode={def.AttachMode}，但调用方传入的 VfxAttach.Mode={at.Mode} 不一致，跳过播放");
                return null;
            }

            var emitParams = parameters ?? EmptyParams;

            Vec2? worldPos = def.AttachMode switch
            {
                VfxAttachMode.World => at.Position,
                VfxAttachMode.Anchor => ResolveAnchor(vfxId, at),
                VfxAttachMode.Socket => ResolveSocketDowngradedToWorld(vfxId, at),
                VfxAttachMode.Screen => ResolveScreen(vfxId, at),
                _ => null,
            };

            if (worldPos == null)
            {
                // 具体原因已在对应 Resolve* 分支记过诊断。
                return null;
            }

            _resourceTracker?.EnsureLoading(def.ResourceRef, ResourceKind.Effect);
            var handle = _renderer2D.EmitParticle(def.ResourceRef, worldPos.Value, emitParams);
            _handleCategory[handle] = def.Category;
            _pool.Track(def.Category, handle, def.Lifetime);
            return handle;
        }

        public void Stop(ParticleHandle handle)
        {
            _pool.Untrack(handle);
            StopInternal(handle);
        }

        public void Update(double dt) => _pool.Update(dt);

        private void StopInternal(ParticleHandle handle)
        {
            _handleCategory.Remove(handle);
            _renderer2D.StopParticle(handle);
        }

        private Vec2? ResolveAnchor(Id vfxId, VfxAttach at)
        {
            var entityId = at.EntityId!.Value;
            var anchorId = at.PointId!.Value;

            var resolved = _anchorResolver?.Invoke(entityId, anchorId);
            if (resolved.HasValue)
            {
                return resolved;
            }

            var fallback = _entityPositionResolver?.Invoke(entityId);
            if (fallback.HasValue)
            {
                _diagnostics.Warn(
                    $"vfx \"{vfxId}\"：锚点 \"{anchorId}\"（实体 \"{entityId}\"）查不到，按 09 第 5.3 节判断记录退化为实体位置");
                return fallback;
            }

            _diagnostics.Warn(
                $"vfx \"{vfxId}\"：锚点 \"{anchorId}\" 与实体 \"{entityId}\" 的兜底位置均查不到，跳过播放");
            return null;
        }

        /// <summary>
        /// 判断记录（socket 挂接降级为 world，见 vfx_sfx/README.md 契约缺口）：
        /// <see cref="Core.Foundation.EngineAdapter.IRenderer3D.AttachToSocket"/> 要求挂接的子体是
        /// 一个已创建的 <see cref="Core.Foundation.EngineAdapter.ModelHandle"/>（骨骼模型实例），
        /// 而不是"把一个粒子特效句柄挂到挂点上"；本模块的契约输入只有 <c>vfx.def.resource_ref</c>
        /// 与实体/挂点 id，既没有预先创建好的子模型句柄，也没有"host 实体的 ModelHandle"这一信息
        /// （由 render/view_binding 模块持有，未在本任务契约清单内），因此无法满足
        /// <c>AttachToSocket</c> 的参数形状——按设计拍板"若接口有但形状不满足；否则记诊断降级为
        /// world"退化：用 <see cref="EntityPositionResolver"/> 取该实体的位置，经
        /// <see cref="IRenderer2D.EmitParticle"/> 以世界坐标方式播放，并记一条诊断，供集成阶段
        /// 换成真正的挂点挂接（需要 render 模块补一个"取某实体某挂点的 ModelHandle 容器"或
        /// "创建子模型并 AttachToSocket"的窄契约）。
        /// </summary>
        private Vec2? ResolveSocketDowngradedToWorld(Id vfxId, VfxAttach at)
        {
            var entityId = at.EntityId!.Value;
            var socketId = at.PointId!.Value;

            var fallback = _entityPositionResolver?.Invoke(entityId);
            if (fallback.HasValue)
            {
                _diagnostics.Warn(
                    $"vfx \"{vfxId}\"：attach_mode=socket（挂点 \"{socketId}\"，实体 \"{entityId}\"）当前 L-1 契约无法挂接粒子句柄，降级为 world 播放于实体位置");
                return fallback;
            }

            _diagnostics.Warn(
                $"vfx \"{vfxId}\"：attach_mode=socket 降级为 world 但实体 \"{entityId}\" 的位置也查不到，跳过播放");
            return null;
        }

        private Vec2? ResolveScreen(Id vfxId, VfxAttach at)
        {
            var resolved = _camera.ScreenToWorld(at.Position);
            if (resolved.HasValue)
            {
                return resolved;
            }

            _diagnostics.Warn($"vfx \"{vfxId}\"：屏幕坐标 {at.Position} 经 ICamera.ScreenToWorld 找不到交点");

            if (_options.SkipOnUnresolvableScreenAttach)
            {
                return null;
            }

            return Vec2.Zero;
        }

        private static readonly IReadOnlyDictionary<string, double> EmptyParams = new Dictionary<string, double>();
    }
}
