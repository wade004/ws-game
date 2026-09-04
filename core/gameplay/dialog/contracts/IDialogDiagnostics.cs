namespace Core.Gameplay.Dialog
{
    /// <summary><see cref="DialogHost"/> 的诊断出口（惯例同
    /// <c>core/gameplay/world_state</c> 的 <c>IWorldStateDiagnostics</c>）：子状态转移失败、
    /// 动作缺少对应回调等非致命情形记一条警告，不抛异常。</summary>
    public interface IDialogDiagnostics
    {
        void Warn(string message);
    }
}
