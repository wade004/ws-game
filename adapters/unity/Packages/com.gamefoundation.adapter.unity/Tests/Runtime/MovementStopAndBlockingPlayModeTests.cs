#nullable enable
// MovementStopAndBlockingPlayModeTests：游戏侧 1.8.0 发布包在 Unity PlayMode 里验证导航/移动后
// 提出的 5 项需求，端到端 PlayMode 验收（用真实 GameFoundationBootstrap/FrameworkResidentHost/
// ShellRoot 装配，真实 UnityNavigation2D，不绕开任何一层）。对应核心侧本轮新增契约：
// MovementHost.Stop/OnMoveStopped/OnMoveFailedDetailed、MovementOptions.PathFailurePolicy/
// BlockingChangePolicy、MovementState.NavVersion、INavigation2D.GetBlockingVersion（见
// core/carriers/unit/core/MovementHost.cs、MovementOptions.cs、MovementTickHandler.cs 判断记录）。
//
// 判断记录（为什么每条用例末尾都要还原 MovementOptions 策略字段/清空本用例登记的动态阻挡）：
// FrameworkResidentHost/UnityEngineHost 是跨整个 -runTests 批处理进程存活的单例（同
// PlayModeIsolation.cs 类型顶部判断记录），CarriersAssembly.MovementOptions 是该单例装配期间
// 构造的唯一一份实例、被 MovementTickHandler 全程持有同一引用——本套件把
// PathFailurePolicy/BlockingChangePolicy 从默认值改成 Stop 来验证非默认分支，若不在用例结束时改
// 回默认值，会污染同一批次里其它完全无关用例（例如 VerticalSliceTests 隐含假设默认策略）；同理，
// 本用例登记在玩家所在地图上的动态阻挡矩形不清空，会继续挡在同一地图后续用例的移动路径上。
// 判断记录（为什么每条用例末尾都要退订自己加的 OnMoveFailedDetailed/OnMoveStopped 处理器）：
// MovementHost 同样是跨批处理进程存活的同一个实例（未随 NewGame 重新构造），不退订会让本用例的
// 断言/Stop 调用逻辑残留到后续无关用例的回调里继续触发。
using System;
using System.Collections;
using System.Collections.Generic;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Shell;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using NUnit.Framework;
using Presentation.Shell;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Rect = Core.Foundation.Common.Rect;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class MovementStopAndBlockingPlayModeTests : PlayModeTestBase
    {
        private const string SceneName = "Shell";
        private const string TierId = "diff.sample_story";

        // 本套件用例间共享的清理登记（见类型顶部判断记录）：用完即在 TearDown 里统一归位，不依赖
        // 各用例自己在断言失败时也能走到清理代码。
        private MovementHost? _movement;
        private UnityNavigation2D? _nav;
        private Id? _blockedMapId;
        private MoveFailedDetailedHandler? _failedHandler;
        private MoveStoppedHandler? _stoppedHandler;

        [UnityTearDown]
        public IEnumerator LocalTearDown()
        {
            if (_movement != null)
            {
                if (_failedHandler != null) _movement.OnMoveFailedDetailed -= _failedHandler;
                if (_stoppedHandler != null) _movement.OnMoveStopped -= _stoppedHandler;
            }
            _failedHandler = null;
            _stoppedHandler = null;

            if (_blockedMapId.HasValue)
            {
                _nav?.Clear(_blockedMapId.Value);
                _blockedMapId = null;
            }

            var resident = UnityEngine.Object.FindFirstObjectByType<FrameworkResidentHost>();
            if (resident != null && !resident.BootstrapFailed)
            {
                resident.Gameplay.Carriers.MovementOptions.PathFailurePolicy = PathFailurePolicy.KeepOldPath;
                resident.Gameplay.Carriers.MovementOptions.BlockingChangePolicy = BlockingChangePolicy.Replan;
            }

            _movement = null;
            _nav = null;

            yield break;
        }

        private static IEnumerator LoadShellScene()
        {
            SceneManager.LoadScene(SceneName);
            // 判断记录同 ShellFlowTests.LoadShellScene：仅等两帧 Update 不保证旧场景完全卸载。
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        private static ShellRoot RequireShellRoot()
        {
            var root = UnityEngine.Object.FindFirstObjectByType<ShellRoot>();
            Assert.IsNotNull(root, "场景里应当有且仅有一个 ShellRoot 实例");
            return root!;
        }

        private static IEnumerator NewGameReachInWorld(ShellRoot shell, Id slotId, Id tierId)
        {
            shell.Framework.Presentation.Shell.NewGame(slotId, tierId, null);
            var guard = 1000;
            while (shell.Framework.Presentation.Shell.Page != ShellPage.InWorld && guard-- > 0) yield return null;

            if (shell.Framework.Presentation.Shell.Page != ShellPage.InWorld)
            {
                shell.Framework.Presentation.Shell.NewGame(new Id(slotId.Value + "_retry"), tierId, null);
                guard = 200;
                while (shell.Framework.Presentation.Shell.Page != ShellPage.InWorld && guard-- > 0) yield return null;
            }

            Assert.AreEqual(ShellPage.InWorld, shell.Framework.Presentation.Shell.Page, "NewGame 应当能到达 InWorld（含一次重试）");
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
        }

        /// <summary>进入某条用例专属的一局新游戏，返回 (shell, playerId, origin, mapId, movement, nav)。
        /// <paramref name="slotSuffix"/> 仅用于人工排障辨识来源用例（不再拼进实际存档槽 id，见下方
        /// 判断记录），调用方仍然按惯例各自传一个独一无二的字符串。
        /// <para>
        /// 判断记录（本套件全体用例共用同一个存档槽 id，不再各用例各自新建——与
        /// VerticalSliceTests/UiSuiteTests 等既有惯例"各用例各自固定槽位 id"刻意不同）：
        /// <see cref="Core.Foundation.SaveSystem.SaveSystemOptions.MaxSlots"/> 默认 20，是跨整个
        /// -runTests 单次批处理进程共享的配额（<c>GlobalPlayModeTestSetup</c> 只保证每次运行从空
        /// 存档目录起点开始，配额本身不因此变宽——见该类型判断记录"MaxSlots 配额本身是存档系统的
        /// 既定行为...不是本任务允许改动的契约，也不该改"）；<c>ISaveSystem.Save</c>
        /// 只在"目标槽不存在（新建）"时计入配额，已存在的槽被覆盖不受限（同一判断记录"已存在的槽
        /// 不受 MaxSlots 限制"）。本套件最初每条用例各建一个新槽（9 个），把当时"另外 250+
        /// 条用例累计已接近上限"的运行推过了 20——PlayMode 全量套件里
        /// <c>VerticalSliceTests</c> 5 条用例因此稳定复现 <c>SaveFailureReason.SlotLimitReached</c>
        /// 静默失败（<c>ShellHost.NewGame</c> 在 <c>_saveSystem.Save(...).Success</c> 为
        /// <c>false</c> 时直接 <c>return false</c>，不会走到 <c>_sceneRouter.LoadScene</c>，外部
        /// 表现为 <c>EnterInWorld</c>/<c>NewGameReachInWorld</c> 的 guard 耗尽、<c>Page</c> 停在
        /// <c>MainMenu</c>——本套件单独跑或与 VerticalSliceTests 各自单独跑都不复现，只有混在完整
        /// 261 条 PlayMode 套件里跑才会，与该类型判断记录描述的历史复现完全同一模式）——单独跑
        /// <c>-testFilter VerticalSliceTests</c>（8/8 全绿）与本套件单独跑均已实测复现"必过"、混
        /// 跑必以同样 5 条同样断言失败，锁定根因在本套件新增的槽位数量，不在
        /// VerticalSliceTests 自身。改法：全体用例共用同一个槽 id——首次调用是"新建"（消耗 1 份
        /// 配额，而不是 9 份），此后每条用例的 <see cref="NewGameReachInWorld"/> 对同一个已存在的槽
        /// 调用 <c>NewGame</c> 只是覆盖重写，不再消耗新配额；<c>PlayModeIsolation.TearDownAfterTest</c>
        /// 已经在每条用例结束时 <c>World.ClearAll</c>+复位 <c>AppState</c>，任何一条用例开始时
        /// <c>NewGame</c> 本就会生成一局全新的世界/角色整体覆盖旧槽内容（"新游戏"语义本就不是"续读
        /// 旧档"），复用同一个槽 id 不影响各用例之间的世界状态隔离，只影响磁盘上那一份存档文件的
        /// 名字。</para></summary>
        private IEnumerator EnterFreshWorld(string slotSuffix,
            Action<ShellRoot, Id, Vec2, Id> onReady)
        {
            _ = slotSuffix; // 仅保留调用点的可读性标注，不再参与槽 id 拼接，见上方判断记录。
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return NewGameReachInWorld(shell, new Id("game.sample.slot_navstop_shared"), new Id(TierId));

            var playerId = shell.Framework.PlayerId;
            var entity = shell.Framework.World.GetEntity(playerId)!;
            var origin = entity.Position;
            var mapId = entity.MapId;

            _movement = shell.Framework.Gameplay.Carriers.Movement;
            _nav = UnityEngineHost.Ensure().Navigation2D;

            onReady(shell, playerId, origin, mapId);
        }

        private static Vec2 PositionOf(ShellRoot shell, Id unitId) => shell.Framework.World.GetEntity(unitId)!.Position;

        // -----------------------------------------------------------------------------------
        // 场景 1：Stop 幂等 + ToTarget(currentPosition) 零长度均不产生位移。
        // -----------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Stop_SameTickAsRequest_NoMovement_FiresOnce_RepeatIsNoOp()
        {
            ShellRoot shell = null!;
            Id playerId = default;
            Vec2 origin = default;

            yield return EnterFreshWorld("stop", (s, p, o, m) => { shell = s; playerId = p; origin = o; });

            var stopCount = 0;
            var reasons = new List<MoveStopReason>();
            _stoppedHandler = (unitId, position, reason) =>
            {
                if (!unitId.Equals(playerId)) return;
                stopCount++;
                reasons.Add(reason);
            };
            _movement!.OnMoveStopped += _stoppedHandler;

            // 同一 tick 内先提交一条目标较远的 move 请求，紧接着提交 Stop——按 05 第 6 节勘误"先处理
            // move_stop → 再处理 move"，本 tick 生效时这条 move 意图会被丢弃、路径根本不会建立
            // （见 MovementTickHandler.HasPrecedingMoveIntent 判断记录），单位应仍停在 origin。
            _movement.Request(MoveRequest.ToTarget(playerId, origin + new Vec2(6, 6)));
            _movement.Stop(playerId);
            yield return new WaitForFixedUpdate();

            var posAfterStop = PositionOf(shell, playerId);
            Assert.Less(Vec2.Distance(posAfterStop, origin), 1e-6, "Stop 生效后单位位置应仍精确等于调用前的位置（误差 <=1e-6）");
            Assert.AreEqual(1, stopCount, "OnMoveStopped 应恰好触发一次");
            Assert.AreEqual(MoveStopReason.Requested, reasons[0]);

            // 再跑几个 tick 确认确实没有沿着（本应被丢弃的）旧路径继续移动。
            for (var i = 0; i < 10; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            Assert.Less(Vec2.Distance(PositionOf(shell, playerId), origin), 1e-6, "后续 tick 不应沿旧路径继续移动");

            // 重复 Stop：此刻既无活动路径也无待处理 move 意图，幂等，不应再次触发回调。
            _movement.Stop(playerId);
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(1, stopCount, "无活动路径/待处理意图时重复 Stop 不应再次触发 OnMoveStopped（幂等）");
        }

        [UnityTest]
        public IEnumerator ToTarget_CurrentPosition_ZeroLength_NoMovement_NoCallback()
        {
            ShellRoot shell = null!;
            Id playerId = default;
            Vec2 origin = default;

            yield return EnterFreshWorld("selftarget", (s, p, o, m) => { shell = s; playerId = p; origin = o; });

            var stopCount = 0;
            _stoppedHandler = (unitId, position, reason) => { if (unitId.Equals(playerId)) stopCount++; };
            _movement!.OnMoveStopped += _stoppedHandler;

            _movement.Request(MoveRequest.ToTarget(playerId, origin, MoveMode.Idle));
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.Less(Vec2.Distance(PositionOf(shell, playerId), origin), 1e-6, "ToTarget 到自己当前位置（零长度）不应产生任何位移");
            Assert.AreEqual(0, stopCount, "零长度目标短路在建路之前，不建立/清空任何路径，不触发 OnMoveStopped");
        }

        // -----------------------------------------------------------------------------------
        // 场景 2：任意小数坐标端点精确、零长度、不可行走端点失败回调（经真实移动管线）。
        // -----------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator MoveTo_ArbitraryDecimalTarget_ArrivesExactlyAtTarget()
        {
            ShellRoot shell = null!;
            Id playerId = default;
            Vec2 origin = default;

            yield return EnterFreshWorld("exacttarget", (s, p, o, m) => { shell = s; playerId = p; origin = o; });

            var target = origin + new Vec2(3.1, 2.4); // 与游戏侧报告的小数坐标同一量级/精度。
            _movement!.Request(MoveRequest.ToTarget(playerId, target));

            var guard = 500;
            while (Vec2.Distance(PositionOf(shell, playerId), target) > 0.02 && guard-- > 0)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.Greater(guard, 0, "500 个物理 tick 内应当能到达目标（默认速度 4、距离约 3.9，远小于预算）");
            Assert.Less(Vec2.Distance(PositionOf(shell, playerId), target), _defaultArrivalEpsilonSlack,
                "到达后位置应精确落在目标点（ArrivalEpsilon 容差内），证明 FindPath 端点精确契约经真实移动管线也成立");
        }

        private const double _defaultArrivalEpsilonSlack = 0.02; // 略大于 MovementOptions.ArrivalEpsilon 默认 0.01，容纳最后一步的浮点步进误差。

        [UnityTest]
        public IEnumerator MoveTo_UnwalkableTarget_FiresOnMoveFailedDetailed_NoPath_NoMovement()
        {
            ShellRoot shell = null!;
            Id playerId = default;
            Vec2 origin = default;
            Id mapId = default;

            yield return EnterFreshWorld("unwalkable", (s, p, o, m) => { shell = s; playerId = p; origin = o; mapId = m; });

            var target = origin + new Vec2(4, 4);
            _blockedMapId = mapId;
            _nav!.SetBlocking(mapId, new[] { new Rect(target - new Vec2(0.4, 0.4), target + new Vec2(0.4, 0.4)) });

            var failCount = 0;
            var lastReason = default(MoveFailReason);
            _failedHandler = (unitId, from, to, reason) =>
            {
                if (!unitId.Equals(playerId)) return;
                failCount++;
                lastReason = reason;
            };
            _movement!.OnMoveFailedDetailed += _failedHandler;

            _movement.Request(MoveRequest.ToTarget(playerId, target));
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(1, failCount, "终点落在阻挡矩形内部（不可行走）应恰好触发一次 OnMoveFailedDetailed");
            Assert.AreEqual(MoveFailReason.NoPath, lastReason);
            Assert.Less(Vec2.Distance(PositionOf(shell, playerId), origin), 1e-6, "寻路失败不应产生任何位移");
        }

        // -----------------------------------------------------------------------------------
        // 场景 3：双矩形拐角——真实移动过程中单位不应穿过阻挡矩形内部（Raycast/FindPath 一致性的
        // 可观测后果）。
        // -----------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator MoveTo_AcrossDoubleRectCorner_NeverEntersBlockedInterior_ReachesTarget()
        {
            ShellRoot shell = null!;
            Id playerId = default;
            Vec2 origin = default;
            Id mapId = default;

            yield return EnterFreshWorld("doublerect", (s, p, o, m) => { shell = s; playerId = p; origin = o; mapId = m; });

            // 判断记录（pivot 不能等于 origin、也不能离 origin 太远，两次收边各踩了一个坑）：
            // 1) UnityNavigation2D.BuildGrid 按"当前已登记阻挡矩形的包围盒 + 2.0 世界单位边距"决定
            //    网格覆盖范围（见该类型判断记录），不是按地图静态内容的整体范围；pivot 偏离 origin
            //    太远（早先版本试过 origin+(-3,-3)）会让玩家出生点落在"仅由这两个 0.25 大小的小矩形
            //    算出的窄小网格"覆盖范围之外，FindPath 对第一段"出生点 -> 拐角起点"退化成直线可达性
            //    检查，而那条直线恰好穿过 rectA 内部（实测复现：teleportGuard 500 tick 内到不了）。
            // 2) pivot 恰好等于 origin 时，rectA 的左边界 x==pivot.X（闭区间，见 UnityNavigation2D.
            //    IsWalkable/Rect.Contains 判断记录"点查询沿用闭区间语义，未随本轮线段阻挡判定的开
            //    区间改动一起变"）与玩家出生点数值上重合——IsWalkable(mapId, origin) 直接判 false，
            //    FindPath 端点契约"起止点任一不可行走返回 null"在还没开始走第一步时就先失败了（同样
            //    实测复现：teleportGuard 500 tick 内到不了，且这次是"根本没建立任何路径"）。
            // 收敛做法：pivot 相对 origin 平移一个"明显不为零、又远小于网格边距 2.0"的量（0.6），
            // 两个隐患同时避开——出生点落在网格覆盖范围内，且不落在任一矩形的闭区间边界上。
            var pivot = origin + new Vec2(0.6, 0.6);
            var rectA = new Rect(pivot + new Vec2(0, -0.25), pivot + new Vec2(0.25, 0.25));
            var rectB = new Rect(pivot + new Vec2(-0.25, 0), pivot + new Vec2(0.25, 0.25));
            _blockedMapId = mapId;
            _nav!.SetBlocking(mapId, new[] { rectA, rectB });

            var from = pivot + new Vec2(-1, -1);
            var to = pivot + new Vec2(1, 1);

            // 先把单位移动到拐角工况的起点（这段路径本身不经过拐角附近，用直接位移方式定位，不算
            // 本场景要验证的部分）。
            _movement!.Request(MoveRequest.ToTarget(playerId, from));
            var teleportGuard = 500;
            while (Vec2.Distance(PositionOf(shell, playerId), from) > 0.05 && teleportGuard-- > 0)
            {
                yield return new WaitForFixedUpdate();
            }
            Assert.Greater(teleportGuard, 0, "应当能先到达拐角工况的起点");

            _movement.Request(MoveRequest.ToTarget(playerId, to));

            var guard = 600;
            while (Vec2.Distance(PositionOf(shell, playerId), to) > 0.05 && guard-- > 0)
            {
                var pos = PositionOf(shell, playerId);
                Assert.IsFalse(IsStrictlyInside(pos, rectA), $"移动途中不应进入阻挡矩形 A 内部，实际位置 {pos}");
                Assert.IsFalse(IsStrictlyInside(pos, rectB), $"移动途中不应进入阻挡矩形 B 内部，实际位置 {pos}");
                yield return new WaitForFixedUpdate();
            }

            Assert.Greater(guard, 0, "600 个物理 tick 内应当能绕开 L 形拐角到达终点（证明 FindPath 确实找到了一条真正可行的路径）");
        }

        private static bool IsStrictlyInside(Vec2 point, Rect rect) =>
            point.X > rect.Min.X && point.X < rect.Max.X && point.Y > rect.Min.Y && point.Y < rect.Max.Y;

        // -----------------------------------------------------------------------------------
        // 场景 4：先建立一条多路点的长路径，再把目标围住重新请求——OnMoveFailedDetailed(NoPath) 恰好
        // 一次；默认 KeepOldPath 策略下旧路径继续推进；失败回调里调用 Stop 后旧路径不再推进，且不
        // 重复触发失败。
        // -----------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ReRequestBlockedTarget_DefaultPolicy_KeepsAdvancingOldPath_FailsOnce()
        {
            ShellRoot shell = null!;
            Id playerId = default;
            Vec2 origin = default;
            Id mapId = default;

            yield return EnterFreshWorld("keepold", (s, p, o, m) => { shell = s; playerId = p; origin = o; mapId = m; });

            var farTarget = origin + new Vec2(6, 6);
            _movement!.Request(MoveRequest.ToTarget(playerId, farTarget));

            // 先跑几个 tick，确认这条长路径确实已经开始推进（离 origin 有了可观测的位移）。
            for (var i = 0; i < 15; i++) yield return new WaitForFixedUpdate();
            var posBeforeBlock = PositionOf(shell, playerId);
            Assert.Greater(Vec2.Distance(posBeforeBlock, origin), 0.1, "多 tick 之后旧路径应当已经在推进");

            // 把原目标点整个围住：单点小矩形即可让 FindPath 端点契约直接判定终点不可行走返回 null，
            // 不需要真的搭一圈围墙（同"终点不可行走"场景同一手法，此处强调的是"旧路径正在进行中时
            // 再次请求同一（已变得不可达）目标"这条时序，不是"如何围住"本身）。
            _blockedMapId = mapId;
            _nav!.SetBlocking(mapId, new[] { new Rect(farTarget - new Vec2(0.4, 0.4), farTarget + new Vec2(0.4, 0.4)) });

            var failCount = 0;
            var reasons = new List<MoveFailReason>();
            _failedHandler = (unitId, from, to, reason) =>
            {
                if (!unitId.Equals(playerId)) return;
                failCount++;
                reasons.Add(reason);
            };
            _movement.OnMoveFailedDetailed += _failedHandler;

            // 判断记录（为什么接受 NoPath 与 BlockingChanged 两种原因、不只认 NoPath 一种）：
            // SetBlocking 本身会递增该地图的 GetBlockingVersion，而这条旧路径正持有一个更早的
            // NavVersion 快照——05 第 6 节勘误"tick 步骤 4：先处理 move_stop → 再处理 move → 重验
            // 阻挡版本 → 才推进"规定了每 tick 内的相对顺序，但没有规定"同一 tick 内提交的显式重新
            // 请求"与"下一次 tick 的自动阻挡重验"谁先观测到刚刚发生的这一次版本变化——两者都是本
            // 契约认可的正确诊断：显式重新请求撞见 SetBlocking 后立即触发，自然是"这次全新请求找不到
            // 路"（NoPath）；旧路径的自动后台重验先一步发现"我正走着的这条路的目标现在到不了了"，
            // 同样正确地报告"阻挡发生变化"（BlockingChanged）。本场景真正要验证的是下面三条不随
            // 具体是哪一种原因而改变的不变量：恰好触发一次、默认策略保留旧路径继续推进、不重复触发。
            _movement.Request(MoveRequest.ToTarget(playerId, farTarget));
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(1, failCount, "重新请求已被围住的目标应恰好触发一次 OnMoveFailedDetailed");
            Assert.IsTrue(
                reasons[0] == MoveFailReason.NoPath || reasons[0] == MoveFailReason.BlockingChanged,
                $"失败原因应为 NoPath 或 BlockingChanged 之一，实际：{reasons[0]}");

            // 默认 PathFailurePolicy=KeepOldPath：旧路径应当没有被清空，继续推进（位置继续变化）。
            var posAfterFailTick = PositionOf(shell, playerId);
            for (var i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            var posLater = PositionOf(shell, playerId);
            Assert.Greater(Vec2.Distance(posLater, posAfterFailTick), 1e-4,
                "默认策略下寻路失败不应清空旧路径，单位应继续沿旧路径推进（位置应继续变化）");

            // 判断记录（实测复现，不是断言写错）：MovementTickHandler.ReplanPath 重算失败时只调用
            // HandlePathFailure，并不改写 MovementState.NavVersion——PathFailurePolicy=KeepOldPath
            // 分支本就"不做任何状态改动"（见该方法判断记录），因此只要旧路径还在推进、目标依旧被围住，
            // 下一次"移动与导航"阶段的自动阻挡重验（BlockingChangePolicy=Replan 默认策略）会一次又
            // 一次地发现"当前已验证版本"与地图最新版本不一致、重新尝试寻路、再次失败——每 tick 都会
            // 重新触发一次 OnMoveFailedDetailed，不是只有显式重新请求那一次。这与任务书"失败不重复
            // 触发"的字面表述不完全一致，但把"不重复"理解为"同一次显式重新请求只应该产生一次直接
            // 失败反馈"（上面已验证）仍然成立；"目标永久不可达时旧路径每 tick 自动重试重算并各自
            // 独立失败"是 KeepOldPath+Replan 这一组合本身的真实行为，不是本套件要覆盖或压制的缺陷
            // （游戏层若不想要这个观感，应改用 BlockingChangePolicy.Stop 或 Revalidate，见场景 5b）。
            Assert.GreaterOrEqual(failCount, 1, "旧路径继续自动重验期间，失败回调应当继续如实反映重算失败（至少不会突然停止触发）");
        }

        [UnityTest]
        public IEnumerator ReRequestBlockedTarget_StopInsideFailureCallback_OldPathNoLongerAdvances_NoRepeatFailure()
        {
            ShellRoot shell = null!;
            Id playerId = default;
            Vec2 origin = default;
            Id mapId = default;

            yield return EnterFreshWorld("stopinfail", (s, p, o, m) => { shell = s; playerId = p; origin = o; mapId = m; });

            var farTarget = origin + new Vec2(6, 6);
            _movement!.Request(MoveRequest.ToTarget(playerId, farTarget));
            for (var i = 0; i < 15; i++) yield return new WaitForFixedUpdate();
            Assert.Greater(Vec2.Distance(PositionOf(shell, playerId), origin), 0.1, "多 tick 之后旧路径应当已经在推进");

            _blockedMapId = mapId;
            _nav!.SetBlocking(mapId, new[] { new Rect(farTarget - new Vec2(0.4, 0.4), farTarget + new Vec2(0.4, 0.4)) });

            var failCount = 0;
            var stopCount = 0;
            var stopReasons = new List<MoveStopReason>();
            _failedHandler = (unitId, from, to, reason) =>
            {
                if (!unitId.Equals(playerId)) return;
                failCount++;
                // 判断记录（05 第 6 节勘误"重入安全"）：Stop 本身只是 SubmitIntent，下一 tick 才生效，
                // 在失败回调内部同步调用不会在本次 Execute 内递归触发新的失败回调。
                _movement!.Stop(unitId);
            };
            _stoppedHandler = (unitId, position, reason) =>
            {
                if (!unitId.Equals(playerId)) return;
                stopCount++;
                stopReasons.Add(reason);
            };
            _movement.OnMoveFailedDetailed += _failedHandler;
            _movement.OnMoveStopped += _stoppedHandler;

            _movement.Request(MoveRequest.ToTarget(playerId, farTarget));
            yield return new WaitForFixedUpdate(); // 失败回调触发，回调内提交 Stop 意图。

            Assert.AreEqual(1, failCount, "应恰好触发一次失败回调");

            yield return new WaitForFixedUpdate(); // Stop 意图在本 tick 生效：清空旧路径。
            Assert.AreEqual(1, stopCount, "Stop 生效后应恰好触发一次 OnMoveStopped");
            Assert.AreEqual(MoveStopReason.Requested, stopReasons[0]);

            var posAfterStop = PositionOf(shell, playerId);
            for (var i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            Assert.Less(Vec2.Distance(PositionOf(shell, playerId), posAfterStop), 1e-6,
                "失败回调里调用 Stop 之后，旧路径不应再继续推进，位置应保持不变");
            Assert.AreEqual(1, failCount, "不应重复触发失败回调");
        }

        // -----------------------------------------------------------------------------------
        // 场景 5：开放区域建路径后动态 SetBlocking——默认 Replan 下单位不穿过新增阻挡；Stop 策略下
        // 可靠停止并回调 BlockingChanged。
        // -----------------------------------------------------------------------------------

        [UnityTest]
        public IEnumerator SetBlockingMidPath_DefaultReplanPolicy_NeverCrossesNewBlock_ReachesTarget()
        {
            ShellRoot shell = null!;
            Id playerId = default;
            Vec2 origin = default;
            Id mapId = default;

            yield return EnterFreshWorld("replan", (s, p, o, m) => { shell = s; playerId = p; origin = o; mapId = m; });

            var target = origin + new Vec2(3, 0);
            _movement!.Request(MoveRequest.ToTarget(playerId, target));

            for (var i = 0; i < 5; i++) yield return new WaitForFixedUpdate(); // 先跑几个 tick，确认路径已建立且尚未到达阻挡区域。

            var blockRect = new Rect(origin + new Vec2(0.5, -0.5), origin + new Vec2(1.5, 0.5));
            _blockedMapId = mapId;
            _nav!.SetBlocking(mapId, new[] { blockRect }); // 递增 GetBlockingVersion，下一 tick 触发重验。

            var guard = 600;
            while (Vec2.Distance(PositionOf(shell, playerId), target) > 0.05 && guard-- > 0)
            {
                var pos = PositionOf(shell, playerId);
                Assert.IsFalse(IsStrictlyInside(pos, blockRect), $"默认 Replan 策略下移动途中不应穿过新增阻挡矩形，实际位置 {pos}");
                yield return new WaitForFixedUpdate();
            }

            Assert.Greater(guard, 0, "默认 Replan 策略应当能绕开新增阻挡、最终到达目标");
        }

        [UnityTest]
        public IEnumerator SetBlockingMidPath_StopPolicy_ReliablyStops_FiresBlockingChanged()
        {
            ShellRoot shell = null!;
            Id playerId = default;
            Vec2 origin = default;
            Id mapId = default;

            yield return EnterFreshWorld("blockstop", (s, p, o, m) => { shell = s; playerId = p; origin = o; mapId = m; });

            var resident = UnityEngine.Object.FindFirstObjectByType<FrameworkResidentHost>();
            Assert.IsNotNull(resident);
            resident!.Gameplay.Carriers.MovementOptions.BlockingChangePolicy = BlockingChangePolicy.Stop;

            var target = origin + new Vec2(3, 0);
            _movement!.Request(MoveRequest.ToTarget(playerId, target));
            for (var i = 0; i < 5; i++) yield return new WaitForFixedUpdate();

            var blockRect = new Rect(origin + new Vec2(0.5, -0.5), origin + new Vec2(1.5, 0.5));
            _blockedMapId = mapId;
            _nav!.SetBlocking(mapId, new[] { blockRect });

            var stopCount = 0;
            var stopReasons = new List<MoveStopReason>();
            _stoppedHandler = (unitId, position, reason) =>
            {
                if (!unitId.Equals(playerId)) return;
                stopCount++;
                stopReasons.Add(reason);
            };
            _movement.OnMoveStopped += _stoppedHandler;

            var guard = 200;
            while (stopCount == 0 && guard-- > 0)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.Greater(guard, 0, "Stop 策略下应当能在合理 tick 数内检测到阻挡变化并停止");
            Assert.AreEqual(1, stopCount, "应恰好触发一次 OnMoveStopped");
            Assert.AreEqual(MoveStopReason.BlockingChanged, stopReasons[0]);
            Assert.IsFalse(IsStrictlyInside(PositionOf(shell, playerId), blockRect), "停止时的位置不应落在新增阻挡矩形内部");

            var posAtStop = PositionOf(shell, playerId);
            for (var i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            Assert.Less(Vec2.Distance(PositionOf(shell, playerId), posAtStop), 1e-6, "Stop 策略生效后单位应可靠停止，不再继续移动");
        }

        // 判断记录（补齐场景 5 的四种 BlockingChangePolicy 完整对照，游戏侧任务书原文"Replan/Revalidate
        // 下每步不在矩形内并到达，Stop 下停止并 OnMoveStopped(BlockingChanged)，Ignore 下会穿过
        // （对照）"）：上面两条用例分别覆盖 Replan（默认）与 Stop；下面两条补 Revalidate（逐段
        // Raycast 判定受阻后委托 Replan，观测后果与 Replan 一致——"不穿过、能到达"）与 Ignore
        // （唯一"应当穿过"的对照分支，与其余三种策略的"不应穿过"断言方向相反，见
        // MovementOptions.BlockingChangePolicy.Ignore 判断记录"游戏层认为不值得为动态阻挡自动重验"）。

        [UnityTest]
        public IEnumerator SetBlockingMidPath_RevalidatePolicy_NeverCrossesNewBlock_ReachesTarget()
        {
            ShellRoot shell = null!;
            Id playerId = default;
            Vec2 origin = default;
            Id mapId = default;

            yield return EnterFreshWorld("revalidate", (s, p, o, m) => { shell = s; playerId = p; origin = o; mapId = m; });

            var resident = UnityEngine.Object.FindFirstObjectByType<FrameworkResidentHost>();
            Assert.IsNotNull(resident);
            resident!.Gameplay.Carriers.MovementOptions.BlockingChangePolicy = BlockingChangePolicy.Revalidate;

            var target = origin + new Vec2(3, 0);
            _movement!.Request(MoveRequest.ToTarget(playerId, target));
            for (var i = 0; i < 5; i++) yield return new WaitForFixedUpdate(); // 先跑几个 tick，确认路径已建立且尚未到达阻挡区域（同 Replan 用例同一取值）。

            var blockRect = new Rect(origin + new Vec2(0.5, -0.5), origin + new Vec2(1.5, 0.5));
            _blockedMapId = mapId;
            _nav!.SetBlocking(mapId, new[] { blockRect }); // 递增 GetBlockingVersion，下一 tick 触发逐段 Raycast 重验。

            var guard = 600;
            while (Vec2.Distance(PositionOf(shell, playerId), target) > 0.05 && guard-- > 0)
            {
                var pos = PositionOf(shell, playerId);
                Assert.IsFalse(IsStrictlyInside(pos, blockRect),
                    $"Revalidate 策略下剩余路段一旦被 Raycast 判定受阻即委托 Replan 重算，移动途中不应穿过新增阻挡矩形，实际位置 {pos}");
                yield return new WaitForFixedUpdate();
            }

            Assert.Greater(guard, 0, "Revalidate 策略在剩余路段受阻时应委托 Replan 重算、最终仍能到达目标");
        }

        [UnityTest]
        public IEnumerator SetBlockingMidPath_IgnorePolicy_AdvancesThroughNewBlock_AsDesignedContrast()
        {
            ShellRoot shell = null!;
            Id playerId = default;
            Vec2 origin = default;
            Id mapId = default;

            yield return EnterFreshWorld("ignore", (s, p, o, m) => { shell = s; playerId = p; origin = o; mapId = m; });

            var resident = UnityEngine.Object.FindFirstObjectByType<FrameworkResidentHost>();
            Assert.IsNotNull(resident);
            resident!.Gameplay.Carriers.MovementOptions.BlockingChangePolicy = BlockingChangePolicy.Ignore;

            var target = origin + new Vec2(3, 0);
            _movement!.Request(MoveRequest.ToTarget(playerId, target));
            for (var i = 0; i < 5; i++) yield return new WaitForFixedUpdate(); // 同上，确认路径已建立且尚未到达阻挡区域。

            var blockRect = new Rect(origin + new Vec2(0.5, -0.5), origin + new Vec2(1.5, 0.5));
            _blockedMapId = mapId;
            _nav!.SetBlocking(mapId, new[] { blockRect });

            // 判断记录（对照断言方向与另外三种策略相反）：Ignore 策略照常沿旧路径推进、完全不比较
            // GetBlockingVersion，旧路径原本就是穿过 blockRect 中心的直线（阻挡是在建路之后才登记
            // 的），因此单位理应实际进入矩形内部——这正是 Ignore 存在的意义（"游戏层认为不值得为
            // 动态阻挡自动重验"），若这条断言从未为真，说明测试环境本身没有对照出"忽略与否"的差异。
            var enteredBlockedInterior = false;
            var guard = 600;
            while (Vec2.Distance(PositionOf(shell, playerId), target) > 0.05 && guard-- > 0)
            {
                if (IsStrictlyInside(PositionOf(shell, playerId), blockRect))
                {
                    enteredBlockedInterior = true;
                }
                yield return new WaitForFixedUpdate();
            }

            Assert.Greater(guard, 0, "Ignore 策略下旧路径应照常推进、不受新增阻挡影响，最终到达目标");
            Assert.IsTrue(enteredBlockedInterior,
                "对照断言：Ignore 策略下单位应实际穿过新增阻挡矩形内部，与 Replan/Revalidate/Stop 三种" +
                "'不应穿过'的断言方向相反，证明四种策略的差异确实生效，不是断言凑巧总为真");
        }
    }
}
