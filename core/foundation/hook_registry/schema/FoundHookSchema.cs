using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.HookRegistry
{
    /// <summary>
    /// <c>found.hook</c> 表的 <see cref="TableSchema"/> 登记（见 04_数据与内容管线.md 第 1.1
    /// 节总索引"脚本钩子注册表：钩子 id、触发时机、参数签名"、本模块
    /// <c>schema/found.hook.md</c> 字段表）。
    /// <para>
    /// 判断记录（主键字段名为 <c>id</c>、<see cref="TableSchema.IsRegistryTable"/> 取默认值
    /// false）：同 <c>Core.Foundation.AppLifecycle.FoundGameStateSchema</c> 判断记录——本表
    /// 每一行的 <c>id</c> 形如 <c>found.hook.&lt;name&gt;</c>，domain 前缀本身就是 <c>found</c>，
    /// 与表名首段一致，不是跨 domain 登记表，不需要豁免"id 的 domain 前缀应等于表名首段"检查。
    /// </para>
    /// </summary>
    public static class FoundHookSchema
    {
        public static readonly TableSchema Table = new TableSchema(
            name: "found.hook",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true,
                    description: "found.hook.<name>，见 schema/found.hook.md"),
                new FieldSchema("signature", FieldKind.String, required: true,
                    description: "参数签名说明文本，供校验与文档化，不做机器可读的强类型声明"),
                new FieldSchema("allow_multiple", FieldKind.Bool, required: false,
                    description: "本挂载点是否允许注册多个回调，缺省 true"),
                new FieldSchema("description", FieldKind.String, required: false,
                    description: "该挂载点的说明文本，供编辑器/文档展示，可为空"),
            },
            migrations: Array.Empty<TableMigration>());

        /// <summary>全部内置 schema，供 <see cref="IDataRegistry.RegisterSchema"/> 批量登记
        /// （本模块目前只有一张表，保留该属性与其它模块的 <c>*Schemas.All</c>/<c>*Schema.All</c>
        /// 惯例一致）。</summary>
        public static IReadOnlyList<TableSchema> All { get; } = new[] { Table };

        /// <summary>
        /// 收边任务补齐的契约缺口（见 <c>schema/found.hook.md</c>"本模块不做什么"一节此前遗留
        /// 的"从数据文件读取并转换成 <see cref="HookPointDefinition"/> 列表是数据注册表的
        /// 职责"）：从 <paramref name="registry"/> 读取 <c>found.hook</c> 表全部行，转换成
        /// <see cref="HookPointDefinition"/> 列表。
        /// <para>
        /// 判断记录（缺表时退化为 <see cref="WellKnownHooks"/> 两个内置挂载点，行为不变）：
        /// 与 <c>AppStateMachineConfig.FromRegistry</c> 同一判断——<paramref name="registry"/>
        /// 未注册/未加载 <c>found.hook</c> 表时 <see cref="IDataRegistryView.GetAll"/> 返回空
        /// 列表，本方法据此回退到 <see cref="WellKnownHooks.ScenePreUnload"/>/
        /// <see cref="WellKnownHooks.ScenePostLoad"/> 两个内置常量（签名照抄各自注释），维持
        /// 集成任务补齐 <see cref="Core.Foundation.SceneRouter"/> 之前"至少这两个挂载点可用"的
        /// 既有行为；<c>data/_framework/found/found.hook.json</c> 已提供与之等价的默认数据行，
        /// 正常装配路径下两者结果一致。
        /// </para>
        /// </summary>
        public static IReadOnlyList<HookPointDefinition> LoadDefinitions(IDataRegistryView registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            var rows = registry.GetAll(Table.Name);
            if (rows.Count == 0)
            {
                return DefaultDefinitions();
            }

            var definitions = new List<HookPointDefinition>(rows.Count);
            foreach (var row in rows)
            {
                row.TryGetBool("allow_multiple", out var allowMultiple);
                var allowMultipleValue = row.Has("allow_multiple") ? allowMultiple : true;
                row.TryGetString("description", out var description);
                definitions.Add(new HookPointDefinition(
                    row.GetId("id"), row.GetString("signature"), allowMultipleValue,
                    string.IsNullOrEmpty(description) ? null : description));
            }

            return definitions;
        }

        /// <summary>与 <c>data/_framework/found/found.hook.json</c> 等价的内存默认值（见类型级
        /// 判断记录）。</summary>
        public static IReadOnlyList<HookPointDefinition> DefaultDefinitions() => new[]
        {
            new HookPointDefinition(
                WellKnownHooks.ScenePreUnload, "sceneId: Id", allowMultiple: true,
                "场景卸载前触发，存档系统在此完成自动存档（见 03 第 6 节步骤 4）"),
            new HookPointDefinition(
                WellKnownHooks.ScenePostLoad, "sceneId: Id", allowMultiple: true,
                "场景加载与初始化完成、应用状态机转入 InWorld 后触发（见 03 第 6 节步骤 6）"),
        };
    }
}
