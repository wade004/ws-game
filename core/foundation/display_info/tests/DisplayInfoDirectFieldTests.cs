using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.DisplayInfo;
using Xunit;

namespace Tests.Foundation.DisplayInfo
{
    /// <summary>
    /// display_info 契约类型的直接断言与 <c>FromRecord</c> 错误路径（T-L5 + T-M15 core 半，
    /// 2026-10-01 测试覆盖第四批）：<c>AnimSetDef.CombatClipKey/CombatClipKeyPrefix</c>、
    /// <c>AnchorDef.OffsetByDirection</c>、<c>DisplayInfo.StrideDistance</c>，以及构造器空参守卫与
    /// 各解析分支的 <see cref="DataFieldException"/>。记录直接由 JSON 构造（绕过 schema 校验），
    /// 专测解析自身的防御行为。
    /// </summary>
    public sealed class DisplayInfoDirectFieldTests
    {
        private static DataRecord Record(string tableName, string json)
        {
            var table = new TableSchema(tableName, "id", 1, new List<FieldSchema>());
            var raw = (JsonObject)JsonReader.Parse(json);
            var key = ((JsonString)raw["id"]).Value;
            return new DataRecord(table, key, new Id(key), raw);
        }

        private static DataRecord MapRecord(string extraFieldsJson)
        {
            return Record("display.map",
                "{\"id\": \"display.t\", \"category\": \"creature\", \"logical_id\": \"creature.t\", " +
                "\"kind\": \"sprite\", \"sprite_set_id\": \"sprite.t\", \"direction_count\": 4" +
                (extraFieldsJson.Length > 0 ? ", " + extraFieldsJson : "") + "}");
        }

        private static DataRecord AnimSetRecord(string clipsJson) =>
            Record("display.anim_set", "{\"id\": \"display.anim_set.t\", \"clips\": " + clipsJson + "}");

        private static DataFieldException FieldError(Action act, string field)
        {
            var ex = Assert.Throws<DataFieldException>(act);
            Assert.Equal(field, ex.Field);
            return ex;
        }

        // -----------------------------------------------------------------
        // AnimSetDef.CombatClipKey / CombatClipKeyPrefix
        // -----------------------------------------------------------------

        [Fact]
        public void CombatClipKey_IsPrefixPlusBaseKey_AndPrefixConstantIsStable()
        {
            Assert.Equal("combat_", AnimSetDef.CombatClipKeyPrefix);
            foreach (var baseKey in new[] { "idle", "move", "attack", "", "a_b" })
            {
                Assert.Equal(AnimSetDef.CombatClipKeyPrefix + baseKey, AnimSetDef.CombatClipKey(baseKey));
            }
        }

        [Fact]
        public void CombatClipKey_UsesOrdinalConcatenation_NoCaseOrCultureTransform()
        {
            Assert.Equal("combat_Idle", AnimSetDef.CombatClipKey("Idle"));
            Assert.Equal("combat_combat_idle", AnimSetDef.CombatClipKey("combat_idle"));
        }

        // -----------------------------------------------------------------
        // AnimSetDef / AnimClipDef / AnimClipEventSpec 构造与解析
        // -----------------------------------------------------------------

        [Fact]
        public void AnimClipEventSpec_NullName_Throws_AndEqualityIsValueBased()
        {
            Assert.Throws<ArgumentNullException>(() => new AnimClipEventSpec(null!, 0.5));

            var a = new AnimClipEventSpec("hit", 0.6);
            Assert.Equal(a, new AnimClipEventSpec("hit", 0.6));
            Assert.Equal(a.GetHashCode(), new AnimClipEventSpec("hit", 0.6).GetHashCode());
            Assert.NotEqual(a, new AnimClipEventSpec("hit", 0.7));
            Assert.NotEqual(a, new AnimClipEventSpec("Hit", 0.6));
            Assert.Equal("hit@0.6", a.ToString());
        }

        [Fact]
        public void AnimClipDef_NullEvents_BecomesEmptyList()
        {
            var clip = new AnimClipDef(new Id("anim.a"));
            Assert.Empty(clip.Events);
            Assert.Equal(new Id("anim.a"), clip.ResourceRef);
        }

        [Fact]
        public void AnimSetDef_NullClips_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new AnimSetDef(new Id("display.anim_set.t"), null!));
        }

        [Fact]
        public void AnimSetDef_FromRecord_ClipValueNotObject_Throws()
        {
            FieldError(() => AnimSetDef.FromRecord(AnimSetRecord("{\"idle\": 3}")), "clips");
            FieldError(() => AnimSetDef.FromRecord(AnimSetRecord("{\"idle\": \"anim.a\"}")), "clips");
        }

        [Fact]
        public void AnimSetDef_FromRecord_ResourceRefMissingOrMalformed_Throws()
        {
            FieldError(() => AnimSetDef.FromRecord(AnimSetRecord("{\"idle\": {}}")), "clips");
            FieldError(() => AnimSetDef.FromRecord(AnimSetRecord("{\"idle\": {\"resource_ref\": 5}}")), "clips");
            FieldError(() => AnimSetDef.FromRecord(AnimSetRecord("{\"idle\": {\"resource_ref\": \"Not Id\"}}")), "clips");
        }

        [Fact]
        public void AnimSetDef_FromRecord_EventsNotArray_Throws_ButNullEventsIsAccepted()
        {
            FieldError(() => AnimSetDef.FromRecord(
                AnimSetRecord("{\"idle\": {\"resource_ref\": \"anim.a\", \"events\": {}}}")), "clips");

            var def = AnimSetDef.FromRecord(
                AnimSetRecord("{\"idle\": {\"resource_ref\": \"anim.a\", \"events\": null}}"));
            Assert.Empty(def.Clips["idle"].Events);
        }

        [Theory]
        [InlineData("[3]")]
        [InlineData("[{\"time_pct\": 0.5}]")]
        [InlineData("[{\"name\": \"\", \"time_pct\": 0.5}]")]
        [InlineData("[{\"name\": 7, \"time_pct\": 0.5}]")]
        [InlineData("[{\"name\": \"hit\"}]")]
        [InlineData("[{\"name\": \"hit\", \"time_pct\": \"0.5\"}]")]
        [InlineData("[{\"name\": \"ok\", \"time_pct\": 0.1}, {\"name\": \"hit\"}]")]
        public void AnimSetDef_FromRecord_BadEventItem_Throws(string eventsJson)
        {
            var record = AnimSetRecord("{\"idle\": {\"resource_ref\": \"anim.a\", \"events\": " + eventsJson + "}}");

            FieldError(() => AnimSetDef.FromRecord(record), "clips");
        }

        [Fact]
        public void AnimSetDef_FromRecord_BadEventIndexIsNamedInMessage()
        {
            var record = AnimSetRecord(
                "{\"idle\": {\"resource_ref\": \"anim.a\", \"events\": [{\"name\": \"ok\", \"time_pct\": 0.1}, {\"name\": \"hit\"}]}}");

            var ex = FieldError(() => AnimSetDef.FromRecord(record), "clips");

            Assert.Contains("第 1 项", ex.Message);
            Assert.Contains("idle", ex.Message);
        }

        [Fact]
        public void AnimSetDef_FromRecord_MissingId_Throws()
        {
            var table = new TableSchema("display.anim_set", "id", 1, new List<FieldSchema>());
            var raw = (JsonObject)JsonReader.Parse("{\"clips\": {}}");
            var record = new DataRecord(table, "display.anim_set.t", null, raw);

            Assert.Throws<DataFieldException>(() => AnimSetDef.FromRecord(record));
        }

        // -----------------------------------------------------------------
        // AnchorDef.OffsetByDirection
        // -----------------------------------------------------------------

        [Fact]
        public void AnchorDef_DefaultOffsetByDirection_IsEmptyNonNull_AndResolveFallsBackToOffset()
        {
            var anchor = new AnchorDef("layer.base", new Vec2(1, 2));

            Assert.NotNull(anchor.OffsetByDirection);
            Assert.Empty(anchor.OffsetByDirection);
            Assert.Equal(new Vec2(1, 2), anchor.ResolveOffset(new Id("dir.front")));
            Assert.Equal("layer.base", anchor.ParentLayer);
        }

        [Fact]
        public void AnchorDef_OffsetByDirection_IsCarried_AndOnlyListedSlotsOverride()
        {
            var overrides = new Dictionary<Id, Vec2>
            {
                [new Id("dir.side_r")] = new Vec2(3, 4),
                [new Id("dir.back")] = new Vec2(-1, 0),
            };
            var anchor = new AnchorDef("layer.base", new Vec2(1, 2), overrides);

            Assert.Equal(2, anchor.OffsetByDirection.Count);
            Assert.Equal(new Vec2(3, 4), anchor.OffsetByDirection[new Id("dir.side_r")]);
            Assert.Equal(new Vec2(3, 4), anchor.ResolveOffset(new Id("dir.side_r")));
            Assert.Equal(new Vec2(-1, 0), anchor.ResolveOffset(new Id("dir.back")));
            Assert.Equal(new Vec2(1, 2), anchor.ResolveOffset(new Id("dir.front")));
        }

        [Fact]
        public void FromRecord_AnchorWithoutOffsetByDirection_HasEmptyOffsetByDirection()
        {
            var info = Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(MapRecord(
                "\"anchor_points\": {\"hand\": {\"parent_layer\": \"layer.a\", \"offset\": {\"x\": 1, \"y\": 2}}}"));

            var anchor = info.Sprite!.AnchorPoints["hand"];
            Assert.Empty(anchor.OffsetByDirection);
            Assert.Equal(new Vec2(1, 2), anchor.Offset);
        }

        [Fact]
        public void FromRecord_AnchorOffsetByDirection_ParsedIntoDictionary()
        {
            var info = Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(MapRecord(
                "\"anchor_points\": {\"hand\": {\"parent_layer\": \"layer.a\", \"offset\": {\"x\": 1, \"y\": 2}, " +
                "\"offset_by_direction\": {\"dir.side_r\": {\"x\": 5, \"y\": 6}, \"dir.back\": {\"x\": 7, \"y\": 8}}}}"));

            var anchor = info.Sprite!.AnchorPoints["hand"];
            Assert.Equal(2, anchor.OffsetByDirection.Count);
            Assert.Equal(new Vec2(5, 6), anchor.OffsetByDirection[new Id("dir.side_r")]);
            Assert.Equal(new Vec2(7, 8), anchor.OffsetByDirection[new Id("dir.back")]);
        }

        [Fact]
        public void FromRecord_AnchorOffsetByDirectionNull_IsTreatedAsAbsent()
        {
            var info = Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(MapRecord(
                "\"anchor_points\": {\"hand\": {\"parent_layer\": \"layer.a\", \"offset\": {\"x\": 1, \"y\": 2}, \"offset_by_direction\": null}}"));

            Assert.Empty(info.Sprite!.AnchorPoints["hand"].OffsetByDirection);
        }

        [Theory]
        [InlineData("3")]
        [InlineData("{\"offset\": {\"x\": 1, \"y\": 2}}")]
        [InlineData("{\"parent_layer\": \"\", \"offset\": {\"x\": 1, \"y\": 2}}")]
        [InlineData("{\"parent_layer\": 4, \"offset\": {\"x\": 1, \"y\": 2}}")]
        [InlineData("{\"parent_layer\": \"layer.a\"}")]
        [InlineData("{\"parent_layer\": \"layer.a\", \"offset\": {\"x\": 1}}")]
        [InlineData("{\"parent_layer\": \"layer.a\", \"offset\": {\"x\": 1, \"y\": 2}, \"offset_by_direction\": []}")]
        [InlineData("{\"parent_layer\": \"layer.a\", \"offset\": {\"x\": 1, \"y\": 2}, \"offset_by_direction\": {\"BAD KEY\": {\"x\": 1, \"y\": 1}}}")]
        [InlineData("{\"parent_layer\": \"layer.a\", \"offset\": {\"x\": 1, \"y\": 2}, \"offset_by_direction\": {\"dir.back\": 3}}")]
        public void FromRecord_MalformedAnchor_ThrowsDataFieldExceptionOnAnchorPoints(string anchorJson)
        {
            var record = MapRecord("\"anchor_points\": {\"hand\": " + anchorJson + "}");

            FieldError(() => Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(record), "anchor_points");
        }

        // -----------------------------------------------------------------
        // DisplayInfo.StrideDistance
        // -----------------------------------------------------------------

        [Fact]
        public void StrideDistance_AbsentInRecord_IsNull()
        {
            Assert.Null(Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(MapRecord("")).StrideDistance);
        }

        [Theory]
        [InlineData("0.75", 0.75)]
        [InlineData("2", 2.0)]
        [InlineData("0", 0.0)]
        [InlineData("-1.5", -1.5)]
        public void StrideDistance_PresentInRecord_IsStoredVerbatim_ConsumerDecidesNonPositive(string json, double expected)
        {
            var info = Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(MapRecord("\"stride_distance\": " + json));

            Assert.Equal(expected, info.StrideDistance);
        }

        [Fact]
        public void StrideDistance_NonNumericOrNull_IsTreatedAsAbsent()
        {
            Assert.Null(Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(MapRecord("\"stride_distance\": \"far\"")).StrideDistance);
            Assert.Null(Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(MapRecord("\"stride_distance\": null")).StrideDistance);
        }

        [Fact]
        public void LegacyConstructor_LeavesStrideDistanceNull_NewOverloadCarriesIt()
        {
            var sprite = new SpriteInfo("sprite.t", 4);
            var legacy = new Core.Foundation.DisplayInfo.DisplayInfo(
                new Id("display.t"), DisplayCategory.Creature, new Id("creature.t"), DisplayKind.Sprite,
                null, null, null, 1.0, ShadowMode.Blob, 0.0, null, sprite, null);
            var withStride = new Core.Foundation.DisplayInfo.DisplayInfo(
                new Id("display.t"), DisplayCategory.Creature, new Id("creature.t"), DisplayKind.Sprite,
                null, null, null, 1.0, ShadowMode.Blob, 0.0, null, sprite, null, 1.25);

            Assert.Null(legacy.StrideDistance);
            Assert.Equal(1.25, withStride.StrideDistance);
        }

        // -----------------------------------------------------------------
        // DisplayInfo.FromRecord 其余错误路径
        // -----------------------------------------------------------------

        [Fact]
        public void FromRecord_UnknownCategoryOrShadow_ThrowsOnThatField()
        {
            var badCategory = Record("display.map",
                "{\"id\": \"display.t\", \"category\": \"monster\", \"logical_id\": \"creature.t\", \"kind\": \"sprite\"," +
                " \"sprite_set_id\": \"s\", \"direction_count\": 4}");
            FieldError(() => Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(badCategory), "category");

            FieldError(() => Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(MapRecord("\"shadow\": \"glow\"")), "shadow");
        }

        [Fact]
        public void FromRecord_AllDocumentedCategoriesAndShadowModes_Parse()
        {
            var categories = new Dictionary<string, DisplayCategory>
            {
                ["skill"] = DisplayCategory.Skill,
                ["aura"] = DisplayCategory.Aura,
                ["item"] = DisplayCategory.Item,
                ["creature"] = DisplayCategory.Creature,
                ["gobj"] = DisplayCategory.Gobj,
                ["projectile"] = DisplayCategory.Projectile,
            };
            foreach (var pair in categories)
            {
                var record = Record("display.map",
                    "{\"id\": \"display.t\", \"category\": \"" + pair.Key + "\", \"logical_id\": \"x.t\", \"kind\": \"sprite\"," +
                    " \"sprite_set_id\": \"s\", \"direction_count\": 4}");
                Assert.Equal(pair.Value, Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(record).Category);
            }

            var shadows = new Dictionary<string, ShadowMode>
            {
                ["none"] = ShadowMode.None,
                ["blob"] = ShadowMode.Blob,
                ["projected"] = ShadowMode.Projected,
            };
            foreach (var pair in shadows)
            {
                Assert.Equal(pair.Value,
                    Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(MapRecord("\"shadow\": \"" + pair.Key + "\"")).Shadow);
            }
        }

        [Fact]
        public void FromRecord_SpriteKindMissingSpriteSetId_OrDirectionCount_Throws()
        {
            var noSet = Record("display.map",
                "{\"id\": \"display.t\", \"category\": \"creature\", \"logical_id\": \"creature.t\", \"kind\": \"sprite\", \"direction_count\": 4}");
            var noCount = Record("display.map",
                "{\"id\": \"display.t\", \"category\": \"creature\", \"logical_id\": \"creature.t\", \"kind\": \"sprite\", \"sprite_set_id\": \"s\"}");

            FieldError(() => Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(noSet), "sprite_set_id");
            FieldError(() => Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(noCount), "direction_count");
        }

        [Fact]
        public void FromRecord_MalformedMirrorPairsAndPaperdollLayers_Throw()
        {
            FieldError(() => Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(MapRecord("\"mirror_pairs\": [3]")), "mirror_pairs");
            FieldError(() => Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(
                MapRecord("\"mirror_pairs\": [{\"direction_slot\": \"dir.a\"}]")), "mirror_pairs");
            FieldError(() => Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(MapRecord("\"paperdoll_layers\": [\"a\", 2]")), "paperdoll_layers");
        }

        [Fact]
        public void FromRecord_MirrorPairWithoutFlipX_DefaultsToFalse()
        {
            var info = Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(MapRecord(
                "\"mirror_pairs\": [{\"direction_slot\": \"dir.a\", \"mirror_of\": \"dir.b\"}]"));

            Assert.False(info.Sprite!.MirrorPairs[0].FlipX);
        }

        [Fact]
        public void FromRecord_ModelKind_MalformedModelFields_Throw()
        {
            string Model(string extra) =>
                "{\"id\": \"display.t\", \"category\": \"creature\", \"logical_id\": \"creature.t\", \"kind\": \"model\"," +
                " \"model_ref\": \"model.t\", \"anim_set_ref\": \"display.anim_set.t\"" + (extra.Length > 0 ? ", " + extra : "") + "}";

            FieldError(() => Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(
                Record("display.map", Model("\"default_slot_meshes\": {\"BAD KEY\": \"mesh.a\"}"))), "default_slot_meshes");
            FieldError(() => Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(
                Record("display.map", Model("\"default_slot_meshes\": {\"slot.a\": 5}"))), "default_slot_meshes");
            FieldError(() => Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(
                Record("display.map", Model("\"material_params\": {\"gloss\": \"high\"}"))), "material_params");

            var noRef = Record("display.map",
                "{\"id\": \"display.t\", \"category\": \"creature\", \"logical_id\": \"creature.t\", \"kind\": \"model\", \"anim_set_ref\": \"a.b\"}");
            FieldError(() => Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(noRef), "model_ref");
        }

        [Fact]
        public void FromRecord_ModelKind_MaterialParamsParsed()
        {
            var record = Record("display.map",
                "{\"id\": \"display.t\", \"category\": \"creature\", \"logical_id\": \"creature.t\", \"kind\": \"model\"," +
                " \"model_ref\": \"model.t\", \"anim_set_ref\": \"display.anim_set.t\", \"material_params\": {\"gloss\": 0.5}}");

            var info = Core.Foundation.DisplayInfo.DisplayInfo.FromRecord(record);

            Assert.Equal(0.5, info.Model!.MaterialParams["gloss"]);
        }
    }
}
