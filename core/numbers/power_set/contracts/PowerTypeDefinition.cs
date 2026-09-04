using System;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;

namespace Core.Numbers.PowerSet
{
    /// <summary>上限来源种类（见 06 第 2.1 节"上限来源：固定值或引用属性"）。</summary>
    public enum PowerMaxSourceKind
    {
        /// <summary>固定数值上限。</summary>
        Fixed,

        /// <summary>上限等于某条属性的当前值，经构造期注入的 <see cref="StatLookup"/> 查询
        /// （本模块不引用 <c>stat_block</c> 模块类型，见本模块 README"与 stat_block 的关系"）。</summary>
        Stat,
    }

    /// <summary>
    /// 一条 <c>arch.power_type</c> 记录的强类型视图（见 06 第 2.1 节、
    /// schema/README.md 字段表）。从 <see cref="DataRecord"/> 构造，构造期完成全部字段解析
    /// 与合法性检查，非法数据在构造期即抛 <see cref="DataFieldException"/>（呼应 11 第 4 节
    /// "数据引用不存在...加载期校验拦截，不允许进入运行时"，这里对应"字段值不合法"同一处理
    /// 时机）。
    /// </summary>
    public sealed class PowerTypeDefinition
    {
        /// <summary>资源类型 id，格式 <c>arch.power.&lt;name&gt;</c>。</summary>
        public Id Id { get; }

        /// <summary>显示名文本键。</summary>
        public Id NameKey { get; }

        /// <summary>上限来源种类。</summary>
        public PowerMaxSourceKind MaxSourceKind { get; }

        /// <summary><see cref="MaxSourceKind"/> 为 <see cref="PowerMaxSourceKind.Fixed"/> 时的固定上限值；
        /// 为 <see cref="PowerMaxSourceKind.Stat"/> 时恒为 0，无意义。</summary>
        public double MaxFixedValue { get; }

        /// <summary><see cref="MaxSourceKind"/> 为 <see cref="PowerMaxSourceKind.Stat"/> 时引用的属性 id；
        /// 为 <see cref="PowerMaxSourceKind.Fixed"/> 时为 null。</summary>
        public Id? MaxStat { get; }

        /// <summary>战斗内每时间单位回复量（以数据集声明的时间单位计，见 04 第 3.1 节），默认 0。</summary>
        public double RegenInCombat { get; }

        /// <summary>脱战每时间单位回复量，默认 0。</summary>
        public double RegenOutOfCombat { get; }

        /// <summary>脱战每时间单位衰减量，默认 0；只在脱战状态下生效（见 06 第 4.5 节）。</summary>
        public double DecayOutOfCombat { get; }

        /// <summary>脱战瞬间是否立即回满（见 06 第 4.5 节"资源回复规则切换（如脱战自动回满）"），默认 false。</summary>
        public bool RefillOnLeaveCombat { get; }

        /// <summary>单位注册时资源是否初始为满（否则初始为 <see cref="Min"/>），默认 true。</summary>
        public bool StartFull { get; }

        /// <summary>是否允许超出上限（<see cref="IPowerHost.ModifyPower"/> 的正向增量不再夹取到 <see cref="PowerHost"/> 记录的上限），默认 false。</summary>
        public bool AllowOverflow { get; }

        /// <summary>下限，默认 0。</summary>
        public double Min { get; }

        public PowerTypeDefinition(DataRecord record)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            Id = record.GetId("id");
            NameKey = record.GetId("name_key");

            var maxSource = record.GetObject("max_source");
            if (!maxSource.TryGetValue("kind", out var kindValue) || !(kindValue is JsonString kindString))
            {
                throw new DataFieldException(record.Table.Name, record.Key, "max_source.kind",
                    "期望字符串，取值 \"fixed\" 或 \"stat\"");
            }

            switch (kindString.Value)
            {
                case "fixed":
                    MaxSourceKind = PowerMaxSourceKind.Fixed;
                    if (!maxSource.TryGetValue("value", out var valueValue) || !(valueValue is JsonNumber valueNumber))
                    {
                        throw new DataFieldException(record.Table.Name, record.Key, "max_source.value",
                            "max_source.kind=\"fixed\" 时必须提供 Number 类型的 value");
                    }

                    MaxFixedValue = valueNumber.Value;
                    MaxStat = null;
                    break;

                case "stat":
                    MaxSourceKind = PowerMaxSourceKind.Stat;
                    if (!maxSource.TryGetValue("stat", out var statValue) || !(statValue is JsonString statString)
                        || !CommonIdTryParse(statString.Value, out var statId))
                    {
                        throw new DataFieldException(record.Table.Name, record.Key, "max_source.stat",
                            "max_source.kind=\"stat\" 时必须提供合法 Id 格式字符串类型的 stat");
                    }

                    MaxStat = statId;
                    MaxFixedValue = 0;
                    break;

                default:
                    throw new DataFieldException(record.Table.Name, record.Key, "max_source.kind",
                        $"非法取值 \"{kindString.Value}\"，只允许 \"fixed\" 或 \"stat\"");
            }

            RegenInCombat = GetNumberOrDefault(record, "regen_in_combat", 0.0);
            RegenOutOfCombat = GetNumberOrDefault(record, "regen_out_of_combat", 0.0);
            DecayOutOfCombat = GetNumberOrDefault(record, "decay_out_of_combat", 0.0);
            RefillOnLeaveCombat = GetBoolOrDefault(record, "refill_on_leave_combat", false);
            StartFull = GetBoolOrDefault(record, "start_full", true);
            AllowOverflow = GetBoolOrDefault(record, "allow_overflow", false);
            Min = GetNumberOrDefault(record, "min", 0.0);

            if (RegenInCombat < 0 || RegenOutOfCombat < 0 || DecayOutOfCombat < 0)
            {
                throw new DataFieldException(record.Table.Name, record.Key, "regen_in_combat/regen_out_of_combat/decay_out_of_combat",
                    "回复/衰减速率不能为负数");
            }
        }

        private static bool CommonIdTryParse(string value, out Id id) => Id.TryParse(value, out id);

        private static double GetNumberOrDefault(DataRecord record, string field, double defaultValue) =>
            record.Has(field) ? record.GetNumber(field) : defaultValue;

        private static bool GetBoolOrDefault(DataRecord record, string field, bool defaultValue) =>
            record.Has(field) ? record.GetBool(field) : defaultValue;
    }
}
