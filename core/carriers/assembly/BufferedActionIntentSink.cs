using System;
using System.Collections.Generic;
using Core.Carriers.Item;
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
    /// 武器优先的输入动作 → 技能映射（手感设计/08 第 1 节换装链的"主手武器 → 判定"一段在生产装配里的落点）：普通攻击输入动作
    /// 优先取单位当前主手武器声明的普攻技能（<c>feel.weapon.auto_attack_timeline_ref</c>，经 <see cref="WeaponActionBinding"/>），
    /// 武器没声明、空手、或不是普通攻击动作时回落到 <see cref="ActionSlotSkillBinding"/>（<c>skill_slot</c> → 技能绑定槽位）。
    /// 因此换武器后普攻（以及它的姿势族）自动切换，游戏不必在换装时重绑槽位，也不必写换装代码。
    /// <para>
    /// 判断记录（哪些动作算"普通攻击"）：缺省所有类别为 <see cref="ActionClass.Attack"/> 的输入动作（同 <see cref="WeaponActionBinding"/>
    /// 契约注释的口径）；游戏有第二个攻击类动作（蓄力、重击等，自带 <c>skill_slot</c>）时用 <see cref="CarriersFeelOptions.AutoAttackActions"/>
    /// 点名只有哪几个动作是普攻，其余攻击类动作始终走槽位绑定，不被武器普攻劫持。
    /// </para>
    /// <para>
    /// 判断记录（不带空手技能）：本类内部的武器映射不带"空手普攻技能"，空手时回落到槽位绑定——空手普攻就是游戏给普攻槽位绑的那个技能，
    /// 不另造一个只服务于本类的配置项。
    /// </para>
    /// </summary>
    public sealed class WeaponPreferredActionBinding : IActionSkillBinding
    {
        private readonly WeaponActionBinding _weapon;
        private readonly IActionSkillBinding _fallback;
        private readonly HashSet<Id>? _autoAttackActions;

        /// <param name="weapon">武器普攻映射（构造时不带空手技能）。</param>
        /// <param name="fallback">武器没有给出技能时的回落映射（通常是 <see cref="ActionSlotSkillBinding"/>）。</param>
        /// <param name="autoAttackActions">被视为普通攻击的动作 id；<c>null</c> = 所有攻击类动作。</param>
        public WeaponPreferredActionBinding(WeaponActionBinding weapon, IActionSkillBinding fallback, IEnumerable<Id>? autoAttackActions = null)
        {
            _weapon = weapon ?? throw new ArgumentNullException(nameof(weapon));
            _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
            _autoAttackActions = autoAttackActions == null ? null : new HashSet<Id>(autoAttackActions);
        }

        public bool TryResolveSkill(Id actorId, BufferedIntent intent, out Id skillId)
        {
            if (intent.Class == ActionClass.Attack
                && (_autoAttackActions == null || _autoAttackActions.Contains(intent.ActionId))
                && _weapon.TryResolveAttackSkill(actorId, out skillId))
            {
                return true;
            }

            return _fallback.TryResolveSkill(actorId, intent, out skillId);
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
        private readonly IActionSkillBinding _binding;
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
            : this(buffer, (IActionSkillBinding)binding, skill, world, bus, reactions, stepSeconds)
        {
        }

        /// <summary>手感落地 M1 补缺：映射来源改为任意 <see cref="IActionSkillBinding"/>（生产装配传 <see cref="WeaponPreferredActionBinding"/>）；上面的构造原样保留并转调本重载。</summary>
        public BufferedActionIntentSink(
            InputBufferHost buffer, IActionSkillBinding binding, SkillHost skill, IWorldSim world, IEventBus bus,
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

            // 手感落地 M2-B（手感设计/01 第 2.4 节）：输入动作声明的宽限条件名随意图交给施法管线（步骤 7 在宽限窗口内放行）。
            var graceConditions = _buffer.GetDefinition(record.ActionId)?.GraceConditions;
            if (graceConditions != null && graceConditions.Count > 0)
            {
                var names = new List<JsonValue>(graceConditions.Count);
                for (var i = 0; i < graceConditions.Count; i++) names.Add(new JsonString(graceConditions[i].Value));
                args.Add("grace_conditions", new JsonArray(names));
            }

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
