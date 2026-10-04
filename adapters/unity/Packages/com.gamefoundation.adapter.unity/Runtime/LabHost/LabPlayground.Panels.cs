#nullable enable
// LabPlayground 的实验室面板（ADR-0150，手感设计 06 第 3.4 节、第 4 节）：调参 | 时间轴 | 轨迹 | 评分 四页，加上原有的"场景"页。
//
// 判断记录（面板只画、不算）：每一页的内容都来自内核里的纯 C# 视图模型（Lab.TuningPanel / TimelineModel / TrajectoryModel / RatingModel），
// 这里只做三件事——把模型画出来、把操作转成模型的命令、保证键盘焦点不和试玩输入打架。所以所有内容无头可测（lab/tests），
// 引擎侧只需要 PlayMode 冒烟确认"分页能切、改一个字段的结果与数据折算一致"。
//
// 判断记录（操作延后到下一帧）：IMGUI 在一帧里会走多个事件（布局、重绘、输入），中途改了控件结构会引发布局警告；
// 所以界面上的所有写操作先进队列，下一帧控制器开头统一执行。事件仍盖"下一个固定步"的戳，所以对逻辑没有影响，只多一帧显示延迟。
//
// 判断记录（键盘焦点守卫）：调参值、备注等文本框拿到焦点时，试玩热键与真实输入轮询都暂停（否则输入数字会出靶子、WASD 会移动角色）；
// Esc 或回车提交后释放焦点。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Core.Foundation.Common;
using Core.Foundation.DataRegistry;
using Core.Foundation.Feel;
using Lab;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Adapter.Unity.LabHost
{
    public sealed partial class LabPlayground
    {
        private TuningPanel? _tuning;
        private readonly TimelineModel _timeline = new TimelineModel();
        private readonly RatingModel _rating = new RatingModel();
        private readonly Queue<Action> _deferred = new Queue<Action>();
        private bool _textFocus;

        /// <summary>调参面板的视图模型（<see cref="Begin"/> 之后可用）。</summary>
        public TuningPanel? Tuning => _tuning;

        /// <summary>帧数据时间轴模型。</summary>
        public TimelineModel Timeline => _timeline;

        /// <summary>评分模型。</summary>
        public RatingModel Rating => _rating;

        /// <summary>评分时记下的设备条件（缺省按有无手柄猜；评分页可改）。</summary>
        public string RatingDevice { get; set; } = string.Empty;

        /// <summary>导出验证记录的行 id（评分页可改；缺省按预设、格子与日期生成）。</summary>
        public string RatingRowId { get; set; } = string.Empty;

        /// <summary>面板产出（预设、写回差异、评分）的本地目录（缺省 <c>&lt;存档目录&gt;/panels</c>，在 lab/out 下，被忽略规则覆盖、不入库）。</summary>
        public string PanelsDirectory => Path.Combine(_savePath, "panels");

        /// <summary>"保存为预设"写出的本地数据根目录；下次开局若存在会自动并入额外数据根，预设列表里就能选到。</summary>
        public string LocalPresetRoot => Path.Combine(PanelsDirectory, "presets");

        private void InitPanels()
        {
            _tuning = new TuningPanel(Session!, _host!.Runner.DatasetFor(Session!.Script).Sources, _repoRoot);
            if (Model.PresetB.Length > 0)
            {
                _tuning.PrimeInactiveSlotPreset(Model.PresetB);
            }

            Model.Tab = LabTab.Scene;
            RatingDevice = Gamepad.current != null ? "pc_gamepad" : "pc_keyboard_mouse";
            _timeline.Sync(Session.Recording, Session.Script, Session.Tick);
            SyncFromTuning();
        }

        /// <summary>调参面板的槽位、预设、顿帧状态回写到试玩模型（模型是面板与断言的唯一读取点）。</summary>
        private void SyncFromTuning()
        {
            if (_tuning == null)
            {
                return;
            }

            Model.ActiveSlot = _tuning.ActiveSlot;
            Model.PresetA = _tuning.PresetOf('A');
            Model.PresetB = _tuning.PresetOf('B');
            Model.Preset = _tuning.ActivePreset;
            Model.HitStopOn = _tuning.HitStopOn;
            Model.SlotSwitches = _tuning.SlotSwitches;
        }

        private void AfterAdvancePanels()
        {
            if (_tuning == null)
            {
                return;
            }

            _tuning.PruneDummyScopes();
            _timeline.Sync(Session!.Recording, Session.Script, Session.Tick);
            SyncFromTuning();
        }

        private void Defer(Action action) => _deferred.Enqueue(action);

        private void RunDeferred()
        {
            while (_deferred.Count > 0)
            {
                var action = _deferred.Dequeue();
                try
                {
                    action();
                }
                catch (LabFormatException ex)
                {
                    Model.Note("面板操作被拒：" + ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    Model.Note("面板操作被拒：" + ex.Message);
                }
                catch (ArgumentException ex)
                {
                    Model.Note("面板操作被拒：" + ex.Message);
                }
            }

            SyncFromTuning();
        }

        // ───────── 命令（界面与测试共用）─────────

        public void SetTab(LabTab tab)
        {
            Model.Tab = tab;
            _textFocus = false;
            GUIUtility.keyboardControl = 0;
            Model.Note("面板页：" + TabLabel(tab));
        }

        public void CycleTab() => SetTab((LabTab)(((int)Model.Tab + 1) % 5));

        private static string TabLabel(LabTab tab)
        {
            switch (tab)
            {
                case LabTab.Scene: return "场景";
                case LabTab.Tuning: return "调参";
                case LabTab.Timeline: return "时间轴";
                case LabTab.Trajectory: return "轨迹";
                default: return "评分";
            }
        }

        /// <summary>保存当前槽位的覆盖为新预设（本地目录，开局自动可选）；<paramref name="name"/> 是预设名（会成为 <c>feel.preset.&lt;名&gt;</c>）。返回写出的文件路径。</summary>
        public string SaveTuningAsPreset(string name)
        {
            if (_tuning == null)
            {
                return string.Empty;
            }

            var saved = _tuning.SaveAsPreset(name, LocalPresetRoot);
            Model.Note("已保存预设 " + saved.RowId + "（" + saved.WrittenFields + " 个字段）→ " + saved.Path + "；下次开局在预设列表里可选");
            return saved.Path;
        }

        /// <summary>把当前槽位的覆盖写成对来源数据表的差异文件（本地目录；不改仓库里的数据）。返回差异文件路径。</summary>
        public string WriteBackTuning()
        {
            if (_tuning == null)
            {
                return string.Empty;
            }

            var path = Path.Combine(PanelsDirectory, "writeback", "writeback_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".diff");
            _tuning.WriteBack(path);
            Model.Note(_tuning.LastNote);
            return path;
        }

        /// <summary>提交当前评分（四项都要打分），并把明细存本地评分文件。返回评分文件路径。</summary>
        public string SubmitRating()
        {
            if (_tuning == null || Session == null)
            {
                return string.Empty;
            }

            _rating.Submit(Session.Tick, _tuning.ActiveSlot);
            var path = SaveRatingLocal();
            Model.Note("已提交评分（第 " + _rating.Samples.Count + " 次），明细 → " + path);
            return path;
        }

        private RatingContext BuildRatingContext() =>
            RatingModel.BuildContext(_host!.Runner, Session!, _tuning!, cell, RatingDevice, DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        /// <summary>评分明细（含预设版本、指纹哈希、设备条件）存本地文件。</summary>
        public string SaveRatingLocal()
        {
            if (_tuning == null || Session == null)
            {
                return string.Empty;
            }

            var path = Path.Combine(PanelsDirectory, "ratings", "rating_" + Session.Script.Meta.ScriptId + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _rating.SaveLocal(path, BuildRatingContext());
            return path;
        }

        /// <summary>导出为 <c>feel.validation</c> 记录（本地目录的表文件；ADR-0146）。返回表文件路径。</summary>
        public string ExportRatingValidation()
        {
            if (_tuning == null || Session == null)
            {
                return string.Empty;
            }

            var id = RatingRowId.Length > 0 ? RatingRowId : DefaultRatingRowId();
            var dir = Path.Combine(PanelsDirectory, "validation");
            var path = _rating.ExportValidation(dir, id, BuildRatingContext());
            Model.Note("已导出验证记录 " + id + " → " + path);
            return path;
        }

        private string DefaultRatingRowId()
        {
            var preset = Model.Preset;
            var tail = preset.Substring(preset.LastIndexOf('.') + 1);
            return "feel.validation." + tail + "_" + cell + "_" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        // ───────── 界面：分页条与各页内容（左侧面板里）─────────

        private static readonly string[] TabLabels = { "场景", "调参", "时间轴", "轨迹", "评分" };

        private void DrawTabBar()
        {
            var selected = GUILayout.Toolbar((int)Model.Tab, TabLabels);
            if (selected != (int)Model.Tab)
            {
                SetTab((LabTab)selected);
            }
        }

        private void DrawTabContent()
        {
            switch (Model.Tab)
            {
                case LabTab.Tuning: DrawTuningTab(); break;
                case LabTab.Timeline: DrawTimelineTab(); break;
                case LabTab.Trajectory: DrawTrajectoryTab(); break;
                case LabTab.Rating: DrawRatingTab(); break;
            }
        }

        private float PanelWidth => Model.Tab == LabTab.Tuning ? 600f : 380f;

        // ───────── 调参页 ─────────

        private FeelGroup _group = FeelGroup.Input;
        private readonly Dictionary<string, string> _edit = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _expanded = new HashSet<string>(StringComparer.Ordinal);
        private IReadOnlyList<TuningRow> _rows = Array.Empty<TuningRow>();
        private string _rowsKey = string.Empty;
        private string _presetName = "my_feel";

        private IReadOnlyList<TuningRow> CurrentRows()
        {
            var key = Session!.Tick + "|" + _tuning!.EventsInjected + "|" + _group + "|" + _tuning.Scope + "|" + _tuning.ActiveSlot;
            if (!string.Equals(key, _rowsKey, StringComparison.Ordinal))
            {
                _rows = _tuning.Rows(_group);
                _rowsKey = key;
            }

            return _rows;
        }

        private void DrawTuningTab()
        {
            var tuning = _tuning;
            if (tuning == null)
            {
                GUILayout.Label("调参面板不可用（会话没有手感装配）。");
                return;
            }

            GUILayout.Label("槽位 " + tuning.ActiveSlot + "（Tab 切换）　预设 " + LabLiveModel.FriendlyName(tuning.ActivePreset) + "　覆盖 " + tuning.ActiveWrites.Count + " 条　注入事件 " + tuning.EventsInjected, _small);

            // 作用域：全局 / 玩家 / 在场靶子。
            var scopes = tuning.Scopes();
            var scopeNames = new string[scopes.Count];
            var scopeIndex = 0;
            for (var i = 0; i < scopes.Count; i++)
            {
                scopeNames[i] = scopes[i].Length == 0 ? "全局" : scopes[i] == TuningPanel.PlayerScope ? "玩家" : scopes[i];
                if (string.Equals(scopes[i], tuning.Scope, StringComparison.Ordinal))
                {
                    scopeIndex = i;
                }
            }

            GUILayout.Label("改值作用于：", _small);
            var pickedScope = GUILayout.SelectionGrid(scopeIndex, scopeNames, 4);
            if (pickedScope != scopeIndex)
            {
                var scope = scopes[pickedScope];
                Defer(() => tuning.SetScope(scope));
            }

            // 七个分组。
            var groups = tuning.Groups;
            var groupNames = new string[groups.Count];
            var groupIndex = 0;
            for (var i = 0; i < groups.Count; i++)
            {
                groupNames[i] = TuningPanel.GroupLabel(groups[i]);
                if (groups[i] == _group)
                {
                    groupIndex = i;
                }
            }

            var pickedGroup = GUILayout.Toolbar(groupIndex, groupNames);
            if (pickedGroup != groupIndex)
            {
                _group = groups[pickedGroup];
                _edit.Clear();
                GUIUtility.keyboardControl = 0;
            }

            var rows = CurrentRows();
            foreach (var row in rows)
            {
                DrawTuningRow(tuning, row);
            }

            GUILayout.Label("— 预设与写回 —");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("恢复基准")) Defer(() => { tuning.RestoreBaseline(); Model.Note("已恢复基准：覆盖层清空，取值回到预设与数据行。"); });
            if (GUILayout.Button("写回数据表（差异文件）")) Defer(() => WriteBackTuning());
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("预设名", GUILayout.Width(50));
            GUI.SetNextControlName("tune_preset_name");
            _presetName = GUILayout.TextField(_presetName, GUILayout.Width(180));
            if (GUILayout.Button("保存为预设"))
            {
                var name = _presetName;
                Defer(() => SaveTuningAsPreset(name));
            }

            GUILayout.EndHorizontal();
            if (tuning.LastNote.Length > 0)
            {
                GUILayout.Label(tuning.LastNote, _small);
            }
        }

        private void DrawTuningRow(TuningPanel tuning, TuningRow row)
        {
            var def = row.Def;
            var previous = GUI.color;
            if (row.Planned)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
            }

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label((row.Overridden ? "● " : string.Empty) + row.Name, GUILayout.Width(190));
            DrawValueControl(tuning, row);
            if (row.UnitText.Length > 0)
            {
                GUILayout.Label(row.UnitText, GUILayout.Width(80));
            }

            if (row.Overridden && GUILayout.Button("×", GUILayout.Width(24)))
            {
                var field = row.Name;
                Defer(() => tuning.Clear(field));
            }

            GUILayout.EndHorizontal();

            var detail = "范围 " + row.RangeText + "　来源 L" + row.SourceLayer + " " + (row.SourceId.Length > 0 ? row.SourceId : "—");
            if (row.AbsoluteText.Length > 0) detail += "　" + row.AbsoluteText;
            if (row.Clamped) detail += "　[限幅]";
            if (row.Pending) detail += "　[下一固定步生效]";
            if (row.Planned) detail += "　[尚未生效" + (row.StatusNote.Length > 0 ? "：" + row.StatusNote : string.Empty) + "]";
            GUILayout.BeginHorizontal();
            GUILayout.Label(detail, _small);
            var open = _expanded.Contains(row.Name);
            if (GUILayout.Button(open ? "▾" : "▸", GUILayout.Width(24)))
            {
                if (open) _expanded.Remove(row.Name); else _expanded.Add(row.Name);
            }

            GUILayout.EndHorizontal();
            if (open)
            {
                foreach (var entry in row.Provenance)
                {
                    GUILayout.Label("　L" + entry.Layer + " " + entry.SourceId + " " + entry.Op + "：" + TuningPanel.FormatValue(entry.ValueBefore) + " → " + TuningPanel.FormatValue(entry.ValueAfter), _small);
                }
            }

            if (row.Overridden)
            {
                GUILayout.Label("覆盖：" + row.OverrideText, _small);
            }

            GUILayout.EndVertical();
            GUI.color = previous;
        }

        private static double NiceStep(FeelFieldDef def, double current)
        {
            double step;
            if (def.Min.HasValue && def.Max.HasValue)
            {
                step = (def.Max.Value - def.Min.Value) / 50.0;
            }
            else
            {
                step = Math.Max(Math.Abs(current) * 0.05, 0.01);
            }

            if (def.Kind == FeelFieldKind.Int)
            {
                return Math.Max(1.0, Math.Round(step));
            }

            var magnitude = Math.Pow(10.0, Math.Floor(Math.Log10(Math.Max(step, 1e-9))));
            var normalized = step / magnitude;
            return (normalized < 1.5 ? 1.0 : normalized < 3.5 ? 2.0 : normalized < 7.5 ? 5.0 : 10.0) * magnitude;
        }

        private void DrawValueControl(TuningPanel tuning, TuningRow row)
        {
            var def = row.Def;
            var field = row.Name;
            var control = "tune_" + field;
            switch (def.Kind)
            {
                case FeelFieldKind.Number:
                case FeelFieldKind.Int:
                {
                    var current = row.Raw.Kind == FeelValueKind.Number ? row.Raw.AsNumber() : (def.Min ?? 0.0);
                    var step = NiceStep(def, current);
                    if (def.Min.HasValue && def.Max.HasValue)
                    {
                        var slid = GUILayout.HorizontalSlider((float)current, (float)def.Min.Value, (float)def.Max.Value, GUILayout.Width(150));
                        if (Math.Abs(slid - current) > 1e-6)
                        {
                            var value = def.Kind == FeelFieldKind.Int ? Math.Round(slid) : Math.Round(slid / step) * step;
                            if (Math.Abs(value - current) > 1e-9) Defer(() => tuning.SetValue(field, FeelValue.Of(value)));
                        }
                    }

                    if (GUILayout.Button("−", GUILayout.Width(22))) Defer(() => tuning.SetValue(field, FeelValue.Of(current - step)));
                    DrawCommitText(control, row.ValueText, 70, text =>
                    {
                        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var typed))
                        {
                            Defer(() => tuning.SetValue(field, FeelValue.Of(typed)));
                        }
                        else
                        {
                            Model.Note("不是数字：" + text);
                        }
                    });
                    if (GUILayout.Button("+", GUILayout.Width(22))) Defer(() => tuning.SetValue(field, FeelValue.Of(current + step)));
                    break;
                }

                case FeelFieldKind.Bool:
                {
                    var current = row.Raw.Kind == FeelValueKind.Bool && row.Raw.AsBool();
                    if (GUILayout.Toggle(current, current ? "是" : "否", "Button", GUILayout.Width(60)) != current)
                    {
                        Defer(() => tuning.SetValue(field, FeelValue.Of(!current)));
                    }

                    break;
                }

                case FeelFieldKind.Enum:
                {
                    var current = row.Raw.Kind == FeelValueKind.Text ? row.Raw.AsText() : string.Empty;
                    GUILayout.BeginHorizontal();
                    foreach (var option in def.EnumValues ?? Array.Empty<string>())
                    {
                        if (GUILayout.Toggle(string.Equals(option, current, StringComparison.Ordinal), option, "Button") && !string.Equals(option, current, StringComparison.Ordinal))
                        {
                            var picked = option;
                            Defer(() => tuning.SetValue(field, FeelValue.Of(picked)));
                        }
                    }

                    GUILayout.EndHorizontal();
                    break;
                }

                case FeelFieldKind.List:
                    DrawCommitText(control, row.ValueText, 260, text =>
                    {
                        var items = new List<string>();
                        foreach (var part in text.Split(','))
                        {
                            var item = part.Trim();
                            if (item.Length > 0 && item != "—") items.Add(item);
                        }

                        Defer(() => tuning.SetValue(field, FeelValue.OfList(items)));
                    });
                    break;

                default:
                    DrawCommitText(control, row.ValueText, 260, text => Defer(() => tuning.SetValue(field, FeelValue.Of(text))));
                    break;
            }
        }

        /// <summary>文本框：拿到焦点时保留正在输入的文本，回车提交（失焦丢弃），平时显示实时取值。</summary>
        private void DrawCommitText(string control, string shown, float width, Action<string> commit)
        {
            var focused = GUI.GetNameOfFocusedControl() == control;
            var current = Event.current;
            var enter = focused && current.type == EventType.KeyDown && (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter);
            var text = focused && _edit.TryGetValue(control, out var typedSoFar) ? typedSoFar : shown;
            GUI.SetNextControlName(control);
            var typed = GUILayout.TextField(text, GUILayout.Width(width));
            if (focused)
            {
                _edit[control] = typed;
                if (enter)
                {
                    commit(typed);
                    _edit.Remove(control);
                    GUIUtility.keyboardControl = 0;
                    current.Use();
                }
            }
            else
            {
                _edit.Remove(control);
            }
        }

        // ───────── 时间轴页 ─────────

        private string _describeKey = string.Empty;
        private List<string> _describe = new List<string>();
        private Vector2 _timelineScroll;

        private static Color SegmentColor(SegmentKind kind)
        {
            switch (kind)
            {
                case SegmentKind.Charge: return new Color(0.95f, 0.85f, 0.25f);
                case SegmentKind.Startup: return new Color(0.35f, 0.65f, 1.00f);
                case SegmentKind.Active: return new Color(1.00f, 0.35f, 0.30f);
                case SegmentKind.Recovery: return new Color(0.60f, 0.60f, 0.72f);
                case SegmentKind.CancelWindow: return new Color(0.30f, 0.90f, 0.50f);
                case SegmentKind.ComboWindow: return new Color(0.20f, 0.75f, 0.85f);
                case SegmentKind.Invuln: return new Color(0.95f, 0.95f, 0.95f);
                case SegmentKind.Armor: return new Color(0.85f, 0.55f, 0.20f);
                case SegmentKind.Guard: return new Color(0.50f, 0.50f, 1.00f);
                case SegmentKind.Hitstop: return new Color(1.00f, 0.60f, 0.90f);
                case SegmentKind.Reaction: return new Color(0.90f, 0.50f, 0.20f);
                case SegmentKind.Downed: return new Color(0.55f, 0.30f, 0.30f);
                default: return new Color(0.60f, 0.80f, 0.40f);
            }
        }

        private static int SegmentLane(SegmentKind kind)
        {
            switch (kind)
            {
                case SegmentKind.CancelWindow:
                case SegmentKind.ComboWindow:
                    return 1;
                case SegmentKind.Invuln:
                case SegmentKind.Armor:
                case SegmentKind.Guard:
                case SegmentKind.Hitstop:
                    return 2;
                default:
                    return 0;
            }
        }

        private static Color MarkColor(MarkKind kind)
        {
            switch (kind)
            {
                case MarkKind.Input: return new Color(1f, 1f, 0.3f);
                case MarkKind.Hit: return new Color(1f, 0.2f, 0.2f);
                case MarkKind.Result: return new Color(1f, 0.6f, 0.2f);
                case MarkKind.Assist: return new Color(0.4f, 1f, 1f);
                case MarkKind.BufferDropped: return new Color(0.7f, 0.4f, 1f);
                case MarkKind.ActionCancelled: return new Color(0.3f, 1f, 0.5f);
                default: return new Color(0.85f, 0.85f, 0.85f);
            }
        }

        private void DrawTimelineTab()
        {
            GUILayout.Label("帧数据时间轴（每实体一条）。暂停后可逐 tick 拖动，只读录制，不影响逻辑。");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Model.Paused ? "继续 (P)" : "暂停 (P)")) TogglePause();
            if (GUILayout.Button("单步 tick (.)")) StepTick();
            GUILayout.EndHorizontal();

            var last = Math.Max(0, _timeline.LastTick);
            var cursor = _timeline.Cursor;
            GUILayout.Label("游标 tick " + cursor + " / 最新 " + last + (_timeline.Pinned.HasValue ? "　[已钉住]" : "　[跟随最新]"));
            var wasEnabled = GUI.enabled;
            GUI.enabled = Model.Paused;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("−10")) _timeline.StepCursor(-10);
            if (GUILayout.Button("−1")) _timeline.StepCursor(-1);
            if (GUILayout.Button("+1")) _timeline.StepCursor(1);
            if (GUILayout.Button("+10")) _timeline.StepCursor(10);
            if (GUILayout.Button("跟随最新")) _timeline.Follow();
            GUILayout.EndHorizontal();
            var slid = Mathf.RoundToInt(GUILayout.HorizontalSlider(cursor, 0, Math.Max(1, last)));
            if (slid != cursor) _timeline.Pin(slid);
            GUI.enabled = wasEnabled;
            if (!Model.Paused)
            {
                GUILayout.Label("（运行中游标跟随最新；按 P 暂停后可拖动）", _small);
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("一屏 tick 数 " + Model.TimelineWindowTicks, GUILayout.Width(120));
            Model.TimelineWindowTicks = Mathf.RoundToInt(GUILayout.HorizontalSlider(Model.TimelineWindowTicks, 60, 600));
            GUILayout.EndHorizontal();

            GUILayout.Label("图例", _small);
            GUILayout.BeginHorizontal();
            foreach (var kind in new[] { SegmentKind.Charge, SegmentKind.Startup, SegmentKind.Active, SegmentKind.Recovery, SegmentKind.CancelWindow, SegmentKind.ComboWindow })
            {
                DrawLegend(kind);
            }

            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            foreach (var kind in new[] { SegmentKind.Invuln, SegmentKind.Armor, SegmentKind.Guard, SegmentKind.Hitstop, SegmentKind.Reaction, SegmentKind.Downed })
            {
                DrawLegend(kind);
            }

            GUILayout.EndHorizontal();
            GUILayout.Label("标记：黄=输入　红=命中点　橙=裁决结果　青=辅助转向　紫=缓冲丢弃　下方青灰条=缓冲槽有内容", _small);

            GUILayout.Label("— 游标读数 —");
            var key = cursor + "|" + last;
            if (!string.Equals(key, _describeKey, StringComparison.Ordinal))
            {
                _describe = _timeline.Describe(Session!.Recording);
                _describeKey = key;
            }

            foreach (var line in _describe)
            {
                GUILayout.Label(line, _small);
            }
        }

        private void DrawLegend(SegmentKind kind)
        {
            var rect = GUILayoutUtility.GetRect(48, 16, GUILayout.Width(52));
            if (Event.current.type == EventType.Repaint)
            {
                Fill(new UnityEngine.Rect(rect.x, rect.y + 3, 8, 10), SegmentColor(kind));
            }

            GUI.Label(new UnityEngine.Rect(rect.x + 10, rect.y - 2, rect.width - 10, 20), SegmentShort(kind), _small);
        }

        private static string SegmentShort(SegmentKind kind)
        {
            switch (kind)
            {
                case SegmentKind.Charge: return "蓄力";
                case SegmentKind.Startup: return "前摇";
                case SegmentKind.Active: return "判定";
                case SegmentKind.Recovery: return "后摇";
                case SegmentKind.CancelWindow: return "取消";
                case SegmentKind.ComboWindow: return "连击";
                case SegmentKind.Invuln: return "无敌";
                case SegmentKind.Armor: return "霸体";
                case SegmentKind.Guard: return "格挡";
                case SegmentKind.Hitstop: return "顿帧";
                case SegmentKind.Reaction: return "硬直";
                case SegmentKind.Downed: return "倒地";
                default: return "起身";
            }
        }

        private static void Fill(UnityEngine.Rect rect, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>屏幕下方的时间轴（Timeline 页才画）：每实体一条，三条车道（阶段 / 窗口 / 状态）+ 玩家的缓冲槽车道 + 标记行。</summary>
        private void DrawTimelineArea(float scale)
        {
            var width = Screen.width / scale;
            var height = Screen.height / scale;
            var x = PanelWidth + 16f;
            var area = new UnityEngine.Rect(x, height - 292f, Math.Max(200f, width - x - 8f), 284f);
            GUILayout.BeginArea(area, GUI.skin.box);
            var window = Math.Max(30, Model.TimelineWindowTicks);
            var cursor = _timeline.Cursor;
            var right = _timeline.Pinned.HasValue ? Math.Min(Math.Max(0, _timeline.LastTick), cursor + window / 2) : Math.Max(0, _timeline.LastTick);
            right = Math.Max(right, window - 1); // 横轴宽度恒为一屏 tick 数：开局不足一屏时色条靠左，刻度不随时间伸缩
            var left = Math.Max(0, right - window + 1);
            GUILayout.Label("tick " + left + " … " + right + "　游标 " + cursor + (Model.Paused ? "　（点击色条区钉住游标）" : string.Empty), _small);
            _timelineScroll = GUILayout.BeginScrollView(_timelineScroll);
            foreach (var track in _timeline.Tracks)
            {
                DrawTrack(track, left, right, cursor);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawTrack(TimelineTrack track, int left, int right, int cursor)
        {
            const float labelWidth = 92f;
            const float laneHeight = 8f;
            var isPlayer = string.Equals(track.Entity, TimelineModel.PlayerLabel, StringComparison.Ordinal);
            var rect = GUILayoutUtility.GetRect(10, isPlayer ? 54f : 44f, GUILayout.ExpandWidth(true));
            var canvas = new UnityEngine.Rect(rect.x + labelWidth, rect.y, Math.Max(10f, rect.width - labelWidth - 4f), rect.height - 2f);
            var span = Math.Max(1, right - left + 1);
            float X(int tick) => canvas.x + (tick - left) / (float)span * canvas.width;

            GUI.Label(new UnityEngine.Rect(rect.x, rect.y, labelWidth, rect.height), track.Entity, _small);
            var ev = Event.current;
            if (ev.type == EventType.MouseDown && Model.Paused && canvas.Contains(ev.mousePosition))
            {
                var picked = left + Mathf.FloorToInt((ev.mousePosition.x - canvas.x) / canvas.width * span);
                _timeline.Pin(picked);
                ev.Use();
            }

            if (ev.type != EventType.Repaint)
            {
                return;
            }

            Fill(canvas, new Color(0.08f, 0.09f, 0.11f, 0.95f));
            var top = canvas.y + 11f;
            foreach (var segment in track.Segments)
            {
                var end = segment.EndOr(right + 1);
                if (end <= left || segment.Start > right)
                {
                    continue;
                }

                var x0 = X(Math.Max(left, segment.Start));
                var x1 = X(Math.Min(right + 1, end));
                var lane = SegmentLane(segment.Kind);
                Fill(new UnityEngine.Rect(x0, top + lane * (laneHeight + 1f), Math.Max(1f, x1 - x0), laneHeight), SegmentColor(segment.Kind));
            }

            if (isPlayer && Session != null)
            {
                for (var t = left; t <= right; t++)
                {
                    var buffer = TimelineModel.BufferAt(Session.Recording, t);
                    if (!string.IsNullOrEmpty(buffer))
                    {
                        Fill(new UnityEngine.Rect(X(t), top + 3 * (laneHeight + 1f), Math.Max(1f, canvas.width / span), laneHeight), new Color(0.35f, 0.7f, 0.7f));
                    }
                }
            }

            foreach (var mark in track.Marks)
            {
                if (mark.Tick < left || mark.Tick > right)
                {
                    continue;
                }

                Fill(new UnityEngine.Rect(X(mark.Tick), canvas.y + 1f, 2f, 10f), MarkColor(mark.Kind));
            }

            if (cursor >= left && cursor <= right)
            {
                Fill(new UnityEngine.Rect(X(cursor), canvas.y, 1.5f, canvas.height), Color.white);
            }
        }

        // ───────── 轨迹页与叠层 ─────────

        private void DrawTrajectoryTab()
        {
            GUILayout.Label("轨迹叠层（画在场景上；只读录制）。暂停并在时间轴页拖动游标，可回看那一刻之前的轨迹。");
            Model.OverlayPath = GUILayout.Toggle(Model.OverlayPath, "移动路径（玩家 + 靶子）");
            Model.OverlayVelocity = GUILayout.Toggle(Model.OverlayVelocity, "速度矢量");
            Model.OverlayShapes = GUILayout.Toggle(Model.OverlayShapes, "判定形状");
            Model.OverlaySweep = GUILayout.Toggle(Model.OverlaySweep, "判定扫掠体（判定相内每 tick 的位姿）");
            Model.OverlayContacts = GUILayout.Toggle(Model.OverlayContacts, "接触点与法线");
            Model.OverlayAssist = GUILayout.Toggle(Model.OverlayAssist, "目标辅助转角");
            GUILayout.BeginHorizontal();
            GUILayout.Label("回看 tick 数 " + Model.OverlayWindowTicks, GUILayout.Width(120));
            Model.OverlayWindowTicks = Mathf.RoundToInt(GUILayout.HorizontalSlider(Model.OverlayWindowTicks, 30, 600));
            GUILayout.EndHorizontal();
            GUILayout.Label("图例：蓝=玩家路径　灰=靶子路径　绿=速度　红=判定形状　橙=扫掠　黄点+线=接触点与法线　青=辅助转向", _small);
            GUILayout.Label("近似说明：位置取各固定步结束时的样本；判定形状取判定标记派发那一刻施法者的位姿；扫掠体是把形状重新锚定到判定相内各 tick 位姿，不是命中判定的逐位复刻。", _small);

            if (Session != null)
            {
                var to = _timeline.Cursor;
                var from = Math.Max(0, to - Model.OverlayWindowTicks);
                var contacts = TrajectoryModel.Contacts(Session.Recording, from, to);
                GUILayout.Label("— 窗口内接触 " + contacts.Count + " 次（最近 6 次）—");
                for (var i = Math.Max(0, contacts.Count - 6); i < contacts.Count; i++)
                {
                    var c = contacts[i];
                    GUILayout.Label("tick " + c.Tick + "　" + c.Attacker + " → " + c.Target + "　点(" + c.Point.X.ToString("0.00", CultureInfo.InvariantCulture) + "," + c.Point.Y.ToString("0.00", CultureInfo.InvariantCulture)
                        + ")　法线(" + c.Normal.X.ToString("0.00", CultureInfo.InvariantCulture) + "," + c.Normal.Y.ToString("0.00", CultureInfo.InvariantCulture) + ")　" + c.Result, _small);
                }

                var assists = TrajectoryModel.Assists(Session.Recording, from, to);
                for (var i = Math.Max(0, assists.Count - 3); i < assists.Count; i++)
                {
                    GUILayout.Label("辅助转向 tick " + assists[i].Tick + "：转 " + assists[i].DeltaDegrees.ToString("0.0", CultureInfo.InvariantCulture) + "°", _small);
                }
            }
        }

        private Vector2 ToGui(Vec2 world)
        {
            var screen = _stage!.StageUnityCamera!.WorldToScreen(world, 0.0);
            return new Vector2((float)screen.X, Screen.height - (float)screen.Y);
        }

        private static void DrawLine(Vector2 a, Vector2 b, Color color, float width)
        {
            var delta = b - a;
            var length = delta.magnitude;
            if (length < 0.5f)
            {
                return;
            }

            var saved = GUI.matrix;
            var old = GUI.color;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new UnityEngine.Rect(a.x, a.y - width / 2f, length, width), Texture2D.whiteTexture);
            GUI.matrix = saved;
            GUI.color = old;
        }

        private void DrawPolyline(IReadOnlyList<Vec2> points, Color color, float width, bool closed)
        {
            for (var i = 1; i < points.Count; i++)
            {
                DrawLine(ToGui(points[i - 1]), ToGui(points[i]), color, width);
            }

            if (closed && points.Count > 2)
            {
                DrawLine(ToGui(points[points.Count - 1]), ToGui(points[0]), color, width);
            }
        }

        /// <summary>轨迹叠层（Trajectory 页才画；单位是屏幕像素，不经面板缩放）：路径、速度、判定形状与扫掠、接触点与法线、辅助转向。</summary>
        private void DrawOverlay()
        {
            if (Model.Tab != LabTab.Trajectory || Event.current.type != EventType.Repaint || Session == null || _stage?.StageUnityCamera == null)
            {
                return;
            }

            var recording = Session.Recording;
            var to = _timeline.Cursor;
            var from = Math.Max(0, to - Model.OverlayWindowTicks);
            if (Model.OverlayPath)
            {
                DrawPolyline(TrajectoryModel.PlayerPath(recording, from, to), new Color(0.35f, 0.65f, 1f, 0.9f), 2f, false);
                var count = 0;
                foreach (var pair in Session.Context!.Dummies)
                {
                    if (++count > 8)
                    {
                        break;
                    }

                    DrawPolyline(TrajectoryModel.DummyPath(recording, pair.Key, from, to), new Color(0.7f, 0.7f, 0.7f, 0.7f), 1.5f, false);
                }
            }

            if (Model.OverlayVelocity)
            {
                foreach (var sample in TrajectoryModel.PlayerVelocities(recording, from, to))
                {
                    var tip = new Vec2(sample.Position.X + sample.Velocity.X * 0.25, sample.Position.Y + sample.Velocity.Y * 0.25);
                    DrawLine(ToGui(sample.Position), ToGui(tip), new Color(0.3f, 1f, 0.4f, 0.9f), 2f);
                }
            }

            if (Model.OverlayShapes || Model.OverlaySweep)
            {
                foreach (var shape in TrajectoryModel.HitShapes(recording, from, to))
                {
                    if (Model.OverlaySweep)
                    {
                        foreach (var step in shape.Sweep)
                        {
                            DrawPolyline(step.Value, new Color(1f, 0.6f, 0.2f, 0.35f), 1f, true);
                        }
                    }

                    if (Model.OverlayShapes)
                    {
                        DrawPolyline(shape.Outline, new Color(1f, 0.25f, 0.2f, 0.95f), 2f, true);
                    }
                }
            }

            if (Model.OverlayContacts)
            {
                foreach (var contact in TrajectoryModel.Contacts(recording, from, to))
                {
                    var p = ToGui(contact.Point);
                    var n = ToGui(new Vec2(contact.Point.X + contact.Normal.X * 0.6, contact.Point.Y + contact.Normal.Y * 0.6));
                    DrawLine(p, n, new Color(1f, 0.95f, 0.2f), 2f);
                    Fill(new UnityEngine.Rect(p.x - 4f, p.y - 4f, 8f, 8f), new Color(1f, 0.95f, 0.2f));
                }
            }

            if (Model.OverlayAssist)
            {
                foreach (var assist in TrajectoryModel.Assists(recording, from, to))
                {
                    DrawLine(ToGui(assist.From), ToGui(assist.To), new Color(0.4f, 1f, 1f, 0.9f), 2f);
                }
            }
        }

        // ───────── 评分页 ─────────

        private void DrawRatingTab()
        {
            GUILayout.Label("评分（对当前槽位的预设：" + LabLiveModel.FriendlyName(Model.Preset) + "，槽位 " + Model.ActiveSlot + "）");
            foreach (var dimension in RatingDimensions.All)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(RatingDimensions.Label(dimension), GUILayout.Width(110));
                var current = _rating.Score(dimension);
                for (var score = 1; score <= 5; score++)
                {
                    if (GUILayout.Toggle(current == score, score.ToString(CultureInfo.InvariantCulture), "Button", GUILayout.Width(34)) && current != score)
                    {
                        var d = dimension;
                        var s = score;
                        Defer(() => _rating.Rate(d, s));
                    }
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.Label("标签", _small);
            GUILayout.BeginHorizontal();
            foreach (var tag in RatingDimensions.Tags)
            {
                var on = System.Linq.Enumerable.Contains(_rating.CurrentTags, tag);
                if (GUILayout.Toggle(on, tag, "Button") != on)
                {
                    var t = tag;
                    Defer(() => _rating.ToggleTag(t));
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.Label("备注", _small);
            GUI.SetNextControlName("rate_note");
            _rating.Note = GUILayout.TextField(_rating.Note);
            if (GUILayout.Button(_rating.Complete ? "提交本次评分" : "提交本次评分（四项都要打分）"))
            {
                Defer(() => SubmitRating());
            }

            GUILayout.Label("已提交 " + _rating.Samples.Count + " 次", _small);
            var summary = _rating.Summary();
            foreach (var dimension in RatingDimensions.All)
            {
                if (summary.TryGetValue(dimension, out var mean))
                {
                    GUILayout.Label("　" + RatingDimensions.Label(dimension) + " 均值 " + mean.ToString("0.00", CultureInfo.InvariantCulture), _small);
                }
            }

            GUILayout.Label("— 记录 —");
            GUILayout.BeginHorizontal();
            GUILayout.Label("设备", GUILayout.Width(40));
            GUI.SetNextControlName("rate_device");
            RatingDevice = GUILayout.TextField(RatingDevice);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("行 id", GUILayout.Width(40));
            GUI.SetNextControlName("rate_id");
            RatingRowId = GUILayout.TextField(RatingRowId.Length > 0 || GUI.GetNameOfFocusedControl() == "rate_id" ? RatingRowId : DefaultRatingRowId());
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("保存评分到本地")) Defer(() => { var p = SaveRatingLocal(); Model.Note("评分明细 → " + p); });
            if (GUILayout.Button("导出为 feel.validation 记录")) Defer(() => ExportRatingValidation());
            GUILayout.EndHorizontal();
            GUILayout.Label("评分会带上预设版本、会话逻辑指纹哈希与设备条件；导出的表文件在本地目录，是否并入游戏仓库由人决定。", _small);
        }

        // ───────── 键盘焦点 ─────────

        /// <summary>每个 OnGUI 事件开头：更新"文本框正占着键盘"的标志，并处理 Esc 释放焦点。</summary>
        private void UpdateTextFocus()
        {
            var name = GUI.GetNameOfFocusedControl();
            _textFocus = name.StartsWith("tune_", StringComparison.Ordinal) || name.StartsWith("rate_", StringComparison.Ordinal);
            var ev = Event.current;
            if (_textFocus && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Escape)
            {
                GUIUtility.keyboardControl = 0;
                _edit.Clear();
                _textFocus = false;
                ev.Use();
            }
        }

        private void PollTabHotkey(Keyboard kb)
        {
            if (kb.f12Key.wasPressedThisFrame)
            {
                CycleTab();
            }
        }
    }
}
