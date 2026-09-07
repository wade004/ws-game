using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SceneRouter;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Rules.Common;
using Core.Foundation.EngineAdapter;
using Presentation.Assembly;
using Presentation.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.Render;
using Presentation.VfxSfx.Contracts;
using Xunit;

namespace Tests.Presentation.Assembly
{
    /// <summary>
    /// 阶段 4 收敛 B 烟雾测试（惯例同 <c>core/gameplay/assembly/tests/GameplayAssemblyTests.cs</c>）：
    /// <see cref="PresentationAssembly"/> 按 <see cref="PresentationSchemaCatalog.RegisterAll"/> 注册的
    /// 全部 L0～L5 schema 构造一份含最小表现层示例数据的 <see cref="DataRegistry"/>，验证构造期全部
    /// 装配（十个 UI 视图模型 + Shell + FeedbackBinder + Camera/Vfx/Sfx/ViewBinder）不抛异常、
    /// 一次伤害事件经 <see cref="Presentation.FeedbackBinder.Core.FeedbackBinder"/> 落到飘字回调、
    /// 以及 <see cref="PresentationAssembly.Dispose"/> 退订后同一事件不再触发回调。
    /// </summary>
    public class PresentationAssemblyTests
    {
        private static readonly Id SamplePlayerTemplateId = new Id("creature.sample_player");
        private static readonly Id SampleMapId = new Id("world.sample_map");

        /// <summary>判断记录：<c>Presentation.Ui.HudViewModel</c>/<c>PlayerPathProvider</c> 构造期
        /// 立即 <c>Refresh()</c>，会真的调用 <c>IProgressionHost.GetLevel</c>/<c>IPowerHost.GetPower</c>
        /// ——与 <see cref="GameplayAssemblyTests"/> 那种"裸 Id、从不实际查询"的最小夹具不同，本测试
        /// 必须让 <c>PlayerUnitProvider()</c> 返回的单位真正经 <c>Stats</c>/<c>Powers</c>/
        /// <c>Progression</c> 三处注册——最省事的路径是经 <see cref="Core.Carriers.Creature.CreatureFactory.Spawn"/>
        /// 生成一个真实生物单位当"玩家"用（该方法内部一次性调用 <c>_stats.RegisterUnit</c>/
        /// <c>_progression.RegisterUnit</c>/<c>_powers.RegisterUnit</c> 三者），而不是手工拼接三份
        /// 注册调用，惯例同 <c>core/carriers/creature/tests/CreatureTestSupport</c>。</summary>
        private static void AddMinimalGameplayTables(InMemoryDataSource source)
        {
            source.Add("item.budget_curve",
                "{\"table\": \"item.budget_curve\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"item.budget.default\", \"entries\": [{\"item_level\": 1, \"budget\": 10}]}" +
                "]}");
            source.Add("stat.definition",
                "{\"table\": \"stat.definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"stat.max_health\", \"name_key\": \"l10n.stat.sample_max_health.name\", \"group\": \"primary\", \"default_base\": 0}" +
                "]}");
            source.Add("combat.hit_table_config",
                "{\"table\": \"combat.hit_table_config\", \"schema_version\": 1, \"rows\": []}");
            source.Add("combat.resist_curve",
                "{\"table\": \"combat.resist_curve\", \"schema_version\": 1, \"rows\": []}");
            source.Add("arch.power_type",
                "{\"table\": \"arch.power_type\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"arch.power.health\", \"name_key\": \"l10n.power.sample_health.name\", " +
                "\"max_source\": {\"kind\": \"stat\", \"stat\": \"stat.max_health\"}}" +
                "]}");
            source.Add("prog.level_curve",
                "{\"table\": \"prog.level_curve\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"prog.sample_curve\", \"max_level\": 1, \"entries\": [" +
                "{\"level\": 1, \"xp_to_next\": 100, \"growth\": {}}" +
                "]}]}");
            source.Add("creature.tier_definition",
                "{\"table\": \"creature.tier_definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"creature.tier.sample_normal\", \"name_key\": \"l10n.creature.tier.sample_normal.name\", " +
                "\"stat_multiplier\": 1, \"control_immune\": false, \"sort_weight\": 0}" +
                "]}");
            source.Add("creature.template",
                "{\"table\": \"creature.template\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"creature.sample_player\", \"name_key\": \"l10n.creature.sample_player.name\", " +
                "\"level\": 1, \"tier\": \"creature.tier.sample_normal\", " +
                "\"base_stats\": {\"stat.max_health\": 100}, \"stat_growth_ref\": \"prog.sample_curve\", " +
                "\"faction_id\": \"fac.sample_player\", \"display_ref\": \"display.sample_player\"}" +
                "]}");
        }

        private static void AddMinimalPresentationTables(InMemoryDataSource source)
        {
            source.Add("l10n.locale",
                "{\"table\": \"l10n.locale\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"l10n.locale.sample_default\", \"is_default\": true}" +
                "]}");
            source.Add("vfx.def",
                "{\"table\": \"vfx.def\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"vfx.sample_hit\", \"category\": \"impact\", \"attach_mode\": \"world\", \"resource_ref\": \"vfx.sample_hit\"}" +
                "]}");
            source.Add("sfx.def",
                "{\"table\": \"sfx.def\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"sfx.sample_hit\", \"layer\": \"combat\", \"resource_ref\": \"sfx.sample_hit\"}" +
                "]}");
            source.Add("display.weapon_style",
                "{\"table\": \"display.weapon_style\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"display.weapon_style.sample_sword\", \"auto_attack_anim\": \"anim.sample_sword.auto_attack\"}" +
                "]}");
            source.Add("feedback.binding",
                "{\"table\": \"feedback.binding\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"feedback.sample_damage_text\", \"event\": \"combat.damage_dealt\", \"actions\": [" +
                "{\"kind\": \"floating_text\", \"params\": {\"style_id\": \"feedback.floating_text_style.sample\", \"text_source\": \"amount\"}}" +
                "]}," +
                // 缺口 6 恢复用例（FlashAction_DefaultWiring_RoutesThroughCharacterRig_WhenViewHasRig）：
                // 对同一事件补一条 flash 规则，target=source（伤害来源自身受击闪白）。
                "{\"id\": \"feedback.sample_damage_flash\", \"event\": \"combat.damage_dealt\", \"actions\": [" +
                "{\"kind\": \"flash\", \"params\": {\"profile_id\": \"feedback.flash.sample\", \"target\": \"source\"}}" +
                "]}]}");
            source.Add("feedback.floating_text_style",
                "{\"table\": \"feedback.floating_text_style\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"feedback.floating_text_style.sample\", \"color_ref\": \"color.white\"}" +
                "]}");
            source.Add("camera_profile",
                "{\"table\": \"camera_profile\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"camera_profile.sample_default\", \"pitch_degrees\": 45, \"yaw_degrees\": 0, " +
                "\"zoom_min\": 5, \"zoom_max\": 15, \"zoom_default\": 10, \"follow_lerp\": 0.2}" +
                "]}");
            source.Add("ui_layout_definition",
                "{\"table\": \"ui_layout_definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"ui_layout_definition.sample_action_bar\", \"panel\": \"action_bar\", \"slots\": 6, \"fields\": {}}" +
                "]}");
            source.Add("shell_menu_definition",
                "{\"table\": \"shell_menu_definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"shell_menu_definition.sample_main\", \"entries\": [" +
                "{\"id\": \"shell_menu_definition.sample_main.new_game\", \"text_key\": \"l10n.shell.sample_new_game\", \"action\": \"new_game\"}" +
                "]}]}");
            // 拍板 7（商店 UI）：ShopViewModelTests 用例需要的最小 econ.currency/econ.vendor 数据。
            source.Add("econ.currency",
                "{\"table\": \"econ.currency\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"econ.currency.sample_gold\", \"name_key\": \"l10n.currency.sample_gold.name\", \"display_ref\": \"display.currency.sample_gold\"}" +
                "]}");
            source.Add("econ.vendor",
                "{\"table\": \"econ.vendor\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"econ.vendor.sample_general\", \"name_key\": \"l10n.vendor.sample_general.name\", \"sell_items\": [" +
                "{\"item_id\": \"item.sample_potion\", \"price_currency_id\": \"econ.currency.sample_gold\", \"price_amount\": 10}" +
                "]}]}");
        }

        private static PresentationAssembly Build(
            out GameplayAssembly gameplay, out WorldSim world, out StubEngine engine, out IEventBus bus,
            PresentationAssemblyOptions? options = null, IViewFactory? viewFactory = null,
            bool withResourceLoader = false)
        {
            bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();
            AddMinimalGameplayTables(source);
            AddMinimalPresentationTables(source);

            var registryOptions = PresentationSchemaCatalog.CreateOptions();
            registryOptions.FailOnUnknownTable = false;
            var registry = new DataRegistry(source, bus, registryOptions);
            PresentationSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var playerFaction = new Id("fac.sample_player");

            // 判断记录（缺口 16，ISaveSystem 归 GameplayAssembly 持有）：engine 需要提前构造出来，
            // 才能拿到 engine.FileSystem 传给 GameplayAssembly 的 saveSystem 参数——PresentationAssembly
            // 现在改为直接读 gameplay.SaveSystem，不再自行用 engine.FileSystem 另建一份。
            engine = new StubEngine();
            var saveSystem = new Core.Foundation.SaveSystem.SaveSystem(
                engine.FileSystem, new SaveSystemOptions(new Id("game.unspecified")), bus);

            Id playerId = default;
            gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => playerId,
                playerFactionId: playerFaction);

            // 判断记录：见 AddMinimalGameplayTables 类型注释——用 CreatureFactory.Spawn 生成一个
            // 真正经 Stats/Powers/Progression 三处注册的单位当"玩家"，PlayerUnitProvider 闭包捕获
            // 的 playerId 在此赋值后，后续调用（包括即将构造的 PresentationAssembly）都会取到它。
            playerId = gameplay.Carriers.Creatures.Spawn(SamplePlayerTemplateId, SampleMapId, Vec2.Zero, 0.0);

            viewFactory ??= new Tests.PresentationViewBinding.FakeViewFactory();
            var sceneRouter = new SceneRouter(registry, engine.ResourceLoader, gameplay.AppState, world, gameplay.Hooks, bus);
            var presentationRng = new RngHost(2);

            // GP-PRES-04 收口：默认仍不传（withResourceLoader=false），保持既有测试行为逐字节不变；
            // 需要断言"首次引用触发 LoadAsync"的用例显式传 true，拿到 engine.ResourceLoader
            // （StubResourceLoader，可观察 LoadRequests）。
            return new PresentationAssembly(
                gameplay, world, registry, bus, presentationRng, viewFactory,
                engine.Renderer2D, engine.Camera, engine.Audio, engine.FileSystem, sceneRouter, options,
                resourceLoader: withResourceLoader ? engine.ResourceLoader : null);
        }

        [Fact]
        public void Construct_WithMinimalPresentationDataset_DoesNotThrow_AndExposesAllHosts()
        {
            var presentation = Build(out _, out _, out _, out _);

            Assert.NotNull(presentation.DisplayInfo);
            Assert.NotNull(presentation.ViewBinder);
            Assert.NotNull(presentation.Render);
            Assert.NotNull(presentation.Camera);
            Assert.NotNull(presentation.Vfx);
            Assert.NotNull(presentation.Sfx);
            Assert.NotNull(presentation.WeaponStyle);
            Assert.NotNull(presentation.Feedback);
            Assert.NotNull(presentation.InputMap);
            Assert.NotNull(presentation.L10n);
            Assert.NotNull(presentation.UiData);
            Assert.NotNull(presentation.UiIntents);
            Assert.NotNull(presentation.Hud);
            Assert.NotNull(presentation.ActionBar);
            Assert.NotNull(presentation.Inventory);
            Assert.NotNull(presentation.QuestLog);
            Assert.NotNull(presentation.DialogView);
            Assert.NotNull(presentation.SkillBook);
            Assert.NotNull(presentation.CharacterStats);
            Assert.NotNull(presentation.Shop);
            Assert.NotNull(presentation.Settings);
            Assert.NotNull(presentation.SaveSlots);
            Assert.NotNull(presentation.PauseMenu);
            Assert.NotNull(presentation.SaveSystem);
            Assert.NotNull(presentation.SettingsStore);
            Assert.NotNull(presentation.Shell);
            Assert.NotNull(presentation.ShellViewModel);

            // 6 个动作条槽位来自 ui_layout_definition 数据（AddMinimalPresentationTables），
            // 不是 PresentationAssemblyOptions 的兜底默认值 8。
            Assert.Equal(6, presentation.ActionBar.SlotCount);
        }

        [Fact]
        public void DamageEvent_DispatchesFloatingText_ViaFeedbackBinder()
        {
            var floatingTexts = new List<(Id EntityId, Id StyleId, string Text)>();
            var options = new PresentationAssemblyOptions
            {
                OnFloatingText = (entityId, styleId, text) => floatingTexts.Add((entityId, styleId, text)),
            };

            var presentation = Build(out _, out _, out _, out var bus, options);

            var evt = new CombatDamageDealtEvent(
                new Id("unit.smoke_player"), new Id("unit.smoke_target"), new Id("skill.school.physical"),
                17.0, isCrit: false, HitResult.Hit);
            bus.PublishImmediate(evt);

            Assert.Single(floatingTexts);
            Assert.Equal(new Id("unit.smoke_target"), floatingTexts[0].EntityId);
            Assert.Equal(new Id("feedback.floating_text_style.sample"), floatingTexts[0].StyleId);
            Assert.Equal("17", floatingTexts[0].Text);
        }

        [Fact]
        public void Dispose_UnsubscribesFeedbackBinder_SameEventNoLongerDispatches()
        {
            var floatingTexts = new List<(Id EntityId, Id StyleId, string Text)>();
            var options = new PresentationAssemblyOptions
            {
                OnFloatingText = (entityId, styleId, text) => floatingTexts.Add((entityId, styleId, text)),
            };

            var presentation = Build(out _, out _, out _, out var bus, options);

            var evt = new CombatDamageDealtEvent(
                new Id("unit.smoke_player"), new Id("unit.smoke_target"), new Id("skill.school.physical"),
                17.0, isCrit: false, HitResult.Hit);
            bus.PublishImmediate(evt);
            Assert.Single(floatingTexts);

            presentation.Dispose();
            bus.PublishImmediate(evt);

            // 退订后同一事件不应再触发 FeedbackBinder 的飘字动作：计数保持不变。
            Assert.Single(floatingTexts);

            // Dispose 幂等，不应抛异常。
            presentation.Dispose();
        }

        [Fact]
        public void RegisterAll_LoadsFullPresentationDataset_ZeroErrorsAndWarnings()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();
            AddMinimalGameplayTables(source);
            AddMinimalPresentationTables(source);

            var registryOptions = PresentationSchemaCatalog.CreateOptions();
            registryOptions.FailOnUnknownTable = false;
            var registry = new DataRegistry(source, bus, registryOptions);
            PresentationSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();

            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));
            // 只断言零 Error：本夹具刻意不加载 l10n.text（本测试与本类其余用例一样，不覆盖本地化
            // 文本内容本身），会产生"文本键存在性检查因 l10n.text 未加载而跳过"的 Warning，这是
            // 预期的、无害的诊断，不是需要清零的校验错误。
            Assert.DoesNotContain(report.Issues, i => i.Severity == ValidationSeverity.Error);
        }

        [Fact]
        public void CreateOptions_UsesSameExprSchemaAsGameplaySchemaCatalog()
        {
            Assert.Same(Core.Gameplay.Assembly.GameplaySchemaCatalog.FullExprSchema, PresentationSchemaCatalog.FullExprSchema);
            Assert.Same(PresentationSchemaCatalog.FullExprSchema, PresentationSchemaCatalog.CreateOptions().ExprSchema);
        }

        [Fact]
        public void ActionBar_FallsBackToOptionsSlotCount_WhenNoLayoutRowPresent()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();
            AddMinimalGameplayTables(source);
            // l10n.locale 是 L10nHost 构造期的硬性前提（见 AddMinimalPresentationTables 同款行），
            // 单独补一份；其余不调用 AddMinimalPresentationTables——数据集里没有任何
            // ui_layout_definition 行，ActionBar 槽位数应退化为
            // PresentationAssemblyOptions.ActionBarSlotCountFallback。
            source.Add("l10n.locale",
                "{\"table\": \"l10n.locale\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"l10n.locale.sample_default\", \"is_default\": true}" +
                "]}");

            var registryOptions = PresentationSchemaCatalog.CreateOptions();
            registryOptions.FailOnUnknownTable = false;
            var registry = new DataRegistry(source, bus, registryOptions);
            PresentationSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var engine = new StubEngine();
            var saveSystem = new Core.Foundation.SaveSystem.SaveSystem(
                engine.FileSystem, new SaveSystemOptions(new Id("game.unspecified")), bus);
            Id playerId = default;
            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => playerId, playerFactionId: new Id("fac.sample_player"));
            playerId = gameplay.Carriers.Creatures.Spawn(SamplePlayerTemplateId, SampleMapId, Vec2.Zero, 0.0);

            var viewFactory = new Tests.PresentationViewBinding.FakeViewFactory();
            var sceneRouter = new SceneRouter(registry, engine.ResourceLoader, gameplay.AppState, world, gameplay.Hooks, bus);

            var options = new PresentationAssemblyOptions { ActionBarSlotCountFallback = 4 };
            var presentation = new PresentationAssembly(
                gameplay, world, registry, bus, new RngHost(2), viewFactory,
                engine.Renderer2D, engine.Camera, engine.Audio, engine.FileSystem, sceneRouter, options);

            Assert.Equal(4, presentation.ActionBar.SlotCount);
        }

        [Fact]
        public void WeaponStyle_ResolvesSwingVfx_FromSampleData()
        {
            var presentation = Build(out _, out _, out _, out _);

            // AddMinimalPresentationTables 的 display.weapon_style 样例行未声明 swing_vfx，
            // 解析应得到 null 而不是抛异常（判断记录：验证 WeaponStyleResolver 真正接的是本装配根
            // 构建出的目录，不是空字典）。
            Assert.Null(presentation.WeaponStyle.ResolveSwingVfx(new Id("display.weapon_style.sample_sword")));
        }

        // ------------------------------------------------------------------
        // 拍板 7：商店 UI（ShopViewModel）。
        // ------------------------------------------------------------------

        [Fact]
        public void Shop_NoVendorOpen_SellItemsEmpty()
        {
            var presentation = Build(out _, out _, out _, out _);

            Assert.Null(presentation.Shop.CurrentVendorId);
            Assert.Empty(presentation.Shop.SellItems);
        }

        [Fact]
        public void Shop_OpenVendor_ListsSellItems_FromSampleData()
        {
            var presentation = Build(out _, out _, out _, out _);

            presentation.Shop.OpenVendor(new Id("econ.vendor.sample_general"));

            Assert.Equal(new Id("econ.vendor.sample_general"), presentation.Shop.CurrentVendorId);
            var item = Assert.Single(presentation.Shop.SellItems);
            Assert.Equal(new Id("item.sample_potion"), item.ItemId);
            Assert.Equal(new Id("econ.currency.sample_gold"), item.PriceCurrencyId);
            Assert.Equal(10, item.PriceAmount);
            Assert.Null(item.Stock); // sample 数据未声明 stock_limit，视为不限量。
        }

        [Fact]
        public void Shop_UnknownVendorId_DegradesToEmptyShelf_NoThrow()
        {
            var presentation = Build(out _, out _, out _, out _);

            var ex = Record.Exception(() => presentation.Shop.OpenVendor(new Id("econ.vendor.unknown")));

            Assert.Null(ex);
            Assert.Empty(presentation.Shop.SellItems);
        }

        [Fact]
        public void Shop_CloseVendor_ClearsSellItemsAndCurrentVendorId()
        {
            var presentation = Build(out _, out _, out _, out _);
            presentation.Shop.OpenVendor(new Id("econ.vendor.sample_general"));

            presentation.Shop.CloseVendor();

            Assert.Null(presentation.Shop.CurrentVendorId);
            Assert.Empty(presentation.Shop.SellItems);
        }

        [Fact]
        public void Shop_GetPlayerBalance_ReflectsEconomyHost()
        {
            var presentation = Build(out var gameplay, out _, out _, out _);
            var playerId = gameplay.PlayerUnitProvider();
            gameplay.Economy.RegisterUnit(playerId);
            gameplay.Economy.Add(playerId, new Id("econ.currency.sample_gold"), 50, new Id("test.grant"));

            Assert.Equal(50, presentation.Shop.GetPlayerBalance(new Id("econ.currency.sample_gold")));
        }

        [Fact]
        public void Shop_CurrencyChangedEvent_RefreshesOpenShelf_StockReflectsPurchase()
        {
            var presentation = Build(out var gameplay, out _, out _, out var bus);
            var playerId = gameplay.PlayerUnitProvider();
            presentation.Shop.OpenVendor(new Id("econ.vendor.sample_general"));

            // economy.currency_changed 只是本用例用来触发一次 Refresh 的信号事件之一（Refresh 本身是
            // 幂等的全量重建，具体触发源不影响断言——这里验证的是订阅确实生效、Refresh 被调用后货架
            // 内容仍然正确，不是"库存因为这个事件而减少"这种因果关系）。
            bus.PublishImmediate(new Core.Gameplay.Economy.CurrencyChangedEvent(
                playerId, new Id("econ.currency.sample_gold"), 0, 10));

            var item = Assert.Single(presentation.Shop.SellItems);
            Assert.Equal(new Id("item.sample_potion"), item.ItemId);
        }

        [Fact]
        public void FloatingTextStyles_ContainsSampleStyle_WithColorRef()
        {
            var presentation = Build(out _, out _, out _, out _);

            Assert.True(presentation.FloatingTextStyles.TryGetValue(
                new Id("feedback.floating_text_style.sample"), out var style));
            Assert.Equal(new Id("color.white"), style!.ColorRef);
        }

        [Fact]
        public void SaveSlots_ListsNoSlots_OnFreshFileSystem()
        {
            var presentation = Build(out _, out _, out _, out _);

            Assert.Empty(presentation.SaveSystem.ListSlots());
            Assert.Empty(presentation.SaveSlots.Slots);
        }

        // ------------------------------------------------------------------
        // 缺口 6 恢复：FeedbackAction.Flash 默认改走 ICharacterRig.ProceduralAnim（见
        // PresentationAssemblyOptions.OnFlash/FlashProfileResolver 判断记录）。
        // ------------------------------------------------------------------

        private sealed class RecordingProceduralAnim : IProceduralAnim
        {
            public readonly List<FlashParams> Flashes = new List<FlashParams>();

            public void Move(MoveParams p, System.Action<Vec2>? s = null, System.Action? c = null) { }
            public void Rotate(RotateParams p, System.Action<double>? s = null, System.Action? c = null) { }
            public void Scale(ScaleParams p, System.Action<double>? s = null, System.Action? c = null) { }
            public void Flash(FlashParams p, System.Action<double>? s = null, System.Action? c = null) => Flashes.Add(p);
            public void Trail(TrailParams p, System.Action<double>? s = null, System.Action? c = null) { }
            public void Stagger(StaggerParams p, System.Action<Vec2>? s = null, System.Action? c = null) { }
            public void Topple(ToppleParams p, System.Action<double>? s = null, System.Action? c = null) { }
            public void Fade(FadeParams p, System.Action<double>? s = null, System.Action? c = null) { }
        }

        private sealed class RecordingCharacterRig : ICharacterRig
        {
            public readonly RecordingProceduralAnim Anim = new RecordingProceduralAnim();

            public Id EntityId { get; }
            public AnimState CurrentAnimState { get; private set; } = AnimState.Idle;
            public IProceduralAnim ProceduralAnim => Anim;

#pragma warning disable CS0067 // 本假实现只需满足 ICharacterRig 接口形状，测试不需要真正触发命中帧事件。
            public event System.Action<Id>? HitFrameReached;
#pragma warning restore CS0067

            public RecordingCharacterRig(Id entityId) => EntityId = entityId;

            public void SetAnimState(AnimState state) => CurrentAnimState = state;
            public void PlayClip(Id clipId, bool loop = false, double speed = 1.0) { }
            public Vec2? ResolveAnchorLocalOffset(Id anchorId, Direction facing) => null;
            public void ComposeAndApplyLayers(IReadOnlyList<string> names, Direction facing, System.Func<SpriteLayerPlacement, Id> resolve) { }
            public void ApplyLayers(IReadOnlyList<Id> resourceIds) { }
            public void Update(double dt) { }
        }

        private sealed class RecordingRigView : IView, IHasCharacterRig
        {
            public ICharacterRig Rig { get; }
            public Id EntityId { get; private set; }
            public bool IsAlive { get; private set; }

            public RecordingRigView(Id entityId) => Rig = new RecordingCharacterRig(entityId);

            public void Bind(Id entityId)
            {
                EntityId = entityId;
                IsAlive = true;
            }

            public void OnEvent(IEvent evt) { }
            public void SyncPose(Vec2 pos, Direction facing, double height) { }
            public void Destroy() => IsAlive = false;
        }

        private sealed class RigViewFactory : IViewFactory
        {
            public RecordingRigView? LastCreated;

            public IView CreateView(ViewKind kind, Id displayId, Id entityId)
            {
                LastCreated = new RecordingRigView(entityId);
                return LastCreated;
            }
        }

        [Fact]
        public void FlashAction_DefaultWiring_RoutesThroughCharacterRig_WhenViewHasRig()
        {
            var rigFactory = new RigViewFactory();
            var presentation = Build(out var gameplay, out _, out _, out var bus, viewFactory: rigFactory);
            var playerId = gameplay.PlayerUnitProvider();

            // Build() 内部先 Spawn 玩家单位、后构造 PresentationAssembly（ViewBinder 此时才开始订阅
            // entity.created），玩家真正的创建事件已经错过；OnEntityCreated 本就 public（供场景切换
            // 等"实体已存在、View 需要重建"场景调用），这里直接补一次绑定。
            presentation.ViewBinder.OnEntityCreated(playerId, "player", new Id("display.sample_player"));
            Assert.NotNull(rigFactory.LastCreated);
            var rig = Assert.IsType<RecordingCharacterRig>(rigFactory.LastCreated!.Rig);

            var evt = new CombatDamageDealtEvent(
                playerId, new Id("unit.smoke_target"), new Id("skill.school.physical"), 5.0, isCrit: false, HitResult.Hit);
            bus.PublishImmediate(evt);

            var flash = Assert.Single(rig.Anim.Flashes);
            Assert.Equal(FlashParams.Default.Intensity, flash.Intensity);
            Assert.Equal(FlashParams.Default.DurationSeconds, flash.DurationSeconds);
        }

        // ------------------------------------------------------------------
        // 拍板 5：离散回放门——FeedbackQueueMode 跟随 gameplay.TimeModelSwitch 自动切换。
        // ------------------------------------------------------------------

        [Fact]
        public void FeedbackQueueMode_AutoSwitchesWithTimeModel_SequentialInDiscreteCombat_ImmediateOnceBack()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();
            AddMinimalGameplayTables(source);
            AddMinimalPresentationTables(source);
            // 探索连续、战斗离散：见 data/_sample/found/found.time_model.json 同款结构，本用例只把
            // combat 行的 mode 改成 discrete（其余字段用默认值：seconds_per_turn=6、
            // initiative_policy=fixed_order，见 TimeModelDefinition.FromRecord）。
            source.Add("found.time_model",
                "{\"table\": \"found.time_model\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"found.time_model.exploration\", \"scope\": \"exploration\", \"mode\": \"continuous\"}," +
                "{\"id\": \"found.time_model.combat\", \"scope\": \"combat\", \"mode\": \"discrete\", " +
                "\"seconds_per_turn\": 6, \"initiative_policy\": \"fixed_order\", \"movement_budget_rule\": \"distance\"}" +
                "]}");

            var registryOptions = PresentationSchemaCatalog.CreateOptions();
            registryOptions.FailOnUnknownTable = false;
            var registry = new DataRegistry(source, bus, registryOptions);
            PresentationSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var engine = new StubEngine();
            var saveSystem = new Core.Foundation.SaveSystem.SaveSystem(
                engine.FileSystem, new SaveSystemOptions(new Id("game.unspecified")), bus);

            Id playerId = default;
            // 拍板 4/5 共用前提：装配离散模式只需传入 clockHost（GameplayAssembly 内部据此构造
            // TurnScheduler/TimeModelSwitch，见该类型构造函数判断记录"3.5) ADR-0013"）。
            var clockHost = new Core.Foundation.SimLoop.SimClockHost(world);
            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => playerId, playerFactionId: new Id("fac.sample_player"),
                clockHost: clockHost);
            playerId = gameplay.Carriers.Creatures.Spawn(SamplePlayerTemplateId, SampleMapId, Vec2.Zero, 0.0);

            var viewFactory = new Tests.PresentationViewBinding.FakeViewFactory();
            var sceneRouter = new SceneRouter(registry, engine.ResourceLoader, gameplay.AppState, world, gameplay.Hooks, bus);

            var floatingTexts = new List<(Id EntityId, Id StyleId, string Text)>();
            var options = new PresentationAssemblyOptions
            {
                OnFloatingText = (entityId, styleId, text) => floatingTexts.Add((entityId, styleId, text)),
            };
            var presentation = new PresentationAssembly(
                gameplay, world, registry, bus, new RngHost(2), viewFactory,
                engine.Renderer2D, engine.Camera, engine.Audio, engine.FileSystem, sceneRouter, options);

            Assert.NotNull(gameplay.TimeModelSwitch);
            Assert.Equal(QueueMode.Immediate, presentation.Feedback.Queue.Mode);

            // 进战：TimeModelSwitch 订阅的 combat.entered 处理函数（注册在先）同步切到 Discrete，
            // 本装配根的订阅（注册在后）随后读到的已经是切换后的值（见构造函数判断记录）。
            bus.PublishImmediate(new CombatEnteredEvent(playerId, new Id("unit.smoke_hostile")));

            Assert.Equal(Core.Foundation.SimLoop.TimeModelMode.Discrete, gameplay.TimeModelSwitch!.CurrentMode);
            Assert.Equal(QueueMode.Sequential, presentation.Feedback.Queue.Mode);

            // 离散下事件入队、presentation.playback_finished 在队列清空时发出且仅发一次（09 第 6.4
            // 节）：AddMinimalPresentationTables 的 feedback.sample_damage_text 规则对
            // combat.damage_dealt 派发一个 floating_text 动作；Sequential 模式下动作先入队，不立即
            // 派发。
            var playbackFinishedCount = 0;
            using var playbackFinishedSub = bus.Subscribe(
                EventKeys.PresentationPlaybackFinished, _ => playbackFinishedCount++);

            var damageEvt = new CombatDamageDealtEvent(
                playerId, new Id("unit.smoke_hostile"), new Id("skill.school.physical"), 7.0, isCrit: false, HitResult.Hit);
            bus.PublishImmediate(damageEvt);

            Assert.Empty(floatingTexts); // 已入队，尚未派发。
            // AddMinimalPresentationTables 对 combat.damage_dealt 挂了两条规则（floating_text +
            // flash，见该方法判断记录），Sequential 模式下两个动作都先入队。
            Assert.Equal(2, presentation.Feedback.Queue.PendingCount);
            Assert.Equal(0, playbackFinishedCount);

            presentation.Feedback.Update(1.0); // 足够推进完队列里两步（默认 SequentialStepSeconds=0.15）。

            Assert.Single(floatingTexts);
            Assert.Equal(0, presentation.Feedback.Queue.PendingCount);
            Assert.Equal(1, playbackFinishedCount); // 恰好一次，不随再次 Update 重复触发。

            presentation.Feedback.Update(1.0);
            Assert.Equal(1, playbackFinishedCount);

            // 脱战（唯一参战者离场 → activeCombatants 归零 → SwitchToContinuous）。
            bus.PublishImmediate(new CombatLeftEvent(playerId));

            Assert.Equal(Core.Foundation.SimLoop.TimeModelMode.Continuous, gameplay.TimeModelSwitch.CurrentMode);
            Assert.Equal(QueueMode.Immediate, presentation.Feedback.Queue.Mode);
        }

        [Fact]
        public void FeedbackQueueMode_ExplicitOption_OverridesAutoSwitch_NeverChanges()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();
            AddMinimalGameplayTables(source);
            AddMinimalPresentationTables(source);
            source.Add("found.time_model",
                "{\"table\": \"found.time_model\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"found.time_model.exploration\", \"scope\": \"exploration\", \"mode\": \"continuous\"}," +
                "{\"id\": \"found.time_model.combat\", \"scope\": \"combat\", \"mode\": \"discrete\", " +
                "\"seconds_per_turn\": 6, \"initiative_policy\": \"fixed_order\", \"movement_budget_rule\": \"distance\"}" +
                "]}");

            var registryOptions = PresentationSchemaCatalog.CreateOptions();
            registryOptions.FailOnUnknownTable = false;
            var registry = new DataRegistry(source, bus, registryOptions);
            PresentationSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var engine = new StubEngine();
            var saveSystem = new Core.Foundation.SaveSystem.SaveSystem(
                engine.FileSystem, new SaveSystemOptions(new Id("game.unspecified")), bus);

            Id playerId = default;
            var clockHost = new Core.Foundation.SimLoop.SimClockHost(world);
            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => playerId, playerFactionId: new Id("fac.sample_player"),
                clockHost: clockHost);
            playerId = gameplay.Carriers.Creatures.Spawn(SamplePlayerTemplateId, SampleMapId, Vec2.Zero, 0.0);

            var viewFactory = new Tests.PresentationViewBinding.FakeViewFactory();
            var sceneRouter = new SceneRouter(registry, engine.ResourceLoader, gameplay.AppState, world, gameplay.Hooks, bus);

            var options = new PresentationAssemblyOptions { FeedbackQueueMode = QueueMode.Immediate };
            var presentation = new PresentationAssembly(
                gameplay, world, registry, bus, new RngHost(2), viewFactory,
                engine.Renderer2D, engine.Camera, engine.Audio, engine.FileSystem, sceneRouter, options);

            bus.PublishImmediate(new CombatEnteredEvent(playerId, new Id("unit.smoke_hostile")));

            // 时间模型确实切到了离散（TimeModelSwitch 不受本装配根选项影响），但显式指定
            // FeedbackQueueMode 后播放队列不再跟随。
            Assert.Equal(Core.Foundation.SimLoop.TimeModelMode.Discrete, gameplay.TimeModelSwitch!.CurrentMode);
            Assert.Equal(QueueMode.Immediate, presentation.Feedback.Queue.Mode);
        }

        /// <summary>
        /// 根治修复（W5c，第三轮审计"离散回放门‘零事件步骤’无自动通知"仍保留项收口）：本装配根
        /// 构造期把 <c>() =&gt; Feedback.HasPendingPlayback</c>（GP-PRES-03 跟进：统一查询，不再只看
        /// <c>Queue.PendingCount</c>，见该属性判断记录）经
        /// <see cref="Core.Gameplay.Assembly.GameplayAssembly.SetPendingPlaybackProbe"/> 接入
        /// <c>gameplay.Pacing</c>（离散模式默认的 <see cref="Core.Foundation.SimLoop.WaitForPlaybackPacingPolicy"/>）
        /// ——本用例直接读 <c>HasPendingPlayback</c> 探针的实时求值结果，验证接线确实反映
        /// <c>Feedback.Queue.PendingCount</c> 的真实变化（入队时变 true、播放耗尽后变回 false），
        /// 不需要驱动完整的 <c>GameplayAssembly.Advance</c> 离散步循环（那条端到端路径见
        /// <c>core/gameplay/tests/Discrete/GameplayAssemblyDiscreteWiringTests.cs</c> 的
        /// <c>DiscretePacing_*</c> 系列，用手工可控探针验证节奏门本身；本用例反过来验证"探针确实是
        /// 接到真实播放队列上的，不是某个恒定值"）。
        /// </summary>
        [Fact]
        public void PendingPlaybackProbe_WiresToGameplayPacing_TracksFeedbackQueuePendingCount()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();
            AddMinimalGameplayTables(source);
            AddMinimalPresentationTables(source);
            source.Add("found.time_model",
                "{\"table\": \"found.time_model\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"found.time_model.exploration\", \"scope\": \"exploration\", \"mode\": \"continuous\"}," +
                "{\"id\": \"found.time_model.combat\", \"scope\": \"combat\", \"mode\": \"discrete\", " +
                "\"seconds_per_turn\": 6, \"initiative_policy\": \"fixed_order\", \"movement_budget_rule\": \"distance\"}" +
                "]}");

            var registryOptions = PresentationSchemaCatalog.CreateOptions();
            registryOptions.FailOnUnknownTable = false;
            var registry = new DataRegistry(source, bus, registryOptions);
            PresentationSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var engine = new StubEngine();
            var saveSystem = new Core.Foundation.SaveSystem.SaveSystem(
                engine.FileSystem, new SaveSystemOptions(new Id("game.unspecified")), bus);

            Id playerId = default;
            var clockHost = new Core.Foundation.SimLoop.SimClockHost(world);
            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => playerId, playerFactionId: new Id("fac.sample_player"),
                clockHost: clockHost);
            playerId = gameplay.Carriers.Creatures.Spawn(SamplePlayerTemplateId, SampleMapId, Vec2.Zero, 0.0);

            var viewFactory = new Tests.PresentationViewBinding.FakeViewFactory();
            var sceneRouter = new SceneRouter(registry, engine.ResourceLoader, gameplay.AppState, world, gameplay.Hooks, bus);

            var presentation = new PresentationAssembly(
                gameplay, world, registry, bus, new RngHost(2), viewFactory,
                engine.Renderer2D, engine.Camera, engine.Audio, engine.FileSystem, sceneRouter);

            var pacing = Assert.IsType<Core.Foundation.SimLoop.WaitForPlaybackPacingPolicy>(gameplay.Pacing);
            Assert.NotNull(pacing.HasPendingPlayback);

            // 战斗前（连续模式，队列默认 Immediate、永远为空）：探针应反映"当前没有待回放内容"。
            Assert.False(pacing.HasPendingPlayback!());

            bus.PublishImmediate(new CombatEnteredEvent(playerId, new Id("unit.smoke_hostile")));
            Assert.Equal(Core.Foundation.SimLoop.TimeModelMode.Discrete, gameplay.TimeModelSwitch!.CurrentMode);
            Assert.Equal(QueueMode.Sequential, presentation.Feedback.Queue.Mode);
            Assert.False(pacing.HasPendingPlayback!(), "进战瞬间队列仍为空，探针应仍为 false");

            // AddMinimalPresentationTables 对 combat.damage_dealt 挂了两条规则（floating_text +
            // flash），Sequential 模式下两个动作先入队——探针应立即反映"确有待回放内容"。
            var damageEvt = new CombatDamageDealtEvent(
                playerId, new Id("unit.smoke_hostile"), new Id("skill.school.physical"), 7.0, isCrit: false, HitResult.Hit);
            bus.PublishImmediate(damageEvt);

            Assert.Equal(2, presentation.Feedback.Queue.PendingCount);
            Assert.True(pacing.HasPendingPlayback!(), "队列非空时探针应返回 true");

            presentation.Feedback.Update(1.0); // 足够推进完队列里两步（默认 SequentialStepSeconds=0.15）。

            Assert.Equal(0, presentation.Feedback.Queue.PendingCount);
            Assert.False(pacing.HasPendingPlayback!(), "队列播放耗尽后探针应回到 false");
        }

        /// <summary>
        /// GP-PRES-03 跟进：验证装配根接的确实是 <see cref="Presentation.FeedbackBinder.Core.
        /// FeedbackBinder.HasPendingPlayback"/> 这一统一查询，不是 <c>Queue.PendingCount</c>——
        /// <see cref="FeedbackOptions.MergeWindow"/> &gt; 0 时数值飘字会先暂存在
        /// <c>FloatingTextMerger</c> 内部、不进队列，若探针仍只看队列长度，队列因"这一步只有 flash
        /// 一个非飘字动作"而先播空时就会误判整步表现已经播完，节奏门/playback_finished 提前解除。
        /// </summary>
        [Fact]
        public void PendingPlaybackProbe_HoldsWhileMergeWindowPending_UntilMergedTextPlayed()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();
            AddMinimalGameplayTables(source);
            AddMinimalPresentationTables(source);
            source.Add("found.time_model",
                "{\"table\": \"found.time_model\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"found.time_model.exploration\", \"scope\": \"exploration\", \"mode\": \"continuous\"}," +
                "{\"id\": \"found.time_model.combat\", \"scope\": \"combat\", \"mode\": \"discrete\", " +
                "\"seconds_per_turn\": 6, \"initiative_policy\": \"fixed_order\", \"movement_budget_rule\": \"distance\"}" +
                "]}");

            var registryOptions = PresentationSchemaCatalog.CreateOptions();
            registryOptions.FailOnUnknownTable = false;
            var registry = new DataRegistry(source, bus, registryOptions);
            PresentationSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var engine = new StubEngine();
            var saveSystem = new Core.Foundation.SaveSystem.SaveSystem(
                engine.FileSystem, new SaveSystemOptions(new Id("game.unspecified")), bus);

            Id playerId = default;
            var clockHost = new Core.Foundation.SimLoop.SimClockHost(world);
            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => playerId, playerFactionId: new Id("fac.sample_player"),
                clockHost: clockHost);
            playerId = gameplay.Carriers.Creatures.Spawn(SamplePlayerTemplateId, SampleMapId, Vec2.Zero, 0.0);

            var viewFactory = new Tests.PresentationViewBinding.FakeViewFactory();
            var sceneRouter = new SceneRouter(registry, engine.ResourceLoader, gameplay.AppState, world, gameplay.Hooks, bus);

            // 不设 FeedbackQueueMode：让播放队列模式随进战自动切 Sequential（拍板 5）。
            var options = new PresentationAssemblyOptions
            {
                FeedbackOptions = new FeedbackOptions { MergeWindow = 1.0, MergeMode = MergeMode.Sum },
            };
            var presentation = new PresentationAssembly(
                gameplay, world, registry, bus, new RngHost(2), viewFactory,
                engine.Renderer2D, engine.Camera, engine.Audio, engine.FileSystem, sceneRouter, options);

            var pacing = Assert.IsType<Core.Foundation.SimLoop.WaitForPlaybackPacingPolicy>(gameplay.Pacing);
            Assert.NotNull(pacing.HasPendingPlayback);

            var finishedCount = 0;
            bus.Subscribe(EventKeys.PresentationPlaybackFinished, _ => finishedCount++);

            bus.PublishImmediate(new CombatEnteredEvent(playerId, new Id("unit.smoke_hostile")));
            Assert.Equal(Core.Foundation.SimLoop.TimeModelMode.Discrete, gameplay.TimeModelSwitch!.CurrentMode);
            Assert.Equal(QueueMode.Sequential, presentation.Feedback.Queue.Mode);

            // AddMinimalPresentationTables 对 combat.damage_dealt 挂了 floating_text(amount) + flash
            // 两条规则：floating_text 走合并窗口（不进队列），flash 直接入队。
            var damageEvt = new CombatDamageDealtEvent(
                playerId, new Id("unit.smoke_hostile"), new Id("skill.school.physical"), 7.0, isCrit: false, HitResult.Hit);
            bus.PublishImmediate(damageEvt);

            Assert.Equal(1, presentation.Feedback.Queue.PendingCount);
            Assert.True(pacing.HasPendingPlayback!(), "flash 已入队，探针应为 true");
            Assert.Equal(0, finishedCount);

            // 默认 SequentialStepSeconds=0.15，0.3s 足够播完 flash 这一步；合并窗口（1.0s）远未到期。
            presentation.Feedback.Update(0.3);

            Assert.Equal(0, presentation.Feedback.Queue.PendingCount);
            Assert.True(pacing.HasPendingPlayback!(), "flash 播完但飘字仍在合并窗口内，探针不应回到 false");
            Assert.Equal(0, finishedCount);

            // 继续推进：剩余窗口 0.7s，0.8s 足以让窗口到期、合并结果入队并播出。
            presentation.Feedback.Update(0.8);

            Assert.False(pacing.HasPendingPlayback!(), "合并结果已播出，探针应回到 false");
            Assert.Equal(1, finishedCount);
        }

        // ------------------------------------------------------------------
        // 缺口 13：IModelHandleProvider.TryGetModelHandle 经 PresentationAssembly 的 socket 挂接路径。
        // ------------------------------------------------------------------

        private sealed class FakeModelHandleView : IView, IModelHandleProvider
        {
            private readonly ModelHandle _handle;
            public FakeModelHandleView(ModelHandle handle) => _handle = handle;
            public Id EntityId { get; private set; }
            public bool IsAlive { get; private set; }
            public void Bind(Id entityId)
            {
                EntityId = entityId;
                IsAlive = true;
            }
            public void OnEvent(IEvent evt) { }
            public void SyncPose(Vec2 pos, Direction facing, double height) { }
            public void Destroy() => IsAlive = false;
            public ModelHandle? TryGetModelHandle() => _handle;
        }

        private sealed class ModelHandleViewFactory : IViewFactory
        {
            private readonly ModelHandle _handle;
            public ModelHandleViewFactory(ModelHandle handle) => _handle = handle;
            public IView CreateView(ViewKind kind, Id displayId, Id entityId) => new FakeModelHandleView(_handle);
        }

        [Fact]
        public void VfxSocketAttach_EndToEnd_AttachesToResolvedHostHandle_ViaRenderer3D()
        {
            var bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();
            AddMinimalGameplayTables(source);
            AddMinimalPresentationTables(source);
            // 追加一条 socket 型 vfx.def 行（多根合并，见 InMemoryDataSource.Add 判断记录"登记一张
            // 表"——同一 tableName 多次 Add 视为多个数据根按顺序合并，只要 id 不重复即可）：
            // AddMinimalPresentationTables 已经登记过 vfx.sample_hit，这里不重复声明。
            source.Add("vfx.def",
                "{\"table\": \"vfx.def\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"vfx.sample_socket\", \"category\": \"impact\", \"attach_mode\": \"socket\", \"resource_ref\": \"model.sample_socket_vfx\"}" +
                "]}");

            var registryOptions = PresentationSchemaCatalog.CreateOptions();
            registryOptions.FailOnUnknownTable = false;
            var registry = new DataRegistry(source, bus, registryOptions);
            PresentationSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking, string.Join("; ", report.Issues));

            var world = new WorldSim(bus);
            var spatial = new StubSpatialQuery();
            var rng = new RngHost(1);
            var engine = new StubEngine();
            var renderer3D = new StubRenderer3D();
            var hostHandle = renderer3D.CreateModelInstance(new Id("model.sample_host"));
            var saveSystem = new Core.Foundation.SaveSystem.SaveSystem(
                engine.FileSystem, new SaveSystemOptions(new Id("game.unspecified")), bus);

            Id playerId = default;
            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, spatial, saveSystem,
                playerUnitProvider: () => playerId, playerFactionId: new Id("fac.sample_player"));
            playerId = gameplay.Carriers.Creatures.Spawn(SamplePlayerTemplateId, SampleMapId, Vec2.Zero, 0.0);

            var viewFactory = new ModelHandleViewFactory(hostHandle);
            var sceneRouter = new SceneRouter(registry, engine.ResourceLoader, gameplay.AppState, world, gameplay.Hooks, bus);

            var presentation = new PresentationAssembly(
                gameplay, world, registry, bus, new RngHost(2), viewFactory,
                engine.Renderer2D, engine.Camera, engine.Audio, engine.FileSystem, sceneRouter,
                renderer3D: renderer3D);

            presentation.ViewBinder.OnEntityCreated(playerId, "player", new Id("display.sample_player"));

            var handle = presentation.Vfx.Spawn(new Id("vfx.sample_socket"), VfxAttach.Socket(playerId, new Id("socket.weapon")), null);

            Assert.NotNull(handle);
            // StubRenderer3D.AttachToSocket 记录以"子模型句柄"为键（见其源码
            // Attachments[child.Value] = (socketId, handle.Value)）：键是刚为特效资源创建出的子
            // 模型句柄，value.ChildHandle 字段实际存的是宿主句柄值——本用例要核对的正是"宿主句柄
            // 确实来自 FakeModelHandleView.TryGetModelHandle（经 ViewBinder 解析出 playerId 对应的
            // View）"，即 value.ChildHandle == hostHandle.Value。
            var attachment = Assert.Single(renderer3D.Attachments);
            Assert.Equal(hostHandle.Value, attachment.Value.ChildHandle);
            Assert.Equal(new Id("socket.weapon"), attachment.Value.SocketId);
        }

        // -----------------------------------------------------------------
        // GP-PRES-04 收口回归（architecture/落地计划/audit-20260907/gameplay-presentation.md）：
        // 此前本构造函数没有 IResourceLoader 参数，VfxPlayer/SfxPlayer 只能拿到 null，永远不会
        // 调用 IResourceLoader.LoadAsync——ADR-0016"谁首次引用谁加载"的责任没有落地。下面两条用例
        // 直接断言：传入 resourceLoader 后，首次 Spawn/Play 会触发一次对应资源 id 的 LoadAsync。
        //
        // 外部审核阻塞项 4 收口（首次特效/音效加载边界，见 architecture/落地计划/audit-20260907/
        // followup-2026-09-07.md"外部审核阻塞项处理"一节）：Register 先把资源登记为"可加载成功"
        // ——VfxPlayer.Spawn/SfxPlayer.Play 现在会等资源真正加载完成才播放（不再是此前"发起加载 +
        // 不管成不成功都立即播放"的 fire-and-forget，那正是"首次施法命中特效/音效不播放"的根因）；
        // StubResourceLoader 默认同步回调（DeferCallbacks=false），Register 过的资源在 LoadAsync
        // 调用当下就会同步标记为已加载完成，因此本用例仍能在同一次调用内拿到非空句柄，验证的是
        // "加载成功后确实播放了"这一更贴近真实场景的路径，而不是此前"不管资源是否已就绪都硬播"。
        // -----------------------------------------------------------------

        [Fact]
        public void Construct_WithResourceLoader_VfxSpawn_TriggersLoadAsync_ForResourceRef()
        {
            var presentation = Build(out _, out _, out var engine, out _, withResourceLoader: true);
            engine.ResourceLoader.Register(new Id("vfx.sample_hit"));

            Assert.Empty(engine.ResourceLoader.LoadRequests);

            var handle = presentation.Vfx.Spawn(new Id("vfx.sample_hit"), VfxAttach.World(Vec2.Zero), null);

            Assert.NotNull(handle);
            Assert.Contains(engine.ResourceLoader.LoadRequests, r => r.ResourceId.Equals(new Id("vfx.sample_hit")));
        }

        [Fact]
        public void Construct_WithResourceLoader_SfxPlay_TriggersLoadAsync_ForResourceRef()
        {
            var presentation = Build(out _, out _, out var engine, out _, withResourceLoader: true);
            engine.ResourceLoader.Register(new Id("sfx.sample_hit"));

            Assert.Empty(engine.ResourceLoader.LoadRequests);

            var handle = presentation.Sfx.Play(new Id("sfx.sample_hit"), Vec2.Zero);

            Assert.NotNull(handle);
            Assert.Contains(engine.ResourceLoader.LoadRequests, r => r.ResourceId.Equals(new Id("sfx.sample_hit")));
        }

        [Fact]
        public void Construct_WithoutResourceLoader_VfxSpawn_DoesNotThrow_BehavesAsBefore()
        {
            // 默认（不传 resourceLoader）行为不变：不抛异常，仍能正常 Spawn——覆盖"未装配资源
            // 加载器的默认模板启动不受影响"（GP-PRES-04 验收标准之一）。
            var presentation = Build(out _, out _, out _, out _, withResourceLoader: false);

            var handle = presentation.Vfx.Spawn(new Id("vfx.sample_hit"), VfxAttach.World(Vec2.Zero), null);

            Assert.NotNull(handle);
        }
    }
}
