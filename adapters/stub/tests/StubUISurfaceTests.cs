using Adapters.Stub;
using Core.Foundation.Common;
using Xunit;

namespace Tests.StubAdapters
{
    public class StubUISurfaceTests
    {
        private static readonly Id Hud = new Id("ui.hud");
        private static readonly Id Menu = new Id("ui.menu");

        [Fact]
        public void CreateSurface_RecordsSize_PerSurface_AndRecreateOverwrites()
        {
            var ui = new StubUISurface();
            ui.CreateSurface(Hud, 800, 600);
            ui.CreateSurface(Menu, 320, 200);
            ui.CreateSurface(Hud, 1024, 768);

            Assert.Equal((1024, 768), ui.Surfaces[Hud]);
            Assert.Equal((320, 200), ui.Surfaces[Menu]);
            Assert.Equal(2, ui.Surfaces.Count);
        }

        [Fact]
        public void SetLayout_StoresLayoutText_PerSurface_LatestWins_WithoutRequiringCreate()
        {
            var ui = new StubUISurface();
            ui.SetLayout(Hud, "<a/>");
            ui.SetLayout(Hud, "<b/>");
            ui.SetLayout(Menu, "<c/>");

            Assert.Equal("<b/>", ui.Layouts[Hud]);
            Assert.Equal("<c/>", ui.Layouts[Menu]);
            Assert.Empty(ui.Surfaces);
        }

        [Fact]
        public void DrawText_AppendsEveryCall_InOrder_WithAllFields_EvenIdenticalOnes()
        {
            var ui = new StubUISurface();
            var font = new Id("font.main");
            ui.DrawText(Hud, "hello", new Vec2(1, 2), font, 12.0);
            ui.DrawText(Hud, "hello", new Vec2(1, 2), font, 12.0);
            ui.DrawText(Menu, "world", new Vec2(-3, 4), new Id("font.alt"), 20.5);

            Assert.Equal(3, ui.DrawTextCalls.Count);
            Assert.Equal(ui.DrawTextCalls[0].Text, ui.DrawTextCalls[1].Text);
            var last = ui.DrawTextCalls[2];
            Assert.Equal(Menu, last.SurfaceId);
            Assert.Equal("world", last.Text);
            Assert.Equal(new Vec2(-3, 4), last.Position);
            Assert.Equal(new Id("font.alt"), last.FontId);
            Assert.Equal(20.5, last.Size);
        }

        [Fact]
        public void Focus_StartsNull_SetFocusTracks_ClearFocusForTestResetsToNull()
        {
            var ui = new StubUISurface();
            Assert.Null(ui.GetFocusedElement());

            ui.SetFocus(new Id("ui.button_a"));
            Assert.Equal(new Id("ui.button_a"), ui.GetFocusedElement());
            ui.SetFocus(new Id("ui.button_b"));
            Assert.Equal(new Id("ui.button_b"), ui.GetFocusedElement());

            ui.ClearFocusForTest();
            Assert.Null(ui.GetFocusedElement());
        }

        [Fact]
        public void Focus_IsGlobalSingle_NotScopedToSurface_AndDoesNotRequireExistingSurface()
        {
            var ui = new StubUISurface();
            ui.CreateSurface(Hud, 1, 1);
            ui.SetFocus(new Id("ui.elem_in_unknown_surface"));
            ui.CreateSurface(Menu, 1, 1);

            Assert.Equal(new Id("ui.elem_in_unknown_surface"), ui.GetFocusedElement());
        }
    }
}
