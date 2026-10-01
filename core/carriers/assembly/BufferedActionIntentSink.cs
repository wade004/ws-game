using System;
using System.Collections.Generic;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Core.Foundation.SimLoop;
using Core.Rules.Combat;
using Core.Rules.Common;
using Core.Rules.Skill;

namespace Core.Carriers.Assembly
{
    /// <summary>
    /// 输入动作 → 技能映射（手感落地 S10）：按 <c>found.input_action.skill_slot</c> 声明的槽位名，从行动者的技能槽位绑定
    /// （<see cref="SkillBindingHost"/>）里取当前绑定的技能。同时是动作时间线取消进入用的 <see cref="IActionSkillBinding"/>
    /// 与缓冲出口 <see cref="BufferedActionIntentSink"/> 的映射来源，两处用同一份，不会各说各话。
    /// <para>未声明 <c>skill_slot</c>、槽位没有绑定技能时返回 false（"这条记录不对应任何技能"）。</para>
    /// </summary>
    public sealed class ActionSlotSkillBinding : IActionSkillBinding
    {
        private readonly InputBufferHost _buffer;
        private readonly SkillBindingHost _bindings;

        public ActionSlotSkillBinding(InputBufferHost buffer, SkillBindingHost bindings)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        }

        public bool TryResolveSkill(Id actorId, BufferedIntent intent, out Id skillId)
        {
            skillId = default;
            var slot = _buffer.GetDefinition(intent.ActionId)?.SkillSlot;
            return slot != null && _bindings.GetBindings(actorId).TryGetValue(slot, out skillId);
        }
    }

    /// <summary>
    /// 输入缓冲在 tick 步骤 1 的生产出口（<see cref="IBufferedIntentSink"/>）：缓冲记录能被接受时，把它翻成一条 <c>cast</c> 意图
    /// （步骤 3 的 <see cref="SkillTickHandler"/> 消费）；施法管线随后若拒绝，经 <c>skill.cast_failed</c> 回报缓冲
    /// （<see cref="InputBufferHost.ReportRejected"/>，手感设计/01 第 2.3 节第 4 点：时间可解的原因保留记录到过期，其余原因丢弃）。
    /// <para>
    /// 判断记录（时间线动作进行中不接受）：时间线动作进行中，记录留给时间线自己在取消窗口/连招窗口里拉取
    /// （<c>CastPipeline</c> 每个推进 tick 末尾对缓冲 <c>TryConsume</c>）。若本出口此时接受，就会在同一 tick 内被施法管线以
    /// <c>ActionLocked</c> 拒绝并"撤销消费"，而缓冲对"本 tick 已取用过"的行动者不再提供候选，时间线的拉取就被饿死。
    /// 时间线动作结束后的下一个步骤 1，本出口接受它——这就是"在后摇结束前 X 毫秒按下，动作一结束就接上"的来源。
    /// </para>
    /// <para>
    /// 判断记录（硬直中不接受）：行动者处于受击硬直（<see cref="IHitReactionQuery.IsStaggered"/>）时记录保留，硬直结束前按缓冲窗口计时
    /// （窗口以动作时钟计，顿帧时暂停）。
    /// </para>
    /// <para>
    /// 判断记录（接受时朝向对齐）：记录 <see cref="BufferedIntent.FaceOnAccept"/> 为真且带按下瞬间方向快照时，在接受瞬间把行动者朝向直接对齐到该方向
    /// （<c>Atan2(y, x)</c>，与运动层同一朝向约定）；没有轴输入保持当前朝向。是瞬时对齐，不经运动层的转向速率——转向速率是行走时的
    /// 惯性，攻击起手的方向修正才是 <c>face_on_accept</c> 的语义。时间线自己在取消窗口里拉取的记录不经本出口，不做对齐（已知局限）。
    /// </para>
    /// <para>
    /// 判断记录（未映射技能的动作）：声明了类别但没有 <c>skill_slot</c>（或槽位没有绑定）的动作，本出口返回 false，记录留在缓冲里直到过期，
    /// 供游戏自己的消费者取用；若它恰好是该行动者最前的候选，会挡住优先级更低的候选直到它过期（缓冲只取最前一条，已知局限）。
    /// 游戏应给每个声明了类别的动作配 <c>skill_slot</c>，或不给它声明类别。
    /// </para>
    /// </summary>
    public sealed class BufferedActionIntentSink : IBufferedIntentSink, IDisposable
    {
        private readonly InputBufferHost _buffer;
        private readonly ActionSlotSkillBinding _binding;
        private readonly SkillHost _skill;
        private readonly IWorldSim _world;
        private readonly IHitReactionQuery? _reactions;
        private readonly double _stepSeconds;
        private readonly Dictionary<Id, Pending> _pending = new Dictionary<Id, Pending>();
        private readonly SubscriptionHandle _failedSubscription;

        private readonly struct Pending
        {
            public readonly Id ActionId;
            public readonly Id SkillId;
            public readonly long Tick;

            public Pending(Id actionId, Id skillId, long tick)
            {
                ActionId = actionId;
                SkillId = skillId;
                Tick = tick;
            }
        }

        public BufferedActionIntentSink(
            InputBufferHost buffer, ActionSlotSkillBinding binding, SkillHost skill, IWorldSim world, IEventBus bus,
            IHitReactionQuery? reactions, double stepSeconds)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _binding = binding ?? throw new ArgumentNullException(nameof(binding));
            _skill = skill ?? throw new ArgumentNullException(nameof(skill));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            _reactions = reactions;
            _stepSeconds = stepSeconds;
            _failedSubscription = bus.Subscribe<SkillCastFailedEvent>(RulesEventKeys.SkillCastFailed, OnCastFailed);
        }

        public bool TryAccept(Id actorId, BufferedIntent record, out Intent intent)
        {
            intent = default!;
            if (!_binding.TryResolveSkill(actorId, record, out var skillId)) return false;
            if (_skill.ActionStateQuery.Current(actorId).HasValue) return false;
            if (_reactions != null && _reactions.IsStaggered(actorId)) return false;

            if (record.FaceOnAccept && record.DirectionSnapshot.HasValue && _world.GetEntity(actorId) is Core.Carriers.Unit.Unit unit)
            {
                var d = record.DirectionSnapshot.Value;
                unit.Facing = Math.Atan2(d.Y, d.X);
            }

            var args = new JsonObjectBuilder().Add("skill_id", new JsonString(skillId.Value));
            if (record.DirectionSnapshot.HasValue)
            {
                var d = record.DirectionSnapshot.Value;
                args.Add("direction", new JsonObjectBuilder()
                    .Add("x", new JsonNumber(d.X))
                    .Add("y", new JsonNumber(d.Y))
                    .Build());
            }

            var held = record.HoldState == BufferHoldState.HoldReleased ? record.HeldTicks : 0;
            if (held > 0) args.Add("held_ticks", new JsonNumber(held));

            intent = new Intent(actorId, "cast", args.Build());
            _pending[actorId] = new Pending(record.ActionId, skillId, _buffer.CurrentTick);
            return true;
        }

        private void OnCastFailed(SkillCastFailedEvent e)
        {
            if (!_pending.TryGetValue(e.CasterId, out var pending)) return;
            _pending.Remove(e.CasterId);
            // 只认"本 tick 由本出口提交的那次施法"的失败：其它来源（AI、脚本）对同一技能的失败不该撤销缓冲记录的消费。
            if (pending.Tick != _buffer.CurrentTick || !pending.SkillId.Equals(e.SkillId)) return;

            _buffer.ReportRejected(e.CasterId, pending.ActionId, e.ReasonCode.ToString(), IsTimeSolvable(e.CasterId, pending, e.ReasonCode));
        }

        /// <summary>手感设计/01 第 2.3 节第 4 点：时间可解 = 节拍锁、公共冷却、忙，以及剩余冷却不超过记录剩余缓冲的冷却中（同时间线取消进入的口径）。</summary>
        private bool IsTimeSolvable(Id actorId, Pending pending, CastFailureReason reason)
        {
            switch (reason)
            {
                case CastFailureReason.ActionLocked:
                case CastFailureReason.GcdActive:
                case CastFailureReason.Busy:
                    return true;
                case CastFailureReason.OnCooldown:
                    var remainingBufferSeconds = _buffer.RemainingBufferTicks(actorId, pending.ActionId) * _stepSeconds;
                    return _skill.GetCooldown(actorId, pending.SkillId) <= remainingBufferSeconds;
                default:
                    return false;
            }
        }

        public void Dispose() => _failedSubscription.Dispose();
    }
}
