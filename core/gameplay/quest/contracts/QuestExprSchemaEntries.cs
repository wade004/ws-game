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
        /// 措辞对齐消费方反馈第四批第 24 条，2026-09-10；ADR-0076 修订，2026-09-23）：
        /// <c>event.&lt;field&gt;</c> 的具体字段名随触发本次求值的事件类型而变（见
        /// <c>Core.Rules.Common.IExprReadableEvent</c> 类型注释"事件字段可被 Expr 读取"），任何一份
        /// 引用登记表都不可能逐条穷举登记；本类型对外层调用（<c>quest</c>/<c>player</c>/<c>world</c>）
        /// 坚持"精确登记，未登记则回退为 Id 字面量"（避免 ADR-0015 参数字面量被误判为引用），但
        /// <c>event</c> 分组不适用这套"已登记/未登记"二态判断——事件键由内容自由定义、可能在运行时
        /// 才登记，本类型对该分组不注入任何签名：<see cref="EventGroupPermissiveSchema.TryGetSignature"/>
        /// 对 <c>event</c> 分组不做特殊处理，原样把"未登记"结果（<c>false</c>）透传给调用方。</para>
        /// <para>
        /// ADR-0076 根治前，这里曾对 <c>event</c> 分组返回一个固定签名（"参数未知、返回 Bool"），
        /// 意图是"放行"，但实现成了"谎称已知且返回类型恒为 Bool"——<see cref="ExprValidator.ValidateCompare"/>
        /// 据此认定 <c>event.hit_result == "Hit"</c> 一类比较的左侧类型是 Bool，与右侧 String/Id
        /// 字面量比较判定"两侧类型不一致"报 Error，而运行期 <c>event.hit_result</c> 实际返回 String——
        /// 静态期的假类型与运行期真实类型冲突，见消费方第二十批第 3 条。根治后：<c>TryGetSignature</c>
        /// 对未登记的 <c>event.&lt;key&gt;</c> 如实返回 <c>false</c>，调用方（<see cref="ExprValidator.ValidateReference"/>）
        /// 已有的"未登记 key 且 group 为 event 时不报 UnknownKey、返回类型未知（<c>null</c>）"分支
        /// （该方法判断记录，本次未改动）据此把它当"类型未知"处理——<see cref="ExprValidator.ValidateCompare"/>/
        /// <see cref="ExprValidator.CheckBoolOperand"/> 对类型未知的一侧一律跳过类型检查而不是报错，
        /// 这才是名副其实的"放行"：不假装知道一个静态期本就无法确定的类型。<see cref="ExprParser.ParseIdentTerm"/>
        /// 的解析期判定不依赖 <c>TryGetSignature</c> 对 <c>event</c> 返回 <c>true</c>——它已有独立的
        /// <c>isEventFallbackReference</c> 分支（该方法判断记录，本次未改动），未登记的
        /// <c>event.&lt;key&gt;</c> 始终解析成 <see cref="ExprReferenceNode"/> 而不会退化成 Id 字面量，
        /// 因此本次改动不影响解析期行为、也不影响运行期 <c>RulesExprHostFactory.QueryEvent</c> 求值
        /// 路径（未触碰）。</para>
        /// <para>
        /// 未采纳"开放按 key 精确注册 event 签名的入口，让消费方自行登记"：那要求消费方为关心的每一个
        /// 事件字段单独注册签名，而 <see cref="PresentationSchemaCatalog.FullExprSchema"/>（内部固定
        /// 引用本类型 <see cref="BuildParsingSchema"/> 的产出）是 <c>PresentationAssembly</c> 硬编码
        /// 构造 <c>FeedbackRule</c>/<c>FeedbackBinder</c> 时使用的唯一一份，消费方即便自己另外
        /// compose 一份更精确的 schema 传入 <c>DataRegistryOptions.ExprSchema</c>，也只能让"内容校验期"
        /// 通过、覆盖不了框架内部这一份用于其它模块校验/运行期装配的 schema，会造成"校验期用一份、
        /// 运行期解析用另一份"的不一致（ADR-0015 决策 3 明确要求两者一致）——治标不治本。等到事件目录
        /// 本身开始登记各事件类型携带哪些字段、各自什么类型时，再谈按事件类型精确登记 <c>event</c>
        /// 分组签名，是这里的将来演进方向，不是本次改动范围。</para>
        /// </summary>
        public static IExprSchema BuildParsingSchema()
        {
            var schema = RegisterInto(new ExprSchema());
            Core.Gameplay.WorldState.WorldExprSchemaEntries.RegisterInto(schema);
            return new EventGroupPermissiveSchema(schema);
        }

        /// <summary>把内层精确登记表包一层：<see cref="TryGetSignature"/> 对全部分组（含
        /// <c>event</c>）如实委托给内层，不再对 <c>event</c> 分组返回任何注入的固定签名（ADR-0076，
        /// 见 <see cref="BuildParsingSchema"/> 判断记录——"未登记就如实说未登记"，交给
        /// <see cref="ExprValidator"/>/<see cref="ExprParser"/> 已有的"类型未知则跳过静态检查"分支
        /// 处理，而不是在这里冒充一个已知类型）。类型名沿用"Permissive"是历史命名，语义已改为
        /// "对 event 分组不做任何特殊拦截"，重命名不在本次改动范围内（避免无谓的大范围重命名 diff）。</summary>
        private sealed class EventGroupPermissiveSchema : IExprSchema
        {
            private readonly IExprSchema _inner;

            public EventGroupPermissiveSchema(IExprSchema inner)
            {
                _inner = inner;
            }

            public bool TryGetSignature(string group, string key, out ExprSignature signature) =>
                _inner.TryGetSignature(group, key, out signature);

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
