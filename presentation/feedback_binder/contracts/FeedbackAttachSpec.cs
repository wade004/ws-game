using Core.Foundation.Common;

namespace Presentation.FeedbackBinder.Contracts
{
    /// <summary>
    /// <see cref="IFeedbackSink.PlayVfx"/> 的挂接参数：<see cref="PlayVfxAction"/> 里"以事件哪一方
    /// 实体为挂接基准"的抽象概念（<see cref="FeedbackAttachTarget"/>）已经被
    /// <c>FeedbackBinder</c> 解析成具体的实体 id/世界坐标，本类型是这一步解析结果的载体，供
    /// <see cref="IFeedbackSink"/> 实现（如 <see cref="Presentation.FeedbackBinder.Core.CompositeFeedbackSink"/>）
    /// 再换算成 <c>Presentation.VfxSfx.Contracts.VfxAttach</c>（L-1 引擎侧挂接形状）。
    /// <para>
    /// 判断记录（命名）：09 第 6.1 节伪代码把这个概念写作 <c>AttachSpec</c>，但本模块已有
    /// <c>Presentation.VfxSfx.Contracts.VfxAttach</c>（world/anchor/socket/screen，L-1 引擎侧形状）
    /// 表达"挂接"，二者语义不同（一个是"挂到哪个实体"，一个是"具体怎么挂"），沿用同名容易让读者
    /// 误以为是同一个类型，因此改名为 <see cref="FeedbackAttachSpec"/>，在 README 里记录这处与
    /// 文档伪代码字面命名的偏差。
    /// </para>
    /// </summary>
    public readonly struct FeedbackAttachSpec
    {
        public FeedbackAttachTarget Target { get; }

        /// <summary><see cref="FeedbackAttachTarget.Source"/>/<see cref="FeedbackAttachTarget.Target"/>
        /// 时的具体实体 id；<see cref="FeedbackAttachTarget.World"/> 时为 null。</summary>
        public Id? EntityId { get; }

        /// <summary><c>play_vfx</c> 规则里声明的 <c>anchor_id</c>；可选。</summary>
        public Id? AnchorId { get; }

        /// <summary><see cref="FeedbackAttachTarget.World"/> 时使用的世界坐标；其余目标为 null
        /// （由 <see cref="Presentation.FeedbackBinder.Core.CompositeFeedbackSink"/> 决定 null 时的
        /// 兜底行为，见该类型判断记录）。</summary>
        public Vec2? WorldPosition { get; }

        public FeedbackAttachSpec(FeedbackAttachTarget target, Id? entityId, Id? anchorId, Vec2? worldPosition)
        {
            Target = target;
            EntityId = entityId;
            AnchorId = anchorId;
            WorldPosition = worldPosition;
        }

        public static FeedbackAttachSpec ForEntity(FeedbackAttachTarget target, Id entityId, Id? anchorId) =>
            new FeedbackAttachSpec(target, entityId, anchorId, null);

        public static FeedbackAttachSpec ForWorld(Vec2 worldPosition) =>
            new FeedbackAttachSpec(FeedbackAttachTarget.World, null, null, worldPosition);
    }
}
