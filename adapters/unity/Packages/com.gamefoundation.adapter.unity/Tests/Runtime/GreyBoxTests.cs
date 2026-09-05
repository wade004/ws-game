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
    }
}
