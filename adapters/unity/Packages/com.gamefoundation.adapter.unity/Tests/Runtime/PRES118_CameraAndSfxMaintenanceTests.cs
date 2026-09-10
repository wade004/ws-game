#nullable enable
// PRES118_CameraAndSfxMaintenanceTests：第十八轮审核 presentation-review.md 三项 P2 缺陷中
// PRES118-CAMERA/PRES118-SFX 两项在两个默认生产装配入口（Adapter.Unity.Shell.FrameworkResidentHost、
// Adapter.Unity.Bootstrap.GameFoundationBootstrap）上的契约回归——真实 Unity Runtime，不是 stub。
//
// 判断记录（镜头）：CameraHost 默认 ResetFollowOnSceneLoadFinished=true，scene.load_finished 到达
// 时会清空跟随目标（见 Presentation.Camera.CameraHostOptions 判断记录）。根治前，两个入口都没有在
// 场景加载完成后重新 Follow，镜头静止不再跟随；根治后 Presentation.Assembly.PresentationAssembly
// 在 AutoConfigureCameraFromFirstProfile（两个入口均未覆盖，默认 true）打开且调用方未显式接管
// FollowTargetResolverOnReset 时，自动补一个"继续跟随同一玩家单位"的默认解析函数（见该类型判断
// 记录"PRES-118-CAMERA 根治"），本文件验证的正是这条默认接线在真实生产装配根上确实生效。
//
// 判断记录（SFX）：连续两次播放同一个缺失音效资源，第二次请求没有任何未来回调可等
// （Presentation.VfxSfx.Core.SfxPlayer._pendingResourceLoads 已记录过该资源 id，不会再次
// LoadAsync），只能靠 ISfxPlayer.Update 的超时扫描清理——根治前 GameFoundationBootstrap 的
// OnFrameTick 完全没有调用 Presentation.Sfx.Update（对照 FrameworkResidentHost 有调用），本文件
// 验证根治后两个入口都统一经 Presentation.Assembly.PresentationAssembly.UpdatePlaybackMaintenance
// 每帧驱动 Sfx.Update，不依赖测试代码显式调用 Update 才能让 pending 归零。为了不依赖磁盘上是否
// 恰好缺某个音频资产（那种依赖会随美术资产增删而漂移），两个入口都新增了一个默认关闭、纯新增、
// 对既有场景/冒烟流程零行为影响的测试专用数据叠加开关（见
// FrameworkResidentHost.ForceSfxMissingResourceOverlayForTest、
// GameFoundationBootstrap._extraDatasetRoot 判断记录），登记一个 resource_ref 不对应任何真实
// 音频资产的探针 sfx.def 行。
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Adapter.Unity.Bootstrap;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Shell;
using Core.Foundation.Common;
using Core.Foundation.SceneRouter;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class PRES118_CameraAndSfxMaintenanceTests : PlayModeTestBase
    {
        private static readonly Id SfxProbeId = new Id("sfx.pres118_missing_resource_probe");

        // -----------------------------------------------------------------
        // FrameworkResidentHost（默认常驻装配根）。
        // -----------------------------------------------------------------

        [UnityTest]
        public IEnumerator Resident_NewGame_CameraFollowsPlayer_AfterSceneLoadFinished()
        {
            var host = FrameworkResidentHost.Ensure();
            Assert.IsFalse(host.BootstrapFailed);
            host.Presentation.Shell.Start();

            var slot = new Id("slot.pres118_camera_newgame");
            Assert.IsTrue(host.Presentation.Shell.NewGame(slot, new Id("diff.sample_story"), null));

            var deadline = Time.realtimeSinceStartup + 10f;
            while (host.Presentation.Shell.Page != global::Presentation.Shell.ShellPage.InWorld && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Assert.AreEqual(global::Presentation.Shell.ShellPage.InWorld, host.Presentation.Shell.Page, "NewGame 应能在保底时限内进入 InWorld");

            // 根治点（PRES-118-CAMERA）：进图完成后主镜头仍跟随玩家单位，不再是 null。
            Assert.AreEqual(host.PlayerId, host.Presentation.Camera.FollowEntityId,
                "默认常驻装配根进图完成后（scene.load_finished 到达）应继续跟随玩家单位");

            host.Presentation.SaveSystem.DeleteSlot(slot);
        }

        [UnityTest]
        public IEnumerator Resident_ReturnToMenuThenLoadGame_CameraReacquiresFollow()
        {
            // 覆盖"切图/读档"两个场景：NewGame 进图一次（首次切图）后回主菜单，再 LoadGame 同一个槽位
            // （读档，ShellHost.LoadGame 内部同样经 ISceneRouter.LoadScene 触发一次新的
            // scene.load_finished，见该方法判断记录）。
            var host = FrameworkResidentHost.Ensure();
            Assert.IsFalse(host.BootstrapFailed);
            host.Presentation.Shell.Start();

            var slot = new Id("slot.pres118_camera_loadgame");
            Assert.IsTrue(host.Presentation.Shell.NewGame(slot, new Id("diff.sample_story"), null));

            var deadline = Time.realtimeSinceStartup + 10f;
            while (host.Presentation.Shell.Page != global::Presentation.Shell.ShellPage.InWorld && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Assert.AreEqual(global::Presentation.Shell.ShellPage.InWorld, host.Presentation.Shell.Page);
            Assert.AreEqual(host.PlayerId, host.Presentation.Camera.FollowEntityId, "首次进图（切图）后应跟随玩家");

            Assert.IsTrue(host.Presentation.Shell.ReturnToMainMenu());
            yield return null;
            yield return new WaitForFixedUpdate();

            var loadResult = host.Presentation.Shell.LoadGame(slot);
            Assert.IsTrue(loadResult.Status == Core.Foundation.SaveSystem.LoadStatus.Loaded ||
                          loadResult.Status == Core.Foundation.SaveSystem.LoadStatus.LoadedFromBackup,
                $"读档应成功，实际 status={loadResult.Status}");

            deadline = Time.realtimeSinceStartup + 10f;
            while (host.Presentation.Shell.Page != global::Presentation.Shell.ShellPage.InWorld && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Assert.AreEqual(global::Presentation.Shell.ShellPage.InWorld, host.Presentation.Shell.Page, "LoadGame 应能在保底时限内重新进入 InWorld");

            // 根治点：读档触发的场景重载完成后，镜头仍应重新跟随玩家单位——不是"下一次某个偶然的
            // 事件恰好重新 Follow"，是每次 scene.load_finished 都会重新建立。
            Assert.AreEqual(host.PlayerId, host.Presentation.Camera.FollowEntityId, "读档重进后应重新跟随玩家");

            host.Presentation.SaveSystem.DeleteSlot(slot);
        }

        [UnityTest]
        public IEnumerator Resident_FrameTick_DrivesSfxMaintenance_ClearsMissingSfxPendingWithoutExplicitUpdate()
        {
            // 强制一次干净的重新装配：见 FrameworkResidentHost.ForceSfxMissingResourceOverlayForTest
            // 判断记录——本标志必须在 Ensure() 第一次真正触发 Bootstrap 之前设置，若场景/其它用例
            // 已经把单例带出来过，这里先销毁重建一次（同 SharedBootstrapDiscreteTests.
            // CleanupStaleSharedCompositionRoots 同一惯例：DestroyImmediate 同步触发 OnDestroy，
            // 该方法内部会把静态 _instance 复位为 null，紧接着 Ensure() 才会走到全新的 Bootstrap()）。
            var existing = UnityEngine.Object.FindFirstObjectByType<FrameworkResidentHost>();
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }

            FrameworkResidentHost.ForceSfxMissingResourceOverlayForTest = true;
            FrameworkResidentHost host;
            try
            {
                host = FrameworkResidentHost.Ensure();
            }
            finally
            {
                // 恢复默认：后续用例（若继续复用本单例）不应该再看到本开关处于"已请求"状态——单例
                // 本身不会因为标志复位而回退，探针行已经加载进当前实例的 registry，无害地继续存在。
                FrameworkResidentHost.ForceSfxMissingResourceOverlayForTest = false;
            }
            Assert.IsFalse(host.BootstrapFailed);

            for (var i = 0; i < 5; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            var first = host.Presentation.Sfx.Play(SfxProbeId, null);
            Assert.IsNull(first, "缺资源的首次播放应立即返回 null（排队等待真实异步加载）");

            var deadline1 = Time.realtimeSinceStartup + 10f;
            while (host.Presentation.Sfx.PendingPlayCount != 0 && Time.realtimeSinceStartup < deadline1)
            {
                yield return null;
            }
            Assert.AreEqual(0, host.Presentation.Sfx.PendingPlayCount, "第一次真实 Unity 异步加载失败回调应当到达并清空 pending");

            var second = host.Presentation.Sfx.Play(SfxProbeId, null);
            Assert.IsNull(second);
            Assert.AreEqual(1, host.Presentation.Sfx.PendingPlayCount,
                "第二次播放同一个缺失资源应停留在 pending（SfxPlayer._pendingResourceLoads 已去重，不会再次 LoadAsync）");

            // 根治点（PRES-118-SFX）：本用例不调用 Sfx.Update，只被动等待真实帧推进——
            // FrameworkResidentHost.OnFrameTick 现经 PresentationAssembly.UpdatePlaybackMaintenance
            // 每帧驱动 Sfx.Update，SfxOptions.FirstLoadTimeoutSeconds 默认 5 秒，10 秒保底时限内应当
            // 靠超时扫描把 pending 清零，不依赖测试代码显式调用。
            var deadline2 = Time.realtimeSinceStartup + 10f;
            while (host.Presentation.Sfx.PendingPlayCount != 0 && Time.realtimeSinceStartup < deadline2)
            {
                yield return null;
            }
            Assert.AreEqual(0, host.Presentation.Sfx.PendingPlayCount,
                "OnFrameTick 应逐帧驱动 Sfx.Update，第二次播放的 pending 应在超时后自动清零，不需要测试代码显式调用 Update");
        }

        // -----------------------------------------------------------------
        // GameFoundationBootstrap（示例/灰盒装配根，无单例守卫，每条用例自建独立实例）。
        // -----------------------------------------------------------------

        private static string TestDataRootAbsolute()
        {
            var repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            return Path.Combine(
                repoRoot,
                "adapters", "unity", "Packages", "com.gamefoundation.adapter.unity",
                "Tests", "Runtime", "TestData");
        }

        private static void CleanupStaleGameFoundationBootstraps()
        {
            foreach (var stale in UnityEngine.Object.FindObjectsByType<GameFoundationBootstrap>(FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(stale.gameObject);
            }
        }

        private GameObject? _gfGo;

        [UnityTearDown]
        public IEnumerator LocalTearDown()
        {
            if (_gfGo != null)
            {
                UnityEngine.Object.Destroy(_gfGo);
                _gfGo = null;
            }
            yield return null;
        }

        /// <summary>惯例同 SharedBootstrapDiscreteTests.BuildInactiveBootstrapWithDiscreteOverlay：
        /// 未激活 GameObject 上先 AddComponent（推迟 Awake），反射注入 _extraDatasetRoot 指向
        /// Tests/Runtime/TestData/（同时含 found.time_model 离散覆盖行与本文件新增的
        /// sfx/sfx.def.json 探针行——两者互不影响，见 TestData/sfx/sfx.def.json 判断记录），
        /// 再 SetActive(true) 触发真正的 Awake -> BuildWorld。</summary>
        private GameFoundationBootstrap BuildInactiveBootstrapWithTestDataOverlay()
        {
            CleanupStaleGameFoundationBootstraps();

            var go = new GameObject("PRES118GameFoundationBootstrapTest");
            go.SetActive(false);
            var bootstrap = go.AddComponent<GameFoundationBootstrap>();

            var field = typeof(GameFoundationBootstrap).GetField(
                "_extraDatasetRoot", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "GameFoundationBootstrap 应当有 _extraDatasetRoot 私有字段（H4 新增）");
            field!.SetValue(bootstrap, TestDataRootAbsolute());

            _gfGo = go;
            go.SetActive(true);

            return bootstrap;
        }

        [UnityTest]
        public IEnumerator GameFoundationBootstrap_SyntheticSceneLoadFinished_CameraReacquiresFollow()
        {
            var bootstrap = BuildInactiveBootstrapWithTestDataOverlay();
            Assert.IsFalse(bootstrap.BootstrapFailed, "叠加测试数据根后装配不应失败");
            Assert.IsNotNull(bootstrap.Presentation);

            for (var i = 0; i < 5; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            // 构造期 AutoConfigureCameraFromFirstProfile（默认 true）应已经 Follow 一次。
            Assert.AreEqual(bootstrap.PlayerId, bootstrap.Presentation!.Camera.FollowEntityId, "构造完成后应已跟随玩家单位");

            // 合成一次 scene.load_finished（模拟真实场景切换完成到达同一事件）——不经真实
            // SceneRouter.LoadScene 完整跑一遍场景资源加载（本类型是灰盒示例装配根，没有第二张可切换
            // 的示例地图），直接在真实事件总线上发布同一个业务事件，验证 CameraHost 收到该事件后的
            // 真实响应链路（同 SharedBootstrapDiscreteTests.cs 用真实 IEventBus 合成 CombatEnteredEvent
            // 验证真实响应链路的既有测试惯例，而不是自建假总线）。
            var busField = typeof(GameFoundationBootstrap).GetField("_bus", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(busField, "GameFoundationBootstrap 应当持有内部事件总线字段");
            var bus = busField!.GetValue(bootstrap) as Core.Foundation.EventBus.IEventBus;
            Assert.IsNotNull(bus);

            bus!.PublishImmediate(new SceneLoadFinishedEvent(new Id("world.pres118_gf_synthetic_map")));

            // 根治点（PRES-118-CAMERA）：scene.load_finished 先清空跟随目标，PresentationAssembly
            // 默认补的 FollowTargetResolverOnReset 在同一次事件处理内立即重新指定为同一玩家单位。
            Assert.AreEqual(bootstrap.PlayerId, bootstrap.Presentation.Camera.FollowEntityId,
                "scene.load_finished 到达后应重新跟随玩家单位，不应停留在 null");
        }

        [UnityTest]
        public IEnumerator GameFoundationBootstrap_FrameTick_DrivesSfxMaintenance_ClearsMissingSfxPendingWithoutExplicitUpdate()
        {
            var bootstrap = BuildInactiveBootstrapWithTestDataOverlay();
            Assert.IsFalse(bootstrap.BootstrapFailed, "叠加测试数据根后装配不应失败");

            for (var i = 0; i < 5; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            var first = bootstrap.Presentation!.Sfx.Play(SfxProbeId, null);
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

            // 根治点（PRES-118-SFX）：GameFoundationBootstrap.OnFrameTick 此前完全没有调用
            // Presentation.Sfx.Update（对照 FrameworkResidentHost 有调用，见该方法判断记录），
            // 本用例不调用 Sfx.Update，只被动等待真实帧推进。
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
