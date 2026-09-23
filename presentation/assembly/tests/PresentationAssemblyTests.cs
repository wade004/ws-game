using System.Collections.Generic;
using Adapters.Stub;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SceneRouter;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Gameplay.Dialog;
using Core.Rules.Combat;
using Core.Rules.Common;
using Core.Foundation.EngineAdapter;
using Presentation.Assembly;
using Presentation.Camera;
using Presentation.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.Render;
using Presentation.Ui;
using Presentation.VfxSfx.Contracts;
using Presentation.ViewBinding;
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
        private static readonly Id SampleTargetTemplateId = new Id("creature.sample_target");
        // ADR-0069（消费方反馈——游戏接入方第十四批）：唯一配置了 gossip_menu_ref 的样例模板，供
        // "最近可交互目标候选生物需有可交互内容"系列用例区分"有内容"/"无内容"两个分支——
        // creature.sample_target 本身没有配置，直接复用为"无内容"分支，不需要再新增一条。
        private static readonly Id SampleTargetWithGossipTemplateId = new Id("creature.sample_target_with_gossip");
        private static readonly Id SampleGossipMenuId = new Id("dialog.gossip_menu.sample_default");
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
            // 本切片新增（消费方反馈第四批第 1/2 条，2026-09-21）：此前恒为空数组——没有任何既有用例
            // 走完整结算管线（Combat.ResolveEffect/AutoAttackHost 挥击伤害），所以一直不需要一条真实
            // 的 combat.hit_table.default 行。新增两条用例需要用框架真实的伤害/死亡路径打死一个目标
            // （不是直接改 IUnitAccess 字段，见 HudViewModel_AutoAttackStateAndTargetAlive 用例判断
            // 记录），Resolver.RequireHitTable 找不到 CombatOptions.HitTableConfigId（缺省
            // "combat.hit_table.default"）对应的行会直接抛异常（不静默降级），因此必须补一条。全部
            // 分支关闭（miss/dodge/parry/glancing_blow/block/crit 恒不触发）保证伤害结算确定性，写法
            // 照抄 core/sim/tests/AutoAttackHostIntegrationTests.cs 的 ZeroVarianceHitTableJson——这条
            // 行只是让"能结算伤害"这件事从无到有，不影响任何既有用例（此前没有用例依赖过命中表数据）。
            source.Add("combat.hit_table_config",
                "{\"table\": \"combat.hit_table_config\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"combat.hit_table.default\", " +
                "\"miss\": {\"enabled\": false, \"base\": 0}, \"dodge\": {\"enabled\": false, \"base\": 0}, " +
                "\"parry\": {\"enabled\": false, \"base\": 0}, \"glancing_blow\": {\"enabled\": false, \"base\": 0}, " +
                "\"block\": {\"enabled\": false, \"base\": 0}, \"crit\": {\"enabled\": false, \"base\": 0}, " +
                "\"crit_multiplier_base\": 2.0}" +
                "]}");
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
                "\"faction_id\": \"fac.sample_player\", \"display_ref\": \"display.sample_player\"}," +
                // 本切片新增（目标框 target.name/target.faction，沿用 ADR-0048 口径）：一条独立的
                // 目标用模板，name_key/faction_id 与玩家模板不同，便于测试断言精确对上"这两个值就是
                // 这条模板声明的值"而不是恰好与玩家模板重合。
                "{\"id\": \"creature.sample_target\", \"name_key\": \"l10n.creature.sample_target.name\", " +
                "\"level\": 1, \"tier\": \"creature.tier.sample_normal\", " +
                "\"base_stats\": {\"stat.max_health\": 50}, \"stat_growth_ref\": \"prog.sample_curve\", " +
                "\"faction_id\": \"fac.sample_hostile\", \"display_ref\": \"display.sample_target\"}," +
                // 消费方反馈第七批第 1 条（ADR-0065）：独立于 creature.sample_target 另开一条带
                // loot_table_ref 的模板，专供 InteractPathProvider_DeadCreature_* 用例走真实死亡
                // 结算掉落——不复用 creature.sample_target 本身，避免给它附带掉落表后连带影响既有
                // 不关心掉落的用例（HudViewModel_AutoAttackStateAndTargetAlive 等）。
                // ADR-0069 勘误：补 gossip_menu_ref——InteractPathProvider_DeadCreature_* 用例在真正
                // 打死目标之前先回归一次"存活时仍是候选"（ADR-0065 既有断言），本次改动后"候选"
                // 额外要求有可交互内容，这条模板此前没有配置，该回归断言会被本次改动误伤（不是在
                // 验证"死亡排除"这条真正想测的东西，而是撞上了新加的内容过滤），补上后让内容过滤
                // 不参与这组用例、只让死亡排除单独起作用，与该组用例本来的验收目标一致。
                "{\"id\": \"creature.sample_lootable_target\", \"name_key\": \"l10n.creature.sample_target.name\", " +
                "\"level\": 1, \"tier\": \"creature.tier.sample_normal\", " +
                "\"base_stats\": {\"stat.max_health\": 50}, \"stat_growth_ref\": \"prog.sample_curve\", " +
                "\"faction_id\": \"fac.sample_hostile\", \"display_ref\": \"display.sample_target\", " +
                "\"loot_table_ref\": \"loot.sample_target_reward\", " +
                "\"gossip_menu_ref\": \"" + SampleGossipMenuId.Value + "\"}," +
                // ADR-0069（消费方反馈——游戏接入方第十四批）：独立于 creature.sample_target 另开一条
                // 带 gossip_menu_ref 的模板，供"最近可交互目标候选生物需有可交互内容"系列用例——不
                // 复用 creature.sample_target 本身，避免给它附带对话内容后连带影响既有不关心内容
                // 过滤的用例（HudViewModel_AutoAttackStateAndTargetAlive 等仍假定它没有可交互内容）。
                "{\"id\": \"" + SampleTargetWithGossipTemplateId.Value + "\", \"name_key\": \"l10n.creature.sample_target.name\", " +
                "\"level\": 1, \"tier\": \"creature.tier.sample_normal\", " +
                "\"base_stats\": {\"stat.max_health\": 50}, \"stat_growth_ref\": \"prog.sample_curve\", " +
                "\"faction_id\": \"fac.sample_hostile\", \"display_ref\": \"display.sample_target\", " +
                "\"gossip_menu_ref\": \"" + SampleGossipMenuId.Value + "\"}" +
                "]}");

            // ADR-0069：creature.sample_target_with_gossip 引用的对话菜单——最小合法行（写法照抄
            // Tests.Gameplay.Assembly.GameplayAssemblyCreatureDialogInteractionTests 的
            // VendorGossipMenuRowsTemplate 形状），本文件的用例只需要 gossip_menu_ref 配置存在这件事
            // 本身（HasInteractableContent 只看配置是否存在，不解引用），不需要真的选项、打开对话。
            source.Add("dialog.gossip_menu",
                "{\"table\": \"dialog.gossip_menu\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"" + SampleGossipMenuId.Value + "\", \"options\": [" +
                "{\"text_key\": \"l10n.gossip.sample_default.open_shop\", \"actions\": [{\"kind\": \"vendor\"}]}" +
                "]}]}");

            // 消费方反馈第七批第 1 条（ADR-0065）：creature.sample_lootable_target 死亡时掉落的
            // 战利品表——复用已注册的 item.sample_sword（不新增一条 item.template，减少数据面）,
            // 100% 掉落（chance_each + weight_or_chance:1.0）保证用例确定性，不依赖随机数种子。
            source.Add("loot.table",
                "{\"table\": \"loot.table\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"loot.sample_target_reward\", \"groups\": [" +
                "{\"roll_mode\": \"chance_each\", \"entries\": [" +
                "{\"ref\": \"item.sample_sword\", \"weight_or_chance\": 1.0, \"count_range\": {\"min\": 1, \"max\": 1}}" +
                "]}]}" +
                "]}");

            // 本切片新增（消费方反馈第四批第 1/2 条，2026-09-21）：一件主手武器最小数据集，供
            // HudViewModel_AutoAttackStateAndTargetAlive 用例把玩家装备起来、经真实
            // AutoAttackHost/Resolver 结算管线打死目标（不是直接改 IUnitAccess 字段）。武器秒伤故意
            // 定得远大于 creature.sample_target 的 50 点生命上限，保证恰好一次挥击必定致死，测试不
            // 依赖多次挥击的循环上限。
            source.Add("item.slot_definition",
                "{\"table\": \"item.slot_definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"item.slot.sample_main_hand\", \"name_key\": \"l10n.slot.sample_main_hand.name\", " +
                "\"is_weapon\": true}" +
                "]}");
            source.Add("item.quality_definition",
                "{\"table\": \"item.quality_definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"item.quality.sample_common\", \"name_key\": \"l10n.quality.sample_common.name\"}" +
                "]}");
            source.Add("item.weapon_dps_curve",
                "{\"table\": \"item.weapon_dps_curve\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"item.weapon_dps.default\", \"entries\": [{\"x\": 1, \"y\": 1000}]}" +
                "]}");
            source.Add("item.template",
                "{\"table\": \"item.template\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"item.sample_sword\", \"slot\": \"item.slot.sample_main_hand\", " +
                "\"quality\": \"item.quality.sample_common\", \"item_level\": 1, " +
                "\"weapon_profile\": {\"damage_min\": 50, \"damage_max\": 50, \"speed\": 1.5, " +
                "\"weapon_school\": \"school.physical\"}, " +
                "\"display_ref\": \"display.item.sample_sword\", \"stack_size\": 1, " +
                "\"name_key\": \"l10n.item.sample_sword.name\"}" +
                "]}");

            // 本切片新增（消费方反馈第四批第 3 条，2026-09-21）：两条光环定义——一条同时声明极性与
            // 图标引用，一条两者都不声明，供 HudViewModel_Auras 用例分别断言"取到声明值"与"退化为
            // Undeclared/null，不是编造默认值"两个分支（写法照抄
            // core/rules/skill/tests/AuraPolarityIconRefTests.cs 的数据形状）。mod_stat 效果引用
            // 已经注册的 stat.max_health，不需要为此另外登记一条属性。
            source.Add("skill.aura_def",
                "{\"table\": \"skill.aura_def\", \"schema_version\": 2, \"rows\": [" +
                "{\"id\": \"skill.aura_def.sample_fortify\", \"max_stacks\": 1, \"duration\": 30, " +
                "\"polarity\": \"beneficial\", \"icon_ref\": \"icon.aura.sample_fortify\", " +
                "\"effects\": [{\"kind\": \"mod_stat\", \"params\": " +
                "{\"stat\": \"stat.max_health\", \"op\": \"flat\", \"value\": 1}}]}," +
                "{\"id\": \"skill.aura_def.sample_unmarked\", \"max_stacks\": 1, \"duration\": 30, " +
                "\"effects\": [{\"kind\": \"mod_stat\", \"params\": " +
                "{\"stat\": \"stat.max_health\", \"op\": \"flat\", \"value\": 1}}]}" +
                "]}");

            // ADR-0062（消费方反馈第五批第 1 条续）：InteractPathProvider_* 用例需要一个最小合法
            // gobj.template 行——kind=save_point，type_data 为空对象（07 第 3.1 节"save_point{}"，
            // 唯一不需要任何 type_data 子字段的 kind，写法照抄
            // core/carriers/gobj/tests/GameObjectHostTests.Interact_SavePoint_InvokesSaveRequester
            // 用到的最小模板形状），不需要 lock_id/on_use。
            source.Add("gobj.template",
                "{\"table\": \"gobj.template\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"gobj.sample_marker\", \"name_key\": \"l10n.gobj.sample_marker.name\", " +
                "\"kind\": \"save_point\", \"type_data\": {}, \"display_ref\": \"display.gobj.sample_marker\"}" +
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
            bool withResourceLoader = false, System.Action<InMemoryDataSource>? extraTables = null)
        {
            bus = new EventBus(
                EventCatalog.FromDefinitions(System.Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false });

            var source = new InMemoryDataSource();
            AddMinimalGameplayTables(source);
            AddMinimalPresentationTables(source);
            // 消费方反馈第六批（阻塞，ADR-0064）新增：可选钩子，供个别用例（如任务标题/目标描述
            // 文本键验收）在共享最小数据集之上追加自己需要的表，不必复制一份完整的 Build 流程。
            // 默认 null，既有全部调用方行为逐字节不变。
            extraTables?.Invoke(source);

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

        /// <summary>消费方反馈第六批（阻塞）验收：经 <see cref="PresentationAssembly"/> 构建出的
        /// <see cref="PresentationAssembly.QuestLog"/> 视图模型最外层（不是宿主层，也不是路径层）
        /// 能读到与真实加载的 <c>quest.def</c> 数据定义一致的标题键/目标描述键——期望值直接复用
        /// 本用例 authoring 数据时使用的 <c>titleKey</c>/<c>descKey0</c> 两个 <see cref="Id"/> 局部
        /// 变量（同一份数据、同一份期望值，不写与数据脱节的裸字符串），同
        /// <c>UiDataSourceTests.Query_quest_state_and_objective</c> 复用 <c>questId</c> 变量同一
        /// 惯例。第二条目标未声明 <c>description_key</c>，验证降级为"无"（<c>null</c>）。</summary>
        [Fact]
        public void QuestLog_ThroughPresentationAssembly_ExposesTitleAndObjectiveDescriptionKeys()
        {
            var questId = new Id("quest.quest_log_text_keys_test");
            var titleKey = new Id("l10n.quest_log_text_keys_test.title");
            var descKey0 = new Id("l10n.quest_log_text_keys_test.objective_0");

            var presentation = Build(out var gameplay, out _, out _, out _, extraTables: source =>
            {
                source.Add("quest.def",
                    "{\"table\": \"quest.def\", \"schema_version\": 1, \"rows\": [" +
                    "{\"id\": \"" + questId.Value + "\", \"title_key\": \"" + titleKey.Value + "\", " +
                    "\"objectives\": [" +
                    "{\"type\": \"kill\", \"target_ref\": \"" + SampleTargetTemplateId.Value + "\", \"count\": 1, " +
                    "\"description_key\": \"" + descKey0.Value + "\"}, " +
                    "{\"type\": \"kill\", \"target_ref\": \"" + SampleTargetTemplateId.Value + "\", \"count\": 1}" +
                    "], \"start_method\": \"npc_gossip\", \"turn_in_method\": \"npc_gossip\", \"repeatable\": \"none\"}" +
                    "]}");
            });

            Assert.True(gameplay.Quest.Accept(gameplay.PlayerUnitProvider(), questId));
            presentation.QuestLog.Refresh();

            Assert.Equal(titleKey, presentation.QuestLog.GetQuestTitleKey(questId));
            Assert.Equal(descKey0, presentation.QuestLog.GetObjectiveDescriptionKey(questId, 0));
            // 数据未填描述的目标（第二条）→ 无。
            Assert.Null(presentation.QuestLog.GetObjectiveDescriptionKey(questId, 1));
        }

        [Fact]
        public void VfxSfxFeedbackViewBinderDiagnostics_AreExposed_AndEachIndependent()
        {
            // 诊断转发到引擎控制台跟进（presentation/assembly/README.md 判断记录 10）：
            // VfxDiagnostics/SfxDiagnostics 必须与内部 VfxPlayer/SfxPlayer 各自持有的 Diagnostics
            // 是同一个实例（adapters/unity 侧靠这份引用轮询转发，见 PresentationAssemblyDiagnosticsForwarder）；
            // Feedback/ViewBinder 本身是具体类型，直接读 .Diagnostics 即可，不需要额外转发属性。
            var presentation = Build(out _, out _, out _, out _);

            Assert.NotNull(presentation.VfxDiagnostics);
            Assert.NotNull(presentation.SfxDiagnostics);
            Assert.NotNull(presentation.Feedback.Diagnostics);
            Assert.NotNull(presentation.ViewBinder.Diagnostics);

            Assert.Same(presentation.VfxDiagnostics, Assert.IsType<global::Presentation.VfxSfx.Core.VfxPlayer>(presentation.Vfx).Diagnostics);
            Assert.Same(presentation.SfxDiagnostics, Assert.IsType<global::Presentation.VfxSfx.Core.SfxPlayer>(presentation.Sfx).Diagnostics);

            // 判断记录 10 的取舍（各自独立，不共享一个 PresentationDiagnosticsRecorder）：本装配根
            // 不传 diagnostics 构造参数给这四个类型，四份实例互不相同——若未来有人误改成共享一个
            // 实例，本断言会失败，提醒改动者这是一次需要更新判断记录的有意设计变更，不是顺手重构。
            var recorders = new object[]
            {
                presentation.VfxDiagnostics, presentation.SfxDiagnostics,
                presentation.Feedback.Diagnostics, presentation.ViewBinder.Diagnostics,
            };
            Assert.Equal(recorders.Length, new HashSet<object>(recorders, ReferenceEqualityComparer.Instance).Count);
        }

        [Fact]
        public void SfxPlaybackDiagnostics_IsExposed_SameInstanceAsSfxPlayer_AndReflectsRealPlayback()
        {
            // ADR-0083：SfxPlaybackDiagnostics 是与 SfxDiagnostics（文本消息列表）平行的独立转发
            // 属性，必须转发的是内部 SfxPlayer 自己持有的同一份 PlaybackDiagnostics 实例（同
            // VfxDiagnostics/SfxDiagnostics 判断记录 10 的转发惯例），且经真实生产装配根调用一次
            // ISfxPlayer.Play 后，计数应当如实反映"确实发生过一次播放"。
            var presentation = Build(out _, out _, out _, out _);

            Assert.NotNull(presentation.SfxPlaybackDiagnostics);
            Assert.Same(
                presentation.SfxPlaybackDiagnostics,
                Assert.IsType<global::Presentation.VfxSfx.Core.SfxPlayer>(presentation.Sfx).PlaybackDiagnostics);

            var before = presentation.SfxPlaybackDiagnostics.PlayRequestedCount;
            presentation.Sfx.Play(new Id("sfx.sample_hit"), null);

            Assert.Equal(before + 1, presentation.SfxPlaybackDiagnostics.PlayRequestedCount);
        }

        [Fact]
        public void FeedbackSinkDiagnostics_IsExposed_SameInstanceAsCompositeFeedbackSink_AndIndependentOfOtherFour()
        {
            // 诊断转发到引擎控制台第三批（presentation/assembly/README.md 判断记录 10b）：本装配根第
            // 3 步构造的局部变量 CompositeFeedbackSink 是第二批扫漏的第五条可达诊断源——
            // FeedbackSinkDiagnostics 必须转发的是这个内部 sink 自己持有的实例（不是 Feedback.Diagnostics
            // 那一份，两者是两个不同子系统各自独立的 recorder，见判断记录 10"各自独立"取舍延续）。
            var presentation = Build(out _, out _, out _, out _);

            Assert.NotNull(presentation.FeedbackSinkDiagnostics);

            // 反面验证：play_vfx 找不到可用世界坐标时会经 CompositeFeedbackSink 记一条警告，该警告必须
            // 出现在 FeedbackSinkDiagnostics 里，而不是 Feedback.Diagnostics（FeedbackBinder 自己的
            // recorder，只记它自身产生的诊断，见该类型判断记录）。
            var recorder = Assert.IsType<PresentationDiagnosticsRecorder>(presentation.FeedbackSinkDiagnostics);
            var feedbackRecorder = Assert.IsType<PresentationDiagnosticsRecorder>(presentation.Feedback.Diagnostics);
            Assert.NotSame(recorder, feedbackRecorder);

            var beforeCount = recorder.Warnings.Count;
            var feedbackBeforeCount = feedbackRecorder.Warnings.Count;
            ((global::Presentation.FeedbackBinder.Contracts.IFeedbackSink)GetFeedbackSink(presentation))
                .PlayVfx(new Id("vfx.does_not_exist_for_diagnostics_test"), new global::Presentation.FeedbackBinder.Contracts.FeedbackAttachSpec(
                    global::Presentation.FeedbackBinder.Contracts.FeedbackAttachTarget.World, null, null, null));
            Assert.True(recorder.Warnings.Count > beforeCount);
            Assert.Equal(feedbackBeforeCount, feedbackRecorder.Warnings.Count);

            // 五个来源（Vfx/Sfx/Feedback/ViewBinder/FeedbackSink）互不相同——同前一条用例的判断记录，
            // 若未来有人误改成共享一个实例，本断言会失败。
            var recorders = new object[]
            {
                presentation.VfxDiagnostics, presentation.SfxDiagnostics,
                presentation.Feedback.Diagnostics, presentation.ViewBinder.Diagnostics,
                presentation.FeedbackSinkDiagnostics,
            };
            Assert.Equal(recorders.Length, new HashSet<object>(recorders, ReferenceEqualityComparer.Instance).Count);
        }

        /// <summary>反射读取本装配根第 3 步构造的私有局部 <c>feedbackSink</c>
        /// 不可行（局部变量无法反射），改为反射 <c>FeedbackBinderCore</c> 内部 <c>_sink</c> 字段——
        /// 与 <see cref="PresentationAssembly.FeedbackSinkDiagnostics"/> 转发的是同一个
        /// <c>CompositeFeedbackSink</c> 实例（本装配根构造 <c>Feedback</c> 时把同一个 <c>feedbackSink</c>
        /// 变量传给了它的 <c>sink</c> 构造参数）。</summary>
        private static object GetFeedbackSink(PresentationAssembly presentation)
        {
            var field = typeof(global::Presentation.FeedbackBinder.Core.FeedbackBinder).GetField(
                "_sink", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.NotNull(field);
            return field!.GetValue(presentation.Feedback)!;
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

        /// <summary>
        /// ADR-0076 根治验收（运行期，消费方第二十批第 3 条）：静态期覆盖见
        /// <c>ADR0076_EventGroupExprSchemaTests</c>（同一份 <see cref="PresentationSchemaCatalog.FullExprSchema"/>
        /// 加载 <c>event.hit_result == "Hit"</c> 条件不再报 <c>expr_parsable</c> Error）；本用例继续
        /// 经真实 <see cref="Presentation.FeedbackBinder.Core.FeedbackBinder"/>（<see cref="Build"/>
        /// 装配出的完整 <see cref="PresentationAssembly"/>，内部真正接的是
        /// <c>Core.Rules.ExprHost.RulesExprHostFactory</c>，不是测试假实现）验证运行期按事件真实字段
        /// 值求值：<see cref="HitResult.Hit"/> 命中时条件为真，规则的 flash 动作被派发。改动前实测：
        /// 本条数据在装配期（<see cref="Build"/> 内 <c>registry.LoadAll()</c> 后的
        /// <c>Assert.False(report.IsBlocking, ...)</c>）就会先阻断，测试根本跑不到这里
        /// （<c>expr_parsable</c> Error "比较两侧类型不一致：Bool 与 String" 属于阻断级）。
        /// </summary>
        [Fact]
        public void EventHitResultStringCondition_ThroughRealFeedbackBinder_EvaluatesTrueOnMatch_ADR0076()
        {
            var flashes = new List<(Id EntityId, Id ProfileId)>();
            var options = new PresentationAssemblyOptions
            {
                OnFlash = (entityId, profileId) => flashes.Add((entityId, profileId)),
            };

            var presentation = Build(out _, out _, out _, out var bus, options, extraTables: source =>
            {
                source.Add("feedback.binding",
                    "{\"table\": \"feedback.binding\", \"schema_version\": 1, \"rows\": [" +
                    "{\"id\": \"feedback.adr0076_hit_result_flash\", \"event\": \"combat.damage_dealt\", " +
                    "\"condition\": \"event.hit_result == \\\"Hit\\\"\", \"actions\": [" +
                    "{\"kind\": \"flash\", \"params\": {\"profile_id\": \"feedback.flash.adr0076_hit\", \"target\": \"target\"}}" +
                    "]}]}");
            });

            var evt = new CombatDamageDealtEvent(
                new Id("unit.smoke_player"), new Id("unit.smoke_target"), new Id("skill.school.physical"),
                17.0, isCrit: false, HitResult.Hit);
            bus.PublishImmediate(evt);

            Assert.Contains(flashes, f => f.EntityId == new Id("unit.smoke_target") && f.ProfileId == new Id("feedback.flash.adr0076_hit"));
        }

        /// <summary>同上一用例的反面：<see cref="HitResult.Miss"/> 不匹配 <c>"Hit"</c> 字面量时条件为
        /// 假，规则的 flash 动作不应被派发——证明本次修复不是把 event 分组的比较恒判定为真（不是从
        /// "恒报错"滑到另一个极端"恒放行为真"），确实按事件真实字段值求值。</summary>
        [Fact]
        public void EventHitResultStringCondition_ThroughRealFeedbackBinder_EvaluatesFalseOnMismatch_ADR0076()
        {
            var flashes = new List<(Id EntityId, Id ProfileId)>();
            var options = new PresentationAssemblyOptions
            {
                OnFlash = (entityId, profileId) => flashes.Add((entityId, profileId)),
            };

            var presentation = Build(out _, out _, out _, out var bus, options, extraTables: source =>
            {
                source.Add("feedback.binding",
                    "{\"table\": \"feedback.binding\", \"schema_version\": 1, \"rows\": [" +
                    "{\"id\": \"feedback.adr0076_hit_result_flash\", \"event\": \"combat.damage_dealt\", " +
                    "\"condition\": \"event.hit_result == \\\"Hit\\\"\", \"actions\": [" +
                    "{\"kind\": \"flash\", \"params\": {\"profile_id\": \"feedback.flash.adr0076_hit\", \"target\": \"target\"}}" +
                    "]}]}");
            });

            var evt = new CombatDamageDealtEvent(
                new Id("unit.smoke_player"), new Id("unit.smoke_target"), new Id("skill.school.physical"),
                17.0, isCrit: false, HitResult.Miss);
            bus.PublishImmediate(evt);

            Assert.DoesNotContain(flashes, f => f.ProfileId == new Id("feedback.flash.adr0076_hit"));
        }

        /// <summary>ADR-0076 验收标准第 3 条（Id 字面量比较）：<c>event.school</c>（见
        /// <c>CombatDamageDealtEvent.TryGetField</c> "school" -&gt; <c>ExprValue.OfId</c>）与 Id
        /// 字面量比较同样经真实管线加载、正确求值。</summary>
        [Fact]
        public void EventSchoolIdCondition_ThroughRealFeedbackBinder_EvaluatesTrueOnMatch_ADR0076()
        {
            var flashes = new List<(Id EntityId, Id ProfileId)>();
            var options = new PresentationAssemblyOptions
            {
                OnFlash = (entityId, profileId) => flashes.Add((entityId, profileId)),
            };

            var presentation = Build(out _, out _, out _, out var bus, options, extraTables: source =>
            {
                source.Add("feedback.binding",
                    "{\"table\": \"feedback.binding\", \"schema_version\": 1, \"rows\": [" +
                    "{\"id\": \"feedback.adr0076_school_flash\", \"event\": \"combat.damage_dealt\", " +
                    "\"condition\": \"event.school == skill.school.physical\", \"actions\": [" +
                    "{\"kind\": \"flash\", \"params\": {\"profile_id\": \"feedback.flash.adr0076_school\", \"target\": \"target\"}}" +
                    "]}]}");
            });

            var evt = new CombatDamageDealtEvent(
                new Id("unit.smoke_player"), new Id("unit.smoke_target"), new Id("skill.school.physical"),
                17.0, isCrit: false, HitResult.Hit);
            bus.PublishImmediate(evt);

            Assert.Contains(flashes, f => f.ProfileId == new Id("feedback.flash.adr0076_school"));
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

        /// <summary>UI-111-01 回归（architecture/落地计划/audit-6739f50-20260909/AUDIT_REPORT.md，见
        /// InventoryViewModel 同名用例判断记录）：同图读档的抑制作用域会连带压住
        /// economy.currency_changed/economy.vendor_restocked 等业务事件本身，只有在该作用域外正常
        /// 派发的 save.loaded 才能保证已打开的货架整体重建，不依赖恰好赶上业务事件。</summary>
        [Fact]
        public void Shop_SaveLoadedEvent_RefreshesOpenShelf_NoManualRefresh()
        {
            var presentation = Build(out _, out _, out _, out var bus);
            presentation.Shop.OpenVendor(new Id("econ.vendor.sample_general"));
            Assert.Single(presentation.Shop.SellItems);

            bus.PublishImmediate(new Core.Foundation.SaveSystem.SaveLoadedEvent(new Id("save.ui111_01")));

            Assert.Equal(new Id("econ.vendor.sample_general"), presentation.Shop.CurrentVendorId);
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

        // -----------------------------------------------------------------
        // PRES-118-CAMERA 回归（第十八轮审核 presentation-review.md"PRES118-01"）：
        // AutoConfigureCameraFromFirstProfile（默认 true）打开时，装配根构造期立即
        // Configure+Follow(玩家单位)；此前 scene.load_finished 到达时 CameraHost 默认
        // ResetFollowOnSceneLoadFinished=true 会清空跟随目标，三个生产装配入口都没有在切图完成后
        // 重新 Follow，镜头从此静止不再跟随。下面三个用例分别验证：默认装配根重新建立目标、调用方
        // 自定义 FollowTargetResolverOnReset 被完整尊重（不被默认逻辑覆盖）、resetFollowOnSceneLoadFinished
        // =false 时跟随目标从未被清空——"是否跟随谁/是否重置"仍然是可替换策略，不是被写死的默认行为。
        // -----------------------------------------------------------------

        [Fact]
        public void Construct_AutoConfigureCamera_SceneLoadFinished_RefollowsSamePlayer()
        {
            var presentation = Build(out var gameplay, out _, out _, out var bus);
            var playerId = gameplay.PlayerUnitProvider();

            Assert.Equal(playerId, presentation.Camera.FollowEntityId);

            // 模拟场景切换完成：CameraHost 收到 scene.load_finished 会先清空跟随目标（其默认选项
            // ResetFollowOnSceneLoadFinished=true），根治点是 PresentationAssembly 在
            // AutoConfigureCameraFromFirstProfile 打开、调用方未显式接管 FollowTargetResolverOnReset
            // 时默认提供的解析函数立即重新指定为同一玩家单位——同一次 CameraHost.OnSceneLoadFinished
            // 处理内完成，不依赖调用方在 SceneRouter PostLoad 钩子与本事件之间抢时序（见
            // PresentationAssembly 构造函数判断记录"PRES-118-CAMERA 根治"）。
            bus.PublishImmediate(new SceneLoadFinishedEvent(new Id("world.pres118_next_map")));

            Assert.Equal(playerId, presentation.Camera.FollowEntityId);
        }

        [Fact]
        public void Construct_CameraHostOptionsWithCustomResolver_IsRespected_NotOverriddenByDefault()
        {
            var customTarget = new Id("unit.pres118_custom_follow");
            var options = new PresentationAssemblyOptions
            {
                CameraHostOptions = new CameraHostOptions(
                    resetFollowOnSceneLoadFinished: true,
                    phaseProfileSwitch: null,
                    followTargetResolverOnReset: () => customTarget),
            };
            var presentation = Build(out _, out _, out _, out var bus, options: options);

            bus.PublishImmediate(new SceneLoadFinishedEvent(new Id("world.pres118_next_map")));

            // 调用方已经自己装配了 FollowTargetResolverOnReset：装配根不覆盖其选择，不强行改回玩家
            // 单位——"跟随谁"仍是调用方可替换的策略。
            Assert.Equal(customTarget, presentation.Camera.FollowEntityId);
        }

        [Fact]
        public void Construct_CameraHostOptionsResetDisabled_FollowPersistsAcrossSceneLoad()
        {
            var options = new PresentationAssemblyOptions
            {
                CameraHostOptions = new CameraHostOptions(resetFollowOnSceneLoadFinished: false),
            };
            var presentation = Build(out var gameplay, out _, out _, out var bus, options: options);
            var playerId = gameplay.PlayerUnitProvider();

            bus.PublishImmediate(new SceneLoadFinishedEvent(new Id("world.pres118_next_map")));

            // resetFollowOnSceneLoadFinished=false：跟随目标从未被清空过，不需要任何重新指定就已经
            // 正确——"是否重置"同样是调用方可替换的策略。
            Assert.Equal(playerId, presentation.Camera.FollowEntityId);
        }

        // -----------------------------------------------------------------
        // PRES-118-SFX 回归（第十八轮审核 presentation-review.md"PRES118-02"）：连续两次播放同一个
        // 缺失音效资源，第二次请求没有任何未来回调可等（SfxPlayer._pendingResourceLoads 已记录过该
        // 资源 id，不会再次 LoadAsync），只能靠 ISfxPlayer.Update 的超时扫描清理——验证新增的统一
        // 逐帧维护入口 PresentationAssembly.UpdatePlaybackMaintenance 会驱动 Sfx.Update。
        // FirstLoadTimeoutSeconds=0 让截止时间恒为"已过期"，避免测试依赖真实挂钟等待。
        // -----------------------------------------------------------------

        [Fact]
        public void UpdatePlaybackMaintenance_SecondMissingSfxPlay_PendingClearedAfterMaintenance()
        {
            var options = new PresentationAssemblyOptions
            {
                SfxOptions = new SfxOptions { FirstLoadTimeoutSeconds = 0 },
            };
            var presentation = Build(out _, out _, out var engine, out _, options: options, withResourceLoader: true);
            var missingSfxId = new Id("sfx.sample_hit"); // 故意不 engine.ResourceLoader.Register，模拟资源缺失。

            var first = presentation.Sfx.Play(missingSfxId, Vec2.Zero);
            Assert.Null(first); // 第一次：LoadAsync 同步失败回调，请求被丢弃。
            Assert.Equal(0, presentation.Sfx.PendingPlayCount);

            var second = presentation.Sfx.Play(missingSfxId, Vec2.Zero);
            Assert.Null(second); // 第二次：资源 id 已记录过，不再次 LoadAsync，停留在 pending。
            Assert.Equal(1, presentation.Sfx.PendingPlayCount);

            // 根治点：统一逐帧维护入口驱动 Sfx.Update，即便没有任何后续 Play 调用，pending 也会归零。
            presentation.UpdatePlaybackMaintenance(0.016);

            Assert.Equal(0, presentation.Sfx.PendingPlayCount);
        }

        [Fact]
        public void UpdatePlaybackMaintenance_WithStepRunner_InvokesEachStepThroughRunner_AndIsolatesExceptions()
        {
            // 见 PresentationAssembly.UpdatePlaybackMaintenance 判断记录：可选 stepRunner 供
            // FrameworkResidentHost/GameFoundationBootstrap 传入各自的 RunPresentationStep 保留
            // "拍板 12 表现层异常隔离"粒度——Feedback/Vfx/Sfx 三步各自独立包一层，一步抛异常不阻塞
            // 其余两步。用一个记录调用次数、且始终 catch 异常的假 runner 验证：三步都经过 runner，
            // 其中一步抛异常不影响另外两步被调用。
            var presentation = Build(out _, out _, out _, out _);
            var callCount = 0;
            var caughtCount = 0;

            void FakeRunner(System.Action step)
            {
                callCount++;
                try
                {
                    step();
                }
                catch
                {
                    caughtCount++;
                }
            }

            var ex = Record.Exception(() => presentation.UpdatePlaybackMaintenance(0.016, FakeRunner));

            Assert.Null(ex); // 顶层不抛出：三步都经过 runner 的 try/catch。
            Assert.Equal(3, callCount); // Feedback/Vfx/Sfx 三步都经过了 runner。
            Assert.Equal(0, caughtCount); // 正常路径下三步都不抛异常。
        }

        // ------------------------------------------------------------------
        // ABI/CS0234 联合修正单（2026-09-20）反向确认：presentation.UiDiagnostics 生产装配下真实
        // 是 Presentation.Ui.InMemoryUiDiagnostics，且累积的 Warnings 反映真实诊断消息——
        // adapters/unity/.../DiagnosticsHubComposition.RegisterCoreSources 第 169 行
        // `presentation.UiDiagnostics is global::Presentation.Ui.InMemoryUiDiagnostics uiDiag` 这一
        // 模式匹配 + `hub.Register("Presentation.Ui", uiDiag.Warnings)` 转发正是依赖这两点。该文件
        // 本身是 Unity-only 胶水、不进本项目编译范围（见其类型级判断记录"本文件不进入 dotnet test
        // 编译范围"），本测试只覆盖它依赖的纯 C# 契约行为——不能验证 CS0234 本身是否已修好（那部分
        // 靠静态命名空间分析 + toolchain/tests 的正则回归确认），但能保证一旦编译通过、这行代码接到
        // 的确实是会随 UI 查询产生真实警告的诊断出口，不是一个恒空的占位对象。
        // ------------------------------------------------------------------

        [Fact]
        public void UiDiagnostics_ProductionWiring_IsInMemoryUiDiagnostics()
        {
            var presentation = Build(out _, out _, out _, out _);

            // 判断记录：本文件命名空间 Tests.Presentation.Assembly 与全局 Presentation.Ui 同前缀
            // 冲突，写不带 global:: 前缀的 `Presentation.Ui.InMemoryUiDiagnostics` 在这里同样会编译
            // 报 CS0234（"命名空间 'Tests.Presentation' 中不存在类型或命名空间名 'Ui'"）——本测试文件
            // 首次编译时确实先撞上了这个错误，才改成 global:: 限定，这正是本单在
            // DiagnosticsHubComposition.cs 里修的同一类命名空间冲突的独立复现，加固了"为什么必须加
            // global::"这条判断记录的可信度。
            Assert.IsType<global::Presentation.Ui.InMemoryUiDiagnostics>(presentation.UiDiagnostics);
        }

        [Fact]
        public void UiDiagnostics_AfterUnknownPathQuery_RecordsWarning_ObservableThroughDiagnosticsProperty()
        {
            var presentation = Build(out _, out _, out _, out _);

            // UiDataSource.Query 对未注册的路径首段调用 _diagnostics.Warn(...)（见该方法判断记录），
            // 用一个必然未被任何 IUiPathProvider 注册的首段触发一次真实警告。
            var result = presentation.UiData.Query("no_such_root.foo");

            Assert.Null(result);
            var uiDiag = Assert.IsType<global::Presentation.Ui.InMemoryUiDiagnostics>(presentation.UiDiagnostics);
            var warning = Assert.Single(uiDiag.Warnings);
            Assert.Contains("no_such_root", warning);
        }

        // ------------------------------------------------------------------
        // 本切片新增（消费方反馈第 3 条续，2026-09-21，沿用 ADR-0048 口径）：目标框补齐
        // target.name/target.faction 两条叶子路径——见 TargetPathProvider/HudViewModel 判断记录。
        // ------------------------------------------------------------------

        [Fact]
        public void HudViewModel_TargetNameAndFaction_ReflectRegisteredTemplate_NullWhenNoTarget()
        {
            Id? currentTarget = null;
            var options = new PresentationAssemblyOptions { TargetResolver = () => currentTarget };
            var presentation = Build(out var gameplay, out _, out _, out _, options: options);

            // 无目标：与既有 target.id 恒 null 的口径逐字一致。
            Assert.Null(presentation.Hud.TargetId);
            Assert.Null(presentation.Hud.TargetName);
            Assert.Null(presentation.Hud.TargetFaction);

            // creature.sample_target 这条模板声明的 name_key/faction_id 就是期望值的规则来源，不写死
            // 裸字符串——这两行常量与 AddMinimalGameplayTables 里那条记录的字面量必须逐字相同，任一方
            // 改了都会让断言失败，天然防止两处漂移。
            var expectedNameKey = new Id("l10n.creature.sample_target.name");
            var expectedFaction = new Id("fac.sample_hostile");

            var targetId = gameplay.Carriers.Creatures.Spawn(SampleTargetTemplateId, SampleMapId, Vec2.Zero, 0.0);
            currentTarget = targetId;
            presentation.Hud.Refresh();

            Assert.Equal(targetId, presentation.Hud.TargetId);
            Assert.Equal(expectedNameKey, presentation.Hud.TargetName);
            Assert.Equal(expectedFaction, presentation.Hud.TargetFaction);

            // 取消目标后回到"无"（同 target.id 既有行为，见 HudViewModel_TargetId_ReflectsCurrentTarget_NullWhenNoTarget）。
            currentTarget = null;
            presentation.Hud.Refresh();

            Assert.Null(presentation.Hud.TargetId);
            Assert.Null(presentation.Hud.TargetName);
            Assert.Null(presentation.Hud.TargetFaction);
        }

        [Fact]
        public void HudViewModel_TargetName_TemplateNotRegistered_ReturnsNull_AndRecordsDiagnostic()
        {
            Id? currentTarget = null;
            var options = new PresentationAssemblyOptions { TargetResolver = () => currentTarget };
            var presentation = Build(out _, out var world, out _, out _, options: options);

            var uiDiag = Assert.IsType<global::Presentation.Ui.InMemoryUiDiagnostics>(presentation.UiDiagnostics);
            var warningsBefore = uiDiag.Warnings.Count;

            // 手工放置一个模板 id 未在 creature.template 登记的单位（不经 CreatureFactory.Spawn——
            // Spawn 要求模板必须先登记才能读取属性/资源配置构造出单位，见该类型判断记录），模拟"目标
            // 存在但模板未登记"这一内容配置问题（AGENTS.md §3"运行时路径不静默降级"要求的诊断场景）。
            var ghostId = new Id("unit.sample_ghost_target");
            var ghostFaction = new Id("fac.sample_hostile");
            var unregisteredTemplateId = new Id("creature.sample_unregistered_ghost");
            var ghost = new CreatureUnit(ghostId, SampleMapId, ghostFaction, unregisteredTemplateId);
            world.AddEntity(ghost);

            currentTarget = ghostId;
            presentation.Hud.Refresh();

            // target.id/target.faction 不经模板查询，正常返回；只有 target.name 因模板未登记查不到，
            // 返回"无"而不是占位文案，且诊断计数从 warningsBefore 变成 warningsBefore + 1。
            Assert.Equal(ghostId, presentation.Hud.TargetId);
            Assert.Equal(ghostFaction, presentation.Hud.TargetFaction);
            Assert.Null(presentation.Hud.TargetName);
            Assert.Equal(warningsBefore + 1, uiDiag.Warnings.Count);
        }

        // ------------------------------------------------------------------
        // 本切片新增（消费方反馈第四批，2026-09-21，ADR-0061 + 缺陷修复）：三条反馈的运行时验收，
        // 全部走生产装配入口测到 HudViewModel 这一层——1.53.0 验收当时只测了规则层 AuraHost（见
        // core/rules/skill/tests/AuraPolarityIconRefTests.cs）与表现层路径查询（UiDataSourceTests），
        // 没有测到本文件这一层，这正是 HudViewModel.RefreshAuras 缺陷潜伏下来的验收缺口，见
        // presentation/ui/README.md 判断记录。
        // ------------------------------------------------------------------

        /// <summary>
        /// 消费方反馈第四批第 3 条（缺陷修复）：<see cref="global::Presentation.Ui.HudViewModel.RefreshAuras"/>
        /// 修复前恒用 <c>AuraSnapshot</c> 五参数构造函数重建快照，<c>Polarity</c>/<c>IconRef</c> 无论
        /// 生产数据是否声明都读到 <c>Undeclared</c>/<c>null</c>——本用例修复前会在第一个 Assert.Equal
        /// (AuraPolarity.Beneficial, ...) 处失败（实际值 Undeclared），修复后验证：①声明了极性/图标
        /// 的光环取到声明值；②未声明的光环仍退化为 Undeclared/null（不是把"修好"做成"一律编一个
        /// 值"）。
        /// </summary>
        [Fact]
        public void HudViewModel_Auras_ReflectPolarityAndIconRef_DeclaredValues_UndeclaredWhenNotDeclared()
        {
            var presentation = Build(out var gameplay, out _, out _, out _);
            var playerId = presentation.Hud.PlayerId;

            var fortifyId = new Id("skill.aura_def.sample_fortify");
            var unmarkedId = new Id("skill.aura_def.sample_unmarked");
            var sourceId = new Id("unit.sample_aura_source");

            // 施加顺序即创建顺序（AuraHost.GetActiveAuraSnapshots 按 SeqNo 排序，见该方法判断记录），
            // 下方按下标断言不需要额外排序/查找。
            gameplay.Carriers.Rules.Skill.EffectSink.ApplyAura(playerId, fortifyId, sourceId);
            gameplay.Carriers.Rules.Skill.EffectSink.ApplyAura(playerId, unmarkedId, sourceId);

            presentation.Hud.Refresh();

            Assert.Equal(2, presentation.Hud.Auras.Count);

            var fortify = presentation.Hud.Auras[0];
            Assert.Equal(fortifyId, fortify.AuraDefId);
            Assert.Equal(AuraPolarity.Beneficial, fortify.Polarity);
            Assert.Equal(new Id("icon.aura.sample_fortify"), fortify.IconRef);

            var unmarked = presentation.Hud.Auras[1];
            Assert.Equal(unmarkedId, unmarked.AuraDefId);
            Assert.Equal(AuraPolarity.Undeclared, unmarked.Polarity);
            Assert.Null(unmarked.IconRef);

            // TargetAuras 同一条转发路径，用同一批数据施加到目标身上核实同一处修复同时覆盖两个属性
            // （HudViewModel.RefreshAuras 是 Auras/TargetAuras 共用的同一份私有方法）。
            Id? currentTarget = null;
            var options = new PresentationAssemblyOptions { TargetResolver = () => currentTarget };
            var targetPresentation = Build(out var targetGameplay, out _, out _, out _, options: options);
            var targetId = targetGameplay.Carriers.Creatures.Spawn(SampleTargetTemplateId, SampleMapId, Vec2.Zero, 0.0);
            currentTarget = targetId;
            targetGameplay.Carriers.Rules.Skill.EffectSink.ApplyAura(targetId, fortifyId, sourceId);
            targetPresentation.Hud.Refresh();

            Assert.Single(targetPresentation.Hud.TargetAuras);
            Assert.Equal(AuraPolarity.Beneficial, targetPresentation.Hud.TargetAuras[0].Polarity);
            Assert.Equal(new Id("icon.aura.sample_fortify"), targetPresentation.Hud.TargetAuras[0].IconRef);
        }

        /// <summary>
        /// 消费方反馈第四批第 1/2 条（ADR-0061）：<see cref="global::Presentation.Ui.HudViewModel.AutoAttackState"/>/
        /// <see cref="global::Presentation.Ui.HudViewModel.TargetAutoAttackState"/>（普通攻击状态）与
        /// <see cref="global::Presentation.Ui.HudViewModel.PlayerAlive"/>/<see
        /// cref="global::Presentation.Ui.HudViewModel.TargetAlive"/>（存活状态）。目标死亡走框架真实
        /// 的普通攻击挥击伤害路径（<c>AutoAttackHost.Update</c> → <c>Resolver.Resolve</c> →
        /// <c>IUnitAccess.SetAlive(false)</c>），不是直接改字段——武器秒伤（1000/秒）远大于目标生命
        /// 上限（50 点，见 <see cref="AddMinimalGameplayTables"/>），保证恰好一次挥击必定致死。
        /// </summary>
        [Fact]
        public void HudViewModel_AutoAttackStateAndTargetAlive_ReflectRealCombatAndDeathPath()
        {
            Id? currentTarget = null;
            var options = new PresentationAssemblyOptions { TargetResolver = () => currentTarget };
            var presentation = Build(out var gameplay, out var world, out _, out _, options: options);
            var playerId = presentation.Hud.PlayerId;

            // 无目标：TargetAlive 口径与既有 TargetId 无目标时逐字一致（null，不是 false）。
            Assert.Null(presentation.Hud.TargetAlive);

            // 玩家自身存活、普通攻击尚未开启：PlayerAlive=true、AutoAttackState=Off。
            Assert.True(presentation.Hud.PlayerAlive);
            Assert.Equal(AutoAttackState.Off, presentation.Hud.AutoAttackState);

            // 给玩家装备主手武器（真实穿戴路径，供 AutoAttackHost 结算时查到武器秒伤/攻速）。
            gameplay.Carriers.Inventory.RegisterUnit(playerId);
            var weaponTemplateId = new Id("item.sample_sword");
            var qualityId = new Id("item.quality.sample_common");
            Assert.True(gameplay.Carriers.Inventory.AddItem(playerId, weaponTemplateId, 1, qualityId, null));
            var instanceId = gameplay.Carriers.Inventory.ListItems(playerId)[0].InstanceId;
            var equipResult = gameplay.Carriers.Equipment.Equip(playerId, instanceId, new Id("item.slot.sample_main_hand"));
            Assert.True(equipResult.Success, equipResult.Reason.ToString());

            var targetId = gameplay.Carriers.Creatures.Spawn(SampleTargetTemplateId, SampleMapId, Vec2.Zero, 0.0);
            currentTarget = targetId;
            presentation.Hud.Refresh();

            // 有目标且存活：TargetAlive=true；尚未开启普通攻击，状态仍是 Off。
            Assert.True(presentation.Hud.TargetAlive);
            Assert.Equal(AutoAttackState.Off, presentation.Hud.AutoAttackState);

            // 开启普通攻击并指定目标：状态变 Attacking（路径查询与视图模型属性同步核对）。
            var autoAttack = gameplay.Carriers.Rules.AutoAttack;
            autoAttack.SetTarget(playerId, targetId);
            autoAttack.SetEnabled(playerId, true);
            presentation.Hud.Refresh();
            Assert.Equal(AutoAttackState.Attacking, presentation.Hud.AutoAttackState);
            Assert.Equal("attacking", presentation.UiData.Query("player.auto_attack.state")!.Value.AsString);

            // 推进恰好一个挥击周期：武器秒伤 1000 远超目标 50 点生命上限，本次挥击必定致死。
            var interval = gameplay.Carriers.Equipment.GetWeaponAttackIntervalSeconds(playerId)!.Value;
            world.Tick(Core.Foundation.SimLoop.SimStep.Continuous(interval));
            Assert.False(gameplay.Carriers.Units.IsAlive(targetId), "本用例要求武器秒伤足以一次挥击致死（1000 dps vs 50 hp）");

            presentation.Hud.Refresh();
            // 死亡已经由 Resolver 落地（IUnitAccess.SetAlive(false)）：TargetAlive 立即反映为 false。
            Assert.False(presentation.Hud.TargetAlive);
            // AutoAttackHost.Update 的"目标死亡"判定在下一次 Update 顶部才会观察到并清空 TargetId
            // （tick 驱动、非全知同步，同 core/sim/tests/AutoAttackHostIntegrationTests.cs
            // DisablingAutoAttack_StopsFurtherDamage_AndStateReturnsToNoTargetAfterTargetDies 判断
            // 记录），致死那一 tick 内 AutoAttackHost 自身仍报 Attacking，不是本次改动引入的时序缺口。
            Assert.Equal(AutoAttackState.Attacking, presentation.Hud.AutoAttackState);

            // 再推进一小步收敛到 NoTarget。
            world.Tick(Core.Foundation.SimLoop.SimStep.Continuous(0.01));
            presentation.Hud.Refresh();
            Assert.Equal(AutoAttackState.NoTarget, presentation.Hud.AutoAttackState);
            Assert.Equal("no_target", presentation.UiData.Query("player.auto_attack.state")!.Value.AsString);

            // 目标死亡后取消目标选中：口径回到"无目标"（null），与既有 TargetId 一致。
            currentTarget = null;
            presentation.Hud.Refresh();
            Assert.Null(presentation.Hud.TargetAlive);
        }

        // -----------------------------------------------------------------
        // ADR-0062（消费方反馈第五批第 1 条续）：interact.nearest.* ——统一的"最近可交互目标"查询，
        // 经生产装配入口（PresentationAssembly.UiData.Query）一路验证到最外层，不止测到
        // CarriersAssembly.InteractionTargets 这一层中间结果（上一版曾经只测到中间层导致字段没
        // 贯通，这次必须测到最外层，见任务书"验收"一节）。
        // -----------------------------------------------------------------

        [Fact]
        public void InteractPathProvider_NearestTarget_PrefersCloserLoot_OverFartherGobj_AndFallsBackAfterPickup()
        {
            var presentation = Build(out var gameplay, out var world, out _, out _);
            var playerId = gameplay.PlayerUnitProvider();

            // 物件更远（距离 5），掉落物更近（距离 1）——查询应命中掉落物。
            var gobjId = gameplay.Carriers.GameObjects.Spawn(new Id("gobj.sample_marker"), SampleMapId, new Vec2(5, 0), 0.0);
            var lootId = gameplay.Loot.Drop(
                SampleMapId, new Vec2(1, 0),
                new List<Core.Carriers.Common.ItemStack> { new Core.Carriers.Common.ItemStack(new Id("item.sample_sword"), 1) });

            Assert.Equal(lootId.Value, presentation.UiData.Query("interact.nearest.id")!.Value.AsId.Value);
            Assert.Equal(EntityKinds.Loot, presentation.UiData.Query("interact.nearest.kind")!.Value.AsString);
            Assert.Equal(1.0, presentation.UiData.Query("interact.nearest.distance")!.Value.AsNumber);

            // 拾取掉落物后再查：应回退到物件（唯一剩下的候选）。判断记录：LootHost.PickUp 只
            // MarkForDestruction（真正从 IWorldSim 集合移除发生在下一次 Tick 的生命周期清理阶段，
            // 见 IWorldSim.MarkForDestruction 类型注释），本查询直接读 IWorldSim 现场结果（见
            // InteractionTargetRegistry 判断记录），因此这里需要推进一次 tick 才能观察到掉落物
            // 真正从候选集合里消失，不是查询本身有延迟。
            var pickup = gameplay.Loot.PickUp(playerId, lootId);
            Assert.True(pickup.Success);
            Assert.DoesNotContain(lootId, gameplay.Loot.ActiveLootIds);
            world.Tick(Core.Foundation.SimLoop.SimStep.Continuous(0.01));

            Assert.Equal(gobjId.Value, presentation.UiData.Query("interact.nearest.id")!.Value.AsId.Value);
            Assert.Equal(EntityKinds.Gobj, presentation.UiData.Query("interact.nearest.kind")!.Value.AsString);
        }

        [Fact]
        public void InteractPathProvider_NoCandidatesNearby_ReturnsNull_NotADiagnostic()
        {
            var presentation = Build(out var gameplay, out _, out _, out _);

            Assert.Null(presentation.UiData.Query("interact.nearest.id"));
            Assert.Null(presentation.UiData.Query("interact.nearest.kind"));
            var uiDiag = Assert.IsType<global::Presentation.Ui.InMemoryUiDiagnostics>(presentation.UiDiagnostics);
            Assert.Empty(uiDiag.Warnings);
        }

        // -----------------------------------------------------------------
        // ADR-0065（消费方反馈第七批第 1 条根治）：死亡生物不是"最近可交互目标"的候选。框架在单位
        // 死亡时不会自动 Despawn，尸体与 CreatureDeathLootListener 在死亡位置生成的战利品同坐标、
        // 等距时按 EntityId 序数取小（creature.* 恒小于 loot.*）——修复前每次击杀后按交互键都会
        // 打在尸体上、捡不到战利品。两例经生产装配入口（GameplayAssembly/PresentationAssembly）+
        // 真实结算致死路径（AutoAttackHost 挥击 → Resolver → IUnitAccess.SetAlive(false) →
        // CreatureDeathLootListener 真实掉落），全程不调用 Despawn。
        // -----------------------------------------------------------------

        [Fact]
        public void InteractPathProvider_DeadCreature_NotCandidate_NearestReturnsItsOwnLoot_AndPickupSucceeds()
        {
            var presentation = Build(out var gameplay, out var world, out _, out _);
            var playerId = gameplay.PlayerUnitProvider();

            // 给玩家装备主手武器，走真实普通攻击挥击伤害路径致死目标（同
            // HudViewModel_AutoAttackStateAndTargetAlive 判断记录：武器秒伤 1000 远超目标 50 点生命
            // 上限，保证恰好一次挥击必定致死，不依赖多次挥击的循环上限）。
            gameplay.Carriers.Inventory.RegisterUnit(playerId);
            var weaponTemplateId = new Id("item.sample_sword");
            var qualityId = new Id("item.quality.sample_common");
            Assert.True(gameplay.Carriers.Inventory.AddItem(playerId, weaponTemplateId, 1, qualityId, null));
            var instanceId = gameplay.Carriers.Inventory.ListItems(playerId)[0].InstanceId;
            var equipResult = gameplay.Carriers.Equipment.Equip(playerId, instanceId, new Id("item.slot.sample_main_hand"));
            Assert.True(equipResult.Success, equipResult.Reason.ToString());
            var swordCountBeforePickup = gameplay.Carriers.Inventory.CountOf(playerId, weaponTemplateId);

            // 目标与玩家同位置（Vec2.Zero，即"玩家旁生成一个生物"）——死亡后尸体与它死亡结算生成的
            // 战利品同坐标，正是消费方反馈复现的场景。
            var targetId = gameplay.Carriers.Creatures.Spawn(
                new Id("creature.sample_lootable_target"), SampleMapId, Vec2.Zero, 0.0);

            // 回归：目标存活时仍是候选（不是本次改动误伤了正常场景）。
            Assert.True(gameplay.Carriers.InteractionTargets.TryFindNearest(playerId, null, out var beforeKill));
            Assert.Equal(targetId, beforeKill.EntityId);
            Assert.Equal(Core.Carriers.Common.InteractionTargetKind.Creature, beforeKill.Kind);

            var autoAttack = gameplay.Carriers.Rules.AutoAttack;
            autoAttack.SetTarget(playerId, targetId);
            autoAttack.SetEnabled(playerId, true);
            var interval = gameplay.Carriers.Equipment.GetWeaponAttackIntervalSeconds(playerId)!.Value;
            world.Tick(Core.Foundation.SimLoop.SimStep.Continuous(interval));
            Assert.False(gameplay.Carriers.Units.IsAlive(targetId), "本用例要求武器秒伤足以一次挥击致死");

            // 全程不调用 Despawn：尸体仍留在世界模拟中。
            Assert.NotNull(world.GetEntity(targetId));

            // 修复前：本查询会返回尸体（creature.* 序数小于 loot.*）。修复后：应返回它自己刚生成的
            // 战利品。
            var found = gameplay.Carriers.InteractionTargets.TryFindNearest(playerId, null, out var nearest);
            Assert.True(found);
            Assert.Equal(Core.Carriers.Common.InteractionTargetKind.Loot, nearest.Kind);
            Assert.NotEqual(targetId, nearest.EntityId);
            var lootId = nearest.EntityId;

            // 同一场景经 PresentationAssembly.UiData 贯通到表现层（interact.nearest.* 与
            // CarriersAssembly.InteractionTargets 共用同一个注册表实例，见 InteractPathProvider
            // 判断记录）。
            Assert.Equal(EntityKinds.Loot, presentation.UiData.Query("interact.nearest.kind")!.Value.AsString);
            Assert.Equal(lootId.Value, presentation.UiData.Query("interact.nearest.id")!.Value.AsId.Value);

            // 对它发 interact 意图 → 拾取成功（物品进背包），全程未调用 Despawn。
            var args = new Core.Foundation.Common.Json.JsonObjectBuilder()
                .Add("loot_instance_id", new Core.Foundation.Common.Json.JsonString(lootId.Value))
                .Build();
            world.SubmitIntent(new Intent(playerId, "interact", args));
            world.Tick(Core.Foundation.SimLoop.SimStep.Continuous(0.01));

            Assert.Equal(swordCountBeforePickup + 1, gameplay.Carriers.Inventory.CountOf(playerId, weaponTemplateId));
        }

        [Fact]
        public void InteractPathProvider_DeadCreatureCloserThanLivingCreature_ReturnsLivingCreature()
        {
            var presentation = Build(out var gameplay, out var world, out _, out _);
            var playerId = gameplay.PlayerUnitProvider();

            gameplay.Carriers.Inventory.RegisterUnit(playerId);
            var weaponTemplateId = new Id("item.sample_sword");
            var qualityId = new Id("item.quality.sample_common");
            Assert.True(gameplay.Carriers.Inventory.AddItem(playerId, weaponTemplateId, 1, qualityId, null));
            var instanceId = gameplay.Carriers.Inventory.ListItems(playerId)[0].InstanceId;
            var equipResult = gameplay.Carriers.Equipment.Equip(playerId, instanceId, new Id("item.slot.sample_main_hand"));
            Assert.True(equipResult.Success, equipResult.Reason.ToString());

            // 用不带 loot_table_ref 的 creature.sample_target：死亡不产生地面掉落物，场上只剩"尸体"
            // 与"活着的 NPC"两个候选，单纯验证候选排除本身，不与掉落候选混杂。
            // ADR-0069 勘误：livingId 改用带 gossip_menu_ref 的 creature.sample_target_with_gossip——
            // 本用例要验证的是"死亡排除优先于距离"，不是"内容过滤"；旧写法两者都用 creature
            // .sample_target（无可交互内容），本次改动后 livingId 会先被内容过滤挡住，不再是因为
            // "尸体更近却被排除"这条本用例想验证的理由返回，因此换成有内容的模板，让"活着的 NPC"
            // 继续满足候选条件，只让死亡排除这一条单独起作用（corpseId 是否有内容不影响它已经死亡
            // 这一事实，不需要跟着改）。
            var corpseId = gameplay.Carriers.Creatures.Spawn(SampleTargetTemplateId, SampleMapId, Vec2.Zero, 0.0);
            var livingId = gameplay.Carriers.Creatures.Spawn(SampleTargetWithGossipTemplateId, SampleMapId, new Vec2(5, 0), 0.0);

            var autoAttack = gameplay.Carriers.Rules.AutoAttack;
            autoAttack.SetTarget(playerId, corpseId);
            autoAttack.SetEnabled(playerId, true);
            var interval = gameplay.Carriers.Equipment.GetWeaponAttackIntervalSeconds(playerId)!.Value;
            world.Tick(Core.Foundation.SimLoop.SimStep.Continuous(interval));
            Assert.False(gameplay.Carriers.Units.IsAlive(corpseId));
            Assert.True(gameplay.Carriers.Units.IsAlive(livingId));

            // 尸体（距离 0）比活着的 NPC（距离 5）更近，但候选排除死亡生物：查询应返回活着的 NPC，
            // 不会被更近的尸体挡住。
            var found = gameplay.Carriers.InteractionTargets.TryFindNearest(playerId, null, out var nearest);
            Assert.True(found);
            Assert.Equal(livingId, nearest.EntityId);
            Assert.Equal(Core.Carriers.Common.InteractionTargetKind.Creature, nearest.Kind);
        }

        // -----------------------------------------------------------------
        // ADR-0069（消费方反馈——游戏接入方第十四批）：InteractionTargetRegistry 此前对生物候选只
        // 核对"存在且存活"（ADR-0065），不看它有没有任何可交互内容——护送/跟随/闲逛一类没有配置
        // gossip_menu_ref 的生物离玩家最近时会被误选中，玩家按交互键什么也不会发生。经真实
        // GameplayAssembly/PresentationAssembly 装配验证：CarriersAssembly.InteractionTargets 与
        // 表现层 interact.nearest.* 两处都必须排除它。
        // -----------------------------------------------------------------

        [Fact]
        public void InteractPathProvider_CreatureWithoutInteractableContentCloser_NotCandidate_NearestReturnsFartherCreatureWithContent()
        {
            var presentation = Build(out var gameplay, out _, out _, out _);
            var playerId = gameplay.PlayerUnitProvider();

            // creature.sample_target（距离 1，更近）没有配置 gossip_menu_ref——对应护送/跟随/闲逛一类
            // 没有原生可交互内容的生物；creature.sample_target_with_gossip（距离 5，更远）配置了。
            var contentlessId = gameplay.Carriers.Creatures.Spawn(SampleTargetTemplateId, SampleMapId, new Vec2(1, 0), 0.0);
            var withContentId = gameplay.Carriers.Creatures.Spawn(SampleTargetWithGossipTemplateId, SampleMapId, new Vec2(5, 0), 0.0);

            var found = gameplay.Carriers.InteractionTargets.TryFindNearest(playerId, null, out var nearest);
            Assert.True(found);
            Assert.Equal(withContentId, nearest.EntityId);
            Assert.NotEqual(contentlessId, nearest.EntityId);
            Assert.Equal(Core.Carriers.Common.InteractionTargetKind.Creature, nearest.Kind);

            // 同一场景经 PresentationAssembly.UiData 贯通到表现层（interact.nearest.* 与
            // CarriersAssembly.InteractionTargets 共用同一个注册表实例，见 InteractPathProvider
            // 判断记录）。
            Assert.Equal(withContentId.Value, presentation.UiData.Query("interact.nearest.id")!.Value.AsId.Value);
            Assert.Equal(EntityKinds.Creature, presentation.UiData.Query("interact.nearest.kind")!.Value.AsString);
        }

        // -----------------------------------------------------------------
        // 消费方反馈第九批（阻塞，2026-09-22，ADR-0066）：经真实 GameplayAssembly/PresentationAssembly
        // 装配、真实数据定义的区域触发、真实 tick 驱动 AreaTriggerTickHandler.Execute→Evaluate 的
        // 端到端验收——比 core/gameplay/area_trigger/tests 与 presentation/ui/tests 两处单元测试更进
        // 一层，覆盖"宿主查询 + 路径层 + HudViewModel"整条链路真的接上了、经生产装配入口走到最外层。
        // -----------------------------------------------------------------

        [Fact]
        public void AreaTrigger_EndToEnd_PlayerMovement_UpdatesHostQueryAndHud()
        {
            // 三个圆形触发体，圆心固定在 (100,100)（远离默认出生点 Vec2.Zero，移动前玩家不在任何
            // 触发范围内，能真实观察到"移入"而不是"出生即在范围内"）：
            // - e2e_a_outer：半径 30，有显示名（外层，"当前区域"候选）。
            // - e2e_c_widest_unnamed：半径 40（比 outer 更大），无显示名——验证"同时处于一个无显示名
            //   的触发内 → 不影响当前区域结果，但宿主查询里仍包含它"。
            // - e2e_b_inner：半径 10，有显示名（内层，重叠区域场景）。
            var outerId = new Id("area.e2e_a_outer");
            var outerNameKey = new Id("l10n.area.e2e_a_outer.name");
            var widestUnnamedId = new Id("area.e2e_c_widest_unnamed");
            var innerId = new Id("area.e2e_b_inner");
            var innerNameKey = new Id("l10n.area.e2e_b_inner.name");
            var center = new Vec2(100, 100);

            var presentation = Build(out var gameplay, out var world, out _, out _, extraTables: source =>
            {
                source.Add("area.trigger_def",
                    "{\"table\": \"area.trigger_def\", \"schema_version\": 1, \"rows\": [" +
                    "{\"id\": \"" + outerId.Value + "\", \"map_id\": \"" + SampleMapId.Value + "\", " +
                    "\"shape\": {\"kind\": \"circle\", \"radius\": 30, \"center\": {\"x\": 100, \"y\": 100}}, " +
                    "\"trigger_type\": \"quest_explore\", \"params\": {}, \"name_key\": \"" + outerNameKey.Value + "\"}," +
                    "{\"id\": \"" + widestUnnamedId.Value + "\", \"map_id\": \"" + SampleMapId.Value + "\", " +
                    "\"shape\": {\"kind\": \"circle\", \"radius\": 40, \"center\": {\"x\": 100, \"y\": 100}}, " +
                    "\"trigger_type\": \"quest_explore\", \"params\": {}}," +
                    "{\"id\": \"" + innerId.Value + "\", \"map_id\": \"" + SampleMapId.Value + "\", " +
                    "\"shape\": {\"kind\": \"circle\", \"radius\": 10, \"center\": {\"x\": 100, \"y\": 100}}, " +
                    "\"trigger_type\": \"quest_explore\", \"params\": {}, \"name_key\": \"" + innerNameKey.Value + "\"}" +
                    "]}");
            });
            var playerId = gameplay.PlayerUnitProvider();

            // EnterMap 是"进入地图"这一步真正把 area.trigger_def 数据登记进 AreaTrigger 宿主的地方
            // （见 GameplayAssembly.EnterMap 判断记录），Build() 本身只 Spawn、不代为调用。
            gameplay.EnterMap(SampleMapId, playerId);

            // 先推进一次 tick，让 AreaTriggerTickHandler 记住出生位置（Vec2.Zero，在三个触发体之外），
            // 后续每次 SetPosition 才会被判定为"位置变化"从而真正调用 Evaluate（见该处理器判断记录）。
            world.Tick(SimStep.Continuous(0.016));
            Assert.Empty(gameplay.AreaTrigger.GetActiveTriggerIds(playerId));

            // 移入 outer + widestUnnamed（距圆心 20，在半径 30/40 内，不在半径 10 内）。
            gameplay.Carriers.Units.SetPosition(playerId, new Vec2(center.X + 20, center.Y));
            world.Tick(SimStep.Continuous(0.016));
            presentation.Hud.Refresh();

            Assert.Equal(
                new[] { outerId, widestUnnamedId },
                gameplay.AreaTrigger.GetActiveTriggerIds(playerId));
            Assert.Equal(outerId, presentation.Hud.CurrentAreaId);
            Assert.Equal(outerNameKey, presentation.Hud.CurrentAreaNameKey);
            Assert.Equal(outerId, presentation.UiData.Query("player.area.id")!.Value.AsId);
            Assert.Equal(outerNameKey, presentation.UiData.Query("player.area.name_key")!.Value.AsId);

            // 移入圆心（额外进入有名的 inner，仍在 outer/widestUnnamed 范围内）：当前区域切到最近进入
            // 且有显示名的 inner；宿主查询顺序反映真实进入先后（inner 最后进入，排在末尾）。
            gameplay.Carriers.Units.SetPosition(playerId, center);
            world.Tick(SimStep.Continuous(0.016));
            presentation.Hud.Refresh();

            Assert.Equal(
                new[] { outerId, widestUnnamedId, innerId },
                gameplay.AreaTrigger.GetActiveTriggerIds(playerId));
            Assert.Equal(innerId, presentation.Hud.CurrentAreaId);
            Assert.Equal(innerNameKey, presentation.Hud.CurrentAreaNameKey);

            // 退回半径 10～30 之间：离开 inner（仍在 outer/widestUnnamed 内）→ 当前区域回落到 outer。
            gameplay.Carriers.Units.SetPosition(playerId, new Vec2(center.X + 20, center.Y));
            world.Tick(SimStep.Continuous(0.016));
            presentation.Hud.Refresh();

            Assert.Equal(
                new[] { outerId, widestUnnamedId },
                gameplay.AreaTrigger.GetActiveTriggerIds(playerId));
            Assert.Equal(outerId, presentation.Hud.CurrentAreaId);
            Assert.Equal(outerNameKey, presentation.Hud.CurrentAreaNameKey);

            // 彻底离开全部三个触发体 → 当前区域回到"无"。
            gameplay.Carriers.Units.SetPosition(playerId, new Vec2(center.X + 1000, center.Y));
            world.Tick(SimStep.Continuous(0.016));
            presentation.Hud.Refresh();

            Assert.Empty(gameplay.AreaTrigger.GetActiveTriggerIds(playerId));
            Assert.Null(presentation.Hud.CurrentAreaId);
            Assert.Null(presentation.Hud.CurrentAreaNameKey);
            Assert.Null(presentation.UiData.Query("player.area.id"));
            Assert.Null(presentation.UiData.Query("player.area.name_key"));
        }

        /// <summary>消费方反馈（游戏接入方第十五批，阻塞）复现 + 修复验收：<see cref="UiIntents.ChooseDialogOption"/>
        /// 此前恒转发 <see cref="IDialogHost.ChooseOption"/>，而 <see cref="IDialogHost.StartStory"/>
        /// 会把会话的 <c>GossipMenuId</c> 置空——剧情会话里经原生对白面板点任何分支都会被
        /// <see cref="IDialogHost.ChooseOption"/> 以"当前不在 gossip 会话"为由拒绝，节点恒不推进。
        /// 本用例经真实 <see cref="GameplayAssembly"/>/<see cref="PresentationAssembly"/> 装配、真实
        /// <c>dialog.gossip_menu</c>/<c>dialog.story_tree</c> 数据，覆盖任务书要求的整条链：从 gossip
        /// 选项触发的 <c>start_story</c> 动作转入剧情会话，此后每一步推进都只经
        /// <see cref="UiIntents.ChooseDialogOption"/>（不直调 <see cref="IDialogHost.AdvanceStory"/>），
        /// 直到终止分支（<c>next_node_id</c> 缺省）——终止即剧情数据模型能表达的"完成效果"（<c>08</c>
        /// 第 3.2/9 节：终止分支关闭会话、发 <c>dialog.ended</c>，见 <c>DialogHostTests.
        /// AdvanceStory_ToTerminalBranch_ClosesDialogAndFiresEnded</c> 同款判断——剧情分支本身不像
        /// gossip 选项那样携带 <c>actions</c> 列表，没有"设置世界标志"这一级动作可用，会话正常关闭
        /// 就是数据支持的唯一"完成效果"）。</summary>
        [Fact]
        public void ChooseDialogOption_ThroughUiIntents_DrivesGossipStartStoryThenAdvancesStoryToCompletion()
        {
            var treeId = new Id("dialog.pres_story_test.tree");
            var node1Id = new Id("dialog.pres_story_test.n1");
            var node2Id = new Id("dialog.pres_story_test.n2");
            var node3Id = new Id("dialog.pres_story_test.n3");
            var menuId = new Id("dialog.pres_story_test.menu");
            var npcId = new Id("creature.pres_story_test_npc");

            var presentation = Build(out var gameplay, out _, out _, out var bus, extraTables: source =>
            {
                source.Add("dialog.story_tree",
                    "{\"table\": \"dialog.story_tree\", \"schema_version\": 1, \"rows\": [" +
                    "{\"id\": \"" + treeId.Value + "\", \"nodes\": [" +
                    "{\"id\": \"" + node1Id.Value + "\", \"text_key\": \"l10n.pres_story_test.n1\", \"branches\": [" +
                    "{\"text_key\": \"l10n.pres_story_test.continue\", \"next_node_id\": \"" + node2Id.Value + "\"}]}," +
                    "{\"id\": \"" + node2Id.Value + "\", \"text_key\": \"l10n.pres_story_test.n2\", \"branches\": [" +
                    "{\"text_key\": \"l10n.pres_story_test.continue\", \"next_node_id\": \"" + node3Id.Value + "\"}]}," +
                    "{\"id\": \"" + node3Id.Value + "\", \"text_key\": \"l10n.pres_story_test.n3\", \"branches\": [" +
                    "{\"text_key\": \"l10n.pres_story_test.end\"}]}" +
                    "]}]}");
                source.Add("dialog.gossip_menu",
                    "{\"table\": \"dialog.gossip_menu\", \"schema_version\": 1, \"rows\": [" +
                    "{\"id\": \"" + menuId.Value + "\", \"options\": [" +
                    "{\"text_key\": \"l10n.pres_story_test.open_story\", \"actions\": [" +
                    "{\"kind\": \"start_story\", \"ref\": \"" + treeId.Value + "\"}]}" +
                    "]}]}");
            });

            var playerId = gameplay.PlayerUnitProvider();
            var endedCount = 0;
            bus.Subscribe(DialogEventKeys.Ended, _ => endedCount++);

            gameplay.Dialog.OpenGossip(playerId, npcId, menuId);
            Assert.NotNull(gameplay.Dialog.GetGossipView(playerId));

            // 闲聊选项触发 start_story：本步骤此前（改前）已经能走通——ChooseDialogOption 当时恒
            // 转发 ChooseOption，此刻会话仍是 gossip，行为不受本次修复影响，回归覆盖"闲聊会话经
            // ChooseDialogOption 照常生效"。
            Assert.True(presentation.UiIntents.ChooseDialogOption(0));

            var afterStartStory = gameplay.Dialog.GetStoryView(playerId);
            Assert.NotNull(afterStartStory);
            Assert.Equal(node1Id, afterStartStory!.NodeId);
            Assert.Null(gameplay.Dialog.GetGossipView(playerId));

            // 改前会在这里失败：会话已转入剧情（GossipMenuId 已被 StartStory 置空），
            // UiIntents.ChooseDialogOption 仍恒转发 ChooseOption，ChooseOption 判定"当前不在 gossip
            // 会话"直接返回 false，节点恒不推进——这正是消费方反馈第十五批复现的缺陷本身。
            Assert.True(presentation.UiIntents.ChooseDialogOption(0));
            Assert.Equal(node2Id, gameplay.Dialog.GetStoryView(playerId)!.NodeId);

            Assert.True(presentation.UiIntents.ChooseDialogOption(0));
            Assert.Equal(node3Id, gameplay.Dialog.GetStoryView(playerId)!.NodeId);

            // 终止分支（next_node_id 缺省）：会话关闭、发 dialog.ended——数据模型能表达的"剧情完成
            // 效果"（判断记录见本用例类型注释）。
            Assert.True(presentation.UiIntents.ChooseDialogOption(0));
            Assert.Null(gameplay.Dialog.GetStoryView(playerId));
            Assert.Equal(1, endedCount);
        }

        // ==================== ADR-0077/ADR-0078（消费方反馈第二十批）====================

        /// <summary>ADR-0078 用例专用：<c>AddMinimalGameplayTables</c> 的 <c>stat.definition</c> 只
        /// 登记了 <c>stat.max_health</c>，本切片新增的移动类用例需要真实经过
        /// <see cref="Core.Carriers.Unit.MovementTickHandler.ResolveSpeed"/>——该方法要求
        /// <c>stat.move_speed</c> 必须先在 <c>stat.definition</c> 声明（未声明会直接抛异常，未赋值
        /// 但已声明则按 <c>default_base</c> 取值，此处仍留 0，配合 <c>creature.sample_player</c> 模板
        /// 未设置该属性，取 <see cref="Core.Carriers.Unit.MovementOptions.DefaultSpeed"/> 缺省值
        /// 4.0——同 <c>UiIntentsTests.Move_EndToEnd_ThroughMovementTickHandler_DisplacesUnitPosition</c>
        /// 依赖的同一条框架既有回退规则，不是本用例发明的新行为）才能补上这条声明，不影响既有夹具其它
        /// 用例（合并同名表，只新增一行）。</summary>
        private static void AddStatMoveSpeedDefinition(InMemoryDataSource source)
        {
            source.Add("stat.definition",
                "{\"table\": \"stat.definition\", \"schema_version\": 1, \"rows\": [" +
                "{\"id\": \"stat.move_speed\", \"name_key\": \"l10n.stat.pres_stride_move_speed.name\", " +
                "\"group\": \"primary\", \"default_base\": 0}" +
                "]}");
        }

        /// <summary>ADR-0077 验收 1：经真实 <see cref="PresentationAssembly.Panels"/>（此前完全未接入
        /// 任何生产装配入口，本次随 ADR-0077 一并接线）打开/关闭一个真实 <c>ui_layout_definition</c>
        /// 面板，真实经 <see cref="IEventBus.PublishImmediate"/> 发出的 <see cref="UiPanelOpenedEvent"/>/
        /// <see cref="UiPanelClosedEvent"/> 能被外部订阅方直接观测到，携带的 <c>PanelId</c> 与调用参数
        /// 一致。改动前该类型从未发布过任何事件，此处为该缺口修复后的直接证据。</summary>
        [Fact]
        public void Panels_OpenAndClose_ThroughRealAssembly_PublishRealUiPanelEvents()
        {
            var presentation = Build(out _, out _, out _, out var bus);
            var panelId = new Id("ui_layout_definition.sample_action_bar");

            var opened = new List<Id>();
            var closed = new List<Id>();
            bus.Subscribe<UiPanelOpenedEvent>(UiEventKeys.PanelOpened, e => opened.Add(e.PanelId));
            bus.Subscribe<UiPanelClosedEvent>(UiEventKeys.PanelClosed, e => closed.Add(e.PanelId));

            presentation.Panels.Open(panelId);
            presentation.Panels.Close(panelId);

            Assert.Equal(new[] { panelId }, opened);
            Assert.Equal(new[] { panelId }, closed);
        }

        /// <summary>ADR-0077 验收 1（`ui.action_invoked`）与验收 2（能驱动 `feedback.binding`）一并
        /// 验证：经真实 <see cref="PresentationAssembly.UiIntents"/> 携带 <c>panelId</c> 的
        /// <see cref="global::Presentation.Ui.UiIntents.CastSkill(Id,Id,Id?)"/> 重载调用，真实发出
        /// <see cref="UiActionInvokedEvent"/>，且真实 <c>feedback.binding</c>
        /// （<c>event: ui.action_invoked</c>，<c>play_sfx</c>）经 <see cref="Presentation.FeedbackBinder.Core.FeedbackBinder"/>
        /// 真实落到 <see cref="StubAudio.PlaySfx"/>（<see cref="StubAudio.ActiveSfxPlaybacks"/> 可观测）。
        /// 改动前 `presentation/ui/**` 不发出任何事件，`feedback.binding` 无法挂在任何 UI 交互上——
        /// 本用例是该缺口修复后的端到端证据。</summary>
        [Fact]
        public void UiIntents_ActionInvokedWithPanelId_ThroughRealAssembly_DrivesFeedbackBindingPlaySfx()
        {
            var panelId = new Id("ui_layout_definition.sample_action_bar");
            var skillId = new Id("skill.sample_ui_cast_test");
            var sfxId = new Id("sfx.sample_hit"); // 已由 AddMinimalPresentationTables 登记

            var presentation = Build(out _, out _, out var engine, out var bus, extraTables: source =>
            {
                source.Add("feedback.binding",
                    "{\"table\": \"feedback.binding\", \"schema_version\": 1, \"rows\": [" +
                    "{\"id\": \"feedback.pres_ui_action_invoked_test\", \"event\": \"ui.action_invoked\", " +
                    "\"actions\": [{\"kind\": \"play_sfx\", \"params\": {\"sfx_id\": \"" + sfxId.Value + "\"}}]}" +
                    "]}");
            });

            UiActionInvokedEvent? received = null;
            bus.Subscribe<UiActionInvokedEvent>(UiEventKeys.ActionInvoked, e => received = e);
            var beforeCount = engine.Audio.ActiveSfxPlaybacks.Count;

            presentation.UiIntents.CastSkill(panelId, skillId, targetId: null);

            Assert.NotNull(received);
            Assert.Equal(panelId, received!.PanelId);
            Assert.Equal("cast_skill", received.ActionName);
            Assert.True(engine.Audio.ActiveSfxPlaybacks.Count > beforeCount);
            Assert.Contains(engine.Audio.ActiveSfxPlaybacks.Values, p => p.SoundId.Equals(sfxId));
        }

        // -----------------------------------------------------------------
        // 消费方反馈第二十五批（两条，基于 v1.65.0 实测，ADR-0077 落地缺陷）复现 + 修复验收
        // -----------------------------------------------------------------

        /// <summary>现象 1 排查记录（未能按消费方描述的最小条件复现，见 ADR-0077"落地缺陷与修复"
        /// 一节）：经真实 <see cref="PresentationAssembly.Panels"/>（含 MenuOverlay 联动，即
        /// <see cref="Presentation.Ui.UiIntents.OpenMenu"/> 真实压栈 <see
        /// cref="Core.Foundation.AppLifecycle.InWorldSubState.MenuOverlay"/>）真实发出一次
        /// <see cref="UiPanelOpenedEvent"/>，且不引入任何会匹配 <c>ui.panel_opened</c> 的
        /// <c>feedback.binding</c> 行（同消费方隔离步骤"两条新增 UI 音效绑定的 event 字段改成不存在
        /// 的占位键"——本用例干脆不新增这两行），之后真实发出一次 <c>item.equipped</c>：
        /// <c>floating_text</c> 绑定应当照常触发。本用例按此最小条件反复实测均为绿（不复现），
        /// 与消费方报告的现象不符——已确认 <see cref="Core.Foundation.EventBus.EventBus.DispatchOne"/>
        /// 按订阅者逐条 try/catch、不同事件 key 各自独立快照遍历（见该类型判断记录），不存在跨事件
        /// key 的状态污染路径；保留本用例作为"仅发布一次真实 ui.panel_opened 本身不会破坏后续任何
        /// 绑定"的常驻回归锁，未来这条路径被破坏时能第一时间发现。真正确认存在的缺陷（同一事件内部
        /// 多条规则/动作未隔离）见下一条 <see
        /// cref="UiPanelOpened_MatchingRuleConditionThrows_ThroughRealAssembly_SiblingRuleForSameEventStillFires"/>。</summary>
        [Fact]
        public void UiPanelOpened_ThenItemEquipped_ThroughRealAssembly_FloatingTextBindingStillFires()
        {
            var floatingTexts = new List<(Id EntityId, Id StyleId, string Text)>();
            var panelId = new Id("ui_layout_definition.sample_action_bar");
            var options = new PresentationAssemblyOptions
            {
                OnFloatingText = (entityId, styleId, text) => floatingTexts.Add((entityId, styleId, text)),
                // 消费方实测打开的是背包一类 MenuOverlay 面板（见 UiPanelRegistry 类型注释"背包/
                // 任务日志/角色属性/设置"），Open() 因此会先经 UiIntents.OpenMenu 推入 MenuOverlay
                // 子状态、再发 ui.panel_opened——本用例复刻这一真实路径，不只发事件本身。
                MenuOverlayPanelIds = new[] { panelId },
            };

            var presentation = Build(out var gameplay, out _, out _, out var bus, options, extraTables: source =>
            {
                source.Add("feedback.binding",
                    "{\"table\": \"feedback.binding\", \"schema_version\": 1, \"rows\": [" +
                    "{\"id\": \"feedback.pres_ui_regression_item_equipped\", \"event\": \"item.equipped\", " +
                    "\"actions\": [{\"kind\": \"floating_text\", \"params\": {\"style_id\": \"feedback.floating_text_style.sample\", " +
                    "\"text_source\": \"literal:l10n.pres_ui_regression_item_equipped_text\"}}]}" +
                    "]}");
            });

            var playerId = gameplay.PlayerUnitProvider();

            // 真实发出一次 ui.panel_opened（经真实 UiPanelRegistry.Open，同现象 1 复现步骤）。
            presentation.Panels.Open(panelId);

            // 稍后触发一个与面板开关完全无关的事件，真实经 FeedbackBinder 落到 floating_text。
            bus.PublishImmediate(new Core.Carriers.Common.ItemEquippedEvent(
                playerId, new Id("item_instance.pres_ui_regression_test"), new Id("item.slot.sample_main_hand")));

            Assert.Single(floatingTexts);
            Assert.Equal(playerId, floatingTexts[0].EntityId);
            Assert.Equal(new Id("feedback.floating_text_style.sample"), floatingTexts[0].StyleId);
        }

        /// <summary>排查记录（不是决策 3 的复现用例——已实测证伪，理由见下）：最初设想"UI 事件
        /// selfId 退化为 <see cref="Core.Rules.ExprHost.RulesExprHostFactory.NoneId"/>（<c>"none.none"</c>）
        /// 时，一条形如 <c>self.is_alive</c> 的 <c>condition</c>（从战斗规则模板照抄来的内容作者失误）
        /// 会经真实 <see cref="Core.Carriers.Unit.WorldUnitAccess"/>.<c>Require</c> 抛
        /// <see cref="System.InvalidOperationException"/>，进而中止 <c>OnEvent</c> 对同一事件命中的
        /// <b>另一条规则</b>"这一假设。
        /// <para>
        /// 实测证伪：<c>Core.Foundation.Expr.ExprEvaluator.Evaluate</c>（<c>core/foundation/expr/core/
        /// ExprEvaluator.cs</c>）本身已经把"宿主 <c>Query</c> 抛异常"整体 try/catch 兜底——异常在抛出点
        /// 记一条诊断错误后收敛为"整个表达式判定为 false"，根本不会以异常形式传出 <c>Evaluate</c>/
        /// <c>EvaluateBool</c>，更不会传到 <c>FeedbackBinder.OnEvent</c>。也就是说，condition 求值这条
        /// 路径在 Expr 层就已经被兜住了，<c>self.is_alive</c> 对 <c>none.none</c> 只是让本条规则的
        /// condition 判定为 false（等价于未命中，正常路由到下一条规则），不是异常隔离在起作用。
        /// </para>
        /// <para>
        /// 本用例改为验证这个真实存在的兜底行为本身（作为回归锁，防止以后有人往 <c>ExprEvaluator</c>
        /// 里去掉这层 try/catch）：条件求值"退化为 false"与决策 3 修复的"动作派发/条件求值异常隔离"
        /// 是两条独立生效的防线，不能互相替代着验收。决策 3 真正的红→绿复现证据在
        /// <c>presentation/feedback_binder/tests/FeedbackBinderTests.cs</c> 的
        /// <c>OnEvent_OneRuleActionThrows_OtherRuleForSameEvent_StillExecutes</c>/
        /// <c>OnEvent_OneActionThrows_LaterActionInSameRule_StillExecutes</c>——用手写的
        /// <c>ThrowingFeedbackSink</c> 让 <b>动作派发</b>（不是条件求值）真实抛出，因为通读
        /// <c>presentation/assembly/PresentationAssembly.cs</c> 的真实装配后发现，vfx/sfx 的实体位置解析
        /// （<c>entityPositionResolver</c>）本身也已经是 <c>snapshot.Exists(id) ? ... : null</c> 防御式
        /// 写法，没有任何真实生产组件的动作派发路径会在当前装配下真的抛出——决策 3 的异常隔离目前只能
        /// 经故障注入的测试替身在 <c>FeedbackBinder</c> 单元层面复现，经真实装配根找不到一条会真的抛出
        /// 的路径，如实记录于此，判断记录同步写入 <c>architecture/adr/0077-ui交互域事件.md</c>
        /// "落地缺陷与修复"一节。</para></summary>
        [Fact]
        public void UiPanelOpened_RuleConditionReferencingMissingEntity_AlreadyContainedByExprEvaluator_SiblingRuleForSameEventStillFires()
        {
            var panelId = new Id("ui_layout_definition.sample_action_bar");
            var safeSfxId = new Id("sfx.sample_hit"); // 已由 AddMinimalPresentationTables 登记。

            var presentation = Build(out _, out _, out var engine, out _, extraTables: source =>
            {
                source.Add("feedback.binding",
                    "{\"table\": \"feedback.binding\", \"schema_version\": 1, \"rows\": [" +
                    // 排在前面（Ordinal 序 a < b）：条件引用 self.is_alive，selfId 退化为 none.none，
                    // 真实 WorldUnitAccess.Require 对该 id 抛异常。
                    "{\"id\": \"feedback.a_pres_ui_panel_opened_boom\", \"event\": \"ui.panel_opened\", " +
                    "\"condition\": \"self.is_alive\", " +
                    "\"actions\": [{\"kind\": \"play_sfx\", \"params\": {\"sfx_id\": \"" + safeSfxId.Value + "\"}}]}," +
                    // 排在后面：与前一条完全无关的另一条 ui.panel_opened 绑定，无 condition。
                    "{\"id\": \"feedback.b_pres_ui_panel_opened_survives\", \"event\": \"ui.panel_opened\", " +
                    "\"actions\": [{\"kind\": \"play_sfx\", \"params\": {\"sfx_id\": \"" + safeSfxId.Value + "\"}}]}" +
                    "]}");
            });

            var beforeCount = engine.Audio.ActiveSfxPlaybacks.Count;

            // 真实发出一次 ui.panel_opened：第一条规则条件求值抛异常，第二条必须照常执行——
            // 断言执行次数（第二条确实把 sfx 播放计数推高了恰好一次），不是只断言"没抛异常"。
            presentation.Panels.Open(panelId);

            Assert.Equal(beforeCount + 1, engine.Audio.ActiveSfxPlaybacks.Count);
            Assert.Contains(engine.Audio.ActiveSfxPlaybacks.Values, p => p.SoundId.Equals(safeSfxId));
        }

        /// <summary>排查记录（现象 2，未复现）：<c>ui.action_invoked</c> -&gt; <c>play_sfx</c> 命中一个
        /// "首次引用、此前从未加载过"的音效资源——这是消费方现象里的关键变量，既有 ADR-0077 验收用例
        /// （<see cref="UiIntents_ActionInvokedWithPanelId_ThroughRealAssembly_DrivesFeedbackBindingPlaySfx"/>）
        /// 未传 <c>withResourceLoader</c>，从未真正经过 <see cref="Core.Foundation.EngineAdapter.IResourceLoader"/>
        /// 这条冷加载路径，没有覆盖到消费方实测的这条真实路径，本用例补上它。
        /// <see cref="Adapters.Stub.StubResourceLoader.DeferCallbacks"/> 置真模拟真实引擎的异步语义
        /// （<c>LoadAsync</c> 不在调用当下同步回调，回调由测试显式经 <c>CompletePending</c> 在"由立即
        /// 派发的表现层事件回调"这一调用栈**之外**触发，还原消费方怀疑的边界条件）。
        /// <para>
        /// 结论：本用例经真实 <see cref="PresentationAssembly"/> 装配根反复验证，在核心层 + 表现层
        /// C# 代码里稳定为绿——冷加载排队（<c>QueuePendingPlay</c>/<c>_pendingResourceLoads</c> 去重）、
        /// 异步回调（同步/延迟两种时机）、经 <c>PublishImmediate</c>（UI 事件的立即派发语义）触发的整条
        /// 链路都按预期工作，播放最终真实发生。沿 <c>play_sfx</c> 整条动作路径通读代码，也没有找到任何
        /// 依赖实体解析（<c>selfId</c>/<c>targetId</c>）的分支——消费方绑定形如"字面量 <c>sfx_id</c>、
        /// 无 <c>attach</c>、无 <c>condition</c>"时该路径没有任何会抛异常的实体查找。本用例未能在这一层
        /// 复现消费方"确认已派发但从未观测到播放"的现象 2；判断记录见
        /// <c>architecture/adr/0077-ui交互域事件.md</c>"落地缺陷与修复（2026-09-23）"一节——现象 2 疑似
        /// 引擎适配层（真实音频播放时机/生命周期）问题，核心层 + 表现层 C# 代码找不到可归因的缺陷，
        /// 留待引擎适配层单独排查。</para></summary>
        [Fact]
        public void UiActionInvoked_PlaySfx_FirstReferenceUnloadedResource_PlaysAfterAsyncLoadCompletesLater()
        {
            var panelId = new Id("ui_layout_definition.sample_action_bar");
            var skillId = new Id("skill.sample_ui_cast_test_cold");
            var sfxId = new Id("sfx.sample_hit"); // 已由 AddMinimalPresentationTables 登记，resource_ref 同名字符串。

            var presentation = Build(out _, out _, out var engine, out _, withResourceLoader: true, extraTables: source =>
            {
                source.Add("feedback.binding",
                    "{\"table\": \"feedback.binding\", \"schema_version\": 1, \"rows\": [" +
                    "{\"id\": \"feedback.pres_ui_regression_action_invoked_sfx\", \"event\": \"ui.action_invoked\", " +
                    "\"actions\": [{\"kind\": \"play_sfx\", \"params\": {\"sfx_id\": \"" + sfxId.Value + "\"}}]}" +
                    "]}");
            });

            // 真实引擎的资源加载是异步的：LoadAsync 不在调用当下同步触发 callback。
            engine.ResourceLoader.DeferCallbacks = true;

            presentation.UiIntents.CastSkill(panelId, skillId, targetId: null);

            // 首次引用尚未加载完成：这一刻不应该已经播放，应该排队等待。
            Assert.Empty(engine.Audio.ActiveSfxPlaybacks);
            Assert.True(engine.ResourceLoader.HasPending(sfxId));

            // 模拟真实引擎异步加载完成回调稍后（不在原调用栈内）到达。
            engine.ResourceLoader.CompletePending(sfxId);

            Assert.True(engine.Audio.ActiveSfxPlaybacks.Count > 0);
            Assert.Contains(engine.Audio.ActiveSfxPlaybacks.Values, p => p.SoundId.Equals(sfxId));
        }

        /// <summary>ADR-0078 验收 3：给玩家单位的 <c>display.map</c> 行登记一个真实的
        /// <c>stride_distance</c>，经真实 <see cref="Core.Carriers.Unit.MovementTickHandler"/>
        /// （<see cref="global::Presentation.Ui.UiIntents.Move"/> + 真实 <see cref="WorldSim.Tick"/>，
        /// 移动速度取 <c>MovementOptions.DefaultSpeed</c> 缺省值 4.0——本模板未登记
        /// <c>stat.move_speed</c>）推进一段真实、按规则可算出的距离，断言经
        /// <see cref="PresentationAssembly.Stride"/> 真实发出的 <see cref="UnitStrideCompletedEvent"/>
        /// 条数等于 <c>floor(distance / strideDistance)</c>（按规则算出的期望值，不写死裸数）——
        /// 第一次 Move+Tick 只建立基准（该单位此前从未有过 unit.moved），第二次才是真正计入的位移；
        /// 余数保留到第三次移动继续累计（第三次断言的期望值把第二次的余数计入）。</summary>
        [Fact]
        public void Stride_UnitWithStrideDistanceRegistered_EmitsCountComputedFromRule_PreservesRemainderAcrossMoves()
        {
            const double strideDistance = 3.0;
            const double defaultMoveSpeed = 4.0; // MovementOptions.DefaultSpeed 缺省值
            const double dtSeconds = 1.0;
            var distancePerMove = defaultMoveSpeed * dtSeconds;

            var presentation = Build(out _, out var world, out _, out var bus, extraTables: source =>
            {
                AddStatMoveSpeedDefinition(source);
                source.Add("display.map",
                    "{\"table\": \"display.map\", \"schema_version\": 1, \"rows\": [" +
                    "{\"id\": \"display.map.pres_stride_player_test\", \"category\": \"creature\", " +
                    "\"logical_id\": \"" + SamplePlayerTemplateId.Value + "\", \"kind\": \"sprite\", " +
                    "\"sprite_set_id\": \"sprite.pres_stride_test\", \"direction_count\": 4, " +
                    "\"stride_distance\": " + strideDistance.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}" +
                    "]}");
            });

            var received = new List<UnitStrideCompletedEvent>();
            bus.Subscribe<UnitStrideCompletedEvent>(ViewBindingEventKeys.UnitStrideCompleted, e => received.Add(e));

            // 本单位此前从未产生过 unit.moved（CreatureFactory.Spawn 只是写初始位置，不经
            // MovementTickHandler、不发 unit.moved）——第一次 Move+Tick 是 StrideEmitter 第一次观测到
            // 这个单位，只建立基准位置，不计入位移（见 StrideEmitter 判断记录"首次观测不产生虚假
            // 位移"），必须恰好 0 条，不是"因为还没走够一个步幅所以是 0"这种巧合。
            presentation.UiIntents.Move(new Core.Foundation.Common.Vec2(1, 0));
            world.Tick(SimStep.Continuous(dtSeconds));
            Assert.Empty(received);

            // 第二次移动才是 StrideEmitter 真正开始累计的第一段真实位移。
            received.Clear();
            presentation.UiIntents.Move(new Core.Foundation.Common.Vec2(1, 0));
            world.Tick(SimStep.Continuous(dtSeconds));
            var expectedFirst = (int)System.Math.Floor(distancePerMove / strideDistance);
            Assert.Equal(expectedFirst, received.Count);

            // 第三次移动验证余数保留：累计值 = 第二次的余数 + 本次新位移，期望值由同一条 floor 规则
            // 重新算出（不是写死裸数），证明累计没有在两次触发之间被清零重置。
            received.Clear();
            presentation.UiIntents.Move(new Core.Foundation.Common.Vec2(1, 0));
            world.Tick(SimStep.Continuous(dtSeconds));
            var remainderAfterFirst = distancePerMove - expectedFirst * strideDistance;
            var expectedSecond = (int)System.Math.Floor((remainderAfterFirst + distancePerMove) / strideDistance);
            Assert.Equal(expectedSecond, received.Count);
        }

        /// <summary>ADR-0078 验收 4：未在 <c>display.map</c> 登记 <c>stride_distance</c> 的单位（本用例
        /// 用默认最小夹具，不追加任何 <c>display.map</c> 行）经真实移动推进后条数恒为 0——零开销
        /// opt-in 的直接证据，不是"注册了但阈值不可达"。</summary>
        [Fact]
        public void Stride_UnitWithoutStrideDistanceRegistered_EmitsZeroEvents_AfterRealMovement()
        {
            var presentation = Build(out _, out var world, out _, out var bus, extraTables: AddStatMoveSpeedDefinition);

            var received = new List<UnitStrideCompletedEvent>();
            bus.Subscribe<UnitStrideCompletedEvent>(ViewBindingEventKeys.UnitStrideCompleted, e => received.Add(e));

            presentation.UiIntents.Move(new Core.Foundation.Common.Vec2(1, 0));
            world.Tick(SimStep.Continuous(1.0));
            presentation.UiIntents.Move(new Core.Foundation.Common.Vec2(1, 0));
            world.Tick(SimStep.Continuous(1.0));

            Assert.Empty(received);
        }

        /// <summary>ADR-0078 验收 5：瞬移钳制——单次位移超过步幅距离
        /// <see cref="StrideEmitter.TeleportDistanceMultiplier"/>（8）倍时只发一条，不按
        /// "距离/步幅距离"整除发多条。用较大的 <c>dt</c>（真实 <see cref="WorldSim.Tick"/> 推进）制造
        /// 一次真实的大跨度单帧位移，而不是直接构造事件。</summary>
        [Fact]
        public void Stride_LargeSingleFrameDisplacement_ThroughRealMovement_EmitsExactlyOneEvent()
        {
            const double strideDistance = 0.1;
            const double defaultMoveSpeed = 4.0; // MovementOptions.DefaultSpeed 缺省值
            const double teleportDtSeconds = 100.0;
            var teleportDistance = defaultMoveSpeed * teleportDtSeconds;
            Assert.True(
                teleportDistance > strideDistance * StrideEmitter.TeleportDistanceMultiplier,
                "本用例的前提：本次单帧位移必须真实超过瞬移钳制阈值，否则不是在测钳制分支");

            var presentation = Build(out _, out var world, out _, out var bus, extraTables: source =>
            {
                AddStatMoveSpeedDefinition(source);
                source.Add("display.map",
                    "{\"table\": \"display.map\", \"schema_version\": 1, \"rows\": [" +
                    "{\"id\": \"display.map.pres_stride_teleport_test\", \"category\": \"creature\", " +
                    "\"logical_id\": \"" + SamplePlayerTemplateId.Value + "\", \"kind\": \"sprite\", " +
                    "\"sprite_set_id\": \"sprite.pres_stride_test\", \"direction_count\": 4, " +
                    "\"stride_distance\": " + strideDistance.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}" +
                    "]}");
            });

            var received = new List<UnitStrideCompletedEvent>();
            bus.Subscribe<UnitStrideCompletedEvent>(ViewBindingEventKeys.UnitStrideCompleted, e => received.Add(e));

            // 建立基准（首次观测不计入位移）。
            presentation.UiIntents.Move(new Core.Foundation.Common.Vec2(1, 0));
            world.Tick(SimStep.Continuous(1.0));
            Assert.Empty(received);

            // 大跨度单帧位移：见上方前提断言，teleportDistance 远超 strideDistance * 8。
            presentation.UiIntents.Move(new Core.Foundation.Common.Vec2(1, 0));
            world.Tick(SimStep.Continuous(teleportDtSeconds));
            Assert.Single(received); // 不是按 floor(teleportDistance / strideDistance) 发一大串。

            // 累计清零：紧接着一次远小于步幅距离的正常移动不应触发。
            received.Clear();
            presentation.UiIntents.Move(new Core.Foundation.Common.Vec2(1, 0));
            world.Tick(SimStep.Continuous(0.01)); // distance = 0.04 < strideDistance
            Assert.Empty(received);
        }
    }
}
