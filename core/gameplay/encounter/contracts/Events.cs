using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;

namespace Core.Gameplay.Encounter
{
    /// <summary>本模块发出的事件 key 常量（对应 found.event_catalog 登记表 <c>encounter.*</c> 五行）。
    /// 惯例同 <c>core/gameplay/world_state</c> 的 <c>WorldStateEventKeys</c>。</summary>
    public static class EncounterEventKeys
    {
        public static readonly Id Started = new Id("encounter.started");
        public static readonly Id WaveSpawned = new Id("encounter.wave_spawned");
        public static readonly Id PhaseChanged = new Id("encounter.phase_changed");
        public static readonly Id Won = new Id("encounter.won");
        public static readonly Id Lost = new Id("encounter.lost");
    }

    /// <summary>
    /// 判断记录（全部五个事件的 <c>encounterId</c> 字段取值）：found.event_catalog 只给出字段名
    /// <c>encounterId</c>，未区分"遭遇定义 id"与"本次运行实例 id"——若取定义 id，同一
    /// <c>encounter.def</c> 被多次 <see cref="IEncounterHost.Start"/>（同一玩法内重复挑战，或多个
    /// 关卡引用同一遭遇）时，订阅方（如 <see cref="LevelHost"/>）无法区分事件来自哪一次运行；
    /// 任务书给 <see cref="IEncounterHost.Start"/> 明确定义了返回值"实例 id"，<see cref="LevelHost"/>
    /// 需要按实例 id 精确匹配自己刚发起的那一次运行才能正确推进
    /// <c>encounter_sequence</c>（见该类型判断记录）。因此本模块统一把全部五个事件的
    /// <c>encounterId</c> 字段落地为 <b>实例 id</b>（<c>encounter.inst_&lt;n&gt;</c>），不是
    /// <c>encounter.def</c> 的定义 id——这是任务书字段表未列出细节时的判断记录。
    /// <para>
    /// 判断记录（P4 备忘 9，样板游戏 A 反馈）：<c>encounterId</c> 仍是实例 id（订阅方按它精确匹配自己发起的那一次运行），但订阅方
    /// 还需要知道"是哪个遭遇定义"（例如"首领遭遇开始"要认 <c>encounter.def</c> 行 id，实例 id 每次运行都不同）。五个事件因此
    /// 新增只读属性 <c>DefinitionId</c> 与带它的构造函数重载；旧构造函数保留，不带定义 id 时该属性是默认值。不新增表达式可读字段
    /// （<c>found.event_catalog</c> 行与各数据哈希基线不动）。
    /// </para>
    /// </summary>
    public sealed class EncounterStartedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => EncounterEventKeys.Started;

        public Id EncounterId { get; }

        /// <summary>遭遇定义 id（<c>encounter.def</c> 行 id）。P4 备忘 9：此前事件只带实例 id，订阅方反查不到
        /// 是哪一个遭遇定义；用旧构造函数（不带定义 id）创建时本属性是默认值。</summary>
        public Id DefinitionId { get; }

        public EncounterStartedEvent(Id encounterId)
        {
            EncounterId = encounterId;
        }

        public EncounterStartedEvent(Id encounterId, Id definitionId)
        {
            EncounterId = encounterId;
            DefinitionId = definitionId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "encounterId": value = ExprValue.OfId(EncounterId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>波次生成时触发（见 found.event_catalog <c>encounter.wave_spawned</c> 行字段表
    /// <c>{encounterId, waveIndex, entityIds}</c>）。<see cref="EntityIds"/> 是列表，不在
    /// <see cref="IExprReadableEvent"/> 覆盖范围内（同 <c>SkillCastSuccessEvent.Targets</c> 惯例）。</summary>
    public sealed class EncounterWaveSpawnedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => EncounterEventKeys.WaveSpawned;

        public Id EncounterId { get; }

        public int WaveIndex { get; }

        public IReadOnlyList<Id> EntityIds { get; }

        /// <summary>遭遇定义 id（见 <see cref="EncounterStartedEvent.DefinitionId"/>）。</summary>
        public Id DefinitionId { get; }

        public EncounterWaveSpawnedEvent(Id encounterId, int waveIndex, IReadOnlyList<Id> entityIds)
        {
            EncounterId = encounterId;
            WaveIndex = waveIndex;
            EntityIds = (entityIds ?? Array.Empty<Id>()).ToArray();
        }

        public EncounterWaveSpawnedEvent(Id encounterId, int waveIndex, IReadOnlyList<Id> entityIds, Id definitionId)
            : this(encounterId, waveIndex, entityIds)
        {
            DefinitionId = definitionId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "encounterId": value = ExprValue.OfId(EncounterId); return true;
                case "waveIndex": value = ExprValue.OfInt(WaveIndex); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>阶段切换时触发（见 found.event_catalog <c>encounter.phase_changed</c> 行字段表
    /// <c>{encounterId, oldPhase, newPhase}</c>）。<see cref="OldPhase"/>/<see cref="NewPhase"/>
    /// 是 <c>encounter.def.phases</c> 数组下标（任务书未给阶段命名 id，判断记录同
    /// <c>EncounterState.CurrentPhaseIndex</c>）；<see cref="OldPhase"/> 为 -1 表示"尚未进入任何
    /// 阶段"这一初始态。</summary>
    public sealed class EncounterPhaseChangedEvent : IEvent, IExprReadableEvent
    {
        public Id Key => EncounterEventKeys.PhaseChanged;

        public Id EncounterId { get; }

        public int OldPhase { get; }

        public int NewPhase { get; }

        /// <summary>遭遇定义 id（见 <see cref="EncounterStartedEvent.DefinitionId"/>）。</summary>
        public Id DefinitionId { get; }

        public EncounterPhaseChangedEvent(Id encounterId, int oldPhase, int newPhase)
        {
            EncounterId = encounterId;
            OldPhase = oldPhase;
            NewPhase = newPhase;
        }

        public EncounterPhaseChangedEvent(Id encounterId, int oldPhase, int newPhase, Id definitionId)
            : this(encounterId, oldPhase, newPhase)
        {
            DefinitionId = definitionId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "encounterId": value = ExprValue.OfId(EncounterId); return true;
                case "oldPhase": value = ExprValue.OfInt(OldPhase); return true;
                case "newPhase": value = ExprValue.OfInt(NewPhase); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>遭遇胜利结算时触发（见 found.event_catalog <c>encounter.won</c> 行）。</summary>
    public sealed class EncounterWonEvent : IEvent, IExprReadableEvent
    {
        public Id Key => EncounterEventKeys.Won;

        public Id EncounterId { get; }

        /// <summary>遭遇定义 id（见 <see cref="EncounterStartedEvent.DefinitionId"/>）。</summary>
        public Id DefinitionId { get; }

        public EncounterWonEvent(Id encounterId)
        {
            EncounterId = encounterId;
        }

        public EncounterWonEvent(Id encounterId, Id definitionId)
        {
            EncounterId = encounterId;
            DefinitionId = definitionId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "encounterId": value = ExprValue.OfId(EncounterId); return true;
                default: value = default; return false;
            }
        }
    }

    /// <summary>遭遇失败结算时触发（见 found.event_catalog <c>encounter.lost</c> 行）。</summary>
    public sealed class EncounterLostEvent : IEvent, IExprReadableEvent
    {
        public Id Key => EncounterEventKeys.Lost;

        public Id EncounterId { get; }

        /// <summary>遭遇定义 id（见 <see cref="EncounterStartedEvent.DefinitionId"/>）。</summary>
        public Id DefinitionId { get; }

        public EncounterLostEvent(Id encounterId)
        {
            EncounterId = encounterId;
        }

        public EncounterLostEvent(Id encounterId, Id definitionId)
        {
            EncounterId = encounterId;
            DefinitionId = definitionId;
        }

        public bool TryGetField(string name, out ExprValue value)
        {
            switch (name)
            {
                case "encounterId": value = ExprValue.OfId(EncounterId); return true;
                default: value = default; return false;
            }
        }
    }
}
