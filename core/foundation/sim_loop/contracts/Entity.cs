using Core.Foundation.Common;

namespace Core.Foundation.SimLoop
{
    /// <summary>
    /// 实体生命周期（见 05_对象模型与世界.md 第 1.1 节）：<c>created → active →
    /// (inactive 可选) → destroyed</c>。本枚举值由 <see cref="WorldSim"/> 管理，业务代码
    /// 只读（见 <see cref="Entity.Lifecycle"/> 的 <c>internal set</c>）。
    /// </summary>
    public enum EntityLifecycle
    {
        Created,
        Active,
        Inactive,
        Destroyed
    }

    /// <summary>
    /// 世界模拟中逻辑对象的公共基类（见 05_对象模型与世界.md 第 1.1 节字段表）。
    /// 世界模拟由纯逻辑对象构成，不继承任何引擎节点或组件（拍板决策 2）；
    /// <c>Unit</c>/<c>GameObject</c> 等具体子类属于 L3
    /// （<c>core/carriers</c>），不属于本模块，本模块只提供公共基类。
    /// </summary>
    public abstract class Entity
    {
        /// <summary>运行期唯一实例 id（区别于内容模板 id）。</summary>
        public Id EntityId { get; }

        /// <summary>来源的内容模板 id（如 <c>creature.grey_wolf</c>），手工放置对象可为空。</summary>
        public Id? TemplateId { get; set; }

        /// <summary>世界平面坐标（见 05 第 3 节）。</summary>
        public Vec2 Position { get; set; }

        /// <summary>纵轴深度排序键（见 05 第 3 节）。</summary>
        public double LayerDepth { get; set; }

        /// <summary>朝向角度（弧度或量化方向索引，见 05 第 3 节）。</summary>
        public double Facing { get; set; }

        /// <summary>所属地图。</summary>
        public Id MapId { get; set; }

        /// <summary>刷新表引用，手工放置为空。</summary>
        public Id? SpawnedBy { get; set; }

        /// <summary>该实体读取世界状态时使用的命名空间前缀（可选，多数继承地图默认）。</summary>
        public Id? WorldFlagsScope { get; set; }

        /// <summary>实体类型（如 "unit"、"gobj"），由具体子类给出；已落地的具体取值见
        /// <see cref="EntityKinds"/>。</summary>
        public abstract string Kind { get; }

        /// <summary>生命周期状态，由 <see cref="WorldSim"/> 管理（见类型注释）。</summary>
        public EntityLifecycle Lifecycle { get; internal set; }

        protected Entity(Id entityId, Id mapId)
        {
            EntityId = entityId;
            MapId = mapId;
            Lifecycle = EntityLifecycle.Created;
        }
    }
}
