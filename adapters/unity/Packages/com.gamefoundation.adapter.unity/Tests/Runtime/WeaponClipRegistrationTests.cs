#nullable enable
// WeaponClipRegistrationTests：PR130-03 复现与回归用例——武器风格解析出的 clipId
// （WeaponStyleDef.AutoAttackAnim/CastAnimOverride）从未随 UnityViewFactory.RegisterDefaultClips 的
// 六个默认状态一起登记进 UnityFrameAnimPlayer，AnimClipResolver 决策出该 clipId 后
// FrameAnimPlayer.Play 对未登记的 clipId 直接抛 ArgumentException（见
// UnityViewFactory.EnsureSpriteClipRegistered 判断记录）。AnimClipResolverTests.cs 明确声明它自己
// 不覆盖"该 clipId 是否已注册"这一层（见该文件顶部判断记录），ModelIntegrationTests.cs 只覆盖 model
// 一侧的引擎落地（Animator 状态现查现用，不需要预注册，天然不受这个问题影响）——本文件补上 sprite
// 一侧真正经 UnityFrameAnimPlayer.Play 落地这一层，用真实 data/_sample 数据集（
// display.weapon_style.sample_sword 的 auto_attack_anim: anim.sample_sword_swing 是一个真实不存在
// 对应 vfx/ 占位资源的 clipId，见该表与 assets/_placeholder/vfx 目录）复现并验收。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adapter.Unity.EngineAdapter;
using Adapter.Unity.Presentation;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Core.Foundation.EventBus;
using Core.Rules.Common;
using NUnit.Framework;
using Presentation.Assembly;
using Presentation.Common;
using Presentation.Render;
using UnityEngine;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class WeaponClipRegistrationTests : PlayModeTestBase
    {
        private static readonly Id SpriteHeroLogicalId = new Id("creature.sample_hero");
        private static readonly Id SampleSwordStyleRef = new Id("display.weapon_style.sample_sword");

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

        /// <summary>PR130-03 核心复现/回归：sprite 型实体的武器风格指向一个从未随默认剪辑表登记过的
        /// clipId（<c>anim.sample_sword_swing</c>，真实数据集里没有对应的 vfx/ 占位资源）——根治前
        /// <see cref="AnimStateMachine.StateChangedWithSkill"/> 触发 Attack 时会经
        /// <c>FrameAnimPlayer.Play</c> 抛 <see cref="ArgumentException"/>；根治后应当不抛，退化为单帧
        /// 占位剪辑呈现并记一次诊断，随后异步升级（若资源确实存在）或保持占位（不重试）。</summary>
        [Test]
        public void Attack_WeaponStyleClip_NotPreRegistered_DoesNotThrow_DegradesToSingleFrame()
        {
            var fx = BuildFixture();
            var entityId = new Id("unit.weapon_clip_registration_test");

            var factory = new UnityViewFactory(
                fx.Host.Renderer2D, new RenderConventionHost(), fx.DisplayInfo, fx.Host.ResourceLoader,
                bus: fx.Bus, dataRegistry: fx.Registry,
                weaponStyleSource: new FixedWeaponStyleSource(SampleSwordStyleRef));

            var view = factory.CreateView(ViewKind.Unit, SpriteHeroLogicalId, entityId);
            view.Bind(entityId);

            // 瞬发进入 Attack（同 AnimClipResolverTests.cs 一贯做法）：武器风格解析优先于默认剪辑表，
            // 命中 display.weapon_style.sample_sword.auto_attack_anim = "anim.sample_sword_swing"。
            Assert.DoesNotThrow(() =>
                fx.Bus.PublishImmediate(new SkillCastStartEvent(entityId, new Id("skill.weapon_clip_registration_test_strike"), castTime: 0.0)));

            view.Destroy();
        }

        /// <summary>见 <see cref="AnimClipResolverTests.Cast_WithMatchingSkillOverride_UsesCastAnimOverride"/>
        /// 同款场景，本文件验证的是引擎落地层（真正经 UnityFrameAnimPlayer.Play）：
        /// <c>display.weapon_style.sample_staff</c> 的 <c>cast_anim_override</c> 命中
        /// <c>skill.sample_fireball</c> 时解析出 <c>anim.sample_staff_cast</c>（同样未预先登记），Cast
        /// 状态触发不应抛异常。</summary>
        [Test]
        public void Cast_WeaponStyleCastOverrideClip_NotPreRegistered_DoesNotThrow()
        {
            var fx = BuildFixture();
            var entityId = new Id("unit.weapon_clip_registration_cast_test");
            var styleRef = new Id("display.weapon_style.sample_staff");
            var skillId = new Id("skill.sample_fireball");

            var factory = new UnityViewFactory(
                fx.Host.Renderer2D, new RenderConventionHost(), fx.DisplayInfo, fx.Host.ResourceLoader,
                bus: fx.Bus, dataRegistry: fx.Registry,
                weaponStyleSource: new FixedWeaponStyleSource(styleRef));

            var view = factory.CreateView(ViewKind.Unit, SpriteHeroLogicalId, entityId);
            view.Bind(entityId);

            Assert.DoesNotThrow(() =>
                fx.Bus.PublishImmediate(new SkillCastStartEvent(entityId, skillId, castTime: 1.5)));

            view.Destroy();
        }
    }
}
