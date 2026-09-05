using System;
using System.Collections.Generic;
using Core.Foundation.DataRegistry;

namespace Presentation.Camera.Schema
{
    /// <summary>
    /// <c>camera_profile</c> 表的 <see cref="TableSchema"/> 登记（见 01 L5 模块表 <c>camera</c> 行
    /// "主要数据表：camera_profile"、09_表现层.md 第 3.5、8 节、<c>../schema/README.md</c> 字段表；
    /// 与 <c>Core.Foundation.DisplayInfo.DisplaySchemas</c>/<c>Presentation.VfxSfx.Schema.VfxSfxSchemas</c>
    /// 同一惯例：只登记 schema 供 <see cref="IDataRegistry.RegisterSchema"/> 使用，不接入
    /// <c>data/_sample/</c> 真实数据集）。强类型 C# 结构与 <see cref="DataRegistry.DataRecord"/> 解析器见
    /// <c>../contracts/CameraProfile.cs</c>（<c>CameraProfile.FromRecord</c>）。
    /// </summary>
    public static class CameraSchemas
    {
        /// <summary><c>camera_profile</c>（见 <c>schema/README.md</c> 字段表）。</summary>
        public static readonly TableSchema Profile = new TableSchema(
            name: "camera_profile",
            primaryKey: "id",
            currentSchemaVersion: 1,
            fields: new[]
            {
                new FieldSchema("id", FieldKind.Id, required: true, description: "镜头档位 id"),
                new FieldSchema("pitch_degrees", FieldKind.Number, required: true, description: "固定俯角，经 ICamera.Configure 设定"),
                new FieldSchema("yaw_degrees", FieldKind.Number, required: true, description: "固定水平朝向，经 ICamera.Configure 设定"),
                new FieldSchema("zoom_min", FieldKind.Number, required: true, description: "允许缩放范围下限"),
                new FieldSchema("zoom_max", FieldKind.Number, required: true, description: "允许缩放范围上限"),
                new FieldSchema("zoom_default", FieldKind.Number, required: true, description: "Configure 时应用的初始缩放，须落在 [zoom_min, zoom_max]"),
                new FieldSchema("follow_lerp", FieldKind.Number, required: true, description: "跟随平滑系数，传给 ICamera.Follow(planePos, smoothing)"),
                new FieldSchema("bounds", FieldKind.Object, required: false, description: "跟随边界 {min: {x,y}, max: {x,y}}"),
                new FieldSchema("shake_presets", FieldKind.Array, required: false, description: "震屏档位清单 List<{id, amplitude, duration, frequency}>"),
            },
            migrations: Array.Empty<TableMigration>());

        public static IReadOnlyList<TableSchema> All { get; } = new[] { Profile };
    }
}
