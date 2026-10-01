using System;
using Core.Foundation.Common;

namespace Presentation.Render
{
    /// <summary>
    /// 姿势解析的呈现侧上下文来源（手感设计/04 第 2 节）：回答"该实体此刻的步态、武器族、变体是什么"。
    /// 只读查询 + 变化通知；实现方不得反向影响判定。<c>PoseSelector</c> 是框架提供的实现。
    /// </summary>
    public interface IPoseContextSource
    {
        /// <summary>该实体当前的上下文；未登记的实体返回 <see cref="PoseContext.Empty"/>。</summary>
        PoseContext GetContext(Id entityId);

        /// <summary>某实体的上下文变化（步态切换、武器族/变体改变）。订阅方（如 <c>AnimClipResolver</c>）据此重新解析运动态姿势。</summary>
        event Action<Id>? ContextChanged;
    }
}
