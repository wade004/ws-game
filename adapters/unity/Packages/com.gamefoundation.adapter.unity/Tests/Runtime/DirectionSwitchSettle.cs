#nullable enable
// DirectionSwitchSettle：ADR-0112 之后旧用例的驱动辅助。方向变化改为"期望方向变了，视图每帧 SyncPose 时询问
// 新方向是否准备好，准备好当帧提交"，因此冷加载下单次 SyncPose 不会当帧换向；生产里绑定器每帧调用
// SyncPose、宿主每帧 Tick 加载器，这里的辅助照生产的方式每帧重复 SyncPose（并按需 Tick 加载器）直到没有
// 待切换，之后的断言与改动前完全相同。
using System;
using System.Collections;
using Adapter.Unity.Presentation;
using Core.Foundation.Common;
using Presentation.Common;
using UnityEngine;

namespace Adapter.Unity.Tests.Runtime
{
    internal static class DirectionSwitchSettle
    {
        /// <summary>每帧 SyncPose（+ 可选 Tick 加载器）直到该视图没有待切换（有界，超时则返回，由调用方断言失败）。</summary>
        public static IEnumerator SyncUntilCommitted(
            UnitySpriteView view, Direction facing, Action? tick = null, int maxFrames = 300)
        {
            for (var i = 0; i < maxFrames; i++)
            {
                view.SyncPose(Vec2.Zero, facing, height: 0.0);
                if (!view.HasPendingDirectionSwitch)
                {
                    yield break;
                }
                tick?.Invoke();
                yield return null;
            }
        }
    }
}
