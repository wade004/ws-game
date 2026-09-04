using Core.Foundation.Common;
using Core.Foundation.SimLoop;

namespace Tests.Foundation.SimLoop
{
    /// <summary>
    /// 测试专用的 <see cref="Entity"/> 子类：本模块只提供公共基类，具体子类
    /// （Unit/GameObject 等）属于更上层模块，测试用这个最小实现代替。
    /// </summary>
    internal sealed class TestEntity : Entity
    {
        public override string Kind { get; }

        public TestEntity(Id entityId, Id mapId, string kind = "test_entity")
            : base(entityId, mapId)
        {
            Kind = kind;
        }
    }
}
