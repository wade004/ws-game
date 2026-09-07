using System;
using Core.Foundation.Expr;

namespace Core.Gameplay.Quest
{
    /// <summary>
    /// 供组装层把 <see cref="QuestExprGroupProvider"/>（<c>quest</c> 分组）与
    /// <see cref="PlayerExprGroupProvider"/>（<c>player</c> 分组）实际支持的键登记进静态校验用的
    /// <see cref="IExprSchema"/>（惯例同 <c>core/gameplay/world_state</c> 的
    /// <c>WorldExprSchemaEntries</c>）。判断记录同该类型："不改
    /// <c>core/rules/expr_host.RulesExprSchema</c>"（该类型 <c>sealed</c>/单例/私有 <c>Build()</c>
    /// 不可扩展，任务书要求本模块自持一份静态登记入口，不依赖也不修改它）；同时必须避免与
    /// <c>RulesExprSchema</c> 的"已知分组一律放行未登记 key"分支合并使用——否则
    /// <c>quest.is_active(quest.deliver_letter)</c> 里的参数 <c>quest.deliver_letter</c> 会被
    /// 误判成一次新的 <c>quest.deliver_letter</c> 引用而不是 Id 字面量（ADR-0015，任务书原句提醒）。
    /// <see cref="BuildStandalone"/> 产出的独立 schema 没有这个问题：<c>quest</c> 分组只精确登记
    /// <see cref="QuestExprGroupProvider"/> 实际认识的五个 key（<c>is_active</c>/<c>is_completed</c>/
    /// <c>is_available</c>/<c>is_objectives_complete</c>/<c>objective_progress</c>，
    /// <c>is_objectives_complete</c> 为 GP-05 收边新增，见该类型判断记录），<c>quest.deliver_letter</c> 这类"quest 域名
    /// 但不是这四个 key 之一"的点分标识符找不到匹配签名，按 ADR-0015 规则退化为 Id 字面量解析，
    /// 与期望行为一致。
    /// </summary>
    public static class QuestExprSchemaEntries
    {
        /// <summary>把 <c>quest.*</c>/<c>player.*</c> 全部已知签名登记进 <paramref name="schema"/>，
        /// 返回同一个实例便于链式调用。</summary>
        public static ExprSchema RegisterInto(ExprSchema schema)
        {
            schema.Register(ExprGroups.Quest, "is_active", ExprValueKind.Bool, ExprValueKind.Id);
            schema.Register(ExprGroups.Quest, "is_completed", ExprValueKind.Bool, ExprValueKind.Id);
            schema.Register(ExprGroups.Quest, "is_available", ExprValueKind.Bool, ExprValueKind.Id);
            schema.Register(ExprGroups.Quest, "is_objectives_complete", ExprValueKind.Bool, ExprValueKind.Id);
            schema.Register(ExprGroups.Quest, "objective_progress", ExprValueKind.Int, ExprValueKind.Id, ExprValueKind.Int);

            schema.Register(ExprGroups.Player, "level", ExprValueKind.Int);
            schema.Register(ExprGroups.Player, "has_item", ExprValueKind.Bool, ExprValueKind.Id);
            schema.Register(ExprGroups.Player, "item_count", ExprValueKind.Int, ExprValueKind.Id);

            return schema;
        }

        /// <summary>便捷入口：构造一份只含本模块两个分组签名的独立 <see cref="ExprSchema"/>
        /// （不合并 <c>RulesExprSchema</c>，见本类型顶部判断记录）。</summary>
        public static ExprSchema BuildStandalone() => RegisterInto(new ExprSchema());

        /// <summary>
        /// 供解析 <c>quest.def.prerequisite</c>/<c>objectives[].param.eventFilter</c> 用：
        /// 08 第 2.1 节 <c>prerequisite</c> 原文"等级、已完成任务、世界标志等"——除
        /// <c>quest</c>/<c>player</c> 外还可能引用 <c>world</c> 分组（如
        /// <c>world.get(world.bridge.repaired)</c>），因此额外合并
        /// <c>core/gameplay/world_state.WorldExprSchemaEntries</c> 三个精确签名（同一个装配体内的
        /// 姊妹模块，非 <c>RulesExprSchema</c>，不违反本类型顶部"不能与 RulesExprSchema 的已知分组
        /// 放行分支合并"的判断记录）。
        /// <para>
        /// 判断记录（<c>event</c> 分组，任务书"event 类目标 param.eventFilter：eventFilter: Expr"）：
        /// <c>event.&lt;field&gt;</c> 的具体字段名随触发本次求值的事件类型而变（见
        /// <c>Core.Rules.Common.IExprReadableEvent</c> 类型注释"事件字段可被 Expr 读取"），无法在
        /// 这里逐条穷举登记；本类型对外层调用（<c>quest</c>/<c>player</c>/<c>world</c>）坚持"精确
        /// 登记，不做已知分组放行"（避免 ADR-0015 参数字面量被误判为引用），但 <c>event</c> 分组
        /// 别无选择，只能采用与 <c>RulesExprSchema</c> 相同的"已知分组、未登记 key 一律放行"策略——
        /// 二者的差异不影响 ADR-0015 的场景，因为 <c>event.eventFilter</c> 文本里从不会出现
        /// "把某个 <c>event.&lt;field&gt;</c> 引用当函数调用参数传给另一个 <c>event.*</c> 调用"这种
        /// 自我嵌套写法（<c>event</c> 分组的字段访问永远只出现在比较表达式的一侧，不会有
        /// <c>event.get(event.foo)</c> 这类形态），因此对 <c>event</c> 单独放宽不会引入
        /// <c>WorldExprSchemaEntries</c> 判断记录里描述的那个问题。</para>
        /// </summary>
        public static IExprSchema BuildParsingSchema()
        {
            var schema = RegisterInto(new ExprSchema());
            Core.Gameplay.WorldState.WorldExprSchemaEntries.RegisterInto(schema);
            return new EventGroupPermissiveSchema(schema);
        }

        /// <summary>把内层精确登记表包一层：<c>event</c> 分组下任意未登记的 key 一律视为"合法引用，
        /// 签名未知"放行（见 <see cref="BuildParsingSchema"/> 判断记录），其余分组严格委托给内层。</summary>
        private sealed class EventGroupPermissiveSchema : IExprSchema
        {
            private static readonly ExprSignature Permissive = new ExprSignature(ExprValueKind.Bool, Array.Empty<ExprValueKind>());

            private readonly IExprSchema _inner;

            public EventGroupPermissiveSchema(IExprSchema inner)
            {
                _inner = inner;
            }

            public bool TryGetSignature(string group, string key, out ExprSignature signature)
            {
                if (_inner.TryGetSignature(group, key, out signature))
                {
                    return true;
                }

                if (group == ExprGroups.Event)
                {
                    signature = Permissive;
                    return true;
                }

                signature = default;
                return false;
            }
        }
    }
}
