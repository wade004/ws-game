using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Expr;

namespace Core.Gameplay.WorldState
{
    /// <summary>
    /// <see cref="IWorldState.OnChanged"/> 的回调签名（见 05_对象模型与世界.md 第 8.2 节
    /// <c>onChanged(flagKey, callback: (oldValue, newValue) -> Void)</c>）。
    /// </summary>
    public delegate void FlagChangedCallback(ExprValue oldValue, ExprValue newValue);

    /// <summary>
    /// 世界状态标志字典的完整读写接口（见 05 第 8 节、ADR-0008、00 第 4 节原则 8、
    /// 01 第 L4 模块表 <c>world_state</c> 行）：一个带命名空间的 <c>flagKey -&gt; 值</c> 字典，
    /// 替代魔兽的 Phasing，供刷新条件、物件显隐、任务前置、对话分支等经 Expr 的 <c>world</c>
    /// 分组统一引用。本接口是 05 第 8.2 节接口的 PascalCase 落地，并按任务书拍板扩展了
    /// <see cref="Remove"/>/<see cref="Keys"/>/<see cref="KeysUnder"/>/<see cref="Count"/>
    /// 四个 05 原文未列出的成员（判断记录见 <c>core/gameplay/world_state/README.md</c>）。
    /// </summary>
    public interface IWorldState
    {
        /// <summary>
        /// 读取某标志当前值（05 第 8.2 节 <c>get</c>）。未设置过的标志按 04 第 6.3 节
        /// "求值期引用对象暂缺 -&gt; 按分组默认值处理"的约定返回 <see cref="ExprValue.OfBool(bool)"/>
        /// <c>false</c>——与 <see cref="Core.Carriers.Common.IWorldFlags.Get"/>（返回
        /// <c>ExprValue?</c>，未设置返回 <c>null</c>）语义不同，二者的取舍见 README。
        /// </summary>
        ExprValue Get(Id flagKey);

        /// <summary>
        /// 写入某标志（05 第 8.2 节 <c>set</c>）。<paramref name="flagKey"/> 必须以
        /// <c>"world."</c> 开头（见 05 第 8.2 节"<c>flagKey</c> 命名空间格式复用 id 规范中的
        /// <c>world.&lt;路径&gt;</c>"），否则抛 <see cref="System.ArgumentException"/>；
        /// <paramref name="writerId"/> 是写入方标识（见该节"每次写入必须带 writerId"），必须是
        /// 合法构造的 <see cref="Id"/>。写入值与当前已存储值相等（<see cref="ExprValue.Equals(ExprValue)"/>）
        /// 时视为无变化，不重复写入、不触发 <c>world.flag_changed</c> 事件。
        /// </summary>
        void Set(Id flagKey, ExprValue value, Id writerId);

        /// <summary>该标志当前是否已设置过（05 第 8.2 节 <c>has</c>）。</summary>
        bool Has(Id flagKey);

        /// <summary>
        /// 移除某标志，使其恢复"未设置"状态（05 原文未列出本方法，任务书拍板补充）。
        /// 标志原先存在时返回 <c>true</c> 并触发一次 <c>world.flag_changed</c>
        /// （<c>newValue</c> 按"缺失"约定取 <see cref="ExprValue.OfBool(bool)"/> <c>false</c>，
        /// 不套用 <see cref="Set"/> 的"同值不发事件"规则——移除是"是否存在"这一状态本身的改变）；
        /// 标志原先不存在时返回 <c>false</c>，不触发事件。<paramref name="flagKey"/>/
        /// <paramref name="writerId"/> 的校验规则同 <see cref="Set"/>。
        /// </summary>
        bool Remove(Id flagKey, Id writerId);

        /// <summary>
        /// 按 <paramref name="flagKey"/> 过滤的 <c>world.flag_changed</c> 便利订阅（05 第 8.2 节
        /// "onChanged 是同一事件的按 key 过滤订阅便利接口"）。回调在事件经事件总线实际派发时调用
        /// （<see cref="WorldStateOptions.DispatchMode.Immediate"/> 下随 <see cref="Set"/>/
        /// <see cref="Remove"/> 同步调用；<see cref="WorldStateOptions.DispatchMode.Enqueue"/>
        /// 下延后到 <c>IEventBus.DispatchPending</c>），回调内抛出的异常被隔离，不影响其它订阅者，
        /// 记一条诊断警告（见 <see cref="IWorldStateDiagnostics"/>）。
        /// </summary>
        SubscriptionHandle OnChanged(Id flagKey, FlagChangedCallback callback);

        /// <summary>全部已设置标志的 key，按 <see cref="Id"/> 序数排序（05 原文未列出，任务书拍板补充，
        /// 供存档/调试/内容工具按命名空间浏览）。</summary>
        IReadOnlyList<Id> Keys { get; }

        /// <summary>
        /// <paramref name="prefix"/> 命名空间下的全部标志 key（<c>flagKey == prefix</c> 或
        /// <c>flagKey</c> 以 <c>"{prefix}."</c> 开头），按 <see cref="Id"/> 序数排序。
        /// 典型用途见 07 第 3.4 节 <c>world.gobj.&lt;实例id&gt;.*</c> 这类命名空间下的批量查询。
        /// </summary>
        IReadOnlyList<Id> KeysUnder(Id prefix);

        /// <summary>已设置标志总数。</summary>
        int Count { get; }
    }
}
