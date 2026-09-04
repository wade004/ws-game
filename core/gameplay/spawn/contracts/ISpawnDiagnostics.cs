namespace Core.Gameplay.Spawn
{
    /// <summary>本模块的最小诊断出口（惯例同 <c>Core.Gameplay.AreaTrigger.IAreaTriggerDiagnostics</c>）。</summary>
    public interface ISpawnDiagnostics
    {
        void Warn(string message);

        void Error(string message);
    }
}
