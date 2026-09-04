using Core.Foundation.DataRegistry;
using Core.Gameplay.Dialog;

namespace Core.Gameplay.Dialog
{
    /// <summary>
    /// <c>dialog.gossip_menu</c>/<c>dialog.story_tree</c> 的 <see cref="TableSchema"/> 声明（见 08
    /// 第 3.1、3.2 节字段表）。<c>options</c>/<c>nodes</c> 内部嵌套结构登记为
    /// <see cref="FieldKind.Array"/>（只做"存在且是数组"检查），具体内部结构校验见
    /// <see cref="DialogContentValidationRule"/>（经 <see cref="GossipMenuDefinition.FromRecord"/>/
    /// <see cref="StoryTreeDefinition.FromRecord"/> 解析）。
    /// </summary>
    public static class DialogSchemas
    {
        public static readonly TableSchema GossipMenu = new TableSchema(
            name: "dialog.gossip_menu",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "dialog.<name> 或内容作者自定义命名"),
                new FieldSchema("options", FieldKind.Array, required: true, description: "List<GossipOption>，见 08 第 3.1 节"),
            });

        public static readonly TableSchema StoryTree = new TableSchema(
            name: "dialog.story_tree",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "dialog.<name> 或内容作者自定义命名"),
                new FieldSchema("nodes", FieldKind.Array, required: true, description: "List<StoryNode>，见 08 第 3.2 节"),
            });
    }
}
