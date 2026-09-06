using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Foundation.DisplayInfo
{
    /// <summary>方向镜像规则的一条记录（见 04 第 7.1 节 <c>display.map.mirror_pairs</c> 元素结构，
    /// 与 09 第 3.2 节镜像规则表一致）。</summary>
    public readonly struct MirrorPair
    {
        /// <summary>被镜像出的方向槽位 id。</summary>
        public Id DirectionSlot { get; }

        /// <summary>镜像的来源方向槽位 id。</summary>
        public Id MirrorOf { get; }

        /// <summary>是否做水平翻转。</summary>
        public bool FlipX { get; }

        public MirrorPair(Id directionSlot, Id mirrorOf, bool flipX)
        {
            DirectionSlot = directionSlot;
            MirrorOf = mirrorOf;
            FlipX = flipX;
        }
    }

    /// <summary>
    /// <c>kind: sprite</c> 型外形的专属字段（见 04_数据与内容管线.md 第 7.1 节"<c>kind: sprite</c>
    /// 型专属字段"表）。不可变值对象，由 <see cref="Core.Foundation.DisplayInfo.DisplayInfo.FromRecord"/>
    /// 从 <c>display.map</c> 记录构造。
    /// </summary>
    public sealed class SpriteInfo
    {
        /// <summary>精灵集资源引用（不含路径，由引擎适配层解析），必填。</summary>
        public string SpriteSetId { get; }

        /// <summary>方向量化档位：4/8/16（见 05 第 3.2 节），必填。</summary>
        public int DirectionCount { get; }

        /// <summary>方向镜像规则，可选，默认空列表。</summary>
        public IReadOnlyList<MirrorPair> MirrorPairs { get; }

        /// <summary>纸娃娃分层引用列表，仅 item/creature 使用，可选，默认空列表。</summary>
        public IReadOnlyList<string> PaperdollLayers { get; }

        /// <summary>
        /// 挂点定义，供特效/武器/头顶信息等对齐，可选，默认空字典；键是锚点 id 的裸名字符串（同
        /// <see cref="AnchorDef.AnchorId"/> 的 <c>Id.Value</c>）。
        /// <para>
        /// GP-PRES-07 收口（09 第 3.3.1 节"锚点表"）：值类型此前是裸 <see cref="Vec2"/>（等价于
        /// 09 表格 <c>offset</c> 一个字段），<c>parent_layer</c>（必填）与
        /// <c>offset_by_direction</c>（可选）两个文档字段没有运行期模型落点——现在改为完整的
        /// <see cref="AnchorDef"/>。
        /// </para>
        /// </summary>
        public IReadOnlyDictionary<string, AnchorDef> AnchorPoints { get; }

        public SpriteInfo(
            string spriteSetId,
            int directionCount,
            IReadOnlyList<MirrorPair>? mirrorPairs = null,
            IReadOnlyList<string>? paperdollLayers = null,
            IReadOnlyDictionary<string, AnchorDef>? anchorPoints = null)
        {
            SpriteSetId = spriteSetId;
            DirectionCount = directionCount;
            MirrorPairs = mirrorPairs ?? System.Array.Empty<MirrorPair>();
            PaperdollLayers = paperdollLayers ?? System.Array.Empty<string>();
            AnchorPoints = anchorPoints ?? EmptyAnchorPoints;
        }

        private static readonly IReadOnlyDictionary<string, AnchorDef> EmptyAnchorPoints =
            new Dictionary<string, AnchorDef>();
    }
}
