using Core.Foundation.EngineAdapter;

namespace Presentation.Common
{
    /// <summary>
    /// 缺口 13（模型挂点）：<see cref="IView"/> 的可选能力接口——model 型外形的 View 实现本接口，
    /// 暴露其持有的 <see cref="ModelHandle"/>，供 <see cref="Presentation.VfxSfx.Core.VfxPlayer"/> 的
    /// <c>attach_mode: socket</c> 挂接模式经 <see cref="IRenderer3D.AttachToSocket"/> 真正挂接到模型
    /// 挂点（见 09_表现层.md 第 3.3.2 节挂点、vfx_sfx/README.md 此前"socket 挂接降级为 world"的契约
    /// 缺口）。<see cref="Presentation.Render.SpriteViewBase"/>（sprite 型外形）不实现——sprite 型没有
    /// 模型实例，见其类型注释。
    /// <para>
    /// 判断记录（用"能力接口"而不是把 <c>TryGetModelHandle</c> 加进 <see cref="IView"/> 本体）：
    /// <see cref="IView"/> 是 sprite/model 两种外形共用的最小契约（09 第 2 节），强行给 sprite 型
    /// View 也加一个恒返回 null 的 <c>TryGetModelHandle</c> 会让"这个 View 有没有模型"这一问题混进
    /// 每个 sprite 型实现都要顺带回答的样板代码；改用可选能力接口（调用方 <c>is IModelHandleProvider</c>
    /// 判定），与 <see cref="Presentation.VfxSfx.Contracts.AnchorResolver"/>/
    /// <c>EntityPositionResolver</c> 一类"按需窄契约"同一惯例。
    /// </para>
    /// </summary>
    public interface IModelHandleProvider
    {
        /// <summary>返回本 View 当前持有的 <see cref="ModelHandle"/>；View 尚未绑定/已销毁/底层引擎
        /// 未能创建模型实例时返回 null（不抛异常，调用方按"查不到"处理，同本模块其它 Resolver 惯例）。</summary>
        ModelHandle? TryGetModelHandle();
    }
}
