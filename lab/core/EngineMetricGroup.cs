using System;
using System.Collections.Generic;

namespace Lab
{
    /// <summary>
    /// 引擎宿主度量组（06 第 4 节"引擎侧表现组度量"）：把 <see cref="EngineRecording"/> 折算成度量。全部是表现类或真实时钟类——
    /// 逻辑类度量只由逻辑组产出，引擎宿主与无头宿主在逻辑组上逐字节一致（跨宿主不变量）。
    /// <para>
    /// 判断记录（条件组，且不在默认注册表里）：只有引擎宿主的运行带 <see cref="LabRecording.Engine"/>，因此本组是
    /// <see cref="IConditionalMetricGroup"/>；它只进 <see cref="MetricRegistry.CreateWithEngine"/> 的注册表，
    /// <see cref="MetricRegistry.CreateDefault"/> 不变——无头宿主的指纹与既有基线逐字不变。
    /// </para>
    /// <para>
    /// 判断记录（引擎侧时间口径）：动画与镜头都按"模拟时间"逐步推进（动画播放器的手动推进入口、相机的 Tick），不用墙钟，
    /// 所以对齐误差与插值曲线是确定的，可以带基线与允差；只有每帧驱动耗时（<c>frame_ms_*</c>）是真实时钟，按倍率上限比较。
    /// </para>
    /// </summary>
    public sealed class EngineMetricGroup : IConditionalMetricGroup
    {
        private static readonly MetricSpec[] SpecList =
        {
            MetricSpec.Exact("plane", MetricClass.Presentation, "格子的平面组合：2d/2_5d/3d"),
            MetricSpec.Exact("rig_kind", MetricClass.Presentation, "引擎侧玩家 rig 种类：sprite/model"),
            MetricSpec.Exact("input_noise", MetricClass.Presentation, "输入噪声模型标签（none 为无噪声）"),
            MetricSpec.Exact("frames_driven", MetricClass.Presentation, "引擎侧驱动的帧数"),
            MetricSpec.Exact("hit_align_count", MetricClass.Presentation, "逻辑命中标记（hit_frame）的次数"),
            MetricSpec.Exact("hit_align_missing", MetricClass.Presentation, "逻辑命中了但引擎动画 hit_frame 事件没有到达的次数"),
            MetricSpec.Absolute("hit_align_max_abs_ms", MetricClass.Presentation, 1.0, "动画 hit_frame 事件与逻辑命中 tick 的对齐误差绝对值最大值，毫秒（只统计到达了的）"),
            MetricSpec.Absolute("hit_align_mean_ms", MetricClass.Presentation, 1.0, "对齐误差（引擎 − 逻辑）的平均值，毫秒（只统计到达了的）"),
            MetricSpec.Exact("camera_impulse_count", MetricClass.Presentation, "引擎相机收到的镜头冲量次数"),
            MetricSpec.Absolute("camera_impulse_peak_max", MetricClass.Presentation, 0.001, "镜头冲量峰值位移最大值（世界单位）"),
            MetricSpec.Exact("camera_impulse_monotone", MetricClass.Presentation, "每条完整的有方向冲量曲线都从峰值单调衰减（1/0；没有这样的曲线为 1）"),
            MetricSpec.Absolute("camera_impulse_end_residual", MetricClass.Presentation, 0.000001, "衰减结束后相机残余位移最大值（世界单位；只看完整曲线）"),
            MetricSpec.Absolute("camera_impulse_curve_dev", MetricClass.Presentation, 0.001, "完整的有方向冲量曲线与声明的线性衰减 峰值×(1−t/衰减时长) 的最大绝对偏差（世界单位）"),
            MetricSpec.Exact("freeze_count", MetricClass.Presentation, "顿帧表现冻结次数（按单位计）"),
            MetricSpec.Absolute("freeze_rig_advance_max_ms", MetricClass.Presentation, 1.0, "冻结期间被冻结单位 rig 动画时间推进量最大值，毫秒（应为 0）"),
            MetricSpec.Absolute("freeze_particle_advance_max_ms", MetricClass.Presentation, 1.0, "冻结期间被冻结单位名下粒子播放时间推进量最大值，毫秒（应为 0；没有粒子冻结为 0）"),
            MetricSpec.Absolute("freeze_bystander_advance_min_ms", MetricClass.Presentation, 1.0, "同一区间旁观 rig 动画时间推进量最小值，毫秒（应大于 0；没有旁观 rig 为 -1）"),
            MetricSpec.Absolute("freeze_particle_control_min_ms", MetricClass.Presentation, 1.0, "同一区间对照粒子（旁观单位名下、不冻结）播放时间推进量最小值，毫秒（应大于 0；没有对照为 -1）"),
            MetricSpec.Exact("control_samples", MetricClass.Presentation, "相机相对输入的样本数"),
            MetricSpec.Absolute("control_max_error_deg", MetricClass.Presentation, 0.01, "输入方向 × 相机偏航 → 世界移动方向与真实相机轴期望方向的最大夹角误差（度）"),
            MetricSpec.Absolute("control_max_screen_error_deg", MetricClass.Presentation, 0.01, "世界移动方向经真实相机投影到屏幕后与摇杆方向的最大夹角误差（度）"),
            MetricSpec.Exact("clip_transitions", MetricClass.Presentation, "引擎侧 rig 实际播放过的剪辑切换（单位标签:剪辑 id，按发生顺序，相邻重复不记）"),
            MetricSpec.Exact("clip_transition_count", MetricClass.Presentation, "上一项的条数"),
            MetricSpec.Exact("input_visible_count", MetricClass.Presentation, "玩家攻击/闪避/技能类按下的次数（引擎侧首次可见响应度量的输入数）"),
            MetricSpec.Exact("input_visible_missing", MetricClass.Presentation, "按下之后引擎里玩家 rig 没有出现新的剪辑切换的次数（同一剪辑被再次触发时不产生切换，也计入）"),
            MetricSpec.Absolute("input_visible_ms", MetricClass.Presentation, 1.0, "每次有响应的按下：从按下 tick 的起点到玩家 rig 第一次剪辑切换（该帧驱动完成时刻）的毫秒数，按下顺序"),
            MetricSpec.Absolute("input_visible_ms_max", MetricClass.Presentation, 1.0, "上一项最大值（没有为 -1）"),
            MetricSpec.Exact("engine_errors", MetricClass.Presentation, "引擎侧装配/驱动中被吞掉的异常与诊断条数（应为 0）"),
            MetricSpec.Exact("layer_audit_count", MetricClass.Presentation, "换装场景核对的图层/图标数"),
            MetricSpec.Exact("layer_audit_mismatch", MetricClass.Presentation, "实际加载结果与期望不一致的图层/图标数"),
            MetricSpec.RatioCeiling("frame_ms_p50", MetricClass.RealTime, 10, 50, "引擎侧每帧驱动耗时中位数，毫秒（10 倍数量级桶上界）"),
            MetricSpec.RatioCeiling("frame_ms_p95", MetricClass.RealTime, 10, 50, "引擎侧每帧驱动耗时 95 分位，毫秒"),
            MetricSpec.RatioCeiling("frame_ms_max", MetricClass.RealTime, 10, 200, "引擎侧每帧驱动耗时最大值，毫秒"),
            MetricSpec.Exact("gpu_frame_status", MetricClass.RealTime, "GPU 帧耗时是否取得到：available / unavailable（没有图形设备的环境，如批处理无图形模式，为 unavailable，原因见引擎记录）"),
            MetricSpec.RatioCeiling("gpu_ms_p50", MetricClass.RealTime, 10, 50, "引擎侧每帧 GPU 完成耗时中位数，毫秒（unavailable 时为 -1）"),
            MetricSpec.RatioCeiling("gpu_ms_p95", MetricClass.RealTime, 10, 50, "引擎侧每帧 GPU 完成耗时 95 分位，毫秒（unavailable 时为 -1）"),
            MetricSpec.RatioCeiling("gpu_ms_max", MetricClass.RealTime, 10, 200, "引擎侧每帧 GPU 完成耗时最大值，毫秒（unavailable 时为 -1）"),
        };

        public string Name => "engine";

        public IReadOnlyList<MetricSpec> Specs => SpecList;

        public bool AppliesTo(LabRecording recording) => recording.Engine != null;

        public void Compute(LabRecording recording, MetricSink sink)
        {
            var engine = recording.Engine ?? throw new InvalidOperationException("engine 度量组只适用于带引擎记录的运行");
            sink.Add("plane", engine.Plane);
            sink.Add("rig_kind", engine.RigKind);
            sink.Add("input_noise", engine.InputNoise);
            sink.Add("frames_driven", engine.FramesDriven);

            var present = 0;
            var missing = 0;
            var maxAbs = 0.0;
            var sum = 0.0;
            foreach (var sample in engine.HitAlignments)
            {
                if (!sample.Present)
                {
                    missing++;
                    continue;
                }

                present++;
                var error = sample.ErrorMilliseconds;
                maxAbs = Math.Max(maxAbs, Math.Abs(error));
                sum += error;
            }

            sink.Add("hit_align_count", engine.HitAlignments.Count);
            sink.Add("hit_align_missing", missing);
            sink.Add("hit_align_max_abs_ms", maxAbs);
            sink.Add("hit_align_mean_ms", present == 0 ? 0.0 : sum / present);

            var peakMax = 0.0;
            var monotone = 1;
            var residual = 0.0;
            var curveDev = 0.0;
            foreach (var trace in engine.CameraImpulses)
            {
                peakMax = Math.Max(peakMax, trace.PeakOffset);
                if (trace.Truncated || !trace.Directional)
                {
                    continue;
                }

                curveDev = Math.Max(curveDev, trace.MaxLinearDeviation());
                for (var i = 1; i < trace.Curve.Count; i++)
                {
                    if (trace.Curve[i].Value > trace.Curve[i - 1].Value + 1e-9)
                    {
                        monotone = 0;
                        break;
                    }
                }

                if (trace.Curve.Count > 0)
                {
                    residual = Math.Max(residual, Math.Abs(trace.Curve[trace.Curve.Count - 1].Value));
                }
            }

            sink.Add("camera_impulse_count", engine.CameraImpulses.Count);
            sink.Add("camera_impulse_peak_max", peakMax);
            sink.Add("camera_impulse_monotone", monotone);
            sink.Add("camera_impulse_end_residual", residual);
            sink.Add("camera_impulse_curve_dev", curveDev);

            var rigMax = 0.0;
            var particleMax = 0.0;
            var bystanderMin = double.PositiveInfinity;
            var controlMin = double.PositiveInfinity;
            foreach (var freeze in engine.Freezes)
            {
                if (!double.IsNaN(freeze.ParticleControlAdvanceSeconds))
                {
                    controlMin = Math.Min(controlMin, freeze.ParticleControlAdvanceSeconds * 1000.0);
                }

                rigMax = Math.Max(rigMax, freeze.RigAdvanceSeconds * 1000.0);
                if (!double.IsNaN(freeze.ParticleAdvanceSeconds))
                {
                    particleMax = Math.Max(particleMax, freeze.ParticleAdvanceSeconds * 1000.0);
                }

                if (!double.IsNaN(freeze.BystanderAdvanceSeconds))
                {
                    bystanderMin = Math.Min(bystanderMin, freeze.BystanderAdvanceSeconds * 1000.0);
                }
            }

            sink.Add("freeze_count", engine.Freezes.Count);
            sink.Add("freeze_rig_advance_max_ms", rigMax);
            sink.Add("freeze_particle_advance_max_ms", particleMax);
            sink.Add("freeze_bystander_advance_min_ms", double.IsPositiveInfinity(bystanderMin) ? -1.0 : bystanderMin);
            sink.Add("freeze_particle_control_min_ms", double.IsPositiveInfinity(controlMin) ? -1.0 : controlMin);

            var controlMax = 0.0;
            var screenMax = 0.0;
            foreach (var control in engine.Controls)
            {
                controlMax = Math.Max(controlMax, control.ErrorDegrees);
                screenMax = Math.Max(screenMax, control.ScreenErrorDegrees);
            }

            sink.Add("control_samples", engine.Controls.Count);
            sink.Add("control_max_error_deg", controlMax);
            sink.Add("control_max_screen_error_deg", screenMax);
            ComputeInputVisible(recording, engine, sink);
            sink.Add("engine_errors", engine.Errors.Count);

            var mismatches = 0;
            foreach (var audit in engine.LayerAudits)
            {
                if (audit.Mismatch)
                {
                    mismatches++;
                }
            }

            sink.Add("layer_audit_count", engine.LayerAudits.Count);
            sink.Add("layer_audit_mismatch", mismatches);

            sink.Add("frame_ms_p50", Percentile(engine.FrameMilliseconds, 0.50));
            sink.Add("frame_ms_p95", Percentile(engine.FrameMilliseconds, 0.95));
            sink.Add("frame_ms_max", Percentile(engine.FrameMilliseconds, 1.0));

            // GPU 帧耗时：没取到（没有图形设备等）时状态为 unavailable、数值为 -1 的哨兵值——度量始终在场（"不可用"是被记下来的状态，不是缺失），
            // 哨兵值不超过任何倍率上限，不会让基线比较误报。
            var gpu = engine.GpuAvailable && engine.GpuFrameMilliseconds.Count > 0;
            sink.Add("gpu_frame_status", gpu ? "available" : "unavailable");
            sink.Add("gpu_ms_p50", gpu ? Percentile(engine.GpuFrameMilliseconds, 0.50) : -1.0);
            sink.Add("gpu_ms_p95", gpu ? Percentile(engine.GpuFrameMilliseconds, 0.95) : -1.0);
            sink.Add("gpu_ms_max", gpu ? Percentile(engine.GpuFrameMilliseconds, 1.0) : -1.0);
        }

        /// <summary>最近秩分位数（样本为空返回 0）。</summary>
        public static double Percentile(IReadOnlyList<double> samples, double p)
        {
            if (samples.Count == 0)
            {
                return 0.0;
            }

            var sorted = new List<double>(samples);
            sorted.Sort();
            var rank = (int)Math.Ceiling(p * sorted.Count) - 1;
            if (rank < 0)
            {
                rank = 0;
            }

            if (rank >= sorted.Count)
            {
                rank = sorted.Count - 1;
            }

            return sorted[rank];
        }

        /// <summary>
        /// 引擎侧"输入 → 首次可见响应"（06 第 3.3 节响应行表现半边、第 5 层"差值=适配层引入的延迟"）：对每个玩家的攻击/闪避/技能类按下，
        /// 取其按下 tick 起点时刻之后玩家 rig 的第一次剪辑切换，换算毫秒。输入类别取自手感记录（<see cref="FeelRecording.InputClasses"/>）；
        /// 没有手感记录或没有时刻记录的运行，按下数为 0 或全部算"没有响应"。
        /// </summary>
        private static void ComputeInputVisible(LabRecording recording, EngineRecording engine, MetricSink sink)
        {
            var transitions = new List<string>(engine.ClipTransitions);
            sink.Add("clip_transitions", string.Join(";", transitions));
            sink.Add("clip_transition_count", transitions.Count);

            var count = 0;
            var missing = 0;
            var ms = new List<double>();
            var max = -1.0;
            var feel = recording.Feel;
            if (feel != null)
            {
                foreach (var e in recording.InjectedInputs)
                {
                    if (e.Kind != ScriptEventKind.Press || e.Actor.Length != 0
                        || !feel.InputClasses.TryGetValue(e.Action, out var cls) || !(cls == "attack" || cls == "dodge" || cls == "skill"))
                    {
                        continue;
                    }

                    count++;
                    var pressSeconds = e.Tick * recording.StepSeconds;
                    var found = double.NaN;
                    for (var i = 0; i < transitions.Count && i < engine.ClipTransitionSeconds.Count; i++)
                    {
                        if (transitions[i].StartsWith("player:", StringComparison.Ordinal) && engine.ClipTransitionSeconds[i] > pressSeconds)
                        {
                            found = engine.ClipTransitionSeconds[i];
                            break;
                        }
                    }

                    if (double.IsNaN(found))
                    {
                        missing++;
                        continue;
                    }

                    var value = (found - pressSeconds) * 1000.0;
                    ms.Add(value);
                    max = Math.Max(max, value);
                }
            }

            sink.Add("input_visible_count", count);
            sink.Add("input_visible_missing", missing);
            sink.Add("input_visible_ms", ms);
            sink.Add("input_visible_ms_max", max);
        }
    }
}
