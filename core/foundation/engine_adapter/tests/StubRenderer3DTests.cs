using Adapters.Stub;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Xunit;

namespace Tests.Foundation.EngineAdapter
{
    /// <summary>
    /// W2 收边补齐（A1 审计第 7 节，测试完备性缺口）：<see cref="IRenderer3D.OnAnimEvent"/> 此前
    /// 未被 <c>Renderer3DScenarios.cs</c> 覆盖，<c>StubRenderer3D.FireAnimEventForTest</c>
    /// 存在但零测试调用。惯例同 <c>StubSpatialQueryTests.cs</c>：直接构造 <c>StubRenderer3D</c>，
    /// 不依赖 <c>adapters/conformance</c> 场景框架。
    /// </summary>
    public sealed class StubRenderer3DTests
    {
        [Fact]
        public void OnAnimEvent_FireAnimEventForTest_InvokesSubscribedCallback_WithHandleAndEventId()
        {
            var renderer = new StubRenderer3D();
            var handle = renderer.CreateModelInstance(new Id("model.sample"));
            ModelHandle? receivedHandle = null;
            Id? receivedEventId = null;

            renderer.OnAnimEvent(handle, (h, eventId) =>
            {
                receivedHandle = h;
                receivedEventId = eventId;
            });

            renderer.FireAnimEventForTest(handle, new Id("anim.hit_frame"));

            Assert.Equal(handle, receivedHandle);
            Assert.Equal(new Id("anim.hit_frame"), receivedEventId);
        }

        [Fact]
        public void OnAnimEvent_MultipleSubscribers_AllInvoked()
        {
            var renderer = new StubRenderer3D();
            var handle = renderer.CreateModelInstance(new Id("model.sample"));
            var firstCallCount = 0;
            var secondCallCount = 0;

            renderer.OnAnimEvent(handle, (_, _) => firstCallCount++);
            renderer.OnAnimEvent(handle, (_, _) => secondCallCount++);

            renderer.FireAnimEventForTest(handle, new Id("anim.footstep"));

            Assert.Equal(1, firstCallCount);
            Assert.Equal(1, secondCallCount);
        }

        [Fact]
        public void OnAnimEvent_Unsubscribed_DoesNotReceiveFurtherEvents()
        {
            var renderer = new StubRenderer3D();
            var handle = renderer.CreateModelInstance(new Id("model.sample"));
            var callCount = 0;

            var subscription = renderer.OnAnimEvent(handle, (_, _) => callCount++);
            subscription.Dispose();

            renderer.FireAnimEventForTest(handle, new Id("anim.footstep"));

            Assert.Equal(0, callCount);
        }

        [Fact]
        public void OnAnimEvent_DifferentHandle_DoesNotReceiveOtherInstanceEvents()
        {
            var renderer = new StubRenderer3D();
            var handleA = renderer.CreateModelInstance(new Id("model.a"));
            var handleB = renderer.CreateModelInstance(new Id("model.b"));
            var callCount = 0;

            renderer.OnAnimEvent(handleA, (_, _) => callCount++);
            renderer.FireAnimEventForTest(handleB, new Id("anim.footstep"));

            Assert.Equal(0, callCount);
        }
    }
}
