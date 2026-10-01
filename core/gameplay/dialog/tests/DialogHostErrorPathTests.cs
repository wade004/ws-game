using System;
using Core.Foundation.AppLifecycle;
using Core.Foundation.Common;
using Core.Gameplay.Dialog;
using Xunit;

namespace Tests.Gameplay.Dialog
{
    /// <summary>
    /// T-M22（测试覆盖剩余项 2026-10-01）：<see cref="DialogHost"/> 的错误路径与边界——未登记的菜单/故事树抛
    /// <see cref="ArgumentException"/>（且失败不留下半开会话）、<c>ChooseOption</c>/<c>AdvanceStory</c> 越界索引与
    /// 无会话返回 false 且不产生副作用、会话已开时再开不重复压子状态、<c>Reload</c> 传空集合清空定义表但不中断
    /// 在途会话、<see cref="InMemoryDialogDiagnostics"/> 的记录行为。
    /// </summary>
    public class DialogHostErrorPathTests
    {
        private static readonly Id Player = TestSupport.Player;
        private static readonly Id Npc = TestSupport.Npc;

        private static GossipMenuDefinition Menu(string id, int optionCount = 1)
        {
            var options = new GossipOption[optionCount];
            for (var i = 0; i < optionCount; i++)
            {
                options[i] = new GossipOption(
                    new Id("l10n.opt_" + i), null,
                    new[] { new GossipActionDef(DialogActionKind.Save, null, null) });
            }
            return new GossipMenuDefinition(new Id(id), options);
        }

        private static StoryTreeDefinition Tree(string id, int branchCount = 1)
        {
            var branches = new StoryBranchDef[branchCount];
            for (var i = 0; i < branchCount; i++)
            {
                branches[i] = new StoryBranchDef(new Id("l10n.branch_" + i), null, null);
            }
            return new StoryTreeDefinition(new Id(id), new[]
            {
                new StoryNodeDefinition(new Id(id + ".node_1"), new Id("l10n.node1"), null, branches, null),
            });
        }

        // ------------------------------ 未登记的菜单 / 故事树 ------------------------------

        [Fact]
        public void OpenGossip_UnregisteredMenu_ThrowsArgumentException_NamingTheId_AndOpensNoSession()
        {
            var h = new Harness(new[] { Menu("dialog.known") }, Array.Empty<StoryTreeDefinition>());

            var ex = Assert.Throws<ArgumentException>(() => h.Host.OpenGossip(Player, Npc, new Id("dialog.unknown")));

            Assert.Contains("dialog.unknown", ex.Message);
            Assert.Null(h.Host.GetGossipView(Player));
            Assert.Empty(h.PublishedOf<GossipOpenedEvent>());
            Assert.False(h.Host.Close(Player));
            Assert.Equal(SubStateId.Explore, h.AppState.CurrentSubState!.Value);
        }

        [Fact]
        public void StartStory_UnregisteredTree_ThrowsArgumentException_NamingTheId_AndOpensNoSession()
        {
            var h = new Harness(Array.Empty<GossipMenuDefinition>(), new[] { Tree("dialog.known_tree") });

            var ex = Assert.Throws<ArgumentException>(() => h.Host.StartStory(Player, new Id("dialog.unknown_tree")));

            Assert.Contains("dialog.unknown_tree", ex.Message);
            Assert.Null(h.Host.GetStoryView(Player));
            Assert.Empty(h.PublishedOf<StoryNodeEnteredEvent>());
            Assert.False(h.Host.Close(Player));
            Assert.Equal(SubStateId.Explore, h.AppState.CurrentSubState!.Value);
        }

        // ------------------------------ 无会话 / 越界 ------------------------------

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(3)]
        public void ChooseOption_WithoutSession_ReturnsFalse_AndDoesNothing(int index)
        {
            var h = new Harness(new[] { Menu("dialog.m") }, Array.Empty<StoryTreeDefinition>());

            Assert.False(h.Host.ChooseOption(Player, index));
            Assert.False(h.SaveRequested);
            Assert.Empty(h.PublishedOf<GossipActionExecutedEvent>());
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        public void AdvanceStory_WithoutSession_ReturnsFalse(int index)
        {
            var h = new Harness(Array.Empty<GossipMenuDefinition>(), new[] { Tree("dialog.t") });

            Assert.False(h.Host.AdvanceStory(Player, index));
            Assert.Empty(h.PublishedOf<DialogEndedEvent>());
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(2)]
        [InlineData(int.MaxValue)]
        public void ChooseOption_OutOfRangeIndex_ReturnsFalse_KeepsSessionOpen_AndRunsNoAction(int index)
        {
            var menu = Menu("dialog.two", optionCount: 2);
            var h = new Harness(new[] { menu }, Array.Empty<StoryTreeDefinition>());
            h.Host.OpenGossip(Player, Npc, menu.Id);

            Assert.False(h.Host.ChooseOption(Player, index));

            Assert.False(h.SaveRequested);
            Assert.Empty(h.PublishedOf<GossipActionExecutedEvent>());
            Assert.NotNull(h.Host.GetGossipView(Player));
            Assert.Empty(h.PublishedOf<DialogEndedEvent>());
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(1)]
        [InlineData(int.MaxValue)]
        public void AdvanceStory_OutOfRangeIndex_ReturnsFalse_KeepsStoryNode(int index)
        {
            var tree = Tree("dialog.one_branch", branchCount: 1);
            var h = new Harness(Array.Empty<GossipMenuDefinition>(), new[] { tree });
            h.Host.StartStory(Player, tree.Id);
            var before = h.Host.GetStoryView(Player)!.NodeId;

            Assert.False(h.Host.AdvanceStory(Player, index));

            Assert.Equal(before, h.Host.GetStoryView(Player)!.NodeId);
            Assert.Empty(h.PublishedOf<DialogEndedEvent>());
        }

        [Fact]
        public void ChooseOption_WhenSessionIsStory_ReturnsFalse_AndAdvanceStory_WhenSessionIsGossip_ReturnsFalse()
        {
            var menu = Menu("dialog.m2");
            var tree = Tree("dialog.t2");
            var h = new Harness(new[] { menu }, new[] { tree });

            h.Host.StartStory(Player, tree.Id);
            Assert.False(h.Host.ChooseOption(Player, 0));

            h.Host.OpenGossip(Player, Npc, menu.Id);
            Assert.False(h.Host.AdvanceStory(Player, 0));
            Assert.False(h.SaveRequested);
        }

        // ------------------------------ 会话已开再开 ------------------------------

        [Fact]
        public void OpenGossip_WhileSessionAlreadyOpen_ReusesSession_PushesSubStateOnce_AndSwitchesMenu()
        {
            var first = Menu("dialog.first", optionCount: 1);
            var second = Menu("dialog.second", optionCount: 3);
            var h = new Harness(new[] { first, second }, Array.Empty<StoryTreeDefinition>());

            h.Host.OpenGossip(Player, Npc, first.Id);
            var view = h.Host.OpenGossip(Player, Npc, second.Id);

            Assert.Equal(second.Id, view.MenuId);
            Assert.Equal(3, view.Options.Count);
            Assert.Equal(second.Id, h.Host.GetGossipView(Player)!.MenuId);
            Assert.Equal(2, h.PublishedOf<GossipOpenedEvent>().Count);

            // 子状态只压了一层：关闭一次后即回到 Explore。
            Assert.True(h.Host.Close(Player));
            Assert.Equal(SubStateId.Explore, h.AppState.CurrentSubState!.Value);
            Assert.False(h.Host.Close(Player));
        }

        [Fact]
        public void OpenGossip_WhileInStory_ReplacesStorySession()
        {
            var menu = Menu("dialog.m3");
            var tree = Tree("dialog.t3");
            var h = new Harness(new[] { menu }, new[] { tree });
            h.Host.StartStory(Player, tree.Id);
            Assert.NotNull(h.Host.GetStoryView(Player));

            h.Host.OpenGossip(Player, Npc, menu.Id);

            Assert.Null(h.Host.GetStoryView(Player));
            Assert.NotNull(h.Host.GetGossipView(Player));
        }

        [Fact]
        public void Sessions_AreIndependentPerUnit()
        {
            var menu = Menu("dialog.m4");
            var h = new Harness(new[] { menu }, Array.Empty<StoryTreeDefinition>());
            var other = new Id("unit.other_player");
            h.Host.OpenGossip(Player, Npc, menu.Id);

            Assert.Null(h.Host.GetGossipView(other));
            Assert.False(h.Host.ChooseOption(other, 0));
            Assert.False(h.Host.Close(other));
            Assert.NotNull(h.Host.GetGossipView(Player));
        }

        // ------------------------------ Reload ------------------------------

        [Fact]
        public void Reload_WithEmptyCollections_ClearsDefinitions_SoPreviouslyKnownIdsNowThrow()
        {
            var menu = Menu("dialog.cleared_menu");
            var tree = Tree("dialog.cleared_tree");
            var h = new Harness(new[] { menu }, new[] { tree });
            h.Host.OpenGossip(Player, Npc, menu.Id);
            h.Host.Close(Player);

            h.Host.Reload(Array.Empty<GossipMenuDefinition>(), Array.Empty<StoryTreeDefinition>());

            Assert.Throws<ArgumentException>(() => h.Host.OpenGossip(Player, Npc, menu.Id));
            Assert.Throws<ArgumentException>(() => h.Host.StartStory(Player, tree.Id));
        }

        [Fact]
        public void Reload_DoesNotCloseAnOpenSession()
        {
            var menu = Menu("dialog.kept_session");
            var h = new Harness(new[] { menu }, Array.Empty<StoryTreeDefinition>());
            h.Host.OpenGossip(Player, Npc, menu.Id);

            h.Host.Reload(new[] { menu }, Array.Empty<StoryTreeDefinition>());

            Assert.NotNull(h.Host.GetGossipView(Player));
            Assert.Empty(h.PublishedOf<DialogEndedEvent>());
            Assert.True(h.Host.Close(Player));
        }

        // ------------------------------ InMemoryDialogDiagnostics ------------------------------

        [Fact]
        public void InMemoryDialogDiagnostics_StartsEmpty_AndRecordsWarningsInOrder()
        {
            var diagnostics = new InMemoryDialogDiagnostics();
            Assert.Empty(diagnostics.Warnings);

            diagnostics.Warn("first");
            diagnostics.Warn("second");
            diagnostics.Warn("first");

            Assert.Equal(new[] { "first", "second", "first" }, diagnostics.Warnings);
        }
    }
}
