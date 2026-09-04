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
    }
}
