using System;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;

namespace Core.Gameplay.Spawn
{
    /// <summary>
    /// 一条 <c>spawn.table</c> 记录的强类型视图（见 05_对象模型与世界.md 第 5.1 节字段表），从
    /// <see cref="DataRecord"/> 构造，构造期完成全部字段解析与合法性检查（惯例同
    /// <c>Core.Gameplay.AreaTrigger.AreaTriggerDef</c>）。
    /// </summary>
    public sealed class SpawnTableDef
    {
        public Id Id { get; }

        public Id MapId { get; }

        /// <summary>指向 <c>creature.template</c> 或 <c>gobj.template</c>（见
        /// <c>SpawnContentRefRule</c> 的域名与存在性校验）。</summary>
        public Id ContentRef { get; }

        public Vec2 Position { get; }

        /// <summary>数据未提供该字段时为 0。</summary>
        public double Facing { get; }

        public string? ConditionText { get; }

        public RespawnPolicy RespawnPolicy { get; }

        /// <summary><see cref="RespawnPolicy"/> 为 <see cref="Gameplay.Spawn.RespawnPolicy.Timer"/> 时
        /// 应非空（字段组完整性见 <c>SpawnRespawnPolicyFieldGroupRule</c>；本类型对缺失的 timer
        /// 兜底为 0，不在构造期阻断，交由内容管线校验报告问题）。</summary>
        public double? RespawnTimer { get; }

        public static SpawnTableDef FromRecord(DataRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            var id = record.GetId("id");
            var mapId = record.GetId("map_id");
            var contentRef = record.GetId("content_ref");
            var position = record.GetVec2("position");
            var facing = record.TryGetNumber("facing", out var f) ? f : 0.0;
            var conditionText = record.TryGetString("condition", out var condition) ? condition : null;

            var policyText = record.GetString("respawn_policy");
            if (!RespawnPolicyNames.TryParse(policyText, out var policy))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "respawn_policy",
                    $"非法取值 \"{policyText}\"，只允许 on_map_enter|once|never|timer");
            }

            var respawnTimer = record.TryGetNumber("respawn_timer", out var timer) ? (double?)timer : null;

            return new SpawnTableDef(id, mapId, contentRef, position, facing, conditionText, policy, respawnTimer);
        }

        private SpawnTableDef(
            Id id, Id mapId, Id contentRef, Vec2 position, double facing, string? conditionText,
            RespawnPolicy respawnPolicy, double? respawnTimer)
        {
            Id = id;
            MapId = mapId;
            ContentRef = contentRef;
            Position = position;
            Facing = facing;
            ConditionText = conditionText;
            RespawnPolicy = respawnPolicy;
            RespawnTimer = respawnTimer;
        }
    }
}
