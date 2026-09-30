#nullable enable
// DirectionSwitchAtomicReproTests：ADR-0112（消费方反馈第六十三批 A 条"方向切换原子化"）复现用例——生产装配级
// （UnityViewFactory + 真实 EventBus + 真实 UnityRenderer2D/UnityResourceLoader，纸娃娃层 = 身体 + 一件装备层，
// 两个方向的逐层剪辑与静态层图，资源全部冷加载）。
//
// 复现：单位站着朝 side_r（idle 逐层剪辑在播、静态层图已加载），朝向变到 front 而 front 方向的全部资源尚未加载。
// 每帧读全部可见渲染器（身体层、装备层、整身兜底渲染器）此刻应用的资源与它的方向：
//   - 期望：任何一帧都不许同屏出现两个方向；任何一帧都不许出现占位方块；切换在单帧内完成（第一个出现 front 内容
//     的帧上全部可见渲染器都已经是 front）。
//   - 修复前（1.90.1）：视图在期望方向变化的当帧就按新朝向重合成静态层（front 静态图冷加载 -> 占位方块），而逐层剪辑
//     内容要等 front 方向的探测异步完成后才被换成新方向，中间若干帧同屏"层是新方向占位/新方向静态图 + 剪辑是旧方向"，
//     本用例的逐帧断言红，失败消息带完整逐帧读数。
// 期望的资源 id 一律按命名规则拼出（LayerResourceId / StaticLayerId），不写死。
using System.Collections;
using System.Collections.Generic;
using Core.Foundation.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class DirectionSwitchAtomicReproTests : DirectionSwitchFixtureBase
    {
        [UnityTest]
        public IEnumerator Repro_ColdTurn_AllVisibleLayersSwitchTogether_NoMixedFrame_NoPlaceholderFrame()
        {
            const string suffix = "a63r";
            const string clipName = "hero63r_idle";
            const string weaponMesh = "mesh.w63r";

            foreach (var dir in new[] { SideDir, FrontDir })
            {
                WriteLayerArt(suffix, null, clipName, dir, "body");
                WriteLayerArt(suffix, weaponMesh, clipName, dir, "mainhand");
            }

            // 起始方向 side_r 的资源全部热加载；front 的资源保持冷（夹具加载器不 Tick 就不会加载）。
            var sideBodyClip = LayerResourceId(null, clipName, SideDir, "body");
            var sideWeaponClip = LayerResourceId(weaponMesh, clipName, SideDir, "mainhand");
            var frontBodyClip = LayerResourceId(null, clipName, FrontDir, "body");
            var frontWeaponClip = LayerResourceId(weaponMesh, clipName, FrontDir, "mainhand");
            yield return WarmEffectCache(sideBodyClip);
            yield return WarmEffectCache(sideWeaponClip);
            yield return WarmImage(StaticLayerId(suffix, null, SideDir, "body"));
            yield return WarmImage(StaticLayerId(suffix, weaponMesh, SideDir, "mainhand"));

            var fx = BuildFixture(suffix, new Dictionary<string, string> { ["idle"] = ResourceRefOf(clipName) });
            EquipMesh(fx, "a63r_w", "slot.mainhand", weaponMesh);
            StandIdle(fx);

            var log = new List<FrameReading>();
            yield return RunFrames(fx, InitialFacing, 6, log);

            // 前置条件：起始方向下两层（身体 + 装备）都在播 side_r 的剪辑，front 资源确实还是冷的。
            var before = log[log.Count - 1];
            Assert.GreaterOrEqual(before.Readings.Count, 2, "前置条件：身体层与装备层都应可见\n" + Dump(log));
            foreach (var r in before.Readings)
            {
                Assert.AreEqual(SideDir, r.Dir, $"前置条件：起始方向 {r}\n" + Dump(log));
                Assert.IsFalse(r.Placeholder, "前置条件：起始画面不应有占位方块\n" + Dump(log));
            }
            Assert.IsFalse(Loader.TryGetEffect(frontBodyClip, out _), "前置条件：front 身体剪辑必须是冷的");
            Assert.IsFalse(Loader.TryGetEffect(frontWeaponClip, out _), "前置条件：front 装备剪辑必须是冷的");
            Assert.IsFalse(Loader.TryGetSprite(StaticLayerId(suffix, null, FrontDir, "body"), out _), "前置条件：front 静态层图必须是冷的");

            // 转向 front（每帧 SyncPose，同生产每帧路径；加载器前 holdFrames 帧不 Tick 模拟慢盘，之后每帧 Tick）。
            const int holdFrames = 4;
            log.Clear();
            yield return RunFrames(fx, Face(FrontDir), 120, log, tickThisFrame: i => i >= holdFrames);
            UnityEngine.Debug.Log("[dir63-trace] A repro per-frame readings:\n" + Dump(log, 0, 25));

            var firstAnyFront = FirstFrameAnyIn(log, FrontDir);
            var firstAllFront = FirstFrameAllIn(log, FrontDir, 2);
            Assert.GreaterOrEqual(firstAllFront, 0, "转向 front 后应当在有限帧内全部可见层切到 front\n" + Dump(log));

            // 1) 没有任何一帧同屏出现两个方向、没有占位方块。
            AssertNoMixedDirection(log, "冷转向");

            // 2) 切换在单帧内完成：第一个出现 front 内容的帧上全部可见渲染器已经都是 front。
            Assert.AreEqual(firstAllFront, firstAnyFront, "切换应在单帧内完成：第一个出现 front 内容的帧必须已是全部层\n" + Dump(log));

            // 3) 加载器在前 holdFrames 帧没有任何进展，前 holdFrames 帧全部保持起始方向；提交之前全部帧都保持起始方向（含身体/装备逐层剪辑）。
            Assert.GreaterOrEqual(firstAllFront, holdFrames, "front 资源没有加载完成之前不许提交\n" + Dump(log));
            for (var i = 0; i < firstAllFront; i++)
            {
                Assert.AreEqual(1, log[i].Dirs().Count, $"提交前第 {i} 帧应恰好显示一个方向\n" + Dump(log));
                Assert.IsTrue(log[i].Dirs().Contains(SideDir), $"提交前第 {i} 帧应保持 side_r\n" + Dump(log));
            }

            fx.EquipSource.Dispose();
            fx.View.Destroy();
        }
    }
}
