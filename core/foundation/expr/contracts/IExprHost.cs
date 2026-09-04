using System.Collections.Generic;

namespace Core.Foundation.Expr
{
    /// <summary>
    /// Expr 求值器的宿主查询出口（见 04 第 6.2 节）："每个宿主实现只需要提供
    /// query(group, key, args) -> Value"。Expr 求值器不关心宿主内部实现，只负责把
    /// 语法树按分组分发出去；宿主对缺失对象（如无目标）返回默认值的责任在宿主本身，
    /// 本模块不干预（见 04 第 6.3、6.4 节）。
    /// </summary>
    public interface IExprHost
    {
        ExprValue Query(string group, string key, IReadOnlyList<ExprValue> args);
    }
}
