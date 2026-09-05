#nullable enable
// InputScenarios：IInput 契约一致性场景（见 02_引擎适配层.md 第 1.5 节）。
using System.Collections;
using System.Collections.Generic;
using Core.Foundation.EngineAdapter;

namespace Adapters.Conformance
{
    public static class InputScenarios
    {
        public static readonly IReadOnlyList<ConformanceScenario<IInput>> All = new[]
        {
            new ConformanceScenario<IInput>("IsKeyDown_未知键返回false", IsKeyDown_UnknownKey_ReturnsFalse),
            new ConformanceScenario<IInput>("GetGamepadAxis_设备越界返回零", GetGamepadAxis_OutOfRangeIndex_ReturnsZero),
            new ConformanceScenario<IInput>("文本输入会话_开始结束不抛异常并返回字符串", TextInputSession_DoesNotThrow_ReturnsString),
        };

        private static IEnumerator IsKeyDown_UnknownKey_ReturnsFalse(IInput input, IConformanceAssert assert, ConformanceContext ctx)
        {
            assert.False(input.IsKeyDown("__conformance_never_bound_key__"), "从未按下过的键，IsKeyDown 应返回 false");
            yield break;
        }

        private static IEnumerator GetGamepadAxis_OutOfRangeIndex_ReturnsZero(IInput input, IConformanceAssert assert, ConformanceContext ctx)
        {
            assert.Equal(0.0, input.GetGamepadAxis(-1, "LeftStickX"), "手柄下标为负时 GetGamepadAxis 应返回 0");
            assert.Equal(0.0, input.GetGamepadAxis(999, "LeftStickX"), "手柄下标越界时 GetGamepadAxis 应返回 0");
            yield break;
        }

        private static IEnumerator TextInputSession_DoesNotThrow_ReturnsString(IInput input, IConformanceAssert assert, ConformanceContext ctx)
        {
            assert.DoesNotThrow(() => input.BeginTextInput("占位提示文本"), "BeginTextInput 不应抛异常");
            string? result = null;
            assert.DoesNotThrow(() => result = input.EndTextInput(), "EndTextInput 不应抛异常");
            assert.NotNull(result, "EndTextInput 应返回一个非空（可以是空字符串）的字符串");
            yield break;
        }
    }
}
