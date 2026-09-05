#nullable enable
// IConformanceAssert：契约一致性场景使用的最小断言抽象（11_工程规范与测试.md 第 6 节 /
// ADR-0016）。场景源码（本目录其余文件）与任何具体测试框架无关——不引用 xUnit，也不引用
// UnityEngine/NUnit——由两侧的包装层（core/foundation/engine_adapter/tests/ConformanceStubTests.cs
// 用 xUnit 断言实现本接口；adapters/unity 包 Tests/Runtime/ConformanceUnityTests.cs 用 NUnit
// 断言实现本接口）各自适配到自己的测试框架，从而同一份场景源码能同时在 dotnet xUnit 与 Unity
// PlayMode/EditMode 两条流水线上运行，见任务书"契约一致性测试套件对桩与各引擎实现共用"。
using System;

namespace Adapters.Conformance
{
    /// <summary>
    /// 场景断言失败时抛出，供两侧包装层捕获后转译为各自测试框架的失败形式（而不是让场景代码直接
    /// 依赖某个测试框架的异常类型）。
    /// </summary>
    public sealed class ConformanceAssertionException : Exception
    {
        public ConformanceAssertionException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// 场景请求"跳过本场景"时抛出（例如某个契约条款在当前实现上没有可确定性触发的协作点，
    /// 见 adapters/conformance/README.md"判断记录"）。两侧包装层应将其映射为测试框架的
    /// "跳过/忽略"结果，而不是失败——xUnit 2.5.3 没有运行期动态跳过 API，包装层退化为"记录一条
    /// 诊断输出后判为通过"；NUnit（Unity 侧）用 Assert.Ignore 真正标记为 Skipped。
    /// </summary>
    public sealed class ConformanceSkipException : Exception
    {
        public ConformanceSkipException(string reason) : base(reason)
        {
        }
    }

    /// <summary>
    /// 场景使用的最小断言集合。方法命名与语义参照主流测试框架的常见惯例，但本接口本身不依赖
    /// 任何测试框架程序集。<paramref name="message"/> 一律要求非空，失败时进最终的断言/跳过异常，
    /// 帮助定位是哪一条契约条款没有满足。
    /// </summary>
    public interface IConformanceAssert
    {
        void True(bool condition, string message);

        void False(bool condition, string message);

        void Equal<T>(T expected, T actual, string message);

        void NotEqual<T>(T notExpected, T actual, string message);

        /// <summary>断言 <paramref name="action"/> 抛出恰好 <typeparamref name="TException"/>
        /// 类型（或其子类型）的异常；未抛出或抛出了不匹配的异常类型都判为断言失败。</summary>
        void Throws<TException>(Action action, string message) where TException : Exception;

        void DoesNotThrow(Action action, string message);

        void IsNull(object? value, string message);

        void NotNull(object? value, string message);

        /// <summary>请求跳过本场景剩余部分（见 <see cref="ConformanceSkipException"/>）。
        /// 调用后场景应立即返回（通常紧跟一个 return/yield break），不再执行后续断言。</summary>
        void Skip(string reason);
    }
}
