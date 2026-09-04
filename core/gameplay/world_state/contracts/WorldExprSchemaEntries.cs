using Core.Foundation.Expr;

namespace Core.Gameplay.WorldState
{
    /// <summary>
    /// 供组装层把 <see cref="WorldExprGroupProvider"/> 实际支持的三个键
    /// （<c>world.get</c>/<c>world.has</c>/<c>world.get_int</c>）登记进静态校验用的
    /// <see cref="IExprSchema"/>。
    /// <para>
    /// 契约缺口（见 README"判断记录"详述）：任务书原句"若 RulesExprSchema 不可扩展，提供一个
    /// ExprSchema 合并帮助函数"——<c>core/rules/expr_host.RulesExprSchema</c> 确实不可扩展
    /// （<c>sealed</c>、单例、<c>Build()</c> 私有），本类型正是为此补的登记入口；同时发现一处更
    /// 严重的问题：<c>RulesExprSchema</c> 对 <c>world</c>/<c>quest</c>/<c>player</c> 三个"已知分组"
    /// 采用"未逐条登记的 key 一律放行"的宽松策略（见该类型判断记录第 2 层），这会让 04 第 6.2 节
    /// 官方示例 <c>world.get(world.bridge.repaired)</c> 中的参数 <c>world.bridge.repaired</c>
    /// 被 <c>ExprParser.ParseIdentTerm</c>（ADR-0015）误判成一个新的 <c>world.bridge.repaired</c>
    /// 引用（因为 <c>RulesExprSchema.TryGetSignature("world", "bridge.repaired", out _)</c> 因
    /// "已知分组放行"分支返回 <c>true</c>），而不是预期的 <c>Id</c> 字面量参数——解析虽不报错，但
    /// 求值时会先对不存在的 <c>world.bridge.repaired</c> 键求值（触发 <see cref="WorldExprGroupProvider"/>
    /// 的"未知 key"警告分支，返回 <c>Bool(false)</c>），再把这个 <c>Bool</c> 值当作 <c>flagKey</c>
    /// 参数传给外层 <c>world.get</c>，因参数类型不是 <c>Id</c> 而抛出
    /// <see cref="System.ArgumentException"/>。<see cref="RegisterInto"/> 产出的 <see cref="ExprSchema"/>
    /// 本身没有这个问题（它对未登记的 key 老实返回"未登记"，不做已知分组放行），但如果调用方把它与
    /// <c>RulesExprSchema</c> 合并（无论用什么顺序），只要 <c>RulesExprSchema</c> 出现在查找链条里，
    /// <c>world</c> 分组下任何未精确登记的 key（包括所有 <c>world.&lt;路径&gt;</c> 形态的标志键
    /// 字面量）仍会被它的"已知分组放行"分支提前截获。<b>结论：任何需要把 <c>world.&lt;路径&gt;</c>
    /// 标志键字面量作为参数传给 <c>world.get</c>/<c>world.has</c>/<c>world.get_int</c> 的 Expr 文本，
    /// 解析时必须使用不含 <c>RulesExprSchema</c>"已知分组放行"分支的 schema（如单独调用
    /// <see cref="RegisterInto"/> 产出的 <see cref="ExprSchema"/>，或专为 <c>self</c>/<c>target</c>/
    /// <c>combat</c>/<c>enemies</c>/<c>time</c> 等其它分组保留 <c>RulesExprSchema</c>、但 <c>world</c>
    /// 分组只信任精确登记表的自定义合并）——这属于 <c>core/rules/expr_host</c>（其他任务/agent 负责的
    /// 目录）的设计取舍，不在本任务允许改动的范围内，本模块只能在此记录、不能修复。</b>
    /// </para>
    /// </summary>
    public static class WorldExprSchemaEntries
    {
        /// <summary>把 <c>world.get</c>/<c>world.has</c>/<c>world.get_int</c> 三条精确签名登记进
        /// <paramref name="schema"/>（<see cref="ExprValue.OfId"/> 参数，返回类型见各自签名），返回
        /// 同一个实例便于链式调用。<c>get</c> 的 <c>ReturnKind</c> 取 <see cref="ExprValueKind.Bool"/>
        /// 仅作占位——它的真实返回类型随标志实际存储的类型而变，<c>ExprEvaluator</c> 求值时从不读取
        /// <see cref="IExprSchema"/> 登记的 <c>ReturnKind</c>（同 <c>RulesExprSchema</c> 判断记录，
        /// 惯例一致），因此占位取值不影响求值语义或静态校验的正确性。</summary>
        public static ExprSchema RegisterInto(ExprSchema schema)
        {
            schema.Register(ExprGroups.World, "get", ExprValueKind.Bool, ExprValueKind.Id);
            schema.Register(ExprGroups.World, "has", ExprValueKind.Bool, ExprValueKind.Id);
            schema.Register(ExprGroups.World, "get_int", ExprValueKind.Int, ExprValueKind.Id);
            return schema;
        }

        /// <summary>便捷入口：构造一份只含本模块三条签名的独立 <see cref="ExprSchema"/>
        /// （不合并任何其它分组，见本类型顶部判断记录"结论"一段推荐用法）。</summary>
        public static ExprSchema BuildStandalone() => RegisterInto(new ExprSchema());
    }
}
