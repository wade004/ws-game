using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.VfxSfx.Contracts;

namespace Presentation.VfxSfx.Core
{
    /// <summary><see cref="IWeaponStyleResolver"/> 的最小实现（见 09_表现层.md 第 4.4 节）：按
    /// <c>weaponStyleRef</c> 查 <c>display.weapon_style</c>，再按技能 id 查
    /// <c>impact_vfx_override</c>/取 <c>swing_vfx</c>。</summary>
    public sealed class WeaponStyleResolver : IWeaponStyleResolver
    {
        private readonly IReadOnlyDictionary<Id, WeaponStyleDef> _catalog;

        public WeaponStyleResolver(IReadOnlyDictionary<Id, WeaponStyleDef> catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public Id? ResolveImpactVfxOverride(Id weaponStyleRef, Id skillId)
        {
            if (!_catalog.TryGetValue(weaponStyleRef, out var def))
            {
                return null;
            }

            return def.ImpactVfxOverride.TryGetValue(skillId, out var vfxId) ? vfxId : (Id?)null;
        }

        public Id? ResolveSwingVfx(Id weaponStyleRef) =>
            _catalog.TryGetValue(weaponStyleRef, out var def) ? def.SwingVfx : null;
    }
}
