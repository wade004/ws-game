using Presentation.Ui;
using Xunit;

namespace Tests.PresentationUi
{
    public class UiPathParserTests
    {
        [Fact]
        public void Parses_plain_dotted_path_into_segments()
        {
            Assert.True(UiPathParser.TryParse("player.power.arch.power.health.current", out var segments));
            Assert.Equal(6, segments.Count);
            Assert.Equal("player", segments[0].Name);
            Assert.Null(segments[0].Index);
            Assert.Equal("current", segments[5].Name);
        }

        [Fact]
        public void Parses_bracket_index_suffix()
        {
            Assert.True(UiPathParser.TryParse("player.inventory[3].template", out var segments));
            Assert.Equal(3, segments.Count);
            Assert.Equal("inventory", segments[1].Name);
            Assert.Equal(3, segments[1].Index);
            Assert.Null(segments[0].Index);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("Player.level")]
        [InlineData("player..level")]
        [InlineData("player.inventory[abc]")]
        public void Rejects_malformed_paths(string? path)
        {
            Assert.False(UiPathParser.TryParse(path, out var segments));
            Assert.Empty(segments);
        }
    }
}
