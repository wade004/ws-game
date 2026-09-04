using Core.Foundation.Common;
using Core.Foundation.Expr;

namespace Core.Carriers.Common
{
    /// <summary>
    /// 依赖倒置接口（见 01_分层与依赖.md 第 3 节依赖矩阵"L3 允许依赖 L0～L2，禁止依赖 L4"、第 8 节
    /// 第 3 种合法方式"策略注入/依赖倒置"）：WorldState 本身属于 L4 玩法层
    /// （见 01 L4 模块表 <c>world_state</c> 行），本模块（L3）不得直接引用它；但 07 第 3.4 节要求
    /// <c>GameObject.state</c>（如 <c>chest.open_state</c>）落地为一条 <c>WorldState</c> 标志、07 第
    /// 3.2 节 <c>LockDef.requirement</c> 的 <c>world_flag</c> 变体需要读世界标志判定开锁——本接口是
    /// 05 第 8.2 节 <c>WorldState</c> 接口的最小只读+写子集（不含 <c>onChanged</c> 订阅：本层用不到，
    /// 订阅需求属于表现层/玩法层自己的职责），由 L4（或游戏组装根）实现，组装期注入给 L3 的
    /// <see cref="IGameObjectHost"/> 实现，L3 本身不持有任何具体实现、不反向引用 L4 程序集。
    /// </summary>
    public interface IWorldFlags
    {
        /// <summary>读取某标志当前值；未设置过返回 null（见 05 第 8.2 节 <c>get</c>，值类型统一按
        /// <see cref="ExprValue"/> 承载——10 第 2.3 节 <c>world_state_flags</c> 字段表注明其取值为
        /// <c>Bool｜Int</c>，<see cref="ExprValue"/> 的判别联合天然覆盖这两种取值，无需另建类型）。</summary>
        ExprValue? Get(Id flagKey);

        /// <summary>写入某标志（见 05 第 8.2 节 <c>set</c>），<paramref name="writerId"/> 是写入方标识
        /// （用于排查与日志，见 05 该节"每次写入必须带 writerId"）。</summary>
        void Set(Id flagKey, ExprValue value, Id writerId);

        /// <summary>该标志当前是否已设置过（见 05 第 8.2 节 <c>has</c>）。</summary>
        bool Has(Id flagKey);
    }
}
