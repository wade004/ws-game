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

    /// <summary>
    /// 一次数据热加载的结果（<see cref="FeelSystem.TryReload"/>，手感设计/05 第 8 节）。拒绝时保持当前档案不变，原因与校验问题写在本对象里，
    /// 不静默吞掉（开发期改坏了表要让改表的人看得见）。
    /// </summary>
    public sealed class FeelReloadResult
    {
        /// <summary>新档案是否已换入解析器。</summary>
        public bool Applied { get; }

        /// <summary>未换入的原因，或已换入时的补充说明（如标定行变化已一并换入）；无话可说为空串。</summary>
        public string Reason { get; }

        /// <summary>档案校验问题（拒绝原因是校验失败时非空）。</summary>
        public IReadOnlyList<FeelCheckIssue> Issues { get; }

        /// <summary>已换入时：新数据里本系统标定行的取值与此前不同——新标定已一并换入（手感落地 M3-B 起不再要求重启）：进行中的动作保持原快照，下一个动作按新标定换算。</summary>
        public bool CalibrationChanged { get; }

        /// <summary>截至本次的成功热加载累计次数（本次已换入时含本次）。</summary>
        public int Generation { get; }

        internal FeelReloadResult(bool applied, string reason, IReadOnlyList<FeelCheckIssue>? issues, bool calibrationChanged, int generation)
        {
            Applied = applied;
            Reason = reason;
            Issues = issues ?? Array.Empty<FeelCheckIssue>();
            CalibrationChanged = calibrationChanged;
            Generation = generation;
        }
    }

    /// <summary>已装配的手感系统：解析器、标定、档案集合、调试覆盖层。</summary>
    public sealed class FeelSystem
    {
        private int _reloadGeneration;

        public FeelResolver Resolver { get; }

        /// <summary>当前生效的标定（热加载成功且标定行取值变化时指向新标定，见 <see cref="TryReload"/>）。</summary>
        public FeelCalibration Calibration { get; private set; }

        /// <summary>当前生效的档案集合（热加载成功后指向新集合）。</summary>
        public FeelProfileSet Profiles { get; private set; }

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

        /// <summary>
        /// 开发期数据热加载（手感设计/05 第 8 节，ADR-0019）：从数据视图重新读出 <c>feel.*</c> 表，通过与装配时同一套静态校验后换入解析器
        /// （<see cref="FeelResolver.Reload"/>：清全部单位缓存、版本号下一次解析时递增；进行中动作的快照不变，下一次动作才看到新数据）。
        /// <para>
        /// 判断记录：新数据没有任何 <c>feel.*</c> 行、缺本系统的标定行或其基础预设、档案校验有错误时<b>拒绝</b>并保持当前档案
        /// （返回的 <see cref="FeelReloadResult.Reason"/>/<see cref="FeelReloadResult.Issues"/> 给出原因），不抛异常、不换入半份数据。
        /// 标定（<see cref="FeelCalibration"/>）同样热换（手感落地 M3-B）：新数据里本系统标定行取值变化时，档案与新标定一并换入
        /// （<see cref="FeelResolver.Reload(FeelProfileSet, FeelCalibration)"/>），结果里仍标 <see cref="FeelReloadResult.CalibrationChanged"/> 供宿主提示，但不再要求重启——
        /// 进行中的动作保持开始时的快照（旧标定下的结果），下一个动作按新标定换算。
        /// </para>
        /// </summary>
        public FeelReloadResult TryReload(IDataRegistryView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            var next = FeelProfileSet.FromRegistry(view, Profiles.Fields);
            if (!next.HasAnyData)
            {
                return new FeelReloadResult(false, "热加载拒绝：数据里没有任何 feel.* 行，保持当前档案", null, false, _reloadGeneration);
            }

            FeelCalibration? calibration = null;
            for (var i = 0; i < next.Calibrations.Count; i++)
            {
                if (next.Calibrations[i].Id == Calibration.Id) calibration = next.Calibrations[i];
            }

            if (calibration == null)
            {
                return new FeelReloadResult(
                    false, $"热加载拒绝：标定行 \"{Calibration.Id}\" 不存在或字段不完整，保持当前档案", null, false, _reloadGeneration);
            }

            if (next.GetPreset(calibration.BasePresetId) == null)
            {
                return new FeelReloadResult(
                    false, $"热加载拒绝：基础预设 \"{calibration.BasePresetId}\" 不存在，保持当前档案", null, false, _reloadGeneration);
            }

            var issues = FeelProfileChecker.Check(next);
            if (issues.Count > 0)
            {
                return new FeelReloadResult(false, $"热加载拒绝：档案校验有 {issues.Count} 个错误，保持当前档案", issues, false, _reloadGeneration);
            }

            var changed = !SameCalibration(calibration, Calibration);
            Resolver.Reload(next, calibration);
            Profiles = next;
            Calibration = calibration;
            _reloadGeneration++;
            return new FeelReloadResult(
                true, changed ? "标定行取值与此前不同：新标定已换入，进行中的动作保持原快照，下一个动作按新标定换算" : string.Empty, null, changed, _reloadGeneration);
        }

        private static bool SameCalibration(FeelCalibration a, FeelCalibration b) =>
            a.BasePresetId == b.BasePresetId && a.ReferenceHeight.Equals(b.ReferenceHeight) && a.BaseSpeed.Equals(b.BaseSpeed)
            && a.AnimationFps.Equals(b.AnimationFps) && a.ReferenceCameraHeight.Equals(b.ReferenceCameraHeight)
            && a.ReferenceZoom.Equals(b.ReferenceZoom) && a.PixelsPerUnit.Equals(b.PixelsPerUnit)
            && a.MarkerToleranceMs.Equals(b.MarkerToleranceMs);
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
