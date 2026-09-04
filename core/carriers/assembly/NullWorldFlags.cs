using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Expr;

namespace Core.Carriers.Assembly
{
    /// <summary>
    /// <see cref="IWorldFlags"/> 的空对象默认实现（阶段 3 整理"事项四"）：<c>Get</c> 恒返回
    /// <c>null</c>（未设置过）、<c>Has</c> 恒返回 <c>false</c>、<c>Set</c> 不做任何事。
    /// <c>core/carriers/gobj.GameObjectHost</c> 的构造函数把 <see cref="IWorldFlags"/> 声明为
    /// 非空必填参数（见该类型构造函数），但真实的 <c>core/gameplay/world_state.WorldState</c> 属于
    /// L4（<see cref="IWorldFlags"/> 顶部判断记录"L3 不得直接引用 L4"），<see cref="CarriersAssembly"/>
    /// 本身（L3）不能替调用方决定用哪个真实实现——调用方（游戏层组装根）未显式注入
    /// <see cref="IWorldFlags"/> 时，用本类型兜底，保持 <c>GameObjectHost</c> 可构造，
    /// <c>gobj.template.type_data</c> 里依赖世界标志的分支（如 <c>chest.open_state</c>）在没有真实
    /// 世界状态存储的最小化场景下按"从未设置"处理，不抛异常。
    /// </summary>
    public sealed class NullWorldFlags : IWorldFlags
    {
        public static readonly NullWorldFlags Instance = new NullWorldFlags();

        private NullWorldFlags()
        {
        }

        public ExprValue? Get(Id flagKey) => null;

        public void Set(Id flagKey, ExprValue value, Id writerId)
        {
        }

        public bool Has(Id flagKey) => false;
    }
}
