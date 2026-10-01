using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.InputMap
{
    /// <summary>
    /// 一条宽限条件声明（<c>found.grace_condition</c> 一行，手感设计/01 第 2.4 节）：条件名与求值表达式（宿主为行动者上下文，
    /// 例如"当前目标在射程内""有锁定目标"）。框架提供机制，不预置任何条件；求值由上层经 <see cref="IGraceConditionEvaluator"/> 实现。
    /// </summary>
    public sealed class GraceConditionDefinition
    {
        /// <summary>条件名（<c>found.input_action.grace_conditions</c> 引用它）。</summary>
        public Id ConditionId { get; }

        /// <summary>求值表达式文本（Expr，宿主为行动者上下文）。</summary>
        public string Expr { get; }

        public string? Description { get; }

        public GraceConditionDefinition(Id conditionId, string expr, string? description = null)
        {
            if (string.IsNullOrEmpty(expr))
            {
                throw new ArgumentException($"宽限条件 \"{conditionId}\" 的表达式不能为空", nameof(expr));
            }

            ConditionId = conditionId;
            Expr = expr;
            Description = description;
        }

        /// <summary>从 <c>found.grace_condition</c> 的一行构造（主键字段名 <c>key</c>，登记表惯例同 <c>found.input_action</c>）。</summary>
        public static GraceConditionDefinition FromRecord(DataRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var description = record.TryGetString("description", out var d) ? d : null;
            return new GraceConditionDefinition(record.GetId("key"), record.GetString("expr"), description);
        }
    }

    /// <summary>
    /// <c>found.grace_condition</c> 表的 <see cref="TableSchema"/> 登记（手感设计/01 第 2.4 节）。
    /// 与 <c>found.input_action</c> 同为登记表（主键字段名 <c>key</c>，记录 id 域名是 <c>input</c>）；框架默认数据根不带任何行，
    /// 由游戏层声明自己的条件。
    /// </summary>
    public static class GraceConditionSchema
    {
        public static readonly TableSchema Table = new TableSchema(
            name: "found.grace_condition",
            primaryKey: "key",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("key", FieldKind.Id, required: true,
                    description: "宽限条件名，如 input.grace.target_in_range；登记表主键字段名为 key（同 found.input_action）"),
                new FieldSchema("expr", FieldKind.Expr, required: true,
                    description: "求值表达式（Expr，宿主为行动者上下文）：每 tick 求值，成立则记录该 tick 为最近一次为真"),
                new FieldSchema("description", FieldKind.String, required: false,
                    description: "条件说明文本，供编辑器/文档展示，可为空"),
            },
            migrations: Array.Empty<TableMigration>(),
            isRegistryTable: true)
            .WithOwnership(SchemaLayer.Foundation, "foundation");

        /// <summary>全部内置 schema（惯例同 <see cref="InputActionSchema.All"/>）。</summary>
        public static IReadOnlyList<TableSchema> All { get; } = new[] { Table };
    }
}
