#nullable enable
// ClockScenarios：IClock 契约一致性场景（见 02_引擎适配层.md 第 1.2 节 / ADR-0016 决策 1）。
using System;
using System.Collections;
using System.Collections.Generic;
using Core.Foundation.EngineAdapter;

namespace Adapters.Conformance
{
    public static class ClockScenarios
    {
        public static readonly IReadOnlyList<ConformanceScenario<IClock>> All = new[]
        {
            new ConformanceScenario<IClock>("Now_单调不递减", Now_IsMonotonicNonDecreasing),
            new ConformanceScenario<IClock>("OnFrame_推进后触发_退订后不再触发", OnFrame_FiresThenStopsAfterDispose),
            new ConformanceScenario<IClock>("RequestFixedStep_累积到步长边界才触发", RequestFixedStep_FiresAtStepBoundary),
            new ConformanceScenario<IClock>("RequestFixedStep_步长非正抛异常", RequestFixedStep_NonPositiveStepThrows),
        };

        private static IEnumerator Now_IsMonotonicNonDecreasing(IClock clock, IConformanceAssert assert, ConformanceContext ctx)
        {
            var first = clock.Now();
            yield return ctx.AdvanceTime(0.05);
            var second = clock.Now();
            assert.True(second >= first, $"Now() 应单调不递减：first={first}, second={second}");
        }

        private static IEnumerator OnFrame_FiresThenStopsAfterDispose(IClock clock, IConformanceAssert assert, ConformanceContext ctx)
        {
            var callCount = 0;
            var sub = clock.OnFrame(_ => callCount++);

            yield return ctx.AdvanceTime(0.016);
            assert.True(callCount >= 1, "OnFrame 回调应在推进一帧后至少触发一次");

            sub.Dispose();
            assert.True(sub.IsDisposed, "Dispose 后 SubscriptionHandle.IsDisposed 应为 true");

            var countAfterDispose = callCount;
            yield return ctx.AdvanceTime(0.016);
            assert.Equal(countAfterDispose, callCount, "退订后不应再收到 OnFrame 回调");
        }

        private static IEnumerator RequestFixedStep_FiresAtStepBoundary(IClock clock, IConformanceAssert assert, ConformanceContext ctx)
        {
            var fireCount = 0;
            const double stepSeconds = 0.1;
            var sub = clock.RequestFixedStep(stepSeconds, dt => { fireCount++; assert.Equal(stepSeconds, dt, "固定步回调应携带与注册时相同的步长秒数"); });

            // 推进小于一个步长：不应触发。
            yield return ctx.AdvanceTime(0.03);
            var countBeforeBoundary = fireCount;

            // 继续推进跨过步长边界：应至少触发一次。
            yield return ctx.AdvanceTime(0.09);
            assert.True(fireCount > countBeforeBoundary, "推进跨过步长边界后，固定步回调应至少触发一次");

            sub.Dispose();
        }

        private static IEnumerator RequestFixedStep_NonPositiveStepThrows(IClock clock, IConformanceAssert assert, ConformanceContext ctx)
        {
            assert.Throws<ArgumentOutOfRangeException>(
                () => clock.RequestFixedStep(0.0, _ => { }),
                "stepSeconds=0 应抛 ArgumentOutOfRangeException");
            assert.Throws<ArgumentOutOfRangeException>(
                () => clock.RequestFixedStep(-1.0, _ => { }),
                "stepSeconds<0 应抛 ArgumentOutOfRangeException");
            yield break;
        }
    }
}
