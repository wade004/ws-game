using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Presentation.VfxSfx.Schema
{
    /// <summary>
    /// 本模块拥有的三张表的 <see cref="TableSchema"/> 登记（见 04_数据与内容管线.md 第 1.1 节表
    /// 清单、09_表现层.md 第 5.1、5.2、4.4 节字段表）：<c>vfx.def</c>、<c>sfx.def</c>、
    /// <c>display.weapon_style</c>（与 <c>Core.Foundation.DisplayInfo.DisplaySchemas</c> 同一
    /// 惯例：只登记 schema 供 <see cref="IDataRegistry.RegisterSchema"/> 使用，本任务不接入真实
    /// 数据集，见 presentation/vfx_sfx/README.md"不负责什么"）。
    /// </summary>
    public static class VfxSfxSchemas
    {
        private static readonly string[] AttachModes = { "world", "anchor", "socket", "screen" };

        /// <summary><c>vfx.def</c>（09 第 5.1 节）。</summary>
        public static readonly TableSchema Vfx = new TableSchema(
            name: "vfx.def",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "vfx.<name>"),
                new FieldSchema("category", FieldKind.String, required: true, description: "分类（施法/命中/环境/UI 等），供对象池分组与音画分层参考"),
                new FieldSchema("attach_mode", FieldKind.Enum, required: true, enumValues: AttachModes),
                new FieldSchema("lifetime", FieldKind.Number, required: false, description: "预期存活时长，用于对象池回收兜底"),
                new FieldSchema("resource_ref", FieldKind.Id, required: true, description: "指向具体引擎资源的间接引用，由适配层解释"),
            },
            migrations: Array.Empty<TableMigration>());

        /// <summary><c>sfx.def</c>（09 第 5.2 节）。</summary>
        public static readonly TableSchema Sfx = new TableSchema(
            name: "sfx.def",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "sfx.<name>"),
                new FieldSchema("layer", FieldKind.String, required: true, description: "分层音效轨道（如 战斗/环境/UI/语音），供混音分组"),
                new FieldSchema("priority", FieldKind.Int, required: false, description: "同轨道抢占优先级"),
                new FieldSchema("variants", FieldKind.IdList, required: false, description: "多个随机变体资源引用，播放时随机挑一个"),
                new FieldSchema("resource_ref", FieldKind.Id, required: true, description: "指向具体引擎资源的间接引用"),
            },
            migrations: Array.Empty<TableMigration>());

        /// <summary><c>display.weapon_style</c>（09 第 4.4 节）：判断记录同
        /// <c>Core.Foundation.DisplayInfo.DisplaySchemas</c>——<c>cast_anim_override</c>/
        /// <c>impact_vfx_override</c> 是 "Id → Id" 映射，schema 层只声明为 <see cref="FieldKind.Object"/>
        /// （结构由上层模块自行解释，见 <see cref="FieldKind.Object"/> 类型注释），不做逐键引用校验。</summary>
        public static readonly TableSchema WeaponStyle = new TableSchema(
            name: "display.weapon_style",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "display.weapon_style.<name>"),
                new FieldSchema("auto_attack_anim", FieldKind.Id, required: true, description: "普攻动作剪辑引用"),
                new FieldSchema("cast_anim_override", FieldKind.Object, required: false, description: "按技能 id 覆盖施法动作剪辑：{skillId: animClipId}"),
                new FieldSchema("swing_vfx", FieldKind.Id, required: false, description: "挥舞轨迹特效，指向 vfx.def"),
                new FieldSchema("impact_vfx_override", FieldKind.Object, required: false, description: "按技能 id 覆盖命中特效：{skillId: vfxId}"),
            },
            migrations: Array.Empty<TableMigration>());

        public static IReadOnlyList<TableSchema> All { get; } = new[] { Vfx, Sfx, WeaponStyle };
    }
}
