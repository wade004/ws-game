namespace Core.Foundation.InputMap
{
    /// <summary>
    /// 按钮边沿接收端（手感设计/01 第 1 节"输入采样"）：<see cref="InputMapHost.Update"/> 把本批次内按钮型动作发生的按下/抬起边沿
    /// <b>按真实发生顺序</b>逐条交给它——输入缓冲据此区分点按与按住（同一批次内先按下后抬起仍是一次完整点按，不丢事件）。
    /// 边沿在整批事件与全部动作状态更新完成之后才交付，接收端此时读到的轴值（移动方向快照）是本批次末的值。
    /// 本接口只描述"动作被按下/抬起"，不携带行动者：由接收端自己决定这些边沿属于哪个受控行动者。
    /// </summary>
    public interface IInputEdgeSink
    {
        /// <summary>按钮型动作 <paramref name="actionName"/>（<see cref="ActionDefinition.ActionId"/> 的字符串）发生了一次边沿。</summary>
        void OnButtonEdge(string actionName, bool isDown);
    }
}
