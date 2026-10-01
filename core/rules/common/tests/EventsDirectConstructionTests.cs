using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Core.Foundation.Common;
using Core.Foundation.Expr;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Common
{
    /// <summary>
    /// T-L13（测试覆盖剩余项 2026-10-01）：<c>Events.cs</c> 里此前没有直接构造用例的事件类型——逐个断言
    /// <c>Key</c> 与 <see cref="RulesEventKeys"/> 一致、公开属性原样回读、<c>TryGetField</c> 暴露的字段名/取值，
    /// 以及"可空字段未提供时查不到"这类约定。事件是引用类型，没有值相等性语义，这里只钉字段往返。
    /// </summary>
    public class EventsDirectConstructionTests
    {
        private static readonly Id Unit = new Id("unit.evt_unit");
        private static readonly Id Other = new Id("unit.evt_other");
        private static readonly Id Aura = new Id("aura.evt_def");

        private static ExprValue Read(IExprReadableEvent evt, string name)
        {
            Assert.True(evt.TryGetField(name, out var value), $"字段 {name} 应可读");
            return value;
        }

        [Fact]
        public void RulesEventKeys_AreUnique_AndAllCarryDomainPrefix()
        {
            var keys = typeof(RulesEventKeys)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(Id))
                .Select(f => ((Id)f.GetValue(null)!).Value)
                .ToList();

            Assert.NotEmpty(keys);
            Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
            foreach (var key in keys)
            {
                Assert.Contains('.', key);
            }
        }

        [Fact]
        public void AuraAppliedEvent_RoundTripsFields_AndDefaultsChainDepthToZero()
        {
            var evt = new AuraAppliedEvent(Unit, Aura, Other, stacks: 3);

            Assert.Equal(RulesEventKeys.AuraApplied, evt.Key);
            Assert.Equal(Unit, evt.TargetId);
            Assert.Equal(Aura, evt.AuraDefId);
            Assert.Equal(Other, evt.SourceId);
            Assert.Equal(3, evt.Stacks);
            Assert.Equal(0, evt.TriggerChainDepth);
            Assert.Equal(Unit, Read(evt, "targetId").AsId);
            Assert.Equal(Aura, Read(evt, "auraDefId").AsId);
            Assert.Equal(Other, Read(evt, "sourceId").AsId);
            Assert.Equal(3L, Read(evt, "stacks").AsInt);
            Assert.Equal(0L, Read(evt, "triggerChainDepth").AsInt);
            Assert.False(evt.TryGetField("noSuchField", out _));
        }

        [Fact]
        public void AuraRemovedEvent_RoundTripsReason_AndRejectsNullReason()
        {
            var evt = new AuraRemovedEvent(Unit, Aura, "expired", triggerChainDepth: 2);

            Assert.Equal(RulesEventKeys.AuraRemoved, evt.Key);
            Assert.Equal("expired", evt.Reason);
            Assert.Equal(2, evt.TriggerChainDepth);
            Assert.Equal("expired", Read(evt, "reason").AsString);
            Assert.Equal(2L, Read(evt, "triggerChainDepth").AsInt);

            Assert.Throws<ArgumentNullException>(() => new AuraRemovedEvent(Unit, Aura, null!));
        }

        [Fact]
        public void AuraStackChangedEvent_ExposesOldAndNewStacks()
        {
            var evt = new AuraStackChangedEvent(Unit, Aura, oldStacks: 2, newStacks: 5);

            Assert.Equal(RulesEventKeys.AuraStackChanged, evt.Key);
            Assert.Equal(2L, Read(evt, "oldStacks").AsInt);
            Assert.Equal(5L, Read(evt, "newStacks").AsInt);
            Assert.Equal(0, evt.TriggerChainDepth);
        }

        [Fact]
        public void ProcTriggeredEvent_ExposesProcAndTriggerSkill()
        {
            var proc = new Id("proc.evt_def");
            var skill = new Id("skill.evt_trigger");

            var evt = new ProcTriggeredEvent(Unit, proc, skill, triggerChainDepth: 1);

            Assert.Equal(RulesEventKeys.ProcTriggered, evt.Key);
            Assert.Equal(Unit, Read(evt, "unitId").AsId);
            Assert.Equal(proc, Read(evt, "procDefId").AsId);
            Assert.Equal(skill, Read(evt, "triggerSkillId").AsId);
            Assert.Equal(1L, Read(evt, "triggerChainDepth").AsInt);
        }

        [Fact]
        public void CombatThreatChangedEvent_ExposesOldAndNewValueAsNumbers()
        {
            var evt = new CombatThreatChangedEvent(Unit, Other, oldValue: 12.5, newValue: 40.25);

            Assert.Equal(RulesEventKeys.CombatThreatChanged, evt.Key);
            Assert.Equal(12.5, Read(evt, "oldValue").AsNumber);
            Assert.Equal(40.25, Read(evt, "newValue").AsNumber);
            Assert.Equal(Other, Read(evt, "sourceId").AsId);
        }

        [Fact]
        public void CombatEnteredEvent_HostileIdIsOptional_AndAbsentFieldIsNotReadable()
        {
            var withoutHostile = new CombatEnteredEvent(Unit);
            Assert.Equal(RulesEventKeys.CombatEntered, withoutHostile.Key);
            Assert.Null(withoutHostile.HostileId);
            Assert.True(withoutHostile.TryGetField("unitId", out _));
            Assert.False(withoutHostile.TryGetField("hostileId", out _));

            var withHostile = new CombatEnteredEvent(Unit, Other);
            Assert.Equal(Other, withHostile.HostileId);
            Assert.Equal(Other, Read(withHostile, "hostileId").AsId);
        }

        [Fact]
        public void CombatLeftEvent_OnlyExposesUnitId()
        {
            var evt = new CombatLeftEvent(Unit);

            Assert.Equal(RulesEventKeys.CombatLeft, evt.Key);
            Assert.Equal(Unit, Read(evt, "unitId").AsId);
            Assert.False(evt.TryGetField("hostileId", out _));
        }

        [Fact]
        public void AutoAttackSwingEvent_ExposesSourceAndTarget()
        {
            var evt = new AutoAttackSwingEvent(Unit, Other);

            Assert.Equal(RulesEventKeys.CombatAutoAttackSwing, evt.Key);
            Assert.Equal(Unit, Read(evt, "sourceId").AsId);
            Assert.Equal(Other, Read(evt, "targetId").AsId);
        }

        [Fact]
        public void UnitDiedEvent_OptionalFieldsDefaultToAbsent_AndPositionIsNotExprReadable()
        {
            var bare = new UnitDiedEvent(Unit, killerId: null);
            Assert.Equal(RulesEventKeys.UnitDied, bare.Key);
            Assert.Null(bare.KillerId);
            Assert.Null(bare.MapId);
            Assert.Equal(default(Vec2), bare.Position);
            Assert.False(bare.TryGetField("killerId", out _));
            Assert.False(bare.TryGetField("mapId", out _));

            var map = new Id("map.evt_map");
            var full = new UnitDiedEvent(Unit, Other, map, new Vec2(3, 4));
            Assert.Equal(Other, Read(full, "killerId").AsId);
            Assert.Equal(map, Read(full, "mapId").AsId);
            Assert.Equal(new Vec2(3, 4), full.Position);
            Assert.False(full.TryGetField("position", out _));
        }

        [Theory]
        [InlineData(RespawnPolicy.RespawnPoint)]
        [InlineData(RespawnPolicy.ReloadSave)]
        [InlineData(RespawnPolicy.Permadeath)]
        public void UnitRespawnedEvent_ExposesPolicyAsEnumName(RespawnPolicy policy)
        {
            var evt = new UnitRespawnedEvent(Unit, policy);

            Assert.Equal(RulesEventKeys.UnitRespawned, evt.Key);
            Assert.Equal(policy, evt.Policy);
            Assert.Equal(policy.ToString(), Read(evt, "policy").AsString);
        }

        [Fact]
        public void UnitFactionChangedEvent_ExposesOldAndNewFaction()
        {
            var oldF = new Id("fac.evt_old");
            var newF = new Id("fac.evt_new");

            var evt = new UnitFactionChangedEvent(Unit, oldF, newF);

            Assert.Equal(RulesEventKeys.UnitFactionChanged, evt.Key);
            Assert.Equal(oldF, Read(evt, "oldFactionId").AsId);
            Assert.Equal(newF, Read(evt, "newFactionId").AsId);
        }

        [Fact]
        public void AiStateChangedEvent_ExposesBothStatesAsEnumNames()
        {
            var evt = new AiStateChangedEvent(Unit, BehaviorState.Patrol, BehaviorState.Chase);

            Assert.Equal(RulesEventKeys.AiStateChanged, evt.Key);
            Assert.Equal(BehaviorState.Patrol, evt.OldState);
            Assert.Equal(BehaviorState.Chase, evt.NewState);
            Assert.Equal(BehaviorState.Patrol.ToString(), Read(evt, "oldState").AsString);
            Assert.Equal(BehaviorState.Chase.ToString(), Read(evt, "newState").AsString);
        }

        [Fact]
        public void AiDecisionMadeEvent_ExposesDecisionId()
        {
            var decision = new Id("ai.evt_decision");

            var evt = new AiDecisionMadeEvent(Unit, decision);

            Assert.Equal(RulesEventKeys.AiDecisionMade, evt.Key);
            Assert.Equal(decision, Read(evt, "decisionId").AsId);
        }

        [Fact]
        public void TargetingResolvedEvent_CopiesTargetList_AndTreatsNullAsEmpty()
        {
            var chain = new Id("target.evt_chain");
            var source = new List<Id> { Unit, Other };

            var evt = new TargetingResolvedEvent(Unit, chain, source);
            source.Clear();

            Assert.Equal(RulesEventKeys.TargetingResolved, evt.Key);
            Assert.Equal(new[] { Unit, Other }, evt.TargetIds);
            Assert.Equal(chain, Read(evt, "chainId").AsId);
            Assert.False(evt.TryGetField("targetIds", out _));

            var empty = new TargetingResolvedEvent(Unit, chain, null!);
            Assert.Empty(empty.TargetIds);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.5)]
        public void TimeModelRescaledEvent_RejectsNonPositiveFactor(double factor)
        {
            Assert.Throws<ArgumentException>(() => new TimeModelRescaledEvent(factor));
        }

        [Fact]
        public void TimeModelRescaledEvent_ExposesFactor()
        {
            var evt = new TimeModelRescaledEvent(2.5);

            Assert.Equal(RulesEventKeys.TimeModelRescaled, evt.Key);
            Assert.Equal(2.5, Read(evt, "factor").AsNumber);
        }
    }
}
