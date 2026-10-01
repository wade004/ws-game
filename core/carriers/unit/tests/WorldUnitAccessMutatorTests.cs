using System;
using System.Collections.Generic;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using Xunit;

namespace Tests.Carriers.Unit
{
    /// <summary>
    /// T-M25（测试覆盖剩余项 2026-10-01）：<see cref="WorldUnitAccess"/> 写入类成员 <c>SetFaction</c>（ADR-0088）与
    /// <c>SetLevel</c> 的直接用例——同值早退不发事件、未注入总线抛 <see cref="InvalidOperationException"/>、
    /// 未知单位抛、<c>SetLevel</c> 对未知单位静默空操作。
    /// </summary>
    public class WorldUnitAccessMutatorTests
    {
        private static readonly Id MapId = new Id("map.test");
        private static readonly Id FactionOld = new Id("fac.wuam_old");
        private static readonly Id FactionNew = new Id("fac.wuam_new");
        private static readonly Id ArchetypeId = new Id("arch.class.sample");
        private static readonly Id HeroId = new Id("unit.wuam_hero");

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        private static (WorldUnitAccess Units, IEventBus Bus, PlayerUnit Hero, List<UnitFactionChangedEvent> Seen) Build(bool withBus = true)
        {
            var bus = NewBus();
            var world = new WorldSim(bus);
            var units = withBus ? new WorldUnitAccess(world, bus) : new WorldUnitAccess(world);
            var hero = new PlayerUnit(HeroId, MapId, FactionOld, ArchetypeId);
            world.AddEntity(hero);

            var seen = new List<UnitFactionChangedEvent>();
            bus.Subscribe<UnitFactionChangedEvent>(RulesEventKeys.UnitFactionChanged, e => seen.Add(e));
            return (units, bus, hero, seen);
        }

        [Fact]
        public void SetFaction_WithoutInjectedBus_ThrowsInvalidOperation_AndLeavesFactionUntouched()
        {
            var (units, _, hero, seen) = Build(withBus: false);

            Assert.Throws<InvalidOperationException>(() => units.SetFaction(HeroId, FactionNew));

            Assert.Equal(FactionOld, hero.FactionId);
            Assert.Empty(seen);
        }

        [Fact]
        public void SetFaction_WithoutBus_ChecksBusBeforeUnitExistence()
        {
            var (units, _, _, _) = Build(withBus: false);

            // 未知单位 + 未注入总线：先报"需要总线"的配置错误（InvalidOperationException 两种原因同型，
            // 这里用消息区分，钉住检查顺序）。
            var ex = Assert.Throws<InvalidOperationException>(() => units.SetFaction(new Id("unit.wuam_unknown"), FactionNew));

            Assert.Contains("IEventBus", ex.Message);
        }

        [Fact]
        public void SetFaction_UnknownUnit_ThrowsInvalidOperation()
        {
            var (units, _, _, seen) = Build();

            var ex = Assert.Throws<InvalidOperationException>(() => units.SetFaction(new Id("unit.wuam_unknown"), FactionNew));

            Assert.DoesNotContain("IEventBus", ex.Message);
            Assert.Empty(seen);
        }

        [Fact]
        public void SetFaction_SameFaction_IsEarlyReturn_NoEvent()
        {
            var (units, bus, hero, seen) = Build();

            units.SetFaction(HeroId, new Id(FactionOld.Value)); // 值相等的另一个 Id 实例
            bus.DispatchPending();

            Assert.Equal(FactionOld, hero.FactionId);
            Assert.Empty(seen);
        }

        [Fact]
        public void SetFaction_Different_WritesFaction_AndPublishesOneChangedEventWithOldAndNew()
        {
            var (units, bus, hero, seen) = Build();

            units.SetFaction(HeroId, FactionNew);
            bus.DispatchPending();

            Assert.Equal(FactionNew, hero.FactionId);
            Assert.Equal(FactionNew, units.GetFaction(HeroId));
            var evt = Assert.Single(seen);
            Assert.Equal(HeroId, evt.UnitId);
            Assert.Equal(FactionOld, evt.OldFactionId);
            Assert.Equal(FactionNew, evt.NewFactionId);

            // 再设回旧阵营：同样一条事件，old/new 对调。
            units.SetFaction(HeroId, FactionOld);
            bus.DispatchPending();
            Assert.Equal(2, seen.Count);
            Assert.Equal(FactionNew, seen[1].OldFactionId);
            Assert.Equal(FactionOld, seen[1].NewFactionId);
        }

        [Fact]
        public void SetLevel_WritesLevel_ReadableThroughGetLevel()
        {
            var (units, _, hero, _) = Build();
            const int target = 17;

            units.SetLevel(HeroId, target);

            Assert.Equal(target, hero.Level);
            Assert.Equal(target, units.GetLevel(HeroId));
        }

        [Fact]
        public void SetLevel_UnknownUnit_IsSilentNoOp()
        {
            var (units, _, hero, _) = Build();
            var before = hero.Level;

            var ex = Record.Exception(() => units.SetLevel(new Id("unit.wuam_unknown"), 99));

            Assert.Null(ex);
            Assert.Equal(before, hero.Level);
        }
    }
}
