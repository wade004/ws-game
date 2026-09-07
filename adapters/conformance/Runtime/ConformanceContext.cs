#nullable enable
// ConformanceContext：场景运行时需要的、随实现而异的协作点（见任务书"ConformanceContext
// 提供实现相关的协作"）。场景源码只通过本类型访问这些协作，不直接感知自己正跑在桩还是某个
// 引擎实现上。
//
// 判断记录（"推进一次时钟/帧"用 Func<double, IEnumerator> 而不是同步 Action，二选一记录）：
// 桩（StubClock.Advance）与 Unity（UnityClock 由 UnityEngineHost.Update/FixedUpdate 真实驱动）
// 在"让时间/帧往前走"这件事上的可行操作方式不同——桩可以在任意时刻同步调用 Advance(seconds)
// 立即完成；Unity 的帧回调只能由引擎自身的播放循环真正推进，测试代码唯一能做的是
// "yield return null" 等待下一次真实 Update。若把本协作点定成同步 Action，Unity 侧将无法实现
// （没有任何同步 API 能在方法调用内部伪造一次引擎帧）；因此改为返回 IEnumerator——桩侧的实现
// 可以同步完成全部工作后返回一个立即结束的空迭代器（不真正暂停），Unity 侧的实现返回一个
// "yield return null 等待一帧"的迭代器。场景内用 `yield return ctx.AdvanceTime(seconds);` 消费，
// 于是场景本身也必须是 IEnumerator（见 ConformanceScenario.cs 判断记录），而不是任务书原始描述的
// `void Run(...)`——这是同一个设计决策的两处体现，一并记录：`void Run` 无法表达"这一步需要真的
// 等一帧"，`IEnumerator Run` 通过是否包含 yield 语句自然区分"纯同步场景"（不含 yield，两侧包装层
// 一次 MoveNext 就能跑完全部断言）与"需要帧协作的场景"（含 yield return ctx.AdvanceTime(...)）。
//
// Unity 侧包装层对嵌套 IEnumerator 的展开依赖 UnityEngine 协程调度器原生支持
// "yield return 另一个 IEnumerator"（自动展开到完成，逐层转发其 yield 值）；xUnit 侧没有引擎
// 协程调度器，包装层自己写一个递归 Pump 函数展开嵌套 IEnumerator（见
// core/foundation/engine_adapter/tests/ConformanceStubTests.cs）。
using System;
using System.Collections;
using System.Collections.Generic;

namespace Adapters.Conformance
{
    public sealed class ConformanceContext
    {
        /// <summary>推进"一次时钟/帧"协作点，默认是不做任何事的空步（多数场景不需要它）。
        /// 桩包装层设为"同步调用 StubClock.Advance 后返回空迭代器"；Unity 包装层设为
        /// "yield return null 等待一帧"（见类型顶部判断记录）。</summary>
        public Func<double, IEnumerator> AdvanceTime { get; set; } = _ => EmptyStep();

        /// <summary>触发一次"用户请求关闭窗口"（对应 StubWindow.RequestCloseForTest /
        /// UnityWindow.RequestCloseForTest 这两个同名但不属于 IWindow 契约本身的测试专用方法）。
        /// 两侧包装层各自用闭包捕获具体实现实例后设置本委托；场景使用前应先判空，为空时调用
        /// <see cref="IConformanceAssert.Skip"/>。</summary>
        public Action? TriggerWindowClose { get; set; }

        /// <summary>IRenderer3D 是条件必需接口（02 §1.12）：桩实现按正常路径工作，Unity 本迭代
        /// 声明降级、全部方法抛 NotSupportedException（见 UnityRenderer3D.cs 类型注释）。
        /// 两侧包装层各自设置本标志，场景据此决定断言"正常工作"还是"抛出 NotSupportedException"。</summary>
        public bool SupportsRenderer3D { get; set; } = true;

        /// <summary>H5b 根治新增：驱动"当前这次 <c>IRenderer3D.PlayAnim</c> 播放的剪辑自然播放完成"
        /// （见 <c>Renderer3DScenarios</c>"非循环剪辑结束发 finished 事件"场景、
        /// <c>Presentation.Render.ModelCharacterRig.AnimFinishedEventId</c> 判断记录）——桩实现没有
        /// 真实的时间推进概念，可以同步立即判定完成（见 <c>StubRenderer3D.CompleteAnimForTest</c>）；
        /// Unity 实现需要真的等待若干真实帧，直到 <c>UnityRenderer3D.Tick</c>（由
        /// <c>UnityEngineHost.Update</c> 驱动）侦测到 Animator/Animation 的播放进度自然到达终点——两侧
        /// 包装层各自用闭包捕获具体实现实例后设置本委托（惯例同 <see cref="TriggerWindowClose"/>），
        /// 返回 <see cref="IEnumerator"/> 供场景 <c>yield return</c>（惯例同 <see cref="AdvanceTime"/>）。
        /// 未设置时（场景与本能力无关的其它测试路径）默认 null，场景使用前应先判空。</summary>
        public Func<Core.Foundation.EngineAdapter.ModelHandle, IEnumerator>? CompleteNonLoopAnim { get; set; }

        /// <summary>模拟"下一次写入失败但保持旧内容不变"（对应 StubFileSystem.FailNextWrite，
        /// 一个只有桩才能确定性触发的测试专用开关）。Unity 的真实文件系统没有同等确定性的触发
        /// 方式，包装层留空，场景据此调用 <see cref="IConformanceAssert.Skip"/>。</summary>
        public Action? SimulateNextWriteFailure { get; set; }

        /// <summary>供需要"登记一个可查询对象"之类实现相关便捷方法的场景使用的通用扩展点
        /// （目前场景均未使用，预留同一套协作机制，避免后续新增协作点时改变本类型的整体形状）。</summary>
        public IDictionary<string, object?> Extras { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

        public static IEnumerator EmptyStep()
        {
            yield break;
        }
    }
}
