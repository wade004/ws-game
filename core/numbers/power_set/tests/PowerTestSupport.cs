using System.Collections.Generic;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EventBus;

namespace Tests.Numbers.PowerSet
{
    /// <summary>测试共用帮助方法：构造登记了本模块事件 key 的 <see cref="IEventBus"/>、
    /// 从 JSON 文本直接构造 <see cref="Core.Numbers.PowerSet.PowerTypeDefinition"/>
    /// （不经完整 DataRegistry 加载管线，直接手搭 <see cref="DataRecord"/>，与
    /// data_registry 自身测试的最小化搭建方式同一惯例）。</summary>
    internal static class PowerTestSupport
    {
        public static IEventBus CreateBus()
        {
            var catalog = EventCatalog.FromDefinitions(new[]
            {
                new EventDefinition(Core.Numbers.PowerSet.PowerEventKeys.Changed, "power",
                    new[] { "unitId", "powerType", "oldValue", "newValue" }),
                new EventDefinition(Core.Numbers.PowerSet.PowerEventKeys.Depleted, "power",
                    new[] { "unitId", "powerType" }),
            });

            return new EventBus(catalog);
        }

        public static Core.Numbers.PowerSet.PowerTypeDefinition Definition(string json)
        {
            var obj = (JsonObject)JsonReader.Parse(json);
            var idString = ((JsonString)obj["id"]).Value;
            var id = new Id(idString);
            var record = new DataRecord(Core.Numbers.PowerSet.PowerSchemas.PowerType, idString, id, obj);
            return new Core.Numbers.PowerSet.PowerTypeDefinition(record);
        }

        /// <summary>固定上限资源类型：id、上限值、可选战斗内/脱战回复速率、脱战衰减速率、
        /// 脱战回满开关、是否初始为满、是否允许溢出、下限。</summary>
        public static Core.Numbers.PowerSet.PowerTypeDefinition FixedType(
            string id,
            double maxValue,
            double regenInCombat = 0,
            double regenOutOfCombat = 0,
            double decayOutOfCombat = 0,
            bool refillOnLeaveCombat = false,
            bool startFull = true,
            bool allowOverflow = false,
            double min = 0)
        {
            var json = "{"
                + "\"id\": \"" + id + "\","
                + "\"name_key\": \"l10n.power." + LastSegment(id) + ".name\","
                + "\"max_source\": {\"kind\": \"fixed\", \"value\": " + maxValue.ToString(System.Globalization.CultureInfo.InvariantCulture) + "},"
                + "\"regen_in_combat\": " + regenInCombat.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"regen_out_of_combat\": " + regenOutOfCombat.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"decay_out_of_combat\": " + decayOutOfCombat.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"refill_on_leave_combat\": " + (refillOnLeaveCombat ? "true" : "false") + ","
                + "\"start_full\": " + (startFull ? "true" : "false") + ","
                + "\"allow_overflow\": " + (allowOverflow ? "true" : "false") + ","
                + "\"min\": " + min.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "}";
            return Definition(json);
        }

        /// <summary>属性引用上限资源类型。</summary>
        public static Core.Numbers.PowerSet.PowerTypeDefinition StatType(
            string id,
            string statId,
            double regenInCombat = 0,
            double regenOutOfCombat = 0,
            double decayOutOfCombat = 0,
            bool refillOnLeaveCombat = false,
            bool startFull = true,
            bool allowOverflow = false,
            double min = 0)
        {
            var json = "{"
                + "\"id\": \"" + id + "\","
                + "\"name_key\": \"l10n.power." + LastSegment(id) + ".name\","
                + "\"max_source\": {\"kind\": \"stat\", \"stat\": \"" + statId + "\"},"
                + "\"regen_in_combat\": " + regenInCombat.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"regen_out_of_combat\": " + regenOutOfCombat.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"decay_out_of_combat\": " + decayOutOfCombat.ToString(System.Globalization.CultureInfo.InvariantCulture) + ","
                + "\"refill_on_leave_combat\": " + (refillOnLeaveCombat ? "true" : "false") + ","
                + "\"start_full\": " + (startFull ? "true" : "false") + ","
                + "\"allow_overflow\": " + (allowOverflow ? "true" : "false") + ","
                + "\"min\": " + min.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "}";
            return Definition(json);
        }

        private static string LastSegment(string id)
        {
            var idx = id.LastIndexOf('.');
            return idx < 0 ? id : id.Substring(idx + 1);
        }

        public static IReadOnlyList<Id> Ids(params string[] values)
        {
            var list = new List<Id>(values.Length);
            foreach (var v in values)
            {
                list.Add(new Id(v));
            }

            return list;
        }
    }
}
