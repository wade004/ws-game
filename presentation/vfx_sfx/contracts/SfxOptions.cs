using System.Collections.Generic;
using Core.Foundation.Common;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary><see cref="Presentation.VfxSfx.Core.SfxPlayer"/> 的策略配置项（见 09_表现层.md
    /// 第 5.5 节"同层音效遵守 priority 抢占规则...口味相关的抢占阈值为可配置项"）。</summary>
    public sealed class SfxOptions
    {
        /// <summary>每个 <see cref="SfxDef.Layer"/> 分组同时并发播放的实例数上限；超出时停掉该层
        /// 当前优先级最低（<see cref="SfxDef.Priority"/> 数值越大视为优先级越高，未声明按最低
        /// 优先级处理，见类型 <see cref="Presentation.VfxSfx.Core.SfxPlayer"/> 判断记录；勘误：此前
        /// 本注释误写为"数值越小越高"，与实现方向相反，以实现为准）的一个。
        /// 未在 <see cref="MaxConcurrentPerLayer"/> 登记的层使用 <see cref="DefaultMaxConcurrent"/>。
        /// 只计入"仍可能在播"的实例：一次性（非循环）音效开始播放满
        /// <see cref="OneShotLayerSlotHoldSeconds"/> 后视为已自然播完，不再占用名额（ADR-0105）。</summary>
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

        /// <summary>
        /// ADR-0105 新增（消费方反馈第五十四批）：一次性（<c>sfx.def.loop</c> 未声明/为 false）音效从
        /// 真正开始播放（<c>IAudio.PlaySfx</c> 被调用，含冷加载完成后的补播放）起，在所属层的并发
        /// 名额里最多占用多少秒；到期后视为已自然播完，从该层记账中释放（不调用
        /// <c>IAudio.StopSfx</c>，若实际仍在播也不会被掐断），不再参与 <see cref="MaxConcurrentPerLayer"/>/
        /// <see cref="DefaultMaxConcurrent"/> 的计数与同层抢占选择。
        /// <para>
        /// 判断记录：<c>IAudio</c> 契约没有"某个句柄是否已播完"的查询，此前记账只在显式停止/被抢占时
        /// 释放，已经自然播完的一次性音效永久占着名额——层一旦累计播满上限就"永远满员"，此后每次
        /// 播放都要抢占一个，同层里优先级最低的新音效会被几毫秒后到来的更高优先级音效当场停掉。计时
        /// 按 <see cref="Presentation.VfxSfx.Contracts.ISfxPlayer.Update"/> 传入的 <c>dt</c> 累计
        /// （生产装配经 <c>PresentationAssembly.UpdatePlaybackMaintenance</c> 逐帧驱动，不读系统时间），
        /// 从未驱动 <c>Update</c> 时不会到期，行为与本项新增前一致。循环音效不受本项影响（显式停止前
        /// 一直占名额）。默认 2 秒：覆盖常见的一次性战斗/界面音效时长；比它更长的一次性音效只是在
        /// 尾段不再计数（上限在尾段变松），不会被提前停止。取值 ≤ 0 表示不释放（回到本项新增前"只在
        /// 显式停止/被抢占时释放"的行为）。
        /// </para>
        /// </summary>
        public double OneShotLayerSlotHoldSeconds { get; set; } = 2.0;
    }
}
