// ResourceReferenceTrackerTests：诊断记录 diag-isolation.md 根治配套用例——覆盖新增的三参数
// EnsureLoading(Id, ResourceKind, LoadCallback?) 重载（取代此前固定传给 IResourceLoader.LoadAsync
// 的空操作回调，见 ResourceReferenceTracker 类型注释"资源首次加载的责任归属"与该重载判断记录）。
using System.Collections.Generic;
using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Presentation.Common;
using Xunit;

namespace Tests.PresentationCommon
{
    public class ResourceReferenceTrackerTests
    {
        [Fact]
        public void EnsureLoading_TwoArgOverload_StillLoadsExactlyOnce_NoCallbackRequired()
        {
            var loader = new StubResourceLoader();
            var tracker = new ResourceReferenceTracker(loader);
            var id = new Id("layer.hero__front__body");

            tracker.EnsureLoading(id, ResourceKind.Image);
            tracker.EnsureLoading(id, ResourceKind.Image);

            var requests = loader.LoadRequests.FindAll(r => r.ResourceId.Equals(id));
            Assert.Single(requests);
        }

        [Fact]
        public void EnsureLoading_WithCallback_SyncLoader_InvokedWithSuccessTrue()
        {
            var loader = new StubResourceLoader();
            var id = new Id("layer.hero__front__body");
            loader.Register(id);
            var tracker = new ResourceReferenceTracker(loader);

            Id? notifiedId = null;
            bool? notifiedSuccess = null;
            tracker.EnsureLoading(id, ResourceKind.Image, (rid, success) =>
            {
                notifiedId = rid;
                notifiedSuccess = success;
            });

            Assert.Equal(id, notifiedId);
            Assert.True(notifiedSuccess);
        }

        [Fact]
        public void EnsureLoading_WithCallback_SyncLoader_UnregisteredId_InvokedWithSuccessFalse()
        {
            var loader = new StubResourceLoader();
            var id = new Id("layer.hero__front__missing");
            var tracker = new ResourceReferenceTracker(loader);

            bool? notifiedSuccess = null;
            tracker.EnsureLoading(id, ResourceKind.Image, (_, success) => notifiedSuccess = success);

            Assert.False(notifiedSuccess);
        }

        [Fact]
        public void EnsureLoading_DeferredLoader_CallbackFiresOnlyAfterCompletePending()
        {
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var id = new Id("layer.hero__front__body");
            loader.Register(id);
            var tracker = new ResourceReferenceTracker(loader);

            var callCount = 0;
            tracker.EnsureLoading(id, ResourceKind.Image, (_, _) => callCount++);

            Assert.Equal(0, callCount);

            loader.CompletePending(id);

            Assert.Equal(1, callCount);
        }

        /// <summary>判断记录（同一 id 至多回调一次）：第二次 EnsureLoading 传入的回调因为
        /// _requested 已经记过该 id 而被直接丢弃、永远不会被调用——见该重载判断记录"去重不受
        /// onComplete 是否为 null 影响"。</summary>
        [Fact]
        public void EnsureLoading_SameIdTwice_SecondCallCallback_NeverInvoked()
        {
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var id = new Id("layer.hero__front__body");
            loader.Register(id);
            var tracker = new ResourceReferenceTracker(loader);

            var firstCalled = false;
            var secondCalled = false;
            tracker.EnsureLoading(id, ResourceKind.Image, (_, _) => firstCalled = true);
            tracker.EnsureLoading(id, ResourceKind.Image, (_, _) => secondCalled = true);

            loader.CompletePending(id);

            Assert.True(firstCalled);
            Assert.False(secondCalled);
            // 只应该有一次真正的 LoadAsync 请求，见既有"同一 id 只触发一次加载"承诺。
            Assert.Single(loader.LoadRequests.FindAll(r => r.ResourceId.Equals(id)));
        }

        /// <summary>D22（ADR-0125，设计决定）：加载失败后同 id 不重试——失败与成功一样都算"已请求"，同一 id 只会
        /// 调用一次 <c>LoadAsync</c>；重试属上层策略（调用方换一个 id 引用，或自己持有加载器再调）。即便失败之后
        /// 资源"后来才可用"（此处在失败后才 Register），也不会自动再次触发。</summary>
        [Fact]
        public void EnsureLoading_AfterFailedLoad_SameId_IsNotRetried()
        {
            var loader = new StubResourceLoader();
            var id = new Id("layer.hero__front__late");
            var tracker = new ResourceReferenceTracker(loader);

            var outcomes = new List<bool>();
            tracker.EnsureLoading(id, ResourceKind.Image, (_, success) => outcomes.Add(success));
            Assert.Equal(new[] { false }, outcomes); // 第一次：未登记 -> 同步失败

            loader.Register(id); // 资源之后变得可加载
            tracker.EnsureLoading(id, ResourceKind.Image, (_, success) => outcomes.Add(success));
            tracker.EnsureLoading(id, ResourceKind.Image);

            Assert.Single(loader.LoadRequests.FindAll(r => r.ResourceId.Equals(id)));
            Assert.Equal(new[] { false }, outcomes); // 后两次的回调被丢弃，没有新的成功通知
            Assert.False(loader.IsLoaded(id));
        }

        /// <summary>D22 配套：失败不影响其它 id 的独立请求（去重按 id 分别记账）。</summary>
        [Fact]
        public void EnsureLoading_FailureOfOneId_DoesNotBlockOtherIds()
        {
            var loader = new StubResourceLoader();
            var bad = new Id("layer.hero__front__bad");
            var good = new Id("layer.hero__front__good");
            loader.Register(good);
            var tracker = new ResourceReferenceTracker(loader);

            tracker.EnsureLoading(bad, ResourceKind.Image);
            tracker.EnsureLoading(good, ResourceKind.Image);

            Assert.False(loader.IsLoaded(bad));
            Assert.True(loader.IsLoaded(good));
        }

        private sealed class ThrowingResourceLoader : IResourceLoader
        {
            public int LoadCalls;

            public void LoadAsync(Id resourceId, ResourceKind kind, LoadCallback callback)
            {
                LoadCalls++;
                throw new System.InvalidOperationException("loader blew up");
            }

            public bool IsLoaded(Id resourceId) => false;
            public double GetLoadProgress(Id resourceId) => 0;
            public void Unload(Id resourceId) { }
        }

        /// <summary>D22 契约（读代码定，设计决定见 ADR-0125）：loader 同步抛异常时，id 已在调用 <c>LoadAsync</c>
        /// 之前登记为"已请求"——异常原样抛给首个调用方（不吞、不包装），之后同 id 的调用不再触达 loader、
        /// 也不再抛（与"失败不重试"同一口径）；调用方若要重试必须换 id 或自持加载器。</summary>
        [Fact]
        public void EnsureLoading_LoaderThrowsSynchronously_PropagatesOnce_ThenIdIsRemembered()
        {
            var loader = new ThrowingResourceLoader();
            var tracker = new ResourceReferenceTracker(loader);
            var id = new Id("layer.hero__front__throws");

            var ex = Assert.Throws<System.InvalidOperationException>(() => tracker.EnsureLoading(id, ResourceKind.Image));
            Assert.Equal("loader blew up", ex.Message);
            Assert.Equal(1, loader.LoadCalls);

            tracker.EnsureLoading(id, ResourceKind.Image); // 不再抛
            tracker.EnsureLoading(id, ResourceKind.Image, (_, _) => { });
            Assert.Equal(1, loader.LoadCalls); // 不再触达 loader

            // 别的 id 不受影响：仍会触达 loader（并照样抛）。
            Assert.Throws<System.InvalidOperationException>(() => tracker.EnsureLoading(new Id("layer.hero__front__other"), ResourceKind.Image));
            Assert.Equal(2, loader.LoadCalls);
        }

        [Fact]
        public void EnsureLoading_NoCallbackPassed_DoesNotThrow_OnCompletion()
        {
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var id = new Id("layer.hero__front__body");
            loader.Register(id);
            var tracker = new ResourceReferenceTracker(loader);

            tracker.EnsureLoading(id, ResourceKind.Image, onComplete: null);

            var ex = Record.Exception(() => loader.CompletePending(id));

            // T-M13：不仅不抛——无回调时加载照常完成，且只发起过一次加载请求。
            Assert.Null(ex);
            Assert.True(loader.IsLoaded(id));
            Assert.Single(loader.LoadRequests.FindAll(r => r.ResourceId.Equals(id)));
        }
    }
}
