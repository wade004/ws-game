namespace Core.Carriers.Gobj
{
    /// <summary>
    /// 本模块的最小诊断出口（与 <c>core/rules/skill</c> 的 <c>ISkillDiagnostics</c> 同一惯例：L3
    /// 载体层模块不强制依赖任何引擎适配层日志接口，默认实现只收集到内存）。用于记录：未注入
    /// <see cref="GobjOptions"/> 回调时对应交互被跳过、未注入 <see cref="Core.Carriers.Common.ILootRoller"/>
    /// 时开箱/采集无掉落等非致命但值得记录的情形。
    /// </summary>
    public interface IGobjDiagnostics
    {
        void Warn(string message);

        void Error(string message);
    }
}
