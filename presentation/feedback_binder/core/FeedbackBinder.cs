using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;
using Core.Rules.ExprHost;
using Presentation.FeedbackBinder.Contracts;
using Presentation.Render;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;

namespace Presentation.FeedbackBinder.Core
{
    /// <summary>
    /// 把"逻辑事件"翻译为"一组具体表现动作"的规则引擎（见 09_表现层.md 第 6 节）：加载规则、
    /// 按 <c>event</c> 分组订阅、事件到达时按 <c>id</c> 序求值 <c>condition</c>（经
    /// <see cref="IExprHostFactory"/>，selfId/targetId 从事件字段
    /// <c>sourceId</c>/<c>casterId</c>/<c>unitId</c> 与 <c>targetId</c> 取），逐条执行动作。
    /// <para>
    /// 表现层铁律遵守：只订阅事件（P2）——本类型经 <see cref="IEventBus.Subscribe"/> 感知世界，
    /// 从不轮询；唯一允许发出的事件是 <see cref="PlaybackFinishedEvent"/>（经
    /// <see cref="PlaybackQueue.Finished"/> 联动），且只在 <see cref="FeedbackOptions.QueueMode"/>
    /// 为 <see cref="QueueMode.Sequential"/> 时才可能发出（<see cref="PlaybackQueue"/> 类型注释）。
    /// 只经 <see cref="IFeedbackSink"/> 这一扇窄门下达动作指令，不直接调用任何 L-1 接口（P4，具体
    /// 绘制/播放留给 <see cref="IFeedbackSink"/> 实现）。
    /// </para>
    /// </summary>
    public sealed class FeedbackBinder : IDisposable
    {
        private readonly IEventBus _bus;
        private readonly IExprHostFactory _exprHosts;
        private readonly IFeedbackSink _sink;
        private readonly DisplayInfoResolver? _displayInfoResolver;
        private readonly EntityLogicalIdResolver? _entityLogicalIdResolver;
        private readonly IUnitAccess? _unitAccess;
        private readonly FeedbackOptions _options;
        private readonly IExprDiagnostics _exprDiagnostics;
        private readonly IPresentationDiagnostics _diagnostics;
        private readonly Func<Id, string>? _textResolver;

        private readonly Dictionary<Id, List<FeedbackRule>> _rulesByEvent = new Dictionary<Id, List<FeedbackRule>>();
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();

        private readonly PlaybackQueue _queue;
        private readonly FloatingTextMerger _merger;
        private readonly HitFrameSyncPolicy? _hitFrameSyncPolicy;

        /// <summary>PR140-04 根治新增，攻击实例 id 遗留根治后降级为兜底路径（见
        /// <see cref="ResolveHitFrameBatchToken"/> 判断记录）：攻击者 -&gt; 当前仍打开着的命中帧同步
        /// 批次 token（见 <see cref="HitFrameSyncPolicy.WaitForHitFrame(Id, object, Action)"/> 判断
        /// 记录）。只在触发 <c>sync: hit_frame</c> 规则的事件不携带
        /// <see cref="Core.Rules.Common.EffectContext.AttackInstanceId"/>（如光环周期效果、纯脚本/
        /// 测试构造的事件，见该字段判断记录）时才会用到——同一个攻击者在这张表还留着条目期间触发的
        /// 每一次命中，都复用同一个 token 对象合并进同一批，按"该攻击者上一批是否已经释放完毕"这一
        /// 时序代理判断是否属于同一次攻击；<see cref="HitFrameSyncPolicy.BatchReleased"/> 触发时移除
        /// 对应条目，下一次同一攻击者命中会分配一个全新 token，开始下一批。已知局限（写清判断，不
        /// 假装本方案精确等价于真实攻击实例 id）：同一攻击者在上一批尚未释放前就发起第二次独立攻击
        /// （如极短 GCD 连续两次技能都命中同一批目标的命中帧到达之前），会被误合并成一批一起释放——
        /// 携带攻击实例 id 的事件（<c>combat.damage_dealt</c>/<c>combat.heal_done</c>，经
        /// <c>CastPipeline.ExecuteEffectsOnly</c> 产生）不再落进本兜底路径，见
        /// <see cref="ResolveHitFrameBatchToken"/>。</summary>
        private readonly Dictionary<Id, object> _hitFrameBatchTokenByAttacker = new Dictionary<Id, object>();

        public FeedbackBinder(
            IEventBus bus,
            IExprHostFactory exprHosts,
            IReadOnlyList<FeedbackRule> rules,
            IFeedbackSink sink,
            DisplayInfoResolver? displayInfoResolver = null,
            EntityLogicalIdResolver? entityLogicalIdResolver = null,
            IUnitAccess? unitAccess = null,
            FeedbackOptions? options = null,
            IExprDiagnostics? exprDiagnostics = null,
            IPresentationDiagnostics? diagnostics = null,
            Func<Id, string>? textResolver = null,
            IHitFrameSource? hitFrameSource = null)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _exprHosts = exprHosts ?? throw new ArgumentNullException(nameof(exprHosts));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _displayInfoResolver = displayInfoResolver;
            _entityLogicalIdResolver = entityLogicalIdResolver;
            _unitAccess = unitAccess;
            _options = options ?? new FeedbackOptions();
            _exprDiagnostics = exprDiagnostics ?? new ExprDiagnosticsRecorder();
            _diagnostics = diagnostics ?? new PresentationDiagnosticsRecorder();
            _textResolver = textResolver;

            // ADR-0017 决策 d：只有策略要求 AnimKeyframeDriven 且调用方确实注入了 IHitFrameSource 时
            // 才构造命中帧等待队列——未注入时（多数既有调用方/测试）行为与改动前完全一致，规则的
            // sync=hit_frame 声明被忽略，全部动作立即派发（见 OnEvent 判断记录）。
            if (_options.HitFrameSync == HitFrameSyncStrategy.AnimKeyframeDriven && hitFrameSource != null)
            {
                _hitFrameSyncPolicy = new HitFrameSyncPolicy(hitFrameSource, _options.HitFrameSyncTimeoutSeconds, _diagnostics);
                _hitFrameSyncPolicy.PendingChanged += TryPublishFinished;
                // PR140-04 根治：批次释放后清掉该攻击者当前打开的 token，见 _hitFrameBatchTokenByAttacker
                // 判断记录——下一次同一攻击者命中 sync: hit_frame 规则会拿到一个全新 token，不会误接到
                // 已经释放完的上一批。
                _hitFrameSyncPolicy.BatchReleased += OnHitFrameBatchReleased;
            }

            _queue = new PlaybackQueue(_options.SequentialStepSeconds) { Mode = _options.QueueMode };

            // 判断记录（GP-PRES-03 跟进：_merger 先于 _queue.Finished 订阅构造）：下面的订阅 lambda
            // 引用了 _merger，C# 闭包按变量捕获、lambda 体延迟求值，_merger 在订阅触发（PlaybackQueue
            // 播空）之前完成赋值即可，构造顺序本不影响正确性；这里仍把 _merger 的构造提前到订阅之前，
            // 只是为了消除阅读时"订阅引用了一个还没构造的字段"的疑虑，不改变任何运行期行为。
            _merger = new FloatingTextMerger(_options.MergeWindow, _options.NumberFormat, DispatchFloatingText);

            // PlaybackFinishedEvent 的发出条件：队列播空 **且** 没有仍停留在合并窗口内、尚未入队的
            // 待合并飘字，**且** sink 侧没有仍在首次加载中的冷 vfx/sfx（见 HasPendingPlayback 判断
            // 记录、TryPublishFinished 判断记录）。MergeWindow > 0 时，数值飘字先暂存在 _merger
            // 内部，窗口到期前 PlaybackQueue 可能因为"这一步只有非飘字动作先播完"而先由非空变空
            // 一次——此时不能认为整个离散步的表现已经播完，否则节奏门会在飘字真正播出前提前解除。
            // 窗口到期后 _merger.Flush 经 DispatchFloatingText 把合并结果送进队列，队列再次由非空变空
            // 时 Finished 会第二次触发，那一次 _merger.HasPendingMerges 已经是 false，才真正发布事件。
            //
            // N17 根治：队列清空这一刻 sink 侧仍可能有冷资源在加载（PlaySfx/PlayVfx 命中未加载完成
            // 的资源会立即返回、不阻塞队列，见 IFeedbackSink.HasPendingPlayback 判断记录），此前
            // 只看 _merger 会让节奏门在资源真正播出前就提前打开。现在 TryPublishFinished 一并核验
            // sink 侧，且额外订阅 _sink.PendingPlaybackChanged——冷资源真正加载完成那一刻（可能发生
            // 在队列早已清空之后，甚至发生在 Immediate 模式下——Immediate 模式的队列永远为空，
            // Queue.Finished 从不触发，PendingPlaybackChanged 是那一模式下唯一的完成信号来源）
            // 补一次完成检查，真正清空时才发出，不会重复发出（TryPublishFinished 是无状态的"当下
            // 是否全部清空"检查，只有真正从"有 pending"变成"全部清空"的那一次调用会通过全部三个
            // 条件，见该方法判断记录）。
            _queue.Finished += TryPublishFinished;
            _sink.PendingPlaybackChanged += TryPublishFinished;

            if (rules == null) throw new ArgumentNullException(nameof(rules));
            foreach (var rule in rules)
            {
                if (!_rulesByEvent.TryGetValue(rule.EventKey, out var list))
                {
                    list = new List<FeedbackRule>();
                    _rulesByEvent[rule.EventKey] = list;
                }
                list.Add(rule);
            }

            foreach (var kv in _rulesByEvent)
            {
                kv.Value.Sort((a, b) => string.CompareOrdinal(a.Id.Value, b.Id.Value));
                _subscriptions.Add(_bus.Subscribe(kv.Key, OnEvent));
            }
        }

        /// <summary>按 <paramref name="dt"/> 推进飘字合并窗口与 <see cref="QueueMode.Sequential"/>
        /// 播放队列。</summary>
        public void Update(double dt)
        {
            _merger.Update(dt);
            _queue.Update(dt);
            _hitFrameSyncPolicy?.Update(dt);
        }

        /// <summary>供离散模式主循环/测试直接控制播放节奏（09 第 6.4 节"加速与跳过"）。</summary>
        public PlaybackQueue Queue => _queue;

        /// <summary>当前离散步是否还有尚未回放完的表现动作：播放队列非空，或仍有停留在合并窗口、
        /// 尚未入队的数值飘字。
        /// <para>
        /// 判断记录（GP-PRES-03 跟进，统一查询）：命名与 <see cref="Core.Foundation.SimLoop.
        /// WaitForPlaybackPacingPolicy.HasPendingPlayback"/> 探针对齐——本属性就是
        /// <see cref="Core.Gameplay.Assembly.GameplayAssembly.SetPendingPlaybackProbe"/> 应该接入的
        /// 那个统一查询。此前 <c>PresentationAssembly</c> 直接接 <c>() => Queue.PendingCount &gt; 0</c>，
        /// 在 <see cref="FeedbackOptions.MergeWindow"/> &gt; 0 时会漏看仍停留在
        /// <see cref="FloatingTextMerger"/>（经 <see cref="FloatingTextMerger.HasPendingMerges"/>）里、
        /// 尚未进 <see cref="Queue"/> 的数值飘字——队列此刻可能恰好为空（这一步没有其它动作，或其它
        /// 动作已播完），探针因此误判"没有待回放内容"而提前放行节奏门，飘字还没播出就已经解除等待。
        /// 装配根、调用方一律应改用本属性作为节奏门探针，不要再直接用 <see cref="PlaybackQueue.
        /// PendingCount"/>。
        /// </para>
        /// <para>
        /// GP-09 根治补充：早前的实现只看 <see cref="Queue"/>/<see cref="FloatingTextMerger"/> 两处，
        /// 漏了 <see cref="IFeedbackSink.HasPendingPlayback"/>——<c>play_vfx</c>/<c>play_sfx</c> 首次
        /// 引用未加载完成的资源时会排队等待（见 <c>Presentation.VfxSfx.Core.VfxPlayer.Spawn</c>/
        /// <c>SfxPlayer.Play</c> 判断记录），既不会同步产生任何"这一步已经播完"的信号，也不经过本类
        /// 的 <see cref="Queue"/>（<see cref="IFeedbackSink.PlayVfx"/>/<see cref="IFeedbackSink.
        /// PlaySfx"/> 调用本身是"发指令"，不是"等播完"，见类型注释"本模块只发指令"）——冷资源的
        /// vfx/sfx 因此会被误判为"这一步没有待回放内容"而提前放行节奏门，真正的播放效果可能在
        /// 下一步甚至后续 vfx/sfx 之间乱序才姗姗来迟。现在把 sink 侧的 pending 信号一并纳入。
        /// </para>
        /// <para>
        /// ADR-0017 决策 d 补充：等待攻击方命中帧释放的动作（<see cref="_hitFrameSyncPolicy"/>）同样
        /// 计入——这些动作已经确定要播放，只是延后到命中帧才真正入队，未入队期间同样属于"这一步还有
        /// 待回放内容"。
        /// </para>
        /// </summary>
        public bool HasPendingPlayback =>
            _queue.PendingCount > 0 || _merger.HasPendingMerges || _sink.HasPendingPlayback || (_hitFrameSyncPolicy?.PendingCount ?? 0) > 0;

        /// <summary>H5b 根治新增（游戏侧复核发现 2）：只读诊断，转发自
        /// <see cref="HitFrameSyncPolicy.LastReleaseReason"/>——命中帧同步等待队列最近一次批次释放
        /// 究竟是因为命中帧事件真正到达，还是超时兜底。未启用命中帧同步（<see cref="_hitFrameSyncPolicy"/>
        /// 未构造，见构造函数判断记录）或尚未发生过任何一次释放时为 <c>null</c>。供测试断言"确实经命中
        /// 帧路径释放，超时兜底没有偷偷掩盖命中帧链路本身的缺口"（见
        /// <c>Tests/Runtime/HitFrameSyncEndToEndTests.cs</c>）。</summary>
        public HitFrameSyncReleaseReason? LastHitFrameSyncReleaseReason => _hitFrameSyncPolicy?.LastReleaseReason;

        /// <summary>
        /// N17 根治：三个"这一步是否已经真正播完"的条件同时满足才发出
        /// <see cref="PlaybackFinishedEvent"/>——队列已空、没有待合并飘字、sink 侧没有仍在首次
        /// 加载中的冷 vfx/sfx。本方法是无状态的"当下检查"，由三条独立路径调用：
        /// <see cref="PlaybackQueue.Finished"/>（队列由非空变空那一刻）、<see cref="IFeedbackSink.
        /// PendingPlaybackChanged"/>（sink 侧 pending 计数可能变化那一刻，见该事件判断记录）、
        /// ADR-0017 决策 d 新增的 <see cref="HitFrameSyncPolicy.PendingChanged"/>（命中帧等待队列
        /// 计数可能变化那一刻）。只有真正四个条件（含 <see cref="HasPendingPlayback"/> 已并入的命中帧
        /// 等待项）同时成立的那一次调用会实际发布事件——其余调用都会在条件判断处提前返回，因此不会
        /// 重复发出。
        /// </summary>
        private void TryPublishFinished()
        {
            if (_queue.PendingCount == 0 && !_merger.HasPendingMerges && !_sink.HasPendingPlayback && (_hitFrameSyncPolicy?.PendingCount ?? 0) == 0)
            {
                _bus.PublishImmediate(new PlaybackFinishedEvent());
            }
        }

        /// <summary>PR140-04 根治新增：见 <see cref="_hitFrameBatchTokenByAttacker"/> 判断记录。</summary>
        private void OnHitFrameBatchReleased(Id attackerEntityId) => _hitFrameBatchTokenByAttacker.Remove(attackerEntityId);

        public void Dispose()
        {
            _sink.PendingPlaybackChanged -= TryPublishFinished;
            if (_hitFrameSyncPolicy != null)
            {
                _hitFrameSyncPolicy.PendingChanged -= TryPublishFinished;
                _hitFrameSyncPolicy.BatchReleased -= OnHitFrameBatchReleased;
                _hitFrameSyncPolicy.Dispose();
            }

            foreach (var sub in _subscriptions)
            {
                sub.Dispose();
            }
            _subscriptions.Clear();
        }

        private void OnEvent(IEvent evt)
        {
            if (!_rulesByEvent.TryGetValue(evt.Key, out var rules))
            {
                return;
            }

            var selfId = ExtractId(evt, "sourceId", "casterId", "unitId") ?? RulesExprHostFactory.NoneId;
            var targetId = ExtractId(evt, "targetId");
            var host = _exprHosts.CreateFor(selfId, targetId, evt);

            // PR130-04 根治（取代此前"每条规则各自调用一次 WaitForHitFrame"的做法）：本次 OnEvent 命中
            // 的全部 sync=hit_frame 规则的动作先累积进 hitFrameBatch，循环结束后只调用一次
            // HitFrameSyncPolicy.WaitForHitFrame——同一个 combat.damage_dealt 一类事件命中多条
            // sync=hit_frame 规则时（如暴击/普通两条规则各自命中，见任务书复现场景"两条同命中规则"），
            // 此前每条规则各自入队成一个独立的 HitFrameSyncPolicy.PendingEntry，而该策略每次命中帧只
            // 释放同一实体最早入队的那一条（见 HitFrameSyncPolicy.OnHitFrameReached 判断记录"多次攻击
            // 不串扰"——这条策略本身的行为没有变化、仍然保留，用于区分"确实不同的两次攻击"），导致第二
            // 条规则的动作错过这一次命中帧、要么串到下一次攻击的命中帧、要么等到超时兜底才播放。改为
            // 同一次 OnEvent（即同一个逻辑事件）触发的全部 sync=hit_frame 规则合并成一个批次、只登记
            // 一个 PendingEntry，使它们作为一个整体随同一次命中帧同时释放（"同一事件的全部动作作为一个
            // 批次"）；不是 sync=hit_frame 的规则、以及非命中帧同步的调用路径均不受影响，仍然立即派发。
            // PR140-04 根治（取代上一段注释描述的旧状态）：范围攻击对多个目标各自产生独立的
            // combat.damage_dealt 事件（见 CombatDamageDealtEvent 单源单目标的既有形状），此前各自经
            // 本方法独立的一次 OnEvent 调用登记为各自独立的 HitFrameSyncPolicy 批次，而该策略每次命中
            // 帧只释放同一实体最早入队的那一批——同一次攻击命中的目标 2、3……要么错到下一次命中帧、要么
            // 只能等超时兜底，不是同一次攻击应有的"同时释放"效果。
            // 攻击实例 id 遗留根治（architecture/落地计划/audit-3224ca1-20260908/AUDIT_REPORT.md
            // "攻击实例 id"）：批次 token 现按 ResolveHitFrameBatchToken 决定——事件携带
            // EffectContext.AttackInstanceId（经 CastPipeline.ExecuteEffectsOnly 产生的
            // combat.damage_dealt/combat.heal_done 均携带）时直接用它的装箱值当 token，
            // HitFrameSyncPolicy 按值比较（见该类型判断记录），同一实例 id 的多个目标天然合批、不同
            // 实例 id（哪怕落在同一个未释放窗口内）天然不合批，不再需要 _hitFrameBatchTokenByAttacker
            // 这张"是否还有未释放批次"的时序代理表；只有不携带攻击实例 id 的事件才退回该表兜底（见
            // ResolveHitFrameBatchToken/_hitFrameBatchTokenByAttacker 判断记录）。
            List<FeedbackAction>? hitFrameBatch = null;

            foreach (var rule in rules)
            {
                if (rule.Condition != null && !ExprEvaluator.EvaluateBool(rule.Condition, host, _exprDiagnostics))
                {
                    continue;
                }

                if (_hitFrameSyncPolicy != null && rule.Sync == FeedbackSyncMode.HitFrame)
                {
                    hitFrameBatch ??= new List<FeedbackAction>();
                    hitFrameBatch.AddRange(rule.Actions);
                    continue;
                }

                foreach (var action in rule.Actions)
                {
                    Dispatch(action, evt, selfId, targetId);
                }
            }

            if (hitFrameBatch != null)
            {
                var actionsSnapshot = hitFrameBatch;
                var batchToken = ResolveHitFrameBatchToken(evt, selfId);

                _hitFrameSyncPolicy!.WaitForHitFrame(selfId, batchToken, () =>
                {
                    foreach (var action in actionsSnapshot)
                    {
                        Dispatch(action, evt, selfId, targetId);
                    }
                });
            }
        }

        /// <summary>攻击实例 id 遗留根治（<c>architecture/落地计划/audit-3224ca1-20260908/AUDIT_REPORT.md</c>
        /// "攻击实例 id"）：<paramref name="evt"/> 携带 <c>attackInstanceId</c> 字段（见
        /// <see cref="Core.Rules.Common.EffectContext.AttackInstanceId"/>/
        /// <see cref="Core.Rules.Common.CombatDamageDealtEvent.AttackInstanceId"/> 判断记录）时，直接
        /// 用它的装箱值作为 <see cref="HitFrameSyncPolicy"/> 的批次 token——该策略现按值比较
        /// （见其类型判断记录），同一实例 id 的多次调用天然合批，不同实例 id 天然不合批，不需要本类型
        /// 再维护"该攻击者当前是否还有未释放批次"这张时序代理表。没有该字段（如光环周期效果、纯脚本/
        /// 测试构造的事件）时退回 <see cref="_hitFrameBatchTokenByAttacker"/> 窗口合批兜底，并在每次
        /// 新开一批（不是复用既有未释放批次）时记一次诊断——诊断只在"新分配一个兜底 token"这一刻触发
        /// 一次，不会随同一批次内后续每个命中重复刷屏。</summary>
        private object ResolveHitFrameBatchToken(IEvent evt, Id selfId)
        {
            if (evt is IExprReadableEvent readable
                && readable.TryGetField("attackInstanceId", out var value)
                && value.Kind == ExprValueKind.Id)
            {
                return value.AsId;
            }

            if (!_hitFrameBatchTokenByAttacker.TryGetValue(selfId, out var fallbackToken))
            {
                fallbackToken = new object();
                _hitFrameBatchTokenByAttacker[selfId] = fallbackToken;
                _diagnostics.Warn(
                    $"命中帧同步：事件 \"{evt.Key}\" 未携带攻击实例 id（attackInstanceId），" +
                    $"按攻击者 \"{selfId}\" 当前未释放窗口退回合批——同一窗口内如果这个攻击者恰好还有" +
                    "另一次独立攻击尚未释放，两者会被误合并成一批，见 FeedbackBinder 判断记录。");
            }

            return fallbackToken;
        }

        private void Dispatch(FeedbackAction action, IEvent evt, Id selfId, Id? targetId)
        {
            switch (action)
            {
                case FloatingTextAction floatingText:
                    DispatchFloatingTextAction(floatingText, evt, selfId, targetId);
                    break;

                case PlayVfxAction playVfx:
                    DispatchPlayVfx(playVfx, evt, selfId, targetId);
                    break;

                case PlaySfxAction playSfx:
                    DispatchPlaySfx(playSfx, evt, selfId, targetId);
                    break;

                case FreezeAction freeze:
                    _queue.Enqueue(() => _sink.Freeze(freeze.DurationMs));
                    break;

                case ShakeCameraAction shake:
                    _queue.Enqueue(() => _sink.ShakeCamera(shake.ProfileId));
                    break;

                case FlashAction flash:
                    DispatchFlash(flash, selfId, targetId);
                    break;
            }
        }

        // ------------------------------------------------------------------
        // floating_text
        // ------------------------------------------------------------------

        /// <summary>飘字挂在哪个实体上：09 第 6.1 节 <c>FloatingText(styleId, textSource)</c> 伪代码
        /// 未携带挂接实体，本模块判断记录：优先挂在 <c>targetId</c>（承受效果的一方，飘字最常见的
        /// 展示位置——如受击目标头顶的伤害数字），缺 <c>targetId</c> 时退回 <c>selfId</c>（如没有
        /// 目标概念的自身资源变化提示）。</summary>
        private void DispatchFloatingTextAction(FloatingTextAction action, IEvent evt, Id selfId, Id? targetId)
        {
            var entityId = targetId ?? selfId;

            switch (action.TextSource.Kind)
            {
                case TextSourceKind.Amount:
                {
                    if (!TryGetEventNumber(evt, "amount", out var amount))
                    {
                        _diagnostics.Warn($"feedback 规则 floating_text：事件 \"{evt.Key}\" 没有可用的 amount 字段，跳过");
                        return;
                    }
                    _merger.Offer(entityId, action.StyleId, amount, _options.MergeMode);
                    return;
                }

                case TextSourceKind.Field:
                {
                    if (!TryGetEventText(evt, action.TextSource.FieldName!, out var text))
                    {
                        _diagnostics.Warn($"feedback 规则 floating_text：事件 \"{evt.Key}\" 没有字段 \"{action.TextSource.FieldName}\"，跳过");
                        return;
                    }
                    _merger.OfferImmediate(entityId, action.StyleId, text);
                    return;
                }

                case TextSourceKind.Literal:
                {
                    // 缺口 7 恢复：09 第 7.3 节"文案一律经本地化表用 key 间接引用"同样约束飘字文本——
                    // _textResolver 未注入（历史默认，见 feedback_binder/README.md 契约缺口）时保留
                    // 此前"直接用文本键原文占位"的退化行为，不阻断装配；注入后（PresentationAssembly
                    // 默认接 IL10nHost.Text，见该类型判断记录）经本地化表解析出真正文案。
                    var textKey = action.TextSource.TextKey!.Value;
                    var text = _textResolver != null ? _textResolver(textKey) : textKey.Value;
                    _merger.OfferImmediate(entityId, action.StyleId, text);
                    return;
                }
            }
        }

        private void DispatchFloatingText(Id entityId, Id styleId, string text) =>
            _queue.Enqueue(() => _sink.FloatingText(entityId, styleId, text));

        // ------------------------------------------------------------------
        // play_vfx / play_sfx
        // ------------------------------------------------------------------

        private void DispatchPlayVfx(PlayVfxAction action, IEvent evt, Id selfId, Id? targetId)
        {
            var vfxId = ResolveDisplayVfxOrSfxId(action.VfxId, action.FromDisplay, evt, selfId, targetId, isVfx: true);
            if (vfxId == null)
            {
                return;
            }

            FeedbackAttachSpec spec;
            switch (action.Attach)
            {
                case FeedbackAttachTarget.World:
                    spec = new FeedbackAttachSpec(FeedbackAttachTarget.World, null, null, null);
                    break;

                case FeedbackAttachTarget.Source:
                    spec = FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Source, selfId, action.AnchorId);
                    break;

                case FeedbackAttachTarget.Target:
                    if (!targetId.HasValue)
                    {
                        _diagnostics.Warn($"feedback 规则 play_vfx：动作声明 attach=target 但事件 \"{evt.Key}\" 没有 targetId，跳过");
                        return;
                    }
                    spec = FeedbackAttachSpec.ForEntity(FeedbackAttachTarget.Target, targetId.Value, action.AnchorId);
                    break;

                default:
                    return;
            }

            _queue.Enqueue(() => _sink.PlayVfx(vfxId.Value, spec));
        }

        private void DispatchPlaySfx(PlaySfxAction action, IEvent evt, Id selfId, Id? targetId)
        {
            var sfxId = ResolveDisplayVfxOrSfxId(action.SfxId, action.FromDisplay, evt, selfId, targetId, isVfx: false);
            if (sfxId == null)
            {
                return;
            }

            _queue.Enqueue(() => _sink.PlaySfx(sfxId.Value, null));
        }

        /// <summary>统一解析 <c>vfx_id?|from_display</c>（<c>play_vfx</c>）与
        /// <c>sfx_id?|from_display</c>（<c>play_sfx</c>）二选一（见 09 第 6.1 节）：字面 id 优先；
        /// <c>from_display: skill</c> 经事件的 <c>skillId</c>/<c>auraDefId</c> 字段查
        /// <see cref="DisplayInfoResolver"/>（任务书拍板路径）；<c>source</c>/<c>target</c> 经
        /// <see cref="ResolveEntityLogicalId"/>（P4-2 起默认改用 <see cref="Core.Rules.Common.IUnitAccess.GetTemplateId"/>，
        /// 见该方法判断记录），查不到时记诊断并跳过整个动作。</summary>
        private Id? ResolveDisplayVfxOrSfxId(Id? literalId, FromDisplaySource? fromDisplay, IEvent evt, Id selfId, Id? targetId, bool isVfx)
        {
            if (literalId.HasValue)
            {
                return literalId;
            }

            if (_displayInfoResolver == null)
            {
                _diagnostics.Warn("feedback 规则使用了 from_display，但未注入 DisplayInfoResolver，跳过该动作");
                return null;
            }

            Id? logicalId = fromDisplay switch
            {
                FromDisplaySource.Skill => ExtractId(evt, "skillId", "auraDefId"),
                FromDisplaySource.Source => ResolveEntityLogicalId(selfId),
                FromDisplaySource.Target => targetId.HasValue ? ResolveEntityLogicalId(targetId.Value) : null,
                _ => null,
            };

            if (logicalId == null)
            {
                _diagnostics.Warn($"feedback 规则 from_display={fromDisplay}：查不到对应的逻辑 id（事件 \"{evt.Key}\"），跳过该动作");
                return null;
            }

            var resolved = isVfx ? _displayInfoResolver.ResolveVfx(logicalId.Value) : _displayInfoResolver.ResolveSfx(logicalId.Value);
            if (resolved == null)
            {
                _diagnostics.Warn($"feedback 规则 from_display={fromDisplay}：逻辑 id \"{logicalId}\" 在 display.map 未声明 {(isVfx ? "vfx_id" : "sfx_id")}，跳过该动作");
            }
            return resolved;
        }

        /// <summary>
        /// 按运行期实体 id 取其"逻辑 id"（技能/光环/物品/生物模板 id），供
        /// <see cref="FromDisplaySource.Source"/>/<see cref="FromDisplaySource.Target"/> 使用（见
        /// <see cref="EntityLogicalIdResolver"/> 类型注释）。
        /// <para>
        /// 判断记录（P4-2 契约缺口最小修补）：<see cref="Core.Rules.Common.IUnitAccess"/> 契约本身已
        /// 补上 <c>GetTemplateId(Id unitId): Id?</c>（05 第 1.1 节 <c>Entity.templateId</c>），本模块
        /// 原先记录的"契约清单没有提供实体 id → 模板 id 映射"这一契约缺口已不存在——默认经注入的
        /// <see cref="_unitAccess"/> 直接取模板 id，不再强依赖单独的 <see cref="EntityLogicalIdResolver"/>
        /// 委托；<see cref="_entityLogicalIdResolver"/> 仍保留为可选覆盖（优先于
        /// <see cref="_unitAccess"/>），供需要与"模板 id"不同的映射规则（例如按运行期外形覆盖而非
        /// 出生模板）的具体游戏接入时使用。两者都未注入时记诊断并跳过该动作，与此前行为一致。
        /// </para>
        /// </summary>
        private Id? ResolveEntityLogicalId(Id entityId)
        {
            if (_entityLogicalIdResolver != null)
            {
                return _entityLogicalIdResolver(entityId);
            }

            if (_unitAccess != null)
            {
                return _unitAccess.GetTemplateId(entityId);
            }

            return null;
        }

        // ------------------------------------------------------------------
        // flash
        // ------------------------------------------------------------------

        private void DispatchFlash(FlashAction action, Id selfId, Id? targetId)
        {
            Id entityId;
            if (action.Target == FeedbackAttachTarget.Source)
            {
                entityId = selfId;
            }
            else
            {
                if (!targetId.HasValue)
                {
                    _diagnostics.Warn("feedback 规则 flash：动作声明 target=target 但事件没有 targetId，跳过");
                    return;
                }
                entityId = targetId.Value;
            }

            _queue.Enqueue(() => _sink.Flash(entityId, action.ProfileId));
        }

        // ------------------------------------------------------------------
        // 事件字段提取
        // ------------------------------------------------------------------

        private static Id? ExtractId(IEvent evt, params string[] fieldNames)
        {
            if (!(evt is IExprReadableEvent readable))
            {
                return null;
            }

            foreach (var name in fieldNames)
            {
                if (readable.TryGetField(name, out var value) && value.Kind == ExprValueKind.Id)
                {
                    return value.AsId;
                }
            }
            return null;
        }

        private static bool TryGetEventNumber(IEvent evt, string field, out double value)
        {
            if (evt is IExprReadableEvent readable && readable.TryGetField(field, out var v) && v.IsNumeric)
            {
                value = v.ToDouble();
                return true;
            }
            value = 0;
            return false;
        }

        private static bool TryGetEventText(IEvent evt, string field, out string text)
        {
            if (evt is IExprReadableEvent readable && readable.TryGetField(field, out var v))
            {
                text = v.ToString();
                return true;
            }
            text = string.Empty;
            return false;
        }
    }
}
