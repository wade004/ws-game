using System;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;

namespace Presentation.VfxSfx.Core
{
    /// <summary>
    /// 经 DisplayInfo 把逻辑 id 映射到具体 vfx_id/sfx_id（见 09_表现层.md 第 5.6 节
    /// "逻辑层触发...只携带逻辑 id...表现层经 DisplayInfo 查表得到具体 vfx_id/sfx_id 后再调用
    /// 播放器接口"）。
    /// <para>
    /// 判断记录（<c>slot</c> 参数，任务书拍板签名 <c>ResolveVfx(Id logicalId, string slot="default")</c>）：
    /// <see cref="Core.Foundation.DisplayInfo.DisplayInfo"/>（04 第 7.1 节）的 <c>vfx_id</c>/
    /// <c>sfx_id</c> 是单值字段，不是"槽位 → id"的映射，因此当前 <c>display.map</c> 数据形状下
    /// 只有 <c>"default"</c> 一个槽位有意义；<paramref name="slot"/> 非 <c>"default"</c> 时按
    /// "未登记该槽位"处理返回 null（不是错误——为未来 <c>display.map</c> 扩展出多槽位 vfx/sfx
    /// 预留调用方签名，槽位本身的多值存储不属于本任务范围）。
    /// </para>
    /// </summary>
    public sealed class DisplayInfoResolver
    {
        public const string DefaultSlot = "default";

        private readonly IDisplayInfoRegistry _registry;

        public DisplayInfoResolver(IDisplayInfoRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>按逻辑 id（技能/光环/物品/生物/物件模板 id）取 <c>display.map.vfx_id</c>；
        /// 未登记该逻辑 id 或该行未声明 <c>vfx_id</c> 时返回 null。</summary>
        public Id? ResolveVfx(Id logicalId, string slot = DefaultSlot)
        {
            if (slot != DefaultSlot)
            {
                return null;
            }

            return _registry.Lookup(logicalId)?.VfxId;
        }

        /// <summary>按逻辑 id 取 <c>display.map.sfx_id</c>；未登记或未声明时返回 null。</summary>
        public Id? ResolveSfx(Id logicalId, string slot = DefaultSlot)
        {
            if (slot != DefaultSlot)
            {
                return null;
            }

            return _registry.Lookup(logicalId)?.SfxId;
        }
    }
}
