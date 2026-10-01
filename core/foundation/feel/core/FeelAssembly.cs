using System;
using System.Collections.Generic;
using System.Text;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.Feel
{
    /// <summary>手感系统装配失败（有手感数据但标定缺项、档案校验有错误等）。</summary>
    public sealed class FeelAssemblyException : InvalidOperationException
    {
        /// <summary>失败代码（<see cref="FeelChecks"/> 的检查名或 <c>feel_assembly_*</c>）。</summary>
        public string Code { get; }

        public IReadOnlyList<FeelCheckIssue> Issues { get; }

        public FeelAssemblyException(string code, string message, IReadOnlyList<FeelCheckIssue>? issues = null)
            : base(message)
        {
            Code = code;
            Issues = issues ?? Array.Empty<FeelCheckIssue>();
        }
    }

    /// <summary>已装配的手感系统：解析器、标定、档案集合、调试覆盖层。</summary>
    public sealed class FeelSystem
    {
        public FeelResolver Resolver { get; }

        public FeelCalibration Calibration { get; }

        public FeelProfileSet Profiles { get; }

        /// <summary>装配根创建的调试覆盖层；调用方自带调试提供者时为 null。</summary>
        public FeelDebugOverrides? DebugOverrides { get; }

        public double StepSeconds { get; }

        internal FeelSystem(FeelResolver resolver, FeelCalibration calibration, FeelProfileSet profiles, FeelDebugOverrides? debug, double stepSeconds)
        {
            Resolver = resolver;
            Calibration = calibration;
            Profiles = profiles;
            DebugOverrides = debug;
            StepSeconds = stepSeconds;
        }
    }

    /// <summary>装配结果：未装配（没有手感数据）或已装配。</summary>
    public sealed class FeelAssemblyResult
    {
        public bool IsAssembled => System != null;

        public FeelSystem? System { get; }

        /// <summary>未装配的原因说明（已装配时为空）。</summary>
        public string Reason { get; }

        private FeelAssemblyResult(FeelSystem? system, string reason)
        {
            System = system;
            Reason = reason;
        }

        internal static FeelAssemblyResult Assembled(FeelSystem system) => new FeelAssemblyResult(system, string.Empty);

        internal static FeelAssemblyResult NotAssembled(string reason) => new FeelAssemblyResult(null, reason);
    }

    /// <summary>
    /// 手感系统装配（手感设计/05 第 7 节）。
    /// <para>
    /// 装配规则（写入模块 README 判断记录）：(1) 数据里<b>没有任何</b> <c>feel.*</c> 行 → 系统<b>不装配</b>
    /// （<see cref="FeelAssemblyResult.IsAssembled"/> 为 false），既有行为逐位不变——装配方不得创建解析器、不得订阅任何事件；
    /// (2) 有手感数据但标定表缺项（零行、多行未指定、行不完整、基础预设不存在）→ 装配<b>失败</b>，抛
    /// <see cref="FeelAssemblyException"/> 并给出明确诊断，不用隐式缺省；(3) 档案静态校验有错误 → 同样失败。
    /// </para>
    /// </summary>
    public static class FeelAssembly
    {
        public static FeelAssemblyResult Assemble(IDataRegistryView view, FeelAssemblyOptions options)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (options == null) throw new ArgumentNullException(nameof(options));
            var fields = options.Fields ?? FeelFields.Default;
            var profiles = FeelProfileSet.FromRegistry(view, fields);
            return AssembleFrom(profiles, options);
        }

        public static FeelAssemblyResult AssembleFrom(FeelProfileSet profiles, FeelAssemblyOptions options)
        {
            if (profiles == null) throw new ArgumentNullException(nameof(profiles));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (!double.IsFinite(options.StepSeconds) || options.StepSeconds <= 0)
            {
                throw new ArgumentException("StepSeconds 必须是正的有限数", nameof(options));
            }

            if (!profiles.HasAnyData)
            {
                return FeelAssemblyResult.NotAssembled("数据里没有任何 feel.* 行，手感系统不装配，既有行为不变");
            }

            if (profiles.CalibrationRowCount == 0)
            {
                throw new FeelAssemblyException(FeelChecks.CalibrationMissing,
                    "手感装配失败：存在 feel.* 数据但 feel.calibration 没有任何行；标定表缺失时不用隐式缺省，请为这款游戏补一行标定（参考身高、基础移速、基础预设等）");
            }

            FeelCalibration? calibration = null;
            if (options.CalibrationId != null)
            {
                for (var i = 0; i < profiles.Calibrations.Count; i++)
                {
                    if (profiles.Calibrations[i].Id == options.CalibrationId) calibration = profiles.Calibrations[i];
                }
                if (calibration == null)
                {
                    throw new FeelAssemblyException(FeelChecks.CalibrationMissing,
                        $"手感装配失败：指定的标定行 \"{options.CalibrationId}\" 不存在或字段不完整");
                }
            }
            else if (profiles.CalibrationRowCount > 1)
            {
                throw new FeelAssemblyException(FeelChecks.CalibrationMissing,
                    $"手感装配失败：feel.calibration 有 {profiles.CalibrationRowCount} 行，装配参数必须指定 CalibrationId");
            }
            else if (profiles.Calibrations.Count == 1)
            {
                calibration = profiles.Calibrations[0];
            }
            else
            {
                throw new FeelAssemblyException(FeelChecks.CalibrationMissing,
                    "手感装配失败：feel.calibration 的唯一一行字段不完整或取值非法");
            }

            if (profiles.GetPreset(calibration.BasePresetId) == null)
            {
                throw new FeelAssemblyException(FeelChecks.CalibrationMissing,
                    $"手感装配失败：标定行 \"{calibration.Id}\" 指定的基础预设 \"{calibration.BasePresetId}\" 不存在");
            }

            var issues = FeelProfileChecker.Check(profiles);
            if (issues.Count > 0)
            {
                var sb = new StringBuilder("手感装配失败：档案校验有 ").Append(issues.Count).Append(" 个错误：");
                for (var i = 0; i < issues.Count && i < 5; i++) sb.Append("\n  ").Append(issues[i]);
                if (issues.Count > 5) sb.Append("\n  ……");
                throw new FeelAssemblyException("feel_assembly_profile_invalid", sb.ToString(), issues);
            }

            FeelDebugOverrides? debug = null;
            var providers = new FeelProviders();
            if (options.Providers != null)
            {
                providers.Body = options.Providers.Body;
                providers.Tags = options.Providers.Tags;
                providers.Equipment = options.Providers.Equipment;
                providers.Action = options.Providers.Action;
                providers.Temporary = options.Providers.Temporary;
                providers.Debug = options.Providers.Debug;
            }
            if (providers.Debug == null)
            {
                debug = new FeelDebugOverrides(profiles.Fields);
                providers.Debug = debug;
            }

            var resolver = new FeelResolver(profiles, calibration, options.StepSeconds, providers);
            if (debug != null)
            {
                debug.Changed += unit =>
                {
                    if (unit.HasValue) resolver.Invalidate(unit.Value, "debug_override_changed");
                    else resolver.InvalidateAll("debug_override_changed");
                };
            }
            return FeelAssemblyResult.Assembled(new FeelSystem(resolver, calibration, profiles, debug, options.StepSeconds));
        }
    }
}
