using System;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.HookRegistry;
using Core.Gameplay.Assembly;
using Presentation.Assembly;
using Presentation.VfxSfx.Contracts;
using Xunit;

namespace Tests.Presentation.Assembly
{
    /// <summary>
    /// 包装 <see cref="SubscriptionTrackingEventBus"/>：指定归属类型（处理函数声明所在最外层类型全名）的订阅，其
    /// <see cref="SubscriptionHandle"/> 在 Dispose 时先真正退订、再抛 <see cref="InvalidOperationException"/>——
    /// 用来给 <see cref="PresentationAssembly.Dispose"/> 造"某个子系统释放时抛异常"的替身。
    /// </summary>
    internal sealed class ThrowingDisposeEventBus : IEventBus
    {
        private readonly SubscriptionTrackingEventBus _inner;
        private readonly HashSet<string> _throwingOwners;

        public ThrowingDisposeEventBus(SubscriptionTrackingEventBus inner, IEnumerable<string> throwingOwners)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _throwingOwners = new HashSet<string>(throwingOwners, StringComparer.Ordinal);
        }

        public SubscriptionHandle Subscribe(Id key, Core.Foundation.EventBus.EventHandler handler) =>
            Wrap(handler, _inner.Subscribe(key, handler));

        public SubscriptionHandle Subscribe<T>(Id key, Core.Foundation.EventBus.EventHandler<T> handler) where T : IEvent =>
            Wrap(handler, _inner.Subscribe(key, handler));

        public void Enqueue(IEvent evt) => _inner.Enqueue(evt);

        public int DispatchPending() => _inner.DispatchPending();

        public void PublishImmediate(IEvent evt) => _inner.PublishImmediate(evt);

        public IDisposable SuppressDispatch() => _inner.SuppressDispatch();

        /// <summary>抛出的异常消息：固定前缀 + 归属类型全名，测试按它识别"是谁抛的"。</summary>
        public static string MessageFor(string owner) => "dispose probe failure: " + owner;

        private SubscriptionHandle Wrap(Delegate handler, SubscriptionHandle real)
        {
            var type = handler.Method.DeclaringType;
            while (type?.DeclaringType != null)
            {
                type = type.DeclaringType;
            }

            var owner = type?.FullName ?? "<unknown>";
            if (!_throwingOwners.Contains(owner))
            {
                return real;
            }

            return new SubscriptionHandle(() =>
            {
                real.Dispose();
                throw new InvalidOperationException(MessageFor(owner));
            });
        }
    }

    /// <summary>
    /// T-M41（拍板）：<see cref="PresentationAssembly.Dispose"/> 逐子系统隔离——单个子系统释放时抛异常，记一条诊断、
    /// 继续释放其余子系统、最后重抛第一个异常。复现用例：用会抛的订阅句柄做子系统替身，修复前"其后的全部子系统都没被释放"。
    /// </summary>
    public partial class PresentationAssemblyTests
    {
        private const string ViewBinderOwner = "Presentation.ViewBinding.ViewBinder";
        private const string HudOwner = "Presentation.Ui.HudViewModel";

        private static PresentationAssembly BuildWithThrowingDispose(
            out GameplayAssembly gameplay, out SubscriptionTrackingEventBus tracking, params string[] throwingOwners)
        {
            var inner = new SubscriptionTrackingEventBus(new EventBus(
                EventCatalog.FromDefinitions(Array.Empty<EventDefinition>()),
                new EventBusOptions { StrictCatalog = false }));
            tracking = inner;
            return Build(
                out gameplay, out _, out _, out _, busOverride: new ThrowingDisposeEventBus(inner, throwingOwners),
                discreteTimeModel: true);
        }

        [Fact]
        public void Dispose_OneSubsystemThrows_StillReleasesAllOthers_AndRethrowsThatException()
        {
            var presentation = BuildWithThrowingDispose(out var gameplay, out var tracking, ViewBinderOwner);
            var postLoadBefore = gameplay.Hooks.CallbackCount(WellKnownHooks.ScenePostLoad);
            Assert.NotEmpty(tracking.LiveOwnersUnder(PresentationOwnerPrefix));

            var thrown = Assert.Throws<InvalidOperationException>(presentation.Dispose);

            Assert.Equal(ThrowingDisposeEventBus.MessageFor(ViewBinderOwner), thrown.Message);
            // 除了"抛出者自己"之外，表现层订阅全部退订（修复前：ViewBinder 之后的每个子系统都没释放）。
            var leaked = tracking.LiveOwnersUnder(PresentationOwnerPrefix).Where(o => o != ViewBinderOwner).Distinct().ToList();
            Assert.True(leaked.Count == 0, "抛异常之后仍未释放的子系统：" + string.Join(", ", leaked));
            // MapLayerHost 在 SceneRouter 上挂的钩子也被注销（它排在 ViewBinder 之后）。
            Assert.Equal(postLoadBefore - 1, gameplay.Hooks.CallbackCount(WellKnownHooks.ScenePostLoad));
        }

        [Fact]
        public void Dispose_OneSubsystemThrows_RecordsADiagnosticNamingTheFailure()
        {
            var presentation = BuildWithThrowingDispose(out _, out _, ViewBinderOwner);
            var recorder = Assert.IsType<PresentationDiagnosticsRecorder>(presentation.FeedbackSinkDiagnostics);
            var warningsBefore = recorder.Warnings.Count;

            Assert.Throws<InvalidOperationException>(presentation.Dispose);

            var added = recorder.Warnings.Skip(warningsBefore).ToList();
            var single = Assert.Single(added);
            Assert.Contains(nameof(PresentationAssembly.ViewBinder), single);
            Assert.Contains(ThrowingDisposeEventBus.MessageFor(ViewBinderOwner), single);
        }

        [Fact]
        public void Dispose_TwoSubsystemsThrow_RethrowsTheFirstInDisposeOrder_AndRecordsBoth()
        {
            // ViewBinder 在释放顺序里排在 Hud 之前：重抛的必须是 ViewBinder 的异常，Hud 的只进诊断。
            var throwing = new[] { ViewBinderOwner, HudOwner };
            var presentation = BuildWithThrowingDispose(out _, out var tracking, throwing);
            var recorder = Assert.IsType<PresentationDiagnosticsRecorder>(presentation.FeedbackSinkDiagnostics);
            var warningsBefore = recorder.Warnings.Count;

            var thrown = Assert.Throws<InvalidOperationException>(presentation.Dispose);

            Assert.Equal(ThrowingDisposeEventBus.MessageFor(ViewBinderOwner), thrown.Message);
            var added = recorder.Warnings.Skip(warningsBefore).ToList();
            Assert.Equal(throwing.Length, added.Count);
            Assert.Contains(ThrowingDisposeEventBus.MessageFor(ViewBinderOwner), added[0]);
            Assert.Contains(ThrowingDisposeEventBus.MessageFor(HudOwner), added[1]);
            var leaked = tracking.LiveOwnersUnder(PresentationOwnerPrefix)
                .Where(o => !throwing.Contains(o)).Distinct().ToList();
            Assert.True(leaked.Count == 0, "抛异常之后仍未释放的子系统：" + string.Join(", ", leaked));
        }

        [Fact]
        public void Dispose_AfterAThrowingDispose_SecondCallIsANoOp()
        {
            var presentation = BuildWithThrowingDispose(out _, out var tracking, ViewBinderOwner);
            Assert.Throws<InvalidOperationException>(presentation.Dispose);
            var liveAfterFirst = tracking.LiveOwners().Count;
            var recorder = Assert.IsType<PresentationDiagnosticsRecorder>(presentation.FeedbackSinkDiagnostics);
            var warningsAfterFirst = recorder.Warnings.Count;

            var second = Record.Exception(presentation.Dispose);

            Assert.Null(second);
            Assert.Equal(liveAfterFirst, tracking.LiveOwners().Count);
            Assert.Equal(warningsAfterFirst, recorder.Warnings.Count);
        }

        [Fact]
        public void Dispose_WhenNothingThrows_RecordsNoDisposeDiagnostic()
        {
            var presentation = BuildWithThrowingDispose(out _, out _);
            var recorder = Assert.IsType<PresentationDiagnosticsRecorder>(presentation.FeedbackSinkDiagnostics);
            var warningsBefore = recorder.Warnings.Count;

            presentation.Dispose();

            Assert.Equal(warningsBefore, recorder.Warnings.Count);
        }
    }
}
