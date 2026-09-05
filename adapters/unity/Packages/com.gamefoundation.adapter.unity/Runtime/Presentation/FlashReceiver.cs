#nullable enable
// FlashReceiver：09_表现层.md 第 6.1 节 Flash（闪白）反馈动作的 Unity 侧落地。
//
// 判断记录（duration/intensity 从哪来）：IFeedbackSink.Flash(entityId, profileId) 只带一个
// "闪白配置引用 Id"，框架未定义任何 flash_profile 一类的数据表/schema 承载具体时长与强度
// （04/09 均未给出），本类型不代为发明一张新表；固定用一个可运行的默认时长/强度（0.15 秒、
// 过曝到 2 倍，见 UnitySpriteView.SetFlash/ClearFlash），profileId 目前只用于诊断日志，不影响
// 具体数值——具体游戏接入真正的闪白配置数据后，应替换本类型对 profileId 的解析方式。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Presentation.ViewBinding;

namespace Adapter.Unity.Presentation
{
    public sealed class FlashReceiver
    {
        private const double DefaultDurationSeconds = 0.15;
        private const double DefaultIntensity = 1.0;

        private sealed class ActiveFlash
        {
            public UnitySpriteView View = null!;
            public double Remaining;
        }

        private readonly ViewBinder _viewBinder;
        private readonly List<ActiveFlash> _active = new List<ActiveFlash>();

        public FlashReceiver(ViewBinder viewBinder)
        {
            _viewBinder = viewBinder ?? throw new ArgumentNullException(nameof(viewBinder));
        }

        /// <summary>迄今为止成功触发过闪白效果的次数（未绑定 View 或非 sprite 型 View 时不计数）。</summary>
        public int TriggerCount { get; private set; }

        /// <summary>绑定给 <c>PresentationAssemblyOptions.OnFlash</c>。</summary>
        public void Show(Id entityId, Id profileId)
        {
            if (!_viewBinder.TryGetView(entityId, out var view) || view is not UnitySpriteView spriteView)
            {
                // 未绑定 View（实体已销毁/尚未创建）或非 sprite 型 View（当前框架只落地了
                // sprite 型 View，见包 README IRenderer3D"声明降级"）时静默跳过，不抛异常
                // （表现层反馈动作允许"目标已消失"这种正常竞态）。
                return;
            }

            spriteView.SetFlash(DefaultIntensity);
            _active.Add(new ActiveFlash { View = spriteView, Remaining = DefaultDurationSeconds });
            TriggerCount++;
        }

        /// <summary>由 <c>GameFoundationBootstrap.Update</c> 每帧调用：推进闪白剩余时间，到期后
        /// 复原颜色。</summary>
        public void Tick(double deltaSeconds)
        {
            for (var i = _active.Count - 1; i >= 0; i--)
            {
                var entry = _active[i];
                entry.Remaining -= deltaSeconds;
                if (entry.Remaining <= 0.0)
                {
                    if (entry.View.IsAlive)
                    {
                        entry.View.ClearFlash();
                    }
                    _active.RemoveAt(i);
                }
            }
        }
    }
}
