#nullable enable
// PlayModeIsolation：PlayMode 测试装配级隔离的集中实现（收边任务 3，见任务书"PlayMode 测试隔离"）。
//
// 背景（本包 DontDestroyOnLoad 单例三次导致批次相关失败的历史，见各处判断记录）：
// UnityEngineHost/FrameworkResidentHost 都是跨整个 -runTests 批处理进程存活的单例（不随场景卸载
// 销毁），此前至少三处独立发现过"上一条用例的残留状态污染下一条完全无关用例"的问题——输入队列被
// 残留引导抢先消费（见 SharedBootstrapDiscreteTests.cs "H5 排障"判断记录）、场景残留的
// GameFoundationBootstrap 实例继续在后台 tick 已清空的世界（VerticalSliceTests.cs 此前的
// TearDown 判断记录）、残留光环/计时器命中已销毁的实体（同上）。此前的修法是在各测试类里各自散落
// 一段规避代码（VerticalSliceTests.TearDown、SharedBootstrapDiscreteTests.CleanupStaleSharedCompositionRoots
// 等），本类型把"跨用例通用"的那部分收敛到一处，供 <see cref="PlayModeTestBase"/> 统一调用；
// 真正"同一条用例内部、构建新组合根之前"需要的防御性清理（如
// SharedBootstrapDiscreteTests.CleanupStaleSharedCompositionRoots，其对应的回归测试
// SharedBootstrap_EndTurnKeyBinding_StillWorks_WithStaleSceneBootstrapLeftAlive 专门验证这一次性
// 调用本身，不是跨用例职责）不属于本类型范围，原地保留。
using System.Collections;
using Adapter.Unity.Bootstrap;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Shell;
using Core.Foundation.AppLifecycle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Adapter.Unity.Tests.Runtime
{
    public static class PlayModeIsolation
    {
        /// <summary>上一次执行 <see cref="TearDownAfterTest"/> 时所属的用例全名，供
        /// <see cref="AssertCleanBeforeTest"/> 断言失败时指出可能的残留来源（"哪条用例的 TearDown
        /// 没有清理干净"），不保证百分之百精确（例如某条用例中途崩溃导致 TearDown 本身未执行），
        /// 但覆盖绝大多数"清理逻辑本身有缺口"的场景，足以指路。</summary>
        public static string? LastTornDownTestFullName { get; private set; }

        /// <summary>每条用例结束后统一执行（由 <see cref="PlayModeTestBase.BaseTearDown"/> 调用）。
        /// 收敛自此前散落在各测试类里的规避代码，逐条对应任务书"PlayMode 测试隔离"列出的清单：
        /// 应用状态切回 MainMenu、World.ClearAll+DispatchPending、等待 ResourceLoader 在途加载归零、
        /// 销毁残留的 GameFoundationBootstrap、清空 UnityInput 模拟队列、复位 FrameworkResidentHost
        /// （不销毁单例本身，只把应用状态带回 MainMenu——单例继续常驻，供下一条用例复用）、全局至多
        /// 一个 EventSystem。</summary>
        public static IEnumerator TearDownAfterTest()
        {
            LastTornDownTestFullName = TestContext.CurrentContext.Test.FullName;

            // 1) 复位 FrameworkResidentHost：应用状态切回 MainMenu（停止其 OnFixedStep 继续 tick
            //    已经即将被清空的世界，避免残留光环/计时器在下一条无关用例的执行窗口内触发，见
            //    VerticalSliceTests.cs 此前的 TearDown 判断记录）+ World.ClearAll + DispatchPending
            //    立即结算清空。不销毁 FrameworkResidentHost 本身（DontDestroyOnLoad 单例继续常驻，
            //    供下一条用例经场景重进/ShellRoot.Awake 的 ReturnToMainMenu 复用）。
            var resident = Object.FindFirstObjectByType<FrameworkResidentHost>();
            if (resident != null && !resident.BootstrapFailed)
            {
                resident.World.ClearAll();
                resident.Bus.DispatchPending();
                resident.Gameplay.AppState.RequestTransition(AppState.MainMenu);
            }

            // 2) 销毁场景中残留的独立 GameFoundationBootstrap 组合根（典型来源：GreyBoxTests 套件
            //    跑完最后一条用例后，GreyBox.unity 场景本身仍处于加载状态；或任何测试手工
            //    AddComponent 出来、忘记自行销毁的实例），避免其继续消费共享 UnityEngineHost.Input
            //    队列、抢占下一条用例注入的按键事件（见 SharedBootstrapDiscreteTests.cs"H5 排障"
            //    判断记录）。
            foreach (var stale in Object.FindObjectsByType<GameFoundationBootstrap>(FindObjectsSortMode.None))
            {
                Object.DestroyImmediate(stale.gameObject);
            }

            // 3) 清空 UnityInput 模拟队列：IInput.PollEvents() 是"读了就清空"的单消费者语义（见
            //    UnityInputTests.PollEvents_DrainsQueue_SecondCallReturnsEmpty），读一次即可让本用例
            //    残留的任何未消费事件不会被下一条用例误当作"自己产生的输入"。
            var host = UnityEngineHost.Ensure();
            host.Input.PollEvents();

            // 4) 等待全部在途资源加载在本用例窗口内落地：UnityResourceLoader 把实际文件读取丢进
            //    后台线程，真正的解码/诊断日志只在下一次 Tick（主线程）才发生，若本用例结束时
            //    仍有未完成的后台加载，迟到的日志会被 Unity Test Framework 记到下一条完全无关的
            //    用例头上（见 UnityResourceLoader.cs PendingLoadCount 判断记录，此前
            //    VerticalSliceTests.TearDown 已有同款逻辑，现收敛到这里）。300 帧仍未清零视为真正
            //    卡死，放行避免整套用例因此永久挂起。
            var guard = 300;
            while (host.ResourceLoader.PendingLoadCount > 0 && guard-- > 0)
            {
                yield return null;
            }

            // 5) 全局至多一个 EventSystem：UnityEngineHost 已经在 SceneManager.sceneLoaded 时做过
            //    一次收敛（见该类型 DeduplicateEventSystems 判断记录），这里补一次防御性收敛，
            //    覆盖"两条用例之间没有发生任何场景加载"的情况（例如两条都不加载场景、只操作共享
            //    单例的测试相邻执行）。
            DeduplicateStrayEventSystems();
        }

        /// <summary>每条用例开始前统一执行（由 <see cref="PlayModeTestBase.BaseSetUp"/> 调用）：
        /// 断言上一条用例的 TearDown 确实清理干净，残留则失败并指出可能的来源用例（见
        /// <see cref="LastTornDownTestFullName"/>），而不是让残留状态静默污染本条用例、产生一个
        /// 难以追查根因的间接失败。</summary>
        public static void AssertCleanBeforeTest()
        {
            var staleBootstraps = Object.FindObjectsByType<GameFoundationBootstrap>(FindObjectsSortMode.None);
            Assert.AreEqual(0, staleBootstraps.Length,
                $"用例开始前检测到 {staleBootstraps.Length} 个残留的 GameFoundationBootstrap 实例" +
                $"（上一条用例：{LastTornDownTestFullName ?? "<未知，可能是本次运行的第一条用例>"}），" +
                "TearDown 未能正确清理，请检查该用例是否绕开了 PlayModeTestBase 的统一清理。");

            var host = UnityEngineHost.Ensure();
            Assert.AreEqual(0, host.ResourceLoader.PendingLoadCount,
                $"用例开始前检测到 {host.ResourceLoader.PendingLoadCount} 个仍在途的资源加载请求" +
                $"（上一条用例：{LastTornDownTestFullName ?? "<未知，可能是本次运行的第一条用例>"}），" +
                "TearDown 未能等待其在本用例窗口内落地。");

            var systems = Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
            Assert.LessOrEqual(systems.Length, 1,
                $"用例开始前检测到 {systems.Length} 个 EventSystem 实例（上一条用例：" +
                $"{LastTornDownTestFullName ?? "<未知，可能是本次运行的第一条用例>"}），全局应当至多一个。");
        }

        /// <summary>与 UnityEngineHost.DeduplicateEventSystems 同一套算法（该方法是 private，分处
        /// 不同职责的类型，重复一份比新增跨类型内部依赖更简单，同本包既有惯例）：优先保留不叫
        /// "GameFoundation.EventSystem"的那个（场景自己烘焙的），销毁其余的。</summary>
        private static void DeduplicateStrayEventSystems()
        {
            var systems = Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
            if (systems.Length <= 1)
            {
                return;
            }

            EventSystem? toKeep = null;
            for (var i = 0; i < systems.Length; i++)
            {
                if (systems[i].gameObject.name != "GameFoundation.EventSystem")
                {
                    toKeep = systems[i];
                    break;
                }
            }
            toKeep ??= systems[0];

            for (var i = 0; i < systems.Length; i++)
            {
                if (systems[i] != toKeep)
                {
                    Object.DestroyImmediate(systems[i].gameObject);
                }
            }
        }
    }
}
