using System;
using Core.Foundation.Common;
using Core.Gameplay.Dialog;
using Xunit;

namespace Tests.Gameplay.Dialog
{
    /// <summary>
    /// P2-05 关联根治回归测试（外部审计 audit-c9ff301-20260909）：<see cref="DialogHost"/> 的
    /// <c>_gossipMenus</c>/<c>_storyTrees</c> 缓存只在构造期从注入的 <c>IEnumerable&lt;T&gt;</c>
    /// 建索引，此前没有任何刷新入口。<see cref="DialogHost.Reload"/> 补齐后，resident host 的
    /// 下一次 <see cref="DialogHost.OpenGossip"/> 应看到新菜单内容。
    /// </summary>
    public sealed class P2_05_DialogHostReloadTests
    {
        private static readonly Id Player = TestSupport.Player;
        private static readonly Id Npc = TestSupport.Npc;

        private static GossipActionDef SaveAction() => new GossipActionDef(DialogActionKind.Save, null, null);

        [Fact]
        public void P2_05_Reload_PicksUpNewGossipMenuOptions_AfterReload()
        {
            var menuId = new Id("dialog.p2_05_menu");
            var before = new GossipMenuDefinition(menuId, new[]
            {
                new GossipOption(new Id("l10n.p2_05_opt_a"), null, new[] { SaveAction() }),
            });
            var h = new Harness(new[] { before }, Array.Empty<StoryTreeDefinition>());

            var viewBefore = h.Host.OpenGossip(Player, Npc, menuId);
            Assert.Single(viewBefore.Options);

            var after = new GossipMenuDefinition(menuId, new[]
            {
                new GossipOption(new Id("l10n.p2_05_opt_a"), null, new[] { SaveAction() }),
                new GossipOption(new Id("l10n.p2_05_opt_b"), null, new[] { SaveAction() }),
            });
            h.Host.Reload(new[] { after }, Array.Empty<StoryTreeDefinition>());

            var viewAfter = h.Host.OpenGossip(Player, Npc, menuId);
            Assert.Equal(2, viewAfter.Options.Count);
        }

        [Fact]
        public void P2_05_Reload_NullArguments_Throw()
        {
            var h = new Harness(Array.Empty<GossipMenuDefinition>(), Array.Empty<StoryTreeDefinition>());
            Assert.Throws<ArgumentNullException>(() => h.Host.Reload(null!, Array.Empty<StoryTreeDefinition>()));
            Assert.Throws<ArgumentNullException>(() => h.Host.Reload(Array.Empty<GossipMenuDefinition>(), null!));
        }
    }
}
