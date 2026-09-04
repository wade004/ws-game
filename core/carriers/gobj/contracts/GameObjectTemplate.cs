using System;
using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Carriers.Gobj
{
    /// <summary>十种游戏对象类型（见 07 第 3.1 节表格行序，新增类型走 ADR，见
    /// <see cref="GobjSchemas.KindValues"/>）。</summary>
    public enum GobjKind
    {
        Door,
        Chest,
        QuestObject,
        Trap,
        SpellFocus,
        GatherNode,
        Teleporter,
        SavePoint,
        Lever,
        Sign,
    }

    /// <summary><see cref="GobjKind"/> 与数据表 snake_case 文本互转（惯例同
    /// <c>core/rules/common</c> 的 <c>EffectKindNames</c>）。</summary>
    public static class GobjKindNames
    {
        private static readonly IReadOnlyDictionary<GobjKind, string> ToName = new Dictionary<GobjKind, string>
        {
            [GobjKind.Door] = "door",
            [GobjKind.Chest] = "chest",
            [GobjKind.QuestObject] = "quest_object",
            [GobjKind.Trap] = "trap",
            [GobjKind.SpellFocus] = "spell_focus",
            [GobjKind.GatherNode] = "gather_node",
            [GobjKind.Teleporter] = "teleporter",
            [GobjKind.SavePoint] = "save_point",
            [GobjKind.Lever] = "lever",
            [GobjKind.Sign] = "sign",
        };

        private static readonly IReadOnlyDictionary<string, GobjKind> FromName = BuildReverse(ToName);

        private static Dictionary<string, GobjKind> BuildReverse(IReadOnlyDictionary<GobjKind, string> map)
        {
            var reverse = new Dictionary<string, GobjKind>(StringComparer.Ordinal);
            foreach (var pair in map)
            {
                reverse[pair.Value] = pair.Key;
            }

            return reverse;
        }

        public static string ToText(GobjKind kind) => ToName[kind];

        public static GobjKind Parse(string text)
        {
            if (TryParse(text, out var kind))
            {
                return kind;
            }

            throw new ArgumentException($"未知的 GobjKind 文本：\"{text ?? "<null>"}\"", nameof(text));
        }

        public static bool TryParse(string? text, out GobjKind kind)
        {
            if (text != null && FromName.TryGetValue(text, out kind))
            {
                return true;
            }

            kind = default;
            return false;
        }
    }

    // -----------------------------------------------------------------
    // 十种类型各自的类型数据（见 07 第 3.1 节表格"类型数据要点"列、任务拍板的具体字段形状）
    // -----------------------------------------------------------------

    /// <summary><c>door</c> 类型数据：<c>{lock_id?}</c>。判断记录：本字段与 <c>gobj.template.lock_id</c>
    /// （顶层字段，见 05 第 1.3 节 <c>GameObject.lockId</c>）语义重叠——<see cref="GameObjectHost"/>
    /// 的开锁/交互逻辑一律读顶层 <c>lock_id</c>（<see cref="GameObjectEntity.LockId"/>），本字段只是
    /// 07 表格给出的"类型数据要点"里提到的信息，为内容作者提供的可读性冗余，不参与运行期逻辑，
    /// 见 schema/README.md"判断记录"。</summary>
    public readonly struct DoorTypeData
    {
        public Id? LockId { get; }

        public DoorTypeData(Id? lockId)
        {
            LockId = lockId;
        }
    }

    /// <summary><c>chest</c> 类型数据：<c>{loot_table_ref, lock_id?}</c>（<c>lock_id</c> 冗余说明同
    /// <see cref="DoorTypeData"/>）。</summary>
    public readonly struct ChestTypeData
    {
        public Id LootTableRef { get; }

        public Id? LockId { get; }

        public ChestTypeData(Id lootTableRef, Id? lockId)
        {
            LootTableRef = lootTableRef;
            LockId = lockId;
        }
    }

    /// <summary><c>quest_object</c> 类型数据：<c>{quest_action_ref}</c>。</summary>
    public readonly struct QuestObjectTypeData
    {
        public Id QuestActionRef { get; }

        public QuestObjectTypeData(Id questActionRef)
        {
            QuestActionRef = questActionRef;
        }
    }

    /// <summary><c>trap</c> 类型数据：<c>{skill_id, trigger_shape}</c>。<see cref="TriggerShape"/> 是
    /// 一个未深入解析的 <see cref="JsonObject"/>（05 第 3.5 节 <c>Shape</c> 的 JSON 形状由触发体/
    /// 目标形状相关模块定义，不属于本模块解析范围，见 schema/README.md"契约缺口"）：本模块只负责
    /// 存储原样透传给 L4 区域触发装配的调用方。</summary>
    public readonly struct TrapTypeData
    {
        public Id SkillId { get; }

        public JsonObject TriggerShape { get; }

        public TrapTypeData(Id skillId, JsonObject triggerShape)
        {
            SkillId = skillId;
            TriggerShape = triggerShape;
        }
    }

    /// <summary><c>spell_focus</c> 类型数据：<c>{required_skill_tag}</c>。</summary>
    public readonly struct SpellFocusTypeData
    {
        public Id RequiredSkillTag { get; }

        public SpellFocusTypeData(Id requiredSkillTag)
        {
            RequiredSkillTag = requiredSkillTag;
        }
    }

    /// <summary><c>gather_node</c> 类型数据：<c>{loot_table_ref, respawn_after_use}</c>。</summary>
    public readonly struct GatherNodeTypeData
    {
        public Id LootTableRef { get; }

        public double RespawnAfterUse { get; }

        public GatherNodeTypeData(Id lootTableRef, double respawnAfterUse)
        {
            LootTableRef = lootTableRef;
            RespawnAfterUse = respawnAfterUse;
        }
    }

    /// <summary><c>teleporter</c> 类型数据：<c>{teleport_target_ref}</c>。</summary>
    public readonly struct TeleporterTypeData
    {
        public Id TeleportTargetRef { get; }

        public TeleporterTypeData(Id teleportTargetRef)
        {
            TeleportTargetRef = teleportTargetRef;
        }
    }

    /// <summary><c>lever</c> 类型数据：<c>{linked_object_ids}</c>。</summary>
    public readonly struct LeverTypeData
    {
        public IReadOnlyList<Id> LinkedObjectIds { get; }

        public LeverTypeData(IReadOnlyList<Id> linkedObjectIds)
        {
            LinkedObjectIds = linkedObjectIds;
        }
    }

    /// <summary><c>sign</c> 类型数据：<c>{text_key}</c>。判断记录：<see cref="TextKey"/> 存为
    /// <see cref="string"/> 而非 <see cref="FieldKind.TextKey"/> 强类型校验——本字段嵌在
    /// <c>type_data</c> Object 内，<see cref="DataRecord"/> 的内建 <c>text_key_exists</c> 校验只覆盖
    /// 表顶层字段（见 <see cref="FieldSchema"/> 声明位置），嵌套字段的文本键存在性校验不在本模块范围
    /// （见 schema/README.md"契约缺口"）。</summary>
    public readonly struct SignTypeData
    {
        public string TextKey { get; }

        public SignTypeData(string textKey)
        {
            TextKey = textKey ?? throw new ArgumentNullException(nameof(textKey));
        }
    }

    /// <summary>
    /// 按 <see cref="GameObjectTemplate.Kind"/> 二选一携带的类型数据容器（惯例同
    /// <c>Core.Foundation.DisplayInfo.DisplayInfo</c> 的 <c>Sprite</c>/<c>Model</c> 写法）：只有
    /// 对应 <see cref="GobjKind"/> 的那个属性非 null，其余为 null。<see cref="SavePoint"/>/
    /// <see cref="Sign"/>/<see cref="SpellFocus"/>/<see cref="QuestObject"/> 等无字段或单字段类型
    /// 同样各自建了结构体（即使 <c>save_point</c> 无字段）以保持"按 kind 索引到对应结构体"这一
    /// 统一访问方式，不搞"部分类型直接查 type_data 原始 JSON、部分类型有强类型包装"的不一致。
    /// </summary>
    public sealed class GobjTypeData
    {
        public DoorTypeData? Door { get; }

        public ChestTypeData? Chest { get; }

        public QuestObjectTypeData? QuestObject { get; }

        public TrapTypeData? Trap { get; }

        public SpellFocusTypeData? SpellFocus { get; }

        public GatherNodeTypeData? GatherNode { get; }

        public TeleporterTypeData? Teleporter { get; }

        public bool IsSavePoint { get; }

        public LeverTypeData? Lever { get; }

        public SignTypeData? Sign { get; }

        private GobjTypeData(
            DoorTypeData? door, ChestTypeData? chest, QuestObjectTypeData? questObject, TrapTypeData? trap,
            SpellFocusTypeData? spellFocus, GatherNodeTypeData? gatherNode, TeleporterTypeData? teleporter,
            bool isSavePoint, LeverTypeData? lever, SignTypeData? sign)
        {
            Door = door;
            Chest = chest;
            QuestObject = questObject;
            Trap = trap;
            SpellFocus = spellFocus;
            GatherNode = gatherNode;
            Teleporter = teleporter;
            IsSavePoint = isSavePoint;
            Lever = lever;
            Sign = sign;
        }

        public static GobjTypeData ForDoor(DoorTypeData data) =>
            new GobjTypeData(data, null, null, null, null, null, null, false, null, null);

        public static GobjTypeData ForChest(ChestTypeData data) =>
            new GobjTypeData(null, data, null, null, null, null, null, false, null, null);

        public static GobjTypeData ForQuestObject(QuestObjectTypeData data) =>
            new GobjTypeData(null, null, data, null, null, null, null, false, null, null);

        public static GobjTypeData ForTrap(TrapTypeData data) =>
            new GobjTypeData(null, null, null, data, null, null, null, false, null, null);

        public static GobjTypeData ForSpellFocus(SpellFocusTypeData data) =>
            new GobjTypeData(null, null, null, null, data, null, null, false, null, null);

        public static GobjTypeData ForGatherNode(GatherNodeTypeData data) =>
            new GobjTypeData(null, null, null, null, null, data, null, false, null, null);

        public static GobjTypeData ForTeleporter(TeleporterTypeData data) =>
            new GobjTypeData(null, null, null, null, null, null, data, false, null, null);

        public static GobjTypeData ForSavePoint() =>
            new GobjTypeData(null, null, null, null, null, null, null, true, null, null);

        public static GobjTypeData ForLever(LeverTypeData data) =>
            new GobjTypeData(null, null, null, null, null, null, null, false, data, null);

        public static GobjTypeData ForSign(SignTypeData data) =>
            new GobjTypeData(null, null, null, null, null, null, null, false, null, data);
    }

    /// <summary><c>gobj.template.on_use</c> 二选一（见 07 第 3.3 节）。</summary>
    public enum OnUseKind
    {
        Skill,
        Dialog,
    }

    /// <summary>解析后的 <c>on_use</c>：<c>{kind, ref}</c>。</summary>
    public readonly struct OnUseRef
    {
        public OnUseKind Kind { get; }

        public Id Ref { get; }

        public OnUseRef(OnUseKind kind, Id @ref)
        {
            Kind = kind;
            Ref = @ref;
        }
    }

    /// <summary>解析后的 <c>gobj.template</c> 记录（字段见 07 第 3.1/3.2/3.3 节、schema/README.md）。
    /// <see cref="FromRecord"/> 假设传入的记录已经通过 <see cref="GobjValidationRules"/> 校验，只做
    /// "按 kind 读取对应字段组"的解析，不重复做字段组完整性业务校验（惯例同
    /// <c>Core.Foundation.DisplayInfo.DisplayInfo.FromRecord</c>）。</summary>
    public sealed class GameObjectTemplate
    {
        public Id Id { get; }

        public Id NameKey { get; }

        public GobjKind Kind { get; }

        public GobjTypeData TypeData { get; }

        /// <summary>见 05 第 1.3 节 <c>GameObject.lockId</c>：运行期实际参与开锁/交互判定的锁引用是
        /// <see cref="GameObjectEntity.LockId"/>（可在生成实例时按模板此值初始化，也可被手工放置覆盖），
        /// 本字段是模板给出的默认值。</summary>
        public Id? LockId { get; }

        public OnUseRef? OnUse { get; }

        public Id DisplayRef { get; }

        public IReadOnlyList<Id> Tags { get; }

        public GameObjectTemplate(
            Id id, Id nameKey, GobjKind kind, GobjTypeData typeData, Id? lockId, OnUseRef? onUse,
            Id displayRef, IReadOnlyList<Id> tags)
        {
            Id = id;
            NameKey = nameKey;
            Kind = kind;
            TypeData = typeData ?? throw new ArgumentNullException(nameof(typeData));
            LockId = lockId;
            OnUse = onUse;
            DisplayRef = displayRef;
            Tags = tags;
        }

        public static GameObjectTemplate FromRecord(DataRecord record)
        {
            var id = record.GetId("id");
            var nameKey = record.GetId("name_key");
            var kind = GobjKindNames.Parse(record.GetString("kind"));
            var typeDataObj = record.GetObject("type_data");
            var typeData = ParseTypeData(record, kind, typeDataObj);

            var lockId = record.TryGetId("lock_id", out var lockVal) ? (Id?)lockVal : null;

            OnUseRef? onUse = null;
            if (record.TryGetObject("on_use", out var onUseObj))
            {
                onUse = ParseOnUse(record, onUseObj);
            }

            var displayRef = record.GetId("display_ref");
            var tags = record.TryGetIdList("tags", out var tagsVal) ? tagsVal : Array.Empty<Id>();

            return new GameObjectTemplate(id, nameKey, kind, typeData, lockId, onUse, displayRef, tags);
        }

        private static GobjTypeData ParseTypeData(DataRecord record, GobjKind kind, JsonObject obj)
        {
            switch (kind)
            {
                case GobjKind.Door:
                    return GobjTypeData.ForDoor(new DoorTypeData(GetOptionalId(record, obj, "lock_id")));

                case GobjKind.Chest:
                    return GobjTypeData.ForChest(new ChestTypeData(
                        GetRequiredId(record, obj, "loot_table_ref"),
                        GetOptionalId(record, obj, "lock_id")));

                case GobjKind.QuestObject:
                    return GobjTypeData.ForQuestObject(new QuestObjectTypeData(
                        GetRequiredId(record, obj, "quest_action_ref")));

                case GobjKind.Trap:
                    return GobjTypeData.ForTrap(new TrapTypeData(
                        GetRequiredId(record, obj, "skill_id"),
                        GetRequiredObject(record, obj, "trigger_shape")));

                case GobjKind.SpellFocus:
                    return GobjTypeData.ForSpellFocus(new SpellFocusTypeData(
                        GetRequiredId(record, obj, "required_skill_tag")));

                case GobjKind.GatherNode:
                    return GobjTypeData.ForGatherNode(new GatherNodeTypeData(
                        GetRequiredId(record, obj, "loot_table_ref"),
                        GetRequiredNumber(record, obj, "respawn_after_use")));

                case GobjKind.Teleporter:
                    return GobjTypeData.ForTeleporter(new TeleporterTypeData(
                        GetRequiredId(record, obj, "teleport_target_ref")));

                case GobjKind.SavePoint:
                    return GobjTypeData.ForSavePoint();

                case GobjKind.Lever:
                    return GobjTypeData.ForLever(new LeverTypeData(GetRequiredIdList(record, obj, "linked_object_ids")));

                case GobjKind.Sign:
                    return GobjTypeData.ForSign(new SignTypeData(GetRequiredString(record, obj, "text_key")));

                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知 GobjKind");
            }
        }

        private static OnUseRef ParseOnUse(DataRecord record, JsonObject obj)
        {
            if (!obj.TryGetValue("kind", out var kindVal) || !(kindVal is JsonString kindStr))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "on_use", "缺少合法字符串字段 \"kind\"");
            }

            var kind = kindStr.Value switch
            {
                "skill" => OnUseKind.Skill,
                "dialog" => OnUseKind.Dialog,
                _ => throw new DataFieldException(record.Table.Name, record.Key, "on_use", $"未知的 on_use.kind 取值：\"{kindStr.Value}\""),
            };

            var @ref = RequireIdProperty(record, "on_use", obj, "ref");
            return new OnUseRef(kind, @ref);
        }

        private static Id GetRequiredId(DataRecord record, JsonObject obj, string property) =>
            RequireIdProperty(record, "type_data", obj, property);

        private static Id? GetOptionalId(DataRecord record, JsonObject obj, string property)
        {
            if (!obj.TryGetValue(property, out var value) || value.Kind == JsonKind.Null)
            {
                return null;
            }

            return RequireIdProperty(record, "type_data", obj, property);
        }

        private static double GetRequiredNumber(DataRecord record, JsonObject obj, string property)
        {
            if (obj.TryGetValue(property, out var value) && value is JsonNumber n)
            {
                return n.Value;
            }

            throw new DataFieldException(record.Table.Name, record.Key, "type_data", $"缺少合法数字字段 \"{property}\"");
        }

        private static string GetRequiredString(DataRecord record, JsonObject obj, string property)
        {
            if (obj.TryGetValue(property, out var value) && value is JsonString s)
            {
                return s.Value;
            }

            throw new DataFieldException(record.Table.Name, record.Key, "type_data", $"缺少合法字符串字段 \"{property}\"");
        }

        private static JsonObject GetRequiredObject(DataRecord record, JsonObject obj, string property)
        {
            if (obj.TryGetValue(property, out var value) && value is JsonObject o)
            {
                return o;
            }

            throw new DataFieldException(record.Table.Name, record.Key, "type_data", $"缺少合法对象字段 \"{property}\"");
        }

        private static IReadOnlyList<Id> GetRequiredIdList(DataRecord record, JsonObject obj, string property)
        {
            if (!obj.TryGetValue(property, out var value) || !(value is JsonArray arr))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "type_data", $"缺少合法 Id 数组字段 \"{property}\"");
            }

            var list = new List<Id>(arr.Count);
            for (var i = 0; i < arr.Count; i++)
            {
                if (!(arr[i] is JsonString s) || !Id.TryParse(s.Value, out var id))
                {
                    throw new DataFieldException(record.Table.Name, record.Key, "type_data", $"字段 \"{property}\" 第 {i} 个元素不是合法 Id 字符串");
                }

                list.Add(id);
            }

            return list;
        }

        private static Id RequireIdProperty(DataRecord record, string field, JsonObject obj, string property)
        {
            if (!obj.TryGetValue(property, out var value) || !(value is JsonString s) || !Id.TryParse(s.Value, out var id))
            {
                throw new DataFieldException(record.Table.Name, record.Key, field, $"缺少合法 Id 字段 \"{property}\"");
            }

            return id;
        }
    }
}
