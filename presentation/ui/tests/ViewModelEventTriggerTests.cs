using System;
using System.Collections.Generic;
using System.Linq;
using Adapters.Stub;
using Core.Carriers.Common;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Foundation.InputMap;
using Core.Foundation.Localization;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Dialog;
using Core.Gameplay.Economy;
using Core.Gameplay.Quest;
using Core.Gameplay.WorldState;
using Core.Numbers.PowerSet;
using Core.Numbers.Progression;
using Core.Numbers.StatBlock;
using Core.Rules.Common;
using Presentation.Ui;
using Xunit;
using FoundationSaveSystem = Core.Foundation.SaveSystem;

namespace Tests.PresentationUi
{
    /// <summary>
    /// 测试覆盖剩余项 T-M38：11 个视图模型的“逐订阅键触发”用例。
    /// 做法（对每个视图模型）：① 记录构造期实际订阅的事件键集合，与下方显式清单逐一相等（订阅键是契约，增删订阅必须改清单）；
    /// ② 对清单里的每个键：先在<b>不发任何事件</b>的前提下静默改变底层宿主状态，断言视图模型仍是旧快照（证明构造期
    /// 之后不会自己刷新）；再发布<b>仅这一个</b>键的事件，断言视图模型快照变成新状态（证明该键独立触发了刷新）；
    /// ③ 无关键不触发；Dispose 之后清单里的键不再触发。
    /// 宿主取舍：PauseMenu（真实 <c>AppStateHost</c>，独立总线）、SaveSlots（真实 <c>SaveSystem</c>）、Shop（真实
    /// <c>EconomyHost</c>，独立总线）、ActionBar（真实 <c>SkillBindingHost</c>）、Settings（本文件 <c>SettingsReal</c> 用例用真实
    /// <c>InputMapHost</c>）使用真实宿主；其余视图模型的真实宿主依赖整套数据注册表，其真实宿主驱动的事件刷新在
    /// <c>PresentationAssemblyViewModelEventTests</c>（装配级，全部真实宿主）里覆盖。
    /// </summary>
    public sealed class ViewModelEventTriggerTests
    {
        private static readonly Id Unrelated = new Id("found.unrelated_event");

        private sealed class RecordingDataSource : IUiDataSource
        {
            private readonly IUiDataSource _inner;
            public readonly List<Id> Keys = new List<Id>();

            public RecordingDataSource(IUiDataSource inner) => _inner = inner;

            public ExprValue? Query(string path) => _inner.Query(path);

            public SubscriptionHandle Subscribe(Id eventKey, Core.Foundation.EventBus.EventHandler handler)
            {
                Keys.Add(eventKey);
                return _inner.Subscribe(eventKey, handler);
            }

            public void Unsubscribe(SubscriptionHandle handle) => _inner.Unsubscribe(handle);
        }

        /// <summary>一个视图模型的测试夹具：<see cref="Observe"/> 给出它当前对外快照的稳定字符串，
        /// <see cref="MutateSilently"/> 在不发任何事件的前提下改变底层宿主状态，使“刷新后”的快照必然不同。</summary>
        private sealed class Case
        {
            public string Name = "";
            public IDisposable Vm = null!;
            public Func<string> Observe = null!;
            public Action MutateSilently = null!;
            public IReadOnlyList<Id> ExpectedKeys = Array.Empty<Id>();
            public UiWorldFixture World = null!;
            public RecordingDataSource Recorder = null!;
        }

        private static readonly Id Health = new Id("arch.power.health");
        private static readonly Id Fireball = new Id("skill.fireball");
        private static readonly Id StrengthStat = new Id("stat.strength");
        private static readonly Id ZhCn = new Id("l10n.zh_cn");
        private static readonly Id EnUs = new Id("l10n.en_us");

        private static string Join(params object?[] parts) => string.Join("|", parts.Select(p => p?.ToString() ?? "<null>"));

        private static Case Build(string name)
        {
            var world = new UiWorldFixture();
            var recorder = new RecordingDataSource(world.DataSource);
            var ds = (IUiDataSource)recorder;
            var c = new Case { Name = name, World = world, Recorder = recorder };
            var l10n = new FakeL10nHost(EnUs, new[] { EnUs, ZhCn });

            switch (name)
            {
                case "Hud":
                {
                    world.Progression.SetForTest(world.PlayerId, 1, 0, 100);
                    // 回合制与应用状态订阅仅在装配了调度器/应用状态时才存在：此夹具两者都装配。
                    var appState = new AppStateHost(new EventBus(
                        EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false }));
                    var scheduler = new TurnScheduler(
                        new RecordingWorldSim(), initiativeStatProvider: _ => 0.0, isPlayerActor: id => id.Equals(world.PlayerId), world.EventBus);
                    var vm = new HudViewModel(ds, world.PlayerId, new[] { Health }, scheduler, appState);
                    c.Vm = vm;
                    c.Observe = () => Join(vm.Level);
                    c.MutateSilently = () => world.Progression.SetForTest(world.PlayerId, 9, 0, 100);
                    c.ExpectedKeys = new[]
                    {
                        PowerEventKeys.Changed, PowerEventKeys.Depleted, ProgressionEventKeys.LevelUp, SaveEventKeys.SaveLoaded,
                        SimEventKeys.TurnStarted, SimEventKeys.TurnEnded, SimEventKeys.RoundEnded, SimEventKeys.AwaitingInput,
                        AppEventKeys.StateChanged,
                    };
                    break;
                }
                case "HudWithoutTurnScheduler":
                {
                    world.Progression.SetForTest(world.PlayerId, 1, 0, 100);
                    var vm = new HudViewModel(ds, world.PlayerId, new[] { Health });
                    c.Vm = vm;
                    c.Observe = () => Join(vm.Level);
                    c.MutateSilently = () => world.Progression.SetForTest(world.PlayerId, 9, 0, 100);
                    c.ExpectedKeys = new[]
                    {
                        PowerEventKeys.Changed, PowerEventKeys.Depleted, ProgressionEventKeys.LevelUp, SaveEventKeys.SaveLoaded,
                    };
                    break;
                }
                case "CharacterStats":
                {
                    var vm = new CharacterStatsViewModel(ds, l10n, world.PlayerId, new[] { (StrengthStat, new Id("l10n.stat.strength.name")) });
                    c.Vm = vm;
                    c.Observe = () => Join(vm.Entries.Select(e => e.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray());
                    c.MutateSilently = () => world.StatHost.SetBase(world.PlayerId, StrengthStat, 42);
                    c.ExpectedKeys = new[] { StatBlockEventKeys.StatChanged, SaveEventKeys.SaveLoaded };
                    break;
                }
                case "Inventory":
                {
                    var vm = new InventoryViewModel(ds, new[] { new Id("slot.main_hand") });
                    c.Vm = vm;
                    c.Observe = () => Join(vm.Slots.Count);
                    c.MutateSilently = () => world.Inventory.AddItemForTest(world.PlayerId, new Id("item.sample_sword"), 2);
                    c.ExpectedKeys = new[]
                    {
                        CarriersEventKeys.ItemAdded, CarriersEventKeys.ItemRemoved, CarriersEventKeys.ItemEquipped,
                        CarriersEventKeys.ItemUnequipped, SaveEventKeys.SaveLoaded,
                    };
                    break;
                }
                case "ActionBar":
                {
                    world.SkillBindings.Bind(world.PlayerId, ActionBarViewModel.SlotKey(0), Fireball);
                    var vm = new ActionBarViewModel(ds, world.PlayerId, 2, world.SkillBindings);
                    c.Vm = vm;
                    c.Observe = () => Join(vm.Slots[0].SkillId, vm.Slots[0].Cooldown.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    c.MutateSilently = () => world.SkillBook.SetCooldownForTest(world.PlayerId, Fireball, 3.5);
                    c.ExpectedKeys = new[]
                    {
                        RulesEventKeys.SkillCastStart, RulesEventKeys.SkillCastSuccess, RulesEventKeys.SkillCastFailed,
                        CarriersEventKeys.UnitSkillBindingChanged, SaveEventKeys.SaveLoaded,
                    };
                    break;
                }
                case "QuestLog":
                {
                    var vm = new QuestLogViewModel(ds, world.Quest, world.PlayerId);
                    c.Vm = vm;
                    c.Observe = () => Join(vm.Log.Count);
                    c.MutateSilently = () => world.Quest.SeedQuestForTest(new Id("quest.sample_q"), QuestState.Active, new[] { 0 });
                    c.ExpectedKeys = new[]
                    {
                        QuestEventKeys.Accepted, QuestEventKeys.ObjectiveProgress, QuestEventKeys.Completed, QuestEventKeys.TurnedIn,
                        QuestEventKeys.Failed, QuestEventKeys.Abandoned, SaveEventKeys.SaveLoaded,
                    };
                    break;
                }
                case "SkillBook":
                {
                    var vm = new SkillBookViewModel(ds, world.SkillBook, world.PlayerId);
                    c.Vm = vm;
                    c.Observe = () => Join(vm.Entries.Count);
                    c.MutateSilently = () => world.SkillBook.LearnForTest(world.PlayerId, Fireball);
                    c.ExpectedKeys = new[]
                    {
                        RulesEventKeys.SkillCastStart, RulesEventKeys.SkillCastSuccess, RulesEventKeys.SkillCastFailed,
                        SaveEventKeys.SaveLoaded,
                    };
                    break;
                }
                case "Dialog":
                {
                    var dialog = new FakeDialogHost();
                    var vm = new DialogViewModel(ds, dialog, world.PlayerId);
                    c.Vm = vm;
                    c.Observe = () => Join(vm.IsOpen);
                    c.MutateSilently = () => dialog.GossipViewToReturn = new GossipView(new Id("dialog.gossip_menu.sample"), Array.Empty<(int, Id)>());
                    c.ExpectedKeys = new[]
                    {
                        DialogEventKeys.StoryNodeEntered, DialogEventKeys.GossipOpened, DialogEventKeys.Ended,
                        QuestEventKeys.Accepted, QuestEventKeys.ObjectiveProgress, QuestEventKeys.Completed, QuestEventKeys.TurnedIn,
                        QuestEventKeys.Failed, CarriersEventKeys.ItemAdded, CarriersEventKeys.ItemRemoved,
                        WorldStateEventKeys.FlagChanged, SaveEventKeys.SaveLoaded,
                    };
                    break;
                }
                case "PauseMenu":
                {
                    // 真实 AppStateHost，挂在独立总线上：状态转移不会经 world.EventBus 通知视图模型（静默）。
                    var privateBus = new EventBus(
                        EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()), new EventBusOptions { StrictCatalog = false });
                    var appState = new AppStateHost(privateBus);
                    var vm = new PauseMenuViewModel(ds, appState,
                        new[] { new PauseMenuOption(new Id("shell.action.resume"), new Id("l10n.pause.resume")) });
                    c.Vm = vm;
                    c.Observe = () => Join(vm.CurrentState, vm.IsPaused);
                    c.MutateSilently = () => Assert.True(appState.RequestTransition(AppState.MainMenu));
                    c.ExpectedKeys = new[] { AppEventKeys.StateChanged };
                    break;
                }
                case "SaveSlots":
                {
                    var saveSystem = new FoundationSaveSystem.SaveSystem(
                        new StubFileSystem(), new SaveSystemOptions(new Id("game.demo")));
                    var vm = new SaveSlotsViewModel(ds, saveSystem);
                    c.Vm = vm;
                    c.Observe = () => Join(vm.Slots.Count);
                    c.MutateSilently = () => saveSystem.Save(new SaveRequest(new Id("save.slot_1"), "2026-09-05T00:00:00Z"));
                    c.ExpectedKeys = new[] { SaveEventKeys.SaveCompleted, SaveEventKeys.SaveLoaded };
                    break;
                }
                case "Settings":
                {
                    var audio = new FakeAudioLayerVolumeHost(new[] { "music" });
                    var vm = new SettingsViewModel(ds, l10n, new FakeInputMapHost(), audio);
                    c.Vm = vm;
                    c.Observe = () => Join(vm.Locale, vm.LayerVolumes["music"].ToString(System.Globalization.CultureInfo.InvariantCulture));
                    c.MutateSilently = () =>
                    {
                        l10n.SetLocale(ZhCn);
                        audio.SetVolume("music", 0.25);
                    };
                    c.ExpectedKeys = new[] { L10nEventKeys.LanguageChanged, InputMapEventKeys.RebindConflict };
                    break;
                }
                case "Shop":
                {
                    // 经济宿主挂在另一个夹具的总线上：购买产生的货币/库存事件不会经 world.EventBus 通知视图模型（静默）。
                    var econWorld = new UiWorldFixture();
                    var economy = ShopViewModelTests.BuildEconomy(econWorld);
                    var vm = new ShopViewModel(ds, economy, world.PlayerId);
                    var shop = new Id("econ.vendor.sample_shop");
                    vm.OpenVendor(shop);
                    c.Vm = vm;
                    c.Observe = () => Join(string.Join(",", vm.SellItems.Select(i => i.Stock?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<null>")));
                    c.MutateSilently = () =>
                    {
                        var result = economy.Buy(econWorld.PlayerId, shop, new Id("item.sample_sword"), 1);
                        Assert.True(result.Success, result.Reason.ToString());
                    };
                    c.ExpectedKeys = new[]
                    {
                        EconomyEventKeys.CurrencyChanged, EconomyEventKeys.VendorRestocked, EconomyEventKeys.ItemPurchased,
                        EconomyEventKeys.ItemSold, SaveEventKeys.SaveLoaded,
                    };
                    break;
                }
                default:
                    throw new ArgumentException("未知视图模型夹具：" + name);
            }

            return c;
        }

        public static IEnumerable<object[]> CaseNames() => new[]
        {
            "Hud", "HudWithoutTurnScheduler", "CharacterStats", "Inventory", "ActionBar", "QuestLog", "SkillBook",
            "Dialog", "PauseMenu", "SaveSlots", "Settings", "Shop",
        }.Select(n => new object[] { n });

        /// <summary>(夹具名, 订阅键) 的全部组合：一个键一例，失败信息直接指出是哪个视图模型的哪个键。</summary>
        public static IEnumerable<object[]> CaseAndKey()
        {
            foreach (var name in CaseNames().Select(a => (string)a[0]))
            {
                var c = Build(name);
                foreach (var key in c.ExpectedKeys)
                {
                    yield return new object[] { name, key.Value };
                }

                c.Vm.Dispose();
            }
        }

        private static void Publish(UiWorldFixture world, Id key) =>
            world.EventBus.PublishImmediate(new GenericEvent(key));

        // ------------------------------------------------------------------ 订阅键清单

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void SubscribedKeys_ExactlyMatchTheDocumentedList(string name)
        {
            var c = Build(name);

            Assert.Equal(
                c.ExpectedKeys.Select(k => k.Value).OrderBy(s => s, StringComparer.Ordinal).ToArray(),
                c.Recorder.Keys.Select(k => k.Value).OrderBy(s => s, StringComparer.Ordinal).ToArray());
            c.Vm.Dispose();
        }

        [Fact]
        public void CaseList_CoversEveryViewModelType()
        {
            var expected = typeof(HudViewModel).Assembly.GetTypes()
                .Where(t => t.IsPublic && t.IsClass && t.Namespace == "Presentation.Ui" && t.Name.EndsWith("ViewModel", StringComparison.Ordinal)
                            && typeof(IDisposable).IsAssignableFrom(t))
                .Select(t => t.Name.Substring(0, t.Name.Length - "ViewModel".Length))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
            var covered = CaseNames().Select(a => (string)a[0])
                .Where(n => n != "HudWithoutTurnScheduler")
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expected, covered); // 新增视图模型时必须同步补进本清单
        }

        // ------------------------------------------------------------------ 逐键触发

        [Theory]
        [MemberData(nameof(CaseAndKey))]
        public void EachSubscribedKey_AloneTriggersARefresh_ThatPicksUpASilentHostChange(string name, string keyValue)
        {
            var c = Build(name);
            var before = c.Observe();

            c.MutateSilently();
            Assert.Equal(before, c.Observe()); // 没有事件：快照仍是旧的，用例才有区分度。

            Publish(c.World, new Id(keyValue));

            Assert.NotEqual(before, c.Observe()); // 仅这一个键就足以触发刷新。
            c.Vm.Dispose();
        }

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void UnrelatedKey_DoesNotTriggerARefresh(string name)
        {
            var c = Build(name);
            var before = c.Observe();
            c.MutateSilently();

            Publish(c.World, Unrelated);

            Assert.Equal(before, c.Observe());
            c.Vm.Dispose();
        }

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void AfterDispose_NoSubscribedKeyTriggersARefreshAnymore(string name)
        {
            var c = Build(name);
            var before = c.Observe();
            c.MutateSilently();
            c.Vm.Dispose();

            foreach (var key in c.ExpectedKeys)
            {
                Publish(c.World, key);
            }

            Assert.Equal(before, c.Observe());
        }

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void ExplicitRefresh_PicksUpTheSameSilentChange_AsTheEventDoes(string name)
        {
            var c = Build(name);
            var before = c.Observe();
            c.MutateSilently();

            if (c.Vm is HudViewModel hud) hud.Refresh();
            else if (c.Vm is CharacterStatsViewModel stats) stats.Refresh();
            else if (c.Vm is InventoryViewModel inventory) inventory.Refresh();
            else if (c.Vm is ActionBarViewModel actionBar) actionBar.Refresh();
            else if (c.Vm is QuestLogViewModel quest) quest.Refresh();
            else if (c.Vm is SkillBookViewModel skills) skills.Refresh();
            else if (c.Vm is DialogViewModel dialog) dialog.Refresh();
            else if (c.Vm is PauseMenuViewModel pause) pause.Refresh();
            else if (c.Vm is SaveSlotsViewModel saves) saves.Refresh();
            else if (c.Vm is SettingsViewModel settings) settings.Refresh();
            else if (c.Vm is ShopViewModel shop) shop.Refresh();
            else throw new InvalidOperationException("未覆盖的视图模型类型：" + c.Vm.GetType().Name);

            Assert.NotEqual(before, c.Observe());
            c.Vm.Dispose();
        }

        // ------------------------------------------------------------------ 真实输入映射宿主

        [Fact]
        public void SettingsReal_InputMapHost_RowsAndConflictsComeFromTheRealHost_AndRebindConflictEventRefreshes()
        {
            var world = new UiWorldFixture();
            var inputMap = new InputMapHost(world.EventBus);
            inputMap.DeclareActionSet(new Id("input.set.sample"), new[]
            {
                new ActionDefinition(new Id("input.action.move_up"), ActionKind.Button, new[] { "key:w" }),
                new ActionDefinition(new Id("input.action.move_down"), ActionKind.Button, new[] { "key:s" }),
            });
            var l10n = new FakeL10nHost(EnUs, new[] { EnUs });
            using var vm = new SettingsViewModel(world.DataSource, l10n, inputMap, new FakeAudioLayerVolumeHost());

            Assert.Equal(new[] { "input.action.move_up", "input.action.move_down" }, vm.Bindings.Select(b => b.ActionName).ToArray());
            Assert.All(vm.Bindings, row => Assert.All(row.Conflicts, c => Assert.Empty(c))); // 自己不算自己的冲突。

            // 成功重绑定本身不发事件（宿主契约），刷新靠显式 Refresh；之后的行必须反映真实宿主的绑定。
            Assert.True(inputMap.Rebind("input.action.move_up", "key:up"));
            Assert.Equal(new[] { "key:w" }, vm.Bindings[0].Bindings.ToArray()); // 事件之前仍是旧的
            vm.Refresh();
            Assert.Equal(new[] { "key:up" }, vm.Bindings[0].Bindings.ToArray());

            // 冲突的重绑定：真实宿主发布 input.rebind_conflict，视图模型经事件自动刷新（先静默改另一行，再触发冲突事件）。
            Assert.True(inputMap.Rebind("input.action.move_down", "key:down")); // 静默：不触发事件
            Assert.Equal(new[] { "key:s" }, vm.Bindings[1].Bindings.ToArray());
            Assert.False(inputMap.Rebind("input.action.move_up", "key:down")); // 与 move_down 冲突 -> 事件 -> 刷新
            Assert.Equal(new[] { "key:down" }, vm.Bindings[1].Bindings.ToArray());
            Assert.Equal(new[] { "key:up" }, vm.Bindings[0].Bindings.ToArray()); // 冲突的重绑定没有改动 move_up
        }
    }
}
