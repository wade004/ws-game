using System;
using Core.Foundation.Common;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary>
    /// <see cref="IVfxPlayer.Spawn"/> 的挂接目标判别联合（见 09_表现层.md 第 5.3 节
    /// <c>VfxAttach</c>）：<c>World(Vec2)</c> | <c>Anchor(entityId, anchorId)</c> |
    /// <c>Socket(entityId, socketId)</c> | <c>Screen(Vec2 normalized)</c>。只读值类型，通过静态
    /// 工厂构造，按 <see cref="Mode"/> 访问对应字段——与 <c>Core.Foundation.Expr.ExprValue</c>
    /// 同一判别联合写法惯例。
    /// </summary>
    public readonly struct VfxAttach : IEquatable<VfxAttach>
    {
        public VfxAttachMode Mode { get; }

        /// <summary><see cref="VfxAttachMode.World"/> 的世界坐标，或 <see cref="VfxAttachMode.Screen"/>
        /// 的归一化屏幕坐标（含义随 <see cref="Mode"/> 而定，二者结构相同、语义不同，见类型注释）。</summary>
        public Vec2 Position { get; }

        /// <summary><see cref="VfxAttachMode.Anchor"/>/<see cref="VfxAttachMode.Socket"/> 的挂接
        /// 宿主实体 id；其余模式为 null。</summary>
        public Id? EntityId { get; }

        /// <summary><see cref="VfxAttachMode.Anchor"/> 的锚点 id 或 <see cref="VfxAttachMode.Socket"/>
        /// 的挂点 id；其余模式为 null。</summary>
        public Id? PointId { get; }

        private VfxAttach(VfxAttachMode mode, Vec2 position, Id? entityId, Id? pointId)
        {
            Mode = mode;
            Position = position;
            EntityId = entityId;
            PointId = pointId;
        }

        public static VfxAttach World(Vec2 worldPosition) => new VfxAttach(VfxAttachMode.World, worldPosition, null, null);

        public static VfxAttach Anchor(Id entityId, Id anchorId) => new VfxAttach(VfxAttachMode.Anchor, Vec2.Zero, entityId, anchorId);

        public static VfxAttach Socket(Id entityId, Id socketId) => new VfxAttach(VfxAttachMode.Socket, Vec2.Zero, entityId, socketId);

        /// <summary><paramref name="normalizedScreenPosition"/> 是归一化屏幕坐标（04/09 未规定
        /// 具体量纲，本模块判断记录：采用 [0,1] 归一化，具体像素换算留给引擎适配层）。</summary>
        public static VfxAttach Screen(Vec2 normalizedScreenPosition) => new VfxAttach(VfxAttachMode.Screen, normalizedScreenPosition, null, null);

        public bool Equals(VfxAttach other) =>
            Mode == other.Mode && Position.Equals(other.Position) && Nullable.Equals(EntityId, other.EntityId) && Nullable.Equals(PointId, other.PointId);

        public override bool Equals(object? obj) => obj is VfxAttach other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Mode;
                hash = (hash * 397) ^ Position.GetHashCode();
                hash = (hash * 397) ^ (EntityId?.GetHashCode() ?? 0);
                hash = (hash * 397) ^ (PointId?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
