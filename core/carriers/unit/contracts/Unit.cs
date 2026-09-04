using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;

namespace Core.Carriers.Unit
{
    /// <summary>进出战斗状态（见 05 第 1.2 节 <c>Unit.combatState</c> <c>out｜in</c>）。</summary>
    public enum UnitCombatState
    {
        Out,
        In,
    }

    /// <summary>
    /// 可战斗、可移动的活体基类（见 05 对象模型与世界.md 第 1.2 节 <c>Unit</c> 字段表）。
    /// <para>
    /// 判断记录——本类型只是"字段容器"，05 §1.2 表里"引用"性质的字段刻意不在本类型重复持有：
    /// </para>
    /// <list type="bullet">
    /// <item><c>statBlock</c>/<c>powerSet</c> 不在本类型持有引用——运行期属性/资源改由 L1
    /// <c>Core.Numbers.StatBlock.IStatHost</c>/<c>Core.Numbers.PowerSet.IPowerHost</c> 按单位 id
    /// 索引管理（见 06 第 1、2 节），本类型只是这些宿主用来查找的 key（<c>EntityId</c>）。</item>
    /// <item><c>auras</c> 不在本类型持有——光环实例由 <c>core/rules/skill</c> 的 <c>AuraHost</c> 按
    /// 单位 id 管理（见 06 第 3 节），查询走 <c>Core.Rules.Common.IAuraQuery</c>。</item>
    /// <item><c>threatTable</c> 不在本类型持有——仇恨表由 <c>core/rules/combat</c> 按单位 id 管理，
    /// 查询走 <c>Core.Rules.Common.IThreatTable</c>。</item>
    /// </list>
    /// <see cref="CombatState"/>/<see cref="Level"/> 是快照字段：由 combat/progression 各自模块在
    /// 写入权威状态的同时回写本字段，本类型只提供存储位置，不自行维护其值。
    /// </summary>
    public abstract class Unit : Entity
    {
        public Id FactionId { get; set; }

        /// <summary>进出战斗状态（见类型注释），由 <c>core/rules/combat</c> 维护。</summary>
        public UnitCombatState CombatState { get; set; } = UnitCombatState.Out;

        public bool Alive { get; set; } = true;

        /// <summary>移动状态（见 05 第 6.2 节），由本模块的 <c>MovementTickHandler</c> 维护。</summary>
        public MovementState MovementState { get; set; } = MovementState.Idle;

        /// <summary>高度偏移（见 05 第 3.3 节），默认 0，只供表现层渲染读取，不参与平面距离与碰撞
        /// 计算。</summary>
        public double HeightOffset { get; set; }

        /// <summary>标签集合（见 <c>Core.Rules.Common.IUnitAccess.GetTags</c> 用途：按标签过滤目标）。
        /// 暴露为可变 <see cref="List{T}"/>（同 <c>core/rules/tests/Integration/TestUnit.Tags</c>
        /// 惯例），供装配/内容加载阶段直接填充。</summary>
        public List<Id> Tags { get; } = new List<Id>();

        /// <summary>当前等级快照（见 05 第 1.2 节字段表判断记录），由 <c>core/numbers/progression</c>
        /// 写入。</summary>
        public int Level { get; set; } = 1;

        protected Unit(Id entityId, Id mapId, Id factionId)
            : base(entityId, mapId)
        {
            FactionId = factionId;
        }
    }
}
