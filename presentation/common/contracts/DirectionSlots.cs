using System;
using System.Collections.Generic;
using Core.Foundation.Common;

namespace Presentation.Common
{
    /// <summary>
    /// 方向档位命名与镜像回退的唯一来源（见
    /// [14_资产规格书模板.md](../../../architecture/14_资产规格书模板.md) 第 2.1 节"框架固定项"：8
    /// 方向档位族 <c>front</c>/<c>front_side</c>/<c>side</c>/<c>back_side</c>/<c>back</c>，每族除
    /// <c>front</c>/<c>back</c> 外各拆出原创绘制的 <c>_r</c> 档位与镜像得到的 <c>_l</c> 档位；4 方向
    /// 只保留 <c>front</c>/<c>side_r</c>/<c>side_l</c>/<c>back</c>）。本类型取代
    /// <c>presentation/render</c> 早前自造的罗盘命名（<c>"dir."</c> 前缀 + 罗盘缩写
    /// e/ne/n/nw/w/sw/s/se，P4-1 判断记录，
    /// 见 <c>RenderConventionHost</c> 类型注释），把"量化索引 → 档位 id""档位 id → 默认镜像来源"两条
    /// 规则集中在这里，供 <c>presentation/render</c>、后续 Unity 侧引擎适配层与 <c>display.map</c>
    /// 数据解析共用同一套命名，不各自发明。
    /// <para>
    /// <b>Id 前缀已拍板结论</b>（2026-09-05 勘误，见
    /// [14_资产规格书模板.md](../../../architecture/14_资产规格书模板.md) 第 2.1 节末尾说明）：运行期
    /// 方向档位 <see cref="Core.Foundation.Common.Id"/> 固定为 <c>"dir.&lt;裸档位名&gt;"</c> 形式（如
    /// <c>"dir.front_side_r"</c>），满足 00 第 4.1 节的标识格式要求（<c>Id</c> 要求"至少一个点分段"，
    /// 裸名字本身不合法）；文件名（见 14 第 1.2 节命名模板）、
    /// <c>toolchain/asset_import/directions.py</c> 与
    /// <c>assets/_placeholder/sprites/*/anchors.json</c> 标注文件一律使用不带前缀的裸档位名，两者一一
    /// 对应，前缀只在裸名字流入 <c>Id</c> 类型字段（如 <c>display.map.mirror_pairs</c> 的
    /// <c>direction_slot</c>/<c>mirror_of</c>）时补上。<see cref="IdPrefix"/> 承载这一拼接约定，
    /// <see cref="StripPrefix"/> 承载反向还原。
    /// </para>
    /// </summary>
    public static class DirectionSlots
    {
        /// <summary>把 14 第 2.1 节的裸档位名包装成合法 <see cref="Id"/> 时使用的前缀（见类型注释
        /// "Id 前缀判断记录"）。</summary>
        public const string IdPrefix = "dir.";

        public static readonly Id Front = new Id(IdPrefix + "front");
        public static readonly Id FrontSideR = new Id(IdPrefix + "front_side_r");
        public static readonly Id FrontSideL = new Id(IdPrefix + "front_side_l");
        public static readonly Id SideR = new Id(IdPrefix + "side_r");
        public static readonly Id SideL = new Id(IdPrefix + "side_l");
        public static readonly Id BackSideR = new Id(IdPrefix + "back_side_r");
        public static readonly Id BackSideL = new Id(IdPrefix + "back_side_l");
        public static readonly Id Back = new Id(IdPrefix + "back");

        /// <summary>
        /// 每种方向档位数下，从 <c>front</c> 到 <c>back</c>（含两端）沿"原创绘制"一侧走一圈的档位裸
        /// 名字序列，长度固定为 <c>directionCount / 2 + 1</c>（见 14 第 2.1 节"canonical 档位…数量
        /// 固定为 direction_count // 2 + 1"）。16 方向的 <c>_a</c>/<c>_b</c> 过渡档位命名与
        /// <c>toolchain/asset_import/directions.py</c> 的 <c>_CANONICAL_NAMES[16]</c> 逐字保持一致
        /// （14 原文只给出"延伸规则"，16 方向具体命名是工具链的自行约定扩展，见该文件顶部注释，本类型
        /// 直接复用避免两套 16 方向命名互相打架）。
        /// </summary>
        private static readonly IReadOnlyDictionary<int, string[]> CanonicalBareNames = new Dictionary<int, string[]>
        {
            [4] = new[] { "front", "side_r", "back" },
            [8] = new[] { "front", "front_side_r", "side_r", "back_side_r", "back" },
            [16] = new[]
            {
                "front",
                "front_side_r_a",
                "front_side_r",
                "front_side_r_b",
                "side_r",
                "back_side_r_b",
                "back_side_r",
                "back_side_r_a",
                "back",
            },
        };

        /// <summary>
        /// 量化方向索引 → 档位 <see cref="Id"/> 的对应表（判断记录，见模块 README"索引→档位对应表"
        /// 一节完整推导）。
        /// <para>
        /// 依据：05 第 3.1 节"sortY 越大越靠前，即 +y 朝向观察者"→ <c>front</c>（面朝观察者）对应
        /// <c>facing</c> 角度 90°（+Y 轴）；<c>Core.Carriers.Unit.DirectionQuantizer</c> 的既有约定
        /// "index 0 = 角度 0（+X 轴），按角度递增方向（逆时针）编号"不变。故 <c>front</c> 的量化索引
        /// 固定为 <c>directionCount / 4</c>（90° / (360° / directionCount)），<c>back</c>（角度 270°、
        /// -Y 轴，背对观察者）固定为 <c>directionCount * 3 / 4</c>。从 <c>front</c>
        /// 沿索引递增方向数 <c>directionCount / 2</c> 步到达 <c>back</c>，这一段依次对应
        /// <see cref="CanonicalBareNames"/> 的原创绘制（<c>_r</c>）档位；继续递增回到 <c>front</c> 的
        /// 另外半圈依次对应镜像（<c>_l</c>）档位，顺序与"原创绘制"半圈相反（越接近 <c>back</c> 的先
        /// 出现），因为两段本就是同一物理方向沿相反角度方向绕行。
        /// </para>
        /// <para>
        /// 8 方向完整对照表（index → 档位，<c>e/ne/n/.../se</c> 为对应的量化角度罗盘方位，仅供人工
        /// 核对）：0=side_l(e) 1=front_side_l(ne) 2=front(n) 3=front_side_r(nw) 4=side_r(w)
        /// 5=back_side_r(sw) 6=back(s) 7=back_side_l(se)。
        /// </para>
        /// </summary>
        public static Id FromQuantized(int index, int directionCount)
        {
            var canonical = RequireCanonicalBareNames(directionCount);
            var half = directionCount / 2;
            var front = directionCount / 4;

            var steps = ((index - front) % directionCount + directionCount) % directionCount;
            var bareName = steps <= half
                ? canonical[steps]
                : MirrorBareName(canonical[directionCount - steps]);

            return new Id(IdPrefix + bareName);
        }

        /// <summary>
        /// 给定档位 <see cref="Id"/>，若它是一个"镜像"档位（裸名字含独立的 <c>_l</c> 分段，即 14 第
        /// 2.1 节命名表右列"否（镜像）"的那些行），返回它的默认镜像来源（<c>_r</c> 变体，
        /// <c>FlipX</c> 恒为 <c>true</c>）；<c>front</c>/<c>back</c>/任意 <c>_r</c> 档位本身没有默认
        /// 镜像来源（它们是原创绘制的一侧），返回 null。
        /// <para>
        /// 用途（见任务书"ResolveDirectionSlot 先看 display.map.mirror_pairs，没有登记时按 14 的
        /// 默认镜像表回退"）：内容侧的 <c>display.map.mirror_pairs</c> 未登记某个 <c>_l</c> 档位时，
        /// 渲染约定按本方法给出的默认镜像表回退，不必每个游戏都重复登记 14 已经固定死的
        /// <c>front_side_l</c>/<c>side_l</c>/<c>back_side_l</c> 镜像自
        /// <c>front_side_r</c>/<c>side_r</c>/<c>back_side_r</c> 这三条规则（16 方向同理扩展到
        /// <c>_a</c>/<c>_b</c> 过渡档位）。
        /// </para>
        /// </summary>
        public static (Id MirrorOf, bool FlipX)? MirrorSourceOf(Id slot)
        {
            var bareName = StripPrefix(slot);
            var lSegmentIndex = FindLastSegmentIndex(bareName, "l");
            if (lSegmentIndex < 0)
            {
                return null;
            }

            return (new Id(IdPrefix + ReplaceSegment(bareName, lSegmentIndex, "r")), true);
        }

        /// <summary>去掉 <see cref="IdPrefix"/>，还原成 14 第 2.1 节的裸档位名（供文件名/引擎侧资源
        /// id 拼接一类需要裸名字的场景使用，见 <c>presentation/render</c>
        /// <c>SpriteViewBase.ResolveLayerResourceId</c>）。<paramref name="slot"/> 不以
        /// <see cref="IdPrefix"/> 开头时原样返回其 <see cref="Id.Value"/>（容错，不强制要求调用方
        /// 一定经本类型产出该 Id）。</summary>
        public static string StripPrefix(Id slot)
        {
            var value = slot.Value;
            return value.StartsWith(IdPrefix, StringComparison.Ordinal) ? value.Substring(IdPrefix.Length) : value;
        }

        private static string MirrorBareName(string canonicalBareName)
        {
            var rIndex = FindLastSegmentIndex(canonicalBareName, "r");
            if (rIndex < 0)
            {
                throw new ArgumentException(
                    $"内部错误：档位名 \"{canonicalBareName}\" 不含可镜像的独立 \"_r\" 分段", nameof(canonicalBareName));
            }
            return ReplaceSegment(canonicalBareName, rIndex, "l");
        }

        /// <summary>在按 <c>_</c> 切分的分段中，找最后一个恰好等于 <paramref name="segment"/> 的分段
        /// 下标（<c>front_side_r</c> 的 <c>"r"</c>、<c>front_side_r_a</c> 的 <c>"a"</c> 前一段
        /// <c>"r"</c> 不算——只找独立分段，同 <c>toolchain/asset_import/directions.py</c>
        /// <c>mirror_slot_name</c> 的"最后一个独立的 r 分段"规则），找不到返回 -1。</summary>
        private static int FindLastSegmentIndex(string bareName, string segment)
        {
            var parts = bareName.Split('_');
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                if (parts[i] == segment)
                {
                    return i;
                }
            }
            return -1;
        }

        private static string ReplaceSegment(string bareName, int segmentIndex, string replacement)
        {
            var parts = bareName.Split('_');
            parts[segmentIndex] = replacement;
            return string.Join("_", parts);
        }

        private static string[] RequireCanonicalBareNames(int directionCount)
        {
            if (!CanonicalBareNames.TryGetValue(directionCount, out var names))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(directionCount), directionCount, "direction_count 仅支持 4/8/16 三档（见 05 第 3.2 节）");
            }
            return names;
        }
    }
}
