using System;
using System.Collections.Generic;
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
    /// 阶段 3 整理判断记录（ADR-0015 严格化）：本类型原先对九个已知分组（<see cref="ExprGroups.All"/>）
    /// 内未逐条登记的 <c>key</c> 一律"放行为合法引用、签名未知"，这直接违反 ADR-0015 的决策——
    /// "未登记的 group.key 组合一律落回 Id 字面量"。放行分支会把 <c>quest.deliver_letter</c>、
    /// <c>world.bridge.repaired</c> 这类内容 id 字面量误判成"合法引用，只是签名未知"，解析虽不报错，
    /// 但语义已经错了（<see cref="ExprEvaluator"/> 会先对这个"引用"求值，而不是把它当参数原样传给
    /// 外层调用，见迁移前 <c>core/gameplay/world_state/contracts/WorldExprSchemaEntries.cs</c> 判断
    /// 记录里给出的具体故障链路）。本类型现改为严格模式：<see cref="TryGetSignature"/> 只认
    /// <see cref="Build"/> 逐条登记过的 <c>group.key</c>，未登记一律返回 <c>false</c>（交给
    /// <see cref="ExprParser"/> 按 Id 字面量解析）。
    /// </para>
    /// <para>
    /// 登记表可组合（同一判断记录）：<c>event</c>/<c>world</c>/<c>quest</c>/<c>player</c> 四个分组
    /// 的具体字段名/key 随事件类型或游戏层内容而变，本类型构造期不可能穷举——严格模式下，这些分组
    /// 下的合法引用（如游戏层接入的 <c>world.get</c>/<c>quest.is_active</c>）必须由调用方经
    /// <see cref="Compose"/> 把游戏层自己的登记表与 <see cref="Base"/> 合并后使用，本类型自身
    /// （<see cref="Base"/>）只登记 04 第 6.2 节示例给出的、<see cref="RulesExprHostFactory"/> 内置
    /// 实现真正支持查询的 <c>self</c>/<c>target</c>/<c>combat</c>/<c>enemies</c>/<c>time</c> 五个
    /// 分组。
    /// </para>
    /// </summary>
    public sealed class RulesExprSchema : IExprSchema
    {
        /// <summary>
        /// L2 基础登记表：全架构共用同一份不可变实例（本类型不持有任何可变状态，也不逐分组放行
        /// 未登记 key，见类型顶部判断记录），只覆盖 <see cref="RulesExprHostFactory"/> 内置支持的
        /// <c>self</c>/<c>target</c>/<c>combat</c>/<c>enemies</c>/<c>time</c> 五个分组。需要
        /// <c>event</c>/<c>world</c>/<c>quest</c>/<c>player</c> 等游戏层分组时经 <see cref="Compose"/>
        /// 叠加游戏层自己的登记表，不要绕过本字段自建一套平行的"基础五分组"登记。
        /// </summary>
        public static readonly RulesExprSchema Base = new RulesExprSchema();

        private readonly ExprSchema _known = Build();

        private RulesExprSchema()
        {
        }

        public bool TryGetSignature(string group, string key, out ExprSignature signature) =>
            _known.TryGetSignature(group, key, out signature);

        /// <summary>
        /// 消费方反馈（编辑器）第 27 条根治（2026-09-11，见
        /// architecture/落地计划/消费方反馈-2026-09-11-编辑器-第27条.md）：转发给
        /// <see cref="_known"/>（真正持有全部已登记签名的内层 <see cref="ExprSchema"/>）——本类型
        /// 不显式重写会落回 <see cref="IExprSchema.KnownKeys"/> 的默认实现（恒返回空集合），与
        /// <see cref="TryGetSignature"/> 已经转发给 <see cref="_known"/> 的行为不一致（签名能查到，
        /// 已知 key 列表却是空的）。
        /// </summary>
        public IReadOnlyCollection<string> KnownKeys(string group) => _known.KnownKeys(group);

        /// <summary>同上一份判断记录：转发给 <see cref="_known"/>。</summary>
        public IReadOnlyCollection<string> KnownGroups => _known.KnownGroups;

        /// <summary>
        /// 把若干份游戏层/上层模块自己的 <see cref="IExprSchema"/>（如 <c>world.get</c>/
        /// <c>quest.is_active</c> 一类分组的精确登记表）与 <see cref="Base"/> 合并成一份
        /// <see cref="IExprSchema"/>——按顺序依次尝试 <paramref name="extras"/>、最后落到
        /// <see cref="Base"/>，第一个命中的签名生效（见 <see cref="CompositeExprSchema"/>）。
        /// <paramref name="extras"/> 为空（或未传）时直接返回 <see cref="Base"/> 本身，不额外包一层
        /// <see cref="CompositeExprSchema"/>。
        /// </summary>
        public static IExprSchema Compose(params IExprSchema[] extras)
        {
            if (extras == null || extras.Length == 0)
            {
                return Base;
            }

            var all = new IExprSchema[extras.Length + 1];
            Array.Copy(extras, all, extras.Length);
            all[extras.Length] = Base;
            return new CompositeExprSchema(all);
        }

        private static ExprSchema Build()
        {
            var schema = new ExprSchema();

            RegisterSelfAndTarget(schema, ExprGroups.Self);
            RegisterSelfAndTarget(schema, ExprGroups.Target);

            // self 专用三项：target 分组不登记同名 key（distance_to_target/threat_top 在 target
            // 分组下未登记，严格模式下会被 ExprParser 按 Id 字面量解析，不再是"放行为签名未知的
            // 引用"——RulesExprHostFactory.QueryUnit 的 target 分支本就不实现这两个 key，行为不变，
            // 只是"点分标识符归类"的判定依据从"放行"改成"未登记→字面量"，见类型顶部判断记录）。
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

            // event/world/quest/player：本类型不登记（分组具体 key 由游戏层决定），见类型顶部
            // 判断记录——需要这些分组的合法引用时经 Compose 叠加游戏层自己的登记表。
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
