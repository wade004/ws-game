#nullable enable
// AuditBlockersPlayModeTests：外部审核阻塞项 1/2/4 的 Unity PlayMode 收口验收（见
// architecture/落地计划/audit-20260907/followup-2026-09-07.md"外部审核阻塞项处理"一节）。
// 外部审核阻塞项 3（默认动画接线）单独在 UnityViewFactoryDefaultAnimationTests.cs 验收。
//
// 判断记录（不经完整 FrameworkResidentHost/ShellRoot 装配，直接构造 GameplayAssembly/
// PresentationAssembly/ShellHost）：同 DiscreteCombatTests.cs 顶部判断记录——本文件需要装配一个
// 真正的 death.reload_save 策略（生产 FrameworkResidentHost/GameFoundationBootstrap 都固定用
// combat.hit_table_config 数据驱动的默认 respawn_point，没有开关可以从外部切到 reload_save），
// 自建一份独立装配，双方互不干扰。
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Carriers.Common;
using Core.Carriers.Unit;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Core.Foundation.Rng;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Gameplay.Death;
using Core.Rules.Common;
using NUnit.Framework;
using Presentation.Assembly;
using Presentation.Render;
using Presentation.Shell;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class AuditBlockersPlayModeTests : PlayModeTestBase
    {
        private const string PlayerFactionId = "fac.player";
        private const string PlayerClassId = "arch.class.sample_a";
        private const string PlayerTemplateId = "creature.sample_hero";
        private const string MapAId = "world.sample_field";

        private sealed class Fixture
        {
            public GameplayAssembly Gameplay = null!;
            public PresentationAssembly Presentation = null!;
            public UnityViewFactory ViewFactory = null!;
            public IWorldSim World = null!;
            public IEventBus Bus = null!;
            public IDataRegistry Registry = null!;
            public Id PlayerId;
            public PlayerUnit Player = null!;
            public Core.Foundation.SceneRouter.SceneRouter SceneRouter = null!;
            public ShellHost Shell = null!;
        }

        private Fixture? _fixture;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_fixture != null)
            {
                _fixture.Shell.Dispose();
                _fixture.World.ClearAll();
                _fixture.Bus.DispatchPending();
                _fixture.ViewFactory.DestroyAllCreatedViews();
                _fixture.Presentation.Dispose();
                _fixture = null;
            }

            yield return null;
        }

        /// <summary><paramref name="deathPolicy"/> 未指定时用示例数据 combat.hit_table_config 的
        /// 既有默认值（respawn_point，见 core/rules/combat/contracts/CombatOptions.cs）。</summary>
        private Fixture BuildFixture(RespawnPolicy? deathPolicy = null, Id? autosaveSlot = null)
        {
            var host = UnityEngineHost.Ensure();

            var definitions = EventKeys.All.Select(k => new EventDefinition(k, k.Domain, Array.Empty<string>())).ToList();
            var catalog = EventCatalog.FromDefinitions(definitions);
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = false });

            var repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            var contentFs = new UnityFileSystem(readOnlyContentMode: true, contentRoot: repoRoot);
            var frameworkSource = new FileSystemDataSource(contentFs, "data/_framework");
            var sampleSource = new FileSystemDataSource(contentFs, "data/_sample");

            var options = PresentationSchemaCatalog.CreateOptions();
            options.FailOnUnknownTable = false;
            var registry = new DataRegistry(sampleSource, bus, options);
            PresentationSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll(new IDataSource[] { frameworkSource, sampleSource });
            Assert.IsFalse(report.IsBlocking, "测试数据集应当能无阻断加载：" + string.Join("; ", report.Issues));

            var rng = new RngHost(20260907UL);
            var world = new WorldSim(bus);
            var playerId = new Id($"unit.audit_blockers_player_{UnityEngine.Random.Range(0, int.MaxValue)}");
            var factionId = new Id(PlayerFactionId);
            var classId = new Id(PlayerClassId);
            var mapAId = new Id(MapAId);

            var slotId = autosaveSlot ?? new Id("slot.audit_blockers_autosave");
            var saveSystem = new SaveSystem(host.FileSystem, new SaveSystemOptions(new Id("game.audit_blockers_test")), bus);

            var gameplay = new GameplayAssembly(
                bus, registry, rng, world, host.SpatialQuery, saveSystem,
                playerUnitProvider: () => playerId,
                playerFactionId: factionId,
                navigation: host.Navigation2D,
                deathPolicyOptions: deathPolicy.HasValue
                    ? new DeathPolicyOptions { Policy = deathPolicy.Value, AutosaveSlotId = slotId }
                    : null);

            var player = new PlayerUnit(playerId, mapAId, factionId, classId)
            {
                Position = Vec2.Zero,
                TemplateId = new Id(PlayerTemplateId),
            };
            world.AddEntity(player);
            gameplay.Carriers.Rules.RegisterUnit(playerId, classId, raceId: null, level: 3);
            gameplay.Economy.RegisterUnit(playerId);

            var viewFactoryDisplayInfo = new DisplayInfoRegistry(registry, bus);
            var viewFactory = new UnityViewFactory(host.Renderer2D, new RenderConventionHost(), viewFactoryDisplayInfo, host.ResourceLoader, bus: bus, dataRegistry: registry);
            var presentationRng = new RngHost(20260907UL ^ 0x9E3779B97F4A7C15UL);
            var sceneRouter = new Core.Foundation.SceneRouter.SceneRouter(
                registry, host.ResourceLoader, gameplay.AppState, world, gameplay.Hooks, bus,
                spatial: host.SpatialQuery, navigation: host.Navigation2D);
            gameplay.AttachSceneRouter(sceneRouter);

            var presentation = new PresentationAssembly(
                gameplay, world, registry, bus, presentationRng,
                viewFactory, host.Renderer2D, host.Camera, host.Audio, host.FileSystem, sceneRouter,
                resourceLoader: host.ResourceLoader);

            gameplay.RegisterPersistables(presentation.SaveSystem, player);

            // 同生产装配（FrameworkResidentHost/GameBootstrap.HandlePostLoad）的既有惯例：ClearAll
            // 之后玩家实体若已缺失则重新 AddEntity，再统一调用 EnterMap（内部已经接了
            // Loot.ReattachToWorld，见外部审核阻塞项 1 收口）。
            sceneRouter.RegisterPostLoadHook(mapId =>
            {
                if (world.GetEntity(playerId) == null)
                {
                    world.AddEntity(player);
                    bus.DispatchPending();
                }
                gameplay.EnterMap(mapId, playerId);
            });

            var settingsStore = new SettingsStore(host.FileSystem);
            var inputMap = new InputMapHost(bus);
            var shell = new ShellHost(
                gameplay.AppState, sceneRouter, presentation.SaveSystem, settingsStore, gameplay.Difficulty, inputMap, bus,
                newGameStarter: (slot, diff, arch) => mapAId,
                timestampProvider: () => "2026-09-07T00:00:00Z");

            return new Fixture
            {
                Gameplay = gameplay, Presentation = presentation, ViewFactory = viewFactory, World = world, Bus = bus,
                Registry = registry, PlayerId = playerId, Player = player, SceneRouter = sceneRouter, Shell = shell,
            };
        }

        /// <summary>驱动一帧：Shell.Update()（内部转发 ISceneRouter.Update()，同 ShellHost.Update 文档
        /// 注释）+ 事件派发，与 FrameworkResidentHost.OnFrameTick 同款最小驱动。</summary>
        private static IEnumerator PumpUntilInWorld(Fixture fx, int maxFrames = 600)
        {
            var guard = maxFrames;
            while (fx.Shell.Page != ShellPage.InWorld && guard-- > 0)
            {
                fx.Shell.Update();
                fx.Bus.DispatchPending();
                yield return null;
            }
            Assert.AreEqual(ShellPage.InWorld, fx.Shell.Page, "场景加载应当在有限帧数内完成并进入 InWorld");
        }

        // -----------------------------------------------------------------
        // 外部审核阻塞项 1：读档一致性——经真实 Presentation.Shell.ShellHost.LoadGame（不是
        // core 端到端测试里手工模拟的 ClearAll/ReattachToWorld 调用序列）验证"保存空掉落档 →
        // 产生掉落物 B → 读取旧档"后 B 确实从世界与 View 里消失。
        // -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator ShellHostLoadGame_LootSavedEmpty_ThenSpawned_ThenLoadOld_LootGoneFromWorldAndView()
        {
            var fx = BuildFixture();
            _fixture = fx;

            fx.Shell.Start();
            yield return null;

            var slotId = new Id("game.sample.slot_audit_loot");
            fx.SceneRouter.LoadScene(new Id(MapAId));
            yield return PumpUntilInWorld(fx);

            var saveEmpty = fx.Presentation.SaveSystem.Save(new SaveRequest(slotId, "2026-09-07T00:00:00Z"));
            Assert.IsTrue(saveEmpty.Success, "空掉落档存档应当成功：" + saveEmpty.Message);

            var lootId = fx.Gameplay.Loot.Drop(new Id(MapAId), new Vec2(2, 2), new[] { new Core.Carriers.Common.ItemStack(new Id("item.sample_token"), 1) });
            fx.Bus.DispatchPending();
            Assert.IsNotNull(fx.World.GetEntity(lootId), "掉落物 B 生成后应当存在于世界里");

            var loadResult = fx.Shell.LoadGame(slotId);
            Assert.IsTrue(loadResult.Status == LoadStatus.Loaded || loadResult.Status == LoadStatus.LoadedFromBackup, $"读档应当成功，实际：{loadResult.Status}");
            yield return PumpUntilInWorld(fx);
            yield return null;

            Assert.IsNull(fx.World.GetEntity(lootId), "读取不含掉落物的旧档后，B 不应该继续存在于 IWorldSim 里（外部审核阻塞项 1）");
            Assert.IsFalse(fx.Presentation.ViewBinder.TryGetView(lootId, out _), "B 的 View 也不应该继续存在");
        }

        // -----------------------------------------------------------------
        // 外部审核阻塞项 2：死亡回档完整流程——玩家真实死亡（生命值砍到 0）→ death.reload_save
        // 策略触发 → 回档后玩家存活、View 仍然存在（不是空视图/悬空引用）。
        // -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator PlayerDies_ReloadSave_PlayerRevivedAndViewExists()
        {
            var slotId = new Id("slot.audit_blockers_death_reload");
            var fx = BuildFixture(deathPolicy: RespawnPolicy.ReloadSave, autosaveSlot: slotId);
            _fixture = fx;

            fx.Shell.Start();
            yield return null;
            fx.SceneRouter.LoadScene(new Id(MapAId));
            yield return PumpUntilInWorld(fx);

            // 存档点时刻：玩家满血存活，作为"最近一次自动存档"。
            var saveResult = fx.Presentation.SaveSystem.Save(new SaveRequest(slotId, "2026-09-07T00:00:00Z"));
            Assert.IsTrue(saveResult.Success, "存档应当成功：" + saveResult.Message);

            // 真实死亡：生命值砍到 0、Alive=false（同 Core.Rules.Combat.Resolver 死亡结算），
            // 发布 unit.died——DeathPolicyHost（reload_save 策略）同步处理。
            var healthBefore = fx.Gameplay.Carriers.Rules.Powers.GetPower(fx.PlayerId, WellKnownPowers.Health);
            fx.Gameplay.Carriers.Rules.Powers.ModifyPower(fx.PlayerId, WellKnownPowers.Health, -healthBefore, fx.PlayerId);
            fx.Gameplay.Carriers.Units.SetAlive(fx.PlayerId, false);
            fx.Bus.PublishImmediate(new UnitDiedEvent(fx.PlayerId, null, new Id(MapAId), fx.Player.Position));

            // C12 复现与根治（architecture/落地计划/audit-7e63d66-20260907/code-review.md）：
            // unit.died 已经让表现层的动画状态机进入死亡终态锁（同真实生产接线，AnimStateMachine
            // 直接订阅 unit.died，见该类型判断记录）。
            var animStateMachine = fx.ViewFactory.AnimStateMachineForTests;
            Assert.IsNotNull(animStateMachine, "UnityViewFactory 应当持有全局单例 AnimStateMachine");
            Assert.IsTrue(animStateMachine!.IsTerminal(fx.PlayerId), "死亡后应当进入终态锁");

            // reload_save 可能触发场景切换（RestoreFromSlot），本用例是同地图重载，理应不切场景，
            // 但仍然驱动几帧让任何待处理的场景/事件收尾（含 DeathPolicyHost 补发的 unit.respawned
            // ——见该类型判断记录"Enqueue 而不是 PublishImmediate"，需要一次 DispatchPending 才
            // 真正可见）。
            for (var i = 0; i < 5; i++)
            {
                fx.Shell.Update();
                fx.Bus.DispatchPending();
                yield return null;
            }

            Assert.IsTrue(fx.Gameplay.Carriers.Units.IsAlive(fx.PlayerId), "reload_save 应当恢复玩家存活状态（外部审核阻塞项 2）");
            Assert.Greater(fx.Gameplay.Carriers.Rules.Powers.GetPower(fx.PlayerId, WellKnownPowers.Health), 0.0, "复活后生命值应当大于 0");

            var entity = fx.World.GetEntity(fx.PlayerId);
            Assert.IsNotNull(entity, "回档后玩家实体应当仍在 IWorldSim 里");
            Assert.IsTrue(
                fx.Presentation.ViewBinder.TryGetView(fx.PlayerId, out var view),
                "回档后玩家应当仍有对应的 View（不是空视图/悬空引用）");
            Assert.IsTrue(view!.IsAlive, "玩家 View 应当仍然存活");

            // C12 核心断言：同图读档成功后应当清理死亡终态锁，且后续 Move/Attack 类事件应当照常生效
            // ——不再被"死亡终态不接受回落"规则拒绝（此前 reload_save 成功分支不发 unit.respawned，
            // 玩家会一直卡在死亡动画姿态）。
            Assert.IsFalse(animStateMachine.IsTerminal(fx.PlayerId), "同图读档成功后应当清理死亡终态锁（C12）");

            fx.Bus.PublishImmediate(new Core.Carriers.Common.UnitStateChangedEvent(fx.PlayerId, "Idle", "Walk"));
            Assert.AreEqual(AnimState.Move, animStateMachine.GetState(fx.PlayerId), "同图读档后应当允许 Move（C12）");

            fx.Bus.PublishImmediate(new SkillCastStartEvent(fx.PlayerId, new Id("skill.sample_basic_attack"), castTime: 0));
            Assert.AreEqual(AnimState.Attack, animStateMachine.GetState(fx.PlayerId), "同图读档后应当允许 Attack（C12）");
        }

        // -----------------------------------------------------------------
        // 外部审核阻塞项 4：首次特效/音效加载边界——冷启动后第一次引用某个 vfx/sfx 资源，命中特效/
        // 音效应当在资源加载完成之后才播放（不是立即调用、也不是永远不播放）。
        // -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator VfxSfx_FirstReference_ColdStart_PlaysExactlyOnce_AfterResourceLoaded()
        {
            var host = UnityEngineHost.Ensure();

            // 用一个不与其它任何用例共享的 vfx/sfx def id，指向真实占位资源（见 data/_sample/vfx/
            // vfx.def.json、data/_sample/sfx/sfx.def.json 的 sample_hit_spark/sample_hit 两行），
            // 并显式 Unload 一次，确定性地把它重置回"从未加载过"的冷启动状态——不依赖"这是本批处理
            // 进程里第一次引用"这个无法保证的执行顺序假设（host.ResourceLoader 跨整批 -runTests
            // 进程存活，见 DiscreteCombatTests.cs 判断记录）。
            var vfxResourceId = new Id("vfx.sample_hit_spark");
            var sfxResourceId = new Id("sfx.sample_hit_v0");
            host.ResourceLoader.Unload(vfxResourceId);
            host.ResourceLoader.Unload(sfxResourceId);
            Assert.IsFalse(host.ResourceLoader.IsLoaded(vfxResourceId));
            Assert.IsFalse(host.ResourceLoader.IsLoaded(sfxResourceId));

            var vfxId = new Id("vfx.sample_hit_spark");
            var vfxCatalog = new System.Collections.Generic.Dictionary<Id, VfxDef>
            {
                [vfxId] = new VfxDef(vfxId, "impact", VfxAttachMode.World, 0.4, vfxResourceId),
            };
            var vfxPlayer = new VfxPlayer(host.Renderer2D, host.Camera, vfxCatalog, resourceLoader: host.ResourceLoader);

            var sfxId = new Id("sfx.sample_hit");
            var sfxCatalog = new System.Collections.Generic.Dictionary<Id, SfxDef>
            {
                [sfxId] = new SfxDef(sfxId, "combat", 5, null, sfxResourceId),
            };
            var sfxPlayer = new SfxPlayer(host.Audio, new RngHost(1), sfxCatalog, resourceLoader: host.ResourceLoader);

            var playSfxCallCountBefore = host.Audio.PlaySfxCallCount;

            var vfxHandle = vfxPlayer.Spawn(vfxId, VfxAttach.World(Vec2.Zero), null);
            var sfxHandle = sfxPlayer.Play(sfxId, Vec2.Zero);

            Assert.IsNull(vfxHandle, "资源尚未加载完成，首次 Spawn 不应立即返回真实句柄（外部审核阻塞项 4）");
            Assert.IsNull(sfxHandle, "资源尚未加载完成，首次 Play 不应立即返回真实句柄（外部审核阻塞项 4）");
            Assert.AreEqual(playSfxCallCountBefore, host.Audio.PlaySfxCallCount, "资源就绪前不应该调用 IAudio.PlaySfx");

            // UnityResourceLoader 的加载是真实异步（后台线程读取 + 主线程 Tick 完成回调，见
            // UnityEngineHost 类型顶部注释），轮询到两个资源都加载完成，允许若干帧。
            var guard = 300;
            while ((!host.ResourceLoader.IsLoaded(vfxResourceId) || !host.ResourceLoader.IsLoaded(sfxResourceId)) && guard-- > 0)
            {
                yield return null;
            }
            Assert.IsTrue(host.ResourceLoader.IsLoaded(vfxResourceId), "vfx 占位资源应当能在有限帧数内加载完成");
            Assert.IsTrue(host.ResourceLoader.IsLoaded(sfxResourceId), "sfx 占位资源应当能在有限帧数内加载完成");

            // 再推进一帧，确保 LoadAsync 回调（可能在 Tick 完成的那一帧才触发）已经跑到
            // OnResourceLoadCompleted 补播放。
            yield return null;

            Assert.AreEqual(playSfxCallCountBefore + 1, host.Audio.PlaySfxCallCount, "sfx 应当在资源加载完成之后被播放，且恰好一次");

            // vfx 侧没有等价的调用计数（见 VfxPlayer 判断记录），改用"资源已加载完成后再次 Spawn
            // 应当立即拿到真实句柄"间接验证首次排队确实推进到了正常路径（不是卡死/异常）。
            var secondVfxHandle = vfxPlayer.Spawn(vfxId, VfxAttach.World(Vec2.Zero), null);
            Assert.IsNotNull(secondVfxHandle, "资源加载完成后，后续 Spawn 应当立即返回真实句柄");
        }
    }
}
