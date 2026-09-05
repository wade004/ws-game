using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.InputMap;
using Core.Foundation.Localization;
using Presentation.VfxSfx.Contracts;

namespace Presentation.Ui
{
    /// <summary>一个按键绑定动作的展示行：当前绑定列表 + 每条绑定当前是否与其它动作冲突。</summary>
    public readonly struct BindingRow
    {
        public string ActionName { get; }

        public IReadOnlyList<string> Bindings { get; }

        /// <summary>与 <see cref="Bindings"/> 等长；<c>Conflicts[i]</c> 是占用
        /// <c>Bindings[i]</c> 的其它动作名列表（不含本动作自己），空列表表示该绑定当前无冲突。</summary>
        public IReadOnlyList<IReadOnlyList<string>> Conflicts { get; }

        public BindingRow(string actionName, IReadOnlyList<string> bindings, IReadOnlyList<IReadOnlyList<string>> conflicts)
        {
            ActionName = actionName;
            Bindings = bindings;
            Conflicts = conflicts;
        }
    }

    /// <summary>
    /// 设置面板视图模型（见 09_表现层.md 第 7.1 节 UI 组成"设置""按键绑定面板"、任务书"音量分层、
    /// 语言、按键绑定列表与冲突提示"）。
    /// <para>
    /// 判断记录（缺口 12 恢复，取代此前"注入 <see cref="Func{String, Double}"/> 回调"的搁置）：
    /// 音量分层清单与读音量均改由构造期注入的 <see cref="IAudioLayerVolumeHost"/> 提供（见该接口
    /// 类型注释），不再各自定义一份平行的层清单/回调；写音量仍经 <see cref="UiIntents.SetLayerVolume"/>
    /// 转发（该方法内部同样已改接同一个 <see cref="IAudioLayerVolumeHost"/>，见其类型注释），本
    /// 视图模型侧不直接持有写入口——UI 控件对用户输入的响应统一走 <see cref="UiIntents"/>（09 第
    /// 7.2 节铁律 P3），本类型只负责"读出来展示"。
    /// </para>
    /// </summary>
    public sealed class SettingsViewModel : IDisposable
    {
        private readonly IUiDataSource _dataSource;
        private readonly IL10nHost _l10n;
        private readonly IInputMapHost _inputMap;
        private readonly IReadOnlyList<string> _actionNames;
        private readonly IAudioLayerVolumeHost _audioVolume;
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();
        private readonly List<BindingRow> _bindingRows = new List<BindingRow>();
        private readonly Dictionary<string, double> _layerVolumes = new Dictionary<string, double>();

        public Id Locale { get; private set; }

        public IReadOnlyList<Id> SupportedLocales => _l10n.SupportedLocales;

        public IReadOnlyList<BindingRow> Bindings => _bindingRows;

        public IReadOnlyDictionary<string, double> LayerVolumes => _layerVolumes;

        public SettingsViewModel(
            IUiDataSource dataSource,
            IL10nHost l10n,
            IInputMapHost inputMap,
            IReadOnlyList<string> actionNames,
            IAudioLayerVolumeHost audioVolume)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            _l10n = l10n ?? throw new ArgumentNullException(nameof(l10n));
            _inputMap = inputMap ?? throw new ArgumentNullException(nameof(inputMap));
            _actionNames = actionNames ?? throw new ArgumentNullException(nameof(actionNames));
            _audioVolume = audioVolume ?? throw new ArgumentNullException(nameof(audioVolume));

            _subscriptions.Add(_dataSource.Subscribe(L10nEventKeys.LanguageChanged, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(InputMapEventKeys.RebindConflict, OnRelevantEvent));

            Refresh();
        }

        private void OnRelevantEvent(IEvent evt) => Refresh();

        public void Refresh()
        {
            Locale = _l10n.GetLocale();

            _bindingRows.Clear();
            foreach (var actionName in _actionNames)
            {
                var bindings = _inputMap.GetBindings(actionName);
                var conflicts = new List<IReadOnlyList<string>>(bindings.Count);
                foreach (var binding in bindings)
                {
                    var conflicting = new List<string>();
                    foreach (var other in _inputMap.GetConflicts(binding))
                    {
                        if (!string.Equals(other, actionName, StringComparison.Ordinal))
                        {
                            conflicting.Add(other);
                        }
                    }
                    conflicts.Add(conflicting);
                }
                _bindingRows.Add(new BindingRow(actionName, bindings, conflicts));
            }

            _layerVolumes.Clear();
            foreach (var layer in _audioVolume.Layers)
            {
                _layerVolumes[layer] = _audioVolume.GetVolume(layer);
            }
        }

        public void Dispose()
        {
            foreach (var handle in _subscriptions)
            {
                handle.Dispose();
            }
            _subscriptions.Clear();
        }
    }
}
