#nullable enable
// FeelLabWindow：手感实验室面板（手感设计/06 第 4 节"面板"）。菜单 GameFoundation/手感实验室。
// 界面只是 LabPanelModel 的一层壳：选脚本/格子并运行（在引擎宿主上）、看度量、改手感档案覆盖（本地覆盖存储文件，不改源数据）、
// 拿两组覆盖在同一脚本上做 A/B 并看度量差异。逻辑都在模型里，由编辑模式测试覆盖。
// 判断记录（需要运行模式）：引擎宿主要驱动真实适配器与资源加载器，这些是运行模式下的单例；编辑模式下面板只显示提示，不运行。
using Adapter.Unity;
using System;
using System.Collections.Generic;
using System.IO;
using FeelLab.Unity;
using Lab;
using UnityEditor;
using UnityEngine;

namespace FeelLab.Unity.Editor
{
    public sealed class FeelLabWindow : EditorWindow
    {
        private const string MenuPath = "GameFoundation/手感实验室";

        private EngineLabHost? _host;
        private LabPanelModel? _model;
        private string _error = string.Empty;
        private string _abText = string.Empty;
        private Vector2 _scroll;
        private int _scriptIndex;
        private int _cellIndex;
        private int _overrideIndex;
        private int _abA;
        private int _abB = 1;
        private int _fieldIndex;
        private string _setName = "override_a";
        private string _opText = "set";
        private string _valueText = string.Empty;
        private string _unitText = string.Empty;
        private string _problems = string.Empty;
        private string _filter = string.Empty;

        [MenuItem(MenuPath)]
        public static void Open() => GetWindow<FeelLabWindow>("手感实验室");

        private static string StorePath => Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Library", "FeelLab", "overrides.json");

        private void EnsureModel()
        {
            if (_model != null)
            {
                return;
            }

            try
            {
                _host = EngineLabHost.Open();
                var host = _host;
                _model = new LabPanelModel(
                    host.LoadScripts(), script => host.RunnableCells(script),
                    (script, cell, overrides) =>
                    {
                        var options = new EngineLabOptions();
                        var run = host.Run(script, cell, options, overrides);
                        return new PanelRunResult(run.Fingerprint, run.Engine, host.JudgeExpectations(run, options, overrides));
                    },
                    host.Registry, StorePath);
                if (_model.Scripts.Count > 0)
                {
                    _model.SelectScript(_model.Scripts[0].Meta.ScriptId);
                }
            }
            catch (Exception ex)
            {
                _error = ex.Message;
                _model = null;
            }
        }

        private void OnGUI()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("引擎宿主需要运行模式（要驱动真实适配器与资源加载器）。请先进入运行模式。", MessageType.Info);
                return;
            }

            EnsureModel();
            if (_model == null)
            {
                EditorGUILayout.HelpBox(_error.Length > 0 ? _error : "实验室未就绪", MessageType.Error);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawSelection(_model);
            DrawOverrides(_model);
            DrawRun(_model);
            DrawAb(_model);
            EditorGUILayout.EndScrollView();
        }

        private void DrawSelection(LabPanelModel model)
        {
            EditorGUILayout.LabelField("脚本与格子", EditorStyles.boldLabel);
            var scriptIds = new List<string>();
            foreach (var script in model.Scripts)
            {
                scriptIds.Add(script.Meta.ScriptId);
            }

            var newScript = EditorGUILayout.Popup("脚本", _scriptIndex, scriptIds.ToArray());
            if (newScript != _scriptIndex || model.SelectedScript == null)
            {
                _scriptIndex = newScript;
                _cellIndex = 0;
                model.SelectScript(scriptIds[_scriptIndex]);
            }

            if (model.SelectedScript != null)
            {
                EditorGUILayout.HelpBox(model.SelectedScript.Meta.Description, MessageType.None);
            }

            var cells = new List<string>(model.Cells);
            if (cells.Count == 0)
            {
                EditorGUILayout.LabelField("该脚本没有可运行的格子");
                return;
            }

            var newCell = EditorGUILayout.Popup("格子", Math.Min(_cellIndex, cells.Count - 1), cells.ToArray());
            if (newCell != _cellIndex || model.SelectedCell == null)
            {
                _cellIndex = newCell;
                model.SelectCell(cells[_cellIndex]);
            }
        }

        private void DrawOverrides(LabPanelModel model)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("手感档案覆盖（本地覆盖存储，不改源数据）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("存储文件", model.StorePath);
            var names = new List<string> { "(无覆盖)" };
            names.AddRange(model.OverrideNames);
            _overrideIndex = Math.Min(_overrideIndex, names.Count - 1);
            var picked = EditorGUILayout.Popup("应用覆盖组", _overrideIndex, names.ToArray());
            if (picked != _overrideIndex)
            {
                _overrideIndex = picked;
                model.SelectOverride(picked == 0 ? null : names[picked]);
            }

            _setName = EditorGUILayout.TextField("覆盖组名", _setName);
            _filter = EditorGUILayout.TextField("字段过滤", _filter);
            var fields = new List<string>();
            foreach (var name in model.FieldNames)
            {
                if (_filter.Length == 0 || name.Contains(_filter))
                {
                    fields.Add(name);
                }
            }

            if (fields.Count > 0)
            {
                _fieldIndex = Math.Min(_fieldIndex, fields.Count - 1);
                _fieldIndex = EditorGUILayout.Popup("字段", _fieldIndex, fields.ToArray());
            }

            _opText = EditorGUILayout.TextField("操作(set/multiply/add/remove)", _opText);
            _valueText = EditorGUILayout.TextField("值", _valueText);
            _unitText = EditorGUILayout.TextField("作用单位（空为全局）", _unitText);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("写入覆盖") && fields.Count > 0)
            {
                var problems = model.SetOverride(_setName, fields[_fieldIndex], _opText, _valueText, _unitText);
                _problems = string.Join("\n", problems);
            }

            if (GUILayout.Button("删除覆盖组"))
            {
                model.RemoveOverrideSet(_setName);
                _overrideIndex = 0;
            }

            EditorGUILayout.EndHorizontal();
            if (_problems.Length > 0)
            {
                EditorGUILayout.HelpBox(_problems, MessageType.Warning);
            }

            foreach (var set in model.Store.Sets)
            {
                EditorGUILayout.LabelField(set.Name, string.Join("；", set.Writes));
            }
        }

        private void DrawRun(LabPanelModel model)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("运行与度量", EditorStyles.boldLabel);
            if (GUILayout.Button("在引擎宿主上运行"))
            {
                try
                {
                    model.Run();
                    _error = string.Empty;
                }
                catch (Exception ex)
                {
                    _error = ex.Message;
                }
            }

            if (_error.Length > 0)
            {
                EditorGUILayout.HelpBox(_error, MessageType.Error);
            }

            if (model.LastRun != null)
            {
                foreach (var expectation in model.LastRun.Expectations)
                {
                    EditorGUILayout.LabelField(expectation.Ok ? "期望通过" : "期望未通过", expectation.Expectation.Id + " " + expectation.Message);
                }

                foreach (var row in model.MetricRows())
                {
                    EditorGUILayout.LabelField(row.FullName + " [" + row.Class + "]", row.Value);
                }
            }
        }

        private void DrawAb(LabPanelModel model)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("A/B 对比", EditorStyles.boldLabel);
            var names = new List<string> { "(无覆盖)" };
            names.AddRange(model.OverrideNames);
            _abA = Math.Min(_abA, names.Count - 1);
            _abB = Math.Min(_abB, names.Count - 1);
            _abA = EditorGUILayout.Popup("A", _abA, names.ToArray());
            _abB = EditorGUILayout.Popup("B", _abB, names.ToArray());
            if (GUILayout.Button("运行 A/B"))
            {
                try
                {
                    var report = model.RunAb(_abA == 0 ? null : names[_abA], _abB == 0 ? null : names[_abB]);
                    _abText = report.Format();
                    _error = string.Empty;
                }
                catch (Exception ex)
                {
                    _error = ex.Message;
                }
            }

            if (_abText.Length > 0)
            {
                EditorGUILayout.TextArea(_abText);
            }
        }
    }
}
