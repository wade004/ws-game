using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Combat
{
    /// <summary>
    /// T-L14（测试覆盖剩余项 2026-10-01）：<c>ThreatTable.RemoveSource</c> / <c>RemoveSourceEverywhere</c> 的直接用例
    /// （此前只经 CombatHost 离开战斗、阵营变更同步两条间接路径触及）。
    /// </summary>
    public class ThreatTableRemoveSourceTests
    {
        private static readonly Id OwnerA = new Id("unit.rs_owner_a");
        private static readonly Id OwnerB = new Id("unit.rs_owner_b");
        private static readonly Id Faction = new Id("fac.rs_test");

        private static (Core.Rules.Combat.ThreatTable table, FakeUnitAccess units, List<CombatThreatChangedEvent> events, IEventBus bus) Make()
        {
            var bus = CombatTestSupport.MakeBus();
            var events = new List<CombatThreatChangedEvent>();
            bus.Subscribe(RulesEventKeys.CombatThreatChanged, e => events.Add((CombatThreatChangedEvent)e));

            var units = new FakeUnitAccess();
            units.Add(OwnerA, Faction);
            units.Add(OwnerB, Faction);
            var table = new Core.Rules.Combat.ThreatTable(units, bus, 16);
            return (table, units, events, bus);
        }

        [Fact]
        public void RemoveSource_RemovesOnlyThatEntry_AndEmitsChangeToZero()
        {
            var (table, units, events, bus) = Make();
            var keep = new Id("unit.rs_keep");
            var drop = new Id("unit.rs_drop");
            units.Add(keep, Faction);
            units.Add(drop, Faction);
            table.AddThreat(OwnerA, keep, 4);
            table.AddThreat(OwnerA, drop, 9);
            bus.DispatchPending();
            events.Clear();

            table.RemoveSource(OwnerA, drop);
            bus.DispatchPending();

            Assert.Equal(0, table.GetThreat(OwnerA, drop));
            Assert.Equal(4, table.GetThreat(OwnerA, keep));
            var evt = Assert.Single(events);
            Assert.Equal(OwnerA, evt.UnitId);
            Assert.Equal(drop, evt.SourceId);
            Assert.Equal(9.0, evt.OldValue);
            Assert.Equal(0.0, evt.NewValue);
        }

        [Fact]
        public void RemoveSource_UnknownOwnerOrUnknownSource_IsSilentNoOp()
        {
            var (table, units, events, bus) = Make();
            var source = new Id("unit.rs_known_source");
            units.Add(source, FactionA());
            table.AddThreat(OwnerA, source, 5);
            bus.DispatchPending();
            events.Clear();

            table.RemoveSource(OwnerB, source);                       // OwnerB 没有仇恨表
            table.RemoveSource(OwnerA, new Id("unit.rs_never_seen")); // OwnerA 表里没有该来源
            bus.DispatchPending();

            Assert.Empty(events);
            Assert.Equal(5, table.GetThreat(OwnerA, source));
        }

        private static Id FactionA() => Faction;

        [Fact]
        public void RemoveSourceEverywhere_RemovesSourceFromEveryOwnersTable_LeavingOthers()
        {
            var (table, units, events, bus) = Make();
            var target = new Id("unit.rs_everywhere");
            var bystander = new Id("unit.rs_bystander");
            units.Add(target, Faction);
            units.Add(bystander, Faction);
            table.AddThreat(OwnerA, target, 3);
            table.AddThreat(OwnerB, target, 6);
            table.AddThreat(OwnerB, bystander, 1);
            bus.DispatchPending();
            events.Clear();

            table.RemoveSourceEverywhere(target);
            bus.DispatchPending();

            Assert.Equal(0, table.GetThreat(OwnerA, target));
            Assert.Equal(0, table.GetThreat(OwnerB, target));
            Assert.Equal(1, table.GetThreat(OwnerB, bystander));

            // 每个持有该来源的表各发一条 old->0 的变更事件，其余来源不受影响。
            Assert.Equal(2, events.Count);
            Assert.All(events, e =>
            {
                Assert.Equal(target, e.SourceId);
                Assert.Equal(0.0, e.NewValue);
            });
            Assert.Equal(
                new[] { OwnerA, OwnerB }.OrderBy(i => i.Value, System.StringComparer.Ordinal),
                events.Select(e => e.UnitId).OrderBy(i => i.Value, System.StringComparer.Ordinal));
        }

        [Fact]
        public void RemoveSourceEverywhere_SourceInNoTable_DoesNothing()
        {
            var (table, units, events, bus) = Make();
            var source = new Id("unit.rs_present");
            units.Add(source, Faction);
            table.AddThreat(OwnerA, source, 2);
            bus.DispatchPending();
            events.Clear();

            table.RemoveSourceEverywhere(new Id("unit.rs_absent"));
            bus.DispatchPending();

            Assert.Empty(events);
            Assert.Equal(2, table.GetThreat(OwnerA, source));
        }
    }
}
