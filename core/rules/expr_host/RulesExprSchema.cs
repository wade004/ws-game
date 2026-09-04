using Core.Foundation.Expr;

namespace Core.Rules.ExprHost
{
    /// <summary>
    /// L2 规则层唯一一份 <see cref="IExprSchema"/>（ADR-0015："同一份 schema 供内容校验与运行期
    /// 共用"）：登记 <see cref="RulesExprHostFactory"/> 实际支持查询的全部 <c>group.key</c> 引用与
    /// 签名，供 <c>DataRegistryOptions.ExprSchema</c>（数据加载期 <c>expr_parsable</c> 校验）与
    /// <c>ExprParser.Parse</c>（运行期解析 <c>skill.proc_def.condition</c>/<c>ai.rotation</c>
    /// <c>condition</c>/<c>target.chain_def.filters</c> 等 Expr 文本）共用同一份词汇表，避免
    /// "校验时认为合法、运行时其实不认识"或反过来的偏差。
    /// <para>
    /// 分两层：1) 04 第 6.2 节示例给出的最小可用集合（见本类 <see cref="Build"/>），逐条登记精确的
    /// <c>group.key</c> 签名，供 <see cref="RulesExprHostFactory"/> 实现比对；2) 对九个已知分组
    /// （<see cref="ExprGroups.All"/>）内未逐条登记的其它 <c>key</c>，一律按"合法引用，签名未知"
    /// 放行——判断记录：<c>event.&lt;field&gt;</c> 这类分组的具体字段名随事件类型变化，无法在这里
    /// 穷举；<c>world</c>/<c>quest</c>/<c>player</c> 三个分组的 key 由 L4/游戏层决定，本类构造期
    /// 不可能预知；本类的取舍与 <c>core/rules/skill/core/PermissiveExprSchema.cs</c>
    /// （已被本类取代，见 <c>core/rules/skill/README.md</c>）一致——只保证"能不能解析"，运行期真正
    /// 的合法性由 <see cref="RulesExprHostFactory"/> 的 <see cref="IExprHost.Query"/> 实现决定
    /// （未知 key 按 04 第 6.3 节"缺失 → 默认值 + 警告"处理，不是解析期错误）。
    /// </para>
    /// </summary>
    public sealed class RulesExprSchema : IExprSchema
    {
        /// <summary>全架构共用同一份实例（本类型不持有任何可变状态，不需要每次注入一份新的）。</summary>
        public static readonly RulesExprSchema Instance = new RulesExprSchema();

        private static readonly ExprSignature Permissive =
            new ExprSignature(ExprValueKind.Bool, System.Array.Empty<ExprValueKind>());

        private readonly ExprSchema _known = Build();

        private RulesExprSchema()
        {
        }

        public bool TryGetSignature(string group, string key, out ExprSignature signature)
        {
            if (_known.TryGetSignature(group, key, out signature))
            {
                return true;
            }

            if (ExprGroups.IsKnown(group))
            {
                // 见本类型上方判断记录第 2 层：已知分组、未逐条登记的 key 一律放行为"合法引用，
                // 签名未知"。ExprEvaluator 求值时只看 IExprHost.Query 的实际返回值，从不读取这里
                // 登记的 ReturnKind（惯例同 PermissiveExprSchema/TargetFilterExprSchema），因此
                // Permissive 的具体取值只是占位，不影响求值语义或静态校验的正确性。
                signature = Permissive;
                return true;
            }

            signature = default;
            return false;
        }

        private static ExprSchema Build()
        {
            var schema = new ExprSchema();

            RegisterSelfAndTarget(schema, ExprGroups.Self);
            RegisterSelfAndTarget(schema, ExprGroups.Target);

            // self 专用三项：target 分组不登记同名 key（见 RulesExprHostFactory 判断记录——
            // target.distance_to_target/target.threat_top 落回"已知分组未登记 key"的放行分支，
            // 运行期按未知 key 处理，返回默认值 + 警告）。
            schema.Register(ExprGroups.Self, "distance_to_target", ExprValueKind.Number);
            schema.Register(ExprGroups.Self, "threat_top", ExprValueKind.Id);

            schema.Register(ExprGroups.Combat, "in_combat", ExprValueKind.Bool);
            schema.Register(ExprGroups.Combat, "is_casting", ExprValueKind.Bool);

            schema.Register(ExprGroups.Enemies, "count_in_range", ExprValueKind.Int, ExprValueKind.Number);
            schema.Register(ExprGroups.Enemies, "nearest_distance", ExprValueKind.Number);

            schema.Register(ExprGroups.Time, "sim_time", ExprValueKind.Number);
            schema.Register(ExprGroups.Time, "since_combat_start", ExprValueKind.Number);
            schema.Register(ExprGroups.Time, "day_cycle", ExprValueKind.Number);
            schema.Register(ExprGroups.Time, "turn_index", ExprValueKind.Int);
            schema.Register(ExprGroups.Time, "round_index", ExprValueKind.Int);
            schema.Register(ExprGroups.Time, "is_my_turn", ExprValueKind.Bool);

            // event/world/quest/player：不逐条登记，见本类型上方判断记录，落回"已知分组放行"分支。
            return schema;
        }

        private static void RegisterSelfAndTarget(ExprSchema schema, string group)
        {
            schema.Register(group, "hp", ExprValueKind.Number);
            schema.Register(group, "hp_max", ExprValueKind.Number);
            schema.Register(group, "hp_pct", ExprValueKind.Number);
            schema.Register(group, "power", ExprValueKind.Number, ExprValueKind.Id);
            schema.Register(group, "power_pct", ExprValueKind.Number, ExprValueKind.Id);
            schema.Register(group, "level", ExprValueKind.Int);
            schema.Register(group, "faction", ExprValueKind.Id);
            schema.Register(group, "is_alive", ExprValueKind.Bool);
            schema.Register(group, "in_combat", ExprValueKind.Bool);
            schema.Register(group, "is_casting", ExprValueKind.Bool);
            schema.Register(group, "has_aura", ExprValueKind.Bool, ExprValueKind.Id);
            schema.Register(group, "aura_stacks", ExprValueKind.Int, ExprValueKind.Id);
            schema.Register(group, "stat", ExprValueKind.Number, ExprValueKind.Id);
            schema.Register(group, "has_tag", ExprValueKind.Bool, ExprValueKind.Id);
            schema.Register(group, "position_x", ExprValueKind.Number);
            schema.Register(group, "position_y", ExprValueKind.Number);
        }
    }
}
