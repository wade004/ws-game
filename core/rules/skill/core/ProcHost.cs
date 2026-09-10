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
        /// <summary>
        /// CR130-03 根治（外部审计 audit-5c444f1-20260908）：与
        /// <see cref="CooldownTracker._currentFactor"/>/<see cref="AuraHost._currentFactor"/>/
        /// <see cref="CastPipeline._currentFactor"/> 同批语义、同一套推导——当前模式 1 个计时单位相当于
        /// 连续模式（数据 authoring 的规范单位）多少秒；初始 1.0，随 <see cref="RescaleAll"/> 每次模式
        /// 切换累乘更新。<see cref="OnEvent"/> 把 <c>ProcDef.InternalCooldown</c>（authoring 规范秒数）
        /// 写入 <see cref="Attachment.IcdRemaining"/> 前先乘该系数；<see cref="RescaleAll"/> 同时把
        /// 已经在倒计时的既有 ICD 按 <c>factor</c> 换算——此前本类型完全没有接入 R05/第五轮外部审核那
        /// 一批时间模式折算（<c>SkillHost.OnTimeModelRescaled</c> 当时只广播给 CooldownTracker/
        /// AuraHost/CastPipeline 三者，见各自判断记录），Proc 的内部冷却在连续/离散模式切换后会用
        /// 错误的单位重新解读，与技能自身冷却各用各的时间基准。
        /// </summary>
        private double _currentFactor = 1.0;


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

        /// <summary>消费方反馈 2026-09-10（同一光环多个 Proc 触发器静默忽略问题，见
        /// architecture/落地计划/消费方反馈-2026-09-10-多Proc触发器.md）根治：此前是
        /// <c>Dictionary&lt;Id, Attachment&gt;</c>——以 <c>instanceId</c> 为单值键的单槽存储，
        /// 同一光环实例第二次 <see cref="Attach"/> 会直接覆盖第一次的订阅（旧订阅的
        /// <see cref="SubscriptionHandle"/> 从未 <c>Dispose</c>，是另一层订阅泄漏），只有最后一次
        /// 挂载的触发器真正生效。改为按 <c>instanceId</c> 分桶的多槽列表——<see cref="AuraHost"/>
        /// 对同一光环实例登记的每个 <c>proc_trigger</c> 各调一次 <see cref="Attach"/>，本类不再
        /// 假设"一个光环实例最多一个触发器"，同一实例下的多个 <see cref="Attachment"/> 各自持有
        /// 独立的 <see cref="Attachment.IcdRemaining"/>（内部冷却独立计时）与独立的
        /// <see cref="SubscriptionHandle"/>（独立订阅、独立摘除）。</summary>
        private readonly Dictionary<Id, List<Attachment>> _attachments = new Dictionary<Id, List<Attachment>>();

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

        /// <summary>挂载一个 <c>proc_trigger</c> 触发器：<paramref name="instanceId"/> 相同、
        /// <paramref name="def"/>（按 <see cref="ProcDef.Id"/>）不同的多次调用各自独立生效（见
        /// <see cref="_attachments"/> 判断记录），不会互相覆盖。<see cref="AuraHost"/> 保证同一光环
        /// 实例不会用相同 <c>procDef.Id</c> 调用本方法两次（加载期
        /// <see cref="AuraProcTriggerDuplicateRule"/> 已拒绝同一光环内重复 <c>proc_ref</c>）。</summary>
        public void Attach(Id instanceId, Id holderId, ProcDef def)
        {
            var attachment = new Attachment { InstanceId = instanceId, HolderId = holderId, Def = def };
            attachment.Subscription = _bus.Subscribe(def.TriggerEvent, evt => OnEvent(attachment, evt));

            if (!_attachments.TryGetValue(instanceId, out var list))
            {
                list = new List<Attachment>();
                _attachments[instanceId] = list;
            }

            list.Add(attachment);
        }

        /// <summary>摘除 <paramref name="instanceId"/> 挂载的全部触发器（语义变更：此前单槽存储下
        /// 等价于"摘除唯一一个"，见 <see cref="_attachments"/> 判断记录）。<see cref="AuraHost"/> 的
        /// 全部实例移除路径（到期、<c>RemoveAura</c>、<c>Dispel</c>、吸收耗尽、叠加溢出替换、目标
        /// 销毁）统一调用本方法一次即完整注销该实例名下的全部触发器订阅，不会有孤儿条目残留。</summary>
        public void Detach(Id instanceId)
        {
            if (_attachments.TryGetValue(instanceId, out var list))
            {
                foreach (var attachment in list)
                {
                    attachment.Subscription?.Dispose();
                }

                _attachments.Remove(instanceId);
            }
        }

        /// <summary>按 <paramref name="procDefId"/> 摘除 <paramref name="instanceId"/> 名下单个
        /// 触发器，其余触发器不受影响；本类当前调用方（<see cref="AuraHost"/>）的全部移除路径都是
        /// "整个实例一起摘除"，不需要按单个 <c>procDef</c> 精细摘除，这里作为公开 API 新增补齐
        /// （与上面 <see cref="Detach(Id)"/> 同名不同参数，纯新增重载，不改变既有签名）。</summary>
        public void Detach(Id instanceId, Id procDefId)
        {
            if (!_attachments.TryGetValue(instanceId, out var list))
            {
                return;
            }

            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Def.Id.Equals(procDefId))
                {
                    list[i].Subscription?.Dispose();
                    list.RemoveAt(i);
                }
            }

            if (list.Count == 0)
            {
                _attachments.Remove(instanceId);
            }
        }

        /// <summary>按 <paramref name="dt"/> 推进全部已挂载触发器的内部冷却——同一光环实例下的多个
        /// 触发器各自持有独立的 <see cref="Attachment.IcdRemaining"/>，互不影响。</summary>
        public void Update(double dt)
        {
            foreach (var list in _attachments.Values)
            {
                foreach (var attachment in list)
                {
                    if (attachment.IcdRemaining > 0)
                    {
                        attachment.IcdRemaining = Math.Max(0, attachment.IcdRemaining - dt);
                    }
                }
            }
        }

        /// <summary>
        /// CR130-03 根治：连续/离散模式切换时把全部已挂载触发器正在倒计时的内部冷却按同一系数换算
        /// （同 <see cref="CooldownTracker.RescaleAll"/>/<see cref="AuraHost.RescaleAll"/>/
        /// <see cref="CastPipeline.RescaleAll"/> 判断记录），由 <c>SkillHost.OnTimeModelRescaled</c>
        /// 同一批调用。<paramref name="factor"/> 语义同上述三者：新单位下 1 个单位对应旧单位下
        /// <paramref name="factor"/> 个单位。
        /// </summary>
        public void RescaleAll(double factor)
        {
            if (factor <= 0)
            {
                throw new ArgumentException("factor 必须为正数", nameof(factor));
            }

            _currentFactor *= factor;

            foreach (var list in _attachments.Values)
            {
                foreach (var attachment in list)
                {
                    if (attachment.IcdRemaining > 0)
                    {
                        attachment.IcdRemaining *= factor;
                    }
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
                // CR130-03 根治：InternalCooldown 是 authoring 规范秒数，写入前折算成当前模式的
                // 计时单位（同 CooldownTracker.StartCooldown/CastPipeline.EnterCastOrChannel 判断
                // 记录），否则 Update(dt) 按当前模式的计时单位推进时会用错误的单位解读这段剩余时间。
                attachment.IcdRemaining = attachment.Def.InternalCooldown.Value * _currentFactor;
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
