namespace Presentation.Render
{
    /// <summary>
    /// 混合渲染约定的口味配置项（见 01 L5 模块表 <c>render</c> 行"策略配置项：排序精度、方向量化
    /// 档位数、外形类型选择"；09 第 3.6 节"参考分辨率……均为可配置项，具体数值不在本架构拍板"）。
    /// 本类型只给出合理默认值，具体数值由游戏层在接入时按 [13_新游戏接入指南.md] 的口味配置清单
    /// 覆盖，不属于架构拍板内容。
    /// </summary>
    public sealed class RenderOptions
    {
        /// <summary>默认方向量化档位数（4/8/16 之一，见 05 第 3.2 节）。默认 8。</summary>
        public int DirectionCount { get; set; } = 8;

        /// <summary>每逻辑单位对应的像素数，供高度偏移换算像素位移（见 09 第 3.4 节、
        /// <see cref="IRenderConventionHost.HeightOffsetToPixels"/>）。默认 32。</summary>
        public double PixelsPerUnit { get; set; } = 32.0;
    }
}
