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

        /// <summary>
        /// 外部审核阻塞项 4 收口（首次音效加载边界，见 <c>Presentation.VfxSfx.Core.SfxPlayer.Play</c>
        /// 判断记录）：某个音效资源（<c>sfx.def.resource_ref</c> 或命中的 <c>variants</c>）首次被
        /// 引用、资源尚未加载完成时，一次排队等待的最长秒数——同 <c>VfxOptions.
        /// FirstLoadTimeoutSeconds</c> 判断记录，但 <see cref="ISfxPlayer"/> 契约本身没有
        /// <c>Update(dt)</c> 方法（09 原文未定义，不新增契约方法），本项按墙钟时间（非模拟时间）
        /// 计量，在下一次任意 <see cref="Presentation.VfxSfx.Core.SfxPlayer.Play"/> 调用时惰性清理
        /// 已超时的排队项（见该方法判断记录），不依赖任何逐帧驱动。默认 5 秒，同
        /// <c>VfxOptions.FirstLoadTimeoutSeconds</c> 取值理由。
        /// </summary>
        public double FirstLoadTimeoutSeconds { get; set; } = 5.0;
    }
}
