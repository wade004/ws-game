namespace Core.Foundation.Expr
{
    /// <summary>
    /// 静态校验用的引用登记表：登记全架构范围内哪些 <c>group.key</c> 组合是合法引用，
    /// 以及各自的返回类型与参数类型（见 04 第 5 节"表达式可解析"校验项、第 6.4 节解析期错误）。
    /// 实现方通常是某个具体宿主分组的注册表（如战斗、任务系统各自登记自己开放的 key）。
    /// </summary>
    public interface IExprSchema
    {
        bool TryGetSignature(string group, string key, out ExprSignature signature);
    }
}
