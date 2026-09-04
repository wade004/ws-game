using Core.Foundation.Common;

namespace Core.Foundation.SimLoop
{
    /// <summary>供 <see cref="EntityFilter.Predicate"/> 使用的具名委托（见 00 架构总则、
    /// common/contracts/Callbacks.cs 的具名委托约定：携带参数的回调按各自接口语义定义
    /// 具名委托，不复用裸 Predicate/Func）。</summary>
    public delegate bool EntityPredicate(Entity entity);

    /// <summary>
    /// <see cref="IWorldSim.QueryEntities"/> 的查询条件：三个条件均可选，全部提供时按
    /// "与"关系联合过滤；结果按 <c>EntityId</c> 序数排序（确定性，见 <see cref="WorldSim"/>）。
    /// </summary>
    public readonly struct EntityFilter
    {
        /// <summary>按实体类型过滤（<see cref="Entity.Kind"/>），null 表示不限制。</summary>
        public string? Kind { get; }

        /// <summary>按所属地图过滤（<see cref="Entity.MapId"/>），null 表示不限制。</summary>
        public Id? MapId { get; }

        /// <summary>自定义谓词，null 表示不限制。</summary>
        public EntityPredicate? Predicate { get; }

        public EntityFilter(string? kind = null, Id? mapId = null, EntityPredicate? predicate = null)
        {
            Kind = kind;
            MapId = mapId;
            Predicate = predicate;
        }
    }
}
