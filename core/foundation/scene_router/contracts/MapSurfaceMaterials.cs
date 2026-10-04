using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.SceneRouter
{
    /// <summary>
    /// 数据驱动的地面材质查询（手感设计/07 第 3 节"脚步材质由地图区域标签决定"，ADR-0148）：读 <c>world.map</c> 的可选字段
    /// <c>surface_materials</c>——条目是 <c>terrain</c> 同一套形状（<c>rect</c> 缺省 / <c>polygon</c> / <c>heightfield</c>，几何解析复用
    /// <see cref="MapTerrainHeights.ReadItem"/>）加一个 <c>material</c> 文本标签；按声明顺序，<b>后声明的盖住先声明的</b>。
    /// 没有声明该字段的地图、区域之外的点返回 null（调用方按通用材质处理）。纯函数、无状态累加，确定性成立；
    /// 表现层的脚步音效按它取材质、选 <c>sfx.def</c> 的 <c>feel_material</c> 行。
    /// </summary>
    public sealed class MapSurfaceMaterials
    {
        private readonly struct Entry
        {
            public readonly ITerrainShape Shape;
            public readonly string Material;

            public Entry(ITerrainShape shape, string material)
            {
                Shape = shape;
                Material = material;
            }
        }

        private readonly Dictionary<Id, Entry[]> _byMap = new Dictionary<Id, Entry[]>();

        public MapSurfaceMaterials()
        {
        }

        /// <summary>读取登记表里全部 <c>world.map</c> 行的 <c>surface_materials</c> 字段（没有该字段的行跳过）。</summary>
        public MapSurfaceMaterials(IDataRegistryView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            foreach (var record in view.GetAll(WorldMapSchema.Table.Name))
            {
                var entries = FromRecord(record);
                if (entries != null)
                {
                    _byMap[new Id(record.Key)] = entries;
                }
            }
        }

        /// <summary>程序化声明一张地图的材质区域（测试与不走数据的宿主用；后声明的盖住先声明的）。</summary>
        public void SetRegions(Id mapId, IReadOnlyList<(ITerrainShape Shape, string Material)> regions)
        {
            if (regions == null) throw new ArgumentNullException(nameof(regions));
            var copy = new Entry[regions.Count];
            for (var i = 0; i < copy.Length; i++)
            {
                if (regions[i].Shape == null) throw new ArgumentNullException(nameof(regions), "区域列表里有空形状");
                if (string.IsNullOrEmpty(regions[i].Material)) throw new ArgumentException("材质标签不能为空", nameof(regions));
                copy[i] = new Entry(regions[i].Shape, regions[i].Material);
            }
            _byMap[mapId] = copy;
        }

        /// <summary>该地图是否声明了材质区域。</summary>
        public bool HasRegions(Id mapId) => _byMap.ContainsKey(mapId);

        /// <summary>点所在区域的材质标签（后声明的盖住先声明的）；没有声明或不在任何区域内返回 null。</summary>
        public string? GetMaterial(Id mapId, Vec2 point)
        {
            if (!_byMap.TryGetValue(mapId, out var entries)) return null;
            for (var i = entries.Length - 1; i >= 0; i--)
            {
                if (entries[i].Shape.Contains(point)) return entries[i].Material;
            }
            return null;
        }

        /// <summary>解析一条 <c>world.map</c> 记录的 <c>surface_materials</c>；没有该字段返回 null。形状非法或缺 <c>material</c> 抛 <see cref="DataFieldException"/>。</summary>
        private static Entry[]? FromRecord(DataRecord record)
        {
            if (!record.TryGetArray(FieldName, out var items)) return null;
            var list = new Entry[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                list[i] = ReadEntry(record, items[i], i);
            }
            return list;
        }

        public const string FieldName = "surface_materials";

        /// <summary>解析 <c>surface_materials</c> 的第 <paramref name="index"/> 个条目（供数据校验逐条目报告）。</summary>
        public static (ITerrainShape Shape, string Material) ReadItem(DataRecord record, JsonValue item, int index)
        {
            var entry = ReadEntry(record, item, index);
            return (entry.Shape, entry.Material);
        }

        private static Entry ReadEntry(DataRecord record, JsonValue item, int index)
        {
            var path = FieldName + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            if (!(item is JsonObject o))
            {
                throw new DataFieldException(record.Table.Name, record.Key, path, "期望 Object");
            }
            if (!o.TryGetValue("material", out var materialValue))
            {
                throw new DataFieldException(record.Table.Name, record.Key, path + ".material", "必填");
            }
            if (!(materialValue is JsonString materialText) || materialText.Value.Length == 0)
            {
                throw new DataFieldException(record.Table.Name, record.Key, path + ".material", "期望非空 String");
            }

            ITerrainShape shape;
            try
            {
                shape = MapTerrainHeights.ReadItem(record, item, index);
            }
            catch (DataFieldException ex)
            {
                // 把几何错误的路径从 terrain[i] 改写为 surface_materials[i]，避免误导。
                var marker = ex.Message.IndexOf("\"：", StringComparison.Ordinal);
                var inner = marker >= 0 ? ex.Message.Substring(marker + 2) : ex.Message;
                throw new DataFieldException(record.Table.Name, record.Key, ex.Field.Replace("terrain[", FieldName + "["), inner);
            }
            return new Entry(shape, materialText.Value);
        }
    }
}
