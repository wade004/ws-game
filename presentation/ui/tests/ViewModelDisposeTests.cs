using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Presentation.Ui;
using Xunit;
using FoundationSaveSystem = Core.Foundation.SaveSystem;

namespace Tests.PresentationUi
{
    /// <summary>
    /// T-M31（ADR-0125）：每个 <see cref="IDisposable"/> 视图模型的 <c>Dispose</c> 必须退订它在构造期订阅的全部事件。
    /// 做法：用记录所有 <see cref="SubscriptionHandle"/> 的 <see cref="IUiDataSource"/> 包装器构造视图模型，
    /// 断言 Dispose 前句柄均为活动态、Dispose 后全部 <see cref="SubscriptionHandle.IsDisposed"/>，重复 Dispose 不抛。
    /// 另有 <see cref="InMemoryUiDiagnostics"/> 的直接用例。
    /// </summary>
    public sealed class ViewModelDisposeTests
    {
        private sealed class TrackingDataSource : IUiDataSource
        {
            private readonly IUiDataSource _inner;
            public readonly List<SubscriptionHandle> Handles = new List<SubscriptionHandle>();

            public TrackingDataSource(IUiDataSource inner) => _inner = inner;

            public ExprValue? Query(string path) => _inner.Query(path);

            public SubscriptionHandle Subscribe(Id eventKey, Core.Foundation.EventBus.EventHandler handler)
            {
                var handle = _inner.Subscribe(eventKey, handler);
                Handles.Add(handle);
                return handle;
            }

            public void Unsubscribe(SubscriptionHandle handle) => _inner.Unsubscribe(handle);
        }

        public static IEnumerable<object[]> ViewModelNames() => new[]
        {
            "Hud", "CharacterStats", "Inventory", "ActionBar", "QuestLog", "SkillBook",
            "Dialog", "PauseMenu", "SaveSlots", "Settings", "Shop", "Equipment",
        }.Select(n => new object[] { n });

        private static IDisposable Build(string name, UiWorldFixture world, IUiDataSource ds)
        {
            var l10n = new FakeL10nHost(new Id("l10n.en_us"), new[] { new Id("l10n.en_us") });
            switch (name)
            {
                case "Hud":
                    world.Progression.SetForTest(world.PlayerId, 1, 0, 100);
                    return new HudViewModel(ds, world.PlayerId, new[] { new Id("arch.power.health") });
                case "CharacterStats":
                    return new CharacterStatsViewModel(ds, l10n, world.PlayerId,
                        new[] { (new Id("stat.strength"), new Id("l10n.stat.strength.name")) });
                case "Inventory":
                    return new InventoryViewModel(ds, new[] { new Id("slot.main_hand") });
                case "ActionBar":
                    return new ActionBarViewModel(ds, world.PlayerId, 2, world.SkillBindings);
                case "QuestLog":
                    return new QuestLogViewModel(ds, world.Quest, world.PlayerId);
                case "SkillBook":
                    return new SkillBookViewModel(ds, world.SkillBook, world.PlayerId);
                case "Dialog":
                    return new DialogViewModel(ds, new FakeDialogHost(), world.PlayerId);
                case "PauseMenu":
                    return new PauseMenuViewModel(ds, new AppStateHost(world.EventBus),
                        new[] { new PauseMenuOption(new Id("shell.action.resume"), new Id("l10n.pause.resume")) });
                case "SaveSlots":
                    var saveSystem = new FoundationSaveSystem.SaveSystem(
                        new StubFileSystem(), new FoundationSaveSystem.SaveSystemOptions(new Id("game.demo")));
                    return new SaveSlotsViewModel(ds, saveSystem);
                case "Settings":
                    return new SettingsViewModel(ds, l10n, new FakeInputMapHost(), new FakeAudioLayerVolumeHost());
                case "Shop":
                    return new ShopViewModel(ds, ShopViewModelTests.BuildEconomy(world), world.PlayerId);
                case "Equipment":
                    return new EquipmentViewModel(ds, EquipmentViewModelTests.BuildRegistry(world), null);
                default:
                    throw new ArgumentException("未知视图模型：" + name);
            }
        }

        [Theory]
        [MemberData(nameof(ViewModelNames))]
        public void Dispose_UnsubscribesEveryHandle_AndIsIdempotent(string name)
        {
            var world = new UiWorldFixture();
            var tracking = new TrackingDataSource(world.DataSource);
            var vm = Build(name, world, tracking);

            Assert.NotEmpty(tracking.Handles); // 每个视图模型都订阅了事件（否则本用例无意义）
            Assert.All(tracking.Handles, h => Assert.False(h.IsDisposed));

            vm.Dispose();

            Assert.All(tracking.Handles, h => Assert.True(h.IsDisposed));
            vm.Dispose(); // 重复 Dispose 不抛
        }

        [Fact]
        public void ViewModelNames_CoverEveryDisposableViewModelType()
        {
            var expected = typeof(HudViewModel).Assembly.GetTypes()
                .Where(t => t.IsPublic && t.IsClass && t.Namespace == "Presentation.Ui" && t.Name.EndsWith("ViewModel", StringComparison.Ordinal)
                            && typeof(IDisposable).IsAssignableFrom(t))
                .Select(t => t.Name.Substring(0, t.Name.Length - "ViewModel".Length))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
            var covered = ViewModelNames().Select(a => (string)a[0]).OrderBy(n => n, StringComparer.Ordinal).ToArray();

            Assert.Equal(expected, covered); // 新增视图模型时必须同步补进本清单
        }

        // ---- InMemoryUiDiagnostics ----

        [Fact]
        public void InMemoryUiDiagnostics_RecordsWarningsInOrder_AndNullBecomesEmptyString()
        {
            var diagnostics = new InMemoryUiDiagnostics();
            Assert.Empty(diagnostics.Warnings);

            diagnostics.Warn("first");
            diagnostics.Warn(null!);
            diagnostics.Warn("third");

            Assert.Equal(new[] { "first", string.Empty, "third" }, diagnostics.Warnings);
        }
    }
}
