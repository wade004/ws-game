#nullable enable
// LabPlayground 的"脚本回放"模式（M5-S7，ADR-0151，手感设计 06 第 3.5 节"慢放到四分之一速对照"）。
//
// 判断记录（回放不是第二套运行时）：回放会话是内核的脚本会话（LabRunner.StartScript，与无头 Run 同一条数据集/格子/变体解析），
// 脚本事件按 tick 由内核注入，固定步序列与帧距无关；控制器只负责按"真实帧间隔 × 时间尺度"推进表现时钟，
// 所以慢放（0.25 倍、0.5 倍等）改变的只是每个表现帧推进的模拟时间，逻辑逐位不变——由 PlayMode 用例对照无头逻辑指纹证明。
// 回放模式下没有真实输入轮询、没有调参/评分面板、不注入任何事件（暂停、单步、时间尺度只改控制器自己的推进，不写标记事件）、不存录制；
// 推进到脚本时长后自动收尾，<see cref="ReplayLogicProjection"/> 给出可与无头宿主逐字节比较的逻辑组指纹。
using System;
using System.Globalization;
using Lab;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Adapter.Unity.LabHost
{
    public sealed partial class LabPlayground
    {
        private bool _replay;

        /// <summary>当前是否处于脚本回放模式（<see cref="BeginReplay"/> 开局）。</summary>
        public bool IsReplay => _replay;

        /// <summary>回放进度（0～1，已推进的固定步 / 脚本时长）；非回放模式为 0。</summary>
        public double ReplayProgress => _replay && Session != null && Session.DurationTicks > 0 ? Math.Min(1.0, (double)Session.Tick / Session.DurationTicks) : 0.0;

        /// <summary>回放是否已推进到脚本时长（到点后控制器自动收尾，<see cref="FinalRecording"/> 可用）。</summary>
        public bool ReplayDone => _replay && _ended;

        /// <summary>
        /// 以回放模式开局：把 <paramref name="script"/> 在当前格子上按 <paramref name="timeScale"/>（表现时钟倍率，1 = 实时，0.25 = 四分之一速）可视回放。
        /// 失败时把原因写进 <see cref="LabLiveModel.Status"/> 并返回 false（不抛）。
        /// </summary>
        public bool BeginReplay(InputScript script, double timeScale = 1.0)
        {
            if (IsBegun)
            {
                return true;
            }

            if (!(timeScale > 0.0) || double.IsInfinity(timeScale))
            {
                Model.Note("回放时间尺度必须是有限正数：" + timeScale.ToString(CultureInfo.InvariantCulture));
                return false;
            }

            try
            {
                _repoRoot = string.IsNullOrEmpty(repoRoot) ? EngineLabHost.LocateRepoRoot() : repoRoot;
                _host = new EngineLabHost(_repoRoot, baseDataRoots, null);
                _savePath = string.IsNullOrEmpty(SaveDirectory) ? System.IO.Path.Combine(_repoRoot, "lab", "out", "playground") : SaveDirectory!;
                _filter = new LabEffectFilter();
                _filter.Submitted += OnFeedbackSubmitted;
                var options = new EngineLabOptions
                {
                    Interactive = true,
                    Effects = _filter,
                    HonorCellCameraMode = true,
                    GpuTiming = false,
                    ProbeParticles = false,
                };
                _stage = new EngineLabStage(options);
                Session = _host.Runner.StartScript(script, cell, null, _stage);
                BuildModel();
                Model.TimeScale = timeScale;
                _ended = false;
                _replay = true;
                Model.Note("脚本回放：" + script.Meta.ScriptId + "（" + cell + "）×" + timeScale.ToString("0.##", CultureInfo.InvariantCulture)
                    + "。P 暂停，. 推进一个 tick，, 推进一个表现帧，[ ] 切时间尺度。");
                return true;
            }
            catch (Exception ex)
            {
                Model.Note("回放启动失败：" + ex.Message);
                UnityEngine.Debug.LogError("[LabPlayground] 回放启动失败：" + ex);
                return false;
            }
        }

        /// <summary>
        /// 回放结束后的逻辑组指纹文本（默认注册表、仅逻辑类度量；与 <c>EngineLabHost.RunHeadlessLogic</c> 同格式，可逐字节比较）；
        /// 回放未结束或非回放模式返回空串。
        /// </summary>
        public string ReplayLogicProjection()
        {
            if (!_replay || FinalRecording == null || _host == null || Session == null)
            {
                return string.Empty;
            }

            var fingerprint = _host.Runner.FingerprintOf(Session.Script, cell, FinalRecording);
            return fingerprint.Project(_host.HeadlessRunner.Registry, MetricClass.Logic);
        }

        /// <summary>回放模式的一个控制器帧：按时间尺度/暂停推进；到脚本时长自动收尾。</summary>
        private void TickReplay(double realDeltaSeconds)
        {
            var session = Session!;
            try
            {
                Model.FrameIntervalMs.Add(realDeltaSeconds * 1000.0);
                RunDeferred();
                var dt = Math.Min(realDeltaSeconds, 0.1) * Model.TimeScale;
                if (!Model.Paused && dt > 0.0)
                {
                    // 不越过脚本时长：剩余固定步数对应的模拟时间封顶（累加器里的零头小于一个固定步，所以最多补满剩余步数）。
                    var remaining = Math.Max(0, session.DurationTicks - session.Tick);
                    if (remaining > 0)
                    {
                        AdvanceSession(Math.Min(dt, remaining * session.StepSeconds));
                    }
                }

                AfterAdvance();
                Model.Tick = session.Tick;
                if (session.Tick >= session.DurationTicks)
                {
                    FinishReplay();
                }
            }
            catch (Exception ex)
            {
                Model.Paused = true;
                Model.Note("回放出错已暂停：" + ex.Message);
                UnityEngine.Debug.LogError("[LabPlayground] " + ex);
            }
        }

        private void FinishReplay()
        {
            if (_ended)
            {
                return;
            }

            if (_filter != null)
            {
                _filter.Submitted -= OnFeedbackSubmitted;
            }

            FinalRecording = Session!.Finish();
            _ended = true;
            _stage?.Dispose();
            Model.Note("回放结束（tick " + Session.Tick + "）。");
        }

        /// <summary>回放模式的热键：只开放面板显隐、暂停、单步、时间尺度（这些都不写任何事件）。</summary>
        private void PollReplayHotkeys(Keyboard kb)
        {
            if (kb.f1Key.wasPressedThisFrame) Model.PanelVisible = !Model.PanelVisible;
            if (kb.pKey.wasPressedThisFrame) TogglePause();
            if (kb.periodKey.wasPressedThisFrame) StepTick();
            if (kb.commaKey.wasPressedThisFrame) StepFrame();
            if (kb.leftBracketKey.wasPressedThisFrame) CycleTimeScale(+1);
            if (kb.rightBracketKey.wasPressedThisFrame) CycleTimeScale(-1);
        }

        /// <summary>回放模式的屏上提示（进度、尺度、暂停；不画任何面板页）。</summary>
        private void DrawReplayHud()
        {
            var text = "脚本回放 " + Model.Cell + "　tick " + Model.Tick + "/" + (Session?.DurationTicks ?? 0)
                + "　×" + Model.TimeScale.ToString("0.##", CultureInfo.InvariantCulture) + (Model.Paused ? "　[暂停]" : string.Empty)
                + (_ended ? "　[结束]" : string.Empty)
                + "　P 暂停　. 单步　, 单帧　[ ] 时间尺度";
            GUI.Label(new UnityEngine.Rect(12, 12, 900, 24), text);
        }
    }
}
