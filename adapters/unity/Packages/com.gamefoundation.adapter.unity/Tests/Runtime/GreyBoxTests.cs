#nullable enable
// GreyBoxTests：U2-4 灰盒场景 PlayMode 测试。全部用例都先加载 Assets/Framework/Scenes/GreyBox.unity
// （由 Adapter.Unity.EditorTools.GreyBoxSceneBuilder.Build 生成、已加入 Build Settings 第 0 位），
// 找到场景里唯一的 GameFoundationBootstrap 实例断言。
//
// 判断记录（"注入意图"一律直接调用 IWorldSim.SubmitIntent / 窄契约 API，不模拟物理键鼠事件）：
// 与 core/gameplay/tests/EndToEndTests.cs、GameWorldFixture 的既有测试哲学一致——那两处测试全部
// 通过直接注入 Intent/调用 API 驱动世界，从未模拟过任何引擎侧的物理输入事件；GameFoundationBootstrap
// 自己的输入映射（键盘/鼠标 -> Intent）在 EngineAdapter 层已有独立的 UnityInputTests 覆盖，本文件
// 只关心"意图注入后，世界与表现层是否正确反应"这条链路，直接注入 Intent 更稳定、不依赖批处理环境
// 下 Input System 模拟按键的时序细节。
//
// 判断记录（移动测试的方向档位断言范围）：presentation/common/contracts/DirectionSlots.cs 的量化
// 表（该文件"8 方向完整对照表"judgment record）显示 +X（东，索引 0）对应 dir.side_l（side_r 的
// 镜像），不是 dir.side_r——原始美术画的是 side_r，dir.side_l 只是同一张图 flipX 之后的镜像使用。
// 本用例断言"移动方向变化后视图从 front 转为侧向档位之一"（覆盖 side_l/side_r/front_side_l/
// front_side_r 四种，不锁死具体是镜像侧还是原始侧），既验证了朝向确实随移动方向更新，也不会因为
// "向右移动到底用 side_r 还是 side_l 表示"这个已经由 DirectionSlots 判断记录钉死、本任务不能改的
// 既有结论而产生一个必然失败的断言。
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Adapter.Unity.Bootstrap;
using Adapter.Unity.EngineAdapter;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SimLoop;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class GreyBoxTests
    {
        private const string SceneName = "GreyBox";

        private static readonly HashSet<string> SidewaysDirectionSlots = new HashSet<string>
        {
            "dir.side_r", "dir.side_l", "dir.front_side_r", "dir.front_side_l",
        };

        [TearDown]
        public void TearDown()
        {
            // 每条用例都会至少 LoadScene 一次；不额外清理——UnityEngineHost 本就设计为
            // DontDestroyOnLoad 跨用例复用（同一批处理进程内），这正是"场景重进不产生重复
            // UnityEngineHost"要覆盖的行为本身。
        }

        private static IEnumerator LoadGreyBoxScene()
        {
            SceneManager.LoadScene(SceneName);
            yield return null;
            yield return null;
            // 判断记录：EntityCreatedEvent 走 IEventBus.Enqueue（排队，非立即派发，见该事件类型
            // 注释），只有 WorldSim.Tick 的"事件派发"阶段才会真正把它送到 ViewBinder；本类型的
            // WorldSim.Tick 只由 GameFoundationBootstrap.FixedUpdate 驱动——单纯等两帧 Update
            // 不保证任何一次 FixedUpdate 已经跑过（尤其批处理环境下两帧之间的真实时间可能远小于
            // Time.fixedDeltaTime），必须显式等至少一次 WaitForFixedUpdate，玩家/生物的 View 才会
            // 出现在 ViewBinder 里。
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        private static GameFoundationBootstrap RequireBootstrap()
        {
            var bootstrap = Object.FindFirstObjectByType<GameFoundationBootstrap>();
            Assert.IsNotNull(bootstrap, "场景里应当有且仅有一个 GameFoundationBootstrap 实例");
            return bootstrap!;
        }

        [UnityTest]
        public IEnumerator Bootstrap_StartsUp_WithoutBlockingDatasetErrors()
        {
            yield return LoadGreyBoxScene();
            var bootstrap = RequireBootstrap();

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.IsFalse(bootstrap.BootstrapFailed, "数据集加载/世界装配不应出现阻断性错误");
            Assert.IsNotNull(bootstrap.Presentation, "PresentationAssembly 应已成功构造");
            Assert.IsNotNull(bootstrap.Gameplay, "GameplayAssembly 应已成功构造");
        }

        [UnityTest]
        public IEnumerator PlayerView_ExistsAndLayerResourcesLoaded_NotPlaceholder()
        {
            yield return LoadGreyBoxScene();
            var bootstrap = RequireBootstrap();

            Assert.IsTrue(bootstrap.Presentation!.ViewBinder.TryGetView(bootstrap.PlayerId, out var view));
            Assert.IsNotNull(view);
            Assert.IsTrue(view!.IsAlive);

            // 等待纸娃娃层资源异步加载完成（UnitySpriteView 在首次 SyncPose 时发起 LoadAsync，
            // 由 UnityResourceLoader 后台线程读取 + 每帧 Tick 在主线程完成解码）。
            SpriteRenderer? heroBodyLayer = null;
            var timeout = 5f;
            while (timeout > 0f)
            {
                heroBodyLayer = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None)
                    .FirstOrDefault(r => r.sprite != null && r.sprite.name.StartsWith("layer.placeholder_hero__") && r.sprite.name.EndsWith("__body"));
                if (heroBodyLayer != null)
                {
                    break;
                }
                yield return null;
                timeout -= Time.unscaledDeltaTime > 0f ? Time.unscaledDeltaTime : 0.02f;
            }

            Assert.IsNotNull(heroBodyLayer, "应当能在场景里找到一个已加载真实贴图（非占位方块）的玩家 body 层精灵");
            Assert.AreNotEqual("placeholder.sprite", heroBodyLayer!.sprite.name, "玩家层资源不应回退到占位方块");
        }

        [UnityTest]
        public IEnumerator Move_Right_IncreasesPlayerX_AndTurnsSideways()
        {
            yield return LoadGreyBoxScene();
            var bootstrap = RequireBootstrap();

            var startX = bootstrap.World!.GetEntity(bootstrap.PlayerId)!.Position.X;

            // 按 05 §6.2"方向向量……每 tick 需要调用方重新提交"的约定，逐 tick 重新提交，覆盖约 1
            // 模拟秒（Time.fixedDeltaTime 默认 0.02s，50 次约 1 秒）。
            var ticks = Mathf.Max(1, Mathf.RoundToInt(1f / Mathf.Max(Time.fixedDeltaTime, 0.001f)));
            for (var i = 0; i < ticks; i++)
            {
                bootstrap.Gameplay!.Carriers.Movement.Request(MoveRequest.InDirection(bootstrap.PlayerId, new Vec2(1, 0)));
                yield return new WaitForFixedUpdate();
            }
            yield return null; // 让 Update() 至少跑一次 ViewBinder.SyncAll，方向档位切换体现到视图。

            var endX = bootstrap.World!.GetEntity(bootstrap.PlayerId)!.Position.X;
            Assert.Greater(endX, startX, "向 +X 方向持续提交移动意图后，玩家世界坐标 x 应当增大");

            var sidewaysSprite = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None)
                .FirstOrDefault(r => r.sprite != null && r.sprite.name.StartsWith("layer.placeholder_hero__") &&
                                      SidewaysDirectionSlots.Any(slot => r.sprite.name.Contains("__" + slot.Substring("dir.".Length) + "__")));
            Assert.IsNotNull(sidewaysSprite, "移动后玩家视图的方向档位应当已从 front 转为侧向档位之一（side_r/side_l/front_side_r/front_side_l）");
        }

        [UnityTest]
        public IEnumerator Attack_ReducesBeastHealth_AndSpawnsFloatingText()
        {
            yield return LoadGreyBoxScene();
            var bootstrap = RequireBootstrap();

            Assert.IsTrue(bootstrap.BeastEntityId.HasValue, "灰盒场景应当已经通过 spawn.sample_beast_field 生成一只生物");
            var beastId = bootstrap.BeastEntityId!.Value;

            var startHealth = bootstrap.Gameplay!.Carriers.Rules.Powers.GetPower(beastId, Core.Rules.Common.WellKnownPowers.Health);

            var attackSkillId = new Id("skill.sample_strike");
            var args = new JsonObjectBuilder().Add("skill_id", new JsonString(attackSkillId.Value)).Build();

            // 命中表存在随机 miss/dodge/crit 分支（同 GameWorldFixture.CastUntilDead 的既有判断
            // 记录），循环提交直到命中或到达上限，不断言"第一次必定命中"。
            var healthDropped = false;
            for (var attempt = 0; attempt < 60 && !healthDropped; attempt++)
            {
                bootstrap.World!.SubmitIntent(new Intent(bootstrap.PlayerId, "cast", args));
                yield return new WaitForFixedUpdate();
                yield return null;

                var current = bootstrap.Gameplay.Carriers.Rules.Powers.GetPower(beastId, Core.Rules.Common.WellKnownPowers.Health);
                if (current < startHealth)
                {
                    healthDropped = true;
                }
            }

            Assert.IsTrue(healthDropped, "多次尝试普攻后，目标生物血量应当至少下降过一次");
            Assert.Greater(bootstrap.FloatingText!.SpawnedCount, 0, "命中造成伤害后，飘字接收器应当至少产生一次飘字");
        }

        [UnityTest]
        public IEnumerator Sfx_PlaysAtLeastOnce_AfterAttackHits()
        {
            yield return LoadGreyBoxScene();
            var bootstrap = RequireBootstrap();
            Assert.IsTrue(bootstrap.BeastEntityId.HasValue, "灰盒场景应当已经通过 spawn.sample_beast_field 生成一只生物");

            var audio = UnityEngineHost.Ensure().Audio;
            var startCallCount = audio.PlaySfxCallCount;

            var args = new JsonObjectBuilder().Add("skill_id", new JsonString("skill.sample_strike")).Build();
            for (var attempt = 0; attempt < 60 && audio.PlaySfxCallCount == startCallCount; attempt++)
            {
                bootstrap.World!.SubmitIntent(new Intent(bootstrap.PlayerId, "cast", args));
                yield return new WaitForFixedUpdate();
                yield return null;
            }

            Assert.Greater(audio.PlaySfxCallCount, startCallCount, "命中后 feedback.binding 的 play_sfx 动作应当至少调用过一次 IAudio.PlaySfx");
        }

        [UnityTest]
        public IEnumerator SceneReload_Twice_DoesNotThrow_AndKeepsSingleEngineHost()
        {
            yield return LoadGreyBoxScene();
            RequireBootstrap();
            var hostAfterFirstLoad = UnityEngineHost.Ensure();

            yield return LoadGreyBoxScene();
            RequireBootstrap();

            yield return LoadGreyBoxScene();
            var bootstrapAfterThirdLoad = RequireBootstrap();

            var hostAfterReloads = UnityEngineHost.Ensure();
            Assert.AreSame(hostAfterFirstLoad, hostAfterReloads, "UnityEngineHost 应当跨场景重进保持单例（DontDestroyOnLoad）");

            var allHosts = Object.FindObjectsByType<UnityEngineHost>(FindObjectsSortMode.None);
            Assert.AreEqual(1, allHosts.Length, "场景重进不应产生重复的 UnityEngineHost 实例");

            yield return new WaitForFixedUpdate();
            Assert.IsFalse(bootstrapAfterThirdLoad.BootstrapFailed, "第三次进入灰盒场景仍应正常装配成功");
        }

        /// <summary>
        /// 引擎侧收口任务验收 1："固定步驱动改走 IClock.RequestFixedStep + GameplayAssembly.Advance
        /// ……退订能退订……PlayMode 测试：退订后不再 tick；两次进图不重复 tick"。
        /// <para>
        /// 判断记录（用"两次重进后的稳态值是否一致"而不是"相对某个预先捕获的基线净增 1"）：
        /// 本套件内每条用例之间没有显式清场（<see cref="TearDown"/> 判断记录"不额外清理"），前一条
        /// 用例遗留的 <see cref="GameFoundationBootstrap"/> 实例在本用例开始时可能仍然存活、仍然
        /// 持有一份固定步/帧回调注册——本用例开始时先捕获的"基线"因此已经把它算在内，"加载一次
        /// 就该恰好 +1"这个假设在"进入本用例前 GreyBox 场景已经处于加载状态"时不成立（早期版本
        /// 用这条假设断言，实测因执行顺序偶发失败："Expected: 基线+1, But was: 基线"——恰好等于
        /// 未变化的基线，说明"卸载旧实例退订 -1、加载新实例注册 +1"两者相抵，而不是真的没有退订）。
        /// 改为不依赖"进入本用例前是什么状态"这一假设：连续两次重进 GreyBox 场景，分别记录重进
        /// 后的注册数，只要两次数值相等（不随重进次数累积增长），就证明每次重进都完整地"先退订
        /// 旧的、再注册新的"，没有累积泄漏——这是"两次进图不重复 tick"真正要验证的性质，且不依赖
        /// 执行顺序/前序用例残留状态。
        /// </para>
        /// </summary>
        [UnityTest]
        public IEnumerator ReenterScene_FixedStepAndFrameRegistrationCounts_StayConstantAcrossReloads()
        {
            var host = UnityEngineHost.Ensure();

            yield return LoadGreyBoxScene();
            RequireBootstrap();
            var fixedStepAfterFirstLoad = host.Clock.FixedStepRegistrationCount;
            var frameAfterFirstLoad = host.Clock.FrameRegistrationCount;

            yield return LoadGreyBoxScene();
            RequireBootstrap();
            var fixedStepAfterSecondLoad = host.Clock.FixedStepRegistrationCount;
            var frameAfterSecondLoad = host.Clock.FrameRegistrationCount;

            yield return LoadGreyBoxScene();
            RequireBootstrap();
            var fixedStepAfterThirdLoad = host.Clock.FixedStepRegistrationCount;
            var frameAfterThirdLoad = host.Clock.FrameRegistrationCount;

            Assert.AreEqual(fixedStepAfterFirstLoad, fixedStepAfterSecondLoad,
                "重进场景后固定步注册数不应比上一次多（旧句柄未被正确退订会逐次累加）");
            Assert.AreEqual(fixedStepAfterFirstLoad, fixedStepAfterThirdLoad,
                "第三次重进场景后固定步注册数仍不应累加");
            Assert.AreEqual(frameAfterFirstLoad, frameAfterSecondLoad,
                "重进场景后帧回调注册数不应比上一次多（旧句柄未被正确退订会逐次累加）");
            Assert.AreEqual(frameAfterFirstLoad, frameAfterThirdLoad,
                "第三次重进场景后帧回调注册数仍不应累加");
        }

        /// <summary>
        /// 引擎侧收口任务验收 1 的另一半："两次进图不重复 tick"——用实际推进量而不是只看注册数
        /// 佐证：玩家移动若干个固定步后记下位置，再次重进场景（新世界、新玩家，从 Vec2.Zero 重新
        /// 出发）后同样移动同样多个固定步，两次位移量应当一致；若旧句柄未退订导致每个物理步都
        /// 触发两次（甚至更多次）<c>GameplayAssembly.Advance</c>，第二次的位移量会明显偏大。
        /// </summary>
        [UnityTest]
        public IEnumerator ReenterScene_MovementDistancePerFixedStep_IsConsistentAcrossReloads()
        {
            yield return LoadGreyBoxScene();
            var firstBootstrap = RequireBootstrap();
            var startX1 = firstBootstrap.World!.GetEntity(firstBootstrap.PlayerId)!.Position.X;

            // 判断记录：Core.Carriers.Unit.MovementHost.Request 内部把方向移动编码为
            // {dx,dy,mode}（见该类型源码），不是 UiIntents.Move 手写的 {dir_x,dir_y}——本用例改用
            // 与 VerticalSliceTests/ShellFlowTests 同一惯例的窄契约 Carriers.Movement.Request，
            // 不自行拼 Intent 参数，避免字段名假设与真实契约不一致（早期版本直接手写
            // {dir_x,dir_y}，实测因字段名不对，MovementTickHandler 读不到方向、玩家全程不移动，
            // deltaX 恒为 0，与"退订未生效导致重复 tick"无关，是本用例自己的用法错误）。
            const int steps = 20;
            for (var i = 0; i < steps; i++)
            {
                firstBootstrap.Gameplay!.Carriers.Movement.Request(MoveRequest.InDirection(firstBootstrap.PlayerId, new Vec2(1, 0)));
                yield return new WaitForFixedUpdate();
            }
            var deltaX1 = firstBootstrap.World.GetEntity(firstBootstrap.PlayerId)!.Position.X - startX1;
            Assert.Greater(deltaX1, 0.0, "第一次进图移动后玩家 X 坐标应当增大");

            yield return LoadGreyBoxScene();
            var secondBootstrap = RequireBootstrap();
            var startX2 = secondBootstrap.World!.GetEntity(secondBootstrap.PlayerId)!.Position.X;

            for (var i = 0; i < steps; i++)
            {
                secondBootstrap.Gameplay!.Carriers.Movement.Request(MoveRequest.InDirection(secondBootstrap.PlayerId, new Vec2(1, 0)));
                yield return new WaitForFixedUpdate();
            }
            var deltaX2 = secondBootstrap.World.GetEntity(secondBootstrap.PlayerId)!.Position.X - startX2;

            // 允许浮点/首尾半步的小误差，但不允许"翻倍"这类量级差异（旧句柄未退订会导致同一份
            // 移动意图在同一个物理步内被多个仍然存活的 Advance 回调重复消费/结算）。
            Assert.AreEqual(deltaX1, deltaX2, deltaX1 * 0.2,
                $"两次进图、同样步数的移动位移应当基本一致（第一次 {deltaX1}，第二次 {deltaX2}），" +
                "明显偏大说明旧场景的固定步回调未被正确退订、发生了重复 tick");
        }

        /// <summary>
        /// 引擎侧收边任务验收：<see cref="GameFoundationBootstrap.OnFixedStep"/> 新增的
        /// <c>AppState == InWorld</c> 门槛（见该方法判断记录，与
        /// <c>Adapter.Unity.Shell.FrameworkResidentHost.OnFixedStep</c> 对齐）——本用例直接断言
        /// 门槛本身生效，与
        /// <c>SharedBootstrapDiscreteTests.SharedBootstrap_EndTurnKeyBinding_StillWorks_WithStaleSceneBootstrapLeftAlive</c>
        /// （验证"即便场景里残留另一个实例，真正活跃的实例仍能正常工作"）互补：把场景里唯一的
        /// <see cref="GameFoundationBootstrap"/> 实例本身手工切出 InWorld（效果等价于"一个已经
        /// 离开 InWorld 但仍残留在场景里继续跑固定步的引导实例"），验证此时真实按下移动键
        /// （<c>'d'</c>，绑定 <c>input.action.move</c> 的 <c>composite2d</c> 右方向子键，见
        /// <c>data/_framework/found/found.input_action.json</c>）不会消费共享输入队列——玩家
        /// 位置不应移动；切回 InWorld 后同一份按键立即恢复生效，证明门槛只是"暂停消费"而不是
        /// 输入队列或世界状态被破坏。
        /// </summary>
        [UnityTest]
        public IEnumerator OnFixedStep_SkipsInputConsumption_WhenAppStateNotInWorld()
        {
            yield return LoadGreyBoxScene();
            var bootstrap = RequireBootstrap();

            // BuildWorld 第 9 步（引擎侧收边任务新增）已自驱 AppState 走到 InWorld，见该方法
            // 判断记录："本类型是灰盒场景唯一挂载的组件，没有外部菜单/读档流程，需要自己驱动
            // Boot->MainMenu->Loading->InWorld 这条链路"。
            Assert.AreEqual(
                Core.Foundation.AppLifecycle.AppState.InWorld, bootstrap.Gameplay!.AppState.GetState(),
                "灰盒场景自身的 GameFoundationBootstrap 装配完成后应已自驱到 InWorld");

            var input = UnityEngineHost.Ensure().Input as UnityInput;
            Assert.IsNotNull(input, "UnityEngineHost.Input 应当是 UnityInput 实现（SimulateKeyForTest 所在类型）");

            // 手工把它切出 InWorld（legal InWorld->MainMenu 转移，见 AppStateMachineConfig.Default()），
            // 模拟"应用已经离开 InWorld，但引导实例仍残留在场景里继续跑固定步"这一场景。
            bootstrap.Gameplay.AppState.RequestTransition(Core.Foundation.AppLifecycle.AppState.MainMenu);
            Assert.AreEqual(Core.Foundation.AppLifecycle.AppState.MainMenu, bootstrap.Gameplay.AppState.GetState());

            var positionBeforeNonInWorld = bootstrap.World!.GetEntity(bootstrap.PlayerId)!.Position;

            // 持续按住向右移动键若干个固定步——若 OnFixedStep 未能正确跳过
            // Presentation.InputMap.Update/HandleFixedInput，这里玩家会明显向 +X 移动
            // （同 Move_Right_IncreasesPlayerX_AndTurnsSideways 的持续提交惯例，改用真实按键而不是
            // 直接调用 Carriers.Movement.Request，覆盖 InputMap.Update 这一步本身）。
            for (var i = 0; i < 15; i++)
            {
                input!.SimulateKeyForTest("d", down: true);
                yield return new WaitForFixedUpdate();
            }
            input!.SimulateKeyForTest("d", down: false);
            yield return new WaitForFixedUpdate();

            var positionAfterNonInWorld = bootstrap.World!.GetEntity(bootstrap.PlayerId)!.Position;
            Assert.AreEqual(positionBeforeNonInWorld.X, positionAfterNonInWorld.X, 0.0001,
                "AppState 非 InWorld 期间，OnFixedStep 应跳过 Presentation.InputMap.Update/" +
                "HandleFixedInput，真实按键输入不应移动玩家");
            Assert.AreEqual(positionBeforeNonInWorld.Y, positionAfterNonInWorld.Y, 0.0001);

            // 切回 InWorld（MainMenu->Loading->InWorld，同 AppStateMachineConfig.Default() 登记的
            // 合法转移），确认同一份按键立即恢复生效——证明门槛只是暂停消费，不是输入队列或世界
            // 状态被破坏。
            bootstrap.Gameplay.AppState.RequestTransition(Core.Foundation.AppLifecycle.AppState.Loading);
            bootstrap.Gameplay.AppState.RequestTransition(Core.Foundation.AppLifecycle.AppState.InWorld);
            Assert.AreEqual(Core.Foundation.AppLifecycle.AppState.InWorld, bootstrap.Gameplay.AppState.GetState());

            for (var i = 0; i < 15; i++)
            {
                input!.SimulateKeyForTest("d", down: true);
                yield return new WaitForFixedUpdate();
            }
            input!.SimulateKeyForTest("d", down: false);
            yield return new WaitForFixedUpdate();

            var positionAfterResume = bootstrap.World!.GetEntity(bootstrap.PlayerId)!.Position;
            Assert.Greater(positionAfterResume.X, positionAfterNonInWorld.X,
                "回到 InWorld 后，同一份移动按键应当恢复生效——证明门槛不是永久性失效");
        }
    }
}
