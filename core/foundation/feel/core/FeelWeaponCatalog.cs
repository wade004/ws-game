using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.Feel
{
    /// <summary><c>feel.weapon.timeline_reference</c>（试调起点参考数值，手感设计/05 第 9 节）；缺项为 null。</summary>
    public sealed class FeelWeaponTimelineReference
    {
        public double? StartupMs { get; }

        public double? ActiveMs { get; }

        public double? RecoveryMs { get; }

        public FeelWeaponTimelineReference(double? startupMs, double? activeMs, double? recoveryMs)
        {
            StartupMs = startupMs;
            ActiveMs = activeMs;
            RecoveryMs = recoveryMs;
        }
    }

    /// <summary>
    /// <c>feel.weapon</c> 一行里换装链需要、但不在 <see cref="FeelRow"/>（只含分层写入）里的非分层字段：武器族、
    /// 普通攻击时间线引用、参考节奏。
    /// </summary>
    public sealed class FeelWeaponInfo
    {
        /// <summary>武器行 id（<c>feel.weapon.*</c>）。</summary>
        public string Ref { get; }

        /// <summary>武器族（姿势集回落链的维度，手感设计/04 第 2 节）。</summary>
        public string Family { get; }

        /// <summary>普通攻击的时间线技能引用（软引用 <c>skill.def</c>）；未声明为 null（保持既有挥击计时）。</summary>
        public Id? AutoAttackTimelineRef { get; }

        public FeelWeaponTimelineReference? TimelineReference { get; }

        public FeelWeaponInfo(string weaponRef, string family, Id? autoAttackTimelineRef, FeelWeaponTimelineReference? timelineReference)
        {
            Ref = weaponRef ?? throw new ArgumentNullException(nameof(weaponRef));
            Family = family ?? string.Empty;
            AutoAttackTimelineRef = autoAttackTimelineRef;
            TimelineReference = timelineReference;
        }
    }

    /// <summary>
    /// <c>feel.weapon</c> 非分层字段的只读目录（手感设计/08 第 1 节换装链：武器 → 武器族 → 姿势家族；武器 →
    /// 普通攻击时间线）。判断记录（为什么不并入 <see cref="FeelProfileSet"/>）：解析器只关心分层写入，
    /// 武器族与普攻时间线引用是"换装链"的输入而不是解析输入，放进 <see cref="FeelRow"/> 会让解析器多背一份
    /// 它永远不读的字段；本目录构造时一次性读出全部 <c>feel.weapon</c> 行（行内容在数据热加载前不变，
    /// 热加载后重新构造即可）。表不存在视为空目录。
    /// </summary>
    public sealed class FeelWeaponCatalog
    {
        private readonly Dictionary<string, FeelWeaponInfo> _byRef = new Dictionary<string, FeelWeaponInfo>(StringComparer.Ordinal);

        public FeelWeaponCatalog(IDataRegistryView registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (!registry.TryGetAll(FeelTables.Weapon, out var records))
            {
                return;
            }

            foreach (var record in records)
            {
                var family = record.TryGetString("family", out var f) ? f : string.Empty;
                Id? auto = record.TryGetId("auto_attack_timeline_ref", out var a) ? a : (Id?)null;
                FeelWeaponTimelineReference? reference = null;
                if (record.TryGetObject("timeline_reference", out var obj))
                {
                    reference = new FeelWeaponTimelineReference(
                        Number(obj, "startup_ms"), Number(obj, "active_ms"), Number(obj, "recovery_ms"));
                }

                _byRef[record.Key] = new FeelWeaponInfo(record.Key, family, auto, reference);
            }
        }

        /// <summary>全部武器行 id（序数序，保证遍历确定）。</summary>
        public IReadOnlyList<string> Refs
        {
            get
            {
                var list = new List<string>(_byRef.Keys);
                list.Sort(StringComparer.Ordinal);
                return list;
            }
        }

        public bool TryGet(string? weaponRef, out FeelWeaponInfo info)
        {
            if (weaponRef != null && _byRef.TryGetValue(weaponRef, out var found))
            {
                info = found;
                return true;
            }

            info = null!;
            return false;
        }

        private static double? Number(JsonObject obj, string field)
        {
            for (var i = 0; i < obj.Count; i++)
            {
                if (string.Equals(obj[i].Key, field, StringComparison.Ordinal) && obj[i].Value is JsonNumber n)
                {
                    return n.Value;
                }
            }

            return null;
        }
    }
}
