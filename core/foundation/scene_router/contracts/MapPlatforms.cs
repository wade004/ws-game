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
    /// 移动平台的运动声明（<c>world.map.platforms[].motion</c>）：平台沿 <see cref="Offset"/>（平面）与 <see cref="Lift"/>（高度）来回运动，
    /// 在两端各停 <see cref="Pause"/> 秒，单程 <see cref="Travel"/> 秒，匀速。位姿是平台时钟的<b>解析函数</b>（不累加位移），
    /// 所以帧长不均匀时没有积分漂移，存档只需记平台时钟。
    /// </summary>
    public readonly struct PlatformMotionDef
    {
        /// <summary>终点相对起点的平面偏移（世界单位）。</summary>
        public Vec2 Offset { get; }

        /// <summary>终点相对起点的高度变化（世界单位；电梯用，水平平台为 0）。</summary>
        public double Lift { get; }

        /// <summary>单程时长（秒，必须为正有限数）。</summary>
        public double Travel { get; }

        /// <summary>在两端各停留的时长（秒，非负）。</summary>
        public double Pause { get; }

        /// <summary>平台时钟的相位偏移（秒）：时刻 t 的位姿按 <c>t + Phase</c> 计算，让同一关里几块平台错开。</summary>
        public double Phase { get; }

        public PlatformMotionDef(Vec2 offset, double lift, double travel, double pause = 0.0, double phase = 0.0)
        {
            if (!(travel > 0.0) || double.IsInfinity(travel)) throw new ArgumentOutOfRangeException(nameof(travel), travel, "单程时长必须为正的有限数");
            if (!(pause >= 0.0) || double.IsInfinity(pause)) throw new ArgumentOutOfRangeException(nameof(pause), pause, "停留时长必须为非负有限数");
            if (double.IsNaN(phase) || double.IsInfinity(phase)) throw new ArgumentOutOfRangeException(nameof(phase), phase, "相位必须为有限数");
            Offset = offset;
            Lift = lift;
            Travel = travel;
            Pause = pause;
            Phase = phase;
        }

        /// <summary>平台时钟 <paramref name="time"/> 时的运动进度，0 = 起点，1 = 终点（一个周期 <c>2·(Travel+Pause)</c>：去程、终点停留、回程、起点停留）。</summary>
        public double Fraction(double time)
        {
            var cycle = 2.0 * (Travel + Pause);
            var u = (time + Phase) % cycle;
            if (u < 0.0) u += cycle;
            if (u < Travel) return u / Travel;
            if (u < Travel + Pause) return 1.0;
            if (u < 2.0 * Travel + Pause) return 1.0 - (u - Travel - Pause) / Travel;
            return 0.0;
        }
    }

    /// <summary>一块平台的声明：轴对齐矩形范围、起点处的顶面高度、可选运动。</summary>
    public readonly struct PlatformDef
    {
        public string Id { get; }

        public Vec2 Min { get; }

        public Vec2 Max { get; }

        /// <summary>顶面的绝对高度（世界单位，运动起点处）。</summary>
        public double Height { get; }

        public PlatformMotionDef? Motion { get; }

        public PlatformDef(string id, Vec2 min, Vec2 max, double height, PlatformMotionDef? motion = null)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("平台 id 不能为空", nameof(id));
            if (max.X < min.X || max.Y < min.Y) throw new ArgumentException("平台的 max 必须不小于 min");
            Id = id;
            Min = min;
            Max = max;
            Height = height;
            Motion = motion;
        }
    }

    /// <summary>
    /// 数据驱动的单向平台与移动平台（<see cref="ITerrainPlatforms2D"/> 的无头/数据实现，ADR-0170）：读 <c>world.map</c> 的可选字段 <c>platforms</c>
    /// （<see cref="PlatformDef"/> 数组：<c>{id, min, max, height, motion?}</c>）。没有声明该字段的地图没有平台。
    /// <para>
    /// 判断记录（单向）：平台只在"脚下起点不低于顶面、终点不高于顶面"的下落步里承接单位；下方的单位、上升中的单位、与平台齐平以下的横向走动全部不受影响
    /// （要挡路用 <c>terrain</c> 的高台，不是平台）。判断记录（确定性）：位姿是 <c>平台时钟</c> 的纯函数，平台时钟按步累加 <c>dt</c>；
    /// 查询按声明顺序遍历、没有哈希遍历。判断记录（范围）：平台范围是矩形，运动时整块平移；点包含判定含边界。
    /// </para>
    /// </summary>
    public sealed class MapPlatforms : ITerrainPlatforms2D
    {
        public const string FieldName = "platforms";

        /// <summary>"顶面与脚下齐平"的浮点容差（不是口味配置）。</summary>
        public const double SupportEpsilon = 1e-6;

        private sealed class Platform
        {
            public Id MapId;
            public PlatformDef Def;
            public Vec2 Offset;
            public double Lift;
            public Vec2 PrevOffset;
            public double PrevLift;

            public double Top => Def.Height + Lift;

            public double PrevTop => Def.Height + PrevLift;

            public bool Contains(Vec2 p) =>
                p.X >= Def.Min.X + Offset.X && p.X <= Def.Max.X + Offset.X && p.Y >= Def.Min.Y + Offset.Y && p.Y <= Def.Max.Y + Offset.Y;
        }

        private readonly Dictionary<Id, Platform[]> _byMap = new Dictionary<Id, Platform[]>();
        private readonly List<PlatformMotion> _motions = new List<PlatformMotion>();
        private double _time;

        public MapPlatforms()
        {
        }

        /// <summary>读取登记表里全部 <c>world.map</c> 行的 <c>platforms</c> 字段（没有该字段的行跳过）。</summary>
        public MapPlatforms(IDataRegistryView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            foreach (var record in view.GetAll(WorldMapSchema.Table.Name))
            {
                var defs = FromRecord(record);
                if (defs != null)
                {
                    SetPlatforms(new Id(record.Key), defs);
                }
            }
        }

        /// <summary>程序化声明一张地图的平台（测试与不走数据的宿主用）；同一张地图里 id 必须唯一。</summary>
        public void SetPlatforms(Id mapId, IReadOnlyList<PlatformDef> defs)
        {
            if (defs == null) throw new ArgumentNullException(nameof(defs));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var list = new Platform[defs.Count];
            for (var i = 0; i < list.Length; i++)
            {
                if (!seen.Add(defs[i].Id)) throw new ArgumentException("平台 id 重复：" + defs[i].Id, nameof(defs));
                list[i] = new Platform { MapId = mapId, Def = defs[i] };
            }

            _byMap[mapId] = list;
            ApplyPose(list);
        }

        public bool HasPlatforms(Id mapId) => _byMap.ContainsKey(mapId);

        public double TimeSeconds => _time;

        public IReadOnlyList<PlatformMotion> LastMotions => _motions;

        public void SetTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "平台时钟必须为有限数");
            _time = seconds;
            _motions.Clear();
            foreach (var pair in _byMap)
            {
                ApplyPose(pair.Value);
            }
        }

        public void Advance(double dt)
        {
            _motions.Clear();
            if (!(dt > 0.0))
            {
                return;
            }

            _time += dt;
            // 按地图 id 序数遍历，保证位移记录顺序确定。
            var keys = new List<Id>(_byMap.Keys);
            keys.Sort();
            for (var k = 0; k < keys.Count; k++)
            {
                var platforms = _byMap[keys[k]];
                for (var i = 0; i < platforms.Length; i++)
                {
                    var p = platforms[i];
                    p.PrevOffset = p.Offset;
                    p.PrevLift = p.Lift;
                    Pose(p);
                    var delta = p.Offset - p.PrevOffset;
                    var lift = p.Lift - p.PrevLift;
                    if (delta.X != 0.0 || delta.Y != 0.0 || lift != 0.0)
                    {
                        _motions.Add(new PlatformMotion(p.Def.Id, p.MapId, delta, lift));
                    }
                }
            }
        }

        public bool TryLand(Id mapId, Vec2 point, double footBefore, double footAfter, string? ignorePlatformId, out PlatformContact contact)
        {
            contact = default;
            if (!_byMap.TryGetValue(mapId, out var platforms)) return false;
            var found = false;
            var best = double.NegativeInfinity;
            string? bestId = null;
            for (var i = 0; i < platforms.Length; i++)
            {
                var p = platforms[i];
                if (ignorePlatformId != null && string.Equals(p.Def.Id, ignorePlatformId, StringComparison.Ordinal)) continue;
                if (!p.Contains(point)) continue;
                if (footBefore < p.PrevTop - SupportEpsilon || footAfter > p.Top) continue;
                if (p.Top > best)
                {
                    best = p.Top;
                    bestId = p.Def.Id;
                    found = true;
                }
            }

            if (found) contact = new PlatformContact(bestId!, best);
            return found;
        }

        public bool TryGetSupport(Id mapId, Vec2 point, double foot, out PlatformContact contact)
        {
            contact = default;
            if (!_byMap.TryGetValue(mapId, out var platforms)) return false;
            var found = false;
            var best = double.NegativeInfinity;
            string? bestId = null;
            for (var i = 0; i < platforms.Length; i++)
            {
                var p = platforms[i];
                if (!p.Contains(point)) continue;
                // 齐平的判定同时看现在与上一步的顶面：刚出生/传送到移动平台上、平台在同一步里已经动了一小段时仍认得出支撑。
                if (Math.Abs(foot - p.Top) > SupportEpsilon && Math.Abs(foot - p.PrevTop) > SupportEpsilon) continue;
                if (p.Top > best)
                {
                    best = p.Top;
                    bestId = p.Def.Id;
                    found = true;
                }
            }

            if (found) contact = new PlatformContact(bestId!, best);
            return found;
        }

        public bool TryGetTop(Id mapId, string platformId, Vec2 point, out double height)
        {
            height = 0.0;
            if (!_byMap.TryGetValue(mapId, out var platforms)) return false;
            for (var i = 0; i < platforms.Length; i++)
            {
                var p = platforms[i];
                if (!string.Equals(p.Def.Id, platformId, StringComparison.Ordinal)) continue;
                if (!p.Contains(point)) return false;
                height = p.Top;
                return true;
            }

            return false;
        }

        /// <summary>某块平台此刻的位姿（测试与表现层用）：矩形范围与顶面高度；平台不存在返回 false。</summary>
        public bool TryGetPose(Id mapId, string platformId, out Vec2 min, out Vec2 max, out double top)
        {
            min = Vec2.Zero;
            max = Vec2.Zero;
            top = 0.0;
            if (!_byMap.TryGetValue(mapId, out var platforms)) return false;
            for (var i = 0; i < platforms.Length; i++)
            {
                var p = platforms[i];
                if (!string.Equals(p.Def.Id, platformId, StringComparison.Ordinal)) continue;
                min = p.Def.Min + p.Offset;
                max = p.Def.Max + p.Offset;
                top = p.Top;
                return true;
            }

            return false;
        }

        private void ApplyPose(Platform[] platforms)
        {
            for (var i = 0; i < platforms.Length; i++)
            {
                Pose(platforms[i]);
                platforms[i].PrevOffset = platforms[i].Offset;
                platforms[i].PrevLift = platforms[i].Lift;
            }
        }

        private void Pose(Platform p)
        {
            if (!p.Def.Motion.HasValue)
            {
                p.Offset = Vec2.Zero;
                p.Lift = 0.0;
                return;
            }

            var m = p.Def.Motion.Value;
            var f = m.Fraction(_time);
            p.Offset = new Vec2(m.Offset.X * f, m.Offset.Y * f);
            p.Lift = m.Lift * f;
        }

        // ------------------------------------------------------------------ 数据解析

        /// <summary>解析 <c>world.map</c> 记录的 <c>platforms</c> 字段；没有该字段返回 null。字段非法或 id 重复抛 <see cref="DataFieldException"/>，消息带路径。</summary>
        public static PlatformDef[]? FromRecord(DataRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (!record.TryGetArray(FieldName, out var items)) return null;
            var list = new PlatformDef[items.Count];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < list.Length; i++)
            {
                list[i] = ReadItem(record, items[i], i);
                if (!seen.Add(list[i].Id))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, Path(i) + ".id", "平台 id 重复：" + list[i].Id);
                }
            }

            return list;
        }

        /// <summary>解析 <c>platforms</c> 数组里第 <paramref name="index"/> 个条目（非法抛 <see cref="DataFieldException"/>，供数据校验规则逐条目报告）。</summary>
        public static PlatformDef ReadItem(DataRecord record, JsonValue item, int index)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var path = Path(index);
            if (!(item is JsonObject o))
            {
                throw new DataFieldException(record.Table.Name, record.Key, path, "期望 Object");
            }

            if (!o.TryGetValue("id", out var idValue))
            {
                throw new DataFieldException(record.Table.Name, record.Key, path + ".id", "必填");
            }

            if (!(idValue is JsonString idText) || idText.Value.Length == 0)
            {
                throw new DataFieldException(record.Table.Name, record.Key, path + ".id", "期望非空 String");
            }

            var min = ReadVec(record, o, "min", path);
            var max = ReadVec(record, o, "max", path);
            if (max.X < min.X || max.Y < min.Y)
            {
                throw new DataFieldException(record.Table.Name, record.Key, path, "max 必须不小于 min");
            }

            if (!o.TryGetValue("height", out var heightValue))
            {
                throw new DataFieldException(record.Table.Name, record.Key, path + ".height", "必填");
            }

            var height = heightValue is JsonNumber hn
                ? hn.Value
                : throw new DataFieldException(record.Table.Name, record.Key, path + ".height", "期望 Number");

            PlatformMotionDef? motion = null;
            if (o.TryGetValue("motion", out var motionValue))
            {
                motion = ReadMotion(record, motionValue, path + ".motion");
            }

            return new PlatformDef(idText.Value, min, max, height, motion);
        }

        private static PlatformMotionDef ReadMotion(DataRecord record, JsonValue value, string path)
        {
            if (!(value is JsonObject m))
            {
                throw new DataFieldException(record.Table.Name, record.Key, path, "期望 Object");
            }

            var kind = "ping_pong";
            if (m.TryGetValue("kind", out var kindValue))
            {
                kind = kindValue is JsonString ks
                    ? ks.Value
                    : throw new DataFieldException(record.Table.Name, record.Key, path + ".kind", "期望 String（ping_pong）");
            }

            if (kind != "ping_pong")
            {
                throw new DataFieldException(record.Table.Name, record.Key, path + ".kind", "未知运动 \"" + kind + "\"（ping_pong）");
            }

            var offset = Vec2.Zero;
            if (m.TryGetValue("offset", out var offsetValue))
            {
                offset = ReadVecValue(record, offsetValue, path + ".offset");
            }

            var lift = ReadNumber(record, m, "lift", path, 0.0);
            if (!m.TryGetValue("travel", out var travelValue))
            {
                throw new DataFieldException(record.Table.Name, record.Key, path + ".travel", "必填");
            }

            var travel = travelValue is JsonNumber tn
                ? tn.Value
                : throw new DataFieldException(record.Table.Name, record.Key, path + ".travel", "期望 Number");
            if (!(travel > 0.0) || double.IsInfinity(travel))
            {
                throw new DataFieldException(record.Table.Name, record.Key, path + ".travel", "单程时长必须为正的有限数");
            }

            var pause = ReadNumber(record, m, "pause", path, 0.0);
            if (!(pause >= 0.0) || double.IsInfinity(pause))
            {
                throw new DataFieldException(record.Table.Name, record.Key, path + ".pause", "停留时长必须为非负有限数");
            }

            var phase = ReadNumber(record, m, "phase", path, 0.0);
            if (double.IsNaN(phase) || double.IsInfinity(phase))
            {
                throw new DataFieldException(record.Table.Name, record.Key, path + ".phase", "相位必须为有限数");
            }

            return new PlatformMotionDef(offset, lift, travel, pause, phase);
        }

        private static string Path(int index) => FieldName + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";

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
