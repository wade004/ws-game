using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Numbers.PowerSet;
using Core.Numbers.Progression;

namespace Presentation.Ui
{
    /// <summary>单条资源条快照（当前值/上限）。</summary>
    public readonly struct PowerBarSnapshot
    {
        public double Current { get; }

        public double Max { get; }

        public PowerBarSnapshot(double current, double max)
        {
            Current = current;
            Max = max;
        }
    }

    /// <summary>
    /// HUD 视图模型（见 09_表现层.md 第 7.1 节 UI 组成"HUD"——"生命/资源条、等级、目标框"，见
    /// <see cref="UiPanel.Hud"/> 判断记录：状态栏与目标框并入本视图模型，不单开面板类别）。纯数据
    /// + 刷新逻辑，不含任何绘制：<see cref="Refresh"/> 经 <see cref="IUiDataSource.Query"/> 重新拉取
    /// 快照，构造期订阅 <c>power.changed</c>/<c>power.depleted</c>/<c>progression.level_up</c> 事件
    /// 触发自动刷新（铁律 P1、P2）。
    /// </summary>
    public sealed class HudViewModel : IDisposable
    {
        private readonly IUiDataSource _dataSource;
        private readonly IReadOnlyList<Id> _powerTypes;
        private readonly List<SubscriptionHandle> _subscriptions = new List<SubscriptionHandle>();
        private readonly Dictionary<Id, PowerBarSnapshot> _powerBars = new Dictionary<Id, PowerBarSnapshot>();
        private readonly Dictionary<Id, PowerBarSnapshot> _targetPowerBars = new Dictionary<Id, PowerBarSnapshot>();

        /// <summary>本视图模型绑定的玩家单位 id（查询本身经 <see cref="IUiDataSource"/> 的
        /// <c>player.*</c> 路径已经隐式绑定到当前玩家，这里只做展示/断言用途）。</summary>
        public Id PlayerId { get; }

        public int Level { get; private set; }

        public IReadOnlyDictionary<Id, PowerBarSnapshot> PowerBars => _powerBars;

        /// <summary>目标框：当前有目标且至少一种配置的资源类型可查到时非空。</summary>
        public bool HasTarget { get; private set; }

        public IReadOnlyDictionary<Id, PowerBarSnapshot> TargetPowerBars => _targetPowerBars;

        public HudViewModel(IUiDataSource dataSource, Id playerId, IReadOnlyList<Id> powerTypes)
        {
            _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
            PlayerId = playerId;
            _powerTypes = powerTypes ?? throw new ArgumentNullException(nameof(powerTypes));

            _subscriptions.Add(_dataSource.Subscribe(PowerEventKeys.Changed, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(PowerEventKeys.Depleted, OnRelevantEvent));
            _subscriptions.Add(_dataSource.Subscribe(ProgressionEventKeys.LevelUp, OnRelevantEvent));

            Refresh();
        }

        private void OnRelevantEvent(IEvent evt) => Refresh();

        public void Refresh()
        {
            var level = _dataSource.Query($"player.level");
            Level = level.HasValue ? (int)level.Value.AsInt : Level;

            _powerBars.Clear();
            _targetPowerBars.Clear();
            HasTarget = false;

            foreach (var powerType in _powerTypes)
            {
                var current = _dataSource.Query($"player.power.{powerType}.current");
                var max = _dataSource.Query($"player.power.{powerType}.max");
                if (current.HasValue && max.HasValue)
                {
                    _powerBars[powerType] = new PowerBarSnapshot(current.Value.AsNumber, max.Value.AsNumber);
                }

                var targetCurrent = _dataSource.Query($"target.power.{powerType}.current");
                var targetMax = _dataSource.Query($"target.power.{powerType}.max");
                if (targetCurrent.HasValue && targetMax.HasValue)
                {
                    _targetPowerBars[powerType] = new PowerBarSnapshot(targetCurrent.Value.AsNumber, targetMax.Value.AsNumber);
                    HasTarget = true;
                }
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
