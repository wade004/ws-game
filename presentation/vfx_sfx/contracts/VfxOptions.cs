using System.Collections.Generic;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary><see cref="Presentation.VfxSfx.Core.VfxPlayer"/>/<see cref="Presentation.VfxSfx.Core.VfxPool"/>
    /// 的策略配置项（见 09_表现层.md 第 5.4 节"池的容量...由引擎适配层实现，接口对上层透明"——
    /// 本模块把"容量"落地为一个可配置项，而不是硬编码常量，呼应该节"具体数值不在本架构拍板"）。</summary>
    public sealed class VfxOptions
    {
        /// <summary>每个 <see cref="VfxDef.Category"/> 分组的对象池容量上限；未在此登记的分类使用
        /// <see cref="DefaultPoolCapacity"/>。</summary>
        public IReadOnlyDictionary<string, int> PoolCapacityPerCategory { get; set; } =
            new Dictionary<string, int>();

        /// <summary>未在 <see cref="PoolCapacityPerCategory"/> 登记的分类使用的默认容量。</summary>
        public int DefaultPoolCapacity { get; set; } = 32;

        /// <summary><c>attach_mode: screen</c> 在 <see cref="Core.Foundation.EngineAdapter.ICamera.ScreenToWorld"/>
        /// 找不到交点时的兜底行为：true（默认）记诊断并跳过播放（返回 null 句柄）；见
        /// vfx_sfx/README.md 判断记录。</summary>
        public bool SkipOnUnresolvableScreenAttach { get; set; } = true;

        /// <summary>
        /// 外部审核阻塞项 4 收口（首次特效加载边界，见 <c>Presentation.VfxSfx.Core.VfxPlayer.Spawn</c>
        /// 判断记录）：某个 <c>vfx.def.resource_ref</c> 首次被引用、资源尚未加载完成时，一次排队
        /// 等待的最长秒数（按 <see cref="IVfxPlayer.Update"/> 的 <c>dt</c> 累计倒计时，不是墙钟时间——
        /// 与本模块其余时间量纲一致）；超时仍未加载完成则丢弃这次排队的播放请求并记一条诊断，不是
        /// 无限期等待。默认 5 秒——一次资源加载在正常网络/磁盘条件下应远快于此，超时基本只会在资源
        /// id 拼写错误、资源确实缺失等异常情况下触发，5 秒足够覆盖正常首帧加载抖动同时不会让异常
        /// 情况无限期占用内存。
        /// </summary>
        public double FirstLoadTimeoutSeconds { get; set; } = 5.0;
    }
}
