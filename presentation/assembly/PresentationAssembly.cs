using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Core.Foundation.Localization;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SceneRouter;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Rules.Common;
using Presentation.Camera;
using Presentation.Camera.Schema;
using Presentation.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.FeedbackBinder.Core;
using Presentation.Render;
using Presentation.Shell;
using Presentation.Ui;
using Presentation.ViewBinding;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;

// 判断记录：`Presentation.FeedbackBinder` 既是命名空间（`Presentation.FeedbackBinder.Core`/
// `.Contracts`）又是该命名空间下的类型名 `FeedbackBinder`（`Presentation.FeedbackBinder.Core.FeedbackBinder`），
// 裸写 "FeedbackBinder" 在本文件（namespace Presentation.Assembly，与 Presentation.FeedbackBinder
// 属同一父命名空间 Presentation 下的平级子命名空间）里会被编译器优先解析成命名空间本身而报
// CS0118，惯例同 core/gameplay/assembly/GameplayAssembly.cs 顶部对 WorldState 同名问题的处理，
// 用别名区分。
using FeedbackBinderCore = Presentation.FeedbackBinder.Core.FeedbackBinder;

namespace Presentation.Assembly
{
    /// <summary>
    /// 可选的表现层组装知会点（见 <see cref="PresentationAssembly"/> 类型注释"注入点清单"）：具体
    /// 数值/回调由具体游戏在接入阶段按自己的内容与美术资源填写；本框架不代为拍板任何具体游戏内容，
    /// 未提供时按本类型给出的默认值退化，退化行为均在各字段注释与
    /// <c>presentation/assembly/README.md</c>"判断记录"一节说明。
    /// </summary>
    public sealed class PresentationAssemblyOptions
    {
        /// <summary>当前无目标时的解析结果；供 <c>target.*</c> UI 路径查询使用（见
        /// <c>TargetPathProvider</c>）。默认恒为 <c>null</c>（"当前无目标"）。</summary>
        public Func<Id?> TargetResolver { get; set; } = () => null;

        /// <summary>动作条槽位数量兜底值：<c>ui_layout_definition</c> 数据里若没有
        /// <c>panel: action_bar</c> 的行，或该行未声明 <c>slots</c>，按本值退化。默认 8。</summary>
        public int ActionBarSlotCountFallback { get; set; } = 8;

        /// <summary>Hud 展示的资源类型（见 <c>HudViewModel</c> 构造参数 <c>powerTypes</c>）。默认
        /// 只含 <see cref="Core.Rules.Common.WellKnownPowers.Health"/>（与
        /// <c>Core.Carriers.Creature.CreatureOptions.DefaultPowerTypes</c> 同一默认惯例）。</summary>
        public IReadOnlyList<Id> HudPowerTypes { get; set; } = new[] { WellKnownPowers.Health };

        /// <summary>角色属性面板展示的属性清单（<c>statId</c>/文本键对），架构未拍板具体属性集合
        /// （游戏内容），默认空列表。</summary>
        public IReadOnlyList<(Id StatId, Id NameKey)> CharacterStatConfig { get; set; } = Array.Empty<(Id, Id)>();

        /// <summary>背包面板展示的装备槽位 id 清单，架构未定义全局槽位登记表（见
        /// <c>presentation/ui/README.md</c>），默认空列表。</summary>
        public IReadOnlyList<Id> EquipmentSlotIds { get; set; } = Array.Empty<Id>();

        /// <summary>暂停菜单选项清单（游戏内容），默认空列表。</summary>
        public IReadOnlyList<PauseMenuOption> PauseMenuOptions { get; set; } = Array.Empty<PauseMenuOption>();

        /// <summary>设置面板展示的按键动作名清单，默认取 <see cref="IInputMapHost"/> 当前已声明的
        /// 动作（构造期通常为空，见 <c>presentation/ui/README.md</c>）。</summary>
        public IReadOnlyList<string>? SettingsActionNames { get; set; }

        /// <summary>飘字动作的最终落地回调（见 09 第 6.1 节 <c>FloatingText</c>）：具体飘字 UI 控件
        /// 池不属于本框架任何一个 L5 模块的契约范围（见 <c>feedback_binder/README.md</c>），默认
        /// 空实现（不渲染，只是不阻断装配）。</summary>
        public Action<Id, Id, string>? OnFloatingText { get; set; }

        /// <summary>顿帧动作的最终落地回调（见 09 第 6.1 节 <c>Freeze</c>）：顿帧怎么影响 tick 节奏
        /// 不属于表现层契约范围。默认空实现。</summary>
        public Action<double>? OnFreeze { get; set; }

        /// <summary>闪白动作的最终落地回调（见 09 第 6.1 节 <c>Flash</c>）：闪白具体材质参数怎么应用
        /// 不属于 <c>vfx_sfx</c>/<c>feedback_binder</c> 契约范围。默认空实现。</summary>
        public Action<Id, Id>? OnFlash { get; set; }

        /// <summary>构造完成后是否立即用 <c>camera_profile</c> 表第一条记录 Configure 镜头并
        /// Follow 玩家单位（见 <see cref="PresentationAssembly"/>"判断记录"）。默认 true；数据集
        /// 里没有任何 <c>camera_profile</c> 行时自动跳过，不抛异常。</summary>
        public bool AutoConfigureCameraFromFirstProfile { get; set; } = true;

        /// <summary>缺口 11 恢复：Shell 读档后地图 id 解析器降级为可选覆盖（见
        /// <c>ShellHost.LoadGame</c> 判断记录）——<see cref="ShellHost"/> 现优先用
        /// <see cref="Core.Foundation.SaveSystem.LoadResult.CurrentMapId"/>（G1 补的字段），只有该
        /// 字段为 null 时才会调用本委托；默认 null（不覆盖，<see cref="LoadResult.CurrentMapId"/>
        /// 为 null 时直接跳过场景切换，见 <c>ShellHost.LoadGame</c>）。</summary>
        public LoadedMapIdResolver? LoadedMapIdResolver { get; set; }

        /// <summary>Shell"新游戏"意图的实际落地（见 <c>NewGameStarter</c>）：如何创建一局新游戏的
        /// 起始状态是具体游戏的事，框架不代为决定。默认在真正被调用时抛
        /// <see cref="NotSupportedException"/>（构造期不调用，不影响"构造成功"验收）。</summary>
        public NewGameStarter? NewGameStarter { get; set; }

        /// <summary>存档摘要时间戳来源（<c>ShellHost</c> 构造参数 <c>timestampProvider</c>）。默认
        /// <c>DateTime.UtcNow</c> 的 ISO-8601 表示。</summary>
        public Func<string>? TimestampProvider { get; set; }

        public ViewBinderOptions? ViewBinderOptions { get; set; }

        /// <summary>缺口 8（方向索引重映射策略）：见 <see cref="Presentation.Render.RenderOptions.DirectionIndexRemap"/>
        /// 字段注释；默认 null（<c>RenderConventionHost</c> 用 <c>RenderOptions</c> 默认值构造，恒等映射）。</summary>
        public Presentation.Render.RenderOptions? RenderOptions { get; set; }

        public VfxOptions? VfxOptions { get; set; }
        public SfxOptions? SfxOptions { get; set; }
        public CameraHostOptions? CameraHostOptions { get; set; }
        public FeedbackOptions? FeedbackOptions { get; set; }
    }

    /// <summary>
    /// L5（<c>presentation</c> 七模块：<c>common</c>/<c>view_binding</c>/<c>render</c>/<c>camera</c>/
    /// <c>vfx_sfx</c>/<c>feedback_binder</c>/<c>ui</c>/<c>shell</c>）在 <see cref="GameplayAssembly"/>
    /// （L0～L4）之上的组装根（阶段 4 收敛 B，<see cref="GameplayAssembly"/> 在 L5 层的延续）。装配
    /// 顺序、注入点清单、判断记录见 <c>presentation/assembly/README.md</c>。
    /// <para>
    /// 表现层铁律（09 第 1 节）在本装配根的落实：本类型只读 <paramref name="gameplay"/>/
    /// <paramref name="world"/> 暴露的只读查询与事件订阅（P1/P2），全部用户输入经
    /// <see cref="UiIntents"/> 转成 <c>IWorldSim.SubmitIntent</c> 或窄契约调用（P3），绘制/播放只经
    /// 构造参数传入的 L-1 引擎适配层接口完成（P4）——本类型自身不直接调用任何具体引擎 API，也不向
    /// <see cref="GameplayAssembly"/>/<c>WorldSim</c> 写任何状态。
    /// </para>
    /// </summary>
    public sealed class PresentationAssembly : IDisposable
    {
        private static readonly Id UnknownMapPlaceholder = new Id("world.unknown");
        private static readonly Id EmptyShellMenuId = new Id("shell_menu_definition.none");

        public GameplayAssembly Gameplay { get; }

        public IDisplayInfoRegistry DisplayInfo { get; }

        public ViewBinder ViewBinder { get; }

        public IRenderConventionHost Render { get; }

        public CameraHost Camera { get; }

        public IVfxPlayer Vfx { get; }

        public ISfxPlayer Sfx { get; }

        public IWeaponStyleResolver WeaponStyle { get; }

        /// <summary>缺口 12：分层音量宿主，见 <see cref="Presentation.VfxSfx.Contracts.IAudioLayerVolumeHost"/>。</summary>
        public Presentation.VfxSfx.Contracts.IAudioLayerVolumeHost AudioVolume { get; }

        public DisplayInfoResolver VfxSfxDisplayInfoResolver { get; }

        public FeedbackBinderCore Feedback { get; }

        public IReadOnlyDictionary<Id, FloatingTextStyleDef> FloatingTextStyles { get; }

        public IInputMapHost InputMap { get; }

        public IL10nHost L10n { get; }

        public IUiDataSource UiData { get; }

        public UiIntents UiIntents { get; }

        public HudViewModel Hud { get; }

        public ActionBarViewModel ActionBar { get; }

        public InventoryViewModel Inventory { get; }

        public QuestLogViewModel QuestLog { get; }

        public DialogViewModel DialogView { get; }

        public SkillBookViewModel SkillBook { get; }

        public CharacterStatsViewModel CharacterStats { get; }

        public SettingsViewModel Settings { get; }

        public SaveSlotsViewModel SaveSlots { get; }

        public PauseMenuViewModel PauseMenu { get; }

        /// <summary>缺口 16（ISaveSystem 归属调整）：直接转发 <see cref="GameplayAssembly.SaveSystem"/>
        /// ——本装配根不再自行构造一份，避免与 <c>GameplayAssembly</c> 各持一份互不相知的
        /// <see cref="ISaveSystem"/>（此前的隐患：<c>RegisterPersistables</c> 需要调用方另行传入
        /// "同一实例"，容易被漏接）。</summary>
        public ISaveSystem SaveSystem { get; }

        public ISettingsStore SettingsStore { get; }

        public ShellHost Shell { get; }

        public ShellViewModel ShellViewModel { get; }

        private readonly Id _playerId;
        private bool _disposed;

        public PresentationAssembly(
            GameplayAssembly gameplay,
            IWorldSim world,
            IDataRegistryView registry,
            IEventBus bus,
            IRngHost rng,
            IViewFactory viewFactory,
            IRenderer2D renderer2D,
            ICamera camera,
            IAudio audio,
            IFileSystem fileSystem,
            ISceneRouter sceneRouter,
            PresentationAssemblyOptions? options = null,
            IRenderer3D? renderer3D = null)
        {
            Gameplay = gameplay ?? throw new ArgumentNullException(nameof(gameplay));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (viewFactory == null) throw new ArgumentNullException(nameof(viewFactory));
            if (renderer2D == null) throw new ArgumentNullException(nameof(renderer2D));
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (audio == null) throw new ArgumentNullException(nameof(audio));
            if (fileSystem == null) throw new ArgumentNullException(nameof(fileSystem));
            if (sceneRouter == null) throw new ArgumentNullException(nameof(sceneRouter));
            var opts = options ?? new PresentationAssemblyOptions();

            _playerId = gameplay.PlayerUnitProvider();

            // 缺口 12：SettingsStore 提前到最前面构造（原在第 4 步 ui 小节内），本装配根第 2 步
            // vfx_sfx 小节构造 AudioLayerVolumeHost 时就需要用到同一个 ISettingsStore 实例。
            SettingsStore = ResolveSettingsStore(fileSystem);

            // ---------------------------------------------------------
            // 1) view_binding + render + camera：只读 WorldSimSnapshot + DisplayInfoRegistry。
            // ---------------------------------------------------------
            var snapshot = new WorldSimSnapshot(world);
            DisplayInfo = new DisplayInfoRegistry(registry, bus);
            // 缺口 8：Render 先于 ViewBinder 构造，二者共享同一个 RenderConventionHost 实例，
            // ViewBinder 的 IAnchorQuery 镜像判定与本装配根对外暴露的 Render 属性用同一份
            // DirectionIndexRemap 配置，不会出现"锚点镜像"与"纸娃娃层镜像"各自看到不同重映射表。
            Render = new RenderConventionHost(opts.RenderOptions);
            ViewBinder = new ViewBinder(bus, viewFactory, snapshot, DisplayInfo, opts.ViewBinderOptions, renderConvention: Render);

            var followTarget = new SimSnapshotFollowTarget(snapshot);
            Camera = new CameraHost(camera, followTarget, bus, opts.CameraHostOptions);
            if (opts.AutoConfigureCameraFromFirstProfile)
            {
                var firstProfileRecord = registry.GetAll(CameraSchemas.Profile.Name).FirstOrDefault();
                if (firstProfileRecord != null)
                {
                    Camera.Configure(Presentation.Camera.CameraProfile.FromRecord(firstProfileRecord));
                    Camera.Follow(_playerId);
                }
            }

            // ---------------------------------------------------------
            // 2) vfx_sfx：从 vfx.def/sfx.def/display.weapon_style 建目录，构造播放器。entityPosition
            //    用只读快照兜底（09 第 5.3 节判断记录"缺省用实体位置"）；AnchorResolver 接
            //    ViewBinder（缺口 6，见 IAnchorQuery 类型注释"谁实现本接口"判断记录）。
            // ---------------------------------------------------------
            var vfxCatalog = registry.GetAll(Presentation.VfxSfx.Schema.VfxSfxSchemas.Vfx.Name)
                .Select(VfxDef.FromRecord).ToDictionary(d => d.Id);
            var sfxCatalog = registry.GetAll(Presentation.VfxSfx.Schema.VfxSfxSchemas.Sfx.Name)
                .Select(SfxDef.FromRecord).ToDictionary(d => d.Id);
            var weaponStyleCatalog = registry.GetAll(Presentation.VfxSfx.Schema.VfxSfxSchemas.WeaponStyle.Name)
                .Select(WeaponStyleDef.FromRecord).ToDictionary(d => d.Id);

            EntityPositionResolver entityPositionResolver =
                id => snapshot.Exists(id) ? (Vec2?)snapshot.GetPosition(id) : null;

            // 缺口 13：renderer3D 可选（默认 null，纯 sprite 型游戏不接 IRenderer3D 也能正常装配）；
            // modelHandleResolver 经 ViewBinder 持有的 View 绑定表解析（谁持有 View 谁提供，同缺口 6
            // IAnchorQuery 判断记录），未接 renderer3D 时该委托即使传入也不会被 VfxPlayer 使用。
            ModelHandleResolver modelHandleResolver = entityId =>
                ViewBinder.TryGetView(entityId, out var view) && view is IModelHandleProvider provider
                    ? provider.TryGetModelHandle()
                    : null;

            var vfxPlayer = new VfxPlayer(
                renderer2D, camera, vfxCatalog, opts.VfxOptions, anchorResolver: ViewBinder.GetAnchorWorldPosition,
                entityPositionResolver: entityPositionResolver,
                renderer3D: renderer3D, modelHandleResolver: modelHandleResolver);
            var sfxPlayer = new SfxPlayer(audio, rng, sfxCatalog, opts.SfxOptions);
            Vfx = vfxPlayer;
            Sfx = sfxPlayer;
            WeaponStyle = new WeaponStyleResolver(weaponStyleCatalog);
            VfxSfxDisplayInfoResolver = new DisplayInfoResolver(DisplayInfo);

            // 缺口 12：分层音量宿主——层清单 = sfx.def.layer 去重（见 AudioLayerVolumeHost 判断记录，
            // 顺序取数据出现顺序，同下方 settingsLayers 此前的去重口味一致，改用同一份计算结果）。
            var sfxLayers = sfxCatalog.Values
                .Select(d => d.Layer).Distinct(StringComparer.Ordinal).OrderBy(l => l, StringComparer.Ordinal).ToList();
            AudioVolume = new AudioLayerVolumeHost(sfxLayers, sfxPlayer, audio, SettingsStore);

            // ---------------------------------------------------------
            // 3) feedback_binder：feedback.binding/feedback.floating_text_style 建规则集；
            //    CompositeFeedbackSink 把 play_vfx/play_sfx 接到第 2 步的播放器，其余四种（飘字/
            //    顿帧/震屏/闪白）转给 opts 注入的回调（震屏默认接 CameraHost.Shake，其余三种默认
            //    空实现，见 PresentationAssemblyOptions 字段注释）。
            // ---------------------------------------------------------
            var feedbackRules = registry.GetAll(Presentation.FeedbackBinder.Schema.FeedbackSchemas.Binding.Name)
                .Select(r => FeedbackRule.FromRecord(r, PresentationSchemaCatalog.FullExprSchema)).ToList();
            FloatingTextStyles = registry.GetAll(Presentation.FeedbackBinder.Schema.FeedbackSchemas.FloatingTextStyle.Name)
                .Select(FloatingTextStyleDef.FromRecord).ToDictionary(d => d.Id);

            var feedbackSink = new CompositeFeedbackSink(
                vfxPlayer, sfxPlayer,
                onFloatingText: opts.OnFloatingText ?? ((_, __, ___) => { }),
                onFreeze: opts.OnFreeze ?? (_ => { }),
                onShakeCamera: profileId => Camera.Shake(profileId),
                onFlash: opts.OnFlash ?? ((_, __) => { }),
                entityPositionResolver: entityPositionResolver);

            Feedback = new FeedbackBinderCore(
                bus, gameplay.ExprHostFactory, feedbackRules, feedbackSink,
                displayInfoResolver: VfxSfxDisplayInfoResolver, entityLogicalIdResolver: null,
                unitAccess: gameplay.Carriers.Units, options: opts.FeedbackOptions);

            // ---------------------------------------------------------
            // 4) ui：InputMapHost/L10nHost 由本装配根自行构造（两者均为 L0 基础设施，不属于
            //    GameplayAssembly 的十个 L4 宿主，见 README 判断记录）；UiDataSource 接三个路径
            //    provider；十个视图模型逐一构造。
            // ---------------------------------------------------------
            InputMap = new Core.Foundation.InputMap.InputMapHost(bus);
            L10n = new L10nHost(registry, bus);

            var skillBookQuery = new SkillHostSkillBookQuery(gameplay.Carriers.Rules.Skill);
            var providers = new IUiPathProvider[]
            {
                new PlayerPathProvider(
                    _playerId, gameplay.Carriers.Rules.Stats, gameplay.Carriers.Rules.Powers,
                    gameplay.Carriers.Rules.Progression, gameplay.Carriers.Inventory, gameplay.Carriers.Equipment,
                    gameplay.Quest, gameplay.Economy, skillBookQuery),
                new TargetPathProvider(opts.TargetResolver, gameplay.Carriers.Rules.Stats, gameplay.Carriers.Rules.Powers),
                new UnitPathProvider(gameplay.Carriers.Rules.Stats, gameplay.Carriers.Rules.Powers),
            };
            UiData = new UiDataSource(bus, providers);

            // 缺口 4：动作条槽位绑定改接 gameplay.Carriers.SkillBindings（G1 新增
            // ISkillBindingHost，见 ActionBarViewModel/UiIntents 类型注释判断记录），删除此前的
            // ActionBarSlotBindingResolver 注入委托选项。
            UiIntents = new UiIntents(
                _playerId, world, gameplay.Carriers.Equipment, gameplay.Quest, gameplay.Dialog, gameplay.Economy,
                InputMap, L10n, gameplay.AppState,
                audioVolume: AudioVolume, skillBindings: gameplay.Carriers.SkillBindings);

            var actionBarSlots = ResolveActionBarSlotCount(registry, opts.ActionBarSlotCountFallback);

            Hud = new HudViewModel(UiData, _playerId, opts.HudPowerTypes);
            ActionBar = new ActionBarViewModel(UiData, _playerId, actionBarSlots, gameplay.Carriers.SkillBindings);
            Inventory = new InventoryViewModel(UiData, opts.EquipmentSlotIds);
            QuestLog = new QuestLogViewModel(UiData, gameplay.Quest, _playerId);
            DialogView = new DialogViewModel(UiData, gameplay.Dialog, _playerId);
            SkillBook = new SkillBookViewModel(UiData, skillBookQuery, _playerId);
            CharacterStats = new CharacterStatsViewModel(UiData, L10n, _playerId, opts.CharacterStatConfig);
            SaveSystem = gameplay.SaveSystem;

            var settingsActionNames = opts.SettingsActionNames ?? Array.Empty<string>();
            Settings = new SettingsViewModel(UiData, L10n, InputMap, settingsActionNames, AudioVolume);

            SaveSlots = new SaveSlotsViewModel(UiData, SaveSystem);
            PauseMenu = new PauseMenuViewModel(UiData, gameplay.AppState, opts.PauseMenuOptions);

            // ---------------------------------------------------------
            // 5) shell：ShellMenuDefinition 取 shell_menu_definition 表第一条（数据集没有任何行时
            //    退化为一份空菜单，不阻断装配，见 EmptyShellMenuId）。
            // ---------------------------------------------------------
            var shellMenu = registry.GetAll(ShellSchemas.ShellMenuDefinitionTable.Name)
                .Select(ShellMenuDefinition.FromRecord).FirstOrDefault()
                ?? new ShellMenuDefinition(EmptyShellMenuId, Array.Empty<ShellMenuEntry>());

            var loadedMapIdResolver = opts.LoadedMapIdResolver ?? (() =>
            {
                var entity = world.GetEntity(gameplay.PlayerUnitProvider());
                return entity?.MapId ?? UnknownMapPlaceholder;
            });
            var newGameStarter = opts.NewGameStarter ?? ((slotId, difficultyId, archetypeId) =>
                throw new NotSupportedException(
                    "NewGameStarter 未注入：如何创建一局新游戏的起始状态是具体游戏的事，框架不提供默认实现，见 presentation/assembly/README.md"));
            var timestampProvider = opts.TimestampProvider ?? (() => DateTime.UtcNow.ToString("o"));

            Shell = new ShellHost(
                gameplay.AppState, sceneRouter, SaveSystem, SettingsStore, gameplay.Difficulty, InputMap, bus,
                newGameStarter, timestampProvider, loadedMapIdResolver: loadedMapIdResolver);
            ShellViewModel = new ShellViewModel(Shell, SaveSystem, bus, shellMenu);
        }

        private static int ResolveActionBarSlotCount(IDataRegistryView registry, int fallback)
        {
            foreach (var record in registry.GetAll(UiSchemas.UiLayoutDefinition.Name))
            {
                var layout = UiLayoutDefinition.FromRecord(record);
                if (layout.Panel == UiPanel.ActionBar && layout.Slots.HasValue)
                {
                    return layout.Slots.Value;
                }
            }
            return fallback;
        }

        private static ISettingsStore ResolveSettingsStore(IFileSystem fileSystem) =>
            new SettingsStore(fileSystem);

        /// <summary>
        /// 退订全部本装配根构造期建立的事件订阅（<see cref="FeedbackBinderCore"/>、<see cref="ShellHost"/>、
        /// <see cref="ViewBinder"/>、<see cref="Camera"/>（缺口 5：两者现均实现
        /// <see cref="IDisposable"/>）与十个 UI 视图模型 + <see cref="ShellViewModel"/>，均在
        /// <c>Dispose</c> 内部释放各自持有的 <see cref="Core.Foundation.Common.SubscriptionHandle"/>，
        /// 见各自类型源码）。幂等：多次调用只生效一次。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;

            ViewBinder.Dispose();
            Camera.Dispose();
            Feedback.Dispose();
            Shell.Dispose();
            ShellViewModel.Dispose();
            Hud.Dispose();
            ActionBar.Dispose();
            Inventory.Dispose();
            QuestLog.Dispose();
            DialogView.Dispose();
            SkillBook.Dispose();
            CharacterStats.Dispose();
            Settings.Dispose();
            SaveSlots.Dispose();
            PauseMenu.Dispose();
        }
    }
}
