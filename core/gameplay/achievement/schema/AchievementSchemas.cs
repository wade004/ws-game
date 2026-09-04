using Core.Foundation.DataRegistry;
using Core.Gameplay.Common;

namespace Core.Gameplay.Achievement
{
    /// <summary>
    /// <c>achv.def</c> 的 <see cref="TableSchema"/> 声明（见 08 第 6.1 节字段表 + 任务书拍板补录
    /// <c>name_key</c>）。
    /// <para>
    /// 判断记录（<c>criteria</c> 用 <see cref="FieldKind.Array"/>）：<c>criteria</c> 是
    /// <c>{type, observe_event, target_ref?, count, filter?}</c> 的对象数组，不是 04 记法里任何
    /// 一种内置字段类型能表达的嵌套形状（同 <c>Core.Gameplay.Common.RewardSchemaFields.Rewards</c>
    /// 判断记录"内部字段的校验由 XxxBundle.FromRecord 在解析期做"同一惯例），因此登记为
    /// <see cref="FieldKind.Array"/>（"存在且是数组"），内部结构校验交给
    /// <see cref="AchievementCriterion.FromRecord"/>（结构错误）与
    /// <see cref="AchievementContentValidationRule"/>（业务规则：<c>type</c> 合法、
    /// <c>observe_event</c> 已登记）。
    /// </para>
    /// </summary>
    public static class AchievementSchemas
    {
        public static readonly TableSchema Def = new TableSchema(
            name: "achv.def",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "achv.<name>"),
                new FieldSchema("name_key", FieldKind.TextKey, required: true,
                    description: "显示名文本键（08 第 6.1 节未列出，任务书拍板补录）"),
                new FieldSchema("criteria", FieldKind.Array, required: true,
                    description: "List<{type, observe_event, target_ref?, count, filter?}>，见本类型判断记录"),
                RewardSchemaFields.Rewards(),
            });
    }
}
