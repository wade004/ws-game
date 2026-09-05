#nullable enable
// ConformanceScenario<TImpl>：一个契约一致性场景的最小载体——一个名字 + 一段以
// (impl, assert, ctx) 为参数的场景体（见 ConformanceContext.cs 顶部判断记录：场景体是
// IEnumerator，不是任务书原始描述的 void Run，以支持 Unity 侧"等一帧"这一必要协作）。
//
// 判断记录（用具体的委托类型而非任务书原文里的 "void Run(IXxx impl, ...)" 实例方法形态）：
// 按接口拆分的每个场景文件（ClockScenarios.cs 等）用静态只读列表登记一组
// ConformanceScenario<IXxx> 实例，两侧包装层（xUnit 的 [Theory] + MemberData、Unity 的
// [UnityTest] 逐个方法）都需要"给定一个实现实例，枚举全部场景并逐个跑、场景名进测试报告"这个
// 能力；用一个小型不可变类型统一承载"名字 + 场景体"比"每个场景是测试类里的一个方法"更适合
// 被两套完全不同的测试框架各自枚举与包装。
using System;
using System.Collections;

namespace Adapters.Conformance
{
    /// <summary>一个契约一致性场景。<typeparamref name="TImpl"/> 是被测的引擎适配层接口类型
    /// （如 IClock、IRenderer2D）。</summary>
    public sealed class ConformanceScenario<TImpl>
    {
        /// <summary>场景名（进两侧测试报告，应能唯一定位到具体契约条款）。</summary>
        public string Name { get; }

        private readonly Func<TImpl, IConformanceAssert, ConformanceContext, IEnumerator> _body;

        public ConformanceScenario(string name, Func<TImpl, IConformanceAssert, ConformanceContext, IEnumerator> body)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _body = body ?? throw new ArgumentNullException(nameof(body));
        }

        /// <summary>运行本场景，返回一个 IEnumerator——不含 yield 语句的场景体在第一次
        /// MoveNext() 内就会跑完全部断言（IEnumerator 立即返回 false）；含
        /// `yield return ctx.AdvanceTime(...)` 的场景体需要调用方（两侧包装层）继续驱动
        /// MoveNext 直到结束，并展开其间产出的嵌套 IEnumerator（见 ConformanceContext.cs
        /// 顶部判断记录）。</summary>
        public IEnumerator Run(TImpl impl, IConformanceAssert assert, ConformanceContext ctx) =>
            _body(impl, assert, ctx);

        public override string ToString() => Name;
    }
}
