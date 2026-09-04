using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;

namespace Presentation.Ui
{
    /// <summary>
    /// UI 框架"查询 + 订阅刷新"数据绑定契约（见 09_表现层.md 第 7.2 节
    /// <c>UiDataSource { query(path): Any; subscribe(eventKey, handler): SubscriptionId; unsubscribe }</c>，
    /// 01_分层与依赖.md L5 模块表 <c>ui</c> 行契约接口名 <c>UiFrameworkHost</c>）。全部 UI 面板与
    /// 视图模型只经本接口读取只读状态（铁律 P1）与订阅事件（铁律 P2），不持有任何可写的游戏状态
    /// 副本。
    /// <para>
    /// 判断记录：09 原文 <c>query</c> 返回类型是不透明的 <c>Any</c>，本仓库已有全架构统一的只读
    /// 求值结果类型 <see cref="ExprValue"/>（Bool/Int/Number/String/Id 五种，见
    /// <c>core/foundation/expr/contracts/ExprValue.cs</c>），UI 查询到的一切值（属性数值、等级、
    /// 物品/技能/任务引用、状态文案）都落在这五种之内，直接复用而不新造一套"UI 专用值类型"。
    /// <c>subscribe</c> 返回类型同样复用仓库既有的 <see cref="SubscriptionHandle"/>
    /// （<c>Dispose()</c> 即取消订阅），<see cref="Unsubscribe"/> 是任务书按 09 原文
    /// "unsubscribe(id): void" 显式要求的便利方法，等价于 <c>handle.Dispose()</c>。
    /// </para>
    /// </summary>
    public interface IUiDataSource
    {
        /// <summary>
        /// 按小语法路径做一次只读快照查询（见 <see cref="UiPathParser"/>、各
        /// <see cref="IUiPathProvider"/> 实现覆盖的路径清单）。路径语法不认识、引用了格式非法的
        /// Id、或查询链路上找不到对应宿主时返回 null 并记一条诊断；路径语法合法但当前无值
        /// （未选中目标、槽位为空、下标越界等）同样返回 null，但不记诊断——两者对调用方而言都是
        /// "查不到"，区别只体现在诊断日志里。
        /// </summary>
        ExprValue? Query(string path);

        /// <summary>按事件 key 订阅（直接转发给构造期注入的 <see cref="IEventBus"/>，见该接口
        /// <see cref="IEventBus.Subscribe(Id, EventHandler)"/>）。返回句柄供
        /// <see cref="Unsubscribe"/> 或直接 <c>Dispose()</c> 取消订阅。</summary>
        SubscriptionHandle Subscribe(Id eventKey, EventHandler handler);

        /// <summary>取消一次订阅（等价 <c>handle.Dispose()</c>，见类型注释判断记录）。</summary>
        void Unsubscribe(SubscriptionHandle handle);
    }
}
