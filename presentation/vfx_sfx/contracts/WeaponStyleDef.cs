using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Presentation.VfxSfx.Contracts
{
    /// <summary><c>display.weapon_style</c> 一条记录的不可变运行期视图（见 09_表现层.md 第 4.4 节
    /// 字段表）。本模块只登记 schema 并提供 <see cref="Presentation.VfxSfx.Core.WeaponStyleResolver"/>
    /// 覆盖命中特效查询这一最小实现（见 vfx_sfx/README.md"不负责什么"）；<c>auto_attack_anim</c>/
    /// <c>cast_anim_override</c> 属于 CharacterRig/动画状态机职责范围（09 第 4 节），本模块只
    /// 如实携带这两个字段供上游模块使用，不解释其语义。</summary>
    public sealed class WeaponStyleDef
    {
        public Id Id { get; }

        /// <summary>普攻动作剪辑引用。</summary>
        public Id AutoAttackAnim { get; }

        /// <summary>按技能 id 覆盖施法动作剪辑；未声明视为空字典。</summary>
        public IReadOnlyDictionary<Id, Id> CastAnimOverride { get; }

        /// <summary>
        /// 按技能 id 声明"释放动作"剪辑（ADR-0174）；未声明视为空字典。声明了的技能：①读条型（<c>cast_time &gt; 0</c>）——读条期间
        /// <see cref="CastAnimOverride"/> 的剪辑<b>循环</b>播放，读条正常完成时播一遍本剪辑再回落，被打断/失败直接回落；
        /// ②瞬发型——施放时直接播本剪辑（取代普攻剪辑 <see cref="AutoAttackAnim"/>）。没声明的技能行为不变（读条剪辑播一遍、瞬发用普攻剪辑）。
        /// </summary>
        public IReadOnlyDictionary<Id, Id> ReleaseAnimOverride { get; }

        /// <summary>挥舞轨迹特效，指向 <c>vfx.def</c>；可选。</summary>
        public Id? SwingVfx { get; }

        /// <summary>按技能 id 覆盖命中特效，指向 <c>vfx.def</c>；未声明视为空字典。</summary>
        public IReadOnlyDictionary<Id, Id> ImpactVfxOverride { get; }

        public WeaponStyleDef(
            Id id,
            Id autoAttackAnim,
            IReadOnlyDictionary<Id, Id>? castAnimOverride,
            Id? swingVfx,
            IReadOnlyDictionary<Id, Id>? impactVfxOverride)
            : this(id, autoAttackAnim, castAnimOverride, swingVfx, impactVfxOverride, null)
        {
        }

        /// <summary>带释放动作覆盖（ADR-0174）的构造重载；<paramref name="releaseAnimOverride"/> 为 null 与五参构造逐位一致。</summary>
        public WeaponStyleDef(
            Id id,
            Id autoAttackAnim,
            IReadOnlyDictionary<Id, Id>? castAnimOverride,
            Id? swingVfx,
            IReadOnlyDictionary<Id, Id>? impactVfxOverride,
            IReadOnlyDictionary<Id, Id>? releaseAnimOverride)
        {
            Id = id;
            AutoAttackAnim = autoAttackAnim;
            CastAnimOverride = castAnimOverride ?? new Dictionary<Id, Id>();
            SwingVfx = swingVfx;
            ImpactVfxOverride = impactVfxOverride ?? new Dictionary<Id, Id>();
            ReleaseAnimOverride = releaseAnimOverride ?? new Dictionary<Id, Id>();
        }

        /// <summary>从一条已加载的 <c>display.weapon_style</c> <see cref="DataRecord"/> 构造。</summary>
        public static WeaponStyleDef FromRecord(DataRecord record)
        {
            var id = record.GetId("id");
            var autoAttackAnim = record.GetId("auto_attack_anim");
            var swingVfx = record.TryGetId("swing_vfx", out var swingVal) ? (Id?)swingVal : null;
            var castAnimOverride = ParseIdMap(record, "cast_anim_override");
            var impactVfxOverride = ParseIdMap(record, "impact_vfx_override");
            var releaseAnimOverride = ParseIdMap(record, "release_anim_override");
            return new WeaponStyleDef(id, autoAttackAnim, castAnimOverride, swingVfx, impactVfxOverride, releaseAnimOverride);
        }

        private static Dictionary<Id, Id> ParseIdMap(DataRecord record, string field)
        {
            var result = new Dictionary<Id, Id>();
            if (!record.TryGetObject(field, out var obj))
            {
                return result;
            }

            foreach (var kv in obj)
            {
                if (!Id.TryParse(kv.Key, out var keyId))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, field, $"键 \"{kv.Key}\" 不是合法 Id");
                }
                if (!(kv.Value is JsonString valueStr) || !Id.TryParse(valueStr.Value, out var valueId))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, field, $"键 \"{kv.Key}\" 的值不是合法 Id 字符串");
                }
                result[keyId] = valueId;
            }

            return result;
        }
    }
}
