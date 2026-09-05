using System.Collections.Generic;
using Core.Foundation.Common;

namespace Core.Gameplay.Dialog
{
    /// <summary>
    /// <see cref="IDialogHost.OpenGossip"/> 的返回值：经 <c>visible_if</c> 过滤后的可见选项列表
    /// （见 08 第 3.1 节）。<see cref="Options"/> 每项的 <c>Index</c> 是该选项在
    /// <c>GossipMenuDefinition.Options</c> 中的原始下标，供 <see cref="IDialogHost.ChooseOption"/>
    /// 使用（不是"可见列表中的第几个"，避免过滤后下标错位导致选错项）。
    /// </summary>
    public sealed class GossipView
    {
        public Id MenuId { get; }

        public IReadOnlyList<(int Index, Id TextKey)> Options { get; }

        public GossipView(Id menuId, IReadOnlyList<(int Index, Id TextKey)> options)
        {
            MenuId = menuId;
            Options = options;
        }
    }

    /// <summary><see cref="IDialogHost.GetStoryView"/> 的返回值：当前节点文本键、说话者、经
    /// <c>condition</c> 过滤后的可见分支列表（见 08 第 3.2 节）。<see cref="VisibleBranches"/>
    /// 同 <see cref="GossipView.Options"/> 判断记录，<c>Index</c> 是原始分支下标。</summary>
    public sealed class StoryView
    {
        public Id TreeId { get; }

        public Id NodeId { get; }

        public Id TextKey { get; }

        public Id? SpeakerRef { get; }

        public IReadOnlyList<(int Index, Id TextKey)> VisibleBranches { get; }

        public StoryView(Id treeId, Id nodeId, Id textKey, Id? speakerRef, IReadOnlyList<(int Index, Id TextKey)> visibleBranches)
        {
            TreeId = treeId;
            NodeId = nodeId;
            TextKey = textKey;
            SpeakerRef = speakerRef;
            VisibleBranches = visibleBranches;
        }
    }

    /// <summary>
    /// 对话系统对外契约（见 08 第 9 节汇总表 Dialog 行 <c>DialogHost.openGossip/advanceStory(...)</c>，
    /// 本接口按任务书拍板展开为完整方法集）。由 <see cref="DialogHost"/> 实现。
    /// </summary>
    public interface IDialogHost
    {
        /// <summary>打开某 NPC 的 gossip 菜单（见 08 第 3.1 节）。按 <c>visible_if</c> 过滤选项、
        /// 推入 Dialog 子状态、发 <c>dialog.gossip_opened</c>。<paramref name="menuId"/> 未登记时
        /// 抛 <see cref="System.ArgumentException"/>。</summary>
        GossipView OpenGossip(Id unitId, Id npcId, Id menuId);

        /// <summary>当前 gossip 会话视图（契约缺口补齐，对称于 <see cref="GetStoryView"/>，见
        /// <c>presentation/ui/README.md</c>"已知契约缺口"一节）：<paramref name="unitId"/> 当前不在
        /// 一个已打开的 gossip 会话中（未打开过，或已转入剧情/已关闭）时返回 null；否则按当前会话的
        /// <c>menuId</c> 重新求值 <c>visible_if</c> 得到可见选项列表，不重复推子状态、不重复发
        /// <c>dialog.gossip_opened</c>（那是 <see cref="OpenGossip"/> 的职责，本方法只读）。</summary>
        GossipView? GetGossipView(Id unitId);

        /// <summary>选择当前已打开菜单的第 <paramref name="index"/> 项（<see cref="GossipView.Options"/>
        /// 中的原始下标），依次执行该项全部动作。要求 <paramref name="unitId"/> 当前处于一个已打开的
        /// gossip 会话，否则返回 false。</summary>
        bool ChooseOption(Id unitId, int index);

        /// <summary>结束当前对话会话（gossip 或剧情），Pop 子状态，发 <c>dialog.ended</c>。
        /// <paramref name="unitId"/> 当前没有打开的对话会话时返回 false。</summary>
        bool Close(Id unitId);

        /// <summary>打开指定剧情树，进入首节点（<c>nodes[0]</c>）。若该单位尚未处于任何对话会话，
        /// 顺带推入 Dialog 子状态（同 <see cref="OpenGossip"/>）；若已处于会话中（例如由某个 gossip
        /// 动作触发），复用既有会话，不重复 Push。<paramref name="treeId"/> 未登记时抛
        /// <see cref="System.ArgumentException"/>。</summary>
        bool StartStory(Id unitId, Id treeId);

        /// <summary>当前剧情节点视图；<paramref name="unitId"/> 当前不在剧情会话中时返回 null。</summary>
        StoryView? GetStoryView(Id unitId);

        /// <summary>选择第 <paramref name="branchIndex"/> 条分支（<see cref="StoryView.VisibleBranches"/>
        /// 的原始下标）推进剧情：<c>next_node_id</c> 为空则结束对话（等价调用 <see cref="Close"/>）；
        /// 否则进入下一节点并发 <c>dialog.story_node_entered</c>。<paramref name="unitId"/> 当前不在
        /// 剧情会话中，或 <paramref name="branchIndex"/> 越界时返回 false。</summary>
        bool AdvanceStory(Id unitId, int branchIndex);
    }
}
