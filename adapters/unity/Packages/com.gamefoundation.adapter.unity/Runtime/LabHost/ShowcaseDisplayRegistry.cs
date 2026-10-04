#nullable enable
// ShowcaseDisplayRegistry：演示场景（ADR-0154）的外形登记。
//
// 判断记录（沿用合成外形登记的做法）：实验室数据里单位的外形行指向没有美术的占位精灵集，舞台一直给引擎视图工厂一个合成登记。
// 演示场景把"一律标准假人"换成"按单位种类给真实美术外形"：玩家 → 英雄，脆皮怪/小怪/巡逻靶/群怪 → 哥布林，精英 → 兽人，
// 木桩/高韧桩/可破坏障碍 → 训练木桩/木箱外形。外形的精灵集与动画集随 assets/_showcase 与 data/_showcase；内核自己的外形登记（记录视图用）不变，
// 数据集哈希、逻辑指纹与既有基线不受影响。方向：只画了正面/侧面（朝右）/背面三张，斜向档位按镜像表取侧向图（左侧水平翻转）。
using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using DisplayInfo = Core.Foundation.DisplayInfo.DisplayInfo;

namespace Adapter.Unity.LabHost
{
    public sealed class ShowcaseDisplayRegistry : IDisplayInfoRegistry
    {
        public const string Hero = "hero";
        public const string Grunt = "grunt";
        public const string Brute = "brute";
        public const string Dummy = "dummy";

        private readonly IDisplayInfoRegistry _inner;
        private readonly HashSet<string> _players = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _seen = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <param name="inner">内核的外形登记。</param>
        public ShowcaseDisplayRegistry(IDisplayInfoRegistry inner)
        {
            _inner = inner;
        }

        /// <summary>登记玩家单位的外形逻辑 id（舞台在为玩家实体建视图前调用）：此后该逻辑 id 一律按英雄外形。</summary>
        public void MarkPlayer(Id logicalId) => _players.Add(logicalId.Value);

        /// <summary>每个逻辑 id 最近一次被解析成的外形名（测试与诊断用）。</summary>
        public IReadOnlyDictionary<string, string> Resolved => _seen;

        /// <summary>按逻辑 id 的种类名取外形；规则只看 id 里的种类词，找不到的未知生物取哥布林。</summary>
        public static string LookOf(string logicalId)
        {
            var id = logicalId.ToLowerInvariant();
            if (id.Contains("hero") || id.Contains("player"))
            {
                return Hero;
            }

            if (id.Contains("elite") || id.Contains("brute"))
            {
                return Brute;
            }

            if (id.Contains("stake") || id.Contains("resilient") || id.Contains("breakable") || id.Contains("dummy"))
            {
                return Dummy;
            }

            return Grunt;
        }

        public DisplayInfo? Lookup(Id logicalId)
        {
            var found = _inner.Lookup(logicalId);
            if (found != null && found.Category != DisplayCategory.Creature)
            {
                return found;
            }

            var look = _players.Contains(logicalId.Value) ? Hero : LookOf(logicalId.Value);
            _seen[logicalId.Value] = look;
            return Build(logicalId, look);
        }

        private static DisplayInfo Build(Id logicalId, string look)
        {
            MirrorPair[] mirrors;
            if (string.Equals(look, Dummy, StringComparison.Ordinal))
            {
                // 木桩只画一张图：所有方向都指到正面（不翻转）。
                mirrors = new[]
                {
                    new MirrorPair(new Id("dir.front_side_r"), new Id("dir.front"), false),
                    new MirrorPair(new Id("dir.side_r"), new Id("dir.front"), false),
                    new MirrorPair(new Id("dir.back_side_r"), new Id("dir.front"), false),
                    new MirrorPair(new Id("dir.back"), new Id("dir.front"), false),
                    new MirrorPair(new Id("dir.front_side_l"), new Id("dir.front"), false),
                    new MirrorPair(new Id("dir.side_l"), new Id("dir.front"), false),
                    new MirrorPair(new Id("dir.back_side_l"), new Id("dir.front"), false),
                };
            }
            else
            {
                // 三张图（正面/侧面朝右/背面）：右侧斜向取侧面图，左侧一律取侧面图并水平翻转。
                mirrors = new[]
                {
                    new MirrorPair(new Id("dir.front_side_r"), new Id("dir.side_r"), false),
                    new MirrorPair(new Id("dir.back_side_r"), new Id("dir.side_r"), false),
                    new MirrorPair(new Id("dir.front_side_l"), new Id("dir.side_r"), true),
                    new MirrorPair(new Id("dir.side_l"), new Id("dir.side_r"), true),
                    new MirrorPair(new Id("dir.back_side_l"), new Id("dir.side_r"), true),
                };
            }

            // 不声明纸娃娃层：整身动画剪辑（AnimRoot）是唯一画面。若声明 "body" 静态层，它会作为"站立姿态"一直垫在
            // 剪辑下面，倒地/受击等非站立姿态就会出现"一个站着、一个趴着"的双影（ADR-0154 决策 10）。
            return new DisplayInfo(
                new Id("display.map.show_" + look), DisplayCategory.Creature, logicalId, DisplayKind.Sprite, null, null, null, 1.0,
                Core.Foundation.DisplayInfo.ShadowMode.Blob, 0.0, null,
                new SpriteInfo("sprite.creature.show_" + look, 8, mirrors, System.Array.Empty<string>()), null);
        }

        public IReadOnlyList<DisplayInfo> LookupByCategory(DisplayCategory category) => _inner.LookupByCategory(category);

        public IReadOnlyList<DisplayInfo> All => _inner.All;

        public void Reload() => _inner.Reload();
    }
}
