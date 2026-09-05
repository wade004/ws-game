using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Xunit;

namespace Tests.Carriers.Unit
{
    /// <summary>
    /// 缺口 4：<see cref="SkillBindingHost"/>（<see cref="ISkillBindingHost"/> 默认实现）的绑定/
    /// 解绑/未知技能拒绝/事件四类行为。
    /// </summary>
    public sealed class SkillBindingHostTests
    {
        private static readonly Id UnitId = new Id("unit.hero");
        private static readonly Id SkillFireball = new Id("skill.fireball");
        private static readonly Id SkillUnknown = new Id("skill.not_learned");

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        private static SkillBindingHost Build(out List<IEvent> events, out IEventBus bus)
        {
            bus = NewBus();
            var capturedEvents = new List<IEvent>();
            bus.Subscribe(CarriersEventKeys.UnitSkillBindingChanged, e => capturedEvents.Add(e));
            events = capturedEvents;

            // 已知技能查询假实现：只有 SkillFireball 算"已学会"，其余一律未知。
            KnownSkillQuery knownSkillQuery = (unitId, skillId) => skillId.Equals(SkillFireball);
            return new SkillBindingHost(bus, knownSkillQuery);
        }

        [Fact]
        public void GetBindings_UnknownUnit_ReturnsEmptyDictionary_NotNull()
        {
            var host = Build(out _, out _);

            var bindings = host.GetBindings(UnitId);

            Assert.NotNull(bindings);
            Assert.Empty(bindings);
        }

        [Fact]
        public void Bind_KnownSkill_Succeeds_AndIsReadableViaGetBindings()
        {
            var host = Build(out var events, out var bus);

            var ok = host.Bind(UnitId, "slot_0", SkillFireball);
            bus.DispatchPending();

            Assert.True(ok);
            Assert.Equal(SkillFireball, host.GetBindings(UnitId)["slot_0"]);
            Assert.Single(events);
            var evt = Assert.IsType<UnitSkillBindingChangedEvent>(events[0]);
            Assert.Equal(UnitId, evt.UnitId);
            Assert.Equal("slot_0", evt.Slot);
            Assert.Equal((Id?)SkillFireball, evt.SkillId);
        }

        [Fact]
        public void Bind_UnknownSkill_Rejected_ReturnsFalse_NoStateChange_NoEvent()
        {
            var host = Build(out var events, out var bus);

            var ok = host.Bind(UnitId, "slot_0", SkillUnknown);
            bus.DispatchPending();

            Assert.False(ok);
            Assert.Empty(host.GetBindings(UnitId));
            Assert.Empty(events);
        }

        [Fact]
        public void Bind_SameSlotTwice_OverwritesPreviousBinding()
        {
            var host = Build(out _, out var bus);

            host.Bind(UnitId, "slot_0", SkillFireball);
            var second = host.Bind(UnitId, "slot_0", SkillFireball);
            bus.DispatchPending();

            Assert.True(second);
            Assert.Single(host.GetBindings(UnitId));
        }

        [Fact]
        public void Unbind_BoundSlot_RemovesBinding_AndEmitsEventWithNullSkillId()
        {
            var host = Build(out var events, out var bus);
            host.Bind(UnitId, "slot_0", SkillFireball);
            bus.DispatchPending();
            events.Clear();

            var ok = host.Unbind(UnitId, "slot_0");
            bus.DispatchPending();

            Assert.True(ok);
            Assert.False(host.GetBindings(UnitId).ContainsKey("slot_0"));
            Assert.Single(events);
            var evt = Assert.IsType<UnitSkillBindingChangedEvent>(events[0]);
            Assert.Null(evt.SkillId);
        }

        [Fact]
        public void Unbind_AlreadyEmptySlot_IsIdempotentSuccess_NoEvent()
        {
            var host = Build(out var events, out var bus);

            var ok = host.Unbind(UnitId, "slot_never_bound");
            bus.DispatchPending();

            Assert.True(ok);
            Assert.Empty(events);
        }
    }
}
