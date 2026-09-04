namespace Core.Gameplay.AreaTrigger
{
    /// <summary>
    /// 供组装层登记本模块引入的 Expr 键的入口（惯例同
    /// <c>Core.Gameplay.WorldState.WorldExprSchemaEntries</c>，见并行任务约束"Expr 键登记只暴露为
    /// 本模块静态 XxxExprSchemaEntries"）。
    /// <para>
    /// 判断记录：<c>area.trigger_def.condition</c> 只消费既有九个宿主分组（05 第 7.1 节未新增分组，
    /// 04 第 6.2 节的 <c>self</c>/<c>world</c> 等分组已覆盖典型用法），本模块不引入任何新的
    /// <c>group.key</c> 组合，因此本类型无任何登记方法可提供——保留本文件只是为了让"是否有新键
    /// 需要登记"这一问题在代码里有一个明确、可搜索的落点，而不是留白让后续读者去猜测。解析
    /// <c>condition</c> 文本复用 <c>Core.Rules.ExprHost.RulesExprSchema.Instance</c>（见
    /// <see cref="AreaTriggerHost"/> 判断记录），求值宿主由构造期注入的
    /// <see cref="Core.Rules.Common.IExprHostFactory"/> 提供，均不经过本类型。
    /// </para>
    /// </summary>
    public static class AreaTriggerExprSchemaEntries
    {
    }
}
