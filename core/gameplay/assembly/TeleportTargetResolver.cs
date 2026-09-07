using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Gameplay.Assembly
{
    /// <summary>
    /// <c>teleport_target_ref</c>（见 07_载体层_物品生物物件.md <c>teleporter</c> 类型数据
    /// <c>{teleport_target_ref}</c>、<c>Core.Carriers.Gobj.GobjOptions.TeleportResolver</c>）解析器
    /// （缺口 15）。
    /// <para>
    /// 判断记录（解析规则，07 未写明 <c>teleport_target_ref</c> 的具体编码格式，本类按设计层拍板
    /// 补一条勘误说明该规则，见 07 变更记录）：<paramref name="teleportTargetRef"/> 按 <c>'.'</c>
    /// 切分成若干段：
    /// <list type="bullet">
    /// <item>恰为两段、且整串等于某条 <c>world.map</c> 行 <c>id</c>（如 <c>"world.town_square"</c>）
    /// → 目标 = 该地图 <c>spawn_points[0]</c> 的 <c>position</c>。</item>
    /// <item>恰为三段、前两段拼接后等于某条 <c>world.map</c> 行 <c>id</c>（如 <c>"world.town_square"</c>）
    /// → 第三段（如 <c>"fountain"</c>）与该地图 <c>teleport_points[]</c>（先查）/
    /// <c>spawn_points[]</c>（再查）逐条比较——判断记录：05 §4.1 示例数据里
    /// <c>spawn_points</c>/<c>teleport_points</c> 各条目的 <c>id</c> 字段有自己独立的领域前缀
    /// （如 <c>"spawn.town_square.default"</c>/<c>"tp.town_square.fountain"</c>），不与地图 id 共享
    /// <c>"world.&lt;地图名&gt;"</c> 前缀，因此第三段只能与点位 <c>id</c> 的**最后一段**比较（如
    /// <c>"fountain"</c> 对 <c>"tp.town_square.fountain"</c>），不能整串比较 → 目标 = 命中点位的
    /// <c>position</c>。</item>
    /// <item>其余情况（非 2/3 段、地图不存在、三段式引用在两个点位列表里都找不到匹配）解析失败，
    /// 返回 null；调用方（<c>GameObjectHost.DoTeleport</c>）已经在解析失败时记一条诊断，本类
    /// 额外接受一个可选的 <c>onFailure</c> 回调记更具体的失败原因，不重复兜底职责，未注入时
    /// 静默。</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class TeleportTargetResolver
    {
        private const string WorldMapTable = "world.map";
        private const string SpawnPointsField = "spawn_points";
        private const string TeleportPointsField = "teleport_points";

        private readonly IDataRegistryView _registry;
        private readonly Action<string>? _onFailure;

        public TeleportTargetResolver(IDataRegistryView registry, Action<string>? onFailure = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _onFailure = onFailure;
        }

        /// <summary>解析入口，签名与 <c>Core.Carriers.Gobj.TeleportResolverDelegate</c> 一致，供组装
        /// 根直接把 <see cref="Resolve"/> 接给 <c>GobjOptions.TeleportResolver</c>。</summary>
        public (Id MapId, Vec2 Position)? Resolve(Id teleportTargetRef)
        {
            // Id 构造期已保证至少含一个 '.'（见 Core.Foundation.Common.Id 格式校验），firstDot 恒 >= 0。
            var value = teleportTargetRef.Value;
            var firstDot = value.IndexOf('.');
            var secondDot = value.IndexOf('.', firstDot + 1);

            if (secondDot < 0)
            {
                // 只有一个 '.'：恰好两段，整串本身就是候选的 world.map 行 id。
                return ResolveMapDefaultSpawn(teleportTargetRef);
            }

            var thirdDot = value.IndexOf('.', secondDot + 1);
            if (thirdDot < 0)
            {
                // 恰好两个 '.'：三段，前两段拼成候选地图 id，第三段是点位名。
                var mapId = new Id(value.Substring(0, secondDot));
                var pointName = value.Substring(secondDot + 1);
                return ResolveNamedPoint(mapId, pointName, teleportTargetRef);
            }

            Fail($"teleport_target_ref \"{teleportTargetRef}\" 段数非法（只支持两段的地图 id，" +
                 "或三段的\"地图 id + 点位名\"）");
            return null;
        }

        /// <summary>N14 新增：调用方（<c>GameplayAssembly.TeleportUnit</c>）已经拿到拆分好的目标地图
        /// id 与（可选）具体点位 id（如 <c>AreaTriggerParams.SpawnPoint</c>），不需要 <see
        /// cref="Resolve"/> 那套"按 '.' 切分猜测段数"的编码规则——本方法按精确 id 匹配
        /// <paramref name="pointId"/>（不是 <see cref="Resolve"/> 三段式那种"只比较点位 id 最后一段"
        /// 的模糊匹配，调用方已经有完整点位 id，没有理由退化成模糊匹配）。<paramref name="pointId"/>
        /// 为空时等价于 <see cref="ResolveMapDefaultSpawn"/>（"未指定点位则用该地图默认出生点"，同
        /// <c>AreaTriggerParams.SpawnPoint</c> 判断记录）。</summary>
        public (Id MapId, Vec2 Position)? ResolveExplicit(Id mapId, Id? pointId)
        {
            if (!pointId.HasValue)
            {
                return ResolveMapDefaultSpawn(mapId);
            }

            var record = _registry.Get(WorldMapTable, mapId);
            if (record == null)
            {
                Fail($"传送目标地图 \"{mapId}\" 不是任何 world.map 行 id");
                return null;
            }

            if (record.TryGetArray(TeleportPointsField, out var teleportPoints)
                && TryFindById(teleportPoints, pointId.Value, out var teleportPosition))
            {
                return (mapId, teleportPosition);
            }

            if (record.TryGetArray(SpawnPointsField, out var spawnPoints)
                && TryFindById(spawnPoints, pointId.Value, out var spawnPosition))
            {
                return (mapId, spawnPosition);
            }

            Fail($"传送点位 \"{pointId}\" 在 world.map \"{mapId}\" 的 teleport_points/spawn_points 里都找不到" +
                 "（按点位 id 精确匹配）");
            return null;
        }

        private static bool TryFindById(JsonArray points, Id pointId, out Vec2 position)
        {
            for (var i = 0; i < points.Count; i++)
            {
                if (points[i] is JsonObject obj
                    && obj.TryGetValue("id", out var idVal) && idVal is JsonString idStr
                    && Id.TryParse(idStr.Value, out var candidateId)
                    && candidateId.Equals(pointId)
                    && TryReadPosition(obj, out position))
                {
                    return true;
                }
            }

            position = Vec2.Zero;
            return false;
        }

        private (Id, Vec2)? ResolveMapDefaultSpawn(Id mapId)
        {
            var record = _registry.Get(WorldMapTable, mapId);
            if (record == null)
            {
                Fail($"teleport_target_ref \"{mapId}\" 不是任何 world.map 行 id");
                return null;
            }

            if (!record.TryGetArray(SpawnPointsField, out var spawnPoints) || spawnPoints.Count == 0
                || !TryReadPosition(spawnPoints[0], out var position))
            {
                Fail($"world.map \"{mapId}\" 缺少合法的 spawn_points[0]，teleport_target_ref \"{mapId}\" 解析失败");
                return null;
            }

            return (mapId, position);
        }

        private (Id, Vec2)? ResolveNamedPoint(Id mapId, string pointName, Id fullRef)
        {
            var record = _registry.Get(WorldMapTable, mapId);
            if (record == null)
            {
                Fail($"teleport_target_ref \"{fullRef}\" 前两段 \"{mapId}\" 不是任何 world.map 行 id");
                return null;
            }

            if (record.TryGetArray(TeleportPointsField, out var teleportPoints)
                && TryFindByLastSegment(teleportPoints, pointName, out var teleportPosition))
            {
                return (mapId, teleportPosition);
            }

            if (record.TryGetArray(SpawnPointsField, out var spawnPoints)
                && TryFindByLastSegment(spawnPoints, pointName, out var spawnPosition))
            {
                return (mapId, spawnPosition);
            }

            Fail($"teleport_target_ref \"{fullRef}\" 在 world.map \"{mapId}\" 的 teleport_points/spawn_points " +
                 $"里都找不到点位名 \"{pointName}\"（按点位 id 最后一段匹配）");
            return null;
        }

        private static bool TryFindByLastSegment(JsonArray points, string pointName, out Vec2 position)
        {
            for (var i = 0; i < points.Count; i++)
            {
                if (points[i] is JsonObject obj
                    && obj.TryGetValue("id", out var idVal) && idVal is JsonString idStr
                    && Id.TryParse(idStr.Value, out var pointId)
                    && string.Equals(LastSegment(pointId.Value), pointName, StringComparison.Ordinal)
                    && TryReadPosition(obj, out position))
                {
                    return true;
                }
            }

            position = Vec2.Zero;
            return false;
        }

        private static bool TryReadPosition(JsonValue value, out Vec2 position)
        {
            if (value is JsonObject obj
                && obj.TryGetValue("position", out var posVal) && posVal is JsonObject posObj
                && posObj.TryGetValue("x", out var xv) && xv is JsonNumber xn
                && posObj.TryGetValue("y", out var yv) && yv is JsonNumber yn)
            {
                position = new Vec2(xn.Value, yn.Value);
                return true;
            }

            position = Vec2.Zero;
            return false;
        }

        private static string LastSegment(string value)
        {
            var idx = value.LastIndexOf('.');
            return idx < 0 ? value : value.Substring(idx + 1);
        }

        private void Fail(string message) => _onFailure?.Invoke(message);
    }
}
