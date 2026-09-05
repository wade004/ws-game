#nullable enable
// 对话框、设置面板（十个界面单元的第 5、8 个）。
//
// 判断记录（占位调试文本标注）：同 GameplayPanels.cs 顶部判断记录——本文件"（当前无对话）"
// "设置""（按键绑定）""保存设置"等属于 UI 框架级 chrome 文案，不经 l10n.text；对话选项本身的
// 文案（<see cref="DialogPanel.RefreshUi"/> 里的 <c>_l10n.Text(...)</c> 调用）已经是真正经
// l10n.text 查询的游戏内容文本。
using System;
using System.Text;
using Core.Foundation.InputMap;
using Core.Foundation.Localization;
using Presentation.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Adapter.Unity.Ui.Panels
{
    /// <summary>对话框：gossip/story 选项，提交选项意图（09 §7.1、<see cref="DialogViewModel"/>，
    /// 阶段 4 验收标准 4 的前置——命中/暴击/死亡反馈不经本面板，本面板只覆盖"对话框选项提交后
    /// DialogHost 状态推进"这条 PlayMode 用例）。</summary>
    public sealed class DialogPanel : UiPanelBehaviour
    {
        private DialogViewModel _vm = null!;
        private UiIntents _intents = null!;
        private IL10nHost _l10n = null!;
        private TextMeshProUGUI _bodyLabel = null!;
        private RectTransform _optionsList = null!;
        private readonly System.Collections.Generic.List<GameObject> _optionButtons = new System.Collections.Generic.List<GameObject>();

        public void Construct(RectTransform parent, DialogViewModel vm, UiIntents intents, IL10nHost l10n)
        {
            _vm = vm;
            _intents = intents;
            _l10n = l10n;
            var root = UiWidgets.CreatePanelBackground("DialogPanel", parent, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(420f, 220f), new Vector2(0f, 200f));
            var vlist = UiWidgets.CreateVerticalList("Content", root, 6f);
            UiWidgets.SetRect(vlist, Vector2.zero, Vector2.one, new Vector2(10, 10), new Vector2(-10, -10));
            _bodyLabel = UiWidgets.CreateLabel("Body", vlist, "（当前无对话）", 16);
            _optionsList = UiWidgets.CreateVerticalList("Options", vlist, 4f);
        }

        public override void RefreshUi()
        {
            if (_vm.Story != null)
            {
                _bodyLabel.text = _l10n.Text(_vm.Story.TextKey);
                RebuildOptions(_vm.Story.VisibleBranches.Count, i => _l10n.Text(_vm.Story.VisibleBranches[i].TextKey), i => _intents.ChooseDialogOption(_vm.Story.VisibleBranches[i].Index));
            }
            else if (_vm.Gossip != null)
            {
                _bodyLabel.text = "（NPC 对话选项）";
                RebuildOptions(_vm.Gossip.Options.Count, i => _l10n.Text(_vm.Gossip.Options[i].TextKey), i => _intents.ChooseDialogOption(_vm.Gossip.Options[i].Index));
            }
            else
            {
                _bodyLabel.text = "（当前无对话）";
                RebuildOptions(0, null, null);
            }
        }

        private void RebuildOptions(int count, Func<int, string>? textOf, Action<int>? onClick)
        {
            while (_optionButtons.Count < count)
            {
                var idx = _optionButtons.Count;
                var (rowRoot, _, _) = UiWidgets.CreateButton($"Option{idx}", _optionsList, "-", () => onClick?.Invoke(idx));
                _optionButtons.Add(rowRoot.gameObject);
            }
            while (_optionButtons.Count > count)
            {
                var last = _optionButtons[_optionButtons.Count - 1];
                _optionButtons.RemoveAt(_optionButtons.Count - 1);
                Destroy(last);
            }
            for (var i = 0; i < count; i++)
            {
                var label = _optionButtons[i].transform.Find("Label").GetComponent<TextMeshProUGUI>();
                label.text = textOf!(i);
            }
        }
    }

    /// <summary>设置面板：音量分层、按键绑定展示、落盘经 <see cref="ISettingsStore"/>（09 §7.1、
    /// <see cref="SettingsViewModel"/>）。</summary>
    public sealed class SettingsPanel : UiPanelBehaviour
    {
        private SettingsViewModel _vm = null!;
        private UiIntents _intents = null!;
        private Action _onSave = null!;
        private TextMeshProUGUI _bindingsLabel = null!;
        private readonly System.Collections.Generic.Dictionary<string, Slider> _volumeSliders = new System.Collections.Generic.Dictionary<string, Slider>(StringComparer.Ordinal);

        public void Construct(
            RectTransform parent, SettingsViewModel vm, UiIntents intents, Action onSave,
            Func<double>? getSfxBusVolume = null, Action<double>? onSfxBusVolumeChanged = null)
        {
            _vm = vm;
            _intents = intents;
            _onSave = onSave;
            var root = UiWidgets.CreatePanelBackground("SettingsPanel", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(380f, 360f), Vector2.zero);
            var vlist = UiWidgets.CreateVerticalList("Content", root, 6f);
            UiWidgets.SetRect(vlist, Vector2.zero, Vector2.one, new Vector2(12, 12), new Vector2(-12, -12));

            UiWidgets.CreateLabel("Title", vlist, "设置", 20);

            // 判断记录（IAudio 总线音量为什么单独一条 slider，不与下面的"分层音量"合并）：
            // presentation/ui 的"分层音量"读写完全经外部注入回调（该模块不拥有 SfxPlayer，见
            // SettingsViewModel 类型注释判断记录），SfxPlayer 自身也没有对外暴露"当前分层音量"
            // 的读取方法（只有 SetLayerVolume 这一个方向，见 presentation/vfx_sfx/contracts/
            // ISfxPlayer.cs），因此"分层音量"这条读写链路在纯只读验收意义上不可判定"确实变了"；
            // IAudio.SetBusVolume/UnityAudio.GetBusVolume（本任务新增的非契约诊断读取，见该类型
            // 判断记录）提供了一条可验证的总线音量链路，满足阶段 4 验收标准"设置面板改音量后
            // IAudio 总线音量变化"的字面要求，与下面的"分层音量"滑条并存、互不替代。
            if (getSfxBusVolume != null && onSfxBusVolumeChanged != null)
            {
                BuildVolumeRow(vlist, "总线：Sfx", getSfxBusVolume(), v => onSfxBusVolumeChanged(v));
            }

            foreach (var layer in vm.LayerVolumes.Keys)
            {
                var capturedLayer = layer;
                var slider = BuildVolumeRow(vlist, layer, vm.LayerVolumes[layer], v => _intents.SetLayerVolume(capturedLayer, v));
                _volumeSliders[layer] = slider;
            }

            _bindingsLabel = UiWidgets.CreateLabel("Bindings", vlist, "（按键绑定）", 13);
            UiWidgets.CreateButton("SaveButton", vlist, "保存设置", () => _onSave());
        }

        private static Slider BuildVolumeRow(Transform parent, string label, double initialValue, Action<double> onChanged)
        {
            var row = UiWidgets.CreateRoot($"Volume_{label}", parent);
            row.sizeDelta = new Vector2(0, 26);
            var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.spacing = 8; hl.childControlWidth = false; hl.childControlHeight = true;
            var labelTmp = UiWidgets.CreateLabel("Label", row, label, 14);
            labelTmp.rectTransform.sizeDelta = new Vector2(120, 24);

            var sliderGo = new GameObject("Slider", typeof(RectTransform));
            var sliderRect = (RectTransform)sliderGo.transform;
            sliderRect.SetParent(row, false);
            sliderRect.sizeDelta = new Vector2(180, 20);
            var slider = sliderGo.AddComponent<Slider>();
            slider.minValue = 0f; slider.maxValue = 1f;
            var bgRect = UiWidgets.CreatePanelBackground("Background", sliderRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var fillAreaGo = new GameObject("FillArea", typeof(RectTransform));
            var fillArea = (RectTransform)fillAreaGo.transform; fillArea.SetParent(sliderRect, false);
            fillArea.anchorMin = Vector2.zero; fillArea.anchorMax = Vector2.one; fillArea.offsetMin = new Vector2(4, 4); fillArea.offsetMax = new Vector2(-4, -4);
            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var fillRect = (RectTransform)fillGo.transform; fillRect.SetParent(fillArea, false);
            fillRect.anchorMin = Vector2.zero; fillRect.anchorMax = new Vector2(0.5f, 1f); fillRect.offsetMin = Vector2.zero; fillRect.offsetMax = Vector2.zero;
            var fillImage = fillGo.GetComponent<Image>(); fillImage.sprite = UiSkin.FlatSprite; fillImage.color = UiSkin.AccentColor;
            slider.fillRect = fillRect; slider.targetGraphic = bgRect.GetComponent<Image>();
            slider.value = initialValue > 0 ? (float)initialValue : 1f;
            slider.onValueChanged.AddListener(v => onChanged(v));
            return slider;
        }

        public override void RefreshUi()
        {
            var sb = new StringBuilder();
            foreach (var row in _vm.Bindings)
            {
                sb.Append(row.ActionName).Append(": ").Append(string.Join(",", row.Bindings));
                for (var i = 0; i < row.Conflicts.Count; i++)
                {
                    if (row.Conflicts[i].Count > 0)
                    {
                        sb.Append("  [冲突: ").Append(string.Join(",", row.Conflicts[i])).Append(']');
                    }
                }
                sb.Append('\n');
            }
            _bindingsLabel.text = sb.Length > 0 ? sb.ToString() : "（无按键绑定配置）";
        }
    }
}
