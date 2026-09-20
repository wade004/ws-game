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

        // 消费方反馈第 65 条：以下四组用例与 toolchain/tests/test_ref_conventions.py 对应函数使用同一组
        // 输入/期望字符串，两侧各自独立实现、互相不调用，任一侧改动规则而另一侧未同步会被各自语言的
        // 测试独立捕获（同本文件类型顶部"跨语言对照"判断记录）。

        [Theory]
        [InlineData("vfx.sample_cast_circle", "vfx/sample_cast_circle")]
        [InlineData("vfx.fire_impact", "vfx/fire_impact")]
        public void VfxResourceDir_MatchesVfxCmdPyOutputPath(string resourceRef, string expected)
        {
            Assert.Equal(expected, AssetRefConventions.VfxResourceDir(new Id(resourceRef)));
        }

        [Theory]
        [InlineData("sfx.sword_hit_v0", "sfx/sword_hit_v0.wav")]
        [InlineData("sfx.sword_hit_v1", "sfx/sword_hit_v1.wav")]
        public void SfxResourceFile_MatchesSfxCmdPyOutputPath(string resourceRef, string expected)
        {
            Assert.Equal(expected, AssetRefConventions.SfxResourceFile(new Id(resourceRef)));
        }

        [Theory]
        [InlineData("anim.idle", "GameFoundation/anim_clips/idle")]
        [InlineData("anim.attack", "GameFoundation/anim_clips/attack")]
        public void AnimClipLogicalPath_MatchesUnityResourceLoaderResolveAnimClipResourcesPath(string resourceRef, string expected)
        {
            Assert.Equal(expected, AssetRefConventions.AnimClipLogicalPath(new Id(resourceRef)));
        }

        [Theory]
        [InlineData("model.placeholder_biped", "GameFoundation/models/placeholder_biped")]
        public void ModelLogicalPath_MatchesUnityResourceLoaderResolveModelResourcesPath(string resourceRef, string expected)
        {
            Assert.Equal(expected, AssetRefConventions.ModelLogicalPath(new Id(resourceRef)));
        }

        // ADR-0038 决策 2/4：以下用例与 toolchain/tests/test_ref_conventions.py 对应用例使用同一组
        // 输入/期望字符串，两侧各自独立实现、互相不调用（同第 65 条判断记录"跨语言对照"）。

        [Theory]
        [InlineData("sprite_anim.sample_hero_idle", "sprite_anim/sample_hero_idle")]
        [InlineData("sprite_anim.sample_hero_attack", "sprite_anim/sample_hero_attack")]
        public void SpriteAnimDir_MatchesVfxResourceDirStyle(string resourceRef, string expected)
        {
            Assert.Equal(expected, AssetRefConventions.SpriteAnimDir(new Id(resourceRef)));
        }

        [Theory]
        [InlineData("paperdoll.item.sample_hero_hat_test", "paperdoll/item_sample_hero_hat_test.png")]
        [InlineData("paperdoll.item.sample_cloak", "paperdoll/item_sample_cloak.png")]
        public void PaperdollLayerFile_ReturnsFlatPngUnderOwnRoot(string resourceRef, string expected)
        {
            Assert.Equal(expected, AssetRefConventions.PaperdollLayerFile(new Id(resourceRef)));
        }

        [Theory]
        [InlineData("sprite.creature.wolf_grey", AssetRefConventions.AssetRefPathSpace.AssetRootRelative, "sprites/creature_wolf_grey")]
        [InlineData("icon.item.sample_blade", AssetRefConventions.AssetRefPathSpace.AssetRootRelative, "icons/item/sample_blade.png")]
        [InlineData("vfx.sample_cast_circle", AssetRefConventions.AssetRefPathSpace.AssetRootRelative, "vfx/sample_cast_circle")]
        [InlineData("sfx.sword_hit_v0", AssetRefConventions.AssetRefPathSpace.AssetRootRelative, "sfx/sword_hit_v0.wav")]
        [InlineData("sprite_anim.sample_hero_idle", AssetRefConventions.AssetRefPathSpace.AssetRootRelative, "sprite_anim/sample_hero_idle")]
        [InlineData("paperdoll.item.sample_hero_hat_test", AssetRefConventions.AssetRefPathSpace.AssetRootRelative, "paperdoll/item_sample_hero_hat_test.png")]
        [InlineData("anim.idle", AssetRefConventions.AssetRefPathSpace.EngineLogicalPath, "GameFoundation/anim_clips/idle")]
        [InlineData("model.placeholder_biped", AssetRefConventions.AssetRefPathSpace.EngineLogicalPath, "GameFoundation/models/placeholder_biped")]
        public void ResolvePathSpace_DispatchesByCategoryPrefix(
            string resourceRef, AssetRefConventions.AssetRefPathSpace expectedSpace, string expectedPath)
        {
            var (space, path) = AssetRefConventions.ResolvePathSpace(new Id(resourceRef));
            Assert.Equal(expectedSpace, space);
            Assert.Equal(expectedPath, path);
        }

        [Fact]
        public void ResolvePathSpace_UnknownCategory_ThrowsWithLegalSetInMessage()
        {
            var ex = Assert.Throws<ArgumentException>(
                () => AssetRefConventions.ResolvePathSpace(new Id("bogus.sample_thing")));
            Assert.Contains("bogus", ex.Message);
            foreach (var known in AssetRefConventions.KnownCategories)
            {
                Assert.Contains(known, ex.Message);
            }
        }

        // 判断记录：resourceRefId 不含点号（无法取出类别前缀）这一分支在 ResolvePathSpace 内部保留
        // 作为防御性代码，但无法通过一个合法构造出来的 Id 触发单测——Id 的构造函数本身要求至少一个
        // 点号（见 Id.FormatRegex），不满足格式的字符串在到达本方法之前已经在 new Id(...) 处抛出
        // ArgumentException（同 AssetRefConventions.StripCategoryPrefix 类型注释"理论不应发生"）。

        // 消费方反馈第 75 条（ADR-0053）：以下用例与 toolchain/tests/test_ref_conventions.py 对应
        // 函数使用同一组输入/期望字符串，两侧各自独立实现、互相不调用（同本文件类型顶部"跨语言对照"
        // 判断记录）；两侧是否实际算出同一个值另有一组更强的跨语言一致性测试，见
        // toolchain/tests/test_ref_conventions.py 里经 toolchain/map_ref_probe 子进程对照的用例
        // （本文件所在语言运行时无法直接调用 Python，不在本文件内重复该项）。

        [Theory]
        [InlineData("world.sample_field", "maps/sample_field")]
        [InlineData("world.another_map", "maps/another_map")]
        public void MapDirectory_MatchesMapCmdPyOutDir(string mapId, string expected)
        {
            Assert.Equal(expected, AssetRefConventions.MapDirectory(new Id(mapId)));
        }

        [Theory]
        [InlineData("world.sample_field", "maps/sample_field/ground.png")]
        [InlineData("world.another_map", "maps/another_map/ground.png")]
        public void MapGroundFile_MatchesMapCmdPyOutputPath(string mapId, string expected)
        {
            Assert.Equal(expected, AssetRefConventions.MapGroundFile(new Id(mapId)));
        }

        [Theory]
        [InlineData("world.sample_field", "maps/sample_field/overlay.png")]
        public void MapOverlayFile_MatchesMapCmdPyOutputPath(string mapId, string expected)
        {
            Assert.Equal(expected, AssetRefConventions.MapOverlayFile(new Id(mapId)));
        }

        [Theory]
        [InlineData("world.sample_field", "maps/sample_field/decal.png")]
        public void MapDecalFile_MatchesMapCmdPyOutputPath(string mapId, string expected)
        {
            Assert.Equal(expected, AssetRefConventions.MapDecalFile(new Id(mapId)));
        }

        [Theory]
        [InlineData("world.sample_field", "maps/sample_field/nav_hint.png")]
        public void MapNavHintFile_MatchesMapCmdPyOutputPath(string mapId, string expected)
        {
            Assert.Equal(expected, AssetRefConventions.MapNavHintFile(new Id(mapId)));
        }

        [Fact]
        public void MapLayerFiles_AllShareMapDirectoryAsCommonPrefix()
        {
            var mapId = new Id("world.sample_field");
            var directory = AssetRefConventions.MapDirectory(mapId);
            Assert.Equal(directory + "/ground.png", AssetRefConventions.MapGroundFile(mapId));
            Assert.Equal(directory + "/overlay.png", AssetRefConventions.MapOverlayFile(mapId));
            Assert.Equal(directory + "/decal.png", AssetRefConventions.MapDecalFile(mapId));
            Assert.Equal(directory + "/nav_hint.png", AssetRefConventions.MapNavHintFile(mapId));
        }
    }
}
