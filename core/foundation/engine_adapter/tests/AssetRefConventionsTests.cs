using System;
using Core.Foundation.Common;
using Core.Foundation.EngineAdapter;
using Xunit;

namespace Tests.Foundation.EngineAdapter
{
    /// <summary>
    /// 消费方反馈第 32 条（ADR-0025）验收测试：<see cref="AssetRefConventions"/> 的正向/反向解析。
    /// <para>
    /// 判断记录（跨语言对照）：本文件与 <c>toolchain/tests/test_ref_conventions.py</c> 使用同一组
    /// 样例（<c>sprite.item.sample_blade</c>/<c>icon.creature.sample_beast</c> 等，均取自
    /// <c>toolchain/import_sample_assets.py</c> 产出的真实 <c>data/_sample/display/display.map.json</c>
    /// 行——见该脚本"来源素材 -> 子命令 -> 产物 -> 回写字段对应表"），两侧各自独立实现同一条规则、
    /// 断言同一个期望字符串；任一侧的实现与约定漂移，会在各自语言的测试里独立暴露，不依赖两侧互相
    /// 调用。
    /// </para>
    /// </summary>
    public class AssetRefConventionsTests
    {
        [Theory]
        [InlineData("sprite.creature.wolf_grey", "creature_wolf_grey")]
        [InlineData("icon.item.sample_blade", "item_sample_blade")]
        [InlineData("vfx.sample_cast_circle", "sample_cast_circle")]
        public void StripCategoryPrefix_RemovesFirstSegmentAndFlattensRemainder(string input, string expected)
        {
            Assert.Equal(expected, AssetRefConventions.StripCategoryPrefix(input));
        }

        [Fact]
        public void StripCategoryPrefix_NoDot_ReturnsOriginal()
        {
            Assert.Equal("nodothere", AssetRefConventions.StripCategoryPrefix("nodothere"));
        }

        [Theory]
        [InlineData("sprite.creature.wolf_grey", "sprites/creature_wolf_grey")]
        [InlineData("sprite.item.sample_blade", "sprites/item_sample_blade")]
        [InlineData("sprite.gobj.sample_chest", "sprites/gobj_sample_chest")]
        public void SpriteSetDirectory_MatchesSpriteCmdPyOutputPath(string spriteSetId, string expected)
        {
            Assert.Equal(expected, AssetRefConventions.SpriteSetDirectory(new Id(spriteSetId)));
        }

        [Theory]
        [InlineData("icon.creature.sample_beast", "icons/creature/sample_beast.png")]
        [InlineData("icon.creature.sample_hero", "icons/creature/sample_hero.png")]
        [InlineData("icon.gobj.sample_chest", "icons/gobj/sample_chest.png")]
        [InlineData("icon.gobj.sample_door", "icons/gobj/sample_door.png")]
        [InlineData("icon.gobj.sample_save_point", "icons/gobj/sample_save_point.png")]
        public void IconFile_MatchesIconCmdPyOutputPath(string iconId, string expected)
        {
            Assert.Equal(expected, AssetRefConventions.IconFile(new Id(iconId)));
        }

        [Fact]
        public void IconFile_MissingNameSegment_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => AssetRefConventions.IconFile(new Id("icon.onlycategory")));
        }

        [Theory]
        [InlineData("sprites/creature_wolf_grey", "sprite.creature.wolf_grey")]
        [InlineData("sprites/item_sample_blade", "sprite.item.sample_blade")]
        [InlineData("/sprites/gobj_sample_chest/", "sprite.gobj.sample_chest")]
        public void TryParseSpriteSetId_RoundTripsSpriteSetDirectory(string relativeDirectory, string expectedId)
        {
            Assert.True(AssetRefConventions.TryParseSpriteSetId(relativeDirectory, out var spriteSetId));
            Assert.Equal(new Id(expectedId), spriteSetId);
        }

        [Theory]
        [InlineData("")]
        [InlineData("vfx/sample_cast_circle")]
        [InlineData("sprites/noUnderscoreHere")]
        public void TryParseSpriteSetId_MalformedInput_ReturnsFalse(string relativeDirectory)
        {
            Assert.False(AssetRefConventions.TryParseSpriteSetId(relativeDirectory, out _));
        }

        [Theory]
        [InlineData("icons/creature/sample_beast.png", "icon.creature.sample_beast")]
        [InlineData("icons/gobj/sample_chest.png", "icon.gobj.sample_chest")]
        [InlineData("/icons/item/sample_blade.png", "icon.item.sample_blade")]
        public void TryParseIconId_RoundTripsIconFile(string relativeFilePath, string expectedId)
        {
            Assert.True(AssetRefConventions.TryParseIconId(relativeFilePath, out var iconId));
            Assert.Equal(new Id(expectedId), iconId);
        }

        [Theory]
        [InlineData("")]
        [InlineData("sprites/creature_wolf_grey")]
        [InlineData("icons/onlyonesegment.png")]
        [InlineData("icons/a/b/c.png")]
        public void TryParseIconId_MalformedInput_ReturnsFalse(string relativeFilePath)
        {
            Assert.False(AssetRefConventions.TryParseIconId(relativeFilePath, out _));
        }

        [Theory]
        [InlineData("sprite.creature.wolf_grey")]
        [InlineData("sprite.item.sample_blade")]
        [InlineData("sprite.gobj.sample_chest")]
        public void SpriteSetDirectory_RoundTripsThroughTryParse(string spriteSetIdText)
        {
            var spriteSetId = new Id(spriteSetIdText);
            var dir = AssetRefConventions.SpriteSetDirectory(spriteSetId);
            Assert.True(AssetRefConventions.TryParseSpriteSetId(dir, out var parsed));
            Assert.Equal(spriteSetId, parsed);
        }

        [Theory]
        [InlineData("icon.creature.sample_beast")]
        [InlineData("icon.item.sample_blade")]
        [InlineData("icon.gobj.sample_save_point")]
        public void IconFile_RoundTripsThroughTryParse(string iconIdText)
        {
            var iconId = new Id(iconIdText);
            var file = AssetRefConventions.IconFile(iconId);
            Assert.True(AssetRefConventions.TryParseIconId(file, out var parsed));
            Assert.Equal(iconId, parsed);
        }
    }
}
