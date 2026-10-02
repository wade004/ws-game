using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;

namespace Core.Foundation.InputMap
{
    /// <summary>
    /// 把输入缓冲接进 tick 管线步骤 1（<see cref="TickPhase.IntentCollection"/>，手感设计/01 第 2.3 节、00 第 3 节数据流）：
    /// 在输入采样（宿主每固定步先调 <c>InputMapHost.Update</c>，边沿已交给缓冲）之后、动作裁决（步骤 3 技能管线消费 <c>cast</c> 意图）之前，
    /// 依次做：缓冲维护（<see cref="InputBufferHost.BeginTick"/>）→ 宽限采样（<see cref="GraceTracker.Sample"/>）→ 对每个有候选记录的
    /// 行动者向动作层（<see cref="IBufferedIntentSink"/>）询问能否接受，接受则把记录标记为已消费、把动作层给出的意图追加进本 tick 的
    /// 意图列表（<see cref="IWorldSim.AppendCurrentIntent"/>）。
    /// <para>
    /// 判断记录（离散模式整体不生效）：手感设计/00 第 7 节——离散模式下缓冲槽不消费（落到既有的法术队列语义），所以离散步一律清空缓冲
    /// 并直接返回；连续步才工作。
    /// </para>
    /// <para>
    /// 判断记录（sink 缺省为空）：没有动作层实现（没有取消窗口、没有"动作 → 技能"映射）时只做缓冲维护与宽限采样，不生成意图——
    /// 动作层也可以不经本处理器，自己在步骤 3 里经 <see cref="IInputBufferQuery"/> 取用（动作时间线的取消窗口就是这样用）；两种用法
    /// 共用同一个 <see cref="InputBufferHost"/>，同一 tick 内每行动者至多取用一条的约束由缓冲保证。
    /// </para>
    /// <para>
    /// 判断记录（行动者遍历顺序）：按缓冲建立的先后顺序遍历，保证意图追加顺序确定。
    /// </para>
    /// </summary>
    public sealed class InputBufferTickHandler : ITickPhaseHandler
    {
        private readonly InputBufferHost _host;
        private readonly IBufferedIntentSink? _sink;
        private readonly GraceTracker? _grace;

        public InputBufferTickHandler(InputBufferHost host, IBufferedIntentSink? sink = null, GraceTracker? grace = null)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _sink = sink;
            _grace = grace;
        }

        /// <summary>登记到世界的 <see cref="TickPhase.IntentCollection"/> 阶段（应在其它步骤 1 处理器之前登记，使缓冲先于它们工作）。</summary>
        public static InputBufferTickHandler Register(
            IWorldSim world, InputBufferHost host, IBufferedIntentSink? sink = null, GraceTracker? grace = null)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            var handler = new InputBufferTickHandler(host, sink, grace);
            world.RegisterPhaseHandler(TickPhase.IntentCollection, handler);
            return handler;
        }

        public void Execute(SimStep step, IWorldSim world)
        {
            if (step.Kind == SimStepKind.Discrete)
            {
                _host.ClearAll();
                return;
            }

            _host.BeginTick();
            var actors = _host.ActorIds;
            if (_grace != null)
            {
                // 手感落地 M2-B：有宽限条件声明时，对每个有缓冲的行动者（以及本地绑定的行动者——第一次按键之前就要开始采样）
                // 登记全部宽限条件名再采样；Register 对重复登记无效果。
                var names = _host.GraceConditionNames;
                if (names.Count > 0)
                {
                    var local = _host.LocalActorId;
                    if (local.HasValue) _grace.Register(local.Value, names);
                    for (var i = 0; i < actors.Count; i++) _grace.Register(actors[i], names);
                }

                _grace.Sample(_host.CurrentTick);
            }

            if (_sink == null) return;

            for (var i = 0; i < actors.Count; i++)
            {
                var actorId = actors[i];
                if (!_host.TryPeek(actorId, out var top)) continue;
                if (!_sink.TryAccept(actorId, top, out var intent)) continue;

                // 候选在 TryPeek 与 TryConsume 之间没有变化，取用到的就是刚才询问过的那一条。
                if (_host.TryConsume(actorId, null, out _))
                {
                    world.AppendCurrentIntent(intent);
                }
            }
        }
    }
}
