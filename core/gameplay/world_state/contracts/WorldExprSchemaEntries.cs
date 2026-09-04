using Core.Foundation.Expr;

namespace Core.Gameplay.WorldState
{
    /// <summary>
    /// 供组装层把 <see cref="WorldExprGroupProvider"/> 实际支持的三个键
    /// （<c>world.get</c>/<c>world.has</c>/<c>world.get_int</c>）登记进静态校验用的
    /// <see cref="IExprSchema"/>。
    /// <para>
    /// 历史判断记录已解决（阶段 3 集成收尾"事项三"复核）：本文件早先记录过一处与
    /// <c>core/rules/expr_host.RulesExprSchema</c> 的不兼容——旧版 <c>RulesExprSchema</c> 对
    /// <c>world</c>/<c>quest</c>/<c>player</c> 三个"已知分组"采用"未逐条登记的 key 一律放行"的
    /// 宽松策略，会把 <c>world.get(world.bridge.repaired)</c> 里的参数 <c>world.bridge.repaired</c>
    /// 误判成一个新引用而不是 <c>Id</c> 字面量。该问题已随 ADR-0015 严格化改造修复：
    /// <see cref="Core.Rules.ExprHost.RulesExprSchema.Base"/> 现在只登记
    /// <c>self</c>/<c>target</c>/<c>combat</c>/<c>enemies</c>/<c>time</c> 五个分组，
    /// <c>world</c>/<c>quest</c>/<c>player</c>/<c>event</c> 四个分组下任何未精确登记的 key（含全部
    /// <c>world.&lt;路径&gt;</c> 形态的标志键字面量）一律不被"已知分组放行"，按 ADR-0015 规则正确
    /// 退化为 <c>Id</c> 字面量。<see cref="RegisterInto"/> 产出的登记表与 <c>RulesExprSchema.Base</c>
    /// 现在可以按任意顺序经 <see cref="Core.Rules.ExprHost.RulesExprSchema.Compose"/>（或本类型未
    /// 提供、调用方自建的 <see cref="Core.Rules.ExprHost.CompositeExprSchema"/>）自由合并，不再需要
    /// "world 分组只信任精确登记表"这类特殊避让——<see cref="BuildStandalone"/> 仍然保留（供只需要
    /// 精确三键、不关心其它分组的场景直接使用），但不再是绕开 bug 的必要手段，只是一个方便的最小
    /// 登记表。<c>core/gameplay/assembly.GameplaySchemaCatalog.FullExprSchema</c> 是当前推荐的
    /// "全分组组合" schema，供游戏组装根统一使用。
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
