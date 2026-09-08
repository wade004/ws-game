#nullable enable
// HitFrameSyncEndToEndTests：W6 收口验收——命中帧同步开关经生产装配根真正打通端到端，不是像
// presentation/feedback_binder/tests/FeedbackBinderHitFrameSyncTests.cs（假 rig）或
// Tests/Runtime/ModelIntegrationTests.cs（手工构造 FeedbackBinderCore，绕开 PresentationAssembly）
// 那样验证机制本身，而是经真实 Adapter.Unity.Bootstrap.GameFoundationBootstrap（GreyBox.unity 唯一
// 挂载的组合根类型本身）走完整链路：GameFoundationBootstrap 开关 -> PresentationAssembly 新增的
// hitFrameSource 构造参数（把 CharacterRigHitFrameSource 接给内部 FeedbackBinderCore）+
// UnityViewFactory 新增的 renderOptions 构造参数（把同一个 RenderOptions 接给 ModelCharacterRig
// 构造期，决定它是否真的订阅 IRenderer3D.OnAnimEvent）——这两处此前都是"如实记录的限制/未被发现的
// 深层缺口"（见 GameFoundationBootstrap.HitFrameSource 属性判断记录、UnityViewFactory._renderOptions
// 字段判断记录），任何一处漏接都会让本用例失败。
//
// 判断记录（不改动 GreyBox.unity 场景资产，改用独立 GameObject + 反射注入，同
// SharedBootstrapDiscreteTests.cs 既有惯例）：本用例需要把玩家外形切成 model 型
// （creature.sample_model_hero）并打开命中帧同步开关，这两项都不是 GreyBox.unity 场景资产的默认值，
// 直接改场景资产会连带影响 GreyBoxTests/ShellFlowTests/UiSuiteTests/VerticalSliceTests 等全部既有
// 场景类用例。做法同 SharedBootstrapDiscreteTests.BuildInactiveBootstrapWithDiscreteOverlay：
// SetActive(false) 推迟 Awake，反射设置私有字段，再 SetActive(true) 触发真正的 Bootstrap()。
//
// 判断记录（用合成 CombatDamageDealtEvent 而不是真实战斗命中循环）：真实战斗存在 miss/dodge/crit
// 随机分支，且 spawn.sample_beast_field 的 creature.sample_beast 挂了 ai.profile.sample_melee
// （会反击），若走真实 SubmitIntent 循环，beast 反击玩家同样会命中 combat.damage_dealt、同样受
// AnimKeyframeDriven 开关影响，容易与本用例要验证的"玩家这一次攻击"互相串扰、引入不必要的时序
// flakiness。改为同 Tests/Runtime/ModelIntegrationTests.cs 一致的手法——直接
// bus.PublishImmediate(new CombatDamageDealtEvent(...))；与该文件不同的是，本用例不手工构造
// FeedbackBinderCore，而是经反射拿到 GameFoundationBootstrap 内部真实持有的 IEventBus，事件流向
// 的是生产装配根 bootstrap.Presentation.Feedback（真实 FeedbackBinder 实例），rig 也是经
// UnityViewFactory.CreateView 生产路径创建、已登记进 HitFrameSource 的真实 ModelCharacterRig——
// 这正是"通过生产装配根，不是手工构造 FeedbackBinder"要验证的那条链路。isCrit:true 确定性命中
// data/_sample/feedback/feedback.binding.json 的 feedback.sample_crit_damage 规则（含 play_vfx），
// 该表唯一一条声明了 play_vfx 的 combat.damage_dealt 规则，不需要额外叠加测试数据。
//
// H5b 根治（游戏侧复核发现 2，加固"超时兜底不能掩盖命中帧链路缺口"）：本文件唯一的用例此前只用一个
// 远大于 HitFrameSyncPolicy 默认超时（0.5s）的 5 秒死线轮询"EmitParticleCallCount 是否终于增加"，
// 从未排除过"命中帧动画事件其实完全没有触发，只是 0.5 秒超时兜底先一步把动作放出来，恰好也让计数
// 增加"这一假通过可能性——即便 ModelCharacterRig.HitFrameReached -> CharacterRigHitFrameSource 这条
// 链路整个断线，旧版用例也会在 0.5 秒后照常通过。现经 HitFrameSyncPolicy.LastReleaseReason/
// FeedbackBinder.LastHitFrameSyncReleaseReason（见二者判断记录，本次新增的只读诊断）直接断言真实释放
// 原因，并新增镜像反例 ModelAttacker_HitFrameSync_NeverFires_TimesOutWithTimeoutReason（刻意不播放
// 攻击动画，验证"超时兜底确实只在命中帧真的没有到达时才触发，且诊断如实报告 Timeout"）——二者合起来
// 完整覆盖 AnimKeyframeDriven 策略的两条释放路径，不再只用一个共同的"计数增加了没有"来源判断成功与否。
using System.Collections;
using System.Reflection;
using Adapter.Unity.Bootstrap;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Rules.Common;
using NUnit.Framework;
using Presentation.FeedbackBinder.Core;
using Presentation.Render;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class HitFrameSyncEndToEndTests : PlayModeTestBase
    {
        private GameObject? _go;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // 判断记录：同 SharedBootstrapDiscreteTests.TearDown——不手动调用 World.ClearAll()，
            // 直接 Destroy(_go) 触发 GameFoundationBootstrap.OnDestroy 既有清理路径即可。
            if (_go != null)
            {
                UnityEngine.Object.Destroy(_go);
                _go = null;
            }
            yield return null;
        }

        /// <summary>见文件顶部判断记录：同步销毁场景里任何残留的 <see cref="GameFoundationBootstrap"/>
        /// 实例，避免与本方法即将构建的新实例共享同一个 <c>UnityEngineHost.Input</c> 单例时互相抢占
        /// 按键事件队列（同 SharedBootstrapDiscreteTests.CleanupStaleSharedCompositionRoots 惯例）。</summary>
        private static void CleanupStaleSharedCompositionRoots()
        {
            foreach (var stale in UnityEngine.Object.FindObjectsByType<GameFoundationBootstrap>(FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(stale.gameObject);
            }
        }

        private GameFoundationBootstrap BuildInactiveBootstrapWithHitFrameSyncEnabled()
        {
            CleanupStaleSharedCompositionRoots();

            var go = new GameObject("HitFrameSyncEndToEndTest");
            go.SetActive(false); // 推迟 Awake，先反射注入两个私有字段。
            var bootstrap = go.AddComponent<GameFoundationBootstrap>();

            var type = typeof(GameFoundationBootstrap);

            var hitFrameSyncField = type.GetField("_hitFrameSyncEnabled", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(hitFrameSyncField, "GameFoundationBootstrap 应当有 _hitFrameSyncEnabled 私有字段（W6 收口新增）");
            hitFrameSyncField!.SetValue(bootstrap, true);

            // 切玩家外形为 model 型（data/_sample/display/display.map.json 的
            // display.map.sample_model_hero，logical_id=creature.sample_model_hero），使其经
            // UnityViewFactory 创建真实 UnityModelView + ModelCharacterRig，而不是默认的 sprite 型。
            var playerTemplateField = type.GetField("_playerTemplateId", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(playerTemplateField, "GameFoundationBootstrap 应当有 _playerTemplateId 私有字段");
            playerTemplateField!.SetValue(bootstrap, "creature.sample_model_hero");

            _go = go;
            go.SetActive(true); // 触发 Awake -> BuildWorld，此时两个字段均已生效。
            return bootstrap;
        }

        private static IEventBus RequireInternalBus(GameFoundationBootstrap bootstrap)
        {
            var field = typeof(GameFoundationBootstrap).GetField("_bus", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "GameFoundationBootstrap 应当持有内部事件总线（私有字段 _bus）");
            var bus = field!.GetValue(bootstrap) as IEventBus;
            Assert.IsNotNull(bus, "内部事件总线不应为空");
            return bus!;
        }

        /// <summary>核心验收：model 型攻击者（玩家）真实 <c>ICharacterRig.PlayClip(anim.attack)</c>
        /// 播放到命中帧之前，AnimKeyframeDriven 策略下 <c>combat.damage_dealt</c> 规则
        /// （<c>feedback.sample_crit_damage</c>，含 <c>play_vfx</c>）的动作不应触发
        /// <see cref="Adapter.Unity.EngineAdapter.UnityRenderer2D.EmitParticle"/>；命中帧到达（占位
        /// <c>attack.anim</c> 50% 处的 <c>hit_frame</c> AnimationEvent，见
        /// <c>GeneratePlaceholderModelAssets.cs</c>）后应当真的触发。</summary>
        [UnityTest]
        public IEnumerator ModelAttacker_HitFrameSync_DelaysPlayVfxUntilRealHitFrame()
        {
            var bootstrap = BuildInactiveBootstrapWithHitFrameSyncEnabled();
            Assert.IsFalse(bootstrap.BootstrapFailed, "开启命中帧同步开关 + 切玩家为 model 型外形后，共享引导装配不应失败");

            // 判断记录（GreyBoxTests.LoadGreyBoxScene 同款等待）：world.AddEntity(player)/
            // gameplay.EnterMap（生成 beast）都在 Bootstrap() 内部先于 PresentationAssembly/
            // ViewBinder 构造完成（见 GameFoundationBootstrap.BuildWorld 判断记录"这里不能提前
            // DispatchPending"），entity.created 事件排队到第一次 world.Tick 的事件派发阶段
            // （固定步）才真正送到 ViewBinder 创建 View——go.SetActive(true) 刚返回时 View 还不存在，
            // 必须先驱动至少一次固定步。
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.IsTrue(bootstrap.BeastEntityId.HasValue, "灰盒场景应当已经通过 spawn.sample_beast_field 生成一只生物");

            Assert.IsTrue(
                bootstrap.Presentation!.ViewBinder.TryGetView(bootstrap.PlayerId, out var playerView) && playerView != null,
                "玩家实体应当已经绑定 View");
            Assert.IsInstanceOf<IHasCharacterRig>(playerView, "切到 creature.sample_model_hero 后，玩家 View 应当是持有 ICharacterRig 的类型（UnityModelView）");
            var rig = ((IHasCharacterRig)playerView!).Rig;

            var bus = RequireInternalBus(bootstrap);
            var renderer2D = UnityEngineHost.Ensure().Renderer2D;
            var startEmitCount = renderer2D.EmitParticleCallCount;

            // isCrit:true 确定性命中 feedback.sample_crit_damage（含 play_vfx: vfx.sample_hit_spark,
            // attach: target），不依赖战斗随机数（见文件顶部判断记录）。
            bus.PublishImmediate(new CombatDamageDealtEvent(
                bootstrap.PlayerId, bootstrap.BeastEntityId!.Value, new Id("school.physical"), 5.0,
                isCrit: true, HitResult.Hit));

            Assert.AreEqual(startEmitCount, renderer2D.EmitParticleCallCount,
                "AnimKeyframeDriven 策略下，combat.damage_dealt 发布的那一刻不应立即触发 EmitParticle（应等待攻击方真实 Animator 播到命中帧）");
            Assert.IsTrue(bootstrap.Presentation!.Feedback.HasPendingPlayback, "命中帧同步等待期间，FeedbackBinder.HasPendingPlayback 应当为真（仍有待回放内容）");
            // H5b 根治（游戏侧复核发现 2）：入队但尚未真正释放前，诊断不应报告任何具体原因——防止
            // 下面的最终断言被"字段从上一条用例遗留了旧值，恰好还是 HitFrame"这种巧合掩盖。
            Assert.IsNull(bootstrap.Presentation!.Feedback.LastHitFrameSyncReleaseReason,
                "本次等待项刚入队，尚未发生过任何一次释放，诊断不应报告具体原因");

            // 真正播放攻击动画（rig 已在 UnityViewFactory.CreateView 时登记进 HitFrameSource，见文件
            // 顶部判断记录）：占位 attack.anim 50% 处内嵌 hit_frame AnimationEvent（见
            // GeneratePlaceholderModelAssets.cs），真实 Animator 播放到那一刻才会触发
            // ModelCharacterRig.HitFrameReached。
            rig.PlayClip(new Id("anim.attack"), loop: false, speed: 1.0);

            var deadline = Time.realtimeSinceStartup + 5f;
            while (renderer2D.EmitParticleCallCount == startEmitCount && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.Greater(renderer2D.EmitParticleCallCount, startEmitCount,
                "攻击方真实 Animator 播放到命中帧后，应当经 ModelCharacterRig.HitFrameReached -> " +
                "CharacterRigHitFrameSource -> FeedbackBinder 释放等待中的 play_vfx 动作（EmitParticle 应被真正调用）");
            // H5b 根治（游戏侧复核发现 2）：上面这条 Assert.Greater 单独看无法排除"命中帧动画事件其实
            // 从未真正触发，只是 HitFrameSyncPolicy 默认 0.5 秒超时兜底先一步释放，恰好也让
            // EmitParticleCallCount 增加，把这条用例本该验证的命中帧链路缺口悄悄掩盖成一次假通过"这一
            // 可能性——本用例 5 秒死线远大于默认 0.5 秒超时，即便命中帧链路完全断线也会在超时后同样让
            // 上面的循环退出、Assert.Greater 同样通过。补一条直接断言真实释放原因：本用例已经调用
            // rig.PlayClip 播放真实攻击动画，若真的经命中帧路径释放，诊断必须报告 HitFrame；报告
            // Timeout 说明命中帧事件从未真正到达，链路本身有缺口，不应被当作用例通过。
            Assert.AreEqual(HitFrameSyncReleaseReason.HitFrame, bootstrap.Presentation!.Feedback.LastHitFrameSyncReleaseReason,
                "释放原因应当是命中帧事件真正到达（HitFrame），而不是默认 0.5 秒超时兜底（Timeout）——" +
                "否则说明 ModelCharacterRig.HitFrameReached -> CharacterRigHitFrameSource 这条链路本身没有真正打通，" +
                "只是被超时兜底悄悄掩盖");
            Assert.IsFalse(bootstrap.Presentation!.Feedback.HasPendingPlayback,
                "命中帧释放待回放动作后，HasPendingPlayback 应当恢复为假（本用例只合成了一次 combat.damage_dealt，不存在其它并行等待项）");
        }

        /// <summary>H5b 根治新增（游戏侧复核发现 2）：上面
        /// <see cref="ModelAttacker_HitFrameSync_DelaysPlayVfxUntilRealHitFrame"/> 的镜像反例——本用例
        /// 刻意不播放攻击动画，攻击方的 <c>anim.attack</c> hit_frame AnimationEvent 因此永远不会到达，
        /// 待回放的 <c>play_vfx</c> 动作只能经 <c>HitFrameSyncPolicy</c> 默认 0.5 秒超时兜底释放
        /// （<see cref="HitFrameSyncPolicy.DefaultTimeoutSeconds"/>）。两条用例合起来才完整覆盖
        /// AnimKeyframeDriven 策略的两条释放路径：前者锁定"命中帧路径确实生效，不是被超时兜底悄悄
        /// 顶替"，本用例锁定"超时兜底确实只在命中帧真的没有到达时才触发，且诊断如实报告 Timeout，
        /// 不会被误标成 HitFrame"——防止 <see cref="HitFrameSyncPolicy.LastReleaseReason"/> 这条新增
        /// 诊断本身写反、或者被某处遗留旧值污染这一相反的假通过风险。</summary>
        [UnityTest]
        public IEnumerator ModelAttacker_HitFrameSync_NeverFires_TimesOutWithTimeoutReason()
        {
            var bootstrap = BuildInactiveBootstrapWithHitFrameSyncEnabled();
            Assert.IsFalse(bootstrap.BootstrapFailed, "开启命中帧同步开关 + 切玩家为 model 型外形后，共享引导装配不应失败");

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.IsTrue(bootstrap.BeastEntityId.HasValue, "灰盒场景应当已经通过 spawn.sample_beast_field 生成一只生物");
            Assert.IsTrue(
                bootstrap.Presentation!.ViewBinder.TryGetView(bootstrap.PlayerId, out var playerView) && playerView != null,
                "玩家实体应当已经绑定 View");
            Assert.IsInstanceOf<IHasCharacterRig>(playerView, "切到 creature.sample_model_hero 后，玩家 View 应当是持有 ICharacterRig 的类型（UnityModelView）");
            // 判断记录：本用例不需要真正持有/使用 rig（不调用 PlayClip），只需确认 View 已就绪、
            // 攻击方确实登记了 rig（HasRig 为真，命中帧同步策略才会真正入队等待而不是立即同步释放，
            // 见 HitFrameSyncPolicy.WaitForHitFrame"攻击方无 rig 时立即播放"判断记录），与上面
            // ModelAttacker_HitFrameSync_DelaysPlayVfxUntilRealHitFrame 同一前提条件。

            var bus = RequireInternalBus(bootstrap);
            var renderer2D = UnityEngineHost.Ensure().Renderer2D;
            var startEmitCount = renderer2D.EmitParticleCallCount;

            bus.PublishImmediate(new CombatDamageDealtEvent(
                bootstrap.PlayerId, bootstrap.BeastEntityId!.Value, new Id("school.physical"), 5.0,
                isCrit: true, HitResult.Hit));

            Assert.AreEqual(startEmitCount, renderer2D.EmitParticleCallCount,
                "入队但攻击动画从未播放时，命中帧同步同样不应立即触发 EmitParticle（超时兜底之前不应提前入队播放）");
            Assert.IsTrue(bootstrap.Presentation!.Feedback.HasPendingPlayback, "超时兜底触发前，仍应视为有待回放内容");
            Assert.IsNull(bootstrap.Presentation!.Feedback.LastHitFrameSyncReleaseReason,
                "本次等待项刚入队，尚未发生过任何一次释放，诊断不应报告具体原因");

            // 刻意不调用 rig.PlayClip：命中帧动画事件永远不会到达，待回放动作只能等
            // HitFrameSyncPolicy.DefaultTimeoutSeconds（0.5 秒）超时兜底释放。5 秒死线远大于该默认
            // 超时，留足真实时间余量。
            var deadline = Time.realtimeSinceStartup + 5f;
            while (renderer2D.EmitParticleCallCount == startEmitCount && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.Greater(renderer2D.EmitParticleCallCount, startEmitCount,
                "命中帧从未到达时，超时兜底最终也应当释放待播放的 play_vfx 动作，不能无限期悬挂");
            Assert.AreEqual(HitFrameSyncReleaseReason.Timeout, bootstrap.Presentation!.Feedback.LastHitFrameSyncReleaseReason,
                "本用例从未播放攻击动画、命中帧事件从未到达，唯一可能的释放路径是超时兜底，诊断应如实报告 Timeout，不应是 HitFrame");
            Assert.IsFalse(bootstrap.Presentation!.Feedback.HasPendingPlayback,
                "超时兜底释放待回放动作后，HasPendingPlayback 应当恢复为假");
        }
    }
}
