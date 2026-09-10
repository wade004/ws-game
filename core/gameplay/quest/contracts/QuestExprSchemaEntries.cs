using System;
using System.Collections.Generic;
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
        /// 判断记录（<c>event</c> 分组，任务书"event 类目标 param.eventFilter：eventFilter: Expr"；
        /// 措辞对齐消费方反馈第四批第 24 条，2026-09-10）：<c>event.&lt;field&gt;</c> 的具体字段名
        /// 随触发本次求值的事件类型而变（见 <c>Core.Rules.Common.IExprReadableEvent</c> 类型注释
        /// "事件字段可被 Expr 读取"），任何一份引用登记表都不可能逐条穷举登记；本类型对外层调用
        /// （<c>quest</c>/<c>player</c>/<c>world</c>）坚持"精确登记，未登记则回退为 Id 字面量"
        /// （避免 ADR-0015 参数字面量被误判为引用），但 <c>event</c> 分组不适用这套"已登记/未登记"
        /// 二态判断——事件键由内容自由定义、可能在运行时才登记，本类型对该分组直接不做签名校验：
        /// <see cref="EventGroupPermissiveSchema.TryGetSignature"/> 对 <c>event</c> 分组恒返回
        /// <c>true</c>（签名固定为"参数未知、返回 Bool"），不存在"某个 event key 已登记、会被真正
        /// 校验"的状态（本类型从不登记任何具体 <c>event.*</c> 签名）。这与 <c>RulesExprSchema</c>
        /// 早期"已知分组内未登记 key 一律放行"的历史策略形似但语义不同：那是"部分 key 可能已登记、
        /// 其余放行"，本类型是"整个分组都不做静态签名检查"。不影响 ADR-0015 的消歧场景，因为
        /// <c>event.eventFilter</c> 文本里从不会出现"把某个 <c>event.&lt;field&gt;</c> 引用当函数
        /// 调用参数传给另一个 <c>event.*</c> 调用"这种自我嵌套写法（<c>event</c> 分组的字段访问永远
        /// 只出现在比较表达式的一侧，不会有 <c>event.get(event.foo)</c> 这类形态），因此对
        /// <c>event</c> 恒放行不会引入 <c>WorldExprSchemaEntries</c> 判断记录里描述的那个问题。</para>
        /// </summary>
        public static IExprSchema BuildParsingSchema()
        {
            var schema = RegisterInto(new ExprSchema());
            Core.Gameplay.WorldState.WorldExprSchemaEntries.RegisterInto(schema);
            return new EventGroupPermissiveSchema(schema);
        }

        /// <summary>把内层精确登记表包一层：<c>event</c> 分组不做签名校验，
        /// <see cref="TryGetSignature"/> 对该分组恒放行（返回"参数未知、返回 Bool"的固定签名，不区分
        /// 具体 key 是否曾经登记过——本类型从不登记任何 <c>event.*</c> 签名，见
        /// <see cref="BuildParsingSchema"/> 判断记录），其余分组严格委托给内层，内层未登记的 key 按
        /// 该分组自身规则处理（普通分组回退为 Id 字面量）。</summary>
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

            /// <summary>
            /// 消费方反馈（编辑器）第 27 条根治（2026-09-11，见
            /// architecture/落地计划/消费方反馈-2026-09-11-编辑器-第27条.md）：<c>event</c> 分组
            /// 恒放行、不存在"已登记 key 集合"（见 <see cref="TryGetSignature"/> 与
            /// <see cref="BuildParsingSchema"/> 判断记录——事件字段由内容自由定义，本类型从不登记
            /// 任何具体 <c>event.*</c> 签名，因此没有"该分组下已知 key 有哪些"这个问题的答案，
            /// 恒返回空集合，不是遗漏，是"恒放行"语义本身决定的——不像其它分组"未登记 = 回退为字面量"，
            /// 这里是"任何 key 都合法、也没有一份清单"），其它分组转发给 <see cref="_inner"/>。
            /// </summary>
            public IReadOnlyCollection<string> KnownKeys(string group) =>
                group == ExprGroups.Event ? Array.Empty<string>() : _inner.KnownKeys(group);

            /// <summary>
            /// 直接转发给 <see cref="_inner"/>，不额外把 <see cref="ExprGroups.Event"/> 塞进结果——
            /// <see cref="IExprSchema.KnownGroups"/> 的契约是"已出现过至少一个已登记 key 的全部分组"
            /// （见接口成员注释、<see cref="ExprSchema.KnownGroups"/> 同一份判断记录），本类型的
            /// <c>event</c> 分组从不登记任何具体 key（见 <see cref="TryGetSignature"/>/
            /// <see cref="KnownKeys"/> 判断记录"恒放行、无登记集合"），按该契约字面语义就不应出现
            /// 在这里——"恒放行"是"未登记 key 也算合法引用"的特殊匹配规则，不等价于"已登记过 key"。
            /// </summary>
            public IReadOnlyCollection<string> KnownGroups => _inner.KnownGroups;
        }
    }
}
