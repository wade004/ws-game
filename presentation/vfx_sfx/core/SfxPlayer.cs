using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.Rng;
using Presentation.VfxSfx.Contracts;

namespace Presentation.VfxSfx.Core
{
    /// <summary>
    /// <see cref="ISfxPlayer"/> 的默认实现（见 09_表现层.md 第 5.3、5.5 节）：查 <c>sfx.def</c>、
    /// <c>variants</c> 非空时用 <see cref="IRngHost"/>（独立于规则层随机流，见
    /// <see cref="SfxOptions.RngStream"/>）随机挑一个变体、同层按 <c>priority</c> 抢占、经
    /// <see cref="IAudio"/> 播放（表现层铁律 P4）。
    /// <para>
    /// 判断记录（<c>priority</c> 大小方向，09 原文未定义）：本实现采用"数值越大优先级越高"，
    /// 未声明 <c>priority</c>（<see cref="SfxDef.Priority"/> 为 null）按最低优先级
    /// （<see cref="int.MinValue"/>）处理，即同层容量不足时最先被抢占停止。
    /// </para>
    /// </summary>
    public sealed class SfxPlayer : ISfxPlayer
    {
        private sealed class ActivePlayback
        {
            public SfxHandle Handle;
            public string Layer = string.Empty;
            public int Priority;
            public long InsertionSeq;
        }

        private readonly IAudio _audio;
        private readonly IRngHost _rng;
        private readonly IReadOnlyDictionary<Id, SfxDef> _catalog;
        private readonly SfxOptions _options;
        private readonly IPresentationDiagnostics _diagnostics;

        private readonly Dictionary<string, List<ActivePlayback>> _activeByLayer = new Dictionary<string, List<ActivePlayback>>(StringComparer.Ordinal);
        private readonly Dictionary<SfxHandle, ActivePlayback> _byHandle = new Dictionary<SfxHandle, ActivePlayback>();
        private readonly Dictionary<string, double> _layerVolume = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly HashSet<string> _mutedLayers = new HashSet<string>(StringComparer.Ordinal);
        private readonly IResourceLoader? _resourceLoader;
        private long _seq;

        /// <summary>外部审核阻塞项 4 收口（首次音效加载边界）：见 <see cref="Play"/> 判断记录"首次
        /// 引用未加载完成的资源"。</summary>
        private sealed class PendingPlay
        {
            public Id SfxId;
            public Id ResourceRef = default!;
            public string Layer = string.Empty;
            public int Priority;
            public Vec2? At;
            public DateTime Deadline;

            /// <summary>同 <c>VfxPlayer.PendingSpawn.Handle</c> 判断记录（同步加载器场景）：
            /// <see cref="QueuePendingPlay"/> 调用 <see cref="IResourceLoader.LoadAsync"/> 之后，若该
            /// 资源恰好同步加载完成，<see cref="OnResourceLoadCompleted"/> 会在同一次调用栈内就把
            /// 本条目从 <see cref="_pendingPlays"/> 摘除并真正 <c>IAudio.PlaySfx</c>——本类型是引用
            /// 类型，写在这里的句柄供 <see cref="QueuePendingPlay"/> 取回，使纯同步的测试/引擎场景
            /// 里 <see cref="Play"/> 依然能拿到真实句柄，不退化为总是 null。</summary>
            public SfxHandle? Handle;
        }

        private readonly List<PendingPlay> _pendingPlays = new List<PendingPlay>();

        /// <summary>同 <c>VfxPlayer._pendingResourceLoads</c> 判断记录：已经调用过
        /// <see cref="IResourceLoader.LoadAsync"/> 的资源 id 集合，且此后永远不再移除（含加载失败的
        /// 情形，失败不重试），避免同一资源被多次排队时重复调用
        /// <see cref="IResourceLoader.LoadAsync"/>。</summary>
        private readonly HashSet<Id> _pendingResourceLoads = new HashSet<Id>();

        /// <summary><paramref name="resourceLoader"/> 可选：注入时 <see cref="Play"/> 首次引用某个
        /// 音效资源 id（<c>sfx.def.resource_ref</c> 或其 <c>variants</c> 命中的具体变体）以
        /// <see cref="ResourceKind.Audio"/> 触发一次 <see cref="IResourceLoader.LoadAsync"/>
        /// （ADR-0016 决策 6）；资源尚未加载完成时排队等待（见 <see cref="Play"/> 判断记录"外部审核
        /// 阻塞项 4"），不是本类型自己再包一层 <see cref="Presentation.Common.ResourceReferenceTracker"/>
        /// ——本类型需要"加载完成后补播放"这个回调本身，<c>ResourceReferenceTracker.EnsureLoading</c>
        /// 的回调固定是空实现（见该类型注释），无法复用，因此改为直接持有 <paramref name="resourceLoader"/>
        /// 本身、自行维护"是否已请求过加载"的去重集合（<see cref="_pendingResourceLoads"/>），不再
        /// 经由 <c>ResourceReferenceTracker</c>。</summary>
        public SfxPlayer(
            IAudio audio,
            IRngHost rng,
            IReadOnlyDictionary<Id, SfxDef> catalog,
            SfxOptions? options = null,
            IPresentationDiagnostics? diagnostics = null,
            IResourceLoader? resourceLoader = null)
        {
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _options = options ?? new SfxOptions();
            _diagnostics = diagnostics ?? new PresentationDiagnosticsRecorder();
            _resourceLoader = resourceLoader;
        }

        public SfxHandle? Play(Id sfxId, Vec2? at)
        {
            SweepTimedOutPendingPlays();

            if (!_catalog.TryGetValue(sfxId, out var def))
            {
                _diagnostics.Warn($"sfx.def 未登记 id=\"{sfxId}\"，跳过播放");
                return null;
            }

            var resourceRef = PickResource(def);
            var layer = def.Layer;
            var priority = def.Priority ?? int.MinValue;

            // 外部审核阻塞项 4 收口（首次音效加载边界，见 architecture/落地计划/audit-20260907/
            // followup-2026-09-07.md"外部审核阻塞项处理"一节）：同 VfxPlayer.Spawn 判断记录——此前
            // 只调用 ResourceReferenceTracker.EnsureLoading（fire-and-forget）就立即在同一次调用内
            // IAudio.PlaySfx，首次引用某个资源时播放的是引擎侧尚未就绪的音效资源，落地为"首次施法
            // 命中音效不播放"（外部审核实测复现）。资源尚未加载完成时改为排队等待，不立即播放；
            // MakeRoomIfNeeded 的"抢占同层名额"这一步同样延后到真正播放时才做（现在就抢占会在
            // 加载失败/超时丢弃时白白抢占了名额却什么都没播），只在这里记录已经确定要播放这条请求。
            if (_resourceLoader != null && !_resourceLoader.IsLoaded(resourceRef))
            {
                // 判断记录（可能同步返回真实句柄，见 PendingPlay.Handle）：同 VfxPlayer.Spawn 同款
                // 判断记录——同步加载器（测试桩/引擎缓存命中）可能已经在 QueuePendingPlay 内部就
                // 完成了整个"加载 -> 补播放"，此时直接返回那次同步产生的真实句柄。
                return QueuePendingPlay(sfxId, resourceRef, layer, priority, at);
            }

            // 判断记录：不再调用 _resourceTracker?.EnsureLoading，同 VfxPlayer.Spawn 同款判断记录
            // ——走到这里说明资源已加载完成，_pendingResourceLoads 这一套机制已经统一负责"资源是否
            // 已请求过加载"，不需要也不应该再经由 ResourceReferenceTracker 重复请求一次（两套独立
            // 去重机制互不知道对方，重复调用会触发两次 LoadAsync）。
            MakeRoomIfNeeded(layer);

            var volume = ResolveVolume(layer);
            var handle = _audio.PlaySfx(resourceRef, volume, pitch: 1.0, position: at);

            var playback = new ActivePlayback { Handle = handle, Layer = layer, Priority = priority, InsertionSeq = _seq++ };
            GetOrCreateLayerList(layer).Add(playback);
            _byHandle[handle] = playback;

            return handle;
        }

        private SfxHandle? QueuePendingPlay(Id sfxId, Id resourceRef, string layer, int priority, Vec2? at)
        {
            var pending = new PendingPlay
            {
                SfxId = sfxId,
                ResourceRef = resourceRef,
                Layer = layer,
                Priority = priority,
                At = at,
                Deadline = DateTime.UtcNow.AddSeconds(_options.FirstLoadTimeoutSeconds),
            };
            _pendingPlays.Add(pending);

            if (_pendingResourceLoads.Add(resourceRef))
            {
                _resourceLoader!.LoadAsync(resourceRef, ResourceKind.Audio, OnResourceLoadCompleted);
            }

            // 见 PendingPlay.Handle 判断记录：同步加载器场景下可能已经同步完成并写好 Handle；
            // 异步场景下仍是 null，原样返回。
            return pending.Handle;
        }

        private void OnResourceLoadCompleted(Id resourceId, bool success)
        {
            var removedAny = false;

            for (var i = _pendingPlays.Count - 1; i >= 0; i--)
            {
                var pending = _pendingPlays[i];
                if (!pending.ResourceRef.Equals(resourceId))
                {
                    continue;
                }

                _pendingPlays.RemoveAt(i);
                removedAny = true;

                if (!success)
                {
                    _diagnostics.Warn($"sfx \"{pending.SfxId}\" 的资源 \"{resourceId}\" 加载失败，丢弃这次排队等待加载完成后播放的请求");
                    continue;
                }

                MakeRoomIfNeeded(pending.Layer);
                var volume = ResolveVolume(pending.Layer);
                var handle = _audio.PlaySfx(pending.ResourceRef, volume, pitch: 1.0, position: pending.At);

                var playback = new ActivePlayback { Handle = handle, Layer = pending.Layer, Priority = pending.Priority, InsertionSeq = _seq++ };
                GetOrCreateLayerList(pending.Layer).Add(playback);
                _byHandle[handle] = playback;
                pending.Handle = handle; // 见 PendingPlay.Handle 判断记录：供同步加载器场景下 QueuePendingPlay 取回。
            }

            // N17 根治：见 VfxPlayer.OnResourceLoadCompleted 同款判断记录，失败分支同样要通知。
            if (removedAny)
            {
                PendingPlayCountChanged?.Invoke();
            }
        }

        /// <summary>外部审核阻塞项 4 收口：<see cref="ISfxPlayer"/> 契约没有 <c>Update(dt)</c>
        /// 方法（09 原文未定义，不新增契约方法，见 <c>SfxOptions.FirstLoadTimeoutSeconds</c> 判断
        /// 记录），超时清理改为在下一次任意 <see cref="Play"/> 调用开头惰性扫一遍——正常游戏循环里
        /// <see cref="Play"/> 会被频繁调用（每次音效触发都会经过），足以在合理时间内发现并清理
        /// 真正卡死不回调的排队项，不需要专门的逐帧驱动。</summary>
        private void SweepTimedOutPendingPlays()
        {
            if (_pendingPlays.Count == 0)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var removedAny = false;
            for (var i = _pendingPlays.Count - 1; i >= 0; i--)
            {
                var pending = _pendingPlays[i];
                if (now >= pending.Deadline)
                {
                    _pendingPlays.RemoveAt(i);
                    removedAny = true;
                    _diagnostics.Warn(
                        $"sfx \"{pending.SfxId}\" 等待资源 \"{pending.ResourceRef}\" 加载超时" +
                        $"（{_options.FirstLoadTimeoutSeconds}s），丢弃这次排队的播放请求");
                }
            }

            // N17 根治：超时清理同样会让 PendingPlayCount 变化，必须一并通知（不止资源加载回调这
            // 一条路径），否则卡死资源永远超时清理却没有信号补一次完成检查，Immediate 模式下
            // 会永久卡在"曾经关闭过的节奏门"上（见 IFeedbackSink.PendingPlaybackChanged 判断记录）。
            if (removedAny)
            {
                PendingPlayCountChanged?.Invoke();
            }
        }

        public int PendingPlayCount => _pendingPlays.Count;

        /// <summary>N17 根治：见 <see cref="ISfxPlayer.PendingPlayCountChanged"/> 判断记录，在
        /// <see cref="OnResourceLoadCompleted"/>/<see cref="SweepTimedOutPendingPlays"/> 里触发。
        /// </summary>
        public event Action? PendingPlayCountChanged;

        public void Stop(SfxHandle handle)
        {
            if (_byHandle.TryGetValue(handle, out var playback))
            {
                RemoveFromLayer(playback);
                _byHandle.Remove(handle);
            }

            _audio.StopSfx(handle);
        }

        public void SetLayerVolume(string layer, double volume)
        {
            _layerVolume[layer] = volume;
        }

        public void SetLayerMuted(string layer, bool muted)
        {
            if (muted)
            {
                _mutedLayers.Add(layer);
            }
            else
            {
                _mutedLayers.Remove(layer);
            }
        }

        private Id PickResource(SfxDef def)
        {
            if (def.Variants == null || def.Variants.Count == 0)
            {
                return def.ResourceRef;
            }

            var index = _rng.NextInt(_options.RngStream, 0, def.Variants.Count - 1);
            return def.Variants[index];
        }

        private double ResolveVolume(string layer)
        {
            if (_mutedLayers.Contains(layer))
            {
                return 0.0;
            }

            return _layerVolume.TryGetValue(layer, out var v) ? v : 1.0;
        }

        private void MakeRoomIfNeeded(string layer)
        {
            var max = _options.MaxConcurrentPerLayer.TryGetValue(layer, out var m) ? m : _options.DefaultMaxConcurrent;
            if (max <= 0)
            {
                return;
            }

            var list = GetOrCreateLayerList(layer);
            if (list.Count < max)
            {
                return;
            }

            var victimIndex = 0;
            var victim = list[0];
            for (var i = 1; i < list.Count; i++)
            {
                var candidate = list[i];
                if (candidate.Priority < victim.Priority ||
                    (candidate.Priority == victim.Priority && candidate.InsertionSeq < victim.InsertionSeq))
                {
                    victim = candidate;
                    victimIndex = i;
                }
            }

            list.RemoveAt(victimIndex);
            _byHandle.Remove(victim.Handle);
            _audio.StopSfx(victim.Handle);
        }

        private void RemoveFromLayer(ActivePlayback playback)
        {
            if (_activeByLayer.TryGetValue(playback.Layer, out var list))
            {
                list.Remove(playback);
            }
        }

        private List<ActivePlayback> GetOrCreateLayerList(string layer)
        {
            if (!_activeByLayer.TryGetValue(layer, out var list))
            {
                list = new List<ActivePlayback>();
                _activeByLayer[layer] = list;
            }
            return list;
        }
    }
}
