using Core.Foundation.Common;
using DisplayShadowMode = Core.Foundation.DisplayInfo.ShadowMode;
using EngineShadowMode = Core.Foundation.EngineAdapter.ShadowMode;

namespace Presentation.Render
{
    /// <summary>
    /// 影子呈现的表现层描述（见 09 第 3.4 节"每个可见单位默认携带一个地面投影影子，影子锚定在
    /// 逻辑平面坐标（不随高度位移）"）。<see cref="Core.Foundation.DisplayInfo.DisplayInfo.Shadow"/>
    /// 与 <c>IRenderer3D.SetShadow</c>/引擎侧 sprite 阴影实现分别使用两个同名但不同命名空间的
    /// <c>ShadowMode</c> 枚举（<c>Core.Foundation.DisplayInfo.ShadowMode</c> 是数据侧取值，
    /// <c>Core.Foundation.EngineAdapter.ShadowMode</c> 是 <c>IRenderer3D.SetShadow</c> 的参数类型，
    /// 见 02 第 1.12 节）——本类型的 <see cref="ToEngineShadowMode"/> 做二者之间的转换，两个枚举的
    /// 取值集合（None/Blob/Projected）语义相同，逐项映射，不丢信息。
    /// </summary>
    public static class ShadowSpec
    {
        /// <summary>影子锚定的世界平面坐标：恒等于逻辑位置本身，不随高度位移（见 09 第 3.4 节）。</summary>
        public static Vec2 ResolveAnchor(Vec2 logicalPos) => logicalPos;

        /// <summary>把 DisplayInfo 数据侧的阴影模式转换为 <c>IRenderer3D.SetShadow</c> 期望的引擎侧
        /// 枚举（见类型注释）。</summary>
        public static EngineShadowMode ToEngineShadowMode(DisplayShadowMode mode)
        {
            switch (mode)
            {
                case DisplayShadowMode.None:
                    return EngineShadowMode.None;
                case DisplayShadowMode.Blob:
                    return EngineShadowMode.Blob;
                case DisplayShadowMode.Projected:
                    return EngineShadowMode.Projected;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(mode), mode, "未知的 ShadowMode 取值");
            }
        }
    }
}
