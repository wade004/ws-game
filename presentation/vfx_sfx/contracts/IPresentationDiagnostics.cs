using System;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary>
    /// 表现层运行期诊断出口，供 <c>vfx_sfx</c>/<c>feedback_binder</c>/<c>render</c> 三个模块共用
    /// （同一 L5 组装体 <c>Presentation.Common</c>，见 <c>presentation/README.md</c>）。语义与
    /// <c>Core.Foundation.Expr.IExprDiagnostics</c> 同一惯例（04 第 6.4 节"求值期错误按分组默认值
    /// 返回，记录警告，不抛异常"）：本接口用于表现层自身的降级路径记录，例如
    /// <c>attach_mode: socket</c> 但 <c>IRenderer3D.AttachToSocket</c> 的参数形状不满足"挂接一个
    /// 粒子效果句柄"的需要而降级为 <c>world</c> 播放（见 vfx_sfx/README.md 判断记录）、
    /// <c>from_display: source/target</c> 缺少实体 id → 逻辑 id 解析器而跳过某条反馈动作、
    /// <c>SpriteCharacterRig</c> 迟到的异步资源加载最终失败（见该类型类型注释"资源加载完成后回填
    /// 已渲染层"判断记录：占位方块保持不变，只记一条诊断）等场景——一律记一条警告，不抛异常、
    /// 不中断调用方。<c>render</c> 模块未走 <c>VfxPlayer</c>/<c>SfxPlayer</c> 的构造期注入模式
    /// （<c>SpriteCharacterRig</c>/<c>SpriteViewBase</c> 现有构造函数不允许加参数，见 AGENTS.md
    /// "ABI 只新增"），固定使用 <see cref="PresentationDiagnosticsRecorder"/> 默认实现，经
    /// <c>SpriteCharacterRig.Diagnostics</c> 只读属性暴露。</summary>
    public interface IPresentationDiagnostics
    {
        void Warn(string message);
    }

    /// <summary><see cref="IPresentationDiagnostics"/> 的内存实现：警告文本按发生顺序累积，
    /// 供测试与调用方检查（与 <c>Core.Foundation.Expr.ExprDiagnosticsRecorder</c> 同一惯例）。</summary>
    public sealed class PresentationDiagnosticsRecorder : IPresentationDiagnostics
    {
        private readonly System.Collections.Generic.List<string> _warnings = new System.Collections.Generic.List<string>();

        public System.Collections.Generic.IReadOnlyList<string> Warnings => _warnings;

        public void Warn(string message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            _warnings.Add(message);
        }
    }
}
