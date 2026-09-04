using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;

namespace Tests.Presentation.FeedbackBinder
{
    /// <summary>最小 <see cref="IDisplayInfoRegistry"/> 假实现：按逻辑 id 直接查字典，不接触
    /// <see cref="Core.Foundation.DataRegistry.DataRegistry"/>（该完整路径已在
    /// <c>Tests.Presentation.VfxSfx.DisplayInfoResolverTests</c> 覆盖，本模块的测试只关心
    /// "FeedbackBinder 是否正确调用了 DisplayInfoResolver"，不重复验证 DisplayInfo 加载管线）。</summary>
    internal sealed class FakeDisplayInfoRegistry : IDisplayInfoRegistry
    {
        private readonly Dictionary<Id, DisplayInfo> _byLogicalId;

        public FakeDisplayInfoRegistry(Dictionary<Id, DisplayInfo> byLogicalId)
        {
            _byLogicalId = byLogicalId;
        }

        public DisplayInfo? Lookup(Id logicalId) => _byLogicalId.TryGetValue(logicalId, out var v) ? v : null;

        public IReadOnlyList<DisplayInfo> LookupByCategory(DisplayCategory category)
        {
            var result = new List<DisplayInfo>();
            foreach (var v in _byLogicalId.Values)
            {
                if (v.Category == category) result.Add(v);
            }
            return result;
        }

        public IReadOnlyList<DisplayInfo> All => new List<DisplayInfo>(_byLogicalId.Values);

        public void Reload()
        {
        }

        public static DisplayInfo Simple(Id id, Id logicalId, DisplayCategory category, Id? vfxId, Id? sfxId) =>
            new DisplayInfo(
                id, category, logicalId, DisplayKind.Sprite, iconId: null, vfxId: vfxId, sfxId: sfxId,
                scale: 1.0, shadow: ShadowMode.Blob, sortOffset: 0, weaponStyleRef: null, sprite: null, model: null);
    }
}
