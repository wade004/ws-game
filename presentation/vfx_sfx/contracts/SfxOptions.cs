using System.Collections.Generic;
using Core.Foundation.Common;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary><see cref="Presentation.VfxSfx.Core.SfxPlayer"/> 的策略配置项（见 09_表现层.md
    /// 第 5.5 节"同层音效遵守 priority 抢占规则...口味相关的抢占阈值为可配置项"）。</summary>
    public sealed class SfxOptions
    {
        /// <summary>每个 <see cref="SfxDef.Layer"/> 分组同时并发播放的实例数上限；超出时停掉该层
        /// 当前优先级最低（<see cref="SfxDef.Priority"/> 数值越小视为优先级越高，未声明按最低
        /// 优先级处理，见类型 <see cref="Presentation.VfxSfx.Core.SfxPlayer"/> 判断记录）的一个。
        /// 未在 <see cref="MaxConcurrentPerLayer"/> 登记的层使用 <see cref="DefaultMaxConcurrent"/>。</summary>
        public IReadOnlyDictionary<string, int> MaxConcurrentPerLayer { get; set; } =
            new Dictionary<string, int>();

        public int DefaultMaxConcurrent { get; set; } = 8;

        /// <summary>随机变体选取使用的 <c>IRngHost</c> 流 id（见 09_表现层.md 第 5.2 节 variants
        /// 字段；表现层随机不影响逻辑判定，只影响呈现，因此不复用规则层的战斗/AI 等流）。</summary>
        public Id RngStream { get; set; } = new Id("presentation.sfx");
    }
}
