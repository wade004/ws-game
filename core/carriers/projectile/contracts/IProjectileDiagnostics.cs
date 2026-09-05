namespace Core.Carriers.Projectile
{
    /// <summary>
    /// 本模块的最小诊断出口（与 <c>core/carriers/gobj</c> 的 <c>IGobjDiagnostics</c> 同一惯例：
    /// L3 载体层模块不强制依赖任何引擎适配层日志接口，默认实现只收集到内存）。用于记录：命中后
    /// 效果列表引用未知的 <c>EffectKind</c>、生成参数缺失关键字段而使用默认值等非致命但值得记录
    /// 的情形。
    /// </summary>
    public interface IProjectileDiagnostics
    {
        void Warn(string message);

        void Error(string message);
    }
}
