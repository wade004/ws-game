#nullable enable
// LabPlayground 的鼠标环绕镜头（ADR-0159）：3D 演示场景里右键拖动转镜头、滚轮缩放，移动随镜头偏航换算（相机相对控制空间）。
//
// 判断记录（宿主就是"游戏"）：框架只提供相机朝向查询接口与输入映射的 camera_relative 控制空间（ADR-0135），"要不要让玩家转镜头"是游戏自己的事——这里试玩宿主扮演游戏：
// ① 在自己的选项里声明移动动作用相机相对控制空间（EngineLabOptions.ControlSpaceOverride，格子数据与框架缺省仍是 world，原 3D 试玩场景与全部基线不受影响）；
// ② 相机朝向查询就是舞台上那台真实的引擎相机（UnityCamera 实现 ICameraOrientation），输入映射每次更新自己取样偏航，宿主不做任何换算；
// ③ 鼠标只改控制器的目标值（OrbitCameraController），每个控制器帧推进平滑，偏航变化录成脚本标记 camera_yaw，在固定步边界提交给相机的朝向查询——
//    所以同一个固定步内偏航不变，录下来的脚本能逐步复现同样的移动方向。
// 判断记录（固定模式 = 此前行为）：面板切到"固定"时姿态逐位复位、鼠标与滚轮被忽略；偏航恒 0，相机相对换算是恒等变换（ADR-0135 决策 3），逻辑指纹与原 3D 试玩场景逐字节一致。
using Adapter.Unity;
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Lab;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FeelLab.Unity
{
    public sealed partial class LabPlayground
    {
        private double _yawMarkerDegrees;
        private bool _orbitDragging;

        /// <summary>是否允许 3D 演示场景装鼠标环绕镜头（缺省 true；必须在 <see cref="Begin"/> 之前设置）。非 3D 演示场景恒不装。</summary>
        public bool OrbitCameraAllowed { get; set; } = true;

        /// <summary>环绕镜头开局是否打开（缺省 true = 鼠标环绕；false = 固定镜头）。必须在 <see cref="Begin"/> 之前设置。</summary>
        public bool OrbitCameraStartsEnabled { get; set; } = true;

        /// <summary>环绕镜头的灵敏度与区间（缺省取 <see cref="OrbitCameraOptions"/> 缺省）；必须在 <see cref="Begin"/> 之前设置。</summary>
        public OrbitCameraOptions? OrbitOptions { get; set; }

        /// <summary>环绕镜头控制器；这个会话没有环绕镜头（非 3D 演示场景、或相机不带俯仰）时为 null。</summary>
        public OrbitCameraController? Orbit => _stage?.Orbit;

        /// <summary>这个会话是否装了环绕镜头（不论当前开关）。</summary>
        public bool HasOrbitCamera => _stage?.Orbit != null;

        /// <summary>当前是否鼠标环绕（假 = 固定镜头或没有环绕镜头）。</summary>
        public bool OrbitCameraOn => _stage?.Orbit?.Enabled == true;

        /// <summary>本场景是否装环绕镜头：试玩宿主扩展要求（<see cref="LabPlaygroundExtension.WantsOrbitCamera"/>）、格子名以 <c>3d_</c> 开头、且没有被 <see cref="OrbitCameraAllowed"/> 关掉。</summary>
        private bool WantsOrbitCamera => Extension != null && Extension.WantsOrbitCamera(cell) && cell.StartsWith("3d_", StringComparison.Ordinal) && OrbitCameraAllowed;

        /// <summary>
        /// 切换"固定 / 鼠标环绕"（F1 面板的镜头开关）。固定 = 此前的镜头，姿态逐位复位；鼠标环绕 = 右键拖动转镜头、滚轮缩放。
        /// 偏航变化经脚本标记录进会话（见类型顶部判断记录），所以切换本身也是可回放的。
        /// </summary>
        public void SetOrbitCamera(bool on)
        {
            if (!IsBegun || _stage?.Orbit == null || _stage.Orbit.Enabled == on)
            {
                return;
            }

            _stage.SetOrbitEnabled(on);
            PumpOrbitMarker();
            _orbitDragging = false;
            Model.Note("镜头：" + (on ? "鼠标环绕（右键拖动转镜头，滚轮缩放）" : "固定"));
        }

        /// <summary>复位镜头（面板按钮）：偏航、俯角偏移、缩放平滑回到缺省；固定镜头下无事可做。</summary>
        public void ResetCamera()
        {
            if (!IsBegun || _stage?.Orbit == null || !_stage.Orbit.Enabled)
            {
                return;
            }

            _stage.Orbit.ResetSmooth();
            Model.Note("镜头已复位。");
        }

        /// <summary>
        /// 一帧的鼠标环绕输入（真实鼠标轮询与测试共用同一个入口）：<paramref name="dxPixels"/>、<paramref name="dyPixels"/> 是右键按住期间的鼠标位移（像素，向右、向上为正），
        /// <paramref name="wheelNotches"/> 是滚轮格数（向上滚为正 = 拉近）。固定镜头下忽略。
        /// </summary>
        public void OrbitInput(double dxPixels, double dyPixels, double wheelNotches)
        {
            var orbit = _stage?.Orbit;
            if (orbit == null || !orbit.Enabled)
            {
                return;
            }

            orbit.AddDrag(dxPixels, dyPixels);
            orbit.AddWheel(wheelNotches);
        }

        /// <summary>每个控制器帧在注入输入与推进会话之前调用：推进环绕镜头平滑、相机与广告牌即刻跟上，偏航变了就录一个标记。</summary>
        private void PumpOrbit(double realDeltaSeconds)
        {
            if (_replay || _stage?.Orbit == null)
            {
                return;
            }

            _stage.StepOrbit(Math.Min(realDeltaSeconds, 0.1));
            PumpOrbitMarker();
        }

        /// <summary>把环绕镜头当前偏航与最近一次录下的标记比较，变了就注入一个 <c>camera_yaw</c> 标记（固定步边界上由舞台提交给相机的朝向查询）。</summary>
        private void PumpOrbitMarker()
        {
            var orbit = _stage?.Orbit;
            if (orbit == null || Session == null || !Session.Live)
            {
                return;
            }

            if (orbit.Yaw != _yawMarkerDegrees)
            {
                Inject(ScriptEventKind.Marker, EngineLabStage.ScriptYawMarker, new Vec2(orbit.Yaw, 0.0));
                _yawMarkerDegrees = orbit.Yaw;
            }
        }

        // ───────── 真实鼠标 ─────────

        private void PollMouse()
        {
            var orbit = _stage?.Orbit;
            var mouse = Mouse.current;
            if (_replay || orbit == null || !orbit.Enabled || mouse == null)
            {
                _orbitDragging = false;
                return;
            }

            var over = PointerOverPanel(mouse.position.ReadValue());
            if (mouse.rightButton.wasPressedThisFrame)
            {
                _orbitDragging = !over && !_textFocus;
            }

            if (!mouse.rightButton.isPressed)
            {
                _orbitDragging = false;
            }

            double dx = 0.0;
            double dy = 0.0;
            if (_orbitDragging)
            {
                var delta = mouse.delta.ReadValue();
                dx = delta.x;
                dy = delta.y;
            }

            // 滚轮：Windows 下一格是 120（输入系统按原始刻度给），其它平台一格可能是 1；绝对值够大按 120 折成格数。
            var scroll = mouse.scroll.ReadValue().y;
            var wheel = 0.0;
            if (scroll != 0f && !over)
            {
                wheel = Math.Abs(scroll) >= 20f ? scroll / 120.0 : scroll;
            }

            OrbitInput(dx, dy, wheel);
        }

        /// <summary>鼠标（屏幕坐标，原点在左下）是否压在展开的调试面板上：压在面板上的滚轮归面板滚动、右键拖动不转镜头。</summary>
        private bool PointerOverPanel(Vector2 screenPosition)
        {
            if (!Model.PanelVisible)
            {
                return false;
            }

            var scale = Mathf.Clamp(Screen.height / 900f, 1f, 2f);
            return screenPosition.x <= (8f + PanelWidth) * scale;
        }

        // ───────── F1 面板的镜头段 ─────────

        /// <summary>场景页的"镜头"段：固定 / 鼠标环绕两选一、复位镜头按钮与当前姿态读数；没有环绕镜头的场景不画。</summary>
        private void DrawCameraSection(float panelWidth)
        {
            var orbit = _stage?.Orbit;
            if (orbit == null)
            {
                return;
            }

            GUILayout.Label("— 镜头 —");
            GUILayout.Label("镜头：固定 / 鼠标环绕");
            var flow = new List<KeyValuePair<string, bool>>
            {
                new KeyValuePair<string, bool>("固定", !orbit.Enabled),
                new KeyValuePair<string, bool>("鼠标环绕", orbit.Enabled),
            };
            var picks = new List<Action> { () => SetOrbitCamera(false), () => SetOrbitCamera(true) };
            DrawToggleFlow(flow, picks, panelWidth);
            GUI.enabled = orbit.Enabled;
            if (GUILayout.Button("复位镜头"))
            {
                ResetCamera();
            }

            GUI.enabled = true;
            _small ??= new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            var pitch = _stage!.StageUnityCamera != null ? _stage.StageUnityCamera.EffectivePitchDegrees : 0.0;
            GUILayout.Label(
                orbit.Enabled
                    ? "右键拖动转镜头（水平 = 偏航，垂直 = 俯角），滚轮缩放。偏航 "
                        + orbit.Yaw.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "°　俯角 "
                        + pitch.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "°　缩放 ×"
                        + (1.0 / orbit.ZoomFactor).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                    : "固定镜头：与原 3D 试玩场景相同的俯角与取景；切到鼠标环绕后移动随镜头偏航换算（W 永远朝屏幕上方走）。",
                _small);
        }
    }
}
