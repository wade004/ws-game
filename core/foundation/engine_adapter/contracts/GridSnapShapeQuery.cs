using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.EngineAdapter
{
    /// <summary>
    /// 离散 + 格子吸附模式下的形状查询（04 第 3.1 节 <c>grid_snap</c>、ADR-0013 决策 6"范围形状按
    /// 格子中心采样"、13 第 4 节"网格吸附"）：<see cref="ISpatialQuery.QueryShape"/> 按候选对象的
    /// 原始坐标做精确几何判定，本类型在此基础上换成"候选对象所属格子的中心点"作为采样点——两个
    /// 调用侧（<c>Core.Rules.Targeting.BuiltinTargetStrategies</c> 的 <c>nearest_in_shape</c>/
    /// <c>all_in_shape</c>、<c>Core.Rules.Skill.SkillHost.FindUnits</c>）都需要同一段逻辑，放在 L-1
    /// 供两个 L2 模块共用（同 <see cref="ShapeGeometry"/> 判断记录：L-1 是唯一允许被所有层调用的
    /// 层）。
    /// </summary>
    public static class GridSnapShapeQuery
    {
        /// <summary>
        /// 两阶段查询：
        /// <list type="number">
        /// <item>广相位——用 <see cref="Shape.Expand(double)"/> 按 <paramref name="cellSize"/> 外扩
        /// 原形状后调用一次 <see cref="ISpatialQuery.QueryShape"/>，取得"宁可多、不可少"的候选集合
        /// （见 <see cref="Shape.Expand(double)"/> 判断记录："候选原始坐标在精确形状之外、但它所属
        /// 格子中心点落在形状内"这一情形，原始形状的精确查询会直接漏掉这个候选，必须先扩大查询半径
        /// 才能捞到它）。</item>
        /// <item>精确重判——对每个候选，经 <paramref name="positionResolver"/> 取其原始坐标，用
        /// <paramref name="gridSnap"/> 换算成格子中心点，再用 <see cref="ShapeGeometry.Contains"/>
        /// 对**原始（未外扩）**形状做一次精确判定；只有格子中心点落在原始形状内的候选才保留。</item>
        /// </list>
        /// 两步缺一不可（见 <see cref="Shape.Expand(double)"/> 判断记录）。返回顺序与
        /// <paramref name="spatial"/> 广相位查询返回的顺序一致（不做任何排序——排序是调用方
        /// <c>TargetHost</c>/<c>SkillHost</c> 各自的职责，本方法只负责"是否命中"这一件事）。
        /// </summary>
        public static IReadOnlyList<Id> QueryShapeAtCellCenters(
            ISpatialQuery spatial,
            Shape shape,
            QueryFilter filter,
            Func<Id, Vec2> positionResolver,
            IGridSnapPolicy gridSnap,
            double cellSize)
        {
            if (spatial == null) throw new ArgumentNullException(nameof(spatial));
            if (positionResolver == null) throw new ArgumentNullException(nameof(positionResolver));
            if (gridSnap == null) throw new ArgumentNullException(nameof(gridSnap));
            if (cellSize <= 0 || !double.IsFinite(cellSize))
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "cellSize 必须为正数");
            }

            var expanded = shape.Expand(cellSize);
            var broad = spatial.QueryShape(expanded, filter);
            var result = new List<Id>(broad.Count);

            for (var i = 0; i < broad.Count; i++)
            {
                var id = broad[i];
                var cellCenter = gridSnap.SnapToCellCenter(positionResolver(id), cellSize);
                if (ShapeGeometry.Contains(shape, cellCenter))
                {
                    result.Add(id);
                }
            }

            return result;
        }
    }
}
