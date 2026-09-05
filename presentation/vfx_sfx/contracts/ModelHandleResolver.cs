using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary>
    /// 缺口 13：按实体 id 取其 View 持有的 <see cref="ModelHandle"/> 的委托，供
    /// <see cref="Presentation.VfxSfx.Core.VfxPlayer"/> 的 <c>attach_mode: socket</c> 挂接模式使用
    /// （同 <see cref="AnchorResolver"/>"按需窄契约"惯例）。返回 null 表示该实体的 View 未实现
    /// <see cref="Presentation.Common.IModelHandleProvider"/>、或实现了但当前没有可用句柄（sprite 型
    /// View、View 未绑定/已销毁等），调用方按 09 第 5.3 节判断记录降级为 world 播放。
    /// </summary>
    public delegate ModelHandle? ModelHandleResolver(Id entityId);
}
