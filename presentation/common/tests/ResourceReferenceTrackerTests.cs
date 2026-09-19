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

        [Fact]
        public void EnsureLoading_NoCallbackPassed_DoesNotThrow_OnCompletion()
        {
            var loader = new StubResourceLoader { DeferCallbacks = true };
            var id = new Id("layer.hero__front__body");
            loader.Register(id);
            var tracker = new ResourceReferenceTracker(loader);

            tracker.EnsureLoading(id, ResourceKind.Image, onComplete: null);

            var ex = Record.Exception(() => loader.CompletePending(id));
            Assert.Null(ex);
        }
    }
}
