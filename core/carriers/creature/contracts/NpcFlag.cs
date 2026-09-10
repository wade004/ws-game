using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;

namespace Core.Carriers.Creature
{
    /// <summary>
    /// 生物职能标志位（见 07_载体层_物品生物物件.md 第 2.2 节）：固定六值，新增一种职能标志需过
    /// 审批（07 原文"标志位是新增原语受控开口之一"）。取值与顺序照抄 07 第 2.2 节表格行序。
    /// </summary>
    public enum NpcFlag
    {
        Vendor,
        Trainer,
        Questgiver,
        Gossip,
        SavePoint,
        SummonOnly,
    }

    /// <summary>
    /// <see cref="NpcFlag"/> 与 <c>creature.template.npc_flags</c> 数据表使用的 <see cref="Id"/> 互转
    /// （惯例同 <c>Core.Rules.Common.EffectKindNames</c>：数据表一律用 Id 格式文本，运行期代码一律用
    /// 强类型枚举，本类是两者之间唯一的转换点）。
    /// <para>
    /// 判断记录：07 第 2.1 节只给出 <c>npc_flags: List&lt;Id&gt;</c> 的类型记法，未规定具体 Id 取值
    /// 格式；本模块选定 <c>npc_flag.&lt;name&gt;</c>（如 <c>npc_flag.vendor</c>）作为数据表侧写法，
    /// 另按 07 "召唤物/宠物跟随" 与任务书拍板，把每个标志额外映射为一个单位标签
    /// <c>tag.npc.&lt;name&gt;</c>（见 <see cref="ToTag"/>），供 <c>Core.Rules.Common.IUnitAccess.GetTags</c>
    /// 一类按标签过滤的场景使用（标签命名空间与数据表 Id 命名空间分开，避免"标签"与"数据引用"
    /// 两种不同用途的字符串共用同一前缀造成误用）。
    /// </para>
    /// <para>
    /// 消费方反馈第 28 条（<c>ToTag</c> 消费方核实）：<see cref="ToTag"/> 并非死代码——
    /// <c>core/carriers/creature/core/CreatureFactory.cs</c> 的 <c>Spawn</c> 方法在生成单位时，对
    /// <c>CreatureTemplate.NpcFlags</c> 的每一项都调用本方法把标志写入 <c>unit.Tags</c>
    /// （<c>unit.Tags.Add(NpcFlagIds.ToTag(flag))</c>），供 <c>IUnitAccess.GetTags</c> 一类按标签过滤
    /// 索敌/交互目标的场景消费（如排除/筛选贩卖商、训练师等 NPC）；<c>ToId</c> 则是
    /// <c>creature.template.npc_flags</c> 数据表侧读写用的 Id 格式，两者服务不同的两端，见类型顶部
    /// 判断记录。
    /// </para>
    /// <para>
    /// 消费方反馈第 28 条（固定取值登记）：<see cref="All"/> 是六个合法职能标志 Id 的有序集合，供
    /// <c>CreatureSchemas.Template</c> 的 <c>npc_flags</c> 字段登记
    /// <see cref="FieldSchema.WithAllowedValues"/>——<c>DataRegistry</c> 加载期据此校验每个取值
    /// （<c>field_allowed_value</c> 检查项），不再需要 <c>CreatureContentValidationRule</c> 手写重复
    /// 校验（见该类型判断记录）。
    /// </para>
    /// </summary>
    public static class NpcFlagIds
    {
        private static readonly IReadOnlyDictionary<NpcFlag, Id> ToIdMap = new Dictionary<NpcFlag, Id>
        {
            [NpcFlag.Vendor] = new Id("npc_flag.vendor"),
            [NpcFlag.Trainer] = new Id("npc_flag.trainer"),
            [NpcFlag.Questgiver] = new Id("npc_flag.questgiver"),
            [NpcFlag.Gossip] = new Id("npc_flag.gossip"),
            [NpcFlag.SavePoint] = new Id("npc_flag.save_point"),
            [NpcFlag.SummonOnly] = new Id("npc_flag.summon_only"),
        };

        private static readonly IReadOnlyDictionary<NpcFlag, Id> ToTagMap = new Dictionary<NpcFlag, Id>
        {
            [NpcFlag.Vendor] = new Id("tag.npc.vendor"),
            [NpcFlag.Trainer] = new Id("tag.npc.trainer"),
            [NpcFlag.Questgiver] = new Id("tag.npc.questgiver"),
            [NpcFlag.Gossip] = new Id("tag.npc.gossip"),
            [NpcFlag.SavePoint] = new Id("tag.npc.save_point"),
            [NpcFlag.SummonOnly] = new Id("tag.npc.summon_only"),
        };

        private static readonly IReadOnlyDictionary<string, NpcFlag> FromIdMap = BuildReverse(ToIdMap);

        /// <summary>六个合法职能标志 Id 的有序集合（顺序照抄 <see cref="NpcFlag"/> 枚举声明顺序，即
        /// 07 第 2.2 节表格行序），供 <c>CreatureSchemas.Template.npc_flags</c> 登记
        /// <see cref="FieldSchema.WithAllowedValues"/>，见类型顶部判断记录。</summary>
        public static readonly IReadOnlyList<Id> All = new[]
        {
            ToIdMap[NpcFlag.Vendor],
            ToIdMap[NpcFlag.Trainer],
            ToIdMap[NpcFlag.Questgiver],
            ToIdMap[NpcFlag.Gossip],
            ToIdMap[NpcFlag.SavePoint],
            ToIdMap[NpcFlag.SummonOnly],
        };

        private static Dictionary<string, NpcFlag> BuildReverse(IReadOnlyDictionary<NpcFlag, Id> map)
        {
            var reverse = new Dictionary<string, NpcFlag>(StringComparer.Ordinal);
            foreach (var pair in map)
            {
                reverse[pair.Value.Value] = pair.Key;
            }
            return reverse;
        }

        /// <summary>枚举值 → 数据表侧 <c>npc_flag.&lt;name&gt;</c> Id。全部取值都在映射表中，不会失败。</summary>
        public static Id ToId(NpcFlag flag) => ToIdMap[flag];

        /// <summary>枚举值 → 单位标签 <c>tag.npc.&lt;name&gt;</c> Id（见类型注释判断记录）。</summary>
        public static Id ToTag(NpcFlag flag) => ToTagMap[flag];

        /// <summary>数据表侧 Id → 枚举值；未登记的 Id 返回 false，不抛异常（供内容校验规则使用）。</summary>
        public static bool TryParse(Id id, out NpcFlag flag) => FromIdMap.TryGetValue(id.Value, out flag);

        /// <summary><paramref name="id"/> 是否是六个合法职能标志 Id 之一。</summary>
        public static bool IsValid(Id id) => FromIdMap.ContainsKey(id.Value);
    }
}
