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

        /// <summary>ADR-0083 新增：见 <see cref="PlaybackDiagnostics"/> 判断记录——独立于
        /// <see cref="_diagnostics"/>（文本消息列表）的单调累计计数式诊断，不可选注入，本类型
        /// 唯一写入方，构造期无条件自建（不像 <see cref="_diagnostics"/> 支持外部注入，因为当前
        /// 没有任何调用方需要跨实例共享/替换这份计数）。</summary>
        private readonly SfxPlaybackDiagnosticsRecorder _playbackDiagnostics = new SfxPlaybackDiagnosticsRecorder();

        private readonly Dictionary<string, List<ActivePlayback>> _activeByLayer = new Dictionary<string, List<ActivePlayback>>(StringComparer.Ordinal);
        private readonly Dictionary<SfxHandle, ActivePlayback> _byHandle = new Dictionary<SfxHandle, ActivePlayback>();

        /// <summary>缺陷修复（2026-09-26，见 <see cref="PlayAttached"/> 判断记录"冷加载路径不登记
        /// attach 键"）：值从"当前在播句柄"改为可表达两态的 <see cref="AttachEntry"/>——
        /// <see cref="AttachEntry.Handle"/>（已在播）或 <see cref="AttachEntry.Pending"/>（仍在排队
        /// 等待首次加载完成，见 <see cref="PendingPlay.AttachEntityId"/>）。键 (sfxId, entityId) →
        /// 当前跟踪的播放状态，供 <see cref="StopAttached"/> 定位。</summary>
        private readonly Dictionary<(Id SfxId, Id EntityId), AttachEntry> _activeByAttachKey = new Dictionary<(Id, Id), AttachEntry>();

        /// <summary>缺陷修复（2026-09-26）：<see cref="_activeByAttachKey"/> 的反向索引，句柄 →
        /// 该句柄对应的 attach 键。任何路径停止一个被跟踪的句柄（<see cref="Stop(SfxHandle)"/>、
        /// <see cref="MakeRoomIfNeeded"/> 同层抢占）都必须经它摘掉 <see cref="_activeByAttachKey"/>
        /// 对应条目，否则 <see cref="PlayAttached"/> 的"同键幂等"会在句柄已经停止播放之后仍然返回这个
        /// 死句柄，调用方以为循环音效还在播。只有升级为 <see cref="AttachEntry.Handle"/> 态的条目才会
        /// 出现在这里；<see cref="AttachEntry.Pending"/> 态没有句柄，不登记。</summary>
        private readonly Dictionary<SfxHandle, (Id SfxId, Id EntityId)> _attachKeyByHandle = new Dictionary<SfxHandle, (Id, Id)>();

        /// <summary>缺陷修复（2026-09-26）：见 <see cref="_activeByAttachKey"/> 判断记录。两态用
        /// <c>Handle</c>/<c>Pending</c> 二选一表达（构造方法保证互斥，不使用可空 struct 组合字段以
        /// 避免"两个都非空"的非法状态可表达），不做成 <c>readonly struct</c>——<see cref="Pending"/>
        /// 引用的 <see cref="PendingPlay"/> 实例的 <see cref="PendingPlay.Cancelled"/> 字段需要被
        /// <see cref="StopAttached"/> 直接原地翻转，语义上就是在改"这个条目当前指向的排队请求"这一
        /// 可变状态，用类类型更直接。</summary>
        private sealed class AttachEntry
        {
            public SfxHandle? Handle;
            public PendingPlay? Pending;

            public static AttachEntry FromHandle(SfxHandle handle) => new AttachEntry { Handle = handle };
            public static AttachEntry FromPending(PendingPlay pending) => new AttachEntry { Pending = pending };
        }
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

            /// <summary>ADR-0089 新增：排队等待首次加载完成时一并记下这次播放请求的循环标志，
            /// <see cref="OnResourceLoadCompleted"/> 真正 <c>IAudio.PlaySfx</c> 时原样传递，同
            /// <c>VfxPlayer.PendingSpawn.BlendMode</c> 判断记录同一套惯例。</summary>
            public bool Loop;

            /// <summary>缺陷修复（2026-09-26）：非 null 时表示这次排队的播放请求来自 <see
            /// cref="PlayAttached"/>，携带方式同 <see cref="Loop"/>——排队那一刻（<see
            /// cref="QueuePendingPlay"/> 内，早于任何 <see cref="IResourceLoader.LoadAsync"/> 回调）
            /// 就把 <c>(SfxId, AttachEntityId.Value)</c> 登记进 <see cref="_activeByAttachKey"/> 为
            /// <see cref="AttachEntry.Pending"/> 态，<see cref="OnResourceLoadCompleted"/> 真正播放
            /// 成功后升级为 <see cref="AttachEntry.Handle"/> 态。修复前只在 <see cref="Play"/> 同步
            /// 返回真实句柄时才登记，冷加载（真实引擎音频解码永远异步，见
            /// <c>UnityResourceLoader.LoadAsync</c> 判断记录"音频解码"）首播必然走这条排队路径，
            /// 键从未登记过，<see cref="StopAttached"/> 永远查不到、循环音效停不下来，见 ADR-0089
            /// "后果/已知限制"节 2026-09-26 追加。</summary>
            public Id? AttachEntityId;

            /// <summary>缺陷修复（2026-09-26）：见 <see cref="StopAttached"/> 判断记录"Pending 态：
            /// 取消排队"——true 时 <see cref="OnResourceLoadCompleted"/> 摘除本条目但不真正
            /// <c>IAudio.PlaySfx</c>（即便加载成功），避免"进入后没等加载完就离开"留下一个永远停不掉
            /// 的循环实例。仅对 <see cref="AttachEntityId"/> 非 null 的排队项有意义（<see
            /// cref="StopAttached"/> 只能定位到经 <see cref="PlayAttached"/> 排队的项）。</summary>
            public bool Cancelled;

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

        /// <summary>诊断转发到引擎控制台跟进（presentation/assembly/README.md 判断记录 10）：见
        /// <see cref="Presentation.VfxSfx.Core.VfxPlayer.Diagnostics"/> 同款判断记录——ABI 只新增只读
        /// 属性，暴露构造期注入（或默认自建）的诊断实例供 adapters/unity 轮询转发。</summary>
        public IPresentationDiagnostics Diagnostics => _diagnostics;

        /// <summary>ADR-0083 新增：单调累计的播放请求/开始/丢弃计数 + 最近一次播放记录，供消费方
        /// 不依赖抓瞬态即可确认"播放调用确实发生过"，经 <see cref="Presentation.Assembly.
        /// PresentationAssembly.SfxPlaybackDiagnostics"/> 转发到装配根，见该属性判断记录。</summary>
        public ISfxPlaybackDiagnostics PlaybackDiagnostics => _playbackDiagnostics;

        public SfxHandle? Play(Id sfxId, Vec2? at) => PlayCore(sfxId, at, attachEntityId: null);

        /// <summary>缺陷修复（2026-09-26）：<see cref="Play"/> 与 <see cref="PlayAttached"/> 共用的实现，
        /// <paramref name="attachEntityId"/> 非 null 时表示调用方是 <see cref="PlayAttached"/>——
        /// 必须在"排队那一刻"（走 <see cref="QueuePendingPlay"/> 分支、真正调用 <c>IAudio.PlaySfx</c>
        /// 之前）就把 attach 键登记为 <see cref="AttachEntry.Pending"/>，不能等 <see cref="Play"/>
        /// 同步返回句柄之后再登记——真实引擎音频解码永远异步（见 <see cref="PendingPlay.
        /// AttachEntityId"/> 判断记录），冷加载路径下 <see cref="Play"/> 本就不会同步返回句柄，
        /// 等它返回值再登记会永远登记不上。<see cref="Play"/> 本身不需要 attach 跟踪，
        /// 传 <c>null</c> 即可，原有行为逐字不变。</summary>
        private SfxHandle? PlayCore(Id sfxId, Vec2? at, Id? attachEntityId)
        {
            // ADR-0083：请求计数覆盖本次调用本身，与下方 SweepTimedOutPendingPlays 可能顺带清理掉
            // 的、属于更早调用的排队项无关（那些项各自的请求早已在各自发生的那次 Play 调用里计数
            // 过一次），因此本行必须在 Sweep 之前，不能与 Sweep 合并计数。
            _playbackDiagnostics.RecordRequested();

            SweepTimedOutPendingPlays();

            if (!_catalog.TryGetValue(sfxId, out var def))
            {
                _diagnostics.Warn($"sfx.def 未登记 id=\"{sfxId}\"，跳过播放");
                _playbackDiagnostics.RecordDropped();
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
            // ADR-0089：def.Loop 决定本次播放是否按循环方式转发给 IAudio.PlaySfx，见 SfxDef.Loop
            // 判断记录；未登记 sfx（上面已 return）不会走到这里，恒为已知 def。
            var loop = def.Loop;

            if (_resourceLoader != null && !_resourceLoader.IsLoaded(resourceRef))
            {
                // 判断记录（可能同步返回真实句柄，见 PendingPlay.Handle）：同 VfxPlayer.Spawn 同款
                // 判断记录——同步加载器（测试桩/引擎缓存命中）可能已经在 QueuePendingPlay 内部就
                // 完成了整个"加载 -> 补播放"，此时直接返回那次同步产生的真实句柄。
                return QueuePendingPlay(sfxId, resourceRef, layer, priority, at, loop, attachEntityId);
            }

            // 判断记录：不再调用 _resourceTracker?.EnsureLoading，同 VfxPlayer.Spawn 同款判断记录
            // ——走到这里说明资源已加载完成，_pendingResourceLoads 这一套机制已经统一负责"资源是否
            // 已请求过加载"，不需要也不应该再经由 ResourceReferenceTracker 重复请求一次（两套独立
            // 去重机制互不知道对方，重复调用会触发两次 LoadAsync）。
            MakeRoomIfNeeded(layer);

            var volume = ResolveVolume(layer);
            var handle = _audio.PlaySfx(resourceRef, volume, pitch: 1.0, position: at, loop: loop);

            var playback = new ActivePlayback { Handle = handle, Layer = layer, Priority = priority, InsertionSeq = _seq++ };
            GetOrCreateLayerList(layer).Add(playback);
            _byHandle[handle] = playback;
            _playbackDiagnostics.RecordStarted(resourceRef);

            if (attachEntityId.HasValue)
            {
                RegisterAttachHandle(sfxId, attachEntityId.Value, handle);
            }

            return handle;
        }

        private SfxHandle? QueuePendingPlay(Id sfxId, Id resourceRef, string layer, int priority, Vec2? at, bool loop, Id? attachEntityId)
        {
            var pending = new PendingPlay
            {
                SfxId = sfxId,
                ResourceRef = resourceRef,
                Layer = layer,
                Priority = priority,
                At = at,
                Deadline = DateTime.UtcNow.AddSeconds(_options.FirstLoadTimeoutSeconds),
                Loop = loop,
                AttachEntityId = attachEntityId,
            };
            _pendingPlays.Add(pending);

            // 缺陷修复（2026-09-26）：必须在这里、调用 LoadAsync 之前登记——见 PlayCore/PendingPlay.
            // AttachEntityId 判断记录"排队那一刻就登记，不等句柄"。即便下面 LoadAsync 是同步桩、在
            // 本方法返回前就已经回调完成，OnResourceLoadCompleted 也要能查到这条 Pending 登记才能
            // 正确升级为 Handle 态（见该方法判断记录），所以登记必须先于 LoadAsync 调用。
            if (attachEntityId.HasValue)
            {
                _activeByAttachKey[(sfxId, attachEntityId.Value)] = AttachEntry.FromPending(pending);
            }

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

                if (pending.Cancelled)
                {
                    // 缺陷修复（2026-09-26）：见 StopAttached 判断记录"Pending 态：取消排队"——
                    // StopAttached 已经在取消时摘掉 attach 键，这里只需要不真正播放；不产生诊断
                    // （同 StopAttached"查不到仍静默"同一惯例，取消本就是调用方主动发起的正常时序，
                    // 不是缺陷信号）。
                    continue;
                }

                if (!success)
                {
                    _diagnostics.Warn($"sfx \"{pending.SfxId}\" 的资源 \"{resourceId}\" 加载失败，丢弃这次排队等待加载完成后播放的请求");
                    _playbackDiagnostics.RecordDropped();
                    UnregisterAttachIfStillPending(pending);
                    continue;
                }

                MakeRoomIfNeeded(pending.Layer);
                var volume = ResolveVolume(pending.Layer);
                var handle = _audio.PlaySfx(pending.ResourceRef, volume, pitch: 1.0, position: pending.At, loop: pending.Loop);

                var playback = new ActivePlayback { Handle = handle, Layer = pending.Layer, Priority = pending.Priority, InsertionSeq = _seq++ };
                GetOrCreateLayerList(pending.Layer).Add(playback);
                _byHandle[handle] = playback;
                _playbackDiagnostics.RecordStarted(pending.ResourceRef);
                pending.Handle = handle; // 见 PendingPlay.Handle 判断记录：供同步加载器场景下 QueuePendingPlay 取回。

                // 缺陷修复（2026-09-26）：真正播放成功后，把排队时登记的 Pending 态升级为 Handle 态
                // ——见 PlayCore/PendingPlay.AttachEntityId 判断记录。ReferenceEquals 守卫：键当前
                // 指向的是不是恰好这一条 pending（不是同键更晚一次 PlayAttached 排队的新 pending）
                // ——StopAttached 取消旧 pending 后允许同键立刻重新 PlayAttached 排队一个新的，两者
                // 会在 _pendingPlays 里共存到各自被摘除为止，不加这层守卫会错误升级到一个已经被取消
                // 替换掉的旧条目上。
                if (pending.AttachEntityId.HasValue)
                {
                    var key = (pending.SfxId, pending.AttachEntityId.Value);
                    if (_activeByAttachKey.TryGetValue(key, out var entry) && ReferenceEquals(entry.Pending, pending))
                    {
                        RegisterAttachHandle(pending.SfxId, pending.AttachEntityId.Value, handle);
                    }
                }
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
                    _playbackDiagnostics.RecordDropped();
                    // 缺陷修复（2026-09-26）：超时丢弃同样要摘掉排队时登记的 attach 键（若未被
                    // StopAttached 提前取消/摘除），否则该键会永久指向一个再也不会被处理的 Pending，
                    // PlayAttached 幂等检查会认为"仍在排队"而拒绝发起新的播放请求。
                    UnregisterAttachIfStillPending(pending);
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

        /// <summary>C07 根治：见 <see cref="ISfxPlayer.Update"/> 判断记录——不依赖下一次 <see
        /// cref="Play"/> 调用，直接复用既有的 <see cref="SweepTimedOutPendingPlays"/>（内部已经在
        /// 真正摘除任何一项时触发 <see cref="PendingPlayCountChanged"/>），供引擎侧逐帧驱动。</summary>
        public void Update(double dt) => SweepTimedOutPendingPlays();

        public void Stop(SfxHandle handle)
        {
            if (_byHandle.TryGetValue(handle, out var playback))
            {
                RemoveFromLayer(playback);
                _byHandle.Remove(handle);
            }

            // 缺陷修复（2026-09-26）：见 _attachKeyByHandle 判断记录——任何路径停止一个被跟踪的句柄
            // 都要经这里摘掉 attach 键，不止 StopAttached 一条路径（同层抢占见 MakeRoomIfNeeded）。
            UnregisterAttachKeyForHandle(handle);

            _audio.StopSfx(handle);
        }

        /// <summary>ADR-0089：见 <see cref="ISfxPlayer.PlayAttached"/> 判断记录。非循环（未登记/
        /// <c>Loop=false</c>）退化为普通 <see cref="Play"/>，不登记键；循环且同键已登记（<see
        /// cref="AttachEntry.Handle"/> 已在播，或 <see cref="AttachEntry.Pending"/> 仍在排队等待冷
        /// 资源加载完成）时幂等返回既有句柄/null，不叠播、不重复排队——键在排队那一刻（<see
        /// cref="QueuePendingPlay"/> 内）就已登记，见 <see cref="PendingPlay.AttachEntityId"/> 判断
        /// 记录"缺陷修复（2026-09-26）"，不再要求"确实产生了句柄才登记"。</summary>
        public SfxHandle? PlayAttached(Id sfxId, Id entityId, Vec2? at)
        {
            if (!_catalog.TryGetValue(sfxId, out var def) || !def.Loop)
            {
                return Play(sfxId, at);
            }

            var key = (sfxId, entityId);
            if (_activeByAttachKey.TryGetValue(key, out var existing))
            {
                // Handle 态直接返回既有句柄；Pending 态原样返回 pending.Handle（同 QueuePendingPlay
                // 判断记录，通常为 null，同步加载器场景下可能已经写好）。
                return existing.Handle ?? existing.Pending!.Handle;
            }

            return PlayCore(sfxId, at, attachEntityId: entityId);
        }

        /// <summary>ADR-0089：见 <see cref="ISfxPlayer.StopAttached"/> 判断记录。缺陷修复
        /// （2026-09-26）新增 Pending 态处理：见类型顶部"后果/已知限制"追加。</summary>
        public void StopAttached(Id sfxId, Id entityId)
        {
            var key = (sfxId, entityId);
            if (!_activeByAttachKey.TryGetValue(key, out var entry))
            {
                return;
            }

            if (entry.Handle.HasValue)
            {
                // Stop 经 _attachKeyByHandle 反向索引摘除本条目（含 _activeByAttachKey 本身），
                // 这里不重复摘除。
                Stop(entry.Handle.Value);
                return;
            }

            // Pending 态：取消排队——"进入后没等加载完就离开"不能留下一个永远停不掉的循环。
            // OnResourceLoadCompleted 回来时见到 Cancelled=true 就不会真正 IAudio.PlaySfx（见该方法
            // 判断记录），此处立即摘键，不等加载完成/超时那一刻才摘。
            entry.Pending!.Cancelled = true;
            _activeByAttachKey.Remove(key);
        }

        /// <summary>缺陷修复（2026-09-26）：<see cref="PlayCore"/>（资源已加载，立即产生句柄）与
        /// <see cref="OnResourceLoadCompleted"/>（冷资源，加载完成后补产生句柄）共用——把 attach 键
        /// 登记/升级为 <see cref="AttachEntry.Handle"/> 态，并同步写反向索引 <see
        /// cref="_attachKeyByHandle"/>，供 <see cref="UnregisterAttachKeyForHandle"/> 定位。</summary>
        private void RegisterAttachHandle(Id sfxId, Id entityId, SfxHandle handle)
        {
            var key = (sfxId, entityId);
            _activeByAttachKey[key] = AttachEntry.FromHandle(handle);
            _attachKeyByHandle[handle] = key;
        }

        /// <summary>缺陷修复（2026-09-26）：见 <see cref="_attachKeyByHandle"/> 判断记录——任何路径
        /// 停止一个被跟踪的句柄都要调用本方法。查不到（该句柄本就不是经 <see cref="PlayAttached"/>
        /// 跟踪的）时静默忽略。</summary>
        private void UnregisterAttachKeyForHandle(SfxHandle handle)
        {
            if (!_attachKeyByHandle.TryGetValue(handle, out var key))
            {
                return;
            }

            _attachKeyByHandle.Remove(handle);
            if (_activeByAttachKey.TryGetValue(key, out var entry) && entry.Handle == handle)
            {
                _activeByAttachKey.Remove(key);
            }
        }

        /// <summary>缺陷修复（2026-09-26）：<see cref="OnResourceLoadCompleted"/> 加载失败分支、<see
        /// cref="SweepTimedOutPendingPlays"/> 超时分支共用——排队项最终没能播放成功时，若 attach 键
        /// 仍然指向这条排队项（未被 <see cref="StopAttached"/> 提前摘除/替换，ReferenceEquals 守卫同
        /// <see cref="OnResourceLoadCompleted"/> 成功分支判断记录），摘掉它，避免键永久指向一个再也
        /// 不会被处理的 Pending。</summary>
        private void UnregisterAttachIfStillPending(PendingPlay pending)
        {
            if (!pending.AttachEntityId.HasValue)
            {
                return;
            }

            var key = (pending.SfxId, pending.AttachEntityId.Value);
            if (_activeByAttachKey.TryGetValue(key, out var entry) && ReferenceEquals(entry.Pending, pending))
            {
                _activeByAttachKey.Remove(key);
            }
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
            // 缺陷修复（2026-09-26）：同层抢占停止的也可能是一个被 attach 跟踪的循环音效实例，同
            // Stop(SfxHandle) 判断记录，必须一并摘键，否则被抢占后 PlayAttached 幂等检查会返回一个
            // 已经停止播放的死句柄。
            UnregisterAttachKeyForHandle(victim.Handle);
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
