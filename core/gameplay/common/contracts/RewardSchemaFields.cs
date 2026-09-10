using Core.Foundation.DataRegistry;

namespace Core.Gameplay.Common
{
    /// <summary>
    /// <c>rewards</c> 字段的 <see cref="FieldSchema"/> 帮助方法：<c>quest.def</c>/
    /// <c>encounter.def</c>/<c>achv.def</c> 三张表都有一个结构相同、语义相同的 <c>rewards</c>
    /// 字段（见 <see cref="RewardBundle"/> 类型注释）。
    /// <para>
    /// P3 文档漂移根治（外部审计 audit-76d16a5-20260910）：本类型原注释说"字段内部结构不是
    /// <see cref="FieldSchema"/> 能表达的嵌套形状"——这句话已经过期，<see cref="FieldSchema"/>
    /// 从 ADR-0019/F1b 起新增了 <see cref="FieldSchema.Fields"/>（Object 的子字段清单）、<see
    /// cref="FieldSchema.Item"/>（Array 元素结构）、<see cref="FieldSchema.Variants"/>（判别式联合），
    /// 完全可以表达 <c>items</c>/<c>currency</c>/<c>skills</c>/<c>world_flags</c> 这类嵌套数组/对象。
    /// 三张表目前都各自登记了带完整 <c>Fields</c> 的 <c>rewards</c>（<c>Core.Gameplay.Quest.
    /// QuestSchemas.RewardsFields</c> 是首个落地、供 <c>Core.Gameplay</c> 单一程序集内其余模块直接
    /// 引用复用的版本，见 <c>quest.def.md</c>"登记落点"判断记录），本类型这个 <see cref="Rewards"/>
    /// 帮助方法因此目前没有任何调用方——保留下来是为了不破坏"三表共用同一入口"这条既有公开 API 形状，
    /// 上游若要收口，应当让本方法也改为返回带 <c>Fields: QuestSchemas.RewardsFields</c> 的
    /// <see cref="FieldSchema"/>（会引入对 <c>Core.Gameplay.Quest</c> 命名空间的依赖，因此未在本次
    /// P3 范围内顺带做——那是一次行为变更，不是纯文档修正）。
    /// </para>
    /// <para>
    /// Object/联合值的边界：<c>Fields</c> 能表达"有哪些具名子字段、各自什么 <see cref="FieldKind"/>"，
    /// 但表达不了"这个位置的值可能是 Bool｜Number｜String｜Id 里的任意一种"这类判别式之外的联合类型
    /// ——<c>world_flags[].value</c>（经 <see cref="ExprValueJson.Parse"/> 解析）正是这类值，三张表
    /// 现有的 <c>RewardsFields</c> 都不登记它的形状，只登记同级的 <c>flagKey</c>；"value 必须存在"与
    /// "value 形状必须落在 <see cref="ExprValueJson.IsValid"/> 接受的集合内"两条判断改由各表自己的
    /// <c>ContentValidationRule</c> 手写兜底（Quest 侧即 <c>reward_world_flag_value_required</c>/
    /// <c>reward_world_flag_value_shape</c>，见 <c>QuestContentValidationRule</c> 判断记录）。
    /// </para>
    /// <para>
    /// 仍由 <see cref="RewardBundle.FromRecord"/> 在解析期（而非 <see cref="FieldSchema"/> 登记阶段）
    /// 校验的内部约束：<c>items[].count</c>/<c>currency[].amount</c>/<c>talent_points</c>/<c>xp</c>
    /// 的数值范围（正数/非负）是构造期硬约束（<see cref="System.ArgumentException"/>），各表的
    /// <c>ContentValidationRule</c> 在此之前先手写同款判断产出可定位的 report（如
    /// <c>reward_item_count_positive</c>），避免真的撞到 <see cref="RewardBundle.FromRecord"/> 才失败；
    /// <c>world_flags[].value</c> 的联合类型解析（格式错误抛 <see cref="System.FormatException"/>）
    /// 同理只在 <see cref="ExprValueJson.Parse"/> 里做一次，不重复实现。
    /// </para>
    /// </summary>
    public static class RewardSchemaFields
    {
        /// <summary>供内容表登记 <c>rewards</c> 字段用（08 第 2.1 节 <c>quest.def.rewards</c> 整体
        /// 标注"否"，即非必填，未提供时 <see cref="RewardBundle.FromRecord"/> 返回
        /// <see cref="RewardBundle.Empty"/>）。判断记录：目前仍只登记裸 <see cref="FieldKind.Object"/>
        /// （不带 <see cref="FieldSchema.Fields"/>），见类型顶部判断记录——当前没有任何调用方，三张
        /// 表都各自用自己的、带 <c>Fields</c> 的登记方式。</summary>
        public static FieldSchema Rewards(bool required = false) => new FieldSchema(
            "rewards", FieldKind.Object, required,
            description: "{items, xp, currency, skills, world_flags, talent_points}，见 Core.Gameplay.Common.RewardBundle");
    }
}
