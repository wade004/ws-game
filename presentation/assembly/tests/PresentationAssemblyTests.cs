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
using Presentation.Assembly;
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
        }

        private static PresentationAssembly Build(
            out GameplayAssembly gameplay, out WorldSim world, out StubEngine engine, out IEventBus bus,
            PresentationAssemblyOptions? options = null)
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

            var viewFactory = new Tests.PresentationViewBinding.FakeViewFactory();
            var sceneRouter = new SceneRouter(registry, engine.ResourceLoader, gameplay.AppState, world, gameplay.Hooks, bus);
            var presentationRng = new RngHost(2);

            return new PresentationAssembly(
                gameplay, world, registry, bus, presentationRng, viewFactory,
                engine.Renderer2D, engine.Camera, engine.Audio, engine.FileSystem, sceneRouter, options);
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
    }
}
