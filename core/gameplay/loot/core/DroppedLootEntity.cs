using System;
using System.Collections.Generic;
using System.Linq;
using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;

namespace Core.Gameplay.Loot
{
    /// <summary>掉落在地面的拾取物运行期实体（见 05 第 1.6 节 <c>DroppedLoot</c> 字段表）。</summary>
    public sealed class DroppedLootEntity : Entity
    {
        /// <summary>
        /// H4 补齐（表现层外形缺口）：掉落物固定复用同一个"地面拾取物外观"逻辑 id，不区分具体
        /// 物品内容（<see cref="Items"/> 可能是任意堆叠组合，没有单一"这一堆掉落物长什么样"的天然
        /// 落点——同 gobj 宝箱一律用同一个外观，不因箱内物品不同而换外观）。<see cref="LootHost.Drop"/>
        /// 把每个新建的 <see cref="DroppedLootEntity"/> 的 <see cref="Entity.TemplateId"/> 设为本常量
        /// （见该方法），<see cref="Core.Foundation.SimLoop.WorldSim.AddEntity"/> 才能算出一个非空、
        /// 稳定的 <c>displayId</c>（<c>entity.TemplateId ?? entity.EntityId</c>，见该方法注释）——
        /// 此前 <see cref="Entity.TemplateId"/> 从未被设置，回退到逐实例不同的
        /// <see cref="Entity.EntityId"/>（如 <c>"loot.inst_1"</c>），不可能有任何 <c>display.map</c>
        /// 静态数据行与之匹配，导致 <c>UnityViewFactory</c> 每次都退化为空视图并记警告（H3a 如实记录
        /// 的缺口 2）。游戏数据集需要为本 id 登记一条 <c>display.map</c> 行（见
        /// <c>data/_sample/display/display.map.json</c> 新增的
        /// <c>display.map.sample_loot_pile</c> 行）。
        /// </summary>
        public static readonly Id GenericDisplayTemplateId = new Id("loot.generic_pile");

        public override string Kind => EntityKinds.Loot;

        /// <summary>掉落物内容（见 05 第 1.6 节 <c>items</c>）；<see cref="LootHost.PickUp"/> 会随着
        /// 部分拾取逐步移除已拾走的堆叠，全部拾完后本列表为空并触发实体销毁。</summary>
        public List<ItemStack> Items { get; }

        /// <summary>
        /// T-N2-8 新增（ADR-0032 决策 7/8；08 第 1.1 节修订段）：与 <see cref="Items"/> 按下标一一
        /// 对应的身份信息（品质骰/词缀骰结果、来源折算的物品等级）——<see cref="LootHost.Drop(Id, Vec2,
        /// IReadOnlyList{ItemStack}, Id?)"/>/<see cref="LootHost.Drop(Id, Vec2,
        /// IReadOnlyList{LootRollOutcome}, Id?)"/>/<see cref="DroppedLootPersistable"/> 三处内部调用
        /// 路径总是让本列表与 <see cref="Items"/> 等长（见 <see cref="LootHost.ResolveDefaultOutcome"/>
        /// 判断记录）；本类型自身（仅 4 参、既有 ABI 不变的构造函数）不做该保证——理论上绕过
        /// <see cref="LootHost"/> 直接调用旧构造函数的外部代码拿到的是空列表，消费方需要按下标访问前
        /// 自行核对 <c>Outcomes.Count == Items.Count</c>（同 <see cref="Core.Carriers.Common.
        /// LootRollOutcome"/> 判断记录"允许‘未解析’"的一贯口径：宁可留一个空集合等消费方兜底，不在
        /// 本类型内部编造缺乏依据的默认值——本类型没有 <c>DataRegistry</c> 可查模板缺省）。
        /// </summary>
        public IReadOnlyList<Core.Carriers.Common.LootRollOutcome> Outcomes { get; private set; }

        /// <summary>单人拾取规则的归属提示（见 05 第 1.6 节 <c>ownerHint</c>）：本架构单机场景无实际
        /// 分配意义，仅为兼容掉落生成管线数据结构而保留（08 第 1.2 节原文）。</summary>
        public Id? OwnerHint { get; set; }

        /// <summary>过期时间（游戏内时间，见 05 第 1.6 节 <c>expireAt</c>）；null 表示不过期。</summary>
        public double? ExpireAt { get; set; }

        private static readonly IReadOnlyList<Core.Carriers.Common.LootRollOutcome> EmptyOutcomes =
            Array.Empty<Core.Carriers.Common.LootRollOutcome>();

        public DroppedLootEntity(Id entityId, Id mapId, IEnumerable<ItemStack> items, Id? ownerHint, double? expireAt)
            : this(entityId, mapId, items, null, ownerHint, expireAt)
        {
        }

        /// <summary>T-N2-8 新增重载（ABI 硬性规则"只允许新增"，不改既有 5 参构造函数签名）：额外接受
        /// <see cref="Outcomes"/>；<paramref name="outcomes"/> 为 <c>null</c> 时退化为空列表（见该
        /// 属性判断记录，与旧构造函数行为一致）。</summary>
        public DroppedLootEntity(
            Id entityId, Id mapId, IEnumerable<ItemStack> items,
            IEnumerable<Core.Carriers.Common.LootRollOutcome>? outcomes, Id? ownerHint, double? expireAt)
            : base(entityId, mapId)
        {
            Items = new List<ItemStack>(items ?? Array.Empty<ItemStack>());
            Outcomes = outcomes != null ? new List<Core.Carriers.Common.LootRollOutcome>(outcomes) : EmptyOutcomes;
            OwnerHint = ownerHint;
            ExpireAt = expireAt;
        }

        /// <summary>供 <see cref="LootHost.RestoreDropped"/> 整体替换 <see cref="Outcomes"/>（读档时
        /// "原地覆写已存活实体"分支，同该方法对 <see cref="Items"/> 的 Clear+AddRange 惯例，只是
        /// <see cref="Outcomes"/> 是整体替换而不是就地可变列表）。</summary>
        internal void ReplaceOutcomes(IReadOnlyList<Core.Carriers.Common.LootRollOutcome> outcomes) =>
            Outcomes = outcomes ?? EmptyOutcomes;
    }
}
