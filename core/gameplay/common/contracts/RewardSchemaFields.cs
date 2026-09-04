using Core.Foundation.DataRegistry;

namespace Core.Gameplay.Common
{
    /// <summary>
    /// <c>rewards</c> 字段的 <see cref="FieldSchema"/> 帮助方法：<c>quest.def</c>/
    /// <c>encounter.def</c>/<c>achv.def</c> 三张表都有一个结构相同、语义相同的 <c>rewards</c>
    /// 字段（见 <see cref="RewardBundle"/> 类型注释），本类型提供统一的字段声明，避免三处各自
    /// 重复拼写同一段 <see cref="FieldSchema"/> 构造代码与说明文字。字段本身的内部结构（items/xp/
    /// currency/skills/world_flags/talent_points）不是 <see cref="TableSchema"/>/<see cref="FieldSchema"/>
    /// 能表达的嵌套形状（04 记法里 <see cref="FieldKind.Object"/> 只做"存在且是对象"检查），
    /// 因此登记为 <see cref="FieldKind.Object"/>，具体内部字段的校验由
    /// <see cref="RewardBundle.FromRecord"/> 在解析期做（格式错误抛 <see cref="System.FormatException"/>）。
    /// </summary>
    public static class RewardSchemaFields
    {
        /// <summary>供内容表登记 <c>rewards</c> 字段用（08 第 2.1 节 <c>quest.def.rewards</c> 整体
        /// 标注"否"，即非必填，未提供时 <see cref="RewardBundle.FromRecord"/> 返回
        /// <see cref="RewardBundle.Empty"/>）。</summary>
        public static FieldSchema Rewards(bool required = false) => new FieldSchema(
            "rewards", FieldKind.Object, required,
            description: "{items, xp, currency, skills, world_flags, talent_points}，见 Core.Gameplay.Common.RewardBundle");
    }
}
