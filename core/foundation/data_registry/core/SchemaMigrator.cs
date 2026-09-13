using System;
using System.Collections.Generic;
using Core.Foundation.Common.Json;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// 消费方反馈第 40 条（2026-09-13，见
    /// <c>architecture/落地计划/消费方反馈-2026-09-13-编辑器-第40条.md</c>）：把此前只服务于
    /// <see cref="DataRegistry.LoadOneTablePartial"/> 内部加载流程的"迁移链串接"与"逐行迁移"逻辑收口为
    /// 公开、单一来源的静态入口，并新增"整表迁移"（信封级、供写回磁盘前调用）——内容工具（如编辑器"执行
    /// 迁移并写回"）以本类为权威实现，不必自行复刻链选取语义（04 第 3 节"迁移链只增不减"），框架未来调整
    /// 该算法细节时消费方能自动感知，不会静默漂移。
    /// <para>
    /// 判断记录（三个方法各自的失败模式不同，不共用一套异常/错误汇报约定）：<see cref="BuildChain"/>/
    /// <see cref="MigrateRow"/> 是纯函数式的底层构件，参数非法直接抛异常（<see cref="ArgumentNullException"/>/
    /// <see cref="ArgumentOutOfRangeException"/>），链缺失环节用返回 <c>null</c> 表达（供调用方在写回前
    /// 自行决定如何处理，不强加"缺链即异常"的语义——<see cref="DataRegistry"/> 加载期把 <c>null</c> 转成
    /// 一条 <see cref="ValidationIssue"/>，<see cref="MigrateEnvelope"/> 则转成
    /// <see cref="InvalidOperationException"/>，两种消费方各自更合适的表达方式）。<see cref="MigrateEnvelope"/>
    /// 是给内容工具直接调用的高层入口，不经过 <see cref="DataRegistry"/> 的 <see cref="ValidationIssue"/>
    /// 汇报机制（工具侧没有一个进行中的校验报告可以挂问题），改用异常表达全部输入错误。
    /// </para>
    /// </summary>
    public static class SchemaMigrator
    {
        /// <summary>
        /// 串出从 <paramref name="fromVersion"/> 到 <paramref name="toVersion"/> 的完整迁移链：从
        /// <paramref name="fromVersion"/> 起，每一步在 <see cref="TableSchema.Migrations"/> 中取第一条
        /// <see cref="TableMigration.FromVersion"/> 等于当前版本的环节，串到该环节的
        /// <see cref="TableMigration.ToVersion"/>，如此反复直至精确到达 <paramref name="toVersion"/>。
        /// 途中某一步找不到衔接当前版本的环节，或链条最终越过目标版本，均返回 <c>null</c>（"缺少迁移
        /// 环节"，由调用方决定如何汇报）。<paramref name="fromVersion"/> 等于 <paramref name="toVersion"/>
        /// 时直接返回空链（不是 <c>null</c>——版本已经是目标版本，不需要任何迁移步骤，这不是一种失败）。
        /// <para>
        /// 与 <see cref="DataRegistry"/> 加载期此前的私有实现逐字一致——该实现已改为委托本方法（保证
        /// "迁移链串接"只有一份实现），见 <see cref="DataRegistry.LoadOneTablePartial"/> 判断记录。
        /// </para>
        /// <para>
        /// 判断记录（两条参数守卫是新入口独有的，不影响加载器路径）：<see cref="DataRegistry"/> 自身调用
        /// 本方法时，<c>fromVersion</c>/<c>toVersion</c> 恒满足 <c>fromVersion &gt;= 1</c>（信封
        /// <c>schema_version</c> 已在 <see cref="DataRegistry.LoadOneTablePartial"/> 更早处校验过）与
        /// <c>toVersion &gt;= fromVersion</c>（只在 <c>schemaVersion &lt; schema.CurrentSchemaVersion</c>
        /// 时才调用），因此这两条守卫在加载器路径上永远不会触发；它们是为本方法作为独立公开入口被任意
        /// 调用方直接调用时新增的输入校验，不改变加载器的既有行为/报错文案。
        /// </para>
        /// </summary>
        public static IReadOnlyList<TableMigration>? BuildChain(TableSchema schema, int fromVersion, int toVersion)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            if (fromVersion < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(fromVersion), fromVersion, "fromVersion 必须 >= 1");
            }
            if (toVersion < fromVersion)
            {
                throw new ArgumentOutOfRangeException(nameof(toVersion), toVersion, "toVersion 必须 >= fromVersion");
            }

            var chain = new List<TableMigration>();
            var current = fromVersion;
            while (current < toVersion)
            {
                TableMigration? step = null;
                foreach (var m in schema.Migrations)
                {
                    if (m.FromVersion == current)
                    {
                        step = m;
                        break;
                    }
                }
                if (step == null) return null;
                chain.Add(step);
                current = step.ToVersion;
            }
            return current == toVersion ? chain : null;
        }

        /// <summary>按 <paramref name="chain"/> 顺序对单条记录依次应用迁移，与
        /// <see cref="DataRegistry.LoadOneTablePartial"/> 加载期的行内循环逐字一致（该处已改为委托本
        /// 方法）。<paramref name="chain"/> 为空时原样返回 <paramref name="row"/>。</summary>
        public static JsonObject MigrateRow(IReadOnlyList<TableMigration> chain, JsonObject row)
        {
            if (chain == null) throw new ArgumentNullException(nameof(chain));
            if (row == null) throw new ArgumentNullException(nameof(row));

            var current = row;
            for (var i = 0; i < chain.Count; i++)
            {
                current = chain[i].Migrate(current);
            }
            return current;
        }

        /// <summary>
        /// 整表迁移的权威实现（消费方反馈第 40 条"建议"一节）：输入 <c>data/README.md</c>"文件顶层
        /// 信封"形态（<c>table</c>/<c>schema_version</c>/<c>rows</c>），供内容工具在把迁移结果写回磁盘
        /// 前调用——内容工具不必自行实现"串链 + 逐行迁移 + 改写信封 schema_version/migrated_from"这套
        /// 组合逻辑。
        /// <para>
        /// 规则：<paramref name="envelope"/> 的 <c>schema_version</c> 缺失/非法、<c>rows</c> 非数组、或
        /// <c>schema_version</c> 超过 <paramref name="schema"/> 的 <see cref="TableSchema.CurrentSchemaVersion"/>，
        /// 均抛 <see cref="ArgumentException"/>（消息说明具体原因；本方法是工具侧直接调用的入口，不产出
        /// <see cref="ValidationIssue"/>，那是 <see cref="DataRegistry"/> 加载期报告的机制）。
        /// <c>schema_version</c> 已等于当前版本时原样返回同一个 <paramref name="envelope"/> 实例（无需
        /// 迁移）。否则调用 <see cref="BuildChain"/>；返回 <c>null</c>（缺少迁移环节）时抛
        /// <see cref="InvalidOperationException"/>。<c>rows</c> 中出现非对象元素时抛
        /// <see cref="ArgumentException"/>（工具侧应先修好数据，不静默跳过——与
        /// <see cref="DataRegistry"/> 加载期"记一条 Error 问题、跳过该行、继续处理其余行"的宽容策略不同，
        /// 那是校验器"尽量报全部问题"的既有惯例，本方法是单次写回操作，没有"部分成功"的中间态）。
        /// </para>
        /// <para>
        /// 迁移成功时返回一个<b>新的</b> <see cref="JsonObject"/>：保留原信封其它键与键序，
        /// <c>schema_version</c> 改写为当前版本，写入/改写 <c>migrated_from</c> 为"最早的原始版本"——若
        /// <paramref name="envelope"/> 已带合法 <c>migrated_from</c>（<c>[1, 原 schema_version - 1]</c>
        /// 范围内的整数，说明此前已迁移过一次），保留那个更早的值，不用本次迁移前的 <c>schema_version</c>
        /// 覆盖它（否则多次增量迁移会逐次丢失最早的原始版本号，"记录原始版本号，便于排查"这一目的落空，
        /// 见 04 第 3 节字段表 <c>migrated_from</c> 一行）；<c>migrated_from</c> 存在但不合法（非整数、
        /// 或超出 <c>[1, 原 schema_version - 1]</c> 范围）时按未提供处理——不校验、不抛异常，用本次迁移前
        /// 的 <c>schema_version</c> 写入。<paramref name="envelope"/> 原本没有
        /// <c>migrated_from</c> 键时，新键追加在信封末尾（键序不影响 <see cref="JsonObject"/> 语义，见该
        /// 类型注释"保持插入顺序"仅为 <see cref="JsonWriter"/> 原样回写时的可读性考虑）。<c>rows</c> 每个
        /// 元素经 <see cref="MigrateRow"/>。<paramref name="envelope"/> 本身不被修改——<see cref="JsonObject"/>
        /// 本就不可变（见该类型注释），本方法全程只读取、从不调用任何修改方法，天然满足"不改输入"。
        /// </para>
        /// </summary>
        public static JsonObject MigrateEnvelope(TableSchema schema, JsonObject envelope)
        {
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));

            if (!envelope.TryGetValue("schema_version", out var svVal) || !(svVal is JsonNumber svNum)
                || !svNum.TryGetInt64(out var svLong) || svLong < 1 || svLong > int.MaxValue)
            {
                throw new ArgumentException(
                    "信封缺少或非法的顶层字段 \"schema_version\"（须为 [1, int.MaxValue] 范围内的整数）",
                    nameof(envelope));
            }
            var schemaVersion = (int)svLong;

            if (schemaVersion > schema.CurrentSchemaVersion)
            {
                throw new ArgumentException(
                    $"信封 schema_version {schemaVersion} 超过当前代码期望的版本 {schema.CurrentSchemaVersion}",
                    nameof(envelope));
            }

            if (!envelope.TryGetValue("rows", out var rowsVal) || !(rowsVal is JsonArray rowsArr))
            {
                throw new ArgumentException("信封缺少或非法的顶层字段 \"rows\"（须为数组）", nameof(envelope));
            }

            if (schemaVersion == schema.CurrentSchemaVersion)
            {
                return envelope;
            }

            var chain = BuildChain(schema, schemaVersion, schema.CurrentSchemaVersion);
            if (chain == null)
            {
                throw new InvalidOperationException(
                    $"从版本 {schemaVersion} 到 {schema.CurrentSchemaVersion} 缺少迁移环节");
            }

            // 已带合法 migrated_from（此前迁移过一次）：保留那个更早的原始版本，不用本次迁移前的
            // schema_version 覆盖（见方法级判断记录）。
            var originalVersion = schemaVersion;
            if (envelope.TryGetValue("migrated_from", out var mfVal) && mfVal is JsonNumber mfNum
                && mfNum.TryGetInt64(out var mfLong) && mfLong >= 1 && mfLong < schemaVersion)
            {
                originalVersion = (int)mfLong;
            }

            var migratedRows = new List<JsonValue>(rowsArr.Count);
            for (var i = 0; i < rowsArr.Count; i++)
            {
                if (!(rowsArr[i] is JsonObject rowObj))
                {
                    throw new ArgumentException($"rows[{i}] 不是对象，无法迁移", nameof(envelope));
                }
                migratedRows.Add(MigrateRow(chain, rowObj));
            }
            var newRowsValue = new JsonArray(migratedRows);
            var newSchemaVersionValue = JsonNumber.FromInt64(schema.CurrentSchemaVersion);
            var newMigratedFromValue = JsonNumber.FromInt64(originalVersion);

            var builder = new JsonObjectBuilder();
            var wroteMigratedFrom = false;
            foreach (var entry in envelope)
            {
                switch (entry.Key)
                {
                    case "schema_version":
                        builder.Add("schema_version", newSchemaVersionValue);
                        break;
                    case "rows":
                        builder.Add("rows", newRowsValue);
                        break;
                    case "migrated_from":
                        builder.Add("migrated_from", newMigratedFromValue);
                        wroteMigratedFrom = true;
                        break;
                    default:
                        builder.Add(entry.Key, entry.Value);
                        break;
                }
            }
            if (!wroteMigratedFrom)
            {
                builder.Add("migrated_from", newMigratedFromValue);
            }

            return builder.Build();
        }
    }
}
