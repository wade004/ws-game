#nullable enable
// SharedBootstrapDiscreteTests：H4 收官验收——真实共享引导（Adapter.Unity.Bootstrap.
// GameFoundationBootstrap，GreyBox.unity 唯一挂载的组件本身，不是 DiscreteCombatTests.cs 自建的
// 最小 GameplayAssembly）在数据集把战斗声明为离散时，能否经真实 combatParticipantsResolver
// （见该类型文件顶部"判断记录 1b"）正确驱动离散战斗。
//
// 判断记录（不改动 GreyBox.unity 场景资产，改用叠加数据根 + 反射注入的独立 GameObject）：
// GreyBox.unity 场景资产本身的 GameFoundationBootstrap 组件必须保持默认（_extraDatasetRoot 留空），
// 否则会连带影响 GreyBoxTests/ShellFlowTests/UiSuiteTests/VerticalSliceTests 等全部既有场景类用例
// （它们都依赖场景默认的连续模式战斗）。本类型因此不加载 GreyBox 场景，而是在一个独立、不挂载到
// 任何场景资产的 GameObject 上 AddComponent<GameFoundationBootstrap>，构造期先 SetActive(false)
//
// 判断记录（H5 排障：批次相关 PlayMode 失败根因，见 CleanupStaleSharedCompositionRoots）——
// SharedBootstrap_EndTurnKeyBinding_UnblocksAwaitingInput 单独跑必过、混在全量套件（NUnit 按类型
// 全名字母序执行，GreyBoxTests 恰好排在本类型之前）里必稳定败在"Expected: greater than or equal
// to 1 But was: 0"：GreyBoxTests 每条用例开头都 SceneManager.LoadScene("GreyBox") 重进场景，但
// 套件跑完最后一条用例后场景本身不会再被卸载/重进——GreyBox.unity 场景资产挂载的
// GameFoundationBootstrap 实例因此在整个批处理进程剩余时间里继续存活。该类型 OnFixedStep（与
// Adapter.Unity.Shell.FrameworkResidentHost.OnFixedStep 不同，没有 AppState==InWorld 门槛）
// 每个固定步都无条件调用 Presentation.InputMap.Update(_host.Input)——本类型
// BuildInactiveBootstrapWithDiscreteOverlay 另建的独立实例与这个"残留"实例共享同一个
// UnityEngineHost.Ensure().Input 单例，而 IInput.PollEvents() 是"读了就清空"的单消费者队列语义
// （见 UnityInputTests.PollEvents_DrainsQueue_SecondCallReturnsEmpty，本模块判断契约就是如此，
// 不打算为多消费者场景改成非破坏性读取——生产环境同一时刻只应有一个组合根活跃，"两个组合根
// 并存"本身就是测试隔离缺口，不是生产架构缺口）：两个 OnFixedStep 回调谁先注册谁在本次固定步
// 先跑，会把 SharedBootstrap_EndTurnKeyBinding_UnblocksAwaitingInput 用 SimulateKeyForTest 注入
// 的按键事件，在本类型自己的 InputMap.Update 读到之前抢先耗尽——GreyBox 场景的实例先于本类型
// 注册（GreyBoxTests 先跑），因此稳定抢先，本类型的 InputMap 永远看不到那个按键事件，
// IsActionActive 恒为 false，driveGuard/holdGuard 耗尽后断言失败。根治：见
// CleanupStaleSharedCompositionRoots——构建本类型自己的实例之前，先同步销毁场景里任何残留的
// GameFoundationBootstrap 实例，保证任一时刻只有本类型自己这一个组合根在消费共享输入队列。

// （Unity 标准做法：组件加到未激活 GameObject 上时 Awake 会推迟到 SetActive(true) 才触发，见
// Unity 生命周期文档"Awake is called even if the script instance is not enabled"一节的推论惯例），
// 经反射把私有字段 _extraDatasetRoot 设为 Tests/Runtime/TestData/ 的绝对路径（同
// DiscreteCombatTests.cs 判断记录：UnityFileSystem 内部用 Path.Combine(contentRoot, subPath)
// 解析——.NET Path.Combine 遇到第二段是绝对路径时会直接丢弃第一段，天然绕开
// StreamingAssets/GameFoundation 同步前置条件，不需要跑 build.ps1 -SyncContent），再 SetActive(true)
// 触发真正的 Awake/BuildWorld，这样既验证了真实 GameFoundationBootstrap 类型本身，又完全不触碰
// GreyBox.unity 场景资产。
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Adapter.Unity.Bootstrap;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Ui.Panels;
using Core.Foundation.SimLoop;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class SharedBootstrapDiscreteTests : PlayModeTestBase
    {
        private GameObject? _go;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // 判断记录：不手动调用 World.ClearAll()——GameFoundationBootstrap.OnDestroy() 已经是
            // GreyBoxTests/ShellFlowTests/UiSuiteTests/VerticalSliceTests 场景重进（SceneManager.
            // LoadScene 卸载旧场景触发同一个 OnDestroy）反复验证过的清理路径（先退订固定步/帧回调
            // 停止驱动，再 Presentation.Dispose()/ViewFactory.DestroyAllCreatedViews()）；额外调用
            // World.ClearAll()（只 Enqueue 不立即派发，见该方法注释）会在固定步驱动已经停止之后
            // 留下一批永远不会被 DispatchPending 的悬空 entity.destroyed 事件，与 OnDestroy() 自身
            // 的清理顺序互相打架（实测复现：曾导致本类型两条用例在 TearDown 阶段抛
            // WorldUnitAccess 的"实体不存在"未处理异常）。直接 Destroy(_go) 即可，与本包其它
            // GameFoundationBootstrap 场景测试的既有清理惯例一致。
            if (_go != null)
            {
                UnityEngine.Object.Destroy(_go);
                _go = null;
            }

            yield return null;
        }

        private static string TestDataRootAbsolute()
        {
            // Application.dataPath = "<repo>/adapters/unity/Assets"，向上三级即仓库根（同
            // DiscreteCombatTests.cs 判断记录）。
            var repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            return Path.Combine(
                repoRoot,
                "adapters", "unity", "Packages", "com.gamefoundation.adapter.unity",
                "Tests", "Runtime", "TestData");
        }

        /// <summary>H5 新增：见文件顶部判断记录——同步销毁场景里任何残留的 <see cref="GameFoundationBootstrap"/>
        /// 实例（典型来源：GreyBoxTests 套件跑完后 GreyBox.unity 场景本身仍处于加载状态，其挂载的
        /// 组件仍在被 <c>UnityEngineHost.Clock</c> 驱动 <c>OnFixedStep</c>），避免它与本类型即将
        /// 构建的新实例共享同一个 <c>UnityEngineHost.Input</c> 单例时互相抢占按键事件队列。用
        /// <see cref="UnityEngine.Object.DestroyImmediate(UnityEngine.Object)"/> 而不是
        /// <see cref="UnityEngine.Object.Destroy(UnityEngine.Object)"/>：本方法是同步方法（调用方
        /// <see cref="BuildInactiveBootstrapWithDiscreteOverlay"/> 也是同步方法，没有协程帧可
        /// yield），<c>Destroy</c> 的销毁会推迟到本帧末尾才真正触发 <c>OnDestroy</c> 退订，来不及在
        /// 本方法返回前生效——<c>DestroyImmediate</c> 立即同步调用 <c>OnDestroy</c>，紧接着构建的新
        /// 实例注册固定步回调时，旧实例的回调已经确实退订完毕。</summary>
        private static void CleanupStaleSharedCompositionRoots()
        {
            foreach (var stale in UnityEngine.Object.FindObjectsByType<GameFoundationBootstrap>(FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(stale.gameObject);
            }
        }

        private GameFoundationBootstrap BuildInactiveBootstrapWithDiscreteOverlay()
        {
            CleanupStaleSharedCompositionRoots();

            var go = new GameObject("SharedBootstrapDiscreteTest");
            go.SetActive(false); // 见文件顶部判断记录：推迟 Awake，先注入测试数据根。
            var bootstrap = go.AddComponent<GameFoundationBootstrap>();

            var field = typeof(GameFoundationBootstrap).GetField(
                "_extraDatasetRoot", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "GameFoundationBootstrap 应当有 _extraDatasetRoot 私有字段（H4 新增）");
            field!.SetValue(bootstrap, TestDataRootAbsolute());

            _go = go;
            go.SetActive(true); // 触发 Awake -> BuildWorld，此时 _extraDatasetRoot 已经生效。

            // 判断记录（补齐 AppState 主状态到 InWorld，同 DiscreteCombatTests.cs 判断记录"AppState
            // 必须先走到 InWorld，离散战斗的 Combat 子状态才压得上去"）：GameFoundationBootstrap 是
            // 最小灰盒场景脚手架，本身从不触碰 AppState（没有主菜单/Loading 流程，见文件顶部"判断
            // 记录"逐条勘察确认），AppStateMachineConfig.Default() 只允许 Boot->MainMenu->Loading->
            // InWorld 这条链路，主状态若停留在默认 Boot，TimeModelSwitch.SwitchToDiscrete 末尾
            // PushSubState(Combat)/GameplayAssembly.Advance 里的 PushSubStateIfNotCurrent
            // (AwaitingInputSubState) 都会因为非法转移而静默失败（AppStateHost 对非法转移不抛异常，
            // 只返回 false）——TurnScheduler 内部数据层面完全正确切到离散模式、正确等待玩家输入，
            // 但 AppState.CurrentSubState 永远读不到，HudPanel 的结束回合按钮可见性/键盘
            // 绑定门槛（均读 AppState.CurrentSubState）因此也永远不会打开（实测复现：两条用例都卡
            // 在"从未观察到 awaiting_input"）。本方法补上这条驱动，让"共享引导 + 离散测试根"这套
            // 装配的 AppState 语义与 DiscreteCombatTests.cs/真实 Shell 流程一致。
            bootstrap.Gameplay!.AppState.RequestTransition(Core.Foundation.AppLifecycle.AppState.MainMenu);
            bootstrap.Gameplay.AppState.RequestTransition(Core.Foundation.AppLifecycle.AppState.Loading);
            bootstrap.Gameplay.AppState.RequestTransition(Core.Foundation.AppLifecycle.AppState.InWorld);

            return bootstrap;
        }

        [UnityTest]
        public IEnumerator SharedBootstrap_WithDiscreteOverlay_EntersDiscreteCombat_ViaRealResolver()
        {
            var bootstrap = BuildInactiveBootstrapWithDiscreteOverlay();
            Assert.IsFalse(bootstrap.BootstrapFailed, "叠加离散测试根后共享引导装配不应失败");
            Assert.IsNotNull(bootstrap.Gameplay, "Gameplay 应已装配");

            var dt = Time.fixedDeltaTime;

            // 让世界"落地"几拍（空间索引登记等首拍延迟，见 GameFoundationBootstrap.BuildWorld
            // 同款判断记录），此时仍是连续模式——TestData/ 叠加的 combat=discrete 行与
            // data/_sample 已改回的 combat=continuous 行是两条不同 id 的记录（见
            // data/_sample/found/found.time_model.json、Tests/Runtime/TestData/found/
            // found.time_model.json），TimeModelSwitch.LoadModel 只取同一 scope 下的第一条——
            // 本用例真正要验证的是"一旦真的进入离散模式，共享引导能否用真实
            // combatParticipantsResolver 正确驱动"，不深究哪一条记录胜出的顺序细节。
            for (var i = 0; i < 10; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            // 技术债 17 收口：原独立的 TurnStatusPanel 已删除，回合状态并入 HudViewModel/HudPanel。
            var uiParent = new GameObject("SharedBootstrapDiscreteTestUi", typeof(RectTransform));
            var uiParentRect = (RectTransform)uiParent.transform;
            var hud = new GameObject("Hud", typeof(RectTransform)).AddComponent<HudPanel>();
            hud.transform.SetParent(uiParentRect, false);
            hud.Construct(uiParentRect, bootstrap.Presentation!.Hud, bootstrap.Presentation!.UiIntents);

            try
            {
                // 合成 combat.entered 触发进入战斗（同 DiscreteCombatTests.cs 判断记录：绕开真实
                // 战斗结算的不稳定性，只验证"离散模式引擎侧接线"这条链路）。
                bootstrap.Gameplay!.Encounter.GetType(); // 触碰一次确保对象存活（防御性，无实际断言）。
                var bus = typeof(GameFoundationBootstrap)
                    .GetField("_bus", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(bootstrap) as Core.Foundation.EventBus.IEventBus;
                Assert.IsNotNull(bus, "共享引导应当持有内部事件总线");
                bus!.PublishImmediate(new Core.Rules.Common.CombatEnteredEvent(bootstrap.PlayerId));

                var enterGuard = 400;
                while (bootstrap.Gameplay!.TimeModelSwitch!.CurrentMode != TimeModelMode.Discrete && enterGuard-- > 0)
                {
                    yield return new WaitForFixedUpdate();
                }

                Assert.AreEqual(
                    TimeModelMode.Discrete, bootstrap.Gameplay!.TimeModelSwitch!.CurrentMode,
                    "叠加离散测试根后，合成 combat.entered 应能让共享引导（真实 GameFoundationBootstrap）切到离散模式");
                Assert.IsNotNull(bootstrap.Gameplay.TurnScheduler, "离散模式下 TurnScheduler 应已装配");
                CollectionAssert.Contains(
                    bootstrap.Gameplay.TurnScheduler!.GetOrder(), bootstrap.PlayerId,
                    "真实 combatParticipantsResolver（按半径+阵营解析）应当把玩家自己纳入参战顺序");

                // 驱动直到轮到玩家等待输入，经真实 HudPanel 点击结束回合（走
                // UiIntents.EndTurn -> GameplayAssembly.TurnScheduler.EndTurn 这条真实调用链），
                // 验证共享引导下"HandleFixedInput 提交的意图不需要任何改动即可在离散模式下正确
                // 路由"（H4 核心修复：WorldSim.AttachDiscreteRouting）。
                var initialRound = bootstrap.Gameplay.TurnScheduler!.RoundIndex;
                var driveGuard = 3000;
                var observedAwaitingInput = false;
                while (bootstrap.Gameplay.TurnScheduler.RoundIndex < initialRound + 1 && driveGuard-- > 0)
                {
                    var awaitingInput = bootstrap.Gameplay.AppState.CurrentSubState.HasValue &&
                        bootstrap.Gameplay.AppState.CurrentSubState.Value.Equals(bootstrap.Gameplay.AwaitingInputSubState);

                    if (awaitingInput && bootstrap.Gameplay.TurnScheduler.GetCurrentActor()?.Equals(bootstrap.PlayerId) == true)
                    {
                        observedAwaitingInput = true;
                        hud.ClickEndTurn();
                    }

                    yield return new WaitForFixedUpdate();
                }

                Assert.IsTrue(observedAwaitingInput, "本用例期间应当至少观察到一次轮到玩家的 awaiting_input 子态");
                Assert.GreaterOrEqual(
                    bootstrap.Gameplay.TurnScheduler.RoundIndex, initialRound + 1,
                    "共享引导下，结束回合意图（真实调用链）+ AI 行动应当能推进轮次——证明 H4 之前的" +
                    "\"卡死在 awaiting_input\"缺口已修复");
            }
            finally
            {
                UnityEngine.Object.Destroy(uiParent);
            }
        }

        /// <summary>
        /// H4 补齐验收（步骤 2.3）：<c>awaiting_input</c> 下按 <c>input.action.end_turn</c> 绑定的
        /// 键（默认 <c>key:t</c>，见 <c>data/_framework/found/found.input_action.json</c>）应当能
        /// 结束回合——与 <see cref="SharedBootstrap_WithDiscreteOverlay_EntersDiscreteCombat_ViaRealResolver"/>
        /// 共用同一套"叠加离散测试根"装配，但改用真实按键（经
        /// <see cref="UnityInput.SimulateKeyForTest"/> 注入、真实
        /// <c>GameFoundationBootstrap.OnFixedStep</c> 里的 <c>Presentation.InputMap.Update(_host.Input)</c>
        /// 驱动，不是直接调用 <see cref="HudPanel.ClickEndTurn"/>）——验证
        /// <see cref="HudPanel.Construct"/> 新增的 <c>inputMap</c> 接线本身，而不只是
        /// <see cref="HudPanel"/> 的按钮点击回调（技术债 17 收口：原 <c>TurnStatusPanel</c> 已删除，
        /// 回合状态并入 <c>HudViewModel</c>/<c>HudPanel</c>）。
        /// </summary>
        [UnityTest]
        public IEnumerator SharedBootstrap_EndTurnKeyBinding_UnblocksAwaitingInput()
        {
            var bootstrap = BuildInactiveBootstrapWithDiscreteOverlay();
            Assert.IsFalse(bootstrap.BootstrapFailed, "叠加离散测试根后共享引导装配不应失败");

            // 判断记录：warmup 帧数比 SharedBootstrap_WithDiscreteOverlay_EntersDiscreteCombat_ViaRealResolver
            // 更宽裕（30 帧而不是 10 帧）——本用例在完整批处理套件里跑在其它多个测试类之后，
            // 累积存活的 GameObject/View 更多，生成/空间索引登记可能比套件里最早跑的用例慢，
            // 需要更多帧数落地。
            for (var i = 0; i < 30; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            var uiParent = new GameObject("SharedBootstrapDiscreteKeybindTestUi", typeof(RectTransform));
            var uiParentRect = (RectTransform)uiParent.transform;
            var hud = new GameObject("Hud", typeof(RectTransform)).AddComponent<HudPanel>();
            hud.transform.SetParent(uiParentRect, false);
            // 传入真实 InputMap（第 4 个参数，H4 新增，见 HudPanel.Construct 判断记录）——
            // 与 ShellRoot.cs 生产接线一致，不是 3 参重载（那条路径没有键盘响应）。
            hud.Construct(uiParentRect, bootstrap.Presentation!.Hud, bootstrap.Presentation!.UiIntents, bootstrap.Presentation!.InputMap);

            var input = UnityEngineHost.Ensure().Input as UnityInput;
            Assert.IsNotNull(input, "UnityEngineHost.Input 应当是 UnityInput 实现（H4 新增 SimulateKeyForTest 所在类型）");

            try
            {
                var bus = typeof(GameFoundationBootstrap)
                    .GetField("_bus", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(bootstrap) as Core.Foundation.EventBus.IEventBus;
                Assert.IsNotNull(bus, "共享引导应当持有内部事件总线");
                bus!.PublishImmediate(new Core.Rules.Common.CombatEnteredEvent(bootstrap.PlayerId));

                var enterGuard = 400;
                while (bootstrap.Gameplay!.TimeModelSwitch!.CurrentMode != TimeModelMode.Discrete && enterGuard-- > 0)
                {
                    yield return new WaitForFixedUpdate();
                }
                Assert.AreEqual(TimeModelMode.Discrete, bootstrap.Gameplay!.TimeModelSwitch!.CurrentMode, "应已切到离散模式");

                var initialRound = bootstrap.Gameplay.TurnScheduler!.RoundIndex;
                var driveGuard = 3000;
                var observedAwaitingInput = false;
                var pressedKey = false;
                while (bootstrap.Gameplay.TurnScheduler.RoundIndex < initialRound + 1 && driveGuard-- > 0)
                {
                    var awaitingInput = bootstrap.Gameplay.AppState.CurrentSubState.HasValue &&
                        bootstrap.Gameplay.AppState.CurrentSubState.Value.Equals(bootstrap.Gameplay.AwaitingInputSubState);

                    if (awaitingInput && bootstrap.Gameplay.TurnScheduler.GetCurrentActor()?.Equals(bootstrap.PlayerId) == true
                        && !pressedKey)
                    {
                        observedAwaitingInput = true;
                        pressedKey = true;
                        // 真实按下 't' 键，交给 GameFoundationBootstrap 内部驱动的
                        // Presentation.InputMap.Update(_host.Input)（挂在 UnityEngineHost.Update
                        // 内部，见 UnityClock.TickFrame）解析出 input.action.end_turn 的按下沿，再由
                        // HudPanel.Update（另一个独立的 MonoBehaviour Update，与
                        // UnityEngineHost.Update 之间没有强制的脚本执行顺序保证）响应——不是本用例
                        // 直接调用 ClickEndTurn。按住多帧（而不是只按一帧）覆盖"两个 Update 谁先谁
                        // 后"的不确定性，确保 HudPanel.Update 至少有一帧能在
                        // InputMap 已经解析出按下状态之后才检查 IsActionActive。
                        // 判断记录（按住期间反复补发 KeyDown，不是只发一次）：PollEvents() 会立即
                        // 清空待处理事件队列（见 UnityInput.PollEvents），若只发一次 KeyDown，
                        // "当前是否激活"这一持续状态在批处理环境下与其它并行运行的用例共用同一个
                        // UnityEngineHost 单例时，观测到偶发被更早/更晚经过的其它帧处理路径提前
                        // 复位——在整个持有窗口内每帧都补发一次 KeyDown，确保不管 InputMap.Update
                        // 与 HudPanel.Update 的相对调用顺序如何、也不管这一批次里跑了多少条
                        // 其它用例，本用例持有 't' 键期间 IsActionActive 都能被稳定观测到 true。
                        var holdGuard = 120;
                        var keyBindingTriggered = false;
                        while (holdGuard-- > 0 && !keyBindingTriggered)
                        {
                            input!.SimulateKeyForTest("t", down: true);
                            // 交替等 FixedUpdate（驱动 GameFoundationBootstrap.OnFixedStep ->
                            // Presentation.InputMap.Update）与普通 Update（驱动
                            // HudPanel.Update -> HandleEndTurnKeybinding）各一次——只等
                            // WaitForFixedUpdate 不保证 HudPanel 的常规 MonoBehaviour.Update
                            // 一定在其后紧跟着跑（两者是不同的 Update 分组，见上方判断记录），显式
                            // 各等一次覆盖这层不确定性。
                            yield return new WaitForFixedUpdate();
                            yield return null;
                            keyBindingTriggered = bootstrap.Gameplay.TurnScheduler.RoundIndex >= initialRound + 1;
                        }
                        input!.SimulateKeyForTest("t", down: false);
                    }

                    yield return new WaitForFixedUpdate();
                }

                Assert.IsTrue(observedAwaitingInput, "本用例期间应当至少观察到一次轮到玩家的 awaiting_input 子态");
                Assert.GreaterOrEqual(
                    bootstrap.Gameplay.TurnScheduler.RoundIndex, initialRound + 1,
                    "按 input.action.end_turn 绑定的键（key:t）应当能结束回合、推进轮次——" +
                    "验证 HudPanel 的键盘绑定接线本身（不是按钮点击回调）");
            }
            finally
            {
                UnityEngine.Object.Destroy(uiParent);
            }
        }

        /// <summary>
        /// H5 回归测试：直接复现文件顶部"H5 排障"判断记录描述的根因（场景残留的
        /// <see cref="GameFoundationBootstrap"/> 实例与本类型自建实例共享同一个
        /// <see cref="UnityEngineHost"/>.Input 单例，谁的 <c>OnFixedStep</c> 先跑就先耗尽共享按键
        /// 事件队列），不依赖 NUnit 套件跑动顺序恰好把 GreyBoxTests 排在本类型之前——本用例自己先
        /// <c>SceneManager.LoadScene("GreyBox")</c> 制造一个"残留"场景实例（模拟 GreyBoxTests 套件
        /// 跑完最后一条用例后的收尾状态：场景已加载、从未清理），再走一遍与
        /// <see cref="SharedBootstrap_EndTurnKeyBinding_UnblocksAwaitingInput"/> 相同的核心断言。
        /// <see cref="CleanupStaleSharedCompositionRoots"/> 若被去掉或失效，本用例会以与原始故障
        /// 完全相同的断言失败复现（"Expected: greater than or equal to 1 But was: 0"）；断言通过
        /// 则证明"构建本类型自己的实例之前先同步销毁残留实例"这一清理确实生效。
        /// </summary>
        [UnityTest]
        public IEnumerator SharedBootstrap_EndTurnKeyBinding_StillWorks_WithStaleSceneBootstrapLeftAlive()
        {
            // 制造"残留"：加载 GreyBox 场景，让其 GameFoundationBootstrap 完整启动、注册好固定步
            // 回调，但故意不做任何清理（同 GreyBoxTests.LoadGreyBoxScene 同一套等待惯例）。
            SceneManager.LoadScene("GreyBox");
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;

            var staleBootstrap = UnityEngine.Object.FindFirstObjectByType<GameFoundationBootstrap>();
            Assert.IsNotNull(staleBootstrap, "本用例应先有一个残留的场景 GameFoundationBootstrap 实例（模拟 GreyBoxTests 收尾状态）");
            Assert.IsFalse(staleBootstrap!.BootstrapFailed, "残留场景实例本身应正常启动，才能构成本用例要验证的竞争场景");

            var bootstrap = BuildInactiveBootstrapWithDiscreteOverlay();
            Assert.IsFalse(bootstrap.BootstrapFailed, "叠加离散测试根后共享引导装配不应失败");

            for (var i = 0; i < 30; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            var uiParent = new GameObject("SharedBootstrapDiscreteStaleRootTestUi", typeof(RectTransform));
            var uiParentRect = (RectTransform)uiParent.transform;
            var hud = new GameObject("Hud", typeof(RectTransform)).AddComponent<HudPanel>();
            hud.transform.SetParent(uiParentRect, false);
            hud.Construct(uiParentRect, bootstrap.Presentation!.Hud, bootstrap.Presentation!.UiIntents, bootstrap.Presentation!.InputMap);

            var input = UnityEngineHost.Ensure().Input as UnityInput;
            Assert.IsNotNull(input, "UnityEngineHost.Input 应当是 UnityInput 实现（H4 新增 SimulateKeyForTest 所在类型）");

            try
            {
                var bus = typeof(GameFoundationBootstrap)
                    .GetField("_bus", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(bootstrap) as Core.Foundation.EventBus.IEventBus;
                Assert.IsNotNull(bus, "共享引导应当持有内部事件总线");
                bus!.PublishImmediate(new Core.Rules.Common.CombatEnteredEvent(bootstrap.PlayerId));

                var enterGuard = 400;
                while (bootstrap.Gameplay!.TimeModelSwitch!.CurrentMode != TimeModelMode.Discrete && enterGuard-- > 0)
                {
                    yield return new WaitForFixedUpdate();
                }
                Assert.AreEqual(TimeModelMode.Discrete, bootstrap.Gameplay!.TimeModelSwitch!.CurrentMode, "应已切到离散模式");

                var initialRound = bootstrap.Gameplay.TurnScheduler!.RoundIndex;
                var driveGuard = 3000;
                var observedAwaitingInput = false;
                var pressedKey = false;
                while (bootstrap.Gameplay.TurnScheduler.RoundIndex < initialRound + 1 && driveGuard-- > 0)
                {
                    var awaitingInput = bootstrap.Gameplay.AppState.CurrentSubState.HasValue &&
                        bootstrap.Gameplay.AppState.CurrentSubState.Value.Equals(bootstrap.Gameplay.AwaitingInputSubState);

                    if (awaitingInput && bootstrap.Gameplay.TurnScheduler.GetCurrentActor()?.Equals(bootstrap.PlayerId) == true
                        && !pressedKey)
                    {
                        observedAwaitingInput = true;
                        pressedKey = true;
                        var holdGuard = 120;
                        var keyBindingTriggered = false;
                        while (holdGuard-- > 0 && !keyBindingTriggered)
                        {
                            input!.SimulateKeyForTest("t", down: true);
                            yield return new WaitForFixedUpdate();
                            yield return null;
                            keyBindingTriggered = bootstrap.Gameplay.TurnScheduler.RoundIndex >= initialRound + 1;
                        }
                        input!.SimulateKeyForTest("t", down: false);
                    }

                    yield return new WaitForFixedUpdate();
                }

                Assert.IsTrue(observedAwaitingInput, "本用例期间应当至少观察到一次轮到玩家的 awaiting_input 子态");
                Assert.GreaterOrEqual(
                    bootstrap.Gameplay.TurnScheduler.RoundIndex, initialRound + 1,
                    "即便场景里残留着另一个仍在消费共享输入队列的 GameFoundationBootstrap 实例，" +
                    "CleanupStaleSharedCompositionRoots 清理后按键结束回合也应正常生效（H5 回归——" +
                    "复现场景残留组合根抢占共享输入事件这一根因）");
            }
            finally
            {
                UnityEngine.Object.Destroy(uiParent);
            }
        }
    }
}
