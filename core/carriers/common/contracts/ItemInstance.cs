using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;

namespace Core.Carriers.Common
{
    /// <summary>
    /// 一个运行期物品实例（见 10 第 2.2 节"<c>ItemInstance</c> 是一条独立结构（实例 id、模板 id、
    /// 堆叠数、实例级词缀/耐久等可选字段）"、07 第 1.3 节 <c>InventoryHost.listItems</c>）。
    /// <see cref="Extra"/> 是 07 第 1.6 节"可选扩展点"（耐久 <c>durability</c>、随机属性等）的落地
    /// 位置：这些扩展点本版不展开强类型字段，统一收进一个自由形状的 <see cref="JsonObject"/>，默认
    /// 空对象，避免未来加入某个扩展点时改动本类型签名。
    /// <para>
    /// T-N2-7（ADR-0032 决策 8"物品实例只存身份"；10 第 2.5 节修订段）：物品实例只存实例 id、模板
    /// id、堆叠数、<see cref="Quality"/>、<see cref="Affixes"/> 五项身份数据，**不存任何算出的属性
    /// 数值**（护甲值、词缀反解出的属性点数等）——这些数值读档/装备时由 <see
    /// cref="Core.Carriers.Item.EquipmentHost"/> 按 <c>item.template</c>/<c>item.affix</c> 经预算反解
    /// 重新算出（见该类型 <c>ApplyArmorValue</c>/<c>ApplyAffixValues</c> 判断记录），联机时各端按
    /// 本地数据重算、不信任对端数值。<see cref="Quality"/> 不是 <c>Id?</c>——ADR-0032 决策 8 原文
    /// "物品实例存……品质"，品质与 <see cref="InstanceId"/>/<see cref="TemplateId"/> 同级，是恒定
    /// 存在的身份字段，不是像 <see cref="Extra"/> 那样的可选扩展点；本仓库内全部创建/读档路径（<see
    /// cref="Core.Carriers.Item.InventoryHost"/> 的 <c>AddItemCore</c>/<c>ResolveTemplateQuality</c>、
    /// <see cref="Core.Carriers.Item.ItemInstanceJson"/> 的 <c>FromJson</c>）均已改为在构造/解析那一
    /// 刻就解析出具体品质（缺省取模板自身 <c>quality</c> 字段），不会让"未解析"状态（<c>Quality.Value
    /// == null</c>，只有旧 4 参构造函数——未指定品质时——才会产生）流出本类型边界进入正常运行时。
    /// </para>
    /// </summary>
    public readonly struct ItemInstance
    {
        private static readonly JsonObject EmptyExtra = new JsonObjectBuilder().Build();
        private static readonly IReadOnlyList<Id> EmptyAffixes = Array.Empty<Id>();

        public Id InstanceId { get; }

        public Id TemplateId { get; }

        public int Count { get; }

        /// <summary>T-N2-7 新增：该实例的品质引用（<c>item.quality_definition</c> 表 id）。见类型顶部
        /// 判断记录"Quality 不是 Id?"。</summary>
        public Id Quality { get; }

        /// <summary>T-N2-7 新增：该实例携带的词缀引用列表（<c>item.affix</c> 表 id），不可变、默认为
        /// 空列表（不是 null）。不存任何算出的属性数值（见类型顶部判断记录）——穿戴时经 <see
        /// cref="Core.Carriers.Item.EquipmentHost"/> 按 <see cref="Quality"/>/本字段重新反解写入 <see
        /// cref="Core.Numbers.StatBlock.IStatHost"/>。</summary>
        public IReadOnlyList<Id> Affixes { get; }

        /// <summary>预留扩展字段（耐久等，见类型注释）；未提供时为一个空 <see cref="JsonObject"/>
        /// （不是 null）。</summary>
        public JsonObject Extra { get; }

        /// <summary>旧 4 参构造函数（T-N2-7 之前的既有签名，原样保留，ABI 不允许删除/改参数）——
        /// <see cref="Quality"/> 为"未解析"状态（<c>Value == null</c>）、<see cref="Affixes"/> 为空
        /// 列表。仓库内部全部调用点已改用下方新增的重载显式传入品质（见类型顶部判断记录）；本构造
        /// 函数继续存在仅为兼容尚未重新编译的外部调用方。</summary>
        public ItemInstance(Id instanceId, Id templateId, int count, JsonObject? extra = null)
            : this(instanceId, templateId, count, default, null, extra)
        {
        }

        /// <summary>T-N2-7 新增重载：附带品质/词缀身份构造（ABI：新增重载而非给旧构造函数追加参数，
        /// 同 <c>core/carriers/item/README.md</c> 判断记录"ABI（构造函数新增重载……）"一贯做法）。</summary>
        public ItemInstance(
            Id instanceId, Id templateId, int count, Id quality, IReadOnlyList<Id>? affixes, JsonObject? extra = null)
        {
            InstanceId = instanceId;
            TemplateId = templateId;
            Count = count;
            Quality = quality;
            Affixes = affixes ?? EmptyAffixes;
            Extra = extra ?? EmptyExtra;
        }

        /// <summary>该实例的不透明引用句柄（见 <see cref="ItemInstanceRef"/>）。</summary>
        public ItemInstanceRef ToRef() => new ItemInstanceRef(InstanceId);
    }
}
