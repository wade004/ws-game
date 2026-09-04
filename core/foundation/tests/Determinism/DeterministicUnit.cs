using Core.Foundation.Common;
using Core.Foundation.SimLoop;

namespace Tests.Foundation.Determinism
{
    /// <summary>
    /// 确定性回放集成测试（T1-9，`DeterminismTests.cs`）专用的最小 <see cref="Entity"/> 子类。
    /// `sim_loop` 只提供公共基类，具体子类（Unit/GameObject 等）属于更上层模块——本类型只是
    /// 测试用的替身，不代表任何正式的载体层类型。
    /// </summary>
    internal sealed class DeterministicUnit : Entity
    {
        public override string Kind { get; }

        public DeterministicUnit(Id entityId, Id mapId, string kind)
            : base(entityId, mapId)
        {
            Kind = kind;
        }
    }
}
