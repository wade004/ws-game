using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 空间查询的中立过滤条件。02_引擎适配层.md 第 1.9 节把 filter 描述为
    /// "中立的过滤条件（阵营、单位类型等，由上层构造)"，但阵营/单位类型是 L1/L2 的概念，
    /// L-1 不得依赖 L0 以上任何东西（见 01_分层与依赖.md 第 3 节依赖矩阵）。因此本类型只提供
    /// 中立的字符串标签匹配（必须包含 RequiredTags 全部、不得包含 ExcludedTags 任一），
    /// 具体标签的语义由上层在构造 filter、以及向查询实现登记可查询对象时约定，
    /// 引擎适配层本身不解释标签含义（见任务汇报第 5 节判断记录）。
    /// </summary>
    public readonly struct QueryFilter
    {
        private readonly IReadOnlyList<string>? _requiredTags;
        private readonly IReadOnlyList<string>? _excludedTags;

        // 属性用 null 合并兜底：结构体除了本构造函数外还存在编译器生成的隐式无参构造函数
        // （例如 default(QueryFilter)、`new QueryFilter[n]`），那条路径不会执行下面的构造函数体，
        // 字段会保持为 null；因此不能依赖构造函数把 null 换成 Array.Empty，属性访问器自己兜底。
        public IReadOnlyList<string> RequiredTags => _requiredTags ?? Array.Empty<string>();
        public IReadOnlyList<string> ExcludedTags => _excludedTags ?? Array.Empty<string>();

        public QueryFilter(IReadOnlyList<string>? requiredTags = null, IReadOnlyList<string>? excludedTags = null)
        {
            _requiredTags = requiredTags ?? Array.Empty<string>();
            _excludedTags = excludedTags ?? Array.Empty<string>();
        }

        /// <summary>不做任何标签过滤，匹配全部已登记对象。</summary>
        public static QueryFilter None { get; } = new QueryFilter();
    }

    /// <summary>Shape 联合类型的具体形状种类（见 05_对象模型与世界.md 第 3.5 节）。</summary>
    public enum ShapeKind
    {
        Circle,
        Cone,
        Line,
        Rect
    }

    /// <summary>
    /// 范围形状联合类型（见 05_对象模型与世界.md 第 3.5 节）：
    /// circle{center,radius}、cone{origin,direction,angle,radius}、
    /// line{origin,direction,length,width}、rect{origin,halfExtents,rotation}。
    /// direction/angle/rotation 的角度单位文档未注明，由调用方与实现方约定一致即可，
    /// 本层不做任何角度换算（见任务汇报第 5 节判断记录）。
    /// </summary>
    public readonly struct Shape
    {
        public ShapeKind Kind { get; }

        /// <summary>circle 的 center；cone/line 的 origin；rect 的 origin。</summary>
        public Vec2 Origin { get; }

        /// <summary>circle/cone 的半径/延伸距离。</summary>
        public double Radius { get; }

        /// <summary>cone/line 的方向。</summary>
        public double Direction { get; }

        /// <summary>cone 的张开角度。</summary>
        public double Angle { get; }

        /// <summary>line 的长度。</summary>
        public double Length { get; }

        /// <summary>line 的宽度。</summary>
        public double Width { get; }

        /// <summary>rect 的半宽高。</summary>
        public Vec2 HalfExtents { get; }

        /// <summary>rect 的旋转角度。</summary>
        public double Rotation { get; }

        private Shape(ShapeKind kind, Vec2 origin, double radius, double direction, double angle, double length, double width, Vec2 halfExtents, double rotation)
        {
            Kind = kind;
            Origin = origin;
            Radius = radius;
            Direction = direction;
            Angle = angle;
            Length = length;
            Width = width;
            HalfExtents = halfExtents;
            Rotation = rotation;
        }

        public static Shape Circle(Vec2 center, double radius) =>
            new Shape(ShapeKind.Circle, center, radius, 0, 0, 0, 0, Vec2.Zero, 0);

        public static Shape Cone(Vec2 origin, double direction, double angle, double radius) =>
            new Shape(ShapeKind.Cone, origin, radius, direction, angle, 0, 0, Vec2.Zero, 0);

        public static Shape Line(Vec2 origin, double direction, double length, double width) =>
            new Shape(ShapeKind.Line, origin, 0, direction, 0, length, width, Vec2.Zero, 0);

        public static Shape Rect(Vec2 origin, Vec2 halfExtents, double rotation) =>
            new Shape(ShapeKind.Rect, origin, 0, 0, 0, 0, 0, halfExtents, rotation);

        /// <summary>
        /// 返回一个只把 <see cref="Origin"/> 换成 <paramref name="origin"/>、其余字段原样保留的新
        /// <see cref="Shape"/>（惯例同 <see cref="Vec2"/> 的纯值运算方法，见该类型 <c>Distance</c>/
        /// <c>Dot</c> 判断记录——只操作本类型自身与 <see cref="Vec2"/>，不引入任何 L0 以上依赖）。
        /// 供"形状模板 + 锚点"两段式调用（见 <c>Core.Rules.Common.ISkillHost.FindUnits</c> 判断
        /// 记录）把不预先绑定坐标的模板重新锚定到一个具体点，本方法只做位置平移，不改变
        /// <see cref="Direction"/>/<see cref="Angle"/>/<see cref="Rotation"/> 等朝向字段——如需同时
        /// 按施法者当前朝向重新计算方向（如 <c>Core.Rules.Targeting.TargetHost</c> 的目标链形状），
        /// 调用方仍需自行构造对应朝向的 Shape，本方法不代为猜测。
        /// </summary>
        public Shape WithOrigin(Vec2 origin)
        {
            switch (Kind)
            {
                case ShapeKind.Circle:
                    return Circle(origin, Radius);
                case ShapeKind.Cone:
                    return Cone(origin, Direction, Angle, Radius);
                case ShapeKind.Line:
                    return Line(origin, Direction, Length, Width);
                case ShapeKind.Rect:
                    return Rect(origin, HalfExtents, Rotation);
                default:
                    throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "未知 Shape 种类");
            }
        }

        /// <summary>
        /// 按 <paramref name="margin"/> 外扩本形状，返回一个覆盖范围更大的新 <see cref="Shape"/>（同
        /// <see cref="WithOrigin"/> 惯例：纯值运算，只操作本类型自身与 <see cref="Vec2"/>，不引入任何
        /// L0 以上依赖）。供格子吸附的"广相位"查询使用（见 <c>GridSnapShapeQuery.QueryShapeAtCellCenters</c>
        /// 判断记录）：候选对象的原始坐标可能落在精确形状之外、但它所属格子的中心点落在形状内（或
        /// 反过来），先用外扩后的形状换一批"宁可多、不可少"的候选集合，再对每个候选的格子中心点用
        /// <see cref="ShapeGeometry.Contains"/> 做精确判定，缺一不可——只外扩不精确重判会把候选原始
        /// 坐标误当格子中心点计入；只精确重判不外扩会漏掉原始坐标恰好卡在形状边界外、格子中心点却
        /// 落在形状内的候选。
        /// <para>
        /// 判断记录（<c>Cone</c> 的角度外扩是近似值）：<c>Circle</c>/<c>Rect</c>（半径/半宽高直接加
        /// <paramref name="margin"/>）与 <c>Line</c>（长度、宽度各加 <c>2 × margin</c>，起点沿反方向
        /// 回退 <paramref name="margin"/>，覆盖"原始坐标卡在起点之前一点点"的情形）都能精确外扩；
        /// <c>Cone</c> 的角度维度不能像半径那样直接相加——本方法用小角度近似
        /// <c>2 × asin(min(1, margin / max(radius, 1e-6)))</c> 估出一个保守的角度外扩量，半径同样
        /// 加 <paramref name="margin"/>。近似值可能在极端参数下（如半径远小于 <paramref name="margin"/>）
        /// 外扩到 360 度（本方法按 <see cref="Math.Min"/> 钳到 2π，退化为整圆查询，仍然"宁可多、不可
        /// 少"，不影响随后精确重判的正确性，只是broad-phase 查询范围偏大）。
        /// </para>
        /// </summary>
        public Shape Expand(double margin)
        {
            if (margin < 0 || !double.IsFinite(margin))
            {
                throw new ArgumentOutOfRangeException(nameof(margin), margin, "margin 不能为负数或非有限值");
            }

            switch (Kind)
            {
                case ShapeKind.Circle:
                    return Circle(Origin, Radius + margin);

                case ShapeKind.Cone:
                {
                    var safeRadius = Math.Max(Radius, 1e-6);
                    var angleMargin = 2.0 * Math.Asin(Math.Min(1.0, margin / safeRadius));
                    var expandedAngle = Math.Min(Angle + angleMargin, 2.0 * Math.PI);
                    return Cone(Origin, Direction, expandedAngle, Radius + margin);
                }

                case ShapeKind.Line:
                {
                    var back = new Vec2(Math.Cos(Direction), Math.Sin(Direction)) * -margin;
                    return Line(Origin + back, Direction, Length + margin * 2.0, Width + margin * 2.0);
                }

                case ShapeKind.Rect:
                    return Rect(Origin, new Vec2(HalfExtents.X + margin, HalfExtents.Y + margin), Rotation);

                default:
                    throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "未知 Shape 种类");
            }
        }
    }

    /// <summary>
    /// 半径/锥形/线段/矩形查询、视线遮挡（见 02_引擎适配层.md 第 1.9 节）。必需接口——
    /// 战斗范围判定与 AI 感知都依赖它。以上全部查询默认作用于调用方当前已加载的场景，
    /// mapId 由该场景隐含，不作为显式参数传入。查询应基于空间索引而非线性扫描全部对象，
    /// 具体索引结构由实现方决定，接口不关心内部实现。
    /// </summary>
    public interface ISpatialQuery
    {
        IReadOnlyList<Id> QueryRadius(Vec2 center, double radius, QueryFilter filter);

        IReadOnlyList<Id> QueryCone(Vec2 origin, double direction, double angle, double range, QueryFilter filter);

        IReadOnlyList<Id> QueryLine(Vec2 from, Vec2 to, QueryFilter filter);

        IReadOnlyList<Id> QueryRect(Vec2 min, Vec2 max, QueryFilter filter);

        /// <summary>按 Shape 联合类型统一查询，供技能目标解析等按同一形状描述复用。</summary>
        IReadOnlyList<Id> QueryShape(Shape shape, QueryFilter filter);

        /// <summary>返回满足过滤条件的最近一个对象；无匹配对象时返回 null。</summary>
        Id? Nearest(Vec2 point, QueryFilter filter);

        bool HasLineOfSight(Vec2 from, Vec2 to);

        /// <summary>
        /// 登记一个对象进空间索引：WorldSim 在对象创建时调用，把该对象的位置、半径与
        /// 过滤用标签登记进空间索引（见 ADR-0016 决策 7）。以上查询方法只查询已登记的对象，
        /// 登记时机与登记内容由调用方负责，接口本身不主动扫描场景。
        /// </summary>
        void Register(Id id, Vec2 position, double radius, IReadOnlyList<string> tags);

        /// <summary>对象位置变化时调用，同步该对象在空间索引里的位置。</summary>
        void UpdatePosition(Id id, Vec2 position);

        /// <summary>对象销毁时调用，从空间索引移除该对象的登记。</summary>
        void Unregister(Id id);

        /// <summary>场景卸载时调用，清空整个空间索引。</summary>
        void Clear();
    }
}
