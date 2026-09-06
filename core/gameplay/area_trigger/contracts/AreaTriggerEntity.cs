using System;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.SimLoop;

namespace Core.Gameplay.AreaTrigger
{
    /// <summary>
    /// 区域触发体运行期实体（见 05_对象模型与世界.md 第 1.5 节 <c>AreaTrigger</c> 字段表）。只持有 05
    /// 明确列出的四个字段——单位是否处于范围内、<c>one_shot</c> 是否已触发等运行期簿记不属于对象
    /// 模型字段，继续由 <see cref="AreaTriggerHost"/> 用内部字典管理（惯例同
    /// <c>core/carriers/projectile</c> <c>ProjectileEntity</c>/<c>ProjectileHost</c> 判断记录：
    /// L3/L4 宿主为运行期簿记维护私有状态，不塞进 <see cref="Entity"/> 子类本身）。
    /// <para>
    /// 判断记录（加固 J：从"纯数据记录 + 宿主字典"改为真正的 <see cref="Entity"/> 子类）：此前
    /// <see cref="AreaTriggerHost"/> 只在内部 <c>RuntimeEntry</c> 字典持有触发定义，不产生任何
    /// <see cref="Entity"/> 实例，不符合 05 第 1 节继承树"<c>AreaTrigger</c> 是 <c>Entity</c> 叶子
    /// 类型之一"的结论（任务拍板：改代码，不改文档结论）。本类型补上这一叶子类型；
    /// <see cref="AreaTriggerHost"/> 侧的 <c>RuntimeEntry</c>（<c>Kind</c>——含数据表四类 + 内部
    /// <c>Trap</c>、<c>MapTransition</c>/<c>EncounterStart</c>/<c>Script</c> 具体参数、
    /// <c>ConditionNode</c> 已解析表达式树等）继续保留：那些字段要么不是 05 第 1.5 节列出的对象模型
    /// 字段，要么是可以从本类字段重新派生的缓存（<c>ConditionNode</c> 由 <see cref="ConditionText"/>
    /// 解析得到），不重复搬到本类型。
    /// </para>
    /// <para>
    /// 判断记录（陷阱共用同一个 <see cref="Entity.Kind"/>、<see cref="TriggerType"/> 为 null）：07
    /// 第 3.1 节 <c>trap</c> 类型复用区域触发的进入检测机制（05 第 7 节变更记录 2026-09-05"陷阱类
    /// 物件复用区域触发的进入检测机制……不在 <c>trigger_type</c> 枚举中另列"），
    /// <see cref="IAreaTriggerHost.RegisterTrap"/> 动态登记的触发体因此同样生成一个
    /// <see cref="Entity.Kind"/> = <see cref="EntityKinds.AreaTrigger"/> 的本类型实例，但
    /// <see cref="TriggerType"/> 为 null（陷阱不是 05 第 1.5 节 <c>triggerType</c> 四选一枚举的合法
    /// 取值；内部用哪一种调度种类区分——含 <c>Trap</c>——的完整信息仍在
    /// <see cref="AreaTriggerHost"/> 内部 <c>RuntimeEntry</c> 持有，不重复放上本实体）。
    /// </para>
    /// <para>
    /// 判断记录（不进存档）：见 05 第 1.5 节"进存档：手工放置的固定触发体不进存档（随地图数据加载）；
    /// <c>oneShot</c> 已触发的状态经 <c>WorldState</c> 记录"——本类型与 <see cref="AreaTriggerHost"/>
    /// 均不实现 <c>Core.Foundation.SaveSystem.IPersistable</c>（见 tests 对应锁定用例，惯例同
    /// <c>ProjectileHostTests.ProjectileHostAndEntity_DoNotImplementIPersistable</c>）；场景卸载时
    /// <c>IWorldSim.ClearAll</c>/<see cref="AreaTriggerHost.UnloadMap"/> 销毁的实体在下次
    /// <see cref="AreaTriggerHost.LoadForMap"/>（<c>post_load</c> 钩子/<c>GameplayAssembly.EnterMap</c>）
    /// 时按数据重新创建，不依赖存档恢复。
    /// </para>
    /// </summary>
    public sealed class AreaTriggerEntity : Entity
    {
        public override string Kind => EntityKinds.AreaTrigger;

        /// <summary>触发范围（绝对世界坐标，见 05 第 1.5 节 <c>shape</c>、第 3.5 节 Shape 联合类型）。</summary>
        public Shape Shape { get; }

        /// <summary>触发类型（见 05 第 1.5 节 <c>triggerType</c>）：数据驱动的四选一
        /// （<c>map_transition</c>/<c>quest_explore</c>/<c>encounter_start</c>/<c>script</c>）；
        /// 陷阱触发体（见类型注释判断记录）为 null。</summary>
        public AreaTriggerType? TriggerType { get; }

        /// <summary>附加触发条件的原始 Expr 文本（见 05 第 1.5 节 <c>condition: Optional&lt;Expr&gt;</c>），
        /// 表示同 <see cref="AreaTriggerDef.ConditionText"/>；未提供该字段/陷阱触发体时为 null。</summary>
        public string? ConditionText { get; }

        /// <summary>是否只触发一次（见 05 第 1.5 节 <c>oneShot</c>）；陷阱触发体恒为 false（见
        /// <see cref="AreaTriggerHost.RegisterTrap"/>"不产生 one_shot/condition 语义"）。</summary>
        public bool OneShot { get; }

        public AreaTriggerEntity(
            Id entityId, Id mapId, Shape shape, AreaTriggerType? triggerType, string? conditionText, bool oneShot)
            : base(entityId, mapId)
        {
            Shape = shape;
            TriggerType = triggerType;
            ConditionText = conditionText;
            OneShot = oneShot;
        }

        /// <summary>
        /// 加固任务补充（05 §3.6 碰撞层落地，`trigger_only` 登记进 <c>ISpatialQuery</c> 时的半径）：
        /// 以 <see cref="Shape.Origin"/>（即本实体 <see cref="Entity.Position"/>，见类型顶部判断记录 1
        /// "Position 取 Shape.Origin"）为圆心的外接半径——覆盖形状内任意一点到 Origin 的最大距离，
        /// 保证按固定半径圆做粗筛的空间索引不会漏掉形状边缘的部分。
        /// <para>
        /// 判断记录（各形状取值依据 05 第 3.5 节 Shape 联合类型的参数定义）：<c>circle</c> 的
        /// <c>Origin</c> 是圆心，外接半径就是 <c>Radius</c> 本身；<c>cone</c> 的 <c>Origin</c> 是扇形
        /// 顶点，外接半径同样是 <c>Radius</c>（张开角度不影响"到顶点的最大距离"这个上界）；
        /// <c>line</c> 的 <c>Origin</c> 是矩形带起点（非中心），最远点在"沿方向走满 Length、再垂直
        /// 偏移半个 Width"的那个角上，外接半径按勾股定理 <c>sqrt(Length² + (Width/2)²)</c>；
        /// <c>rect</c> 的 <c>Origin</c> 是矩形中心，外接半径是到任一角的距离
        /// <c>sqrt(HalfExtents.X² + HalfExtents.Y²)</c>。
        /// </para>
        /// <para>
        /// 判断记录（为什么放在这里而不是 <c>EntitySpatialSyncHost.KindConfig.Radius</c> 固定值）：
        /// <c>core/carriers/assembly</c> 是 L3，01 第 3 节依赖矩阵禁止 L3 依赖 L4，不能在那里引用本
        /// 类型按 <see cref="Shape"/> 精确计算；<c>Core.Gameplay.Assembly.GameplayAssembly</c>（L4）
        /// 通过 <c>EntitySpatialSyncHost.KindConfig.RadiusResolver</c> 委托把本属性接上，覆盖
        /// <c>CarriersAssembly.DefaultSpatialSyncKinds</c> 里的固定近似值 0.5（见该属性判断记录）。
        /// </para>
        /// </summary>
        public double BoundingRadius
        {
            get
            {
                switch (Shape.Kind)
                {
                    case ShapeKind.Circle:
                    case ShapeKind.Cone:
                        return Shape.Radius;

                    case ShapeKind.Line:
                        return Math.Sqrt(Shape.Length * Shape.Length + (Shape.Width / 2.0) * (Shape.Width / 2.0));

                    case ShapeKind.Rect:
                        return Math.Sqrt(
                            Shape.HalfExtents.X * Shape.HalfExtents.X + Shape.HalfExtents.Y * Shape.HalfExtents.Y);

                    default:
                        return 0.0;
                }
            }
        }
    }
}
