#nullable enable
// ModelIntegrationTests：W6-B 端到端验收，覆盖任务书两条要求——
//   1) 武器风格：装备主手武器后 Attack 播 auto_attack_anim（model 一侧；sprite 一侧的决策逻辑见
//      AnimClipResolverTests.cs，二者共用同一个 AnimClipResolver，不重复验证决策本身，这里额外验证
//      "真的经 IRenderer3D.PlayAnim 播到了 Animator 的 attack 状态"这一层引擎落地）。
//   2) 命中帧同步端到端：AnimKeyframeDriven 策略下，combat.damage_dealt 规则（sync: hit_frame）的
//      动作应当延迟到攻击方真实 Animator 播放到命中帧才入队，未收到时保持 pending，超时兜底立即释放
//      （见 HitFrameSyncPolicy 类型注释）——presentation/feedback_binder 侧已用假 rig 验证过等待队列
//      本身的正确性（FeedbackBinderHitFrameSyncTests.cs），本文件验证的是"真实 UnityRenderer3D 播放
//      占位剪辑确实会经 CharacterRigHitFrameSource 触发这条等待队列"这一整条链路，不是重复造轮子。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EngineAdapter;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using Core.Rules.Common;
using NUnit.Framework;
using Presentation.Assembly;
using Presentation.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.FeedbackBinder.Core;
using Presentation.Render;
using Presentation.VfxSfx.Contracts;
using Presentation.VfxSfx.Core;
using UnityEngine;
using UnityEngine.TestTools;
using FeedbackBinderCore = Presentation.FeedbackBinder.Core.FeedbackBinder;

namespace Adapter.Unity.Tests.Runtime
{
    /// <summary>永远返回构造期指定实体 id 的假求值宿主工厂——本文件构造的规则从不声明
    /// <c>condition</c>，<c>CreateFor</c> 的返回值不会被求值，仅需满足签名。</summary>
    internal sealed class NullExprHostFactory : IExprHostFactory
    {
        public IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent) => null!;
    }

    /// <summary>最小 <see cref="IFeedbackSink"/> 测试替身：只记录 <see cref="Flash"/> 调用次数，其余
    /// 方法空实现（同 presentation/feedback_binder/tests 既有 RecordingFeedbackSink 同一惯例，本文件
    /// 不能直接引用该内部类型——不同程序集，见 AnimClipResolverTests.cs 顶部同类判断记录）。</summary>
    internal sealed class RecordingFlashSink : IFeedbackSink
    {
        public int FlashCount;
        public event Action? PendingPlaybackChanged;
        public bool HasPendingPlayback => false;
        public void FloatingText(Id entityId, Id styleId, string text) { }
        public void PlayVfx(Id vfxId, FeedbackAttachSpec attach) { }
        public void PlaySfx(Id sfxId, Vec2? at) { }
        public void Freeze(double durationMs) { }
        public void ShakeCamera(Id profileId) { }
        public void Flash(Id entityId, Id profileId) => FlashCount++;
    }

    public sealed class ModelIntegrationTests : PlayModeTestBase
    {
        private static readonly Id ModelHeroLogicalId = new Id("creature.sample_model_hero");
        private static readonly Id ModelSwordLogicalId = new Id("item.sample_model_sword");

        private (IEventBus Bus, IDataRegistryView Registry, IDisplayInfoRegistry DisplayInfo, UnityEngineHost Host) BuildFixture()
        {
            var host = UnityEngineHost.Ensure();
            var definitions = EventKeys.All.Select(k => new EventDefinition(k, k.Domain, Array.Empty<string>())).ToList();
            var catalog = EventCatalog.FromDefinitions(definitions);
            var bus = new EventBus(catalog, new EventBusOptions { StrictCatalog = false, AuditLog = false });

            var repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            var contentFs = new UnityFileSystem(readOnlyContentMode: true, contentRoot: repoRoot);
            var sampleSource = new FileSystemDataSource(contentFs, "data/_sample");
            var frameworkSource = new FileSystemDataSource(contentFs, "data/_framework");

            var options = PresentationSchemaCatalog.CreateOptions();
            options.FailOnUnknownTable = false;
            var registry = new DataRegistry(sampleSource, bus, options);
            PresentationSchemaCatalog.RegisterAll(registry);
            var report = registry.LoadAll(new IDataSource[] { frameworkSource, sampleSource });
            Assert.IsFalse(report.IsBlocking, "测试数据集应当能无阻断加载：" + string.Join("; ", report.Issues));

            var displayInfo = new DisplayInfoRegistry(registry, bus);
            return (bus, registry, displayInfo, host);
        }

        /// <summary>核心验收 1：装备主手武器（模拟装备解析结果固定返回 item.sample_model_sword）后，
        /// Attack 状态切换应当经 AnimClipResolver 解出 display.weapon_style.sample_model_sword 的
        /// AutoAttackAnim（"anim.attack"，与占位 Animator 的真实状态名同名），并真的经
        /// IRenderer3D.PlayAnim 播放到该 Animator 状态——不是默认剪辑表的
        /// "anim.default.<displayId>.attack"。</summary>
        [UnityTest]
        public IEnumerator EquippedWeapon_AttackState_PlaysAutoAttackAnimOnRealAnimator()
        {
            var fx = BuildFixture();
            var entityId = new Id("unit.model_weapon_style_test");

            MainHandWeaponTemplateResolver resolver = unitId => unitId.Equals(entityId) ? (Id?)ModelSwordLogicalId : null;
            using var weaponStyleSource = new EquipmentWeaponStyleSource(fx.Bus, resolver, fx.DisplayInfo);

            var factory = new UnityViewFactory(
                fx.Host.Renderer2D, new RenderConventionHost(), fx.DisplayInfo, fx.Host.ResourceLoader,
                bus: fx.Bus, dataRegistry: fx.Registry, renderer3D: fx.Host.Renderer3D, weaponStyleSource: weaponStyleSource);

            var view = (UnityModelView)factory.CreateView(ViewKind.Unit, ModelHeroLogicalId, entityId);
            view.Bind(entityId);
            var handle = view.TryGetModelHandle()!.Value;
            var renderer3D = (UnityRenderer3D)fx.Host.Renderer3D;

            fx.Bus.PublishImmediate(new SkillCastStartEvent(entityId, new Id("skill.model_weapon_style_attack"), castTime: 0.0));

            var deadline = Time.realtimeSinceStartup + 3f;
            while (!renderer3D.IsPlayingState(handle, "attack") && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsTrue(renderer3D.IsPlayingState(handle, "attack"), "武器风格的 AutoAttackAnim（anim.attack）应当真的驱动 Animator 进入 attack 状态");

            view.Destroy();
        }

        /// <summary>核心验收 2：命中帧同步端到端——见文件顶部注释。</summary>
        [UnityTest]
        public IEnumerator HitFrameSync_AnimKeyframeDriven_DelaysFlashUntilRealHitFrame_ThenTimeoutFallback()
        {
            var fx = BuildFixture();
            var attackerId = new Id("unit.model_hitframe_sync_attacker");
            var targetId = new Id("unit.model_hitframe_sync_target");

            var info = fx.DisplayInfo.Lookup(ModelHeroLogicalId)!;
            var renderer3D = (UnityRenderer3D)fx.Host.Renderer3D;
            var handle = renderer3D.CreateModelInstance(info.Model!.ModelRef);
            var rig = new ModelCharacterRig(
                attackerId, renderer3D, handle, info,
                new RenderOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven });

            var hitFrameSource = new CharacterRigHitFrameSource();
            hitFrameSource.RegisterRig(attackerId, rig);

            var sink = new RecordingFlashSink();
            var rule = new FeedbackRule(
                id: new Id("feedback.binding.model_hitframe_sync_test"),
                eventKey: RulesEventKeys.CombatDamageDealt,
                condition: null,
                actions: new List<FeedbackAction> { new FlashAction(new Id("flash.profile.model_hitframe_sync_test"), FeedbackAttachTarget.Source) },
                sync: FeedbackSyncMode.HitFrame);

            // 第一条：验证"到达命中帧才释放"；第二条（超时更短）留给下面单独的超时用例，避免同一个
            // binder 内两次入队互相影响判定时机。
            var binderOptions = new FeedbackOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven };
            using var binder = new FeedbackBinderCore(
                fx.Bus, new NullExprHostFactory(), new List<FeedbackRule> { rule }, sink,
                options: binderOptions, hitFrameSource: hitFrameSource);

            fx.Bus.PublishImmediate(new CombatDamageDealtEvent(
                attackerId, targetId, new Id("school.physical"), 5.0, isCrit: false, HitResult.Hit));

            Assert.AreEqual(0, sink.FlashCount, "命中帧到达前不应当播放 Flash 动作");
            Assert.IsTrue(binder.HasPendingPlayback, "命中帧同步等待期间应当计入待回放内容");

            rig.PlayClip(new Id("anim.attack"), loop: false, speed: 1.0);

            var deadline = Time.realtimeSinceStartup + 5f;
            while (sink.FlashCount == 0 && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(1, sink.FlashCount, "真实 Animator 播放到 50% 触发命中帧事件后，应当经 CharacterRigHitFrameSource 释放等待中的 Flash 动作");
            Assert.IsFalse(binder.HasPendingPlayback);

            rig.Dispose();
            renderer3D.DestroyModelInstance(handle);
        }

        /// <summary>超时兜底：命中帧事件迟迟不到达时，等待动作最终仍应被释放（不是永久悬挂）。</summary>
        [Test]
        public void HitFrameSync_TimeoutFallback_ReleasesActionAnyway()
        {
            var fx = BuildFixture();
            var attackerId = new Id("unit.model_hitframe_timeout_attacker");
            var targetId = new Id("unit.model_hitframe_timeout_target");

            var info = fx.DisplayInfo.Lookup(ModelHeroLogicalId)!;
            var renderer3D = (UnityRenderer3D)fx.Host.Renderer3D;
            var handle = renderer3D.CreateModelInstance(info.Model!.ModelRef);
            var rig = new ModelCharacterRig(
                attackerId, renderer3D, handle, info,
                new RenderOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven });

            var hitFrameSource = new CharacterRigHitFrameSource();
            hitFrameSource.RegisterRig(attackerId, rig);

            var sink = new RecordingFlashSink();
            var rule = new FeedbackRule(
                id: new Id("feedback.binding.model_hitframe_timeout_test"),
                eventKey: RulesEventKeys.CombatDamageDealt,
                condition: null,
                actions: new List<FeedbackAction> { new FlashAction(new Id("flash.profile.model_hitframe_timeout_test"), FeedbackAttachTarget.Source) },
                sync: FeedbackSyncMode.HitFrame);

            var binderOptions = new FeedbackOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven, HitFrameSyncTimeoutSeconds = 0.2 };
            using var binder = new FeedbackBinderCore(
                fx.Bus, new NullExprHostFactory(), new List<FeedbackRule> { rule }, sink,
                options: binderOptions, hitFrameSource: hitFrameSource);

            fx.Bus.PublishImmediate(new CombatDamageDealtEvent(
                attackerId, targetId, new Id("school.physical"), 5.0, isCrit: false, HitResult.Hit));
            Assert.AreEqual(0, sink.FlashCount);

            // 不播放任何动画（命中帧永远不会到达）：只推进超时计时。
            binder.Update(0.3);

            Assert.AreEqual(1, sink.FlashCount, "命中帧超时未到达时应当按兜底策略立即释放");

            rig.Dispose();
            renderer3D.DestroyModelInstance(handle);
        }
    }
}
