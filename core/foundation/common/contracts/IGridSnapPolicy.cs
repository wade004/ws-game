using System;

namespace Core.Foundation.Common
{
    /// <summary>
    /// 格子吸附策略（见 04_数据与内容管线.md 第 3.1 节 <c>found.time_model.grid_snap</c>、
    /// ADR-0013 决策 6"移动在离散模式下...可选格子吸附策略（格子尺寸为参数，范围形状按格子中心
    /// 采样）"、13_新游戏接入指南.md 第 4 节"网格吸附"口味配置项）：把一个连续平面坐标换算成它所属
    /// 格子的中心点。声明为可替换策略（而不是写死在调用方内部的一个私有算法），是因为"格子"本身的
    /// 几何定义（是否有偏移原点、是否允许非正方形格子、是否六边形/等距网格等）不是三份承诺文本
    /// 规定的架构结论——三处承诺只说"声明格子尺寸；范围形状按格子中心采样"，没有进一步约束格子的
    /// 具体几何形态；把它做成接口 + 默认实现，游戏层可以在不改动调用侧代码的前提下替换成自己需要
    /// 的网格几何，默认实现只覆盖"以原点为基准的正方形网格"这一种最常见形态。
    /// </summary>
    public interface IGridSnapPolicy
    {
        /// <summary>
        /// 把 <paramref name="position"/> 换算成它所属格子的中心点。<paramref name="cellSize"/> 对应
        /// <c>found.time_model.grid_snap.cell_size</c>（04 第 3.1 节字段表），调用方负责保证其为正数
        /// （<c>Core.Foundation.SimLoop.TimeModelValidationRule</c> 在数据加载期已校验该字段为正——
        /// 本模块 <c>core/foundation/common</c> 层级低于 <c>core/foundation/sim_loop</c>，不写
        /// <c>&lt;see cref&gt;</c> 跨模块引用，只用文字说明来源）；实现可以自行选择是否再次防御性
        /// 校验，本接口不强制。
        /// </summary>
        Vec2 SnapToCellCenter(Vec2 position, double cellSize);
    }

    /// <summary>
    /// <see cref="IGridSnapPolicy"/> 的默认实现：以原点 (0, 0) 为基准的正方形网格——每个格子是
    /// <c>[i * cellSize, (i + 1) * cellSize) × [j * cellSize, (j + 1) * cellSize)</c>（<c>i</c>/<c>j</c>
    /// 为整数），中心点即 <c>((i + 0.5) * cellSize, (j + 0.5) * cellSize)</c>。X/Y 两轴独立换算，
    /// 互不影响。
    /// </summary>
    public sealed class GridSnapPolicy : IGridSnapPolicy
    {
        public Vec2 SnapToCellCenter(Vec2 position, double cellSize)
        {
            if (cellSize <= 0 || !double.IsFinite(cellSize))
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize), cellSize, "cellSize 必须为正数");
            }

            return new Vec2(SnapAxis(position.X, cellSize), SnapAxis(position.Y, cellSize));
        }

        private static double SnapAxis(double value, double cellSize) =>
            (Math.Floor(value / cellSize) + 0.5) * cellSize;
    }
}
