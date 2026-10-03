using System;
using System.Collections.Generic;
using System.Globalization;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;

namespace Core.Foundation.SceneRouter
{
    /// <summary>
    /// <c>world.map.terrain</c> 的一块高度区域：轴对齐矩形 <c>[min, max]</c>（含边界），地面高度 =
    /// <see cref="Ground"/> + <see cref="SlopeX"/>·(x − min.x) + <see cref="SlopeY"/>·(y − min.y)，天花板为绝对高度（可选）。
    /// </summary>
    public readonly struct TerrainRegion : ITerrainShape
    {
        public Vec2 Min { get; }

        public Vec2 Max { get; }

        /// <summary>矩形左下角（<see cref="Min"/>）处的地面高度。</summary>
        public double Ground { get; }

        /// <summary>地面高度沿 x 方向每世界单位的增量（斜坡；0 = 平台）。</summary>
        public double SlopeX { get; }

        /// <summary>地面高度沿 y 方向每世界单位的增量。</summary>
        public double SlopeY { get; }

        /// <summary>天花板的绝对高度；<see cref="double.PositiveInfinity"/> = 没有。</summary>
        public double Ceiling { get; }

        public TerrainRegion(Vec2 min, Vec2 max, double ground = 0.0, double slopeX = 0.0, double slopeY = 0.0, double ceiling = double.PositiveInfinity)
        {
            if (max.X < min.X || max.Y < min.Y)
            {
                throw new ArgumentException("terrain 区域的 max 必须不小于 min");
            }

            Min = min;
            Max = max;
            Ground = ground;
            SlopeX = slopeX;
            SlopeY = slopeY;
            Ceiling = ceiling;
        }

        public bool Contains(Vec2 p) => p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y;

        public double GroundAt(Vec2 p) => Ground + SlopeX * (p.X - Min.X) + SlopeY * (p.Y - Min.Y);
    }

    /// <summary>
    /// 数据驱动的地形高度（<see cref="ITerrainHeight2D"/> 的无头/数据实现）：读 <c>world.map</c> 的可选字段 <c>terrain</c>
    /// （<see cref="ITerrainShape"/> 数组，按声明顺序，<b>后声明的盖住先声明的</b>）。没有声明 <c>terrain</c> 的地图、区域之外的点：
    /// 地面 0、没有天花板——与没有地形能力时一致。
    /// <para>
    /// 形状（同表声明，条目的 <c>shape</c> 字段选择，缺省 <c>rect</c>）：<c>rect</c> 轴对齐矩形加线性斜面（<see cref="TerrainRegion"/>，既有）、
    /// <c>polygon</c> 任意凸多边形的平面/斜面（<see cref="TerrainPolygon"/>）、<c>heightfield</c> 规则网格高度场 + 双线性插值
    /// （<see cref="TerrainHeightField"/>）。判断记录：三种形状的查询都是 O(区域数 × 顶点数/常数) 的纯函数、没有浮点累加，确定性成立；
    /// 凹区域用多个凸多边形拼（后声明盖住先声明）；更不规则的地形（网格模型、任意碰撞体）由引擎适配层用物理射线实现同一接口
    /// （<c>UnityTerrainHeight2D</c>）。
    /// </para>
    /// </summary>
    public sealed class MapTerrainHeights : ITerrainHeight2D
    {
        private readonly Dictionary<Id, ITerrainShape[]> _byMap = new Dictionary<Id, ITerrainShape[]>();

        public MapTerrainHeights()
        {
        }

        /// <summary>读取登记表里全部 <c>world.map</c> 行的 <c>terrain</c> 字段（没有该字段的行跳过）。</summary>
        public MapTerrainHeights(IDataRegistryView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            foreach (var record in view.GetAll(WorldMapSchema.Table.Name))
            {
                var shapes = ShapesFromRecord(record);
                if (shapes != null)
                {
                    _byMap[new Id(record.Key)] = shapes;
                }
            }
        }

        /// <summary>程序化声明一张地图的矩形地形区域（测试与不走数据的宿主用）。</summary>
        public void SetRegions(Id mapId, IReadOnlyList<TerrainRegion> regions)
        {
            if (regions == null) throw new ArgumentNullException(nameof(regions));
            var copy = new ITerrainShape[regions.Count];
            for (var i = 0; i < copy.Length; i++) copy[i] = regions[i];
            _byMap[mapId] = copy;
        }

        /// <summary>程序化声明一张地图的地形形状（矩形、凸多边形、高度场可混用；后声明的盖住先声明的）。</summary>
        public void SetShapes(Id mapId, IReadOnlyList<ITerrainShape> shapes)
        {
            if (shapes == null) throw new ArgumentNullException(nameof(shapes));
            var copy = new ITerrainShape[shapes.Count];
            for (var i = 0; i < copy.Length; i++) copy[i] = shapes[i] ?? throw new ArgumentNullException(nameof(shapes), "形状列表里有空元素");
            _byMap[mapId] = copy;
        }

        /// <summary>该地图是否声明了地形区域。</summary>
        public bool HasTerrain(Id mapId) => _byMap.ContainsKey(mapId);

        public double GetGroundHeight(Id mapId, Vec2 point)
        {
            if (!_byMap.TryGetValue(mapId, out var regions)) return 0.0;
            for (var i = regions.Length - 1; i >= 0; i--)
            {
                if (regions[i].Contains(point)) return regions[i].GroundAt(point);
            }

            return 0.0;
        }

        public double GetCeilingHeight(Id mapId, Vec2 point)
        {
            if (!_byMap.TryGetValue(mapId, out var regions)) return double.PositiveInfinity;
            for (var i = regions.Length - 1; i >= 0; i--)
            {
                if (regions[i].Contains(point)) return regions[i].Ceiling;
            }

            return double.PositiveInfinity;
        }

        /// <summary>
        /// 解析 <c>world.map</c> 记录的 <c>terrain</c> 字段里的<b>矩形</b>区域；没有该字段返回 null。旧入口（ABI 保留）：
        /// 记录里出现 <c>polygon</c>/<c>heightfield</c> 条目时抛 <see cref="DataFieldException"/>（它不能用矩形表达，不静默丢弃），
        /// 要读全部形状用 <see cref="ShapesFromRecord"/>。
        /// </summary>
        public static TerrainRegion[]? RegionsFromRecord(DataRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var shapes = ShapesFromRecord(record);
            if (shapes == null) return null;
            var list = new TerrainRegion[shapes.Length];
            for (var i = 0; i < shapes.Length; i++)
            {
                if (!(shapes[i] is TerrainRegion region))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "terrain[" + i.ToString(CultureInfo.InvariantCulture) + "].shape",
                        "不是矩形区域，RegionsFromRecord 只读矩形；请改用 ShapesFromRecord");
                }

                list[i] = region;
            }

            return list;
        }

        /// <summary>
        /// 解析 <c>world.map</c> 记录的 <c>terrain</c> 字段（矩形、凸多边形、高度场，按声明顺序）；没有该字段返回 null。
        /// 字段缺失、类型不符、形状非法（多边形非凸/面积为零、高度场行长不齐/cell 非正等）抛 <see cref="DataFieldException"/>，
        /// 消息里带路径（<c>terrain[i].…</c>）。
        /// </summary>
        public static ITerrainShape[]? ShapesFromRecord(DataRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (!record.TryGetArray("terrain", out var items)) return null;

            var list = new List<ITerrainShape>(items.Count);
            for (var i = 0; i < items.Count; i++)
            {
                list.Add(ReadItem(record, items[i], i));
            }

            return list.ToArray();
        }

        /// <summary>
        /// 解析 <c>terrain</c> 数组里第 <paramref name="index"/> 个条目（形状非法时抛 <see cref="DataFieldException"/>）。
        /// 供数据校验规则逐条目报告问题（一个条目坏了不挡住其余条目的检查）。
        /// </summary>
        public static ITerrainShape ReadItem(DataRecord record, JsonValue item, int index)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var path = "terrain[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            if (!(item is JsonObject o))
            {
                throw new DataFieldException(record.Table.Name, record.Key, path, "期望 Object");
            }

            var shape = "rect";
            if (o.TryGetValue("shape", out var shapeValue))
            {
                shape = shapeValue is JsonString shapeText
                    ? shapeText.Value
                    : throw new DataFieldException(record.Table.Name, record.Key, path + ".shape", "期望 String（rect|polygon|heightfield）");
            }

            var ceiling = ReadNumber(record, o, "ceiling", path, double.PositiveInfinity);
            switch (shape)
            {
                case "rect":
                    return ReadRect(record, o, path, ceiling);
                case "polygon":
                    return ReadPolygon(record, o, path, ceiling);
                case "heightfield":
                    return ReadHeightField(record, o, path, ceiling);
                default:
                    throw new DataFieldException(record.Table.Name, record.Key, path + ".shape", "未知形状 \"" + shape + "\"（rect|polygon|heightfield）");
            }
        }

        private static TerrainRegion ReadRect(DataRecord record, JsonObject o, string path, double ceiling)
        {
            var min = ReadVec(record, o, "min", path);
            var max = ReadVec(record, o, "max", path);
            var ground = ReadNumber(record, o, "ground", path, 0.0);
            var slopeX = 0.0;
            var slopeY = 0.0;
            if (o.TryGetValue("slope", out var slopeValue))
            {
                var slope = ReadVecValue(record, slopeValue, path + ".slope");
                slopeX = slope.X;
                slopeY = slope.Y;
            }

            if (max.X < min.X || max.Y < min.Y)
            {
                throw new DataFieldException(record.Table.Name, record.Key, path, "max 必须不小于 min");
            }

            return new TerrainRegion(min, max, ground, slopeX, slopeY, ceiling);
        }

        private static TerrainPolygon ReadPolygon(DataRecord record, JsonObject o, string path, double ceiling)
        {
            if (!o.TryGetValue("points", out var pointsValue))
            {
                throw new DataFieldException(record.Table.Name, record.Key, path + ".points", "必填");
            }

            if (!(pointsValue is JsonArray pointArray))
            {
                throw new DataFieldException(record.Table.Name, record.Key, path + ".points", "期望 Array");
            }

            var vertices = new Vec2[pointArray.Count];
            for (var k = 0; k < vertices.Length; k++)
            {
                vertices[k] = ReadVecValue(record, pointArray[k], path + ".points[" + k.ToString(CultureInfo.InvariantCulture) + "]");
            }

            var ground = ReadNumber(record, o, "ground", path, 0.0);
            var slopeX = 0.0;
            var slopeY = 0.0;
            if (o.TryGetValue("slope", out var slopeValue))
            {
                var slope = ReadVecValue(record, slopeValue, path + ".slope");
                slopeX = slope.X;
                slopeY = slope.Y;
            }

            Vec2? origin = null;
            if (o.TryGetValue("origin", out var originValue))
            {
                origin = ReadVecValue(record, originValue, path + ".origin");
            }

            try
            {
                return new TerrainPolygon(vertices, ground, slopeX, slopeY, ceiling, origin);
            }
            catch (ArgumentException ex)
            {
                throw new DataFieldException(record.Table.Name, record.Key, path + ".points", ex.Message);
            }
        }

        private static TerrainHeightField ReadHeightField(DataRecord record, JsonObject o, string path, double ceiling)
        {
            var min = ReadVec(record, o, "min", path);
            if (!o.TryGetValue("cell", out var cellValue))
            {
                throw new DataFieldException(record.Table.Name, record.Key, path + ".cell", "必填");
            }

            var cell = cellValue is JsonNumber cellNumber
                ? cellNumber.Value
                : throw new DataFieldException(record.Table.Name, record.Key, path + ".cell", "期望 Number");
            if (!o.TryGetValue("heights", out var heightsValue))
            {
                throw new DataFieldException(record.Table.Name, record.Key, path + ".heights", "必填");
            }

            if (!(heightsValue is JsonArray rows))
            {
                throw new DataFieldException(record.Table.Name, record.Key, path + ".heights", "期望 Array（行数组，每行是结点高度数组）");
            }

            var heights = new double[rows.Count][];
            for (var j = 0; j < rows.Count; j++)
            {
                var rowPath = path + ".heights[" + j.ToString(CultureInfo.InvariantCulture) + "]";
                if (!(rows[j] is JsonArray row))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, rowPath, "期望 Array");
                }

                heights[j] = new double[row.Count];
                for (var i = 0; i < row.Count; i++)
                {
                    heights[j][i] = row[i] is JsonNumber n
                        ? n.Value
                        : throw new DataFieldException(record.Table.Name, record.Key, rowPath + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", "期望 Number");
                }
            }

            try
            {
                return new TerrainHeightField(min, cell, heights, ceiling);
            }
            catch (ArgumentException ex)
            {
                throw new DataFieldException(record.Table.Name, record.Key, path + ".heights", ex.Message);
            }
        }

        private static Vec2 ReadVec(DataRecord record, JsonObject o, string name, string path)
        {
            if (!o.TryGetValue(name, out var value))
            {
                throw new DataFieldException(record.Table.Name, record.Key, path + "." + name, "必填");
            }

            return ReadVecValue(record, value, path + "." + name);
        }

        private static Vec2 ReadVecValue(DataRecord record, JsonValue value, string path)
        {
            if (value is JsonObject v && v.TryGetValue("x", out var xv) && xv is JsonNumber xn
                && v.TryGetValue("y", out var yv) && yv is JsonNumber yn)
            {
                return new Vec2(xn.Value, yn.Value);
            }

            throw new DataFieldException(record.Table.Name, record.Key, path, "期望 Vec2（{\"x\": Number, \"y\": Number}）");
        }

        private static double ReadNumber(DataRecord record, JsonObject o, string name, string path, double fallback)
        {
            if (!o.TryGetValue(name, out var value)) return fallback;
            if (value is JsonNumber n) return n.Value;
            throw new DataFieldException(record.Table.Name, record.Key, path + "." + name, "期望 Number");
        }
    }
}
