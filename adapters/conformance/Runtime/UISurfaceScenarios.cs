#nullable enable
// UISurfaceScenarios：IUISurface 契约一致性场景（见 02_引擎适配层.md 第 1.10 节）。
using System.Collections;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Adapters.Conformance
{
    public static class UISurfaceScenarios
    {
        public static readonly IReadOnlyList<ConformanceScenario<IUISurface>> All = new[]
        {
            new ConformanceScenario<IUISurface>("CreateSurface_SetLayout不抛异常", CreateSurface_SetLayout_DoesNotThrow),
            new ConformanceScenario<IUISurface>("SetFocus_GetFocusedElement往返一致", SetFocus_ThenGetFocusedElement_RoundTrips),
            new ConformanceScenario<IUISurface>("DrawText_已创建的surface上不抛异常", DrawText_OnCreatedSurface_DoesNotThrow),
        };

        private static IEnumerator CreateSurface_SetLayout_DoesNotThrow(IUISurface ui, IConformanceAssert assert, ConformanceContext ctx)
        {
            var surfaceId = new Id("ui.conformance_probe_a");
            assert.DoesNotThrow(() => ui.CreateSurface(surfaceId, 320, 240), "CreateSurface 不应抛异常");
            assert.DoesNotThrow(() => ui.SetLayout(surfaceId, "{\"conformance\":true}"), "SetLayout 不应抛异常（layoutData 是不透明字符串，见 02 §1.10）");
            yield break;
        }

        private static IEnumerator SetFocus_ThenGetFocusedElement_RoundTrips(IUISurface ui, IConformanceAssert assert, ConformanceContext ctx)
        {
            var elementId = new Id("ui.conformance_focus_probe");
            ui.SetFocus(elementId);
            assert.Equal(elementId, ui.GetFocusedElement(), "SetFocus 之后 GetFocusedElement 应返回同一个元素 id");
            yield break;
        }

        private static IEnumerator DrawText_OnCreatedSurface_DoesNotThrow(IUISurface ui, IConformanceAssert assert, ConformanceContext ctx)
        {
            var surfaceId = new Id("ui.conformance_probe_b");
            var fontId = new Id("font.conformance_placeholder");
            ui.CreateSurface(surfaceId, 320, 240);

            assert.DoesNotThrow(
                () => ui.DrawText(surfaceId, "conformance", new Vec2(0, 0), fontId, 16),
                "在已创建的 surface 上调用 DrawText 不应抛异常");
            yield break;
        }
    }
}
