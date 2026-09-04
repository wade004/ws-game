using Core.Foundation.Common;

namespace Presentation.Common
{
    /// <summary>
    /// 只读快照门面（铁律 P1"表现层只能读取 WorldSim 暴露的只读快照/查询接口"）：把
    /// <c>IWorldSim.GetEntity</c> 收窄成表现层实际需要的几个只读字段，View 绑定、镜头跟随等
    /// 表现层组件经本接口读取逻辑状态，不直接持有 <c>IWorldSim</c>/<c>Entity</c> 引用。
    /// </summary>
    public interface ISimSnapshot
    {
        /// <summary>实体的世界平面坐标；实体不存在时抛 <see cref="System.InvalidOperationException"/>
        /// （调用前应先 <see cref="Exists"/>）。</summary>
        Vec2 GetPosition(Id entityId);

        /// <summary>实体的朝向角度（弧度，见 05 第 3 节）；实体不存在时抛
        /// <see cref="System.InvalidOperationException"/>。</summary>
        double GetFacing(Id entityId);

        /// <summary>实体的高度偏移（见 05 第 3.3 节，仅 <c>Unit</c> 子类有意义，非 <c>Unit</c>
        /// 实体固定返回 0）；实体不存在时抛 <see cref="System.InvalidOperationException"/>。</summary>
        double GetHeight(Id entityId);

        /// <summary>实体当前是否存在（未销毁）。</summary>
        bool Exists(Id entityId);

        /// <summary>用于查询外形的逻辑 id（<c>TemplateId ?? EntityId</c>，见 03 第 5 节）；
        /// 实体不存在返回 null。</summary>
        Id? GetDisplayId(Id entityId);

        /// <summary>实体对应的 <see cref="ViewKind"/>；无法从实体的 <c>Kind</c> 字符串映射出已知
        /// 分类，或实体不存在时返回 null（见 <see cref="EntityKindMapping"/> 契约缺口说明）。</summary>
        ViewKind? GetKind(Id entityId);
    }
}
