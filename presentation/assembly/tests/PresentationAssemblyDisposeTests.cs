using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.HookRegistry;
using Core.Foundation.SceneRouter;
using Core.Foundation.SimLoop;
using Core.Gameplay.Assembly;
using Core.Rules.Combat;
using Core.Rules.Common;
using Presentation.Assembly;
using Presentation.Camera;
using Presentation.Common;
using Presentation.FeedbackBinder.Contracts;
using Presentation.Render;
using Presentation.ViewBinding;
using Xunit;

namespace Tests.Presentation.Assembly
{
    /// <summary>
    /// 包装真实 <see cref="EventBus"/>，为每条订阅记下"处理函数声明所在的最外层类型"（订阅方归属），并在对应
    /// <see cref="SubscriptionHandle"/> 被 Dispose 时注销——<see cref="IEventBus"/> 没有订阅计数接口，Dispose
    /// 完整性探针靠它把"装配根及其子系统是否全部退订"变成可断言的数字。只观测、不改变派发行为
    /// （含转发 <see cref="SuppressDispatch"/>，否则会落到接口默认的空操作）。
    /// </summary>
    internal sealed class SubscriptionTrackingEventBus : IEventBus
    {
        private sealed class Entry
        {
            public readonly Id Key;
            public readonly string Owner;

            public Entry(Id key, string owner)
            {
                Key = key;
                Owner = owner;
            }
        }

        private readonly IEventBus _inner;
        private readonly List<Entry> _live = new List<Entry>();

        public SubscriptionTrackingEventBus(IEventBus inner) => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        /// <summary>当前仍有效的订阅，按"处理函数声明所在最外层类型全名"列出（含重复，一条订阅一项）。</summary>
        public IReadOnlyList<string> LiveOwners() => _live.Select(e => e.Owner).ToList();

        public IReadOnlyList<string> LiveOwnersUnder(string namespacePrefix) =>
            _live.Select(e => e.Owner).Where(o => o.StartsWith(namespacePrefix, StringComparison.Ordinal)).ToList();

        public SubscriptionHandle Subscribe(Id key, Core.Foundation.EventBus.EventHandler handler) => Track(key, handler, _inner.Subscribe(key, handler));

        public SubscriptionHandle Subscribe<T>(Id key, Core.Foundation.EventBus.EventHandler<T> handler) where T : IEvent =>
            Track(key, handler, _inner.Subscribe(key, handler));

        public void Enqueue(IEvent evt) => _inner.Enqueue(evt);

        public int DispatchPending() => _inner.DispatchPending();

        public void PublishImmediate(IEvent evt) => _inner.PublishImmediate(evt);

        public IDisposable SuppressDispatch() => _inner.SuppressDispatch();

        private SubscriptionHandle Track(Id key, Delegate handler, SubscriptionHandle inner)
        {
            var entry = new Entry(key, OwnerOf(handler));
            _live.Add(entry);
            return new SubscriptionHandle(() =>
            {
                _live.Remove(entry);
                inner.Dispose();
            });
        }

        private static string OwnerOf(Delegate handler)
        {
            var type = handler.Method.DeclaringType;
            while (type?.DeclaringType != null)
            {
                type = type.DeclaringType;
            }
            return type?.FullName ?? "<unknown>";
        }
    }

    /// <summary>
    /// 生产装配接线探针（测试覆盖梳理 T-M41）：<see cref="PresentationAssembly.Dispose"/> 的完整性——构造真实装配后
    /// Dispose，装配根自身与所有持有订阅的子系统在事件总线上的订阅全部退订、场景钩子注销、再派发事件无副作用；
    /// 二次 Dispose 幂等。订阅数通过 <see cref="SubscriptionTrackingEventBus"/> 观测，不写裸数。
    /// </summary>
    public partial class PresentationAssemblyTests
    {
        private const string PresentationOwnerPrefix = "Presentation.";

        private static PresentationAssembly BuildTracked(
            out GameplayAssembly gameplay, out WorldSim world, out StubEngine engine, out SubscriptionTrackingEventBus bus,
            PresentationAssemblyOptions? options = null, IViewFactory? viewFactory = null, IHitFrameSource? hitFrameSource = null)
        {
            var tracking = new SubscriptionTrackingEventBus(new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false }));
            bus = tracking;
            // discreteTimeModel:true：让 gameplay.TimeModelSwitch 非空，装配根才会建立自己的三个订阅
            // （combat.entered/combat.left/unit.died 跟随自动切队列模式），这条路径也要被 Dispose 覆盖。
            return Build(
                out gameplay, out world, out engine, out _, options, viewFactory,
                busOverride: tracking, hitFrameSource: hitFrameSource, discreteTimeModel: true);
        }

        /// <summary>装配根公开属性里实现 <see cref="IDisposable"/> 的子系统（按属性名）。</summary>
        private static IReadOnlyList<(string Name, object Value)> DisposablePublicSubsystems(PresentationAssembly presentation) =>
            typeof(PresentationAssembly)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => (p.Name, Value: p.GetValue(presentation)))
                .Where(x => x.Value is IDisposable)
                .Select(x => (x.Name, x.Value!))
                .ToList();

        [Fact]
        public void Dispose_RetiresEveryPresentationOwnedBusSubscription_AndSceneHooks_LeavesOthersUntouched()
        {
            var presentation = BuildTracked(out var gameplay, out _, out _, out var bus);
            var before = bus.LiveOwnersUnder(PresentationOwnerPrefix);
            var othersBefore = bus.LiveOwners().Count - before.Count;
            var postLoadBefore = gameplay.Hooks.CallbackCount(WellKnownHooks.ScenePostLoad);
            var preUnloadBefore = gameplay.Hooks.CallbackCount(WellKnownHooks.ScenePreUnload);

            // 非空证明：装配根确实建立了表现层订阅，且包含装配根自己在离散模式下的那一组。
            Assert.NotEmpty(before);
            Assert.Contains(typeof(PresentationAssembly).FullName, before);

            presentation.Dispose();

            var remaining = bus.LiveOwnersUnder(PresentationOwnerPrefix);
            Assert.True(remaining.Count == 0, "Dispose 后仍有表现层订阅未退订：" + string.Join(", ", remaining.GroupBy(o => o).Select(g => g.Key + "×" + g.Count())));
            // 非表现层订阅（gameplay/core 自己的）不受影响。
            Assert.Equal(othersBefore, bus.LiveOwners().Count);
            // MapLayerHost 在 SceneRouter 上挂的 post_load/pre_unload 钩子各注销一个；MapMusicHost（P4 备忘 2，默认开）另挂一个 post_load。
            Assert.Equal(postLoadBefore - 2, gameplay.Hooks.CallbackCount(WellKnownHooks.ScenePostLoad));
            Assert.Equal(preUnloadBefore - 1, gameplay.Hooks.CallbackCount(WellKnownHooks.ScenePreUnload));
        }

        [Fact]
        public void Dispose_CoversEverySubscribingPresentationSubsystem_NamedInExpectedOwnerList()
        {
            var presentation = BuildTracked(out _, out _, out _, out var bus);

            var owners = bus.LiveOwnersUnder(PresentationOwnerPrefix).Distinct().OrderBy(o => o, StringComparer.Ordinal).ToList();

            // 明确的订阅方清单：新增任何一个在总线上订阅的表现层组件，必须同时加进这里并确认 PresentationAssembly.Dispose
            // 会释放它（上一条用例保证清单内的全部订阅在 Dispose 后归零）。
            var expected = new[]
            {
                "Presentation.Assembly.PresentationAssembly",
                "Presentation.Camera.CameraHost",
                "Presentation.FeedbackBinder.Core.FeedbackBinder",
                "Presentation.Shell.ShellHost",
                "Presentation.Shell.ShellViewModel",
                "Presentation.Ui.ActionBarViewModel",
                "Presentation.Ui.CharacterStatsViewModel",
                "Presentation.Ui.DialogViewModel",
                "Presentation.Ui.EquipmentViewModel",
                "Presentation.Ui.HudViewModel",
                "Presentation.Ui.InventoryViewModel",
                "Presentation.Ui.PauseMenuViewModel",
                "Presentation.Ui.QuestLogViewModel",
                "Presentation.Ui.SaveSlotsViewModel",
                "Presentation.Ui.SettingsViewModel",
                "Presentation.Ui.ShopViewModel",
                "Presentation.Ui.SkillBookViewModel",
                "Presentation.ViewBinding.StrideEmitter",
                "Presentation.ViewBinding.ViewBinder",
            }.OrderBy(o => o, StringComparer.Ordinal).ToList();
            Assert.Equal(expected, owners);
        }

        [Fact]
        public void Dispose_EveryPublicIDisposableSubsystem_IsObservableAsSubscriberOrSceneHookOwner()
        {
            var presentation = BuildTracked(out _, out _, out _, out var bus);
            var owners = new HashSet<string>(bus.LiveOwnersUnder(PresentationOwnerPrefix));

            var subsystems = DisposablePublicSubsystems(presentation);

            // 装配根把 Dispose 责任落在自己身上的每个子系统，要么在总线上有订阅（Dispose 后归零已由
            // Dispose_RetiresEveryPresentationOwnedBusSubscription_* 断言），要么是 MapLayerHost/MapMusicHost（场景钩子，同上条断言）。
            // 新增 IDisposable 公开子系统却没有可观测的释放效果时，这里会红，提醒补上观测手段。
            Assert.NotEmpty(subsystems);
            foreach (var (name, value) in subsystems)
            {
                var typeName = value.GetType().FullName!;
                Assert.True(
                    owners.Contains(typeName) || value is MapLayerHost || value is global::Presentation.VfxSfx.Core.MapMusicHost,
                    $"公开 IDisposable 子系统 {name}（{typeName}）没有任何可观测的订阅/钩子，Dispose 完整性无法被验证");
            }
        }

        [Fact]
        public void Dispose_ReleasesHitFrameSourceSubscription_HeldByFeedbackBinderPolicy()
        {
            var source = new FakeHitFrameSource();
            var options = new PresentationAssemblyOptions
            {
                FeedbackOptions = new FeedbackOptions { HitFrameSync = HitFrameSyncStrategy.AnimKeyframeDriven },
            };
            var presentation = BuildTracked(out _, out _, out _, out _, options, hitFrameSource: source);
            Assert.Equal(1, source.SubscriberCount);

            presentation.Dispose();

            Assert.Equal(0, source.SubscriberCount);
        }

        [Fact]
        public void Dispose_CalledTwice_IsIdempotent_NoThrow_NoFurtherChange()
        {
            var presentation = BuildTracked(out var gameplay, out _, out _, out var bus);

            presentation.Dispose();
            var liveAfterFirst = bus.LiveOwners().Count;
            var postLoadAfterFirst = gameplay.Hooks.CallbackCount(WellKnownHooks.ScenePostLoad);
            var preUnloadAfterFirst = gameplay.Hooks.CallbackCount(WellKnownHooks.ScenePreUnload);

            var second = Record.Exception(presentation.Dispose);
            var third = Record.Exception(presentation.Dispose);

            Assert.Null(second);
            Assert.Null(third);
            Assert.Equal(liveAfterFirst, bus.LiveOwners().Count);
            Assert.Equal(postLoadAfterFirst, gameplay.Hooks.CallbackCount(WellKnownHooks.ScenePostLoad));
            Assert.Equal(preUnloadAfterFirst, gameplay.Hooks.CallbackCount(WellKnownHooks.ScenePreUnload));
        }

        [Fact]
        public void Dispose_ThenEventsPublished_SubsystemsNoLongerReact()
        {
            var statId = new Id("stat.max_health");
            var customFollow = new Id("unit.dispose_probe_custom_follow");
            var options = new PresentationAssemblyOptions
            {
                CharacterStatConfig = new[] { (statId, new Id("l10n.stat.sample_max_health.name")) },
                CameraHostOptions = new CameraHostOptions(
                    resetFollowOnSceneLoadFinished: true, phaseProfileSwitch: null, followTargetResolverOnReset: () => customFollow),
            };
            var factory = new Tests.PresentationViewBinding.FakeViewFactory();
            var presentation = BuildTracked(out var gameplay, out _, out _, out var bus, options, viewFactory: factory);
            var playerId = gameplay.PlayerUnitProvider();
            var stats = gameplay.Carriers.Rules.Stats;
            var baseValue = stats.GetBase(playerId, statId);

            // --- 对照（Dispose 之前各子系统确实在响应）---
            var changedBefore = baseValue + 1;
            stats.SetBase(playerId, statId, changedBefore);
            bus.DispatchPending();
            Assert.Equal(stats.GetStat(playerId, statId), presentation.CharacterStats.Entries[0].Value);

            bus.PublishImmediate(new SceneLoadFinishedEvent(new Id("world.dispose_probe_a")));
            Assert.Equal(customFollow, presentation.Camera.FollowEntityId);
            presentation.Camera.Follow(playerId);

            var viewsBefore = presentation.ViewBinder.Count;
            bus.PublishImmediate(new Core.Foundation.SimLoop.EntityCreatedEvent(
                new Id("unit.dispose_probe_entity_a"), "player", new Id("display.sample_player")));
            Assert.Equal(viewsBefore + 1, presentation.ViewBinder.Count);

            Assert.Equal(QueueMode.Immediate, presentation.Feedback.Queue.Mode);

            // --- Dispose 之后：同样的事件不再产生任何反应 ---
            presentation.Dispose();

            var changedAfter = changedBefore + 1;
            stats.SetBase(playerId, statId, changedAfter);
            bus.DispatchPending();
            Assert.NotEqual(stats.GetStat(playerId, statId), presentation.CharacterStats.Entries[0].Value);

            bus.PublishImmediate(new SceneLoadFinishedEvent(new Id("world.dispose_probe_b")));
            Assert.Equal(playerId, presentation.Camera.FollowEntityId);

            var viewsAfter = presentation.ViewBinder.Count;
            bus.PublishImmediate(new Core.Foundation.SimLoop.EntityCreatedEvent(
                new Id("unit.dispose_probe_entity_b"), "player", new Id("display.sample_player")));
            Assert.Equal(viewsAfter, presentation.ViewBinder.Count);

            // 装配根自己的订阅：进战后 TimeModelSwitch 切到离散，但队列模式不再被装配根跟随切换。
            bus.PublishImmediate(new CombatEnteredEvent(playerId, new Id("unit.dispose_probe_hostile")));
            Assert.Equal(TimeModelMode.Discrete, gameplay.TimeModelSwitch!.CurrentMode);
            Assert.Equal(QueueMode.Immediate, presentation.Feedback.Queue.Mode);
        }
    }
}
