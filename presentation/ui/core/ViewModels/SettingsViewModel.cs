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
    /// <para>
    /// 判断记录（按键绑定行只列"当前已声明"的动作，测试覆盖第二批缺陷 1）：动作名清单在构造期就要给，
    /// 而输入映射宿主通常在装配根构造期才新建、此时尚无任何动作集被声明——此前对清单里未声明的动作盲调
    /// <see cref="IInputMapHost.GetBindings"/>，抛 <see cref="InvalidOperationException"/>，清单选项事实上
    /// 不可用。现在每次 <see cref="Refresh"/> 都重算：用 <see cref="IInputMapHost.GetDeclaredActionNames"/>
    /// 取当前已声明动作，清单里未声明的动作跳过（不抛、不占行），之后声明了再 <see cref="Refresh"/> 行即出现；
    /// 不传清单（<c>null</c>）则列出全部已声明动作（声明顺序）。输入映射宿主没有"声明/绑定成功变化"事件
    /// （只有重绑定冲突事件，已订阅），所以"变化后刷新"靠 <see cref="Refresh"/> 重算，不新增事件契约。
    /// 宿主若不支持枚举（<see cref="IInputMapHost.GetDeclaredActionNames"/> 返回 <c>null</c>，第三方实现的
    /// 默认值）：显式清单退化为信任调用方（行为同修复前，清单里出现未声明动作仍由宿主抛出）；<c>null</c>
    /// 清单则无行可列。
    /// </para>
    /// </summary>
    public sealed class SettingsViewModel : IDisposable
    {
        private readonly IUiDataSource _dataSource;
        private readonly IL10nHost _l10n;
        private readonly IInputMapHost _inputMap;
        private readonly IReadOnlyList<string>? _actionNames;
        private readonly IAudioLayerVolumeHost _audioVolume;
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();
        private readonly List<BindingRow> _bindingRows = new List<BindingRow>();
        private readonly Dictionary<string, double> _layerVolumes = new Dictionary<string, double>();

        public Id Locale { get; private set; }

        public IReadOnlyList<Id> SupportedLocales => _l10n.SupportedLocales;

        public IReadOnlyList<BindingRow> Bindings => _bindingRows;

        public IReadOnlyDictionary<string, double> LayerVolumes => _layerVolumes;

        /// <summary>按键绑定行只列 <paramref name="actionNames"/> 里<b>当前已声明</b>的动作（未声明的跳过，
        /// 之后声明了 <see cref="Refresh"/> 即出现，见类型注释判断记录）；<paramref name="actionNames"/>
        /// 不可为 <c>null</c>（要"全部已声明动作"用不带清单的重载）。</summary>
        public SettingsViewModel(
            IUiDataSource dataSource,
            IL10nHost l10n,
            IInputMapHost inputMap,
            IReadOnlyList<string> actionNames,
            IAudioLayerVolumeHost audioVolume)
            : this(dataSource, l10n, inputMap, actionNames ?? throw new ArgumentNullException(nameof(actionNames)), audioVolume, listAllDeclared: false)
        {
        }

        /// <summary>按键绑定行列出输入映射宿主当前已声明的<b>全部</b>动作（声明顺序），每次
        /// <see cref="Refresh"/> 重算，之后新声明的动作随刷新出现。</summary>
        public SettingsViewModel(
            IUiDataSource dataSource,
            IL10nHost l10n,
            IInputMapHost inputMap,
            IAudioLayerVolumeHost audioVolume)
            : this(dataSource, l10n, inputMap, null, audioVolume, listAllDeclared: true)
        {
        }

        private SettingsViewModel(
            IUiDataSource dataSource,
            IL10nHost l10n,
            IInputMapHost inputMap,
            IReadOnlyList<string>? actionNames,
            IAudioLayerVolumeHost audioVolume,
            bool listAllDeclared)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            _l10n = l10n ?? throw new ArgumentNullException(nameof(l10n));
            _inputMap = inputMap ?? throw new ArgumentNullException(nameof(inputMap));
            _actionNames = listAllDeclared ? null : actionNames;
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
            foreach (var actionName in ResolveActionNames())
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

        /// <summary>本次刷新要展示的动作名：已声明动作 ∩ 显式清单（清单按调用方给的顺序），或全部已声明动作。</summary>
        private IReadOnlyList<string> ResolveActionNames()
        {
            var declared = _inputMap.GetDeclaredActionNames();
            if (_actionNames == null)
            {
                return declared ?? Array.Empty<string>();
            }

            if (declared == null)
            {
                return _actionNames;
            }

            var declaredSet = new HashSet<string>(declared, StringComparer.Ordinal);
            var result = new List<string>(_actionNames.Count);
            foreach (var name in _actionNames)
            {
                if (declaredSet.Contains(name))
                {
                    result.Add(name);
                }
            }
            return result;
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
