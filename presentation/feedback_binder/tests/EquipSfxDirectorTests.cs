using System;
using System.Collections.Generic;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Presentation.FeedbackBinder.Core;
using Xunit;

namespace Tests.Presentation.FeedbackBinder
{
    /// <summary>
    /// <see cref="EquipSfxDirector"/>（ADR-0153）：声明了 <c>equip_sfx_ref</c> 的模板在装备/卸装时按声明出声，位置取持有者位置；
    /// 模板来源先查注入的解析委托、退回 <c>item.added</c> 累积表；未声明与查不到的一律静默；Dispose 后不再出声。
    /// </summary>
    public sealed class EquipSfxDirectorTests
    {
        private static readonly Id Unit = new Id("unit.equip_sfx_hero");
        private static readonly Id Slot = new Id("item.slot.main");
        private static readonly Id Chime = new Id("item.chime");
        private static readonly Id Plain = new Id("item.plain");
        private static readonly Id ChimeSfx = new Id("sfx.chime_equip");
        private static readonly Id ChimeInstance = new Id("inst.chime");
        private static readonly Id PlainInstance = new Id("inst.plain");

        private static IEventBus NewBus() =>
            new EventBus(EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });

        private static readonly Dictionary<Id, Id> Declared = new Dictionary<Id, Id> { [Chime] = ChimeSfx };

        [Fact]
        public void EquipThenUnequip_Declared_PlaysDeclaredSfxAtUnitPosition_Twice()
        {
            var bus = NewBus();
            var sink = new RecordingFeedbackSink();
            var at = new Vec2(3, 4);
            var map = new Dictionary<Id, Id> { [ChimeInstance] = Chime, [PlainInstance] = Plain };
            using var director = new EquipSfxDirector(
                bus, sink, Declared, (u, i) => map.TryGetValue(i, out var t) ? t : (Id?)null, u => u.Equals(Unit) ? at : (Vec2?)null);

            bus.PublishImmediate(new ItemEquippedEvent(Unit, ChimeInstance, Slot));
            bus.PublishImmediate(new ItemUnequippedEvent(Unit, Slot, ChimeInstance));

            Assert.Equal(2, sink.PlaySfxCalls.Count);
            foreach (var call in sink.PlaySfxCalls)
            {
                Assert.Equal(ChimeSfx, call.SfxId);
                Assert.Equal(at, call.At);
            }
            Assert.Equal(2, director.PlayedCount);
        }

        [Fact]
        public void UndeclaredTemplate_And_UnknownInstance_PlayNothing()
        {
            var bus = NewBus();
            var sink = new RecordingFeedbackSink();
            var map = new Dictionary<Id, Id> { [PlainInstance] = Plain };
            using var director = new EquipSfxDirector(bus, sink, Declared, (u, i) => map.TryGetValue(i, out var t) ? t : (Id?)null);

            bus.PublishImmediate(new ItemEquippedEvent(Unit, PlainInstance, Slot));
            bus.PublishImmediate(new ItemUnequippedEvent(Unit, Slot, PlainInstance));
            bus.PublishImmediate(new ItemEquippedEvent(Unit, new Id("inst.unknown"), Slot));

            Assert.Empty(sink.PlaySfxCalls);
            Assert.Equal(0, director.PlayedCount);
        }

        [Fact]
        public void NoResolver_FallsBackToItemAddedTable()
        {
            var bus = NewBus();
            var sink = new RecordingFeedbackSink();
            using var director = new EquipSfxDirector(bus, sink, Declared);

            bus.PublishImmediate(new ItemAddedEvent(Unit, ChimeInstance, Chime, 1));
            bus.PublishImmediate(new ItemEquippedEvent(Unit, ChimeInstance, Slot));

            var call = Assert.Single(sink.PlaySfxCalls);
            Assert.Equal(ChimeSfx, call.SfxId);
            Assert.Null(call.At);
        }

        [Fact]
        public void AfterDispose_NoMorePlayback()
        {
            var bus = NewBus();
            var sink = new RecordingFeedbackSink();
            var director = new EquipSfxDirector(bus, sink, Declared, (u, i) => Chime);

            director.Dispose();
            bus.PublishImmediate(new ItemEquippedEvent(Unit, ChimeInstance, Slot));

            Assert.Empty(sink.PlaySfxCalls);
        }
    }
}
