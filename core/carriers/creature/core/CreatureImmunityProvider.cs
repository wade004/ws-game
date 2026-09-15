using System;
using Core.Carriers.Unit;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Carriers.Creature
{
    /// <summary>
    /// <see cref="IStaticImmunityProvider"/> 的默认实现（阶段 3 整理"事项三"）：读
    /// <see cref="CreatureUnit.Immunities"/>（07 第 2.1 节"免疫的学派/效果类型/控制类别"），供
    /// <c>core/rules/combat</c>/<c>core/rules/skill</c> 在光环免疫之外叠加查询。
    /// <para>
    /// <c>Immunities</c> 元素格式（07 原文只给出"List&lt;Id&gt;，免疫学派/效果类型/控制类别"，未
    /// 规定具体写法，本模块拍板补录，见 README）：
    /// <list type="bullet">
    /// <item><c>effect.&lt;kind&gt;</c>（<paramref name="kind"/> 取 <see cref="EffectKindNames"/>
    /// 的 snake_case 文本，如 <c>effect.school_damage</c>）——免疫该效果原语类型，不区分学派。</item>
    /// <item><c>control.&lt;flag&gt;</c>（<c>flag</c> 取 <c>no_move</c>/<c>no_cast</c>/
    /// <c>no_attack</c>/<c>no_interact</c> 之一，同 <c>AuraHost.ParseControlFlags</c> 惯例）——
    /// 静态免疫该单项控制标志，不参与 <see cref="IsImmune"/> 判定，只影响
    /// <see cref="GetControlImmunity"/>。</item>
    /// <item>其余取值一律按"学派 id"处理（如 <c>school.fire</c>）——免疫该学派下全部效果原语类型，
    /// 不区分具体 <paramref name="kind"/>（同 <c>AuraHost.IsImmune</c> 里
    /// "<c>ImmuneSchools</c> 命中即免疫，不看 <c>ImmuneEffectKinds</c>"的惯例，见该方法实现）。</item>
    /// <item><see cref="CreatureOptions.ImmunityTagPrefix"/> 配置的"整体控制免疫"标记（默认
    /// <c>immunity.control_immune</c>，由 <see cref="CreatureFactory.Spawn"/> 按
    /// <c>creature.tier_definition.control_immune</c> 写入）——命中时 <see cref="GetControlImmunity"/>
    /// 返回全部四个控制标志位（"tier control_immune → 全部控制免疫"），<see cref="IsControlCategoryImmune"/>
    /// 对任意类别查询都返回 true（"tier control_immune=true → 全部类别免疫"，旧布尔迁移等价语义，
    /// T-N3-6）；均不参与 <see cref="IsImmune"/> 判定。本类型的 <paramref name="controlImmuneMarker"/>
    /// 构造参数须与游戏层实际使用的 <see cref="CreatureOptions.ImmunityTagPrefix"/> 保持一致（缺省值
    /// 两边相同，游戏层改了前者的非默认值时需要把同一个值也传给本类型，两者没有共享同一份配置对象，
    /// 见构造函数判断记录）。</item>
    /// <item><c>control_category.&lt;category&gt;</c>（T-N3-6 新增，<c>category</c> 取
    /// <see cref="ControlCategoryValues"/> 六值之一，由 <see cref="CreatureFactory.Spawn"/> 按
    /// <c>creature.tier_definition.control_immune_categories</c> 逐条写入，见
    /// <see cref="ControlCategoryPrefix"/> 判断记录）——静态免疫该单项控制类别，只影响
    /// <see cref="IsControlCategoryImmune"/>，不参与 <see cref="IsImmune"/>/<see cref="GetControlImmunity"/>
    /// 判定（与 <c>control.&lt;flag&gt;</c> 是两个正交维度，互不影响）。</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class CreatureImmunityProvider : IStaticImmunityProvider
    {
        private const string EffectPrefix = "effect.";
        private const string ControlPrefix = "control.";

        /// <summary>T-N3-6 新增：<c>CreatureUnit.Immunities</c> 里按控制类别声明免疫的条目前缀
        /// （<c>control_category.&lt;category&gt;</c>，<c>category</c> 取
        /// <see cref="ControlCategoryValues"/> 六值之一），与既有 <see cref="ControlPrefix"/>
        /// （<c>control.&lt;flag&gt;</c>，按标志位声明）是两个正交维度——不复用同一前缀，避免
        /// "<c>control.stun</c>"这类写法在 <see cref="GetControlImmunity"/> 里被误当成未知标志位
        /// 静默吃掉（<see cref="ParseControlFlag"/> 对未识别取值返回 <see cref="ControlFlags.None"/>，
        /// 不报错也不提示，两套前缀混用会产生看似生效实则无声失败的内容错误）。<c>internal</c>：
        /// 供同程序集的 <see cref="CreatureFactory"/> 按
        /// <c>creature.tier_definition.control_immune_categories</c> 写入 <c>CreatureUnit.Immunities</c>
        /// 时复用同一前缀，避免两处各自硬编码字符串字面量后续漂移。</summary>
        internal const string ControlCategoryPrefix = "control_category.";

        private static readonly ControlFlags AllControlFlags =
            ControlFlags.NoMove | ControlFlags.NoCast | ControlFlags.NoAttack | ControlFlags.NoInteract;

        private readonly IWorldSim _world;
        private readonly Id _controlImmuneMarker;

        /// <summary>
        /// <paramref name="controlImmuneMarker"/>：判定"tier 全部控制免疫"的标记 Id，缺省
        /// <c>immunity.control_immune</c>——与 <see cref="CreatureOptions.ImmunityTagPrefix"/> 的
        /// 默认值一致（见类型顶部判断记录）。<paramref name="world"/> 用于按 <c>unitId</c> 取回
        /// <see cref="CreatureUnit"/> 实例（同 <c>CreatureFactory</c> 读取实体的方式），非
        /// <see cref="CreatureUnit"/>（如玩家单位、或单位已被销毁/不存在）一律按"不免疫"处理。
        /// </summary>
        public CreatureImmunityProvider(IWorldSim world, Id? controlImmuneMarker = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _controlImmuneMarker = controlImmuneMarker ?? new Id("immunity.control_immune");
        }

        public bool IsImmune(Id unitId, Id school, EffectKind kind)
        {
            var unit = _world.GetEntity(unitId) as CreatureUnit;
            if (unit == null)
            {
                return false;
            }

            var immunities = unit.Immunities;
            for (var i = 0; i < immunities.Count; i++)
            {
                var entry = immunities[i];
                if (entry.Equals(_controlImmuneMarker))
                {
                    continue;
                }

                var value = entry.Value;
                if (value.StartsWith(ControlPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                // T-N3-6：control_category.<category> 条目同 control.<flag> 一样只影响控制免疫判定
                // （本方法是效果原语类型/学派免疫，不是控制免疫），跳过、不落入下面"当学派 id 处理"
                // 分支（否则形如 school id == "control_category.stun" 这种几乎不可能发生但概念上
                // 错误的误判路径会存在）。
                if (value.StartsWith(ControlCategoryPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (value.StartsWith(EffectPrefix, StringComparison.Ordinal))
                {
                    if (EffectKindNames.TryParse(value.Substring(EffectPrefix.Length), out var parsedKind) &&
                        parsedKind == kind)
                    {
                        return true;
                    }
                    continue;
                }

                if (entry.Equals(school))
                {
                    return true;
                }
            }

            return false;
        }

        public ControlFlags GetControlImmunity(Id unitId)
        {
            var unit = _world.GetEntity(unitId) as CreatureUnit;
            if (unit == null)
            {
                return ControlFlags.None;
            }

            var result = ControlFlags.None;
            var immunities = unit.Immunities;
            for (var i = 0; i < immunities.Count; i++)
            {
                var entry = immunities[i];
                if (entry.Equals(_controlImmuneMarker))
                {
                    return AllControlFlags;
                }

                var value = entry.Value;
                if (value.StartsWith(ControlPrefix, StringComparison.Ordinal))
                {
                    result |= ParseControlFlag(value.Substring(ControlPrefix.Length));
                }
            }

            return result;
        }

        /// <summary>
        /// T-N3-6 新增：显式覆盖 <see cref="IStaticImmunityProvider.IsControlCategoryImmune"/> 的
        /// 默认实现（同 <see cref="InterfaceDefaultMemberForwardingTests"/> 门禁要求，见该接口成员
        /// 判断记录）。判定顺序：① <paramref name="unitId"/> 命中 <see cref="_controlImmuneMarker"/>
        /// （"tier control_immune=true"）→ 对任意 <paramref name="category"/> 都返回 true（旧布尔
        /// 迁移等价语义：全部类别免疫，不需要枚举具体类别，见类型顶部判断记录）；② 存在
        /// <c>control_category.&lt;category&gt;</c> 条目且 <paramref name="category"/> 精确匹配
        /// （<see cref="StringComparison.Ordinal"/>，不做大小写归一化，同 <c>kind</c>/<c>flag</c>
        /// 等既有取值一律 snake_case 精确比较的惯例）→ 返回 true；③ 其余（含未登记/拼写错误的
        /// <paramref name="category"/> 文本、或该单位完全没有任何控制免疫声明）→ 返回 false，即
        /// "未登记类别的控制视为不免疫"（06/ADR-0031 均未规定"未知类别默认免疫"这一相反语义，从严
        /// 按不免疫处理，避免内容拼写错误被静默放大为意外的全面免疫）。
        /// </summary>
        public bool IsControlCategoryImmune(Id unitId, string category)
        {
            var unit = _world.GetEntity(unitId) as CreatureUnit;
            if (unit == null)
            {
                return false;
            }

            var immunities = unit.Immunities;
            for (var i = 0; i < immunities.Count; i++)
            {
                var entry = immunities[i];
                if (entry.Equals(_controlImmuneMarker))
                {
                    return true;
                }

                var value = entry.Value;
                if (value.StartsWith(ControlCategoryPrefix, StringComparison.Ordinal) &&
                    string.Equals(value.Substring(ControlCategoryPrefix.Length), category, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static ControlFlags ParseControlFlag(string flag)
        {
            switch (flag)
            {
                case "no_move": return ControlFlags.NoMove;
                case "no_cast": return ControlFlags.NoCast;
                case "no_attack": return ControlFlags.NoAttack;
                case "no_interact": return ControlFlags.NoInteract;
                default: return ControlFlags.None;
            }
        }
    }
}
