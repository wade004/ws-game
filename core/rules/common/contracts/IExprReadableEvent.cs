using Core.Foundation.Expr;

namespace Core.Rules.Common
{
    /// <summary>
    /// 集成任务补齐的契约缺口：事件字段可被 Expr 读取（供 <c>event.&lt;field&gt;</c> 分组求值，
    /// 见 04_数据与内容管线.md 第 6.2 节九个宿主引用分组之一 <c>event</c>、<c>skill.proc_def</c>
    /// 的 <c>condition</c> 一类 Proc 条件表达式访问触发本次求值的事件携带数值）。<c>event</c> 分组
    /// 的具体宿主实现（<c>core/rules/expr_host</c> 的 <c>RulesExprHostFactory</c>）拿到
    /// <c>IExprHostFactory.CreateFor</c> 传入的 <c>triggeringEvent</c> 后，按本接口把事件字段
    /// 转成 <see cref="ExprValue"/> 返回，不需要为每种具体事件类型各写一段 switch/反射。
    /// <para>
    /// 只覆盖 Expr 的五种标量类型（Bool/Int/Number/String/Id，见 <see cref="ExprValueKind"/>）：
    /// 事件里非标量字段（如 <c>SkillCastSuccessEvent.Targets</c> 这样的 <c>IReadOnlyList&lt;Id&gt;</c>）
    /// 不在本接口覆盖范围内，<see cref="TryGetField"/> 对这类字段名返回 false（Expr 语言本身没有
    /// 列表类型，见 04 第 6.3 节）。枚举字段（如 <see cref="HitResult"/>/<see cref="CastFailureReason"/>）
    /// 按其 <c>ToString()</c>（英文标识符原名，非 snake_case 转换）落地为
    /// <see cref="ExprValueKind.String"/>，供表达式按字符串比较；这是任务书未规定细节时的判断记录，
    /// 见 <c>core/rules/common/contracts/Events.cs</c> 各事件类型 <c>TryGetField</c> 实现。
    /// </para>
    /// </summary>
    public interface IExprReadableEvent
    {
        /// <summary>按登记表 <c>found.event_catalog</c> 该事件的 <c>fields</c> 列表里的
        /// camelCase 字段名（如 <c>"casterId"</c>、<c>"amount"</c>）查询字段值；未知字段名或本接口
        /// 不支持的字段类型（列表等）返回 false，不抛异常——与 <see cref="IExprHost.Query"/>
        /// 对缺失对象返回默认值同一惯例（04 第 6.3、6.4 节），由 <c>event</c> 分组的宿主实现决定
        /// "查不到"时的默认值与警告。</summary>
        bool TryGetField(string name, out ExprValue value);
    }
}
