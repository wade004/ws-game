#nullable enable
// AnimReplayAndFinishEndToEndTests：H5b 根治验收——经生产装配根 Adapter.Unity.Bootstrap.
// GameFoundationBootstrap 构造，验证游戏侧复核发现的三项问题已经端到端根治（不是只在单元测试
// 层面验证机制本身，见 HitFrameSyncEndToEndTests.cs 同款"经生产装配根"惯例）：
//   1) model 路线补齐完成回调（UnityRenderer3D.Tick + ModelCharacterRig.AnimFinishedEventId）——
//      Attack/Hit 播放完毕后 AnimStateMachine 能正确回落，不会永久卡死。
//   2) 同状态重入重播（AnimStateMachine.StateRetriggered + AnimClipResolver + UnityRenderer3D.PlayAnim
//      的 Animator.Play 硬切分支）——连续攻击每次都重播一遍完整剪辑，不会被"状态数值没变"吞掉。
//   3) 循环剪辑（idle/move）继续保持幂等，不受上述两项改动影响。
//
// sprite/model 两条路线各一组，覆盖同一份验收清单：连续普攻 3 次（第二次在首次动画结束前到达）
// 应当每次都重播且各自的命中帧事件触发一次；受击（Hit）后继续攻击应当能进入 Attack；Cast 后应当
// 回落 Idle；循环剪辑（Idle）不应触发完成回落。
//
// 判断记录（不改场景资产，反射注入私有字段，同 HitFrameSyncEndToEndTests.cs 一贯手法）：本文件同样
// 需要打开命中帧同步开关，model 组另需切玩家外形为 creature.sample_model_hero。
//
// 判断记录（sprite 组的"命中帧各触发一次"子项额外补一份真实多帧剪辑，其余子项复用生产默认剪辑表）：
// data/_sample 目前只给 sprite 型攻击/受击剪辑声明了 resource_ref（anim.sample_hero_attack/
// anim.sample_hero_hit），但没有对应的真实多帧占位资源落盘（不同于 model 路线——占位模型资产由
// GeneratePlaceholderModelAssets.cs 实际生成，见该脚本）；UnityViewFactory.RegisterDefaultClips 在
// 资源缺失时按"缺表现资源不阻断游戏"策略退化为不带关键帧的单帧剪辑（仍然会真实播放并触发
// OnComplete，只是没有命中帧标记可触发）。"命中帧各触发一次"子项因此额外经反射拿到生产装配根
// 内部真实持有的 UnityFrameAnimPlayer 实例，用 RegisterClip 给同一个 clipId（默认剪辑表已分配好的
// "attack"状态 clipId）原地登记一份带 hit_frame 关键帧的真实三帧剪辑——同一个 clipId、同一个生产
// UnityFrameAnimPlayer 实例、同一套 AnimStateMachine/AnimClipResolver/OnComplete 接线全部保持生产
// 原样不变，只是把"这个 clipId 具体对应哪些帧"换成一份真实存在关键帧的数据，等价于"具体游戏提供了
// 真实的攻击序列帧资源"这一即将发生的真实场景，不是绕开生产链路的另一套断言方式。其余子项（受击
// 回落、Cast 回落、循环剪辑不回落）不依赖关键帧，直接用生产默认剪辑表（哪怕是单帧退化剪辑，仍然是
// 真实的 IFrameAnimPlayer.Play/OnComplete 生命周期）。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Adapter.Unity.Bootstrap;
using Adapter.Unity.Presentation;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Rules.Common;
using NUnit.Framework;
using Presentation.Render;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class AnimReplayAndFinishEndToEndTests : PlayModeTestBase
    {
        private GameObject? _go;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // 判断记录同 HitFrameSyncEndToEndTests.TearDown：不手动调用 World.ClearAll()，直接
            // Destroy(_go) 触发 GameFoundationBootstrap.OnDestroy 既有清理路径即可。
            if (_go != null)
            {
                UnityEngine.Object.Destroy(_go);
                _go = null;
            }
            yield return null;
        }

        private static void CleanupStaleSharedCompositionRoots()
        {
            foreach (var stale in UnityEngine.Object.FindObjectsByType<GameFoundationBootstrap>(FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(stale.gameObject);
            }
        }

        /// <summary>见文件顶部判断记录：反射打开命中帧同步开关，<paramref name="playerTemplateId"/>
        /// 非空时额外切玩家外形为该 model 型模板。</summary>
        private GameFoundationBootstrap BuildInactiveBootstrap(string? playerTemplateId)
        {
            CleanupStaleSharedCompositionRoots();

            var go = new GameObject("AnimReplayAndFinishEndToEndTest");
            go.SetActive(false);
            var bootstrap = go.AddComponent<GameFoundationBootstrap>();
            var type = typeof(GameFoundationBootstrap);

            var hitFrameSyncField = type.GetField("_hitFrameSyncEnabled", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(hitFrameSyncField, "GameFoundationBootstrap 应当有 _hitFrameSyncEnabled 私有字段");
            hitFrameSyncField!.SetValue(bootstrap, true);

            if (playerTemplateId != null)
            {
                var playerTemplateField = type.GetField("_playerTemplateId", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(playerTemplateField, "GameFoundationBootstrap 应当有 _playerTemplateId 私有字段");
                playerTemplateField!.SetValue(bootstrap, playerTemplateId);
            }

            _go = go;
            go.SetActive(true);
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

        private static IEnumerator WaitUntilOrFail(Func<bool> condition, string failureMessage, float timeoutSeconds = 5f)
        {
            var deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Assert.IsTrue(condition(), failureMessage);
        }

        /// <summary>判断记录：data/_sample 的 spawn.sample_beast_field 生成一只挂
        /// ai.profile.sample_melee（perception_radius 20）的野兽，出生点 (5,0) 与玩家默认出生点
        /// （Vec2.Zero）距离仅 5，落在感知范围内——真实世界模拟持续推进（固定步没有暂停），本文件
        /// 多数用例需要等待数秒真实时间才能观察到"动画自然播放完成"，这段时间足够野兽发起 AI 决策
        /// 主动攻击玩家，其命中会经真实 combat.damage_dealt 反复触发/重触发玩家的 Hit 状态，与本文件
        /// 要验证的"这一次由测试合成的 Hit/Attack 事件"互相串扰（同 HitFrameSyncEndToEndTests.cs 文件
        /// 顶部判断记录"容易与本用例要验证的……互相串扰"，那里靠"等待窗口足够短"侧面规避，本文件的
        /// 等待窗口明显更长，不能依赖同样的运气）。本方法在每条用例的世界真正建好之后、发起任何合成
        /// 战斗事件之前，直接退注野兽的 AI 登记（同 FrameworkResidentHost.OnUnitDied 判断记录"AiHost.
        /// UnregisterUnit 是契约方法"一致的用法），使它此后不再被 AiTickHandler 决策/移动/攻击，从根上
        /// 消除这条串扰来源，不依赖等待时机的运气。</summary>
        private static void DisableBeastAiToAvoidInterference(GameFoundationBootstrap bootstrap)
        {
            Assert.IsTrue(bootstrap.BeastEntityId.HasValue, "灰盒场景应当已经通过 spawn.sample_beast_field 生成一只生物");
            var ai = bootstrap.Gameplay!.Carriers.Rules.Ai;
            if (System.Linq.Enumerable.Contains(ai.RegisteredUnitIds, bootstrap.BeastEntityId!.Value))
            {
                ai.UnregisterUnit(bootstrap.BeastEntityId.Value);
            }
        }

        /// <summary>见文件顶部判断记录：给生产 UnityFrameAnimPlayer 里已经分配好的"attack"状态
        /// clipId 原地登记一份真实三帧、50% 处带 hit_frame 关键帧的剪辑（帧率 10fps，总时长 0.3
        /// 秒），使命中帧真的能被观察到。</summary>
        private static void PatchSpriteAttackClipWithRealHitFrame(GameFoundationBootstrap bootstrap, Id playerId)
        {
            var factory = bootstrap.ViewFactory!;
            var playersField = typeof(UnityViewFactory).GetField("_animPlayersByEntity", BindingFlags.NonPublic | BindingFlags.Instance);
            var clipsField = typeof(UnityViewFactory).GetField("_animClipsByEntity", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(playersField, "UnityViewFactory 应当有 _animPlayersByEntity 私有字段");
            Assert.IsNotNull(clipsField, "UnityViewFactory 应当有 _animClipsByEntity 私有字段");

            var players = (Dictionary<Id, UnityFrameAnimPlayer>)playersField!.GetValue(factory)!;
            var clips = (Dictionary<Id, IReadOnlyDictionary<string, Id>>)clipsField!.GetValue(factory)!;
            Assert.IsTrue(players.TryGetValue(playerId, out var player), "玩家应当已经挂接默认 UnityFrameAnimPlayer");
            Assert.IsTrue(clips.TryGetValue(playerId, out var table) && table.TryGetValue("attack", out var attackClipId), "玩家应当已经登记默认 attack 剪辑 id");

            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            var sprite = Sprite.Create(texture, new UnityEngine.Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
            var frames = new[] { sprite, sprite, sprite };
            var keyframes = new Dictionary<string, int> { ["hit_frame"] = 1 };

            player!.RegisterClip(attackClipId, frames, frameRate: 10.0, keyframes: keyframes);
        }

        // ------------------------------------------------------------------
        // sprite 组
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Sprite_ConsecutiveAttacks_EachReplaysAndFiresHitFrameOnce()
        {
            var bootstrap = BuildInactiveBootstrap(playerTemplateId: null);
            Assert.IsFalse(bootstrap.BootstrapFailed);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
            DisableBeastAiToAvoidInterference(bootstrap);

            Assert.IsTrue(bootstrap.Presentation!.ViewBinder.TryGetView(bootstrap.PlayerId, out var playerView) && playerView != null);
            var rig = ((IHasCharacterRig)playerView!).Rig;
            var stateMachine = bootstrap.ViewFactory!.AnimStateMachineForTests!;

            PatchSpriteAttackClipWithRealHitFrame(bootstrap, bootstrap.PlayerId);

            var hitFrameCount = 0;
            ((IHitFrameEmitter)rig).HitFrameReached += _ => hitFrameCount++;

            var bus = RequireInternalBus(bootstrap);
            var attackSkillId = new Id("skill.sample_strike");

            for (var i = 1; i <= 3; i++)
            {
                if (i > 1)
                {
                    // 验证"第二/三次攻击在前一次动画播完之前到达"：此刻状态机应当仍停在 Attack
                    // （尚未因为完成回调回落到运动态）。
                    Assert.AreEqual(AnimState.Attack, stateMachine.GetState(bootstrap.PlayerId),
                        $"第 {i} 次普攻发起前，前一次的 Attack 播放形态应当还没有播完（仍处于 Attack 状态）");
                }

                bus.PublishImmediate(new SkillCastStartEvent(bootstrap.PlayerId, attackSkillId, castTime: 0.0));
                Assert.AreEqual(AnimState.Attack, stateMachine.GetState(bootstrap.PlayerId), $"第 {i} 次普攻应当（重）进入 Attack 状态");

                var expectedCount = i;
                yield return WaitUntilOrFail(() => hitFrameCount >= expectedCount,
                    $"第 {i} 次普攻应当重播一遍完整剪辑并再触发一次命中帧事件（累计应达到 {expectedCount} 次）");
            }

            Assert.AreEqual(3, hitFrameCount, "三次普攻应当各自恰好触发一次命中帧事件，不多不少");
        }

        [UnityTest]
        public IEnumerator Sprite_Hit_FinishesAndFallsBack_ThenAttackEntersNormally()
        {
            var bootstrap = BuildInactiveBootstrap(playerTemplateId: null);
            Assert.IsFalse(bootstrap.BootstrapFailed);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
            DisableBeastAiToAvoidInterference(bootstrap);
            // 判断记录（H5b 首轮排障发现）：entity.created 经固定步事件派发才真正创建 View（见
            // GameFoundationBootstrap.BuildWorld 判断记录），单纯等够帧数不等于确认 View 真的已经就绪
            // ——直接断言取到 View，比"等固定次数的帧"更可靠，避免偶发时序竞争让接下来发布的合成事件
            // 找不到已登记的 rig/播放器，静默不生效（AnimStateMachine 的状态记账本身不依赖 View 是否
            // 存在，会误判"进入了 Hit/Cast"却实际上没有播放任何剪辑）。
            Assert.IsTrue(bootstrap.Presentation!.ViewBinder.TryGetView(bootstrap.PlayerId, out var readyView) && readyView != null,
                "玩家实体应当已经绑定 View（entity.created 已经过至少一次固定步派发）");

            var stateMachine = bootstrap.ViewFactory!.AnimStateMachineForTests!;
            var bus = RequireInternalBus(bootstrap);
            var attackerId = new Id("unit.h5b_sprite_hit_attacker");

            bus.PublishImmediate(new CombatDamageDealtEvent(
                attackerId, bootstrap.PlayerId, new Id("school.physical"), 5.0, isCrit: false, HitResult.Hit));
            Assert.AreEqual(AnimState.Hit, stateMachine.GetState(bootstrap.PlayerId), "受击应当进入 Hit 状态");

            yield return WaitUntilOrFail(() => stateMachine.GetState(bootstrap.PlayerId) == AnimState.Idle,
                "Hit 播放完成后应当自动回落到运动态（Idle）", timeoutSeconds: 8f);

            bus.PublishImmediate(new SkillCastStartEvent(bootstrap.PlayerId, new Id("skill.sample_strike"), castTime: 0.0));
            Assert.AreEqual(AnimState.Attack, stateMachine.GetState(bootstrap.PlayerId), "Hit 回落之后应当能正常进入 Attack，不应被卡死在 Hit");
        }

        [UnityTest]
        public IEnumerator Sprite_Cast_FallsBackToIdle_LoopIdleClip_DoesNotTriggerFinishFallback()
        {
            var bootstrap = BuildInactiveBootstrap(playerTemplateId: null);
            Assert.IsFalse(bootstrap.BootstrapFailed);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
            DisableBeastAiToAvoidInterference(bootstrap);
            // 判断记录（H5b 首轮排障发现）：entity.created 经固定步事件派发才真正创建 View（见
            // GameFoundationBootstrap.BuildWorld 判断记录），单纯等够帧数不等于确认 View 真的已经就绪
            // ——直接断言取到 View，比"等固定次数的帧"更可靠，避免偶发时序竞争让接下来发布的合成事件
            // 找不到已登记的 rig/播放器，静默不生效（AnimStateMachine 的状态记账本身不依赖 View 是否
            // 存在，会误判"进入了 Hit/Cast"却实际上没有播放任何剪辑）。
            Assert.IsTrue(bootstrap.Presentation!.ViewBinder.TryGetView(bootstrap.PlayerId, out var readyView) && readyView != null,
                "玩家实体应当已经绑定 View（entity.created 已经过至少一次固定步派发）");

            var stateMachine = bootstrap.ViewFactory!.AnimStateMachineForTests!;
            var bus = RequireInternalBus(bootstrap);
            var skill1Id = new Id("skill.sample_burn");

            bus.PublishImmediate(new SkillCastStartEvent(bootstrap.PlayerId, skill1Id, castTime: 1.0));
            Assert.AreEqual(AnimState.Cast, stateMachine.GetState(bootstrap.PlayerId), "读条技能应当进入 Cast 状态");

            bus.PublishImmediate(new SkillCastSuccessEvent(bootstrap.PlayerId, skill1Id, Array.Empty<Id>()));
            Assert.AreEqual(AnimState.Idle, stateMachine.GetState(bootstrap.PlayerId), "施法收尾事件应当立即让 Cast 回落到 Idle（视觉时长与读条时长天然同步）");

            // 循环剪辑（Idle）不应触发完成回落：等待超过一个循环周期，状态应当仍然是 Idle，不应有任何
            // 意外的状态切换（若循环剪辑被误判为"播放完成"，会被 NotifyTransientStateFinished 拉回
            // 运动态本身——由于本来就在 Idle，误判不会改变 GetState 的观察结果，因此额外用事件计数
            // 交叉验证：StateChanged 在这段等待期间不应再触发）。
            var stateChangeCount = 0;
            stateMachine.StateChanged += (_, _, _) => stateChangeCount++;
            yield return new WaitForSecondsRealtime(1.5f);

            Assert.AreEqual(AnimState.Idle, stateMachine.GetState(bootstrap.PlayerId));
            Assert.AreEqual(0, stateChangeCount, "循环剪辑（Idle）播放期间不应触发任何状态切换（完成回落只对非循环瞬态生效）");
        }

        // ------------------------------------------------------------------
        // model 组
        // ------------------------------------------------------------------

        private const string ModelHeroTemplateId = "creature.sample_model_hero";

        [UnityTest]
        public IEnumerator Model_ConsecutiveAttacks_EachReplaysAndFiresHitFrameOnce()
        {
            var bootstrap = BuildInactiveBootstrap(ModelHeroTemplateId);
            Assert.IsFalse(bootstrap.BootstrapFailed, "切玩家为 model 型外形后，共享引导装配不应失败");
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
            DisableBeastAiToAvoidInterference(bootstrap);

            Assert.IsTrue(bootstrap.Presentation!.ViewBinder.TryGetView(bootstrap.PlayerId, out var playerView) && playerView != null);
            Assert.IsInstanceOf<IHasCharacterRig>(playerView, "切到 creature.sample_model_hero 后，玩家 View 应当是持有 ICharacterRig 的类型（UnityModelView）");
            var rig = ((IHasCharacterRig)playerView!).Rig;
            var stateMachine = bootstrap.ViewFactory!.AnimStateMachineForTests!;

            var hitFrameCount = 0;
            ((IHitFrameEmitter)rig).HitFrameReached += _ => hitFrameCount++;

            var bus = RequireInternalBus(bootstrap);
            var attackSkillId = new Id("skill.sample_strike");

            for (var i = 1; i <= 3; i++)
            {
                if (i > 1)
                {
                    Assert.AreEqual(AnimState.Attack, stateMachine.GetState(bootstrap.PlayerId),
                        $"第 {i} 次普攻发起前，前一次的 Attack 播放形态应当还没有播完（仍处于 Attack 状态）");
                }

                bus.PublishImmediate(new SkillCastStartEvent(bootstrap.PlayerId, attackSkillId, castTime: 0.0));
                Assert.AreEqual(AnimState.Attack, stateMachine.GetState(bootstrap.PlayerId), $"第 {i} 次普攻应当（重）进入 Attack 状态");

                var expectedCount = i;
                yield return WaitUntilOrFail(() => hitFrameCount >= expectedCount,
                    $"第 {i} 次普攻应当重播一遍完整剪辑并再触发一次命中帧事件（累计应达到 {expectedCount} 次）");
            }

            Assert.AreEqual(3, hitFrameCount, "三次普攻应当各自恰好触发一次命中帧事件，不多不少");
        }

        [UnityTest]
        public IEnumerator Model_Hit_FinishesAndFallsBack_ThenAttackEntersNormally()
        {
            var bootstrap = BuildInactiveBootstrap(ModelHeroTemplateId);
            Assert.IsFalse(bootstrap.BootstrapFailed);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
            DisableBeastAiToAvoidInterference(bootstrap);
            // 判断记录（H5b 首轮排障发现）：entity.created 经固定步事件派发才真正创建 View（见
            // GameFoundationBootstrap.BuildWorld 判断记录），单纯等够帧数不等于确认 View 真的已经就绪
            // ——直接断言取到 View，比"等固定次数的帧"更可靠，避免偶发时序竞争让接下来发布的合成事件
            // 找不到已登记的 rig/播放器，静默不生效（AnimStateMachine 的状态记账本身不依赖 View 是否
            // 存在，会误判"进入了 Hit/Cast"却实际上没有播放任何剪辑）。
            Assert.IsTrue(bootstrap.Presentation!.ViewBinder.TryGetView(bootstrap.PlayerId, out var readyView) && readyView != null,
                "玩家实体应当已经绑定 View（entity.created 已经过至少一次固定步派发）");

            var stateMachine = bootstrap.ViewFactory!.AnimStateMachineForTests!;
            var bus = RequireInternalBus(bootstrap);
            var attackerId = new Id("unit.h5b_model_hit_attacker");

            bus.PublishImmediate(new CombatDamageDealtEvent(
                attackerId, bootstrap.PlayerId, new Id("school.physical"), 5.0, isCrit: false, HitResult.Hit));
            Assert.AreEqual(AnimState.Hit, stateMachine.GetState(bootstrap.PlayerId), "受击应当进入 Hit 状态");

            // 占位内容 hit.anim 时长 0.3 秒（见 GeneratePlaceholderModelAssets.cs H5b 新增），加上
            // UnityRenderer3D.Tick 逐帧检测，几帧真实时间内即应完成回落。
            yield return WaitUntilOrFail(() => stateMachine.GetState(bootstrap.PlayerId) == AnimState.Idle,
                "Hit 播放完成后应当自动回落到运动态（Idle）——H5b 根治前 model 路线永久卡死在 Hit", timeoutSeconds: 5f);

            bus.PublishImmediate(new SkillCastStartEvent(bootstrap.PlayerId, new Id("skill.sample_strike"), castTime: 0.0));
            Assert.AreEqual(AnimState.Attack, stateMachine.GetState(bootstrap.PlayerId), "Hit 回落之后应当能正常进入 Attack，不应被卡死在 Hit");
        }

        [UnityTest]
        public IEnumerator Model_Cast_FallsBackToIdle_LoopIdleClip_DoesNotTriggerFinishFallback()
        {
            var bootstrap = BuildInactiveBootstrap(ModelHeroTemplateId);
            Assert.IsFalse(bootstrap.BootstrapFailed);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
            DisableBeastAiToAvoidInterference(bootstrap);
            // 判断记录（H5b 首轮排障发现）：entity.created 经固定步事件派发才真正创建 View（见
            // GameFoundationBootstrap.BuildWorld 判断记录），单纯等够帧数不等于确认 View 真的已经就绪
            // ——直接断言取到 View，比"等固定次数的帧"更可靠，避免偶发时序竞争让接下来发布的合成事件
            // 找不到已登记的 rig/播放器，静默不生效（AnimStateMachine 的状态记账本身不依赖 View 是否
            // 存在，会误判"进入了 Hit/Cast"却实际上没有播放任何剪辑）。
            Assert.IsTrue(bootstrap.Presentation!.ViewBinder.TryGetView(bootstrap.PlayerId, out var readyView) && readyView != null,
                "玩家实体应当已经绑定 View（entity.created 已经过至少一次固定步派发）");

            var stateMachine = bootstrap.ViewFactory!.AnimStateMachineForTests!;
            var bus = RequireInternalBus(bootstrap);
            var skill1Id = new Id("skill.sample_burn");

            bus.PublishImmediate(new SkillCastStartEvent(bootstrap.PlayerId, skill1Id, castTime: 1.0));
            Assert.AreEqual(AnimState.Cast, stateMachine.GetState(bootstrap.PlayerId), "读条技能应当进入 Cast 状态");

            bus.PublishImmediate(new SkillCastSuccessEvent(bootstrap.PlayerId, skill1Id, Array.Empty<Id>()));
            Assert.AreEqual(AnimState.Idle, stateMachine.GetState(bootstrap.PlayerId), "施法收尾事件应当立即让 Cast 回落到 Idle");

            // idle.anim 循环 1.0 秒（见 GeneratePlaceholderModelAssets.cs）：UnityRenderer3D.PlayAnim
            // 对 loop:true 的剪辑恒把 FinishNotified 置 true，Tick 因此永不对它检测——等待超过一个
            // 循环周期，状态应当仍然是 Idle，不应有任何意外的状态切换。
            var stateChangeCount = 0;
            stateMachine.StateChanged += (_, _, _) => stateChangeCount++;
            yield return new WaitForSecondsRealtime(1.5f);

            Assert.AreEqual(AnimState.Idle, stateMachine.GetState(bootstrap.PlayerId));
            Assert.AreEqual(0, stateChangeCount, "循环剪辑（Idle）播放期间不应触发任何状态切换（完成回落只对非循环瞬态生效）");
        }
    }
}
