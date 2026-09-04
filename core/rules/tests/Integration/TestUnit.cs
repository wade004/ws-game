using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.SimLoop;

namespace Tests.Rules.Integration
{
    /// <summary>
    /// T2-11 集成测试专用的最小 <see cref="Entity"/> 子类（同 <c>core/foundation/tests/Determinism/
    /// DeterministicUnit.cs</c> 的做法：<c>sim_loop</c> 只提供公共基类，具体子类属于更上层模块，
    /// 本类型不代表任何正式的载体层类型）。补充 L2 四模块通过 <see cref="Core.Rules.Common.IUnitAccess"/>
    /// 需要、但 <see cref="Entity"/> 基类本身不提供的字段：阵营、标签、存活。
    /// </summary>
    internal sealed class TestUnit : Entity
    {
        public override string Kind => "unit";

        public Id FactionId { get; set; }

        public List<Id> Tags { get; } = new List<Id>();

        public bool Alive { get; set; } = true;

        public int Level { get; set; } = 1;

        public TestUnit(Id entityId, Id mapId, Id factionId, int level = 1)
            : base(entityId, mapId)
        {
            FactionId = factionId;
            Level = level;
        }
    }
}
