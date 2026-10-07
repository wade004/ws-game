using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.EventBus;
using Core.Foundation.SaveSystem;
using Xunit;
using SaveSys = Core.Foundation.SaveSystem.SaveSystem;

namespace Tests.Foundation.SaveSystem
{
    /// <summary>
    /// P4 备忘 7（样板游戏 A 反馈）：新游戏需要"把全部持久化域清回从未发生过的默认态"的框架入口——此前游戏只能在装配完成时
    /// 先存一份"原始槽"、新游戏再读回来。<see cref="ISaveSystem.ResetAllSections"/> 复用读档同一条通路里"段缺失"的既定语义：
    /// 对每个已登记段按读档同序调用一次 <c>Load(JsonNull)</c>。红：接口上没有该入口（编译不过）；不变量：
    /// 复位后每段收到的都是 JsonNull、顺序与读档一致、声明"缺失即保留"的段不动、派生钩子照读档那样回调、期间业务事件被抑制。
    /// </summary>
    public sealed class SaveSystemResetTests
    {
        private static readonly Id GameId = new Id("game.demo");

        private sealed class Section : IPersistable
        {
            private readonly List<string> _log;

            public string SectionKey { get; }

            public JsonValue Value { get; private set; }

            public bool KeepStateWhenSectionMissing { get; set; }

            public bool ThrowOnLoad { get; set; }

            public Section(string key, string initial, List<string> log)
            {
                SectionKey = key;
                Value = new JsonString(initial);
                _log = log;
            }

            public JsonValue Save() => Value;

            public void Load(JsonValue data)
            {
                _log.Add("load:" + SectionKey);
                if (ThrowOnLoad)
                {
                    throw new InvalidOperationException("boom:" + SectionKey);
                }

                Value = data;
            }
        }

        private sealed class Rebuilder : IDerivedStateRebuilder
        {
            public int BeforeLoadCount;

            public readonly List<string> Sections = new List<string>();

            public void BeforeLoad() => BeforeLoadCount++;

            public void OnSectionLoaded(string sectionKey) => Sections.Add(sectionKey);
        }

        private static SaveSys CreateSut(IEventBus? bus = null) =>
            new SaveSys(new StubFileSystem(), new SaveSystemOptions(GameId), bus);

        [Fact]
        public void ResetAllSections_LoadsJsonNullIntoEverySection_InReadOrder_AndReturnsTrue()
        {
            var log = new List<string>();
            var sut = CreateSut();
            var b = new Section("custom.b", "dirty-b", log);
            var a = new Section("custom.a", "dirty-a", log);
            sut.RegisterPersistable(b);
            sut.RegisterPersistable(a);

            var ok = sut.ResetAllSections();

            Assert.True(ok);
            Assert.IsType<JsonNull>(a.Value);
            Assert.IsType<JsonNull>(b.Value);
            Assert.Equal(new[] { "load:custom.a", "load:custom.b" }, log);
        }

        [Fact]
        public void ResetAllSections_KeepStateWhenSectionMissing_SectionIsLeftAlone()
        {
            var log = new List<string>();
            var sut = CreateSut();
            var keeper = new Section("custom.keep", "keep-me", log) { KeepStateWhenSectionMissing = true };
            var other = new Section("custom.other", "dirty", log);
            sut.RegisterPersistable(keeper);
            sut.RegisterPersistable(other);

            Assert.True(sut.ResetAllSections());

            Assert.Equal("keep-me", ((JsonString)keeper.Value).Value);
            Assert.IsType<JsonNull>(other.Value);
            Assert.DoesNotContain("load:custom.keep", log);
        }

        [Fact]
        public void ResetAllSections_RunsDerivedStateRebuilderLikeLoad()
        {
            var log = new List<string>();
            var sut = CreateSut();
            var rebuilder = new Rebuilder();
            sut.RegisterPersistable(new Section("custom.a", "x", log));
            sut.RegisterPersistable(new Section("custom.b", "y", log));
            sut.SetDerivedStateRebuilder(rebuilder);

            Assert.True(sut.ResetAllSections());

            Assert.Equal(1, rebuilder.BeforeLoadCount);
            Assert.Equal(new[] { "custom.a", "custom.b" }, rebuilder.Sections);
        }

        [Fact]
        public void ResetAllSections_OneSectionThrows_OthersStillReset_ReturnsFalse_AndReportsDiagnostics()
        {
            var log = new List<string>();
            var diagnostics = new InMemorySaveDiagnostics();
            var sut = new SaveSys(new StubFileSystem(), new SaveSystemOptions(GameId), null, diagnostics);
            var bad = new Section("custom.a", "x", log) { ThrowOnLoad = true };
            var good = new Section("custom.b", "y", log);
            sut.RegisterPersistable(bad);
            sut.RegisterPersistable(good);

            var ok = sut.ResetAllSections();

            Assert.False(ok);
            Assert.IsType<JsonNull>(good.Value);
            Assert.NotEmpty(diagnostics.Errors);
        }

        [Fact]
        public void ResetAllSections_SuppressesBusinessEventsDuringReset()
        {
            var catalog = EventCatalog.FromDefinitions(new[] { new EventDefinition(new Id("test.reset_probe"), "test", new[] { "n" }) });
            var bus = new EventBus(catalog);
            var seen = 0;
            using var _ = bus.Subscribe(new Id("test.reset_probe"), e => seen++);
            var sut = CreateSut(bus);
            sut.RegisterPersistable(new ProbeSection(bus));

            Assert.True(sut.ResetAllSections());
            bus.DispatchPending();

            Assert.Equal(0, seen);
        }

        private sealed class ProbeEvent : IEvent
        {
            public Id Key => new Id("test.reset_probe");
        }

        private sealed class ProbeSection : IPersistable
        {
            private readonly IEventBus _bus;

            public ProbeSection(IEventBus bus) => _bus = bus;

            public string SectionKey => "custom.probe";

            public JsonValue Save() => JsonNull.Instance;

            public void Load(JsonValue data) => _bus.Enqueue(new ProbeEvent());
        }
    }
}
