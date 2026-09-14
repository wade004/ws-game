using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;

namespace Core.Numbers.Archetype
{
    /// <summary>
    /// <c>arch.class</c> 一行的只读视图（见 <see cref="ArchSchemas.Class"/>）。<see cref="BaseStats"/>
    /// 保持数据文件中 <c>base_stats</c> 字段的声明顺序，供 <see cref="ArchetypeRegistry.ApplyTo"/>
    /// 按确定顺序调用 <see cref="StatBaseWriter"/>（顺序本身不影响最终属性值，但便于测试断言、
    /// 也避免"顺序不确定"这种非确定性behaviour，见 11_工程规范与测试.md"确定性"相关要求）。
    /// </summary>
    public sealed class ClassDefinition
    {
        public Id Id { get; }

        public string NameKey { get; }

        public Id PrimaryStat { get; }

        public IReadOnlyList<KeyValuePair<string, double>> BaseStats { get; }

        public IReadOnlyList<Id> PowerTypes { get; }

        public Id? SkillBookRef { get; }

        public Id? TalentTreeRef { get; }

        public Id? LevelCurveRef { get; }

        /// <summary>T-N1-4（ADR-0030 决策 2"职业模板可覆盖派生系数（<c>arch.class.derivation_overrides</c>，
        /// 可选）"）：本职业登记的派生系数覆盖列表，元素为 <c>(目标派生属性, 来源属性, 覆盖系数)</c>
        /// 三元组，未登记 <c>derivation_overrides</c> 字段时为空列表（不是 null，与 <see
        /// cref="BaseStats"/>/<see cref="PowerTypes"/> 等其它列表字段同一"缺省即空集合"惯例）。经
        /// <see cref="ArchetypeRegistry.ApplyTo"/> 转发给 <see cref="DerivationCoefficientOverrideWriter"/>
        /// （若构造时注入了该委托），由属性宿主（<c>StatHost.SetDerivationCoefficientOverrides</c>）
        /// 实际生效——本模块自己不解释系数含义，只透传（同 <see cref="PowerTypes"/> 的既有分层选择）。
        /// </summary>
        public IReadOnlyList<(Id Stat, Id Source, double Coefficient)> DerivationOverrides { get; }

        public ClassDefinition(
            Id id,
            string nameKey,
            Id primaryStat,
            IReadOnlyList<KeyValuePair<string, double>> baseStats,
            IReadOnlyList<Id> powerTypes,
            Id? skillBookRef,
            Id? talentTreeRef,
            Id? levelCurveRef)
            : this(id, nameKey, primaryStat, baseStats, powerTypes, skillBookRef, talentTreeRef, levelCurveRef,
                  Array.Empty<(Id, Id, double)>())
        {
        }

        /// <summary>T-N1-4 新增重载：在既有八参构造之上追加 <see cref="DerivationOverrides"/>（ABI
        /// 门禁 G3 禁止给既有公开构造加可选参数，只能新增重载，见任务硬性规则）。既有八参构造改为
        /// 委托本构造并传空数组，行为完全不变，不产生任何调用点破坏。</summary>
        public ClassDefinition(
            Id id,
            string nameKey,
            Id primaryStat,
            IReadOnlyList<KeyValuePair<string, double>> baseStats,
            IReadOnlyList<Id> powerTypes,
            Id? skillBookRef,
            Id? talentTreeRef,
            Id? levelCurveRef,
            IReadOnlyList<(Id Stat, Id Source, double Coefficient)> derivationOverrides)
        {
            Id = id;
            NameKey = nameKey;
            PrimaryStat = primaryStat;
            BaseStats = baseStats;
            PowerTypes = powerTypes;
            SkillBookRef = skillBookRef;
            TalentTreeRef = talentTreeRef;
            LevelCurveRef = levelCurveRef;
            DerivationOverrides = derivationOverrides;
        }
    }

    /// <summary><c>arch.race</c> 一行的只读视图（见 <see cref="ArchSchemas.Race"/>）。
    /// <see cref="PassiveAuras"/> 由 <see cref="ArchetypeRegistry.ApplyTo"/> 经注入的
    /// <see cref="AuraApplier"/> 施加（W1 收边补齐，见该方法注释——此前"本模块只保存、不应用"的
    /// 前提是"L2 skill 尚未实现"，该前提已过期，<c>core/rules/skill</c> 目前已完整实现）；
    /// 未注入 <see cref="AuraApplier"/>（调用方未装配）时仍旧只保存不应用，向后兼容。</summary>
    public sealed class RaceDefinition
    {
        public Id Id { get; }

        public string NameKey { get; }

        public IReadOnlyList<KeyValuePair<string, double>> StatMods { get; }

        public IReadOnlyList<Id> PassiveAuras { get; }

        public RaceDefinition(
            Id id,
            string nameKey,
            IReadOnlyList<KeyValuePair<string, double>> statMods,
            IReadOnlyList<Id> passiveAuras)
        {
            Id = id;
            NameKey = nameKey;
            StatMods = statMods;
            PassiveAuras = passiveAuras;
        }
    }

    /// <summary>天赋树单个节点（见 <see cref="ArchSchemas.TalentTree"/>）。<see cref="Grants"/>
    /// 结构未知，由上层（L2 skill 一类消费方）自行解释，本模块只做透传（04 第 5 节
    /// <see cref="FieldKind.Object"/> 的既定处理方式）。</summary>
    public sealed class TalentNode
    {
        public string NodeId { get; }

        public IReadOnlyList<string> Prerequisites { get; }

        public int Cost { get; }

        public JsonObject Grants { get; }

        public TalentNode(string nodeId, IReadOnlyList<string> prerequisites, int cost, JsonObject grants)
        {
            NodeId = nodeId;
            Prerequisites = prerequisites;
            Cost = cost;
            Grants = grants;
        }
    }

    /// <summary><c>arch.talent_tree</c> 一行的只读视图。前置存在性与无环由
    /// <see cref="ArchTalentTreeCycleValidationRule"/> 在数据校验期保证，本类型不重复校验。</summary>
    public sealed class TalentTree
    {
        public Id Id { get; }

        public IReadOnlyList<TalentNode> Nodes { get; }

        public TalentTree(Id id, IReadOnlyList<TalentNode> nodes)
        {
            Id = id;
            Nodes = nodes;
        }
    }

    /// <summary><see cref="IArchetypeRegistry.ApplyTo"/> 的返回值：应用了哪个职业/种族、
    /// 该职业引用的等级曲线是谁（供调用方紧接着 <c>IProgressionHost.RegisterUnit</c>，
    /// 本模块不直接依赖 progression 模块的具体类型，只透传 Id）。</summary>
    public sealed class AppliedArchetype
    {
        public Id ClassId { get; }

        public Id? RaceId { get; }

        public Id? LevelCurveRef { get; }

        public AppliedArchetype(Id classId, Id? raceId, Id? levelCurveRef)
        {
            ClassId = classId;
            RaceId = raceId;
            LevelCurveRef = levelCurveRef;
        }
    }
}
