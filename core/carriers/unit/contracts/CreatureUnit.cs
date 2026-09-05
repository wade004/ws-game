using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;
using Core.Rules.Common;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 非玩家单位（见 05 第 1.2 节 <c>CreatureUnit</c>）。
    /// </summary>
    public sealed class CreatureUnit : Unit
    {
        public override string Kind => EntityKinds.Creature;

        /// <summary>行为外壳当前状态快照（见 05 第 1.2 节 <c>aiState</c>），复用
        /// <c>core/rules/ai</c> 已定义的 <see cref="BehaviorState"/> 枚举，不在本层另建一套平行状态
        /// 集合（06 第 6.1 节状态图与本字段同源）。由 <c>core/rules/ai</c> 写入快照。</summary>
        public BehaviorState AiState { get; set; } = BehaviorState.Idle;

        /// <summary>掉落表引用（见 05 第 1.2 节 <c>lootTableId</c>）。</summary>
        public Id? LootTableId { get; set; }

        /// <summary>免疫的学派/效果类型/控制类别（见 07 第 2.1 节 <c>immunities</c>）。</summary>
        public List<Id> Immunities { get; } = new List<Id>();

        /// <summary>职能标志位（见 05 第 1.2 节 <c>npcFlags</c>、07 第 2.2 节标志清单）。</summary>
        public List<Id> NpcFlags { get; } = new List<Id>();

        /// <summary>召唤物/宠物用，指向拥有者单位（见 05 第 1.2 节 <c>ownerId</c>、07 第 4 节）。</summary>
        public Id? OwnerId { get; set; }

        /// <summary>
        /// <paramref name="templateId"/> 在本子类型是必填的（见 05 第 1.2 节 <c>CreatureUnit</c> 额外
        /// 字段表——不同于 <see cref="Core.Foundation.SimLoop.Entity.TemplateId"/> 基类字段"手工放置
        /// 对象可为空"的宽松语义，任何 <see cref="CreatureUnit"/> 都必然来自某个 <c>creature.template</c>）。
        /// 复用基类 <see cref="Core.Foundation.SimLoop.Entity.TemplateId"/> 作存储位置（不重复定义
        /// 一个同名字段），只在构造函数层面把它变成必填参数。
        /// </summary>
        public CreatureUnit(Id entityId, Id mapId, Id factionId, Id templateId)
            : base(entityId, mapId, factionId)
        {
            TemplateId = templateId;
        }
    }
}
