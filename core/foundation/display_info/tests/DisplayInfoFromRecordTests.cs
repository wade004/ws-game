using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.DisplayInfo;
using Xunit;

namespace Tests.Foundation.DisplayInfo
{
    public class DisplayInfoFromRecordTests
    {
        [Fact]
        public void FromRecord_SpriteExample_ParsesAllCommonAndSpriteFields()
        {
            var (registry, report) = DisplayInfoTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["display.map"] = "[" + DisplayInfoTestSupport.GreyWolfSpriteRow + "]",
            });
            Assert.False(report.IsBlocking);

            var record = registry.Get("display.map", "display.grey_wolf")!;
            var info = Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(record);

            Assert.Equal(new Id("display.grey_wolf"), info.Id);
            Assert.Equal(DisplayCategory.Creature, info.Category);
            Assert.Equal(new Id("creature.grey_wolf"), info.LogicalId);
            Assert.Equal(DisplayKind.Sprite, info.Kind);
            Assert.Equal("icon.creature.wolf_grey", info.IconId);
            Assert.Equal(new Id("sfx.wolf_growl"), info.SfxId);
            Assert.Null(info.VfxId);
            Assert.Equal(1.0, info.Scale);
            Assert.Equal(ShadowMode.Blob, info.Shadow);
            Assert.Equal(0.0, info.SortOffset);
            Assert.Null(info.WeaponStyleRef);
            Assert.Null(info.Model);

            Assert.NotNull(info.Sprite);
            Assert.Equal("sprite.creature.wolf_grey", info.Sprite!.SpriteSetId);
            Assert.Equal(8, info.Sprite.DirectionCount);
            Assert.Empty(info.Sprite.MirrorPairs);
            Assert.Empty(info.Sprite.PaperdollLayers);
            Assert.Empty(info.Sprite.AnchorPoints);
        }

        [Fact]
        public void FromRecord_ModelExample_ParsesAllCommonAndModelFields()
        {
            var (registry, report) = DisplayInfoTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["display.map"] = "[" + DisplayInfoTestSupport.StoneGolemModelRow + "]",
                ["display.anim_set"] = "[" + DisplayInfoTestSupport.StoneGolemAnimSetRow + "]",
            });
            Assert.False(report.IsBlocking);

            var record = registry.Get("display.map", "display.stone_golem")!;
            var info = Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(record);

            Assert.Equal(DisplayCategory.Creature, info.Category);
            Assert.Equal(new Id("creature.stone_golem"), info.LogicalId);
            Assert.Equal(DisplayKind.Model, info.Kind);
            Assert.Equal(1.2, info.Scale);
            Assert.Equal(ShadowMode.Projected, info.Shadow);
            Assert.Null(info.Sprite);

            Assert.NotNull(info.Model);
            Assert.Equal(new Id("model.creature.stone_golem"), info.Model!.ModelRef);
            Assert.Equal(new Id("display.anim_set.stone_golem"), info.Model.AnimSetRef);
            Assert.Equal(new[] { new Id("socket.hand_main"), new Id("socket.hand_off") }, info.Model.Sockets);
            Assert.Equal(new[] { new Id("slot.weapon_main") }, info.Model.Slots);
            Assert.True(info.Model.DefaultSlotMeshes.TryGetValue(new Id("slot.weapon_main"), out var mesh));
            Assert.Equal(new Id("mesh.golem_fist"), mesh);
            Assert.Empty(info.Model.MaterialParams);
        }

        [Fact]
        public void FromRecord_MissingOptionalCommonFields_UsesDocumentedDefaults()
        {
            const string minimalSpriteRow = @"
            {
              ""id"": ""display.minimal"",
              ""category"": ""gobj"",
              ""logical_id"": ""gobj.wooden_chest"",
              ""kind"": ""sprite"",
              ""sprite_set_id"": ""sprite.gobj.wooden_chest"",
              ""direction_count"": 4
            }";

            var (registry, report) = DisplayInfoTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["display.map"] = "[" + minimalSpriteRow + "]",
            });
            Assert.False(report.IsBlocking);

            var info = Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(registry.Get("display.map", "display.minimal")!);

            Assert.Null(info.IconId);
            Assert.Null(info.VfxId);
            Assert.Null(info.SfxId);
            Assert.Equal(1.0, info.Scale); // 04 第 7.1 节：scale 默认 1.0
            Assert.Equal(ShadowMode.Blob, info.Shadow); // 默认 blob
            Assert.Equal(0.0, info.SortOffset); // 默认 0
            Assert.Null(info.WeaponStyleRef);
            Assert.Equal(DisplayCategory.Gobj, info.Category);
        }

        [Fact]
        public void FromRecord_MirrorPairsPaperdollLayersAnchorPoints_ParsedCorrectly()
        {
            var (registry, report) = DisplayInfoTestSupport.BuildRegistry(new Dictionary<string, string>
            {
                ["display.map"] = "[" + DisplayInfoTestSupport.PlayerHeroSpriteRow + "]",
            });
            Assert.False(report.IsBlocking);

            var info = Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(registry.Get("display.map", "display.player_hero")!);

            Assert.NotNull(info.Sprite);
            Assert.Single(info.Sprite!.MirrorPairs);
            var pair = info.Sprite.MirrorPairs[0];
            Assert.Equal(new Id("dir.se"), pair.DirectionSlot);
            Assert.Equal(new Id("dir.sw"), pair.MirrorOf);
            Assert.True(pair.FlipX);

            Assert.Equal(new[] { "layer.base", "layer.armor" }, info.Sprite.PaperdollLayers);

            Assert.True(info.Sprite.AnchorPoints.TryGetValue("hand_main", out var anchor));
            Assert.Equal(new Vec2(1.5, 0.5), anchor);
        }

        [Fact]
        public void FromRecord_UnknownEnumValue_ThrowsDataFieldException()
        {
            const string badRow = @"
            {
              ""id"": ""display.bad_kind"",
              ""category"": ""creature"",
              ""logical_id"": ""creature.something"",
              ""kind"": ""hologram"",
              ""sprite_set_id"": ""sprite.x"",
              ""direction_count"": 4
            }";

            // 判断记录：kind 枚举合法集合只登记 sprite|model（04 第 7.1 节），"hologram" 不在其中，
            // 数据校验期本应由 field_type/枚举合法检查项拦截；这里绕过校验直接构造 DataRecord
            // 验证 FromRecord 自身对未知枚举值的防御行为（DisplayKindFieldGroupRule 遇到未知
            // kind 会直接跳过该记录，不产生错误，因此改用 FailOnUnknownTable=false 之外的方式：
            // 直接注册一个不含枚举约束的宽松 schema 让数据"合法通过"，专测 FromRecord 的解析防御。
            var looseSchema = new Core.Foundation.DataRegistry.TableSchema(
                "display.map", "id", 1,
                new[]
                {
                    new Core.Foundation.DataRegistry.FieldSchema("id", Core.Foundation.DataRegistry.FieldKind.Id, required: true),
                    new Core.Foundation.DataRegistry.FieldSchema("category", Core.Foundation.DataRegistry.FieldKind.String, required: true),
                    new Core.Foundation.DataRegistry.FieldSchema("logical_id", Core.Foundation.DataRegistry.FieldKind.Id, required: true),
                    new Core.Foundation.DataRegistry.FieldSchema("kind", Core.Foundation.DataRegistry.FieldKind.String, required: true),
                    new Core.Foundation.DataRegistry.FieldSchema("sprite_set_id", Core.Foundation.DataRegistry.FieldKind.String, required: false),
                    new Core.Foundation.DataRegistry.FieldSchema("direction_count", Core.Foundation.DataRegistry.FieldKind.Int, required: false),
                });

            var source = new Core.Foundation.DataRegistry.InMemoryDataSource()
                .Add("display.map", "{\"table\": \"display.map\", \"schema_version\": 1, \"rows\": [" + badRow + "]}");
            var registry = new Core.Foundation.DataRegistry.DataRegistry(source, DisplayInfoTestSupport.CreateBus());
            registry.RegisterSchema(looseSchema);
            var report = registry.LoadAll();
            Assert.False(report.IsBlocking);

            var record = registry.Get("display.map", "display.bad_kind")!;
            Assert.Throws<Core.Foundation.DataRegistry.DataFieldException>(() => Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(record));
        }
    }
}
