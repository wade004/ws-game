using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Rules.Common;
using Xunit;

namespace Tests.Rules.Combat
{
    /// <summary>
    /// <see cref="Core.Rules.Combat.ThreatTable"/> 单元测试（见落地方案 T2-8 行"仇恨值增减、
    /// 进出战斗事件触发条件均有测试""禁止仇恨表大小无上限增长（需有清理策略并测试覆盖）"）。
    /// 直接构造 <see cref="Core.Rules.Combat.ThreatTable"/>（不经 <see cref="Core.Rules.Combat.CombatHost"/>），
    /// 只需要真实 <see cref="IEventBus"/> + Fake <see cref="IUnitAccess"/>。
    /// </summary>
    public class ThreatTableTests
    {
        private static readonly Id Owner = new Id("unit.threat_owner");
        private static readonly Id FactionA = new Id("fac.threat_test_a");

        private static (Core.Rules.Combat.ThreatTable table, FakeUnitAccess units, List<IEvent> events, IEventBus bus) Make(int maxEntries = 16)
        {
            var bus = CombatTestSupport.MakeBus();
            var events = new List<IEvent>();
            bus.Subscribe(RulesEventKeys.CombatThreatChanged, e => events.Add(e));

            var units = new FakeUnitAccess();
            units.Add(Owner, FactionA);

            var table = new Core.Rules.Combat.ThreatTable(units, bus, maxEntries);
            return (table, units, events, bus);
        }

        [Fact]
        public void AddThreat_IncreasesValue()
        {
            var (table, units, _, _) = Make();
            var source = new Id("unit.threat_source_1");
            units.Add(source, FactionA);

            table.AddThreat(Owner, source, 10);
            table.AddThreat(Owner, source, 5);

            Assert.Equal(15, table.GetThreat(Owner, source));
        }

        [Fact]
        public void AddThreat_IgnoresDeadSource()
        {
            var (table, units, _, _) = Make();
            var source = new Id("unit.threat_dead");
            units.Add(source, FactionA, alive: false);

            table.AddThreat(Owner, source, 10);

            Assert.Equal(0, table.GetThreat(Owner, source));
            Assert.Empty(table.GetAll(Owner));
        }

        [Fact]
        public void AddThreat_IgnoresNonexistentSource()
        {
            var (table, _, _, _) = Make();
            var ghost = new Id("unit.threat_ghost");

            table.AddThreat(Owner, ghost, 10);

            Assert.Equal(0, table.GetThreat(Owner, ghost));
        }

        [Fact]
        public void GetTopThreat_NoEntries_ReturnsNull()
        {
            var (table, _, _, _) = Make();
            Assert.Null(table.GetTopThreat(Owner));
        }

        [Fact]
        public void GetTopThreat_TieBreak_SmallestIdWins()
        {
            var (table, units, _, _) = Make();
            var a = new Id("unit.threat_a");
            var b = new Id("unit.threat_b");
            var c = new Id("unit.threat_c");
            units.Add(a, FactionA);
            units.Add(b, FactionA);
            units.Add(c, FactionA);

            table.AddThreat(Owner, b, 10);
            table.AddThreat(Owner, a, 10); // 与 b 并列最高，Id 序数 a < b
            table.AddThreat(Owner, c, 5);

            Assert.Equal(a, table.GetTopThreat(Owner));
        }

        [Fact]
        public void EnforceCap_Evicts17To16_RemovesSmallestValue()
        {
            var (table, units, _, _) = Make(maxEntries: 16);

            for (int i = 1; i <= 17; i++)
            {
                var id = new Id($"unit.threat_src_{i:00}");
                units.Add(id, FactionA);
                table.AddThreat(Owner, id, i); // 各不相同的仇恨值，最小值来自 i=1
            }

            var all = table.GetAll(Owner);
            Assert.Equal(16, all.Count);

            var smallestStillPresent = false;
            foreach (var (source, _) in all)
            {
                if (source == new Id("unit.threat_src_01"))
                {
                    smallestStillPresent = true;
                }
            }
            Assert.False(smallestStillPresent, "值最小（i=1）的来源应被裁剪掉");
        }

        [Fact]
        public void SetThreat_TauntPlacesSourceOnTop()
        {
            var (table, units, _, _) = Make();
            var a = new Id("unit.threat_a");
            var taunter = new Id("unit.threat_taunter");
            units.Add(a, FactionA);
            units.Add(taunter, FactionA);

            table.AddThreat(Owner, a, 100);
            Assert.Equal(a, table.GetTopThreat(Owner));

            var currentTop = table.GetThreat(Owner, a);
            table.SetThreat(Owner, taunter, currentTop + 1);

            Assert.Equal(taunter, table.GetTopThreat(Owner));
        }

        [Fact]
        public void Clear_RemovesAllEntries()
        {
            var (table, units, _, _) = Make();
            var a = new Id("unit.threat_a");
            units.Add(a, FactionA);
            table.AddThreat(Owner, a, 10);

            table.Clear(Owner);

            Assert.Empty(table.GetAll(Owner));
            Assert.Null(table.GetTopThreat(Owner));
        }

        [Fact]
        public void PruneDead_RemovesEntriesForDeadOrMissingSources()
        {
            var (table, units, _, _) = Make();
            var alive = new Id("unit.threat_alive");
            var dying = new Id("unit.threat_dying");
            units.Add(alive, FactionA);
            units.Add(dying, FactionA);

            table.AddThreat(Owner, alive, 10);
            table.AddThreat(Owner, dying, 20);

            units.SetAlive(dying, false);
            table.PruneDead(Owner);

            var all = table.GetAll(Owner);
            Assert.Single(all);
            Assert.Equal(alive, all[0].source);
        }

        [Fact]
        public void ThreatChangedEvent_EmittedOnValueChange_NotOnNoChange()
        {
            var (table, units, events, bus) = Make();
            var source = new Id("unit.threat_evt");
            units.Add(source, FactionA);

            table.AddThreat(Owner, source, 10);
            bus.DispatchPending(); // ThreatTable 只 Enqueue，需要显式派发才能被订阅者观察到
            Assert.Single(events);
            var evt = Assert.IsType<CombatThreatChangedEvent>(events[0]);
            Assert.Equal(Owner, evt.UnitId);
            Assert.Equal(source, evt.SourceId);
            Assert.Equal(0.0, evt.OldValue);
            Assert.Equal(10.0, evt.NewValue);

            events.Clear();
            table.SetThreat(Owner, source, 10); // 设成同一个值，不应再发事件
            bus.DispatchPending();
            Assert.Empty(events);
        }

        [Fact]
        public void GetAll_ReturnsAllTrackedSources()
        {
            var (table, units, _, _) = Make();
            var a = new Id("unit.threat_a");
            var b = new Id("unit.threat_b");
            units.Add(a, FactionA);
            units.Add(b, FactionA);

            table.AddThreat(Owner, a, 1);
            table.AddThreat(Owner, b, 2);

            Assert.Equal(2, table.GetAll(Owner).Count);
        }
    }
}
