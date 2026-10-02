using System;
using System.Collections.Generic;
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
    public readonly struct TerrainRegion
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
    /// （<see cref="TerrainRegion"/> 数组，按声明顺序，<b>后声明的盖住先声明的</b>）。没有声明 <c>terrain</c> 的地图、区域之外的点：
    /// 地面 0、没有天花板——与没有地形能力时一致。
    /// <para>
    /// 判断记录：区域只用轴对齐矩形加线性斜面，足以表达平台、台阶、坡道、低天花板，且查询是 O(区域数) 的纯函数、没有浮点累加，
    /// 确定性成立；更复杂的地形（任意多边形、高度图）由引擎适配层用物理射线实现同一接口（<c>UnityTerrainHeight2D</c>）。
    /// </para>
    /// </summary>
    public sealed class MapTerrainHeights : ITerrainHeight2D
    {
        private readonly Dictionary<Id, TerrainRegion[]> _byMap = new Dictionary<Id, TerrainRegion[]>();

        public MapTerrainHeights()
        {
        }

        /// <summary>读取登记表里全部 <c>world.map</c> 行的 <c>terrain</c> 字段（没有该字段的行跳过）。</summary>
        public MapTerrainHeights(IDataRegistryView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            foreach (var record in view.GetAll(WorldMapSchema.Table.Name))
            {
                var regions = RegionsFromRecord(record);
                if (regions != null)
                {
                    _byMap[new Id(record.Key)] = regions;
                }
            }
        }

        /// <summary>程序化声明一张地图的地形区域（测试与不走数据的宿主用）。</summary>
        public void SetRegions(Id mapId, IReadOnlyList<TerrainRegion> regions)
        {
            if (regions == null) throw new ArgumentNullException(nameof(regions));
            var copy = new TerrainRegion[regions.Count];
            for (var i = 0; i < copy.Length; i++) copy[i] = regions[i];
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

        /// <summary>解析 <c>world.map</c> 记录的 <c>terrain</c> 字段；没有该字段返回 null。</summary>
        public static TerrainRegion[]? RegionsFromRecord(DataRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (!record.TryGetArray("terrain", out var items)) return null;

            var list = new List<TerrainRegion>(items.Count);
            for (var i = 0; i < items.Count; i++)
            {
                var path = "terrain[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
                if (!(items[i] is JsonObject o))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, path, "期望 Object");
                }

                var min = ReadVec(record, o, "min", path);
                var max = ReadVec(record, o, "max", path);
                var ground = ReadNumber(record, o, "ground", path, 0.0);
                var ceiling = ReadNumber(record, o, "ceiling", path, double.PositiveInfinity);
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

                list.Add(new TerrainRegion(min, max, ground, slopeX, slopeY, ceiling));
            }

            return list.ToArray();
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
