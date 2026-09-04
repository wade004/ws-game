using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.SceneRouter
{
    /// <summary>
    /// 从 <c>world.map</c> 记录抽取场景路由需要的最小字段（见
    /// 05_对象模型与世界.md 第 4.1 节字段表；本模块只读取场景路由需要的字段，其余字段
    /// ——regions/teleport_points/music_ref/allowed_difficulties——不属于本模块关注范围，
    /// 见 <see cref="WorldMapSchema"/> 类型注释、本模块 README 判断记录）。
    /// </summary>
    public sealed class SceneDescriptor
    {
        public Id Id { get; }

        /// <summary>场景资源引用（不含路径，由引擎适配层解析加载），必填。</summary>
        public string SceneRef { get; }

        /// <summary>导航资源引用（可行走区域、遮挡层数据）。05 第 4.1 节原文标注必填，本类型
        /// 按任务书拍板暴露为可空——见本模块 README 判断记录（TableSchema 仍按 05 原文登记为
        /// 必填，本属性额外做一层防御性读取，不代表 nav_ref 在数据层面真的可选）。</summary>
        public string? NavRef { get; }

        /// <summary>默认出生点坐标：取 <c>spawn_points</c> 列表第一个元素的 <c>position</c>
        /// （见本模块 README 判断记录——05 未指定"默认"具体取哪一个，示例数据里唯一一条
        /// 出生点的 id 后缀恰好是 <c>default</c>，按"列表第一个即默认"处理）。</summary>
        public Vec2 DefaultSpawnPosition { get; }

        public SceneDescriptor(Id id, string sceneRef, string? navRef, Vec2 defaultSpawnPosition)
        {
            Id = id;
            SceneRef = sceneRef;
            NavRef = navRef;
            DefaultSpawnPosition = defaultSpawnPosition;
        }

        public static SceneDescriptor FromRecord(DataRecord record)
        {
            var id = record.GetId("id");
            var sceneRef = record.GetString("scene_ref");
            var navRef = record.TryGetString("nav_ref", out var navRefVal) ? navRefVal : null;

            var spawnPoints = record.GetArray("spawn_points");
            if (spawnPoints.Count == 0)
            {
                throw new DataFieldException(record.Table.Name, record.Key, "spawn_points", "至少需要一个出生点才能确定默认出生点");
            }

            if (!(spawnPoints[0] is JsonObject first)
                || !first.TryGetValue("position", out var posVal)
                || !(posVal is JsonObject posObj)
                || !posObj.TryGetValue("x", out var xv) || !(xv is JsonNumber xn)
                || !posObj.TryGetValue("y", out var yv) || !(yv is JsonNumber yn))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "spawn_points", "第 0 个出生点缺少合法 position（{\"x\": Number, \"y\": Number}）");
            }

            var defaultSpawnPosition = new Vec2(xn.Value, yn.Value);

            return new SceneDescriptor(id, sceneRef, navRef, defaultSpawnPosition);
        }
    }
}
