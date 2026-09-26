using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Presentation.Common;
using Presentation.VfxSfx.Contracts;

namespace Presentation.Render
{
    /// <summary>
    /// <see cref="IRenderConventionHost"/> 的默认实现（见接口注释、09 第 3.1～3.4 节）。除构造期注入的
    /// 只读 <see cref="RenderOptions"/>（缺口 8：<see cref="RenderOptions.DirectionIndexRemap"/>）与
    /// 可选诊断出口（ADR-0094 决策 3）外无其它可变内部状态，全部公开方法都是纯函数（诊断去重集合不
    /// 影响任何返回值），可安全作为单例复用。
    /// </summary>
    public sealed class RenderConventionHost : IRenderConventionHost
    {
        private readonly RenderOptions _options;
        private readonly IPresentationDiagnostics? _diagnostics;

        /// <summary>ADR-0094 决策 3：<see cref="ResolveDirectionSlot"/> 已经按 (方向档位数,
        /// <see cref="RenderOptions.DirectionIndexRemap"/> 表长度) 记过一次"长度不匹配、已按恒等映射
        /// 处理"诊断的组合去重集合——同一组合只记一次，避免同一实体每帧 SyncPose 都重复刷屏。</summary>
        private readonly HashSet<(int DirectionCount, int RemapLength)> _warnedRemapLengthMismatches =
            new HashSet<(int, int)>();

        public RenderConventionHost(RenderOptions? options = null) : this(options, null)
        {
        }

        /// <summary>ADR-0094 决策 3 新增重载（ABI 加法，不改既有单参构造函数签名，见 AGENTS.md 第 3
        /// 节"可选参数也算加参数"——不是给既有构造函数加参数，而是新增一个重载）：
        /// <paramref name="diagnostics"/> 可选，未提供（<c>null</c>，既有单参构造函数/无参构造的既有
        /// 调用点全部落在这里）时 <see cref="ResolveDirectionSlot"/> 遇到 <see cref="RenderOptions.DirectionIndexRemap"/>
        /// 长度不匹配仍然静默按恒等映射处理、不记诊断——与改动前逐字节一致；提供时才记一条一次性 Warn
        /// 诊断（见 <see cref="_warnedRemapLengthMismatches"/> 判断记录）。</summary>
        public RenderConventionHost(RenderOptions? options, IPresentationDiagnostics? diagnostics)
        {
            _options = options ?? new RenderOptions();
            _diagnostics = diagnostics;
        }

        public double ComputeSortY(Vec2 logicalPos, double sortOffset) => logicalPos.Y + sortOffset;

        public IComparer<(Id Id, double SortY)> TieBreakComparer { get; } = new SortYThenIdComparer();

        /// <summary>
        /// 判断记录（P4-2 恢复，见模块 README"索引→档位对应表"一节、
        /// <see cref="Presentation.Common.DirectionSlots"/> 类型注释）：量化索引先经
        /// <see cref="DirectionSlots.FromQuantized"/> 换算成 14 第 2.1 节命名的规范档位 id
        /// （<c>front</c>/<c>front_side_r</c>/…）；先查 <c>display.map.mirror_pairs</c>（内容侧显式
        /// 登记优先，见 09 第 3.2 节镜像规则表），没有登记时再按
        /// <see cref="DirectionSlots.MirrorSourceOf"/> 给出的 14 默认镜像表回退（<c>_l</c> 档位镜像
        /// 自同族 <c>_r</c> 档位）；两处都没有命中（<c>front</c>/<c>back</c>/<c>_r</c> 档位本身）时
        /// 直接使用规范档位、不翻转。
        /// <para>
        /// 缺口 8：换算规范档位之前先经 <see cref="RenderOptions.DirectionIndexRemap"/> 重映射一次量化
        /// 索引（长度须等于 <paramref name="direction"/>.<see cref="Direction.DirectionCount"/>，否则
        /// 忽略、按恒等映射处理，见该字段注释）——供镜头朝向与 05 第 3.1 节默认约定不一致的具体游戏在
        /// 接入阶段配置，不改 <see cref="DirectionSlots"/>/<c>DirectionQuantizer</c> 这两处固定原语。
        /// </para>
        /// </summary>
        public (Id SlotId, bool FlipX) ResolveDirectionSlot(Direction direction, SpriteInfo spriteInfo)
        {
            if (spriteInfo == null)
            {
                throw new System.ArgumentNullException(nameof(spriteInfo));
            }

            var remap = _options.DirectionIndexRemap;
            int index;
            if (remap == null)
            {
                index = direction.Index;
            }
            else if (remap.Count == direction.DirectionCount)
            {
                index = remap[direction.Index];
            }
            else
            {
                // ADR-0094 决策 3：长度不匹配不再纯静默——按 (方向档位数, 表长度) 去重记一条 Warn，
                // 行为仍然退恒等（不抛异常，见字段/构造函数判断记录），未注入诊断出口时不记（与改动前
                // 逐字节一致）。
                index = direction.Index;
                var key = (direction.DirectionCount, remap.Count);
                if (_diagnostics != null && _warnedRemapLengthMismatches.Add(key))
                {
                    _diagnostics.Warn(
                        $"RenderOptions.DirectionIndexRemap 长度（{remap.Count}）与当前方向档位数" +
                        $"（{direction.DirectionCount}）不一致，本次及此后同一组合按恒等映射处理" +
                        "（见 RenderOptions.DirectionIndexRemap 字段注释）");
                }
            }
            var canonical = DirectionSlots.FromQuantized(index, direction.DirectionCount);

            for (var i = 0; i < spriteInfo.MirrorPairs.Count; i++)
            {
                var pair = spriteInfo.MirrorPairs[i];
                if (pair.DirectionSlot.Equals(canonical))
                {
                    return (pair.MirrorOf, pair.FlipX);
                }
            }

            var defaultMirror = DirectionSlots.MirrorSourceOf(canonical);
            if (defaultMirror.HasValue)
            {
                return (defaultMirror.Value.MirrorOf, defaultMirror.Value.FlipX);
            }

            return (canonical, false);
        }

        /// <summary>ADR-0094 决策 1：先按 <see cref="RenderOptions.MirrorFacingY"/> 镜像（角度取负），
        /// 再加 <see cref="RenderOptions.FacingAngleOffsetRadians"/>——两个口味项均默认值（<c>false</c>/
        /// <c>0</c>）时原样返回 <paramref name="rawRadians"/>，与改动前逐字节一致。</summary>
        public double ApplyFacingConvention(double rawRadians)
        {
            var mirrored = _options.MirrorFacingY ? -rawRadians : rawRadians;
            return mirrored + _options.FacingAngleOffsetRadians;
        }

        public IReadOnlyList<SpriteLayerPlacement> ComposeSpriteLayers(
            IReadOnlyList<string> layerNamesInOrder, SpriteInfo spriteInfo, Direction direction)
        {
            if (layerNamesInOrder == null)
            {
                throw new System.ArgumentNullException(nameof(layerNamesInOrder));
            }

            var (slotId, flipX) = ResolveDirectionSlot(direction, spriteInfo);

            var result = new List<SpriteLayerPlacement>(layerNamesInOrder.Count);
            for (var i = 0; i < layerNamesInOrder.Count; i++)
            {
                result.Add(new SpriteLayerPlacement(layerNamesInOrder[i], slotId, flipX));
            }

            return result;
        }

        public double HeightOffsetToPixels(double height, double pixelsPerUnit) => height * pixelsPerUnit;

        private sealed class SortYThenIdComparer : IComparer<(Id Id, double SortY)>
        {
            public int Compare((Id Id, double SortY) x, (Id Id, double SortY) y)
            {
                var cmp = x.SortY.CompareTo(y.SortY);
                return cmp != 0 ? cmp : x.Id.CompareTo(y.Id);
            }
        }
    }
}
