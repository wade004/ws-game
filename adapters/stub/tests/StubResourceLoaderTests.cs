using System;
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Xunit;

namespace Tests.StubAdapters
{
    public class StubResourceLoaderTests
    {
        private static readonly Id Known = new Id("img.known");
        private static readonly Id Unknown = new Id("img.unknown");

        private sealed class Recorder
        {
            public readonly List<(Id Id, bool Success)> Calls = new List<(Id, bool)>();

            public void Callback(Id id, bool success) => Calls.Add((id, success));
        }

        // ---- 默认（同步回调）模式 ----

        [Fact]
        public void LoadAsync_RegisteredResource_CallsBackSynchronouslyWithSuccess_AndMarksLoaded()
        {
            var loader = new StubResourceLoader();
            loader.Register(Known);
            var rec = new Recorder();

            loader.LoadAsync(Known, ResourceKind.Image, rec.Callback);

            Assert.Equal(new[] { (Known, true) }, rec.Calls);
            Assert.True(loader.IsLoaded(Known));
            Assert.Equal(1.0, loader.GetLoadProgress(Known));
        }

        [Fact]
        public void LoadAsync_UnregisteredResource_CallsBackWithFailure_AndNeverMarksLoaded()
        {
            var loader = new StubResourceLoader();
            var rec = new Recorder();

            loader.LoadAsync(Unknown, ResourceKind.Image, rec.Callback);

            Assert.Equal(new[] { (Unknown, false) }, rec.Calls);
            Assert.False(loader.IsLoaded(Unknown));
            Assert.Equal(0.0, loader.GetLoadProgress(Unknown));
        }

        [Fact]
        public void LoadAsync_NullCallback_ThrowsArgumentNull_AndRecordsNoRequest()
        {
            var loader = new StubResourceLoader();
            Assert.Throws<ArgumentNullException>(() => loader.LoadAsync(Known, ResourceKind.Image, null!));
            Assert.Empty(loader.LoadRequests);
        }

        [Fact]
        public void LoadRequests_RecordEveryCallInOrderIncludingRepeatsAndKinds()
        {
            var loader = new StubResourceLoader();
            loader.Register(Known);
            var rec = new Recorder();

            loader.LoadAsync(Known, ResourceKind.Image, rec.Callback);
            loader.LoadAsync(Unknown, ResourceKind.Audio, rec.Callback);
            loader.LoadAsync(Known, ResourceKind.Image, rec.Callback);

            Assert.Equal(
                new[] { (Known, ResourceKind.Image), (Unknown, ResourceKind.Audio), (Known, ResourceKind.Image) },
                loader.LoadRequests);
        }

        [Fact]
        public void Unload_ClearsLoadedFlag_ButResourceStaysRegistered_SoReloadSucceeds()
        {
            var loader = new StubResourceLoader();
            loader.Register(Known);
            loader.LoadAsync(Known, ResourceKind.Image, new Recorder().Callback);
            loader.Unload(Known);

            Assert.False(loader.IsLoaded(Known));
            Assert.Equal(0.0, loader.GetLoadProgress(Known));

            var rec = new Recorder();
            loader.LoadAsync(Known, ResourceKind.Image, rec.Callback);
            Assert.Equal(new[] { (Known, true) }, rec.Calls);
        }

        [Fact]
        public void Unregister_MakesLaterLoadsFail_ButDoesNotUnloadAlreadyLoadedResource()
        {
            var loader = new StubResourceLoader();
            loader.Register(Known);
            loader.LoadAsync(Known, ResourceKind.Image, new Recorder().Callback);

            loader.Unregister(Known);
            Assert.True(loader.IsLoaded(Known));

            var rec = new Recorder();
            loader.LoadAsync(Known, ResourceKind.Image, rec.Callback);
            Assert.Equal(new[] { (Known, false) }, rec.Calls);
        }

        [Fact]
        public void Unload_OfNeverLoadedResource_IsHarmless()
        {
            var loader = new StubResourceLoader();
            Assert.Null(Record.Exception(() => loader.Unload(Unknown)));
        }

        [Fact]
        public void HintsOverload_IsInterfaceDefault_ForwardsToThreeArgLoadAndIgnoresHints()
        {
            var loader = new StubResourceLoader();
            loader.Register(Known);
            var rec = new Recorder();

            ((IResourceLoader)loader).LoadAsync(Known, ResourceKind.Effect, new ResourceLoadHints(new Id("spr.set")), rec.Callback);

            Assert.Equal(new[] { (Known, true) }, rec.Calls);
            Assert.Equal(new[] { (Known, ResourceKind.Effect) }, loader.LoadRequests);
        }

        // ---- DeferCallbacks 模式 ----

        [Fact]
        public void Deferred_LoadAsync_FiresNothingAndLeavesStateUntouched_UntilCompleted()
        {
            var loader = new StubResourceLoader { DeferCallbacks = true };
            loader.Register(Known);
            var rec = new Recorder();

            loader.LoadAsync(Known, ResourceKind.Image, rec.Callback);

            Assert.Empty(rec.Calls);
            Assert.True(loader.HasPending(Known));
            Assert.False(loader.IsLoaded(Known));
            Assert.Equal(0.0, loader.GetLoadProgress(Known));
            Assert.Single(loader.LoadRequests);
        }

        [Fact]
        public void Deferred_CompletePending_FiresSuccessOnce_MarksLoaded_AndClearsPending()
        {
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var rec = new Recorder();
            loader.LoadAsync(Known, ResourceKind.Image, rec.Callback);

            loader.CompletePending(Known);

            Assert.Equal(new[] { (Known, true) }, rec.Calls);
            Assert.True(loader.IsLoaded(Known));
            Assert.Equal(1.0, loader.GetLoadProgress(Known));
            Assert.False(loader.HasPending(Known));
        }

        [Fact]
        public void Deferred_FailPending_FiresFailureOnce_DoesNotMarkLoaded_AndClearsPending()
        {
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var rec = new Recorder();
            loader.LoadAsync(Known, ResourceKind.Image, rec.Callback);

            loader.FailPending(Known);

            Assert.Equal(new[] { (Known, false) }, rec.Calls);
            Assert.False(loader.IsLoaded(Known));
            Assert.False(loader.HasPending(Known));
        }

        [Fact]
        public void Deferred_SecondLoadWhilePending_Throws_AfterResolvingItIsAllowedAgain()
        {
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var rec = new Recorder();
            loader.LoadAsync(Known, ResourceKind.Image, rec.Callback);

            Assert.Throws<InvalidOperationException>(() => loader.LoadAsync(Known, ResourceKind.Image, rec.Callback));

            loader.FailPending(Known);
            Assert.Null(Record.Exception(() => loader.LoadAsync(Known, ResourceKind.Image, rec.Callback)));
            Assert.True(loader.HasPending(Known));
        }

        [Fact]
        public void Deferred_PendingRequestsOfDifferentResources_AreIndependent()
        {
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var rec = new Recorder();
            var other = new Id("img.other");
            loader.LoadAsync(Known, ResourceKind.Image, rec.Callback);
            loader.LoadAsync(other, ResourceKind.Image, rec.Callback);

            loader.CompletePending(other);

            Assert.Equal(new[] { (other, true) }, rec.Calls);
            Assert.True(loader.HasPending(Known));
            Assert.False(loader.HasPending(other));
        }

        [Fact]
        public void CompleteOrFailPending_WithoutPendingRequest_Throws_InBothModesAndAfterResolution()
        {
            var sync = new StubResourceLoader();
            Assert.Throws<InvalidOperationException>(() => sync.CompletePending(Known));
            Assert.Throws<InvalidOperationException>(() => sync.FailPending(Known));

            var deferred = new StubResourceLoader { DeferCallbacks = true };
            deferred.LoadAsync(Known, ResourceKind.Image, new Recorder().Callback);
            deferred.CompletePending(Known);
            Assert.Throws<InvalidOperationException>(() => deferred.CompletePending(Known));
            Assert.Throws<InvalidOperationException>(() => deferred.FailPending(Known));
        }

        [Fact]
        public void Deferred_RegistrationDoesNotDecideOutcome_ExplicitCompleteOrFailDoes()
        {
            // 延迟模式下结果由测试显式裁决：未登记的资源也可以被 CompletePending 判成功，
            // 登记过的也可以被 FailPending 判失败。
            var loader = new StubResourceLoader { DeferCallbacks = true };
            loader.Register(Known);
            var rec = new Recorder();
            loader.LoadAsync(Known, ResourceKind.Image, rec.Callback);
            loader.LoadAsync(Unknown, ResourceKind.Image, rec.Callback);

            loader.FailPending(Known);
            loader.CompletePending(Unknown);

            Assert.Equal(new[] { (Known, false), (Unknown, true) }, rec.Calls);
        }
    }
}
