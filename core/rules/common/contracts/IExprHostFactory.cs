using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;

namespace Core.Rules.Common
{
    /// <summary>
    /// L2 内各模块（Proc 的 <c>condition</c>、Rotation 的 <c>condition</c>、AreaTrigger 的
    /// <c>condition</c> 等）求值 <c>Expr</c> 时用来取得一个绑定了具体上下文的 <see cref="IExprHost"/>
    /// （见 04 第 6.2 节"每个宿主实现只需要提供 query(group, key, args) -&gt; Value"）。本接口只定义
    /// "怎么拿到宿主"这一工厂签名，<see cref="IExprHost"/> 本身的分组/求值语义由 04 与 expr 模块唯一
    /// 定义；具体实现（把 <paramref name="selfId"/>/<paramref name="targetId"/>/<paramref name="triggeringEvent"/>
    /// 接到 self/target/combat/enemies 等分组的查询）属于集成任务，本任务只声明接口。
    /// </summary>
    public interface IExprHostFactory
    {
        /// <summary>
        /// 构造一个绑定到 <paramref name="selfId"/>（表达式里的 <c>self</c> 分组）与可选
        /// <paramref name="targetId"/>（<c>target</c> 分组）的求值宿主；<paramref name="triggeringEvent"/>
        /// 非空时供表达式引用触发本次求值的事件字段（如 Proc 的 <c>trigger_event</c> 条件里访问
        /// 事件携带的数值）。
        /// </summary>
        IExprHost CreateFor(Id selfId, Id? targetId, IEvent? triggeringEvent);
    }
}
