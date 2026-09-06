#nullable enable
// RespawnFeedbackReceiver：unit.respawned 的视图侧占位演出（W3b 收边，拍板 3/8"复活演出：订阅
// unit.respawned（EventKeys）→ 视图闪白/无敌帧占位演出（经原语 Flash+Fade）"）。
//
// 判断记录（为什么是"占位"演出、只用已有两个原语）：09/06 均未给"复活"拍板专属的表现规格
// （不像"闪白"在 09 第 4.1 节原语清单例句里直接点名"受击/无敌帧反馈"），本类型因此不发明新的
// 表现效果，只组合两个已经真实落地的程序动画原语：Flash（一次性闪白，标记"发生了什么"）+
// Fade（短暂半透明，标记"无敌帧窗口"，窗口结束后显式淡回不透明）——具体游戏若需要更讲究的复活
// 演出（无敌帧期间闪烁频率、专属特效），应在自己的表现层装配代码里替换/扩展本类型，本类型只
// 保证"最小可用、看得出复活发生了"这一底线。
//
// 铁律遵守：只经 IEventBus.Subscribe 订阅事件（同 FlashReceiver/FreezeFrameReceiver 既有惯例），
// 只经 ICharacterRig.ProceduralAnim 这一扇既有窄门下达动作指令，不新增任何 IRenderer2D 契约方法。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Rules.Common;
using Presentation.Render;
using Presentation.ViewBinding;

namespace Adapter.Unity.Presentation
{
    public sealed class RespawnFeedbackReceiver : IDisposable
    {
        private const double InvulnerableFadeAlpha = 0.4;
        private const double FadeTransitionSeconds = 0.2;
        private const double InvulnerableWindowSeconds = 1.0;

        private sealed class PendingRestore
        {
            public UnitySpriteView View = null!;
            public double Remaining;
        }

        private readonly ViewBinder _viewBinder;
        private readonly SubscriptionHandle _subscription;
        private readonly List<PendingRestore> _pending = new List<PendingRestore>();

        /// <summary>迄今为止成功触发过复活演出的次数（未绑定 View 或非 sprite 型 View 时不计数，
        /// 同 <see cref="FlashReceiver.TriggerCount"/> 既有惯例）。</summary>
        public int TriggerCount { get; private set; }

        public RespawnFeedbackReceiver(IEventBus bus, ViewBinder viewBinder)
        {
            if (bus == null) throw new ArgumentNullException(nameof(bus));
            _viewBinder = viewBinder ?? throw new ArgumentNullException(nameof(viewBinder));
            _subscription = bus.Subscribe<UnitRespawnedEvent>(RulesEventKeys.UnitRespawned, OnRespawned);
        }

        private void OnRespawned(UnitRespawnedEvent evt)
        {
            if (!_viewBinder.TryGetView(evt.UnitId, out var view) || view is not UnitySpriteView spriteView)
            {
                // 未绑定 View（实体尚未创建/已销毁）或非 sprite 型 View：静默跳过，同
                // FlashReceiver.Show 既有惯例——表现层反馈允许"目标此刻还看不到"这种正常竞态。
                return;
            }

            spriteView.Rig.ProceduralAnim.Flash(FlashParams.Default);
            spriteView.PlayFade(new FadeParams(InvulnerableFadeAlpha, FadeTransitionSeconds));
            _pending.Add(new PendingRestore { View = spriteView, Remaining = InvulnerableWindowSeconds });
            TriggerCount++;
        }

        /// <summary>由引导代码的帧回调每帧调用（同 <c>FlashReceiver.Tick</c>/
        /// <c>FreezeFrameReceiver.Tick</c> 既有惯例）：推进无敌帧窗口剩余时间，到期后显式淡回
        /// 不透明——<see cref="FadeParams"/> 的既有语义是"到达并保持"（见该类型注释），需要消费方
        /// 主动发起第二次 Fade 才能"淡回"，不会自己归零。</summary>
        public void Tick(double deltaSeconds)
        {
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                var entry = _pending[i];
                entry.Remaining -= deltaSeconds;
                if (entry.Remaining <= 0)
                {
                    entry.View.PlayFade(new FadeParams(1.0, FadeTransitionSeconds));
                    _pending.RemoveAt(i);
                }
            }
        }

        public void Dispose()
        {
            _subscription.Dispose();
            _pending.Clear();
        }
    }
}
