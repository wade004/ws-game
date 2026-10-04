namespace Presentation.Render
{
    /// <summary>
    /// 残影渲染的可选能力接口（手感设计/07 第 1 节、08 第 2 节 <c>afterimage_enabled</c>，ADR-0148）：View 实现它即可被
    /// <c>trail_start/trail_end</c> 标记（手感字段 <c>afterimage_enabled</c> 为真时）开关残影——快速位移/挥砍期间在身后留下逐渐淡出的幽灵拷贝。
    /// 不实现的 View 静默不出残影。残影是纯表现，不回流逻辑层。
    /// </summary>
    public interface IAfterimageTarget
    {
        /// <summary>开始/停止残影（幂等）。</summary>
        void SetAfterimage(bool enabled);
    }
}
