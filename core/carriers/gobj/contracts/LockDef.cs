using Core.Carriers.Common;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.Expr;

namespace Core.Carriers.Gobj
{
    /// <summary>解析后的 <c>gobj.lock</c> 记录（见 07 第 3.2 节 <c>LockDef</c>、schema/README.md）。
    /// <see cref="Requirement"/> 复用 <c>core/carriers/common</c> 已声明的
    /// <see cref="LockRequirement"/> 判别联合，不重复定义一套等价类型。</summary>
    public sealed class LockDef
    {
        public Id Id { get; }

        public LockRequirement Requirement { get; }

        /// <summary><see cref="LockRequirementKind.ItemKey"/> 时是否消耗钥匙，缺省 false（见 07 第
        /// 3.2 节字段表）。</summary>
        public bool ConsumeKey { get; }

        public LockDef(Id id, LockRequirement requirement, bool consumeKey)
        {
            Id = id;
            Requirement = requirement;
            ConsumeKey = consumeKey;
        }

        /// <summary><see cref="FromRecord"/> 假设传入的记录已经通过
        /// <see cref="GobjLockRequirementFieldGroupRule"/> 校验，只做"按 requirement.kind 读取对应
        /// 字段组"的解析。</summary>
        public static LockDef FromRecord(DataRecord record)
        {
            var id = record.GetId("id");
            var requirementObj = record.GetObject("requirement");
            var consumeKey = record.TryGetBool("consume_key", out var consumeKeyVal) && consumeKeyVal;

            var requirement = ParseRequirement(record, requirementObj);
            return new LockDef(id, requirement, consumeKey);
        }

        private static LockRequirement ParseRequirement(DataRecord record, JsonObject obj)
        {
            var kindText = RequireString(record, obj, "kind");
            switch (kindText)
            {
                case "item_key":
                    return LockRequirement.ItemKey(RequireId(record, obj, "item_id"));

                case "world_flag":
                    var flagKey = RequireId(record, obj, "flag_key");
                    var expected = RequireExprValue(record, obj, "expected");
                    return LockRequirement.WorldFlag(flagKey, expected);

                case "skill_check":
                    var skillTag = RequireId(record, obj, "skill_tag");
                    var minValue = RequireNumber(record, obj, "min_value");
                    return LockRequirement.SkillCheck(skillTag, minValue);

                default:
                    throw new DataFieldException(record.Table.Name, record.Key, "requirement", $"未知的 requirement.kind 取值：\"{kindText}\"");
            }
        }

        private static string RequireString(DataRecord record, JsonObject obj, string property)
        {
            if (obj.TryGetValue(property, out var value) && value is JsonString s)
            {
                return s.Value;
            }

            throw new DataFieldException(record.Table.Name, record.Key, "requirement", $"缺少合法字符串字段 \"{property}\"");
        }

        private static Id RequireId(DataRecord record, JsonObject obj, string property)
        {
            if (obj.TryGetValue(property, out var value) && value is JsonString s && Id.TryParse(s.Value, out var id))
            {
                return id;
            }

            throw new DataFieldException(record.Table.Name, record.Key, "requirement", $"缺少合法 Id 字段 \"{property}\"");
        }

        private static double RequireNumber(DataRecord record, JsonObject obj, string property)
        {
            if (obj.TryGetValue(property, out var value) && value is JsonNumber n)
            {
                return n.Value;
            }

            throw new DataFieldException(record.Table.Name, record.Key, "requirement", $"缺少合法数字字段 \"{property}\"");
        }

        /// <summary><c>expected</c> 按 <see cref="IWorldFlags"/> 顶部判断记录统一用
        /// <see cref="ExprValue"/> 承载，取值为 <c>Bool｜Int</c>（见 10 第 2.3 节
        /// <c>world_state_flags</c>）；<c>world_flag</c> requirement 的 <c>expected</c> 字段照此收窄。</summary>
        private static ExprValue RequireExprValue(DataRecord record, JsonObject obj, string property)
        {
            if (!obj.TryGetValue(property, out var value))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "requirement", $"缺少字段 \"{property}\"");
            }

            switch (value)
            {
                case JsonBool b: return ExprValue.OfBool(b.Value);
                case JsonNumber n when n.TryGetInt64(out var i): return ExprValue.OfInt(i);
                case JsonNumber n: return ExprValue.OfNumber(n.Value);
                default:
                    throw new DataFieldException(record.Table.Name, record.Key, "requirement", $"字段 \"{property}\" 期望 Bool 或 Int（world_state_flags 取值范围）");
            }
        }
    }
}
