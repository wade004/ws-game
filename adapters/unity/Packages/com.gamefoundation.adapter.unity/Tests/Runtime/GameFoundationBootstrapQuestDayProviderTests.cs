#nullable enable
// GameFoundationBootstrapQuestDayProviderTests：第十一方深度审核修复"第 0 步"验收
// （architecture/落地计划/audit-85f1f4f-20260908，08 号文档"装配扩展点"一节）——证明
// Adapter.Unity.Bootstrap.GameFoundationBootstrap 新增的公开属性 QuestDayProvider（见该类型判断
// 记录）确实被转发进真正装配的 GameplayAssembly/QuestHost，不是"组合根收下了赋值但没接线"的假象。
//
// 判断记录（为什么不复用 core/gameplay/assembly/tests/GameplayAssemblyOwnerDayVendorExtensionPointTests.cs）：
// 那份测试直接 new GameplayAssembly(...)，证明的是"GameplayAssembly 构造函数本身把 questDayProvider
// 转发给内部 QuestHost"这一层——本文件证明的是再往外一层："三处装配根之一（真实
// GameFoundationBootstrap 类型本身，不是自建 fixture）在构造 GameplayAssembly 时，确实把自己的
// QuestDayProvider 属性传了进去"，覆盖的是本轮任务书"第 0 步"实际要根治的缺口（此前三处装配根都
// 硬编码不传，等价于恒 null）。
//
// 判断记录（构造方式：同 SharedBootstrapDiscreteTests.cs 既有测试惯例）：GameFoundationBootstrap
// 没有 Ensure() 单例守卫，标准做法是新建一个未激活 GameObject、AddComponent 后立即赋值测试用的
// QuestDayProvider（Unity 惯例：未激活物体上新增组件时 Awake 会推迟到 SetActive(true) 才触发），
// 再激活触发真正的 Awake/BuildWorld。测试数据用独立的 Tests/Runtime/TestData/QuestDayProviderOverlay/
// 子目录（只含一张 quest.def 表，一条 repeatable=daily 的示例任务），经反射注入 _extraDatasetRoot
// 私有字段叠加为第三数据根——不复用 SharedBootstrapDiscreteTests 使用的共享 TestData/ 根目录
// （那份根目录还含 found.time_model.json 的 combat=discrete 覆盖行），避免两类测试互相耦合。
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using Adapter.Unity.Bootstrap;
using Core.Foundation.Common;
using Core.Gameplay.Quest;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class GameFoundationBootstrapQuestDayProviderTests : PlayModeTestBase
    {
        private static readonly Id QuestId = new Id("quest.g11_qdp_daily_sample");

        private GameObject? _go;
        private long _currentDay;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // 判断记录：同 SharedBootstrapDiscreteTests.TearDown——直接 Destroy(_go) 即可，
            // GameFoundationBootstrap.OnDestroy() 自身已负责退订固定步/帧回调与释放表现层资源。
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
            // SharedBootstrapDiscreteTests.cs/DiscreteCombatTests.cs 同款判断记录）。
            var repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            return Path.Combine(
                repoRoot,
                "adapters", "unity", "Packages", "com.gamefoundation.adapter.unity",
                "Tests", "Runtime", "TestData", "QuestDayProviderOverlay");
        }

        /// <summary>见 SharedBootstrapDiscreteTests.CleanupStaleSharedCompositionRoots 同款判断
        /// 记录：先同步销毁场景里任何残留的 GameFoundationBootstrap 实例，避免与本类型即将构建的
        /// 独立实例共享同一个 UnityEngineHost.Input 单例互相抢占。</summary>
        private static void CleanupStaleSharedCompositionRoots()
        {
            foreach (var stale in UnityEngine.Object.FindObjectsByType<GameFoundationBootstrap>(FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(stale.gameObject);
            }
        }

        private GameFoundationBootstrap BuildInactiveBootstrapWithQuestDayProviderOverlay()
        {
            CleanupStaleSharedCompositionRoots();

            var go = new GameObject("QuestDayProviderOverlayTest");
            go.SetActive(false); // 推迟 Awake，先注入测试数据根 + QuestDayProvider。
            var bootstrap = go.AddComponent<GameFoundationBootstrap>();

            var field = typeof(GameFoundationBootstrap).GetField(
                "_extraDatasetRoot", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "GameFoundationBootstrap 应当有 _extraDatasetRoot 私有字段（H4 新增）");
            field!.SetValue(bootstrap, TestDataRootAbsolute());

            // 本轮新增：公开属性直接赋值，不需要反射——见 GameFoundationBootstrap.QuestDayProvider
            // 判断记录。
            bootstrap.QuestDayProvider = () => _currentDay;

            _go = go;
            go.SetActive(true); // 触发 Awake -> BuildWorld，此时 _extraDatasetRoot/QuestDayProvider 均已生效。

            return bootstrap;
        }

        [UnityTest]
        public IEnumerator QuestDayProvider_Forwarded_ControlsDailyRepeatableAvailability_OnRealCompositionRoot()
        {
            _currentDay = 1;
            var bootstrap = BuildInactiveBootstrapWithQuestDayProviderOverlay();

            Assert.IsFalse(bootstrap.BootstrapFailed, "测试数据根应当能正常装配（叠加一张 quest.def 表）");
            Assert.IsNotNull(bootstrap.Gameplay, "装配成功后应有真实 GameplayAssembly 实例");

            var gameplay = bootstrap.Gameplay!;
            var playerId = bootstrap.PlayerId;

            Assert.IsTrue(gameplay.Quest.Accept(playerId, QuestId), "接取每日任务应成功");
            gameplay.Quest.UpdateProgress(playerId, QuestId, 0, 1);
            Assert.IsTrue(gameplay.Quest.TurnIn(playerId, QuestId), "交任务应成功");

            // 同一天（QuestDayProvider 转发给 QuestHost 后仍返回 1）：不可再接。这一步只有在
            // GameFoundationBootstrap 真的把 bootstrap.QuestDayProvider 转发进内部 QuestHost 时才
            // 成立——此前恒传 null 的旧代码会让 QuestHost 退化到某个不随本测试推进的默认天数来源，
            // 与下面"切到第二天后变为可再接"这一断言组合起来，能排出"看似 Unavailable 但其实是别的
            // 原因（例如任务状态压根没进入 Completed）"这一假阳性。
            Assert.AreEqual(QuestState.Unavailable, gameplay.Quest.GetState(playerId, QuestId),
                "转发的天数来源仍是第 1 天时，每日任务当天应不可再接");

            // 天数来源推进到第二天：应当重新变为可接（不再是 Unavailable）——证明 QuestHost 读取的
            // 确实是本测试实时可控的 _currentDay，而不是构造期捕获的某个快照值。
            _currentDay = 2;
            Assert.AreNotEqual(QuestState.Unavailable, gameplay.Quest.GetState(playerId, QuestId),
                "转发的天数来源推进到第 2 天后，每日任务应重新变为可接");

            yield return null;
        }
    }
}
