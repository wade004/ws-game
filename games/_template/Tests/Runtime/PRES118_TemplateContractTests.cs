#nullable enable
// PRES118_TemplateContractTests：第十八轮审核 presentation-review.md 三项 P2 缺陷中
// PRES118-CAMERA/PRES118-SFX 两项在模板生产装配入口（Game.Template.GameBootstrap）上的契约
// 回归——真实 Unity Runtime，不是 stub。惯例同
// Adapter.Unity.Tests.Runtime.PRES118_CameraAndSfxMaintenanceTests（FrameworkResidentHost/
// GameFoundationBootstrap 两个入口的同名契约测试），本文件只覆盖模板特有的第三种场景：
// GameOptions.CameraResetFollowOnSceneLoadFinished=false 时（13 §4 第 12 行口味配置项、
// PresentationAssemblyOptions.CameraHostOptions 透传，见 GameBootstrap.BuildPresentationOptions
// 判断记录"PRES-118-CAMERA 根治"）跟随目标应从未被清空过。
//
// 判断记录（不经 GameTemplateShell 场景/TemplateShellUi，改用独立未激活 GameObject 装配）：模板
// 自带的最小示例数据集（games/_template/data/game）没有登记任何 camera_profile 行（模板本就是
// "最小闭环"，见 README"已知限制"一节，配表现步骤本就允许留空），
// PresentationAssembly.AutoConfigureCameraFromFirstProfile 找不到任何 camera_profile 行时整段跳过
// 构造期的 Configure+Follow——这与 PRES-118-CAMERA 是否根治无关，是本模板数据集固有的前提。三个
// 用例因此都不依赖"构造完成后已经在跟随"这一前提是否成立：进图用例只断言"进入 InWorld 之后"这一
// 终态；reset=false 用例改为先手工 Follow 建立一个基线（Follow 本身不要求先 Configure），再验证
// 该基线跨合成的 scene.load_finished 不被清空。改用独立 GameObject（同
// Adapter.Unity.Tests.Runtime.SharedBootstrapDiscreteTests/PRES118_CameraAndSfxMaintenanceTests 里
// GameFoundationBootstrap 那一半同款惯例）而不是 GameTemplateSmokeTests.LoadShellScene() 场景流程，
// 是为了避免与同一 PlayMode 批次里先跑的 GameTemplateSmokeTests（尤其
// TemplateSmokeRunner_InEditor_CompletesSequenceSuccessfully，跑完冒烟序列后停留在 InWorld、从不
// ReturnToMainMenu）共用同一个 DontDestroyOnLoad 单例 GameBootstrap 时的跨用例状态残留——本类型
// 每条用例都先销毁场景里任何残留实例，构建一个完全独立、装配根 AppState 从 Boot 起步的新实例。
using System.Collections;
using System.Reflection;
using Core.Foundation.Common;
using Core.Foundation.SceneRouter;
using Game.Template;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Template.Tests
{
    public sealed class PRES118_TemplateContractTests
    {
        private static readonly Id SfxProbeId = new Id("sfx.pres118_missing_resource_probe");

        private GameObject? _standaloneGo;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_standaloneGo != null)
            {
                Object.Destroy(_standaloneGo);
                _standaloneGo = null;
            }
            GameBootstrap.ForceSfxMissingResourceOverlayForTest = false;
            yield return null;
        }

        /// <summary>惯例同 Adapter.Unity.Tests.Runtime.SharedBootstrapDiscreteTests.
        /// CleanupStaleSharedCompositionRoots：构建本类型自己的独立实例之前，先同步销毁场景里任何
        /// 残留的 GameBootstrap 实例——GameBootstrap.Awake 有单例守卫（_instance != null &amp;&amp;
        /// _instance != this 时自毁，见该类型判断记录），不清理干净会导致本类型新建的 GameObject
        /// 自毁、拿到的是旧实例，测试断言的是"上一条用例（或同批次先跑的 GameTemplateSmokeTests）的
        /// 旧装配"而不是本用例刚设置的口味配置。</summary>
        private static void CleanupStaleGameBootstraps()
        {
            foreach (var stale in Object.FindObjectsByType<GameBootstrap>(FindObjectsSortMode.None))
            {
                Object.DestroyImmediate(stale.gameObject);
            }
        }

        /// <summary>未激活 GameObject 上先 AddComponent（推迟 Awake），调用方可在 SetActive(true)
        /// 之前经公开的 <see cref="GameBootstrap.Options"/> 属性直接修改口味配置（不需要反射——
        /// <c>GameOptions _options</c> 字段本身是私有的，但 <c>Options</c> 是返回同一个可变对象引用的
        /// 只读属性，同 <c>SharedBootstrapDiscreteTests</c> 反射注入 _extraDatasetRoot 达到的效果
        /// 一致，只是本类型恰好有公开属性可以不用反射）。</summary>
        private GameBootstrap BuildInactiveBootstrap()
        {
            CleanupStaleGameBootstraps();

            var go = new GameObject("PRES118TemplateBootstrapTest");
            go.SetActive(false);
            var bootstrap = go.AddComponent<GameBootstrap>();
            _standaloneGo = go;
            return bootstrap;
        }

        private static Core.Foundation.EventBus.IEventBus GetBus(GameBootstrap bootstrap)
        {
            var busField = typeof(GameBootstrap).GetField("_bus", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(busField, "GameBootstrap 应当持有内部事件总线字段");
            var bus = busField!.GetValue(bootstrap) as Core.Foundation.EventBus.IEventBus;
            Assert.IsNotNull(bus, "GameBootstrap._bus 不应为 null（说明 Awake/Bootstrap 尚未真正跑过）");
            return bus!;
        }

        [UnityTest]
        public IEnumerator GameBootstrap_NewGame_CameraFollowsPlayer_AfterInWorld()
        {
            var bootstrap = BuildInactiveBootstrap();
            bootstrap.gameObject.SetActive(true);
            Assert.IsFalse(bootstrap.BootstrapFailed, "默认 GameOptions 下 GameBootstrap 不应装配失败");

            // 独立实例的 AppState 从 Boot 起步（不经 TemplateShellUi.Awake 那一步隐式 Start），
            // 需要本用例自己显式 Start 一次，同 Adapter.Unity.Tests.Runtime.
            // PRES118_CameraAndSfxMaintenanceTests.Resident_NewGame_* 同款惯例。
            bootstrap.Presentation.Shell.Start();

            // 判断记录（存档槽 id 前缀）：GameOptions.GameId 恒为 "game.template"，
            // SaveSystemOptions.MaxSlots（默认 20）按这一个 game id 下累计的全部槽位计数——本模板
            // 测试套件的 [SetUpFixture] GlobalTemplateTestSetup 只在整套运行开始前清理一次"game.
            // template." 前缀的槽文件（同 TemplateSmokeRunner.DefaultSaveSlotId 的既有命名惯例，见
            // 该类型 [OneTimeSetUp] 注释），不是"game.sample."/"game.template."/裸 slot.* 全前缀
            // 隔离到专属测试目录那一套更彻底的机制（那一套只覆盖 Adapter.Unity.Tests.Runtime 命名
            // 空间，见 GlobalPlayModeTestSetup.cs 判断记录）——本槽位 id 必须以 "game.template." 开头
            // 才会被下一次整套运行开始前的清理收走，否则会在这台机器上跨多次运行无限累积，早晚撞见
            // "存档槽数量已达上限"（真实复现过：RequestNewGame 因此返回 false，且只在与其它测试套件
            // 一起跑的全量批次里出现，单独跑本类型不会复现，见该复现记录）。
            var slot = new Id("game.template.pres118_camera");
            Assert.IsTrue(bootstrap.RequestNewGame(slot), "RequestNewGame 应当成功");

            var deadline = Time.realtimeSinceStartup + 15f;
            while (bootstrap.Presentation.Shell.Page != global::Presentation.Shell.ShellPage.InWorld && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Assert.AreEqual(global::Presentation.Shell.ShellPage.InWorld, bootstrap.Presentation.Shell.Page,
                "RequestNewGame 应能在保底时限内进入 InWorld");

            // 根治点（PRES-118-CAMERA）：模板默认口味配置（GameOptions.CameraResetFollowOnSceneLoadFinished
            // 默认 true）下，进图完成（scene.load_finished 到达）后主镜头仍跟随玩家单位——不要求模板
            // 数据集登记了 camera_profile 行（PresentationAssembly 在 AutoConfigureCameraFromFirstProfile
            // 打开时默认补的 FollowTargetResolverOnReset 只依赖该开关本身，不依赖是否真的找到过
            // profile 行，见其类型判断记录）。
            Assert.AreEqual(bootstrap.PlayerId, bootstrap.Presentation.Camera.FollowEntityId,
                "模板默认装配根进图完成后应继续跟随玩家单位");

            bootstrap.Presentation.SaveSystem.DeleteSlot(slot);
        }

        [UnityTest]
        public IEnumerator GameBootstrap_CameraResetFollowDisabled_FollowNeverClearedAcrossSyntheticSceneLoad()
        {
            var bootstrap = BuildInactiveBootstrap();
            // Awake 之前设置：CameraResetFollowOnSceneLoadFinished=false 时，
            // BuildPresentationOptions() 透传给 PresentationAssemblyOptions.CameraHostOptions 的
            // ResetFollowOnSceneLoadFinished 恒为 false——CameraHost.OnSceneLoadFinished 整段提前
            // return，跟随目标从未被清空过，验证"是否重置"仍是可替换策略（不是被写死的默认行为）。
            bootstrap.Options.CameraResetFollowOnSceneLoadFinished = false;
            bootstrap.gameObject.SetActive(true);

            Assert.IsFalse(bootstrap.BootstrapFailed, "reset=false 不应影响装配本身是否成功");

            for (var i = 0; i < 5; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            // 判断记录：模板示例数据集未登记 camera_profile 行（见文件顶部判断记录），构造期
            // AutoConfigureCameraFromFirstProfile 找不到 profile 行会整段跳过，不代表 PRES-118-CAMERA
            // 未根治——本用例的断言目标是"reset=false 时既有跟随目标不会被清空"，不是"构造完成后自动
            // 开始跟随"，因此先手工 Follow 建立一个基线（ICameraHost.Follow 本身不要求先 Configure）。
            bootstrap.Presentation.Camera.Follow(bootstrap.PlayerId);
            Assert.AreEqual(bootstrap.PlayerId, bootstrap.Presentation.Camera.FollowEntityId, "手工 Follow 后应已跟随玩家单位");

            var bus = GetBus(bootstrap);
            bus.PublishImmediate(new SceneLoadFinishedEvent(new Id("world.pres118_template_synthetic_map")));

            Assert.AreEqual(bootstrap.PlayerId, bootstrap.Presentation.Camera.FollowEntityId,
                "CameraResetFollowOnSceneLoadFinished=false 时 scene.load_finished 不应清空跟随目标");
        }

        [UnityTest]
        public IEnumerator GameBootstrap_FrameTick_DrivesSfxMaintenance_ClearsMissingSfxPendingWithoutExplicitUpdate()
        {
            GameBootstrap.ForceSfxMissingResourceOverlayForTest = true;
            var bootstrap = BuildInactiveBootstrap();
            bootstrap.gameObject.SetActive(true);
            GameBootstrap.ForceSfxMissingResourceOverlayForTest = false;

            Assert.IsFalse(bootstrap.BootstrapFailed, "叠加缺失资源探针后装配不应失败");

            for (var i = 0; i < 5; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            var first = bootstrap.Presentation.Sfx.Play(SfxProbeId, null);
            Assert.IsNull(first, "缺资源的首次播放应立即返回 null（排队等待真实异步加载）");

            var deadline1 = Time.realtimeSinceStartup + 10f;
            while (bootstrap.Presentation.Sfx.PendingPlayCount != 0 && Time.realtimeSinceStartup < deadline1)
            {
                yield return null;
            }
            Assert.AreEqual(0, bootstrap.Presentation.Sfx.PendingPlayCount, "第一次真实 Unity 异步加载失败回调应当到达并清空 pending");

            var second = bootstrap.Presentation.Sfx.Play(SfxProbeId, null);
            Assert.IsNull(second);
            Assert.AreEqual(1, bootstrap.Presentation.Sfx.PendingPlayCount,
                "第二次播放同一个缺失资源应停留在 pending（SfxPlayer._pendingResourceLoads 已去重，不会再次 LoadAsync）");

            // 根治点（PRES-118-SFX）：模板 GameBootstrap.OnFrameTick 此前只手工罗列了
            // Feedback.Update/Vfx.Update 两步，漏抄了 Sfx.Update（见该方法判断记录）。本用例不调用
            // Sfx.Update，只被动等待真实帧推进。
            var deadline2 = Time.realtimeSinceStartup + 10f;
            while (bootstrap.Presentation.Sfx.PendingPlayCount != 0 && Time.realtimeSinceStartup < deadline2)
            {
                yield return null;
            }
            Assert.AreEqual(0, bootstrap.Presentation.Sfx.PendingPlayCount,
                "OnFrameTick 应逐帧驱动 Sfx.Update，第二次播放的 pending 应在超时后自动清零，不需要测试代码显式调用 Update");
        }
    }
}
