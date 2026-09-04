using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Foundation.DisplayInfo
{
    /// <summary>
    /// 一条 <c>display.map</c> 记录的不可变强类型视图（见 04_数据与内容管线.md 第 7.1 节全部
    /// 字段）：公共字段 + 按 <see cref="Kind"/> 二选一携带的 <see cref="Sprite"/>/<see cref="Model"/>
    /// 专属字段组。<see cref="FromRecord"/> 从 <see cref="DataRecord"/> 构造，只做"按 kind 读取
    /// 对应字段组"的解析，不做"字段组完整性"业务校验——那是
    /// <see cref="DisplayKindFieldGroupRule"/>（<see cref="IValidationRule"/> 扩展点）的职责，
    /// 二者关注点分离：<see cref="FromRecord"/> 假设传入的记录已经通过校验，缺失必填字段时
    /// 按 <see cref="DataRecord"/> 惯例抛 <see cref="DataFieldException"/>。
    /// </summary>
    public sealed class DisplayInfo
    {
        public Id Id { get; }

        public DisplayCategory Category { get; }

        /// <summary>指向的逻辑记录 id（技能/光环/物品/生物/物件）。</summary>
        public Id LogicalId { get; }

        public DisplayKind Kind { get; }

        /// <summary>UI 图标资源引用，可选。</summary>
        public string? IconId { get; }

        /// <summary>指向 <c>vfx.def</c>，可选。</summary>
        public Id? VfxId { get; }

        /// <summary>指向 <c>sfx.def</c>，可选。</summary>
        public Id? SfxId { get; }

        /// <summary>默认缩放，默认 1.0。</summary>
        public double Scale { get; }

        /// <summary>阴影呈现模式，默认 <see cref="ShadowMode.Blob"/>。</summary>
        public ShadowMode Shadow { get; }

        /// <summary>同一 sortY 下的排序微调偏移，默认 0。</summary>
        public double SortOffset { get; }

        /// <summary>指向 <c>display.weapon_style</c>，供武器类物品关联武器表现档案，可选。</summary>
        public Id? WeaponStyleRef { get; }

        /// <summary><see cref="Kind"/> 为 <see cref="DisplayKind.Sprite"/> 时非空，否则为 null。</summary>
        public SpriteInfo? Sprite { get; }

        /// <summary><see cref="Kind"/> 为 <see cref="DisplayKind.Model"/> 时非空，否则为 null。</summary>
        public ModelInfo? Model { get; }

        public DisplayInfo(
            Id id,
            DisplayCategory category,
            Id logicalId,
            DisplayKind kind,
            string? iconId,
            Id? vfxId,
            Id? sfxId,
            double scale,
            ShadowMode shadow,
            double sortOffset,
            Id? weaponStyleRef,
            SpriteInfo? sprite,
            ModelInfo? model)
        {
            Id = id;
            Category = category;
            LogicalId = logicalId;
            Kind = kind;
            IconId = iconId;
            VfxId = vfxId;
            SfxId = sfxId;
            Scale = scale;
            Shadow = shadow;
            SortOffset = sortOffset;
            WeaponStyleRef = weaponStyleRef;
            Sprite = sprite;
            Model = model;
        }

        /// <summary>从一条已加载的 <c>display.map</c> <see cref="DataRecord"/> 构造。</summary>
        public static DisplayInfo FromRecord(DataRecord record)
        {
            var id = record.GetId("id");
            var category = ParseCategory(record, record.GetString("category"));
            var logicalId = record.GetId("logical_id");
            var kind = ParseKind(record, record.GetString("kind"));

            var iconId = record.TryGetString("icon_id", out var iconVal) ? iconVal : null;
            var vfxId = record.TryGetId("vfx_id", out var vfxVal) ? (Id?)vfxVal : null;
            var sfxId = record.TryGetId("sfx_id", out var sfxVal) ? (Id?)sfxVal : null;
            var scale = record.TryGetNumber("scale", out var scaleVal) ? scaleVal : 1.0;
            var shadow = record.TryGetString("shadow", out var shadowVal) ? ParseShadow(record, shadowVal) : ShadowMode.Blob;
            var sortOffset = record.TryGetNumber("sort_offset", out var sortVal) ? sortVal : 0.0;
            var weaponStyleRef = record.TryGetId("weapon_style_ref", out var wsrVal) ? (Id?)wsrVal : null;

            var sprite = kind == DisplayKind.Sprite ? ParseSpriteInfo(record) : null;
            var model = kind == DisplayKind.Model ? ParseModelInfo(record) : null;

            return new DisplayInfo(
                id, category, logicalId, kind, iconId, vfxId, sfxId, scale, shadow, sortOffset,
                weaponStyleRef, sprite, model);
        }

        private static DisplayCategory ParseCategory(DataRecord record, string value) => value switch
        {
            "skill" => DisplayCategory.Skill,
            "aura" => DisplayCategory.Aura,
            "item" => DisplayCategory.Item,
            "creature" => DisplayCategory.Creature,
            "gobj" => DisplayCategory.Gobj,
            "projectile" => DisplayCategory.Projectile,
            _ => throw new DataFieldException(record.Table.Name, record.Key, "category", $"未知的 category 取值：\"{value}\""),
        };

        private static DisplayKind ParseKind(DataRecord record, string value) => value switch
        {
            "sprite" => DisplayKind.Sprite,
            "model" => DisplayKind.Model,
            _ => throw new DataFieldException(record.Table.Name, record.Key, "kind", $"未知的 kind 取值：\"{value}\""),
        };

        private static ShadowMode ParseShadow(DataRecord record, string value) => value switch
        {
            "none" => ShadowMode.None,
            "blob" => ShadowMode.Blob,
            "projected" => ShadowMode.Projected,
            _ => throw new DataFieldException(record.Table.Name, record.Key, "shadow", $"未知的 shadow 取值：\"{value}\""),
        };

        private static SpriteInfo ParseSpriteInfo(DataRecord record)
        {
            var spriteSetId = record.GetString("sprite_set_id");
            var directionCount = (int)record.GetInt("direction_count");

            IReadOnlyList<MirrorPair>? mirrorPairs = null;
            if (record.TryGetArray("mirror_pairs", out var mirrorArr))
            {
                var list = new List<MirrorPair>(mirrorArr.Count);
                for (var i = 0; i < mirrorArr.Count; i++)
                {
                    if (!(mirrorArr[i] is JsonObject obj))
                    {
                        throw new DataFieldException(record.Table.Name, record.Key, "mirror_pairs", $"第 {i} 个元素不是对象");
                    }

                    var directionSlot = ParseIdProperty(record, "mirror_pairs", obj, "direction_slot");
                    var mirrorOf = ParseIdProperty(record, "mirror_pairs", obj, "mirror_of");
                    var flipX = obj.TryGetValue("flip_x", out var flipVal) && flipVal is JsonBool flipBool && flipBool.Value;

                    list.Add(new MirrorPair(directionSlot, mirrorOf, flipX));
                }
                mirrorPairs = list;
            }

            IReadOnlyList<string>? paperdollLayers = null;
            if (record.TryGetArray("paperdoll_layers", out var layersArr))
            {
                var list = new List<string>(layersArr.Count);
                for (var i = 0; i < layersArr.Count; i++)
                {
                    if (!(layersArr[i] is JsonString s))
                    {
                        throw new DataFieldException(record.Table.Name, record.Key, "paperdoll_layers", $"第 {i} 个元素不是字符串");
                    }
                    list.Add(s.Value);
                }
                paperdollLayers = list;
            }

            IReadOnlyDictionary<string, Vec2>? anchorPoints = null;
            if (record.TryGetObject("anchor_points", out var anchorObj))
            {
                var dict = new Dictionary<string, Vec2>();
                foreach (var kv in anchorObj)
                {
                    dict[kv.Key] = ParseVec2(record, "anchor_points", kv.Value);
                }
                anchorPoints = dict;
            }

            return new SpriteInfo(spriteSetId, directionCount, mirrorPairs, paperdollLayers, anchorPoints);
        }

        private static ModelInfo ParseModelInfo(DataRecord record)
        {
            var modelRef = record.GetId("model_ref");
            var animSetRef = record.GetId("anim_set_ref");

            var sockets = record.TryGetIdList("sockets", out var socketsVal) ? socketsVal : null;
            var slots = record.TryGetIdList("slots", out var slotsVal) ? slotsVal : null;

            IReadOnlyDictionary<Id, Id>? defaultSlotMeshes = null;
            if (record.TryGetObject("default_slot_meshes", out var dsmObj))
            {
                var dict = new Dictionary<Id, Id>();
                foreach (var kv in dsmObj)
                {
                    if (!Id.TryParse(kv.Key, out var slotId))
                    {
                        throw new DataFieldException(record.Table.Name, record.Key, "default_slot_meshes", $"键 \"{kv.Key}\" 不是合法 Id");
                    }
                    if (!(kv.Value is JsonString meshStr) || !Id.TryParse(meshStr.Value, out var meshId))
                    {
                        throw new DataFieldException(record.Table.Name, record.Key, "default_slot_meshes", $"键 \"{kv.Key}\" 的值不是合法 Id 字符串");
                    }
                    dict[slotId] = meshId;
                }
                defaultSlotMeshes = dict;
            }

            IReadOnlyDictionary<string, double>? materialParams = null;
            if (record.TryGetObject("material_params", out var mpObj))
            {
                var dict = new Dictionary<string, double>();
                foreach (var kv in mpObj)
                {
                    if (!(kv.Value is JsonNumber num))
                    {
                        throw new DataFieldException(record.Table.Name, record.Key, "material_params", $"键 \"{kv.Key}\" 的值不是数字");
                    }
                    dict[kv.Key] = num.Value;
                }
                materialParams = dict;
            }

            return new ModelInfo(modelRef, animSetRef, sockets, slots, defaultSlotMeshes, materialParams);
        }

        private static Id ParseIdProperty(DataRecord record, string field, JsonObject obj, string property)
        {
            if (!obj.TryGetValue(property, out var value) || !(value is JsonString s) || !Id.TryParse(s.Value, out var id))
            {
                throw new DataFieldException(record.Table.Name, record.Key, field, $"元素缺少合法 Id 属性 \"{property}\"");
            }
            return id;
        }

        private static Vec2 ParseVec2(DataRecord record, string field, JsonValue value)
        {
            if (value is JsonObject o
                && o.TryGetValue("x", out var xv) && xv is JsonNumber xn
                && o.TryGetValue("y", out var yv) && yv is JsonNumber yn)
            {
                return new Vec2(xn.Value, yn.Value);
            }
            throw new DataFieldException(record.Table.Name, record.Key, field, "期望 Vec2（{\"x\": Number, \"y\": Number}）");
        }
    }
}
