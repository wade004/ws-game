using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Presentation.VfxSfx.Contracts;

namespace Presentation.VfxSfx.Core
{
    /// <summary>
    /// <see cref="ParticleFreezeFallbackRenderer2D"/> 的选项（手感落地 M4-W3）。
    /// </summary>
    public sealed class ParticleFreezeFallbackOptions
    {
        /// <summary>
        /// 适配层若有"调整一个已发射粒子的播放时间缩放"的能力（例如引擎粒子系统的模拟速度），由宿主在这里给出：暂停时以 0 调用、恢复时以 1 调用，
        /// 参数是被包装适配层自己的粒子句柄。给出后优先使用：粒子留在画面上、停在原处，恢复后从停住的那一帧继续，不消失、不重播。
        /// 缺省 null——用"停止并在恢复时按原参数重新发射"（见 <see cref="ParticleFreezeFallbackRenderer2D"/> 的判断记录）。
        /// </summary>
        public Action<ParticleHandle, double>? SetTimeScale { get; set; }
    }

    /// <summary>
    /// 没有 <see cref="IParticleFreezer"/> 的第三方适配层的通用兜底（手感落地 M4-W3，ADR-0129 增补）：一个 <see cref="IRenderer2D"/> 装饰器，
    /// 包住任意既有的 <see cref="IRenderer2D"/>（只用契约里已有的 <c>EmitParticle</c>/<c>StopParticle</c>，或宿主给的时间缩放口），自己实现
    /// <see cref="IParticleFreezer"/>，使 <see cref="VfxPlayer"/> 在这类适配层上也能在顿帧期间让该单位名下的粒子视觉上停住。
    /// <para>
    /// 用法：<c>var renderer = ParticleFreezeFallbackRenderer2D.Wrap(thirdPartyRenderer);</c> 再把 <c>renderer</c> 当作 <see cref="IRenderer2D"/> 交给表现装配；
    /// 被包装的适配层已经实现 <see cref="IParticleFreezer"/> 时 <see cref="Wrap"/> 原样返回它（不叠加第二层）。
    /// </para>
    /// <para>
    /// 判断记录（句柄间接）：装饰器给 <see cref="VfxPlayer"/> 的粒子句柄是它自己分配的"虚拟句柄"，内部映射到被包装适配层的真实句柄——暂停再恢复会换一个真实句柄
    /// （重新发射），虚拟句柄对 <see cref="VfxPlayer"/> 保持不变，它登记的宿主、跟随目标、存活计时都不必改。虚拟句柄与真实句柄的数值空间互不相干，
    /// 所以对不认识的句柄（已停止）调用 <c>StopParticle</c>/<c>SetParticlePaused</c> 一律静默忽略，不转发给被包装者。
    /// </para>
    /// <para>
    /// 判断记录（停止并重发的视觉语义，剩余边界）：<b>没有时间缩放口时</b>，暂停 = 停止真实粒子（画面上消失），恢复 = 在最近一次已知位置按原参数、原混合模式重新发射。
    /// 因此循环类特效（拖尾、光环、灼烧：挂在单位身上的持续特效，这正是冻结的主要对象）恢复后无缝接上，只是顿帧期间看不见；非循环特效恢复后从头重播一遍
    /// （存活计时仍从冻结点继续，所以只播剩余时长）——纯契约原语（发射/停止）里没有"读出已播进度"，无法精确续播。这是原语集合决定的边界，不是实现偷懒：
    /// 要画面精确停在原处，适配层应实现 <see cref="IParticleFreezer"/>（框架随附的 Unity 适配层已实现），或在 <see cref="ParticleFreezeFallbackOptions.SetTimeScale"/>
    /// 里接上自己的时间缩放口（有它则不消失、不重播）。
    /// </para>
    /// <para>
    /// 判断记录（可选能力透传）：被包装的适配层若实现 <see cref="IParticleRepositioner"/>，<see cref="Wrap"/> 返回的是同时实现它的子类
    /// （<see cref="RepositioningParticleFreezeFallbackRenderer2D"/>），位置更新透传给真实句柄、暂停期间只记录（恢复时按最新位置重发）；没实现则不实现，
    /// 使 <see cref="VfxPlayer"/> 的"未装配的能力静默跳过"探测结果与不包装时一致。
    /// </para>
    /// </summary>
    public class ParticleFreezeFallbackRenderer2D : IRenderer2D, IParticleFreezer
    {
        private sealed class Entry
        {
            public ParticleHandle Real;
            public Id EffectId;
            public Vec2 Position;
            public IReadOnlyDictionary<string, double> Parameters = null!;
            public VfxBlendMode? Blend;
            public bool Paused;
        }

        private readonly IRenderer2D _inner;
        private readonly ParticleFreezeFallbackOptions _options;
        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        private int _nextHandle = 1;

        public ParticleFreezeFallbackRenderer2D(IRenderer2D inner, ParticleFreezeFallbackOptions? options = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _options = options ?? new ParticleFreezeFallbackOptions();
        }

        /// <summary>
        /// 包装 <paramref name="inner"/>：它已经实现 <see cref="IParticleFreezer"/> 就原样返回；否则返回兜底装饰器（被包装者实现 <see cref="IParticleRepositioner"/>
        /// 时返回带位置透传的子类）。
        /// </summary>
        public static IRenderer2D Wrap(IRenderer2D inner, ParticleFreezeFallbackOptions? options = null)
        {
            if (inner == null) throw new ArgumentNullException(nameof(inner));
            if (inner is IParticleFreezer) return inner;
            return inner is IParticleRepositioner
                ? new RepositioningParticleFreezeFallbackRenderer2D(inner, options)
                : new ParticleFreezeFallbackRenderer2D(inner, options);
        }

        /// <summary>当前被装饰器跟踪的粒子数（已发射、尚未停止；暂停中的也计）。</summary>
        public int TrackedCount => _entries.Count;

        /// <summary>虚拟句柄当前是否处于暂停（句柄未知或已停止返回 false）。</summary>
        public bool IsPaused(ParticleHandle handle) => _entries.TryGetValue(handle.Value, out var e) && e.Paused;

        // ---------------------------------------------------------------- 粒子：虚拟句柄

        public ParticleHandle EmitParticle(Id effectId, Vec2 position, IReadOnlyDictionary<string, double> parameters) =>
            Track(_inner.EmitParticle(effectId, position, parameters), effectId, position, parameters, null);

        public ParticleHandle EmitParticle(Id effectId, Vec2 position, IReadOnlyDictionary<string, double> parameters, VfxBlendMode blendMode) =>
            Track(_inner.EmitParticle(effectId, position, parameters, blendMode), effectId, position, parameters, blendMode);

        private ParticleHandle Track(
            ParticleHandle real, Id effectId, Vec2 position, IReadOnlyDictionary<string, double> parameters, VfxBlendMode? blend)
        {
            var virtualHandle = new ParticleHandle(_nextHandle++);
            _entries[virtualHandle.Value] = new Entry
            {
                Real = real,
                EffectId = effectId,
                Position = position,
                Parameters = new Dictionary<string, double>(parameters),
                Blend = blend,
            };
            return virtualHandle;
        }

        public void StopParticle(ParticleHandle handle)
        {
            // 句柄空间由本装饰器独占：不认识的句柄一定是已经停止过的（虚拟句柄与被包装者的真实句柄数值可能重合，转发会误停别的粒子），静默忽略。
            if (!_entries.TryGetValue(handle.Value, out var entry)) return;

            _entries.Remove(handle.Value);
            if (!entry.Paused) _inner.StopParticle(entry.Real);
        }

        /// <inheritdoc />
        public void SetParticlePaused(ParticleHandle handle, bool paused)
        {
            if (!_entries.TryGetValue(handle.Value, out var entry)) return; // 已停止/不存在：静默忽略（契约）。
            if (entry.Paused == paused) return; // 幂等。

            var setTimeScale = _options.SetTimeScale;
            if (setTimeScale != null)
            {
                setTimeScale(entry.Real, paused ? 0.0 : 1.0);
                entry.Paused = paused;
                return;
            }

            if (paused)
            {
                _inner.StopParticle(entry.Real);
                entry.Paused = true;
                return;
            }

            entry.Real = entry.Blend.HasValue
                ? _inner.EmitParticle(entry.EffectId, entry.Position, entry.Parameters, entry.Blend.Value)
                : _inner.EmitParticle(entry.EffectId, entry.Position, entry.Parameters);
            entry.Paused = false;
        }

        /// <summary>位置更新（仅 <see cref="RepositioningParticleFreezeFallbackRenderer2D"/> 对外实现 <see cref="IParticleRepositioner"/>）。</summary>
        internal void Reposition(ParticleHandle handle, Vec2 position)
        {
            if (!_entries.TryGetValue(handle.Value, out var entry)) return;
            entry.Position = position;
            if (!entry.Paused) ((IParticleRepositioner)_inner).SetParticlePosition(entry.Real, position);
        }

        // ---------------------------------------------------------------- 其余成员：原样透传（含接口默认成员）

        public SpriteHandle CreateSpriteInstance(Id spriteSetId) => _inner.CreateSpriteInstance(spriteSetId);

        public void SetLayers(SpriteHandle handle, IReadOnlyList<Id> layers) => _inner.SetLayers(handle, layers);

        public void SetTransform(SpriteHandle handle, Vec2 position, double height, double sortY, int layer, double rotation, double scale, bool flipX) =>
            _inner.SetTransform(handle, position, height, sortY, layer, rotation, scale, flipX);

        public void SetShaderParam(SpriteHandle handle, string paramName, double value) => _inner.SetShaderParam(handle, paramName, value);

        public void SetShadow(SpriteHandle handle, ShadowMode mode) => _inner.SetShadow(handle, mode);

        public void DestroySpriteInstance(SpriteHandle handle) => _inner.DestroySpriteInstance(handle);

        public void SetSortIdentity(SpriteHandle handle, Id id) => _inner.SetSortIdentity(handle, id);

        public int CompareDrawOrder(SpriteHandle a, SpriteHandle b) => _inner.CompareDrawOrder(a, b);

        public MapLayerHandle CreateMapLayerInstance(Id mapId, MapLayerKind layer, Rect worldBounds, int renderLayer) =>
            _inner.CreateMapLayerInstance(mapId, layer, worldBounds, renderLayer);

        public void DestroyMapLayerInstance(MapLayerHandle handle) => _inner.DestroyMapLayerInstance(handle);
    }

    /// <summary>
    /// <see cref="ParticleFreezeFallbackRenderer2D"/> 的带位置透传版本：被包装的适配层实现了 <see cref="IParticleRepositioner"/> 时由
    /// <see cref="ParticleFreezeFallbackRenderer2D.Wrap"/> 返回，使 <see cref="VfxPlayer"/> 的跟随探测结果与不包装时一致。
    /// </summary>
    public sealed class RepositioningParticleFreezeFallbackRenderer2D : ParticleFreezeFallbackRenderer2D, IParticleRepositioner
    {
        public RepositioningParticleFreezeFallbackRenderer2D(IRenderer2D inner, ParticleFreezeFallbackOptions? options = null)
            : base(RequireRepositioner(inner), options)
        {
        }

        private static IRenderer2D RequireRepositioner(IRenderer2D inner)
        {
            if (inner == null) throw new ArgumentNullException(nameof(inner));
            if (!(inner is IParticleRepositioner))
            {
                throw new ArgumentException("被包装的适配层没有实现 IParticleRepositioner，请改用 ParticleFreezeFallbackRenderer2D", nameof(inner));
            }

            return inner;
        }

        /// <inheritdoc />
        public void SetParticlePosition(ParticleHandle handle, Vec2 position) => Reposition(handle, position);
    }
}
