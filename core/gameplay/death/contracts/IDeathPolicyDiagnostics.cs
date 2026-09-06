namespace Core.Gameplay.Death
{
    /// <summary>本模块的最小诊断出口（惯例同 <c>Core.Gameplay.Spawn.ISpawnDiagnostics</c>/
    /// <c>Core.Gameplay.AreaTrigger.IAreaTriggerDiagnostics</c>）。</summary>
    public interface IDeathPolicyDiagnostics
    {
        void Warn(string message);

        void Error(string message);
    }
}
