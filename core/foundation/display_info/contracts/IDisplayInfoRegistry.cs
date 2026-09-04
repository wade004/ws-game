using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.DisplayInfo
{
    /// <summary>
    /// 外形注册表契约（见 01_分层与依赖.md L0 模块表 <c>display_info</c> 行、
    /// 03_运行时骨架.md 第 9 节 <c>DisplayInfoRegistry</c> 签名）：把逻辑 id 映射到表现资源引用。
    /// </summary>
    public interface IDisplayInfoRegistry
    {
        /// <summary>按单个逻辑 id（<c>display.map.logical_id</c>）取其外形信息；未登记返回 null。</summary>
        DisplayInfo? Lookup(Id logicalId);

        /// <summary>按类别批量取用，供表现层预加载或编辑器工具枚举使用；未登记该类别返回空列表。</summary>
        IReadOnlyList<DisplayInfo> LookupByCategory(DisplayCategory category);

        /// <summary>全部已登记的外形信息，只读。</summary>
        IReadOnlyList<DisplayInfo> All { get; }

        /// <summary>从 <c>IDataRegistryView</c> 重新读取 <c>display.map</c> 表并重建索引，
        /// 完成后 <c>PublishImmediate</c> 一个 <see cref="DisplayInfoReloadedEvent"/>（key
        /// <c>display_info.reloaded</c>）。</summary>
        void Reload();
    }
}
