using Core.Foundation.Common;
using Core.Gameplay.Economy;
using Tests.Gameplay.Loot;
using Xunit;

namespace Tests.Gameplay.Economy
{
    /// <summary>
    /// 分阶段落地计划 T-N4-7 验收（ADR-0034 决策 4；08 第 7.4 节修订段"达到 econ.currency.cap 时
    /// 超出部分丢弃并发 economy.currency_overflow"）：溢出事件 2 组——<see cref="EconomyHost.Add"/>
    /// 真正把余额顶到 cap 之上而丢弃时发 <see cref="CurrencyOverflowEvent"/>（余额本身仍被夹到
    /// cap）；<see cref="EconomyHost.SetBalance"/> 即便结果同样被夹到 cap，也不发该事件。
    /// </summary>
    public sealed class T_N4_7_CurrencyOverflowTests
    {
        private const string CappedCurrencyRow =
            "[{\"id\": \"econ.currency.sample_coin\", \"name_key\": \"l10n.currency.sample.name\", " +
            "\"cap\": 100, \"display_ref\": \"display.sample_coin\"}]";

        private static readonly Id CurrencyId = new Id("econ.currency.sample_coin");
        private static readonly Id UnitId = new Id("player.sample_1");

        private static EconomyHost NewHost(out Core.Foundation.EventBus.IEventBus bus)
        {
            bus = EconomyTestSupport.NewEventBus();
            var registry = EconomyTestSupport.MakeRegistry(bus, CappedCurrencyRow, "[]");
            var inventory = new FakeInventoryHost();
            return new EconomyHost(registry, bus, inventory, new FakeNumericExprHostFactory());
        }

        [Fact]
        public void Add_ExceedsCap_DiscardsExcessAndPublishesOverflowEvent()
        {
            var host = NewHost(out var bus);

            // 先充到 80（未超 cap 100，不应发溢出事件）。
            host.Add(UnitId, CurrencyId, 80, sourceId: UnitId);

            CurrencyOverflowEvent? overflow = null;
            var overflowCount = 0;
            bus.Subscribe<CurrencyOverflowEvent>(EconomyEventKeys.CurrencyOverflow, e =>
            {
                overflow = e;
                overflowCount++;
            });

            // 再加 50：raw = 80 + 50 = 130，超出 cap 100，丢弃 30。
            host.Add(UnitId, CurrencyId, 50, sourceId: UnitId);
            bus.DispatchPending();

            Assert.Equal(100, host.GetBalance(UnitId, CurrencyId));
            Assert.Equal(1, overflowCount);
            Assert.NotNull(overflow);
            Assert.Equal(UnitId, overflow!.UnitId);
            Assert.Equal(CurrencyId, overflow.CurrencyId);
            Assert.Equal(30, overflow.Discarded);
        }

        [Fact]
        public void Add_WithinCap_DoesNotPublishOverflowEvent()
        {
            var host = NewHost(out var bus);

            var overflowCount = 0;
            bus.Subscribe<CurrencyOverflowEvent>(EconomyEventKeys.CurrencyOverflow, _ => overflowCount++);

            host.Add(UnitId, CurrencyId, 100, sourceId: UnitId); // 恰好等于 cap，未超出。
            bus.DispatchPending();

            Assert.Equal(100, host.GetBalance(UnitId, CurrencyId));
            Assert.Equal(0, overflowCount);
        }

        [Fact]
        public void SetBalance_ExceedsCap_ClampsButDoesNotPublishOverflowEvent()
        {
            var host = NewHost(out var bus);

            var overflowCount = 0;
            CurrencyChangedEvent? changed = null;
            bus.Subscribe<CurrencyOverflowEvent>(EconomyEventKeys.CurrencyOverflow, _ => overflowCount++);
            bus.Subscribe<CurrencyChangedEvent>(EconomyEventKeys.CurrencyChanged, e => changed = e);

            host.SetBalance(UnitId, CurrencyId, 150); // 夹到 100，但 SetBalance 不发溢出事件。
            bus.DispatchPending();

            Assert.Equal(100, host.GetBalance(UnitId, CurrencyId));
            Assert.Equal(0, overflowCount);
            Assert.NotNull(changed);
            Assert.Equal(0, changed!.OldValue);
            Assert.Equal(100, changed.NewValue);
        }
    }
}
