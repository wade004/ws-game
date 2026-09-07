using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.Rng;
using Core.Rules.Common;

namespace Core.Rules.Skill
{
    /// <summary>
    /// Proc 触发系统（见 06 第 3.4 节、落地方案 T2-6 行）：<see cref="Attach"/> 由
    /// <see cref="AuraHost"/> 在施加带 <c>proc_trigger</c> 效果的光环时调用，订阅
    /// <see cref="ProcDef.TriggerEvent"/>；事件到达时按"持有者相关性 → 内部冷却就绪 → 条件 Expr →
    /// 命中率"顺序判定，命中则经调用方注入的 <paramref name="triggerCast"/> 回调触发释放
    /// （绕过读条与 GCD、不入队列，见 <see cref="CastPipeline.TriggerCast"/>），并发出
    /// <c>proc.triggered</c>。
    /// </summary>
    public sealed class ProcHost
    {
        /// <summary>供 <see cref="ProcHost"/> 触发释放的回调：由 <see cref="SkillHost"/> 注入，
        /// 转调 <see cref="CastPipeline.TriggerCast"/>（绕过读条与 GCD、不入队列）。
        /// <para>
        /// RC-01 收边勘误：触发链递归深度上限（见 <see cref="SkillOptions.MaxTriggerDepth"/>）不再
        /// 由 <see cref="CastPipeline"/> 内部一个 ambient 计数器维护——该计数器只在同一次调用栈的
        /// 同步嵌套（<c>trigger_spell</c> 效果原语的直接方法调用）里可靠，<see cref="ProcHost"/>
        /// 由 <c>combat.damage_dealt</c>/<c>combat.heal_done</c> 一类经
        /// <see cref="Core.Foundation.EventBus.IEventBus.Enqueue"/> 异步入队、下一个
        /// <see cref="Core.Foundation.EventBus.IEventBus.DispatchPending"/> pass 才派发的事件触发时，
        /// ambient 计数器早已归零，深度预算对这条路径完全失效（见 <see cref="CastPipeline"/> 类型
        /// 注释、审计 RC-01）。<paramref name="chainDepth"/> 现在显式携带"触发本次调用的深度"——
        /// <see cref="OnEvent"/> 从触发事件本身读回（见 <see cref="EventCorrelation.GetTriggerChainDepth"/>
        /// 与 <see cref="ITriggerChainEvent"/>），<see cref="EffectDispatcher.ApplyTriggerSpell"/>
        /// 则从当前 <see cref="EffectContext.TriggerChainDepth"/> 读回，两条触发路径（事件驱动 vs.
        /// 直接方法调用）经由同一个显式参数共用同一套深度预算判定，不再需要 ambient 状态。
        /// </para>
        /// 返回是否触发成功（深度超限、技能不存在等均返回 false）。</summary>
        public delegate bool TriggerCastCallback(Id casterId, Id skillId, IReadOnlyList<Id> targets, int chainDepth);

        private sealed class Attachment
        {
            public Id InstanceId = default!;
            public Id HolderId;
            public ProcDef Def = default!;
            public double IcdRemaining;
            public SubscriptionHandle? Subscription;
        }

        private readonly IEventBus _bus;
        private readonly IRngHost _rng;
        private readonly IExprHostFactory _exprHosts;
        private readonly SkillOptions _options;
        private readonly ISkillDiagnostics _diagnostics;
        private readonly TriggerCastCallback _triggerCast;

        private readonly Dictionary<Id, Attachment> _attachments = new Dictionary<Id, Attachment>();

        public ProcHost(
            IEventBus bus,
            IRngHost rng,
            IExprHostFactory exprHosts,
            SkillOptions options,
            ISkillDiagnostics diagnostics,
            TriggerCastCallback triggerCast)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            _exprHosts = exprHosts ?? throw new ArgumentNullException(nameof(exprHosts));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _triggerCast = triggerCast ?? throw new ArgumentNullException(nameof(triggerCast));
        }

        public void Attach(Id instanceId, Id holderId, ProcDef def)
        {
            var attachment = new Attachment { InstanceId = instanceId, HolderId = holderId, Def = def };
            attachment.Subscription = _bus.Subscribe(def.TriggerEvent, evt => OnEvent(attachment, evt));
            _attachments[instanceId] = attachment;
        }

        public void Detach(Id instanceId)
        {
            if (_attachments.TryGetValue(instanceId, out var attachment))
            {
                attachment.Subscription?.Dispose();
                _attachments.Remove(instanceId);
            }
        }

        /// <summary>按 <paramref name="dt"/> 推进全部已挂载触发器的内部冷却。</summary>
        public void Update(double dt)
        {
            foreach (var attachment in _attachments.Values)
            {
                if (attachment.IcdRemaining > 0)
                {
                    attachment.IcdRemaining = Math.Max(0, attachment.IcdRemaining - dt);
                }
            }
        }

        private void OnEvent(Attachment attachment, IEvent evt)
        {
            if (!EventCorrelation.IsRelatedToHolder(evt, attachment.HolderId))
            {
                return;
            }

            if (attachment.IcdRemaining > 0)
            {
                return;
            }

            if (attachment.Def.Condition != null)
            {
                var host = _exprHosts.CreateFor(attachment.HolderId, null, evt);
                var diagnostics = new ExprDiagnosticsRecorder();
                if (!ExprEvaluator.EvaluateBool(attachment.Def.Condition, host, diagnostics))
                {
                    return;
                }
            }

            var roll = _rng.Next(_options.RngStream);
            if (roll >= attachment.Def.ProcChance)
            {
                return;
            }

            if (attachment.Def.InternalCooldown.HasValue)
            {
                attachment.IcdRemaining = attachment.Def.InternalCooldown.Value;
            }

            // RC-01 收边补齐：depth 读自触发本次判定的事件本身（见 TriggerCastCallback 类型
            // 注释），不再依赖 CastPipeline 内部已对跨 pass 派发失效的 ambient 计数器。
            var chainDepth = EventCorrelation.GetTriggerChainDepth(evt);
            var triggered = _triggerCast(attachment.HolderId, attachment.Def.TriggerSkill, Array.Empty<Id>(), chainDepth);
            if (triggered)
            {
                _bus.Enqueue(new ProcTriggeredEvent(attachment.HolderId, attachment.Def.Id, attachment.Def.TriggerSkill, chainDepth + 1));
            }
        }
    }
}
