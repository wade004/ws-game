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
                new FieldSchema("attach_mode", FieldKind.Enum, required: true, enumValues: AttachModes, description: "挂载方式（world/anchor/socket/screen），决定特效跟随谁播放"),
                new FieldSchema("lifetime", FieldKind.Number, required: false, description: "预期存活时长，用于对象池回收兜底"),
                new FieldSchema("resource_ref", FieldKind.Id, required: true, description: "指向具体引擎资源的间接引用，由适配层解释"),
            },
            migrations: Array.Empty<TableMigration>()).WithOwnership(SchemaLayer.Presentation, "vfx");

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
                new FieldSchema("variants", FieldKind.IdList, required: false, description: "多个随机变体资源引用，播放时随机挑一个")
                    .WithFreeIds("指向具体引擎资源的间接引用，不是任何已登记内容表的记录 id"),
                new FieldSchema("resource_ref", FieldKind.Id, required: true, description: "指向具体引擎资源的间接引用"),
            },
            migrations: Array.Empty<TableMigration>()).WithOwnership(SchemaLayer.Presentation, "sfx");

        /// <summary><c>display.weapon_style</c>（09 第 4.4 节）：判断记录同
        /// <c>Core.Foundation.DisplayInfo.DisplaySchemas</c>——<c>cast_anim_override</c>/
        /// <c>impact_vfx_override</c> 是 "Id → Id" 映射。ADR-0024 第二批登记：键（技能 id）与值
        /// （动作剪辑/特效引用 id）均登记为 <see cref="MapSchema.FreeKeyed"/> + <see cref="FieldKind.Id"/>
        /// ——只做格式校验，不做逐键引用完整性校验，理由与本表 <c>auto_attack_anim</c>/
        /// <c>swing_vfx</c> 两个同类字段维持 <see cref="FieldKind.Id"/>（不升级为 Reference）完全一致
        /// （本模块不静态耦合 skill.def/vfx.def 的表结构，见类型注释与 <c>WeaponStyleDef.ParseIdMap</c>
        /// 判断记录），只是把"存在且是对象"升级为"键值均是合法 Id 格式"，与
        /// <c>WeaponStyleDef.ParseIdMap</c> 对键值均 <c>Id.TryParse</c>、非法即抛
        /// <c>DataFieldException</c> 的解析代码逐字段核对一致。</summary>
        public static readonly TableSchema WeaponStyle = new TableSchema(
            name: "display.weapon_style",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "display.weapon_style.<name>"),
                new FieldSchema("auto_attack_anim", FieldKind.Id, required: true, description: "普攻动作剪辑引用"),
                new FieldSchema("cast_anim_override", FieldKind.Object, required: false, description: "按技能 id 覆盖施法动作剪辑：{skillId: animClipId}")
                    .WithMap(MapSchema.FreeKeyed(
                        "键为技能 id，本模块不静态耦合 skill.def 表结构（同类型注释判断记录）；值为动作剪辑引用，由引擎适配层解析，均只做格式校验",
                        new FieldSchema("<anim_clip_id>", FieldKind.Id, required: true, description: "覆盖的施法动作剪辑引用"))),
                new FieldSchema("swing_vfx", FieldKind.Id, required: false, description: "挥舞轨迹特效，指向 vfx.def"),
                new FieldSchema("impact_vfx_override", FieldKind.Object, required: false, description: "按技能 id 覆盖命中特效：{skillId: vfxId}")
                    .WithMap(MapSchema.FreeKeyed(
                        "键为技能 id，本模块不静态耦合 skill.def 表结构（同类型注释判断记录）；值为 vfx.def 引用，同 swing_vfx 字段不做引用完整性检查，只做格式校验",
                        new FieldSchema("<vfx_id>", FieldKind.Id, required: true, description: "覆盖的命中特效引用，指向 vfx.def，不做引用完整性检查"))),
            },
            migrations: Array.Empty<TableMigration>()).WithOwnership(SchemaLayer.Presentation, "display");

        public static IReadOnlyList<TableSchema> All { get; } = new[] { Vfx, Sfx, WeaponStyle };
    }
}
