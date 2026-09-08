using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SimLoop;

namespace Core.Carriers.Unit
{
    /// <summary>
    /// 玩家控制的单位（见 05 第 1.2 节 <c>PlayerUnit</c>）。
    /// <para>
    /// 判断记录：05 §1.2 列出的额外字段里，<c>inventory</c>/<c>equipment</c> 不在本类型出现——它们
    /// 由并行开发的 <c>core/carriers/item</c> 模块经 <c>Core.Carriers.Common.IInventoryHost</c>/
    /// <c>IEquipmentHost</c> 按单位 id 管理（本类型只是这些宿主的 key），不在 <c>Unit</c> 树里重复
    /// 持有一份物品集合。<c>skillBook</c>（已学技能）同理不出现——已学技能集合的权威来源是
    /// <c>core/rules/skill</c> 的相关宿主（06 第 7 节 <c>skill.book</c>），本类型不重复存储一份，
    /// 任务书原句"已学技能由 skill 管理，这里不重复存"即此意。<c>questLog</c> 例外保留为一个
    /// <see cref="JsonObject"/> 占位字段——量化任务状态的 L4 <c>quest</c> 模块不在本任务范围内，
    /// 暂无其它权威存储位置，先按任务书"JsonObject 占位，由 L4 quest 管理"给一个存储位，具体结构
    /// 留待 quest 模块落地时再展开。
    /// </para>
    /// </summary>
    public sealed class PlayerUnit : Unit
    {
        private static readonly JsonObject EmptyObject = new JsonObjectBuilder().Build();

        public override string Kind => EntityKinds.Player;

        /// <summary>职业模板引用（见 05 第 1.2 节 <c>archetypeId</c>）。</summary>
        public Id ArchetypeId { get; set; }

        /// <summary>
        /// 种族模板引用（可选）。种族被动光环跨图丢失根治（architecture/落地计划/
        /// audit-85f1f4f-20260908）补录：<see cref="Core.Rules.Assembly.RulesAssembly.RegisterUnit"/>
        /// 的 <c>raceId</c> 参数此前只在调用当下用于施加基础属性修正/被动光环，应用完成后不会被
        /// 任何 L3 类型记住"这个单位是哪个种族"（见类型注释历史判断记录），导致
        /// <c>World.ClearAll</c>（跨图切换）后想要重放种族被动光环时，没有任何地方能查到"该用哪个
        /// 种族重放"。本字段补上这个引用，可选（<c>null</c> = 未设置种族，或游戏本身不使用种族
        /// 概念）——调用方（游戏引导代码）在调用 <c>RegisterUnit</c> 传入非空 <c>raceId</c> 时应
        /// 同时把它写进本字段，本类型自己不会自动同步（05/L2 分层边界：<c>RulesAssembly</c> 不
        /// 知道 <see cref="PlayerUnit"/> 这个 L3 类型的存在，见 <see cref="ArchetypeId"/> 判断记录
        /// 同款分层理由）。存档见 <c>Core.Carriers.Unit.UnitPersistable.RaceId</c>。
        /// </summary>
        public Id? RaceId { get; set; }

        /// <summary>已选天赋（见 05 第 1.2 节 <c>talents</c>）。</summary>
        public List<Id> Talents { get; } = new List<Id>();

        /// <summary>任务日志占位（见类型注释判断记录）。</summary>
        public JsonObject QuestLog { get; set; } = EmptyObject;

        public PlayerUnit(Id entityId, Id mapId, Id factionId, Id archetypeId)
            : base(entityId, mapId, factionId)
        {
            ArchetypeId = archetypeId;
        }
    }
}
