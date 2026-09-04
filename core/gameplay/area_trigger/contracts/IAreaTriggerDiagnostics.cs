namespace Core.Gameplay.AreaTrigger
{
    /// <summary>
    /// 本模块的最小诊断出口（惯例同 <c>Core.Carriers.Gobj.IGobjDiagnostics</c>）：记录未注入
    /// <see cref="AreaTriggerOptions"/> 回调时对应分发被跳过、离散步跳过评估等非致命但值得记录的
    /// 情形。
    /// </summary>
    public interface IAreaTriggerDiagnostics
    {
        void Warn(string message);

        void Error(string message);
    }
}
