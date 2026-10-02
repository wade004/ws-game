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
//
// 手感落地 M2-A（引擎侧手感生产接线，验收缺口 e）：本文件末尾的 FeelEngine_* 用例经同一套"独立 GameObject + 反射注入"的真实
// GameFoundationBootstrap（玩家 model 型外形 → model rig、示例生物 sprite 型外形 → sprite rig）验证开手感后的引擎侧链路：
// 按键 → 输入缓冲 → 只施法一次；命中 → 顿帧 → 被击/攻击方 rig 冻结的 tick 数等于顿帧档案换算值、旁观单位不冻结；
// 命中 → 打击反馈包 → 镜头冲量（引擎 UnityCamera 的 ICameraImpulse）幅度来自档案；不开手感时同场景走原直接施法路径、不冻结。
// 判断记录（测试数据写进临时目录而不是新增仓库文件）：这组用例需要一条带时间线的技能、一份带镜头冲量的打击反馈包与一条 play_impact
// 反馈规则（都是游戏侧数据）；写成仓库里的新测试数据文件会在 Unity 导入范围内新增需要 .meta 的文件，改为每条用例运行时写进
// 临时目录、经 _extraDatasetRoot（绝对路径，同 GameFoundationBootstrapQuestDayProviderTests）叠加为第三数据根，用完删除。
// 判断记录（逐 tick 观测挂在 sim.tick_finished 上）：冻结/解冻都在固定步末尾的事件派发里落到 rig，观测点放在同一个派发里
// （订阅晚于视图绑定与反馈绑定，必然在它们之后执行），不依赖 WaitForFixedUpdate 与引导固定步的相对次序。
// 判断记录（命中随机性）：示例数据的命中表可能让一次普攻未命中；用例最多重试 5 次，取第一次真正命中的那一轮作观测窗口。
// 手感落地 M3-C（已知限制解除）：FeelEngine_*Particle*/FeelEngine_*HotReload* 用例——顿帧期间被冻结单位名下（anchor 挂接）的粒子/特效按反馈包
// freeze_layers.particles 暂停（引擎 UnityRenderer2D 的 IParticleFreezer：序列帧播放器停推进/粒子系统 Pause），旁观单位的不暂停；
// 引导开 EnableDataHotReload 后改手感预设文件，单位在不重启下读到新值。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Adapter.Unity.Bootstrap;
using Adapter.Unity.EngineAdapter;
using Core.Carriers.Assembly;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Core.Foundation.SimLoop;
using Core.Rules.Common;
using NUnit.Framework;
using Presentation.FeedbackBinder.Core;
using Presentation.Render;
using Presentation.VfxSfx.Contracts;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    [Category("module:feedback_binder")]
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
        // ------------------------------------------------------------------
        // 手感落地 M2-A：引擎侧手感生产接线（见文件顶部判断记录）
        // ------------------------------------------------------------------

        private const string FeelCalibrationId = "feel.calibration.framework_default";
        private const string FeelSwingSkillId = "skill.m2a_swing";
        private const string FeelImpactProfileId = "feedback.impact_profile.m2a";
        private const string FeelFreezeProfileId = "feedback.impact_profile.m3c_freeze";
        private const double FeelProfileImpulseGain = 0.5;
        private const double FeelProfileDecayMs = 120;
        private const string AttackActionId = "input.action.attack";
        private const string AttackSlot = "slot_m2a_atk";
        private static readonly Id FeelMapId = new Id("world.sample_field");

        private string? _feelOverlayDir;

        [TearDown]
        public void DeleteFeelOverlayDir()
        {
            if (_feelOverlayDir != null)
            {
                try { Directory.Delete(_feelOverlayDir, recursive: true); } catch { /* 尽力而为的清理 */ }
                _feelOverlayDir = null;
            }
        }

        /// <summary>把本组用例的游戏侧数据写进临时目录并返回其绝对路径（见文件顶部判断记录）。</summary>
        private string WriteFeelOverlay(bool presetOverride = false)
        {
            var dir = Path.Combine(Application.temporaryCachePath, "feel_m2a_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "skill"));
            Directory.CreateDirectory(Path.Combine(dir, "feedback"));
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            // 时间线技能：前摇 100 ms、有效 60 ms、后摇 240 ms，hit 标记在有效帧起点；伤害 1 点（示例生物不会被一击打死，死亡会改变命中后的反应）。
            File.WriteAllText(Path.Combine(dir, "skill", "skill.def.json"),
                "{\"table\":\"skill.def\",\"schema_version\":1,\"rows\":[{" +
                "\"id\":\"" + FeelSwingSkillId + "\",\"school\":\"school.physical\",\"kind\":\"active\",\"range\":30,\"cast_time\":0.4," +
                "\"cooldown_duration\":0,\"respects_gcd\":false,\"target_shape_ref\":\"target.chain.sample_nearest_enemy\"," +
                "\"timeline\":{\"startup_ms\":100,\"active_ms\":60,\"recovery_ms\":240,\"markers\":[{\"name\":\"hit\",\"at_ms\":100}]}," +
                "\"effects\":[{\"kind\":\"school_damage\",\"params\":{\"base_value\":1,\"coefficient\":0,\"school\":\"school.physical\"}}]}]}");
            File.WriteAllText(Path.Combine(dir, "feedback", "feedback.impact_profile.json"),
                "{\"table\":\"feedback.impact_profile\",\"schema_version\":1,\"rows\":[{\"id\":\"" + FeelImpactProfileId + "\",\"variants\":[" +
                "{\"class\":\"medium\",\"outcome\":\"hit\",\"camera\":{\"impulse_gain\":" + FeelProfileImpulseGain.ToString(inv) +
                ",\"decay_ms\":" + FeelProfileDecayMs.ToString(inv) + "}}]}," +
                // 手感落地 M3-C：粒子层冻结的反馈包（freeze_layers.particles 为真）；上一行那个包没写 freeze_layers，缺省只冻骨骼/序列帧。
                "{\"id\":\"" + FeelFreezeProfileId + "\",\"variants\":[" +
                "{\"class\":\"medium\",\"outcome\":\"hit\",\"freeze_layers\":{\"particles\":true}}]}]}");
            File.WriteAllText(Path.Combine(dir, "feedback", "feedback.binding.json"),
                "{\"table\":\"feedback.binding\",\"schema_version\":1,\"rows\":[{\"id\":\"feedback.m2a_impact\",\"event\":\"combat.hit_confirmed\"," +
                "\"actions\":[{\"kind\":\"play_impact\",\"params\":{}}]}]}");
            if (presetOverride)
            {
                Directory.CreateDirectory(Path.Combine(dir, "feel"));
                File.WriteAllText(Path.Combine(dir, "feel", "feel.preset.json"), BuildBasePresetOverrideRow());
            }

            _feelOverlayDir = dir;
            return dir;
        }

        /// <summary>手感落地 M3-C：热重载用例的叠加数据——把框架手感根里基础档（<c>feel.preset</c> 第一行，框架缺省标定的基础档）原样复制成一行带 <c>override: true</c>
        /// 的同主键行写进叠加根（DataRegistry 的跨根覆盖语义：后加载根里声明覆盖的同主键行整行替换前层），这样运行中改叠加根里的这一个文件就能热换基础档，
        /// 不必复制整个内容根。复制不改任何字段值，所以未改动前单位读到的值与框架根完全一致。</summary>
        private static string BuildBasePresetOverrideRow()
        {
            var contentRoot = new UnityFileSystem(readOnlyContentMode: true).GetContentRootDir();
            var basePath = Path.Combine(contentRoot, "data", "_feel", "feel", "feel.preset.json");
            Assert.IsTrue(File.Exists(basePath), "框架手感预设表应已随内容根同步：" + basePath);
            var text = File.ReadAllText(basePath);
            var start = text.IndexOf('{', text.IndexOf("\"rows\"", StringComparison.Ordinal));
            var depth = 0;
            var end = -1;
            for (var i = start; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0) { end = i; break; }
            }

            Assert.Greater(end, start, "应能截出 feel.preset 的第一行");
            var row = "{\"override\":true," + text.Substring(start + 1, end - start);
            return "{\"table\":\"feel.preset\",\"schema_version\":1,\"rows\":[" + row + "]}";
        }

        /// <summary>独立引导（玩家 model 外形 + 临时叠加数据根）；<paramref name="feelOn"/> 为真时打开 FeelOptions。</summary>
        private GameFoundationBootstrap BuildFeelBootstrap(bool feelOn, bool hotReload = false, bool presetOverride = false)
        {
            CleanupStaleSharedCompositionRoots();

            var go = new GameObject("FeelEngineWiringTest");
            go.SetActive(false); // 推迟 Awake，先注入数据根、玩家外形与手感选项。
            var bootstrap = go.AddComponent<GameFoundationBootstrap>();
            var type = typeof(GameFoundationBootstrap);
            type.GetField("_playerTemplateId", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(bootstrap, "creature.sample_model_hero");
            type.GetField("_extraDatasetRoot", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(bootstrap, WriteFeelOverlay(presetOverride));
            if (feelOn)
            {
                bootstrap.FeelOptions = new CarriersFeelOptions { CalibrationId = FeelCalibrationId };
            }

            bootstrap.EnableDataHotReload = hotReload;

            _go = go;
            go.SetActive(true);
            return bootstrap;
        }

        private sealed class FeelScene
        {
            public GameFoundationBootstrap Bootstrap = null!;
            public Id Player;
            public Id Target;
            public Id Bystander;
            public readonly List<SkillCastStartEvent> Casts = new List<SkillCastStartEvent>();
            public readonly List<CombatHitConfirmedEvent> Hits = new List<CombatHitConfirmedEvent>();
            public readonly Dictionary<Id, List<bool>> Frozen = new Dictionary<Id, List<bool>>();

            public IPresentationFreezable? Freezable(Id id) =>
                Bootstrap.Presentation!.ViewBinder.TryGetView(id, out var view) && view is IHasCharacterRig has
                    ? has.Rig as IPresentationFreezable
                    : null;

            public void ClearObservations()
            {
                Casts.Clear();
                Hits.Clear();
                foreach (var list in Frozen.Values) list.Clear();
            }
        }

        /// <summary>等视图建好后挂观测：目标 = 示例生物（sprite rig）、旁观者 = 同模板第二只生物（更远，不在命中名单里）、攻击方 = 玩家（model rig）。</summary>
        private IEnumerator SetUpFeelScene(GameFoundationBootstrap bootstrap, FeelScene scene)
        {
            Assert.IsFalse(bootstrap.BootstrapFailed, "手感场景引导装配不应失败");
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(bootstrap.BeastEntityId.HasValue, "应当已经生成示例生物");

            scene.Bootstrap = bootstrap;
            scene.Player = bootstrap.PlayerId;
            scene.Target = bootstrap.BeastEntityId!.Value;
            var gameplay = bootstrap.Gameplay!;

            // 旁观者：同模板、比目标更远（目标选择链取最近的敌对单位，所以命中的是目标）；两只生物都摘掉 AI，免得反击玩家干扰观测。
            var targetPos = bootstrap.World!.GetEntity(scene.Target)!.Position;
            scene.Bystander = gameplay.Carriers.Creatures.Spawn(
                new Id("creature.sample_beast"), FeelMapId, new Vec2(targetPos.X + 12.0, targetPos.Y), Math.PI, null, 1);
            foreach (var id in new[] { scene.Target, scene.Bystander })
            {
                var ai = gameplay.Carriers.Rules.Ai;
                foreach (var registered in new List<Id>(ai.RegisteredUnitIds))
                {
                    if (registered.Equals(id)) { ai.UnregisterUnit(id); break; }
                }
            }

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate(); // 实体创建事件在固定步末尾派发，视图随之建好。

            foreach (var id in new[] { scene.Player, scene.Target, scene.Bystander })
            {
                Assert.IsNotNull(scene.Freezable(id), "单位 " + id + " 应当有持有可冻结 rig 的视图");
                scene.Frozen[id] = new List<bool>();
            }

            var bus = RequireInternalBus(bootstrap);
            bus.Subscribe<SkillCastStartEvent>(RulesEventKeys.SkillCastStart, e => { if (e.CasterId.Equals(scene.Player)) scene.Casts.Add(e); });
            bus.Subscribe<CombatHitConfirmedEvent>(EventKeys.CombatHitConfirmed, e => scene.Hits.Add(e));
            bus.Subscribe<SimTickFinishedEvent>(SimEventKeys.TickFinished, _ =>
            {
                foreach (var pair in scene.Frozen)
                {
                    pair.Value.Add(scene.Freezable(pair.Key)?.IsPresentationFrozen ?? false);
                }
            });
        }

        /// <summary>开手感时：把普攻动作声明为攻击类别并绑到时间线技能的槽位（游戏侧数据的等价物），学会技能。</summary>
        private static void DeclareBufferedAttack(GameFoundationBootstrap bootstrap)
        {
            var gameplay = bootstrap.Gameplay!;
            var player = bootstrap.PlayerId;
            gameplay.Carriers.Rules.Skill.LearnSkill(player, new Id(FeelSwingSkillId));
            Assert.IsTrue(gameplay.Carriers.SkillBindings.Bind(player, AttackSlot, new Id(FeelSwingSkillId)));
            gameplay.Feel!.InputBuffer.DeclareActions(new[]
            {
                new ActionDefinition(
                    new Id(AttackActionId), ActionKind.Button, new[] { "key:space" }, "default", null,
                    ActionClass.Attack, 160.0, null, null, InputRepeatPolicy.Refresh, null, null, AttackSlot),
            });
            Assert.IsTrue(gameplay.Feel.InputBuffer.IsBuffered(new Id(AttackActionId)));
        }

        private static IEnumerator TapAttack()
        {
            var input = (UnityInput)UnityEngineHost.Ensure().Input;
            for (var i = 0; i < 3; i++)
            {
                input.SimulateKeyForTest("space", down: true);
                yield return new WaitForFixedUpdate();
            }

            input.SimulateKeyForTest("space", down: false);
            yield return new WaitForFixedUpdate();
        }

        private static IEnumerator RunSteps(int steps)
        {
            for (var i = 0; i < steps; i++) yield return new WaitForFixedUpdate();
        }

        /// <summary>点按普攻并跑过整个动作（含顿帧延长），最多重试 5 次直到出现一次命中；返回时场景观测里只有命中那一轮的记录。</summary>
        private IEnumerator AttackUntilHit(FeelScene scene)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                scene.ClearObservations();
                yield return TapAttack();
                yield return RunSteps(60);
                if (scene.Hits.Count > 0) yield break;
            }

            Assert.Fail("5 次普攻都没有命中，无法观测顿帧/镜头冲量");
        }

        [UnityTest]
        public IEnumerator FeelEngine_AttackKey_CastsExactlyOnce_ViaBufferExitOnly()
        {
            var bootstrap = BuildFeelBootstrap(feelOn: true);
            var scene = new FeelScene();
            yield return SetUpFeelScene(bootstrap, scene);
            Assert.IsNotNull(bootstrap.Gameplay!.Feel, "开手感：玩法装配应带手感系统");
            DeclareBufferedAttack(bootstrap);

            yield return TapAttack();
            yield return RunSteps(60);

            // 只经缓冲出口施法：恰一次，且是绑定的时间线技能；引擎侧没有再直接提交普攻（默认普攻技能 0 次）——否则同一次按键施法两次。
            var swings = scene.Casts.Count(c => c.SkillId.Value == FeelSwingSkillId);
            var direct = scene.Casts.Count(c => c.SkillId.Value != FeelSwingSkillId);
            Assert.AreEqual(1, swings, "一次点按经输入缓冲只应施法一次");
            Assert.AreEqual(0, direct, "开手感后引擎侧不得再直接提交施法意图（重复施法）");
        }

        [UnityTest]
        public IEnumerator FeelEngine_Hit_FreezesAttackerAndTargetRigsByProfileTicks_BystanderUnaffected()
        {
            var bootstrap = BuildFeelBootstrap(feelOn: true);
            var scene = new FeelScene();
            yield return SetUpFeelScene(bootstrap, scene);
            DeclareBufferedAttack(bootstrap);

            // 攻击方与被击方的顿帧毫秒调成明显不同的两档（经手感系统的调试覆盖口，同生产装配里同一个提供者），避免相等时测不出谁冻了多久。
            var feel = bootstrap.Gameplay!.Feel!;
            feel.Feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.AttackerHitstopMs, FeelOp.Set, FeelValue.Of(50)));
            feel.Feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.TargetHitstopMs, FeelOp.Set, FeelValue.Of(110)));
            var judging = feel.Resolver.ResolveJudging(scene.Player);
            var step = Time.fixedDeltaTime;
            var attackerTicks = FeelCalibration.MillisecondsToTicks(judging.GetNumber(FeelFieldNames.AttackerHitstopMs), step);
            var targetTicks = FeelCalibration.MillisecondsToTicks(judging.GetNumber(FeelFieldNames.TargetHitstopMs), step);
            Assert.IsTrue(attackerTicks > 0 && targetTicks > 0 && attackerTicks != targetTicks);

            yield return AttackUntilHit(scene);

            var hit = scene.Hits[0];
            Assert.AreEqual(attackerTicks, hit.AttackerHitStopTicks);
            Assert.AreEqual(targetTicks, hit.TargetHitStopTicks);
            Assert.AreEqual(attackerTicks, scene.Frozen[scene.Player].Count(f => f), "攻击方 model rig 冻结的 tick 数 = 攻击方顿帧档案换算值");
            Assert.AreEqual(targetTicks, scene.Frozen[scene.Target].Count(f => f), "被击方 sprite rig 冻结的 tick 数 = 被击方顿帧档案换算值");
            Assert.AreEqual(0, scene.Frozen[scene.Bystander].Count(f => f), "不在命中名单里的旁观单位不冻结");

            // 不变量：冻结是连续的一段，结束后没有残留冻结。
            foreach (var id in new[] { scene.Player, scene.Target })
            {
                var trace = scene.Frozen[id];
                var first = trace.IndexOf(true);
                Assert.GreaterOrEqual(first, 0);
                Assert.AreEqual(trace.Count(f => f), trace.Skip(first).TakeWhile(f => f).Count(), "冻结应为连续一段");
                Assert.IsFalse(scene.Freezable(id)!.IsPresentationFrozen, "顿帧结束后 rig 应已恢复");
            }
        }

        [UnityTest]
        public IEnumerator FeelEngine_Hit_TriggersEngineCameraImpulse_MagnitudeFromProfile()
        {
            var bootstrap = BuildFeelBootstrap(feelOn: true);
            var scene = new FeelScene();
            yield return SetUpFeelScene(bootstrap, scene);
            DeclareBufferedAttack(bootstrap);
            var feel = bootstrap.Gameplay!.Feel!;
            feel.Feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.ImpactProfileRef, FeelOp.Set, FeelValue.Of(FeelImpactProfileId)));

            var camera = UnityEngineHost.Ensure().Camera;
            Assert.IsTrue(camera.SupportsCameraImpulse);
            var before = camera.ImpulseCount;

            yield return AttackUntilHit(scene);

            // 期望值由规则算出：min(基础冲量增益 × 变体增益 × 强度因子(无曲线/非暴击/非击杀 = 1), 震屏上限)，增益与上限取自当前手感解析。
            var presenting = feel.Resolver.ResolvePresenting(scene.Player);
            var cap = presenting.GetRaw(FeelFieldNames.CameraShakeCap).AsNumber();
            var expected = Math.Min(presenting.GetRaw(FeelFieldNames.CameraImpulseGain).AsNumber() * FeelProfileImpulseGain, cap);
            Assert.Greater(expected, 0.0);
            Assert.GreaterOrEqual(camera.ImpulseCount, before + 1, "命中应经引擎相机的 ICameraImpulse 触发镜头冲量，而不是退化为震屏");
            var last = camera.LastImpulse!.Value;
            Assert.AreEqual(expected, last.Magnitude, 1e-9, "冲量幅度应来自档案：min(增益 × 变体增益, 上限)");
            Assert.AreEqual(FeelProfileDecayMs, last.DecayMs, 1e-9);
            Assert.LessOrEqual(last.Magnitude, cap + 1e-12, "不变量：幅度不超过震屏上限");
        }

        /// <summary>预加载持续特效资源（走热路径，Spawn 直接返回真实句柄），并给每个单位挂一个 anchor 持续特效。</summary>
        private IEnumerator AttachBurnEffects(GameFoundationBootstrap bootstrap, FeelScene scene, Dictionary<Id, ParticleHandle> handles)
        {
            var host = UnityEngineHost.Ensure();
            var resource = new Id("vfx.sample_burn"); // data/_sample/vfx/vfx.def.json：anchor 挂接、无 lifetime、序列帧循环播放
            if (!host.ResourceLoader.IsLoaded(resource))
            {
                host.ResourceLoader.LoadAsync(resource, ResourceKind.Effect, (_, __) => { });
                var guard = 300;
                while (!host.ResourceLoader.IsLoaded(resource) && guard-- > 0) yield return null;
            }

            Assert.IsTrue(host.ResourceLoader.IsLoaded(resource), "占位持续特效资源应能在有限帧内加载完成");
            foreach (var unit in new[] { scene.Player, scene.Target, scene.Bystander })
            {
                var handle = bootstrap.Presentation!.Vfx.Spawn(resource, VfxAttach.Anchor(unit, new Id("anchor.m3c_chest")), null);
                Assert.IsTrue(handle.HasValue, "持续特效应能挂到单位 " + unit);
                handles[unit] = handle!.Value;
            }
        }

        /// <summary>顿帧期间逐 tick 观测每个粒子的暂停状态与已推进的播放时间（挂在 sim.tick_finished 上，与 rig 观测同一观测点）。</summary>
        private static void ObserveParticles(
            GameFoundationBootstrap bootstrap, Dictionary<Id, ParticleHandle> handles,
            Dictionary<Id, List<bool>> paused, Dictionary<Id, List<double>> played)
        {
            var renderer2D = UnityEngineHost.Ensure().Renderer2D;
            foreach (var unit in handles.Keys)
            {
                paused[unit] = new List<bool>();
                played[unit] = new List<double>();
            }

            RequireInternalBus(bootstrap).Subscribe<SimTickFinishedEvent>(SimEventKeys.TickFinished, _ =>
            {
                foreach (var pair in handles)
                {
                    paused[pair.Key].Add(renderer2D.IsParticlePaused(pair.Value));
                    played[pair.Key].Add(renderer2D.GetParticlePlayedSeconds(pair.Value) ?? double.NaN);
                }
            });
        }

        [UnityTest]
        public IEnumerator FeelEngine_Hit_ParticleFreezeLayer_PausesOwnedParticlesByProfileTicks_BystanderUnaffected()
        {
            var bootstrap = BuildFeelBootstrap(feelOn: true);
            var scene = new FeelScene();
            yield return SetUpFeelScene(bootstrap, scene);
            DeclareBufferedAttack(bootstrap);
            Assert.IsNull(bootstrap.HotReload, "缺省不开热重载：不挂热重载组件");

            var feel = bootstrap.Gameplay!.Feel!;
            feel.Feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.AttackerHitstopMs, FeelOp.Set, FeelValue.Of(50)));
            feel.Feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.TargetHitstopMs, FeelOp.Set, FeelValue.Of(110)));
            feel.Feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.ImpactProfileRef, FeelOp.Set, FeelValue.Of(FeelFreezeProfileId)));
            var judging = feel.Resolver.ResolveJudging(scene.Player);
            var step = Time.fixedDeltaTime;
            var attackerTicks = FeelCalibration.MillisecondsToTicks(judging.GetNumber(FeelFieldNames.AttackerHitstopMs), step);
            var targetTicks = FeelCalibration.MillisecondsToTicks(judging.GetNumber(FeelFieldNames.TargetHitstopMs), step);
            Assert.IsTrue(attackerTicks > 0 && targetTicks > 0 && attackerTicks != targetTicks);

            var handles = new Dictionary<Id, ParticleHandle>();
            yield return AttachBurnEffects(bootstrap, scene, handles);
            var paused = new Dictionary<Id, List<bool>>();
            var played = new Dictionary<Id, List<double>>();
            ObserveParticles(bootstrap, handles, paused, played);

            yield return AttackUntilHit(scene);
            // 粒子观测不随 AttackUntilHit 的 ClearObservations 清理：命中那一轮的顿帧是唯一的暂停段（未命中的轮次没有顿帧、不产生暂停），下面只数暂停 tick。

            var renderer2D = UnityEngineHost.Ensure().Renderer2D;
            Assert.AreEqual(attackerTicks, paused[scene.Player].Count(f => f), "攻击方名下的持续特效暂停的 tick 数 = 攻击方顿帧档案换算值");
            Assert.AreEqual(targetTicks, paused[scene.Target].Count(f => f), "被击方名下的持续特效暂停的 tick 数 = 被击方顿帧档案换算值");
            Assert.AreEqual(0, paused[scene.Bystander].Count(f => f), "旁观单位的特效不暂停");

            // 不变量一：暂停期间时间轴确实没推进（序列帧累计播放时间在这一段里不增长）；旁观单位的特效整个观测窗口里持续推进。
            foreach (var unit in new[] { scene.Player, scene.Target })
            {
                var trace = paused[unit];
                var first = trace.IndexOf(true);
                Assert.GreaterOrEqual(first, 0);
                Assert.AreEqual(trace.Count(f => f), trace.Skip(first).TakeWhile(f => f).Count(), "暂停应为连续一段");
                var last = first + trace.Count(f => f) - 1;
                Assert.AreEqual(played[unit][first], played[unit][last], 1e-9, "暂停期间 " + unit + " 名下特效的播放时间不应增长");
                Assert.IsFalse(renderer2D.IsParticlePaused(handles[unit]), "顿帧结束后特效应已恢复");
            }

            Assert.Greater(played[scene.Bystander].Last(), played[scene.Bystander].First(), "旁观单位的特效应持续推进");

            // 不变量二：被暂停的特效实例没有因此消失（暂停不等于销毁，结束后还能继续）。
            foreach (var handle in handles.Values)
            {
                Assert.IsTrue(renderer2D.GetParticlePlayedSeconds(handle).HasValue, "特效实例仍在");
            }
        }

        [UnityTest]
        public IEnumerator FeelEngine_Hit_ParticleLayerNotDeclared_RigFreezesButParticlesKeepRunning()
        {
            // 反馈包没声明 freeze_layers（缺省只冻骨骼/序列帧）：rig 照冻，粒子照常推进。
            var bootstrap = BuildFeelBootstrap(feelOn: true);
            var scene = new FeelScene();
            yield return SetUpFeelScene(bootstrap, scene);
            DeclareBufferedAttack(bootstrap);
            var feel = bootstrap.Gameplay!.Feel!;
            feel.Feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.TargetHitstopMs, FeelOp.Set, FeelValue.Of(110)));
            feel.Feel.DebugOverrides!.SetGlobal(new FeelWrite(FeelFieldNames.ImpactProfileRef, FeelOp.Set, FeelValue.Of(FeelImpactProfileId)));

            var handles = new Dictionary<Id, ParticleHandle>();
            yield return AttachBurnEffects(bootstrap, scene, handles);
            var paused = new Dictionary<Id, List<bool>>();
            var played = new Dictionary<Id, List<double>>();
            ObserveParticles(bootstrap, handles, paused, played);

            yield return AttackUntilHit(scene);

            Assert.Greater(scene.Frozen[scene.Target].Count(f => f), 0, "rig 照常被冻结");
            foreach (var unit in handles.Keys)
            {
                Assert.AreEqual(0, paused[unit].Count(f => f), "没声明 freeze_layers.particles 时单位 " + unit + " 名下特效不应暂停");
            }
        }

        [UnityTest]
        public IEnumerator FeelEngine_DataHotReload_FeelPresetEdit_UnitReadsNewValue_AndLoadCompletedPublished()
        {
            var bootstrap = BuildFeelBootstrap(feelOn: true, hotReload: true, presetOverride: true);
            Assert.IsFalse(bootstrap.BootstrapFailed, "开手感 + 开热重载的引导装配不应失败");
            var hot = bootstrap.HotReload;
            Assert.IsNotNull(hot, "EnableDataHotReload 为真且数据加载成功后应挂载热重载组件");
            var feel = bootstrap.Gameplay!.Feel;
            Assert.IsNotNull(feel, "开手感：玩法装配应带手感系统");

            var loadCompleted = 0;
            RequireInternalBus(bootstrap).Subscribe(DataRegistryEventKeys.LoadCompleted, _ => loadCompleted++);

            var field = FeelFieldNames.BufferMs;
            var before = feel!.Resolver.ResolveJudging(bootstrap.PlayerId).GetNumber(field);
            var expected = before + 80; // 期望值由编辑本身算出（旧值 + 增量），不写死裸数

            var presetPath = Path.Combine(_feelOverlayDir!, "feel", "feel.preset.json");
            var original = File.ReadAllText(presetPath);
            var edited = new System.Text.RegularExpressions.Regex("\"buffer_ms\"\\s*:\\s*[0-9.]+").Replace(
                original, "\"buffer_ms\": " + expected.ToString(System.Globalization.CultureInfo.InvariantCulture), 1);
            Assert.AreNotEqual(original, edited, "测试前置条件：叠加根里的手感预设文件应被改写");
            File.WriteAllText(presetPath, edited);

            var deadline = Time.realtimeSinceStartup + 10f;
            var after = before;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                hot!.ProcessPendingChanges();
                after = feel.Resolver.ResolveJudging(bootstrap.PlayerId).GetNumber(field);
                if (after != before) break;
            }

            Assert.AreEqual(expected, after, 1e-9, "热重载后单位应读到新的缓冲窗口毫秒（不重启）");
            Assert.GreaterOrEqual(loadCompleted, 1, "热重载应在 Reload 之后发布 data.load_completed（手感热加载依赖它）");
            Assert.GreaterOrEqual(hot!.ReloadCount, 1);
        }

        [UnityTest]
        public IEnumerator FeelEngine_DataHotReload_NotEnabled_NoComponent_EditIsNotPickedUp()
        {
            // 缺省关闭：不挂组件，改文件后手感数据保持装配时的值（热重载是开发期显式打开的能力）。
            var bootstrap = BuildFeelBootstrap(feelOn: true, hotReload: false, presetOverride: true);
            Assert.IsFalse(bootstrap.BootstrapFailed);
            Assert.IsNull(bootstrap.HotReload);
            Assert.IsNull(bootstrap.GetComponent<Adapter.Unity.Bootstrap.BootstrapDataHotReload>());

            var feel = bootstrap.Gameplay!.Feel!;
            var before = feel.Resolver.ResolveJudging(bootstrap.PlayerId).GetNumber(FeelFieldNames.BufferMs);
            var presetPath = Path.Combine(_feelOverlayDir!, "feel", "feel.preset.json");
            File.WriteAllText(presetPath, File.ReadAllText(presetPath).Replace("\"buffer_ms\": ", "\"buffer_ms\": 9"));

            for (var i = 0; i < 30; i++) yield return null;

            Assert.AreEqual(before, feel.Resolver.ResolveJudging(bootstrap.PlayerId).GetNumber(FeelFieldNames.BufferMs), 1e-9, "不开热重载时编辑文件不应改变已装配的数据");
        }

        [UnityTest]
        public IEnumerator FeelEngine_FeelOff_SameScene_DirectCastPathUnchanged_NothingFreezesNoImpulse()
        {
            var bootstrap = BuildFeelBootstrap(feelOn: false);
            var scene = new FeelScene();
            yield return SetUpFeelScene(bootstrap, scene);
            Assert.IsNull(bootstrap.Gameplay!.Feel, "不开手感：玩法装配不带手感系统（与此前一致）");

            var camera = UnityEngineHost.Ensure().Camera;
            var before = camera.ImpulseCount;
            yield return TapAttack();
            yield return RunSteps(60);

            // 现状路径：引擎侧直接提交默认普攻技能，恰一次；没有顿帧、没有镜头冲量。
            Assert.AreEqual(1, scene.Casts.Count, "不开手感时按键只经原直接施法路径，施法一次");
            Assert.AreNotEqual(FeelSwingSkillId, scene.Casts[0].SkillId.Value);
            foreach (var pair in scene.Frozen)
            {
                Assert.AreEqual(0, pair.Value.Count(f => f), "不开手感时单位 " + pair.Key + " 的 rig 不应被冻结");
            }

            Assert.AreEqual(before, camera.ImpulseCount, "不开手感时不触发镜头冲量");
        }
    }
}
