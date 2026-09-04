using Core.Foundation.DataRegistry;
using Core.Gameplay.Common;

namespace Core.Gameplay.Quest
{
    /// <summary>
    /// <c>quest.def</c> 的 <see cref="TableSchema"/> 声明（见 08 第 2.1 节字段表 + 任务书补充
    /// <c>title_key</c>/<c>description_key</c>）。<c>objectives</c>/<c>rewards</c> 的内部嵌套结构
    /// 不是 <see cref="FieldSchema"/> 能表达的形状，登记为 <see cref="FieldKind.Array"/>/
    /// <see cref="FieldKind.Object"/>（只做"存在且是数组/对象"检查），具体内部结构校验见
    /// <see cref="QuestContentValidationRule"/>（经 <see cref="QuestDefinition.FromRecord"/> 解析，
    /// 解析失败即报告为一条校验问题）。
    /// </summary>
    public static class QuestSchemas
    {
        public static readonly TableSchema Def = new TableSchema(
            name: "quest.def",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "quest.<name>"),
                new FieldSchema("title_key", FieldKind.TextKey, required: true, description: "任务标题文本键（08 原文未列出，任务书拍板补录）"),
                new FieldSchema("description_key", FieldKind.TextKey, required: false, description: "任务描述文本键（08 原文未列出，任务书拍板补录）"),
                new FieldSchema("objectives", FieldKind.Array, required: true, description: "List<QuestObjective>，见 08 第 2.1 节"),
                new FieldSchema("prerequisite", FieldKind.Expr, required: false, description: "前置条件 Expr"),
                new FieldSchema("exclusive_group", FieldKind.Id, required: false, description: "互斥组 id"),
                new FieldSchema("start_method", FieldKind.Enum, required: true, enumValues: QuestEnumWireNames.StartMethodValues),
                new FieldSchema("turn_in_method", FieldKind.Enum, required: true, enumValues: QuestEnumWireNames.TurnInMethodValues),
                RewardSchemaFields.Rewards(required: false),
                new FieldSchema("repeatable", FieldKind.Enum, required: true, enumValues: QuestEnumWireNames.RepeatableValues),
            });
    }
}
