#nullable enable
// LabPlayground：手感实验室的"人手试玩"宿主（ADR-0141，手感设计 06 第 1/2/4 节、判断记录 46 的延伸）。
//
// 它不是第二套运行时：背后是内核的同一个宿主装配（Lab.LabHost.Start → LabSession）加引擎舞台（EngineLabStage，试玩模式），
// 每个渲染帧做三件事——① 轮询真实输入（LabLiveInput）→ 换成盖当前 tick 戳的脚本事件注入会话；② 推进会话；③ 更新面板模型。
// 面板上每个会影响逻辑的操作（出靶子、清场、换武器/体型、切预设、A/B、关顿帧）都落成同一种脚本事件注入，所以整局可以存成本地脚本
// （lab/out/playground/，已被忽略规则覆盖），交给无头的 FeelLab run 逐 tick 重放，逻辑组逐字节一致；
// 纯呈现的操作（时间尺度、暂停/单步、单项效果开关、角落闪块）落成标记事件，逻辑不读它、只为重放时复现画面。
// 面板里提供不了"录得下来"的操作——没有录不下来的逻辑操作。
//
// 判断记录（数据根可换）：基础数据根、额外数据根、靶子集、仓库根都是可序列化字段，缺省是框架根 + 实验室数据集 + 实验室动作式数据根；
// 游戏接入时把它们换成自己的根即可，宿主不读任何写死的路径（动作式实验室输入动作的槽位绑定见 LabLive.DefaultSkillSlots，同样可换）。
// 判断记录（实验室面板，ADR-0150）：调参、帧数据时间轴、轨迹叠层、评分四页在 LabPlayground.Panels.cs，全部由内核里的纯 C# 视图模型驱动；
// 预设切换、A/B、顿帧开关经调参面板（TuningPanel）落成同一种脚本事件，所以 A/B 槽位各自带自己的覆盖组。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Adapter.Unity.EngineAdapter;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Core.Foundation.Feel;
using Core.Foundation.InputMap;
using Lab;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;

namespace Adapter.Unity.LabHost
{
    public sealed partial class LabPlayground : MonoBehaviour
    {
        public const string EliteSwingSkill = "skill.lab_a_elite_swing";

        // ───────── 可序列化配置（场景里按格子填，缺省即实验室动作式数据根）─────────
        [SerializeField] private string cell = "2d_action";
        [SerializeField] private string[] baseDataRoots = { "data/_framework", "data/_lab" };
        [SerializeField] private string[] extraDataRoots = { "data/_feel", "data/_feel_templates", "data/_lab_action", "lab/fixtures/data/feel_templates" };
        [SerializeField] private string dummySet = LabLive.DefaultDummySet;
        [SerializeField] private string repoRoot = string.Empty;
        [SerializeField] private bool autoStart = true;

        /// <summary>演示场景的数据根（只含外形表：动画集与武器风格行；不含任何逻辑表）。</summary>
        public const string ShowcaseDataRoot = "data/_showcase";

        /// <summary>3D 演示场景追加的数据根（ADR-0158；同样只含外形表：模型型的动画集与武器风格行）。</summary>
        public const string Showcase3dDataRoot = "data/_showcase_3d";

        /// <summary>演示场景（ADR-0154）：同一套逻辑与手感运行时，换真实美术呈现 + 游戏内 HUD，调试面板缺省收起。</summary>
        [SerializeField] private bool showcase;

        private ShowcaseHud? _hud;

        private EngineLabHost? _host;
        private EngineLabStage? _stage;
        private LabEffectFilter? _filter;
        private LabLiveInput? _poller;
        private string _repoRoot = string.Empty;
        private string _savePath = string.Empty;
        private bool _ended;
        private int _eventCursor;
        private double _lastAdvanceStartReal;
        private readonly List<ActionDefinition> _actions = new List<ActionDefinition>();
        private readonly List<PendingInput> _pending = new List<PendingInput>();
        private readonly List<PendingInput> _awaitingVisible = new List<PendingInput>();
        private readonly List<int> _pendingHits = new List<int>();
        private readonly List<KeyValuePair<int, double>> _submissions = new List<KeyValuePair<int, double>>();
        private readonly Dictionary<string, KeyValuePair<string, bool>> _effectMarkers = new Dictionary<string, KeyValuePair<string, bool>>();
        private readonly Stopwatch _watch = new Stopwatch();
        private readonly List<KeyValuePair<string, string>> _live = new List<KeyValuePair<string, string>>();
        private int _spawnSerial;
        private int _frameCounter;

        private sealed class PendingInput
        {
            public int Tick;
            public double RealTime;
            public int Frame;
            public double AcceptRealTime;
            public int AcceptTick;
        }

        /// <summary>面板模型（读它即可断言面板内容，不依赖任何界面）。</summary>
        public LabLiveModel Model { get; } = new LabLiveModel();

        public LabSession? Session { get; private set; }

        public EngineLabStage? Stage => _stage;

        public EngineLabHost? Host => _host;

        public LabEffectFilter? Effects => _filter;

        /// <summary>演示场景开关；必须在 <see cref="Begin"/> 之前设置。</summary>
        public bool Showcase
        {
            get => showcase;
            set => showcase = value;
        }

        /// <summary>演示场景的游戏内 HUD（非演示场景为 null）。</summary>
        public ShowcaseHud? Hud => _hud;

        /// <summary>
        /// 某个格子（视角）的演示场景在菜单里的条目名（菜单路径 <c>GameFoundation/手感试玩/&lt;条目名&gt;</c>，见编辑器的 <c>LabPlaygroundSceneBuilder</c>）；
        /// 不是 2D/2.5D/3D 动作格子时为 null。占位美术场景的面板提示按它指向"同一视角"的演示场景。
        /// </summary>
        public static string? ShowcaseMenuNameOf(string cell)
        {
            switch (cell)
            {
                case "2d_action": return "打开演示场景 2D（真实美术）";
                case "2_5d_action": return "打开演示场景 2.5D（真实美术）";
                case "3d_action": return "打开演示场景 3D（真实美术）";
                default: return null;
            }
        }

        /// <summary>占位美术工程场景的面板提示：指向与本场景同一格子的演示场景；没有对应演示场景的格子为 null。</summary>
        public static string? PlaceholderHintFor(string cell)
        {
            var name = ShowcaseMenuNameOf(cell);
            return name == null ? null : "占位美术工程场景；真实美术与界面皮肤请用菜单 手感试玩 → " + name;
        }

        /// <summary>面板头部第二行的提示文字（演示场景为 null：用户已经在真实美术里，不需要再被指路）。</summary>
        public string? PanelHint => showcase ? null : PlaceholderHintFor(cell);

        public string RepoRoot => _repoRoot;

        public string Cell
        {
            get => cell;
            set => cell = value;
        }

        public bool IsBegun => Session != null && !_ended;

        /// <summary>真实输入来源（缺省 <c>UnityEngineHost.Ensure().Input</c>，即真实输入适配器）；测试可在 <see cref="Begin"/> 之前换成假输入。</summary>
        public IInput? InputSource { get; set; }

        /// <summary>手柄按钮读取（缺省读 Input System 的当前手柄）；测试可换。</summary>
        public Func<string, bool>? PadReader { get; set; }

        /// <summary>为真时 <c>Update</c> 不自己推进（测试手动调 <see cref="Tick"/>）。</summary>
        public bool ManualDrive { get; set; }

        public LabLiveInput? Poller => _poller;

        /// <summary>换输入来源（测试用：会话开始后把真实输入换成假输入；轮询器按同一份动作定义重建）。</summary>
        public void UseInput(IInput input, Func<string, bool>? padReader = null)
        {
            InputSource = input;
            PadReader = padReader ?? PadReader;
            _poller = new LabLiveInput(input, _actions, "input.action.move", PadReader);
        }

        /// <summary>录制脚本的保存目录（缺省 <c>&lt;仓库根&gt;/lab/out/playground</c>，被忽略规则覆盖、不入库）；必须在 <see cref="Begin"/> 之前设置。</summary>
        public string? SaveDirectory { get; set; }

        /// <summary>配置（给测试/代码建场景用）：必须在 <see cref="Begin"/> 之前调用。</summary>
        public void Configure(string cellShortName, IEnumerable<string>? baseRoots = null, IEnumerable<string>? extraRoots = null, string? dummySetId = null, string? repo = null)
        {
            cell = cellShortName;
            if (baseRoots != null) baseDataRoots = new List<string>(baseRoots).ToArray();
            if (extraRoots != null) extraDataRoots = new List<string>(extraRoots).ToArray();
            if (dummySetId != null) dummySet = dummySetId;
            if (repo != null) repoRoot = repo;
        }

        private void Start()
        {
            if (autoStart && !IsBegun)
            {
                Begin();
            }
        }

        // ───────── 开局 ─────────

        /// <summary>装配宿主与会话；失败时把原因写进 <see cref="LabLiveModel.Status"/> 并返回 false（不抛）。</summary>
        public bool Begin()
        {
            if (IsBegun)
            {
                return true;
            }

            try
            {
                _repoRoot = string.IsNullOrEmpty(repoRoot) ? EngineLabHost.LocateRepoRoot() : repoRoot;
                _host = new EngineLabHost(_repoRoot, baseDataRoots, null);
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _savePath = string.IsNullOrEmpty(SaveDirectory) ? Path.Combine(_repoRoot, "lab", "out", "playground") : SaveDirectory!;
                var roots = new List<string>(extraDataRoots);
                if (File.Exists(Path.Combine(LocalPresetRoot, "feel", "feel.preset.json")))
                {
                    // 调参面板"保存为预设"写出的本地预设自动并入，预设列表里就能选到（本地、被忽略规则覆盖、不入库）。
                    roots.Add(LocalPresetRoot);
                }

                if (showcase && !roots.Contains(ShowcaseDataRoot))
                {
                    roots.Add(ShowcaseDataRoot);
                }

                if (showcase && cell.StartsWith("3d_", StringComparison.Ordinal) && !roots.Contains(Showcase3dDataRoot))
                {
                    // 3D 演示场景（ADR-0158）：模型型外形的动画集与武器风格行（真实骨骼剪辑），格子名以 3d_ 开头的才并入。
                    roots.Add(Showcase3dDataRoot);
                }

                var script = LabLive.CreateScript("playground_" + cell + "_" + stamp, 60, 60, roots, dummySet);
                _filter = new LabEffectFilter();
                _filter.Submitted += OnFeedbackSubmitted;
                var options = new EngineLabOptions
                {
                    Interactive = true,
                    Effects = _filter,
                    HonorCellCameraMode = true,
                    GpuTiming = false,
                    ProbeParticles = false,
                    Showcase = showcase,
                };
                _stage = new EngineLabStage(options);
                Session = _host.Runner.StartLive(script, cell, null, _stage);
                BuildModel();
                if (showcase)
                {
                    // 演示场景：调试面板缺省收起（F1 展开），屏上只留游戏内 HUD。
                    Model.PanelVisible = false;
                    _hud = ShowcaseHud.Create(_stage, transform);
                }

                InputSource ??= UnityEngineHost.Ensure().Input;
                var ctx = Session.Context!;
                _actions.Clear();
                foreach (var record in ctx.World.Registry.GetAll("found.input_action"))
                {
                    _actions.Add(ActionDefinition.FromRecord(record));
                }

                _poller = new LabLiveInput(InputSource, _actions, "input.action.move", PadReader);
                _ended = false;
                _replay = false;
                InitPanels();
                Model.Note("试玩会话已开始（" + cell + "）。F1 显示/隐藏面板，F12 切页（场景/调参/时间轴/轨迹/评分）。");
                return true;
            }
            catch (Exception ex)
            {
                Model.Note("试玩会话启动失败：" + ex.Message);
                Debug.LogError("[LabPlayground] 启动失败：" + ex);
                return false;
            }
        }

        private void BuildModel()
        {
            var ctx = Session!.Context!;
            Model.Cell = cell;
            var feel = ctx.World.Gameplay.Feel?.Feel;
            var presets = new List<LabChoice>();
            var weapons = new List<LabChoice> { new LabChoice(string.Empty, "无") };
            var archetypes = new List<LabChoice> { new LabChoice(string.Empty, "无") };
            if (feel != null)
            {
                foreach (var row in feel.Profiles.Presets) presets.Add(new LabChoice(row.Id, LabLiveModel.FriendlyName(row.Id)));
                foreach (var row in feel.Profiles.Weapons) weapons.Add(new LabChoice(row.Id, LabLiveModel.FriendlyName(row.Id)));
                foreach (var row in feel.Profiles.Archetypes) archetypes.Add(new LabChoice(row.Id, LabLiveModel.FriendlyName(row.Id)));
            }

            Model.Presets = presets;
            Model.Weapons = weapons;
            Model.Archetypes = archetypes;
            Model.Preset = ctx.Cell.DefaultPreset;
            Model.PresetA = ctx.Cell.DefaultPreset;
            Model.PresetB = string.Empty;
            foreach (var p in presets)
            {
                if (!string.Equals(p.Id, Model.PresetA, StringComparison.Ordinal))
                {
                    Model.PresetB = p.Id;
                    break;
                }
            }

            var kinds = new List<LabChoice>();
            var catalog = new LabCatalog(ctx.World.Registry);
            var set = catalog.GetDummySet(new Id(dummySet));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in set.Entries)
            {
                if (entry.Name.StartsWith("pack_", StringComparison.Ordinal) || !seen.Add(entry.Name))
                {
                    continue;
                }

                kinds.Add(new LabChoice(entry.Name, LabLiveModel.FriendlyName(entry.Name)));
            }

            Model.DummyKinds = kinds;
            Model.EffectChannels = LabEffectFilter.Channels;
            Model.EffectOn = c => _filter != null && _filter.IsOn(c);
            Model.EffectSubmitted = c => _filter?.SubmittedCount(c) ?? 0;
            Model.EffectSuppressed = c => _filter?.SuppressedCount(c) ?? 0;
        }

        // ───────── 每帧 ─────────

        private void Update()
        {
            if (!IsBegun || ManualDrive)
            {
                return;
            }

            if (!_textFocus)
            {
                PollHotkeys();
            }

            Tick(Time.unscaledDeltaTime);
        }

        private void LateUpdate()
        {
            if (IsBegun && !ManualDrive)
            {
                FinishFrame();
            }
        }

        /// <summary>
        /// 一个控制器帧：轮询真实输入并注入、按时间尺度/暂停推进会话、更新面板模型与指标。
        /// <paramref name="realDeltaSeconds"/> 是真实帧间隔（秒）。
        /// </summary>
        public void Tick(double realDeltaSeconds)
        {
            if (!IsBegun)
            {
                return;
            }

            if (_replay)
            {
                TickReplay(realDeltaSeconds);
                return;
            }

            var session = Session!;
            try
            {
                Model.FrameIntervalMs.Add(realDeltaSeconds * 1000.0);
                RunDeferred();
                if (!_textFocus)
                {
                    _poller?.Poll(OnInputEvent);
                }

                var dt = Math.Min(realDeltaSeconds, 0.1) * Model.TimeScale;
                if (!Model.Paused && dt > 0.0)
                {
                    AdvanceSession(dt);
                }

                AfterAdvance();
            }
            catch (Exception ex)
            {
                Model.Paused = true;
                Model.Note("会话出错已暂停：" + ex.Message);
                Debug.LogError("[LabPlayground] " + ex);
            }

            Model.Tick = session.Tick;
        }

        private void AdvanceSession(double seconds)
        {
            _lastAdvanceStartReal = RealNow();
            _submissions.Clear();
            _watch.Restart();
            Session!.Advance(seconds);
            _watch.Stop();
            Model.AdvanceMs.Add(_watch.Elapsed.TotalMilliseconds);
        }

        private static double RealNow() => Time.realtimeSinceStartupAsDouble;

        private void OnInputEvent(ScriptEvent e)
        {
            Session!.Inject(e);
            if (e.Kind == ScriptEventKind.Press)
            {
                _pending.Add(new PendingInput { Tick = Session.Tick, RealTime = RealNow(), Frame = _frameCounter });
                if (Model.CornerFlash)
                {
                    Model.CornerFlashFrames = 4;
                }
            }
        }

        private void OnFeedbackSubmitted(string channel, int tick)
        {
            _submissions.Add(new KeyValuePair<int, double>(tick, RealNow()));
        }

        private void AfterAdvance()
        {
            var recording = Session!.Recording;
            for (; _eventCursor < recording.Events.Count; _eventCursor++)
            {
                var e = recording.Events[_eventCursor];
                if (e.Kind == "cast_success" && string.Equals(e.Source, "player", StringComparison.Ordinal))
                {
                    for (var i = 0; i < _pending.Count; i++)
                    {
                        if (_pending[i].Tick <= e.Tick)
                        {
                            var p = _pending[i];
                            _pending.RemoveAt(i);
                            p.AcceptTick = e.Tick;
                            p.AcceptRealTime = RealNow();
                            Model.InputToAcceptTicks.Add(e.Tick - p.Tick);
                            Model.InputToAcceptMs.Add((p.AcceptRealTime - p.RealTime) * 1000.0);
                            _awaitingVisible.Add(p);
                            break;
                        }
                    }
                }
                else if (e.Kind == "damage" && string.Equals(e.Source, "player", StringComparison.Ordinal))
                {
                    _pendingHits.Add(e.Tick);
                }
            }

            // 命中确认 → 首个反馈提交：同一次推进里提交了反馈的取"不早于命中 tick 的最早提交"。
            for (var i = _pendingHits.Count - 1; i >= 0; i--)
            {
                KeyValuePair<int, double>? best = null;
                foreach (var s in _submissions)
                {
                    if (s.Key >= _pendingHits[i] && (!best.HasValue || s.Key < best.Value.Key))
                    {
                        best = s;
                    }
                }

                if (best.HasValue)
                {
                    Model.HitToFeedbackTicks.Add(best.Value.Key - _pendingHits[i]);
                    Model.HitToFeedbackMs.Add(Math.Max(0.0, (best.Value.Value - _lastAdvanceStartReal) * 1000.0));
                    _pendingHits.RemoveAt(i);
                }
            }

            // 丢弃久未接受的按键（被缓冲吞掉、被冷却拒绝等：没有 cast_success 就不是"动作接受"）。
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                if (Session.Tick - _pending[i].Tick > 120)
                {
                    _pending.RemoveAt(i);
                }
            }

            for (var i = _pendingHits.Count - 1; i >= 0; i--)
            {
                if (Session.Tick - _pendingHits[i] > 120)
                {
                    _pendingHits.RemoveAt(i);
                }
            }

            Model.DummyCount = _live.Count;
            Model.RecordedEvents = Session.Script.Events.Count;
            AfterAdvancePanels();
        }

        /// <summary>帧尾（渲染提交之前）：完成"首次可见响应"的计时。</summary>
        public void FinishFrame()
        {
            if (!IsBegun)
            {
                return;
            }

            var now = RealNow();
            foreach (var p in _awaitingVisible)
            {
                Model.InputToVisibleMs.Add((now - p.RealTime) * 1000.0);
                Model.InputToVisibleFrames.Add(Math.Max(1, _frameCounter - p.Frame + 1));
            }

            _awaitingVisible.Clear();
            _frameCounter++;
            if (Model.CornerFlashFrames > 0)
            {
                Model.CornerFlashFrames--;
            }
        }

        // ───────── 热键 ─────────

        private void PollHotkeys()
        {
            var kb = Keyboard.current;
            if (kb == null)
            {
                return;
            }

            if (_replay)
            {
                PollReplayHotkeys(kb);
                return;
            }

            PollTabHotkey(kb);
            if (kb.f1Key.wasPressedThisFrame) Model.PanelVisible = !Model.PanelVisible;
            if (kb.f2Key.wasPressedThisFrame) Model.HelpVisible = !Model.HelpVisible;
            if (kb.tabKey.wasPressedThisFrame) SwitchAb();
            if (kb.pKey.wasPressedThisFrame) TogglePause();
            if (kb.periodKey.wasPressedThisFrame) StepTick();
            if (kb.commaKey.wasPressedThisFrame) StepFrame();
            if (kb.leftBracketKey.wasPressedThisFrame) CycleTimeScale(+1);
            if (kb.rightBracketKey.wasPressedThisFrame) CycleTimeScale(-1);
            if (kb.backspaceKey.wasPressedThisFrame) ClearDummies();
            if (kb.f5Key.wasPressedThisFrame) SaveRecording();
            if (kb.f3Key.wasPressedThisFrame) CycleWeapon();
            if (kb.f4Key.wasPressedThisFrame) CycleArchetype();
            if (kb.gKey.wasPressedThisFrame) EliteSwing();
            if (kb.f6Key.wasPressedThisFrame) ToggleEffect(LabEffectFilter.Shake);
            if (kb.f7Key.wasPressedThisFrame) ToggleEffect(LabEffectFilter.Flash);
            if (kb.f8Key.wasPressedThisFrame) SetHitStop(!Model.HitStopOn);
            if (kb.f9Key.wasPressedThisFrame) ToggleEffect(LabEffectFilter.Sfx);
            if (kb.f10Key.wasPressedThisFrame) ToggleEffect(LabEffectFilter.CameraImpulse);
            if (kb.f11Key.wasPressedThisFrame) ToggleCornerFlash();
            var keys = new[] { kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key, kb.digit6Key, kb.digit7Key, kb.digit8Key };
            for (var i = 0; i < keys.Length && i < Model.DummyKinds.Count; i++)
            {
                if (keys[i].wasPressedThisFrame)
                {
                    SpawnDummy(Model.DummyKinds[i].Id, kb.leftShiftKey.isPressed ? Model.GroupCount : 1);
                }
            }
        }

        // ───────── 命令（面板按钮与热键共用；每个逻辑命令一个脚本事件）─────────

        private void Inject(ScriptEventKind kind, string action, Vec2 value = default, string actor = "")
        {
            if (!Session!.Live)
            {
                // 脚本回放会话不接受实时事件：暂停/单步/时间尺度在回放里只改控制器自己的推进，不写标记事件。
                return;
            }

            Session!.Inject(new ScriptEvent(0, action, kind, value, null, actor));
        }

        /// <summary>在玩家前方出 <paramref name="count"/> 只靶子（<paramref name="count"/> &gt; 1 时沿垂直方向扇形排开）；巡逻靶在数据声明的位置出场（它的巡逻路径在世界坐标里）。</summary>
        public void SpawnDummy(string kind, int count = 1)
        {
            if (!IsBegun)
            {
                return;
            }

            var ctx = Session!.Context!;
            if (_live.Count + count > 60)
            {
                Model.Note("场上靶子已多（60 只上限），先清场。");
                return;
            }

            var catalog = new LabCatalog(ctx.World.Registry);
            LabDummy? entry = null;
            foreach (var e in catalog.GetDummySet(new Id(dummySet)).Entries)
            {
                if (string.Equals(e.Name, kind, StringComparison.Ordinal))
                {
                    entry = e;
                    break;
                }
            }

            if (entry == null)
            {
                Model.Note("靶子集里没有条目 " + kind);
                return;
            }

            var ticks = Session.Recording.Ticks;
            var player = ticks.Count > 0 ? ticks[ticks.Count - 1].Position : Vec2.Zero;
            var facing = ticks.Count > 0 ? ticks[ticks.Count - 1].Facing : 0.0;
            var fwd = new Vec2(Math.Cos(facing), Math.Sin(facing));
            var side = new Vec2(-fwd.Y, fwd.X);
            for (var i = 0; i < count; i++)
            {
                Vec2 pos;
                if (string.Equals(entry.Kind, "patrol", StringComparison.Ordinal))
                {
                    pos = entry.Position;
                }
                else
                {
                    var spread = count > 1 ? (i - (count - 1) / 2.0) * 0.9 : 0.0;
                    var back = count > 1 ? Math.Abs(spread) * 0.3 : 0.0;
                    var dist = 2.2 + back;
                    pos = new Vec2(player.X + fwd.X * dist + side.X * spread, player.Y + fwd.Y * dist + side.Y * spread);
                }

                var label = kind + "@" + (++_spawnSerial).ToString(System.Globalization.CultureInfo.InvariantCulture);
                Inject(ScriptEventKind.Spawn, kind, pos, label);
                _live.Add(new KeyValuePair<string, string>(label, entry.Kind));
            }

            Model.Note("出靶子：" + LabLiveModel.FriendlyName(kind) + (count > 1 ? " ×" + count : string.Empty));
        }

        public void ClearDummies()
        {
            if (!IsBegun)
            {
                return;
            }

            Inject(ScriptEventKind.ClearDummies, "clear");
            _live.Clear();
            Model.Note("已清场。");
        }

        /// <summary>让每只在场精英出一次挥击（靶子出手，脚本 cast 事件）。</summary>
        public void EliteSwing()
        {
            if (!IsBegun)
            {
                return;
            }

            var n = 0;
            foreach (var d in _live)
            {
                if (string.Equals(d.Value, "elite", StringComparison.Ordinal))
                {
                    Inject(ScriptEventKind.Cast, EliteSwingSkill, default, d.Key);
                    n++;
                }
            }

            Model.Note(n > 0 ? "精英出手 ×" + n : "场上没有精英（先按数字键出一只）。");
        }

        /// <summary>
        /// 换武器（<c>feel.weapon.*</c> 行；空串 = 卸下）。判断记录：开局不装武器，让预设值直接生效（A/B 才有意义）；
        /// 武器/体型行写的字段以玩家单位调试覆盖的形式叠在预设之上，所以装了武器之后，武器写过的字段（单手剑与巨剑都写了顿帧、冲击等级等）
        /// 切预设不再变化——想比较预设的顿帧差请先卸下武器。已知限制：调试覆盖层位于动作层之上，动作自己的 feel_ref 值（如终结技的顿帧）
        /// 在这些字段上会被武器值压平。
        /// </summary>
        public void SetWeapon(string rowId) => ApplyLoadout(rowId, quiet: false);

        public void SetArchetype(string rowId) => ApplyLoadout(rowId, quiet: false);

        private void ApplyLoadout(string rowId, bool quiet)
        {
            if (!IsBegun)
            {
                return;
            }

            if (rowId.Length > 0)
            {
                var isWeapon = rowId.StartsWith("feel.weapon.", StringComparison.Ordinal);
                var isArchetype = rowId.StartsWith("feel.archetype.", StringComparison.Ordinal);
                if (!isWeapon && !isArchetype)
                {
                    throw new ArgumentException("不是 feel.weapon/feel.archetype 行：" + rowId);
                }

                Inject(ScriptEventKind.Loadout, rowId);
                if (isWeapon) Model.Weapon = rowId; else Model.Archetype = rowId;
            }
            else
            {
                Inject(ScriptEventKind.Loadout, string.Empty);
                Model.Weapon = string.Empty;
                Model.Archetype = string.Empty;
            }

            if (!quiet)
            {
                Model.Note("武器：" + LabLiveModel.FriendlyName(Model.Weapon) + "　体型：" + LabLiveModel.FriendlyName(Model.Archetype));
            }
        }

        public void CycleWeapon() => CycleLoadout(Model.Weapons, Model.Weapon);

        public void CycleArchetype() => CycleLoadout(Model.Archetypes, Model.Archetype);

        private void CycleLoadout(IReadOnlyList<LabChoice> choices, string current)
        {
            if (choices.Count == 0)
            {
                return;
            }

            var index = 0;
            for (var i = 0; i < choices.Count; i++)
            {
                if (string.Equals(choices[i].Id, current, StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }

            ApplyLoadout(choices[(index + 1) % choices.Count].Id, quiet: false);
        }

        /// <summary>
        /// 切基础预设（运行中标定热换：进行中的动作沿用开始时的快照，下一个动作按新预设，05 第 8 节）。
        /// 同时把它写进当前槽位（A/B），使 <see cref="SwitchAb"/> 能回来。
        /// </summary>
        public void SetPreset(string presetId)
        {
            if (!IsBegun || string.IsNullOrEmpty(presetId) || _tuning == null)
            {
                return;
            }

            _tuning.SetPreset(presetId);
            SyncFromTuning();
            Model.Note("预设：" + LabLiveModel.FriendlyName(presetId) + "（槽位 " + Model.ActiveSlot + "）");
        }

        /// <summary>
        /// A/B 槽位瞬间切换（热键 Tab）：每个槽位各自持有基础预设与自己的一组覆盖（调参面板里改的值），切换 = 预设事件（预设不同时）
        /// + 清空覆盖 + 重注入目标槽位的覆盖，全部落进脚本，可无头重放。
        /// </summary>
        public void SwitchAb()
        {
            if (!IsBegun || _tuning == null)
            {
                return;
            }

            _tuning.SwitchSlot();
            SyncFromTuning();
            Model.Note("A/B 切到槽位 " + Model.ActiveSlot + "：" + LabLiveModel.FriendlyName(Model.Preset) + "（覆盖 " + _tuning.ActiveWrites.Count + " 条）");
        }

        /// <summary>
        /// 顿帧开关：局部顿帧是判定型手感（改的是逻辑时钟），所以关顿帧是一条录进脚本的覆盖事件
        /// （攻击方/受击方顿帧时长置 0，按槽位各自保存），不是呈现闸；重新打开 = 清掉这两条覆盖。
        /// </summary>
        public void SetHitStop(bool on)
        {
            if (!IsBegun || _tuning == null || on == _tuning.HitStopOn)
            {
                return;
            }

            _tuning.SetHitStop(on);
            SyncFromTuning();
            Model.Note("顿帧：" + (on ? "开" : "关（逻辑覆盖，已录入脚本）"));
        }

        /// <summary>单项呈现通道开关（震屏/闪白/音效/镜头冲击）：纯呈现，落成标记事件。</summary>
        public void ToggleEffect(string channel)
        {
            if (!IsBegun || _filter == null)
            {
                return;
            }

            var on = !_filter.IsOn(channel);
            _filter.Set(channel, on);
            Inject(ScriptEventKind.Marker, "effect:" + channel, new Vec2(on ? 1.0 : 0.0, 0.0));
            Model.Note("呈现通道 " + channel + "：" + (on ? "开" : "关"));
        }

        public void ToggleCornerFlash()
        {
            if (!IsBegun)
            {
                return;
            }

            Model.CornerFlash = !Model.CornerFlash;
            Inject(ScriptEventKind.Marker, "corner_flash", new Vec2(Model.CornerFlash ? 1.0 : 0.0, 0.0));
            Model.Note("输入瞬间角落闪块：" + (Model.CornerFlash ? "开（外接相机测延迟用）" : "关"));
        }

        public void SetTimeScale(double scale)
        {
            if (!IsBegun)
            {
                return;
            }

            Model.TimeScale = scale;
            Inject(ScriptEventKind.Marker, "time_scale", new Vec2(scale, 0.0));
            Model.Note("时间尺度 ×" + scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        }

        private static readonly double[] TimeScales = { 1.0, 0.5, 0.25 };

        public void CycleTimeScale(int direction)
        {
            var index = 0;
            for (var i = 0; i < TimeScales.Length; i++)
            {
                if (Math.Abs(TimeScales[i] - Model.TimeScale) < 1e-9)
                {
                    index = i;
                }
            }

            SetTimeScale(TimeScales[Math.Max(0, Math.Min(TimeScales.Length - 1, index + (direction > 0 ? 1 : -1)))]);
        }

        public void TogglePause()
        {
            if (!IsBegun)
            {
                return;
            }

            Model.Paused = !Model.Paused;
            if (!Model.Paused)
            {
                _timeline.Follow();
            }

            Inject(ScriptEventKind.Marker, "pause", new Vec2(Model.Paused ? 1.0 : 0.0, 0.0));
            Model.Note(Model.Paused ? "已暂停：. 推进一个 tick，, 推进一个表现帧，P 继续" : "继续");
        }

        /// <summary>暂停时推进一个固定步（运行中也可调用，等价多推一步）。</summary>
        public void StepTick()
        {
            if (!IsBegun)
            {
                return;
            }

            Inject(ScriptEventKind.Marker, "step_tick", new Vec2(1.0, 0.0));
            RunStep(Session!.StepSeconds);
        }

        /// <summary>暂停时推进一个表现帧（脚本声明的帧长）。</summary>
        public void StepFrame()
        {
            if (!IsBegun)
            {
                return;
            }

            Inject(ScriptEventKind.Marker, "step_frame", new Vec2(1.0, 0.0));
            RunStep(Session!.FrameSeconds);
        }

        private void RunStep(double seconds)
        {
            try
            {
                _timeline.Follow();
                if (_replay && Session!.Tick >= Session.DurationTicks)
                {
                    return;
                }

                AdvanceSession(seconds);
                AfterAdvance();
                Model.Tick = Session!.Tick;
                if (_replay && Session.Tick >= Session.DurationTicks)
                {
                    FinishReplay();
                }
            }
            catch (Exception ex)
            {
                Model.Paused = true;
                Model.Note("单步出错：" + ex.Message);
            }
        }

        // ───────── 录制 ─────────

        /// <summary>把到目前为止的这一局存成本地输入脚本（lab/out/playground/，被忽略规则覆盖，不入库）；返回文件路径。</summary>
        public string SaveRecording(string? fileName = null)
        {
            if (Session == null)
            {
                return string.Empty;
            }

            var script = _ended ? Session.Script : LabLive.PrefixScript(Session.Script, Session.Tick);
            Directory.CreateDirectory(_savePath);
            var name = fileName ?? ("session_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".script.json");
            var path = Path.Combine(_savePath, name);
            File.WriteAllText(path, script.ToJson(), new System.Text.UTF8Encoding(false));
            Model.LastSavedPath = path;
            Model.Note("已保存脚本：" + path + "（无头重放：FeelLab run <脚本> --cell " + cell + "）");
            return path;
        }

        /// <summary>结束会话：定稿记录、释放舞台，并把整局存成本地脚本（有事件才存）；返回存下的脚本路径（没存为空串）。</summary>
        public string End()
        {
            if (Session == null || _ended)
            {
                return Model.LastSavedPath;
            }

            var saved = string.Empty;
            try
            {
                if (_filter != null)
                {
                    _filter.Submitted -= OnFeedbackSubmitted;
                }

                FinalRecording = Session.Finish();
                _ended = true;
                if (!_replay && Session.Script.Events.Count > 0)
                {
                    saved = SaveRecording("last_session.script.json");
                }
            }
            finally
            {
                _ended = true;
                if (_hud != null)
                {
                    Destroy(_hud.gameObject);
                    _hud = null;
                }

                _stage?.Dispose();
            }

            return saved;
        }

        /// <summary>会话结束后的完整记录（<see cref="End"/> 之后可用；逻辑组与重放该脚本的无头结果逐字节一致）。</summary>
        public LabRecording? FinalRecording { get; private set; }

        private void OnDestroy()
        {
            try
            {
                End();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[LabPlayground] 结束会话时出错：" + ex.Message);
            }
        }

        // ───────── 屏上叠层（IMGUI，只从模型绘制）─────────

        private Vector2 _scroll;
        private GUIStyle? _small;

        private void OnGUI()
        {
            if (!IsBegun)
            {
                if (Model.Status.Length > 0)
                {
                    GUI.Label(new UnityEngine.Rect(12, 12, 900, 60), Model.Status);
                }

                return;
            }

            if (_replay)
            {
                DrawReplayHud();
                return;
            }

            UpdateTextFocus();
            if (!Model.PanelVisible && _textFocus)
            {
                GUIUtility.keyboardControl = 0;
                _textFocus = false;
            }

            DrawOverlay();
            var scale = Mathf.Clamp(Screen.height / 900f, 1f, 2f);
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                if (Model.CornerFlash && Model.CornerFlashFrames > 0)
                {
                    var old = GUI.color;
                    GUI.color = Color.white;
                    GUI.DrawTexture(new UnityEngine.Rect(0, 0, 110, 110), Texture2D.whiteTexture);
                    GUI.color = old;
                }

                if (!Model.PanelVisible)
                {
                    if (showcase)
                    {
                        // 演示场景：一条角落小字（右上），不占画面。
                        _small ??= new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
                        var corner = new GUIStyle(_small) { alignment = TextAnchor.UpperRight };
                        GUI.Label(new UnityEngine.Rect(Screen.width / scale - 612, 6, 600, 20), "F1 展开调试面板　F12 切页　J 攻击　K 闪避　L 重击　U 蓄力　G 精英出手", corner);
                    }
                    else
                    {
                        GUI.Label(new UnityEngine.Rect(12, Screen.height / scale - 28, 600, 24), "F1 显示面板　J 攻击　K 闪避　L 技能　U 蓄力（按住）　WASD/方向键 移动");
                    }

                    return;
                }

                _small ??= new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
                DrawPanel(scale);
                if (Model.Tab == LabTab.Timeline)
                {
                    DrawTimelineArea(scale);
                }
            }
            finally
            {
                GUI.matrix = matrix;
            }
        }

        /// <summary>重绘时测得的面板滚动区内容宽（逻辑像素）与面板宽；内容比面板宽就会出现横向滚动，居中的按钮文字被挤出可见区（"按钮文字空白"缺陷的度量）。</summary>
        public float PanelContentWidth { get; private set; }

        public float PanelViewportWidth { get; private set; }

        public LabTab PanelLayoutTab { get; private set; }

        /// <summary>已采到的重绘样本数（OnGUI 在无图形的环境里不会被调用，样本数为 0）。</summary>
        public int PanelLayoutSamples { get; private set; }

        private void ProbePanelLayout(float panelWidth)
        {
            var r = GUILayoutUtility.GetRect(0f, 0f);
            if (Event.current.type == EventType.Repaint)
            {
                PanelContentWidth = r.width;
                PanelViewportWidth = panelWidth;
                PanelLayoutTab = Model.Tab;
                PanelLayoutSamples++;
            }
        }

        private void DrawPanel(float scale)
        {
            var width = PanelWidth;
            var height = Screen.height / scale - 16f;
            GUILayout.BeginArea(new UnityEngine.Rect(8, 8, width, height), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label("手感试玩  " + Model.Cell + "　tick " + Model.Tick + "　靶子 " + Model.DummyCount + (Model.Paused ? "　[暂停]" : string.Empty));
            var hint = PanelHint;
            if (hint != null)
            {
                _small ??= new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
                GUILayout.Label(hint, _small);
            }

            GUILayout.Label(Model.Status, _small);
            DrawTabBar();
            if (Model.Tab != LabTab.Scene)
            {
                DrawTabContent();
                ProbePanelLayout(width);
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            // 场景控制
            GUILayout.Label("— 场景控制 —");
            GUILayout.BeginHorizontal();
            for (var i = 0; i < Model.DummyKinds.Count; i++)
            {
                var kind = Model.DummyKinds[i];
                if (GUILayout.Button((i + 1) + " " + kind.Label))
                {
                    SpawnDummy(kind.Id, 1);
                }

                if ((i + 1) % 3 == 0 && i + 1 < Model.DummyKinds.Count)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("群 N=" + Model.GroupCount, GUILayout.Width(70));
            Model.GroupCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(Model.GroupCount, 2, 12));
            if (GUILayout.Button("出一群（脆皮怪）") && Model.DummyKinds.Count > 0)
            {
                SpawnDummy(FindKind("frail"), Model.GroupCount);
            }

            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("清场 (Backspace)")) ClearDummies();
            if (GUILayout.Button("精英出手 (G)")) EliteSwing();
            GUILayout.EndHorizontal();

            GUILayout.Label("武器 (F3)：" + LabLiveModel.FriendlyName(Model.Weapon) + "　体型 (F4)：" + LabLiveModel.FriendlyName(Model.Archetype));
            var flow = new List<KeyValuePair<string, bool>>();
            var picks = new List<Action>();
            foreach (var w in Model.Weapons)
            {
                var weapon = w;
                flow.Add(new KeyValuePair<string, bool>(weapon.Label, weapon.Id == Model.Weapon));
                picks.Add(() => SetWeapon(weapon.Id));
            }

            DrawToggleFlow(flow, picks, width);
            flow.Clear();
            picks.Clear();
            foreach (var a in Model.Archetypes)
            {
                var archetype = a;
                flow.Add(new KeyValuePair<string, bool>(archetype.Label, archetype.Id == Model.Archetype));
                picks.Add(() => SetArchetype(archetype.Id));
            }

            DrawToggleFlow(flow, picks, width);

            GUILayout.Label("时间尺度 ([ ])");
            flow.Clear();
            picks.Clear();
            foreach (var ts in TimeScales)
            {
                var scaleValue = ts;
                flow.Add(new KeyValuePair<string, bool>("×" + scaleValue.ToString("0.##"), Math.Abs(Model.TimeScale - scaleValue) < 1e-9));
                picks.Add(() => SetTimeScale(scaleValue));
            }

            DrawToggleFlow(flow, picks, width);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Model.Paused ? "继续 (P)" : "暂停 (P)")) TogglePause();
            if (GUILayout.Button("单步 tick (.)")) StepTick();
            if (GUILayout.Button("单步帧 (,)")) StepFrame();
            GUILayout.EndHorizontal();

            GUILayout.Label("效果开关（关 = 该项不播）");
            flow.Clear();
            picks.Clear();
            flow.Add(new KeyValuePair<string, bool>("顿帧(F8)", Model.HitStopOn));
            picks.Add(() => SetHitStop(!Model.HitStopOn));
            foreach (var channel in Model.EffectChannels)
            {
                var ch = channel;
                flow.Add(new KeyValuePair<string, bool>(EffectLabel(ch), Model.EffectOn(ch)));
                picks.Add(() => ToggleEffect(ch));
            }

            DrawToggleFlow(flow, picks, width, false);
            if (GUILayout.Toggle(Model.CornerFlash, "输入瞬间角落闪块 (F11，外接相机测延迟)") != Model.CornerFlash) ToggleCornerFlash();
            foreach (var channel in Model.EffectChannels)
            {
                GUILayout.Label("  " + EffectLabel(channel) + "：流水线发出 " + Model.EffectSubmitted(channel) + " 次，被关掉 " + Model.EffectSuppressed(channel) + " 次", _small);
            }

            // 预设 / A-B
            GUILayout.Label("— 预设（Tab = A/B 切换）—");
            GUILayout.Label("当前：" + LabLiveModel.FriendlyName(Model.Preset) + "　槽位 " + Model.ActiveSlot + "　A=" + LabLiveModel.FriendlyName(Model.PresetA) + "　B=" + LabLiveModel.FriendlyName(Model.PresetB), _small);
            foreach (var p in Model.Presets)
            {
                if (GUILayout.Toggle(p.Id == Model.Preset, p.Label, "Button") && p.Id != Model.Preset) SetPreset(p.Id);
            }

            if (GUILayout.Button("A/B 切换 (Tab)")) SwitchAb();

            // 指标
            GUILayout.Label("— 指标（最近值 / 滚动均值）—");
            GUILayout.Label("输入→动作接受：" + Fmt(Model.InputToAcceptTicks, "tick") + "　" + Fmt(Model.InputToAcceptMs, "ms"), _small);
            GUILayout.Label("输入→首次可见响应：" + Fmt(Model.InputToVisibleMs, "ms") + "　" + Fmt(Model.InputToVisibleFrames, "帧"), _small);
            GUILayout.Label("命中确认→首个反馈：" + Fmt(Model.HitToFeedbackTicks, "tick") + "　" + Fmt(Model.HitToFeedbackMs, "ms"), _small);
            GUILayout.Label("帧间隔 p50 " + Pct(Model.FrameIntervalMs, 0.5) + " / p95 " + Pct(Model.FrameIntervalMs, 0.95) + " ms", _small);
            GUILayout.Label("每帧推进 p50 " + Pct(Model.AdvanceMs, 0.5) + " / p95 " + Pct(Model.AdvanceMs, 0.95) + " ms", _small);

            // 录制
            GUILayout.Label("— 录制 —");
            GUILayout.Label("已记 " + Model.RecordedEvents + " 个事件（整局持续记录）", _small);
            if (GUILayout.Button("保存本局脚本 (F5)")) SaveRecording();
            if (Model.LastSavedPath.Length > 0) GUILayout.Label(Model.LastSavedPath, _small);

            if (Model.HelpVisible)
            {
                GUILayout.Label("— 按键 (F2 收起) —");
                GUILayout.Label(HelpText(), _small);
            }

            ProbePanelLayout(width);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>
        /// 一组切换按钮按面板宽折行（缺陷修复，ADR-0154）：此前每组是一整排不换行的按钮，模板多时那一排比面板宽几十倍，
        /// 滚动区内容宽被撑大，同页其它按钮跟着被拉宽、居中的文字落到可见区外。折行规划是纯函数（<see cref="LabPanelFlow"/>），宽度按按钮样式实测。
        /// </summary>
        private void DrawToggleFlow(IReadOnlyList<KeyValuePair<string, bool>> items, IReadOnlyList<Action> picks, float panelWidth, bool radio = true)
        {
            var widths = new float[items.Count];
            for (var i = 0; i < widths.Length; i++)
            {
                widths[i] = GUI.skin.button.CalcSize(new GUIContent(items[i].Key)).x;
            }

            var rows = LabPanelFlow.Plan(widths, panelWidth - LabPanelFlow.PanelChrome, LabPanelFlow.Spacing);
            foreach (var row in rows)
            {
                GUILayout.BeginHorizontal();
                foreach (var index in row)
                {
                    var on = items[index].Value;
                    var now = GUILayout.Toggle(on, items[index].Key, "Button", GUILayout.ExpandWidth(false));
                    if (radio ? now && !on : now != on)
                    {
                        picks[index]();
                    }
                }

                GUILayout.EndHorizontal();
            }
        }

        private string FindKind(string preferred)
        {
            foreach (var k in Model.DummyKinds)
            {
                if (string.Equals(k.Id, preferred, StringComparison.Ordinal))
                {
                    return preferred;
                }
            }

            return Model.DummyKinds[0].Id;
        }

        private string HelpText()
        {
            var attack = _poller?.Describe("input.action.lab_a_attack") ?? string.Empty;
            var dodge = _poller?.Describe("input.action.lab_a_dodge") ?? string.Empty;
            var skill = _poller?.Describe("input.action.lab_a_skill") ?? string.Empty;
            var charge = _poller?.Describe("input.action.lab_a_charge") ?? string.Empty;
            return "移动 WASD/方向键/左摇杆\n攻击(三连击) " + attack + "\n闪避 " + dodge + "\n技能(重击) " + skill + "\n蓄力(按住) " + charge
                + "\n数字键 1.. 出靶子（按住 Shift = 出一群）\nF3 换武器　F4 换体型　Tab A/B　F5 存脚本　F12 切页（场景/调参/时间轴/轨迹/评分）\nF6 震屏　F7 闪白　F8 顿帧　F9 音效　F10 镜头冲击　F11 角落闪块\n[ ] 时间尺度　P 暂停　. 单步 tick　, 单步帧　G 精英出手";
        }

        private static string EffectLabel(string channel)
        {
            switch (channel)
            {
                case LabEffectFilter.Shake: return "震屏(F6)";
                case LabEffectFilter.Flash: return "闪白(F7)";
                case LabEffectFilter.Sfx: return "音效(F9)";
                case LabEffectFilter.CameraImpulse: return "镜头冲击(F10)";
                default: return channel;
            }
        }

        private static string Fmt(RollingStat stat, string unit) =>
            stat.HasData
                ? stat.Last.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " / " + stat.Mean.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " " + unit
                : "— " + unit;

        private static string Pct(RollingStat stat, double q) =>
            stat.HasData ? stat.Percentile(q).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "—";
    }
}
