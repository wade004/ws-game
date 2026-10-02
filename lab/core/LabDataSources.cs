using System;
using System.Collections.Generic;
using System.IO;
using Core.Foundation.Common.Json;
using Core.Foundation.DataRegistry;
using Core.Foundation.EngineAdapter;

namespace Lab
{
    /// <summary>
    /// 从磁盘目录构造数据来源（命令行入口与测试共用）。判断记录：<c>adapters/stub</c> 的文件系统只有内存实现，
    /// 实验室需要对真实磁盘上的数据根跑 <c>DataRegistry.LoadAll</c>，因此内核里放一份最小只读磁盘文件系统
    /// （同 <c>toolchain/validator/DiskFileSystem.cs</c> 惯例，只做文件 I/O，不含任何数据校验/解析逻辑）。
    /// </summary>
    public static class LabDataSources
    {
        /// <summary>目录必须存在；相对路径按当前工作目录解析。</summary>
        public static IDataSource FromDirectory(string root)
        {
            var full = Path.IsPathRooted(root) ? root : Path.Combine(Directory.GetCurrentDirectory(), root);
            full = Path.GetFullPath(full).Replace('\\', '/');
            if (!Directory.Exists(full))
            {
                throw new DirectoryNotFoundException($"数据根目录不存在：{full}");
            }

            return new FileSystemDataSource(new ReadOnlyDiskFileSystem(), full);
        }

        /// <summary>
        /// 脚本级数据源：在基础数据来源之后追加脚本声明的额外根（<paramref name="resolveRoot"/> 把根路径变成数据来源），
        /// 额外根里 <see cref="ScriptMeta.ExtraDataExcludeTables"/> 的表整表剔除、<see cref="ScriptMeta.ExtraDataExcludeRows"/> 的行剔除。
        /// 脚本没有任何扩展数据声明时原样返回基础来源。武器 → 普攻时间线的映射是数据（<c>feel.weapon.auto_attack_timeline_ref</c>），不在这里改写。
        /// </summary>
        public static IReadOnlyList<IDataSource> ForScript(
            IReadOnlyList<IDataSource> baseSources, ScriptMeta meta, Func<string, IDataSource> resolveRoot, bool stripTimelines = false)
        {
            if (meta.ExtraDataRoots.Count == 0)
            {
                return baseSources;
            }

            var result = new List<IDataSource>(baseSources);
            var exclude = new HashSet<string>(meta.ExtraDataExcludeTables, StringComparer.Ordinal);
            foreach (var root in meta.ExtraDataRoots)
            {
                IDataSource source = resolveRoot(root);
                if (exclude.Count > 0)
                {
                    source = new TableFilterDataSource(source, exclude);
                }

                if (meta.ExtraDataExcludeRows.Count > 0)
                {
                    source = new TableOverlayDataSource(source, (table, text) => RewriteTable(table, text, meta));
                }

                if (stripTimelines && meta.Feel)
                {
                    // 目标选择式变体：同一批技能剥掉 timeline 块（连招/取消/蓄力/标记/位移都在块里，随之消失）并把 cast_time 置 0（瞬发）。
                    source = new TableOverlayDataSource(source, (table, text) => StripTimelines(table, text));
                }

                result.Add(source);
            }

            return result;
        }

        /// <summary>
        /// 剥掉 <c>skill.def</c> 每一行的 <c>timeline</c> 块并把 <c>cast_time</c> 置 0：有 <c>timeline</c> 的技能 <c>cast_time</c>
        /// 必须等于三相之和（校验规则），剥块后不置 0 就变成一个读条技能，而不是"没有时间线的瞬发技能"。其余表与其余行原样返回（null）。
        /// </summary>
        public static string? StripTimelines(string table, string text)
        {
            if (!string.Equals(table, "skill.def", StringComparison.Ordinal))
            {
                return null;
            }

            var root = LabJson.ParseObject(text, table);
            var rewritten = new JsonObjectBuilder();
            for (var i = 0; i < root.Count; i++)
            {
                var entry = root[i];
                if (!string.Equals(entry.Key, "rows", StringComparison.Ordinal) || !(entry.Value is JsonArray rows))
                {
                    rewritten.Add(entry.Key, entry.Value);
                    continue;
                }

                var newRows = new List<JsonValue>();
                foreach (var row in rows)
                {
                    if (!(row is JsonObject rowObj) || !rowObj.ContainsKey("timeline"))
                    {
                        newRows.Add(row);
                        continue;
                    }

                    var builder = new JsonObjectBuilder();
                    for (var k = 0; k < rowObj.Count; k++)
                    {
                        var key = rowObj[k].Key;
                        if (string.Equals(key, "timeline", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        builder.Add(key, string.Equals(key, "cast_time", StringComparison.Ordinal) ? LabJson.Num(0) : rowObj[k].Value);
                    }

                    newRows.Add(builder.Build());
                }

                rewritten.Add(entry.Key, new JsonArray(newRows));
            }

            return LabJson.Write(rewritten.Build());
        }

        private static string? RewriteTable(string table, string text, ScriptMeta meta)
        {
            var dropRules = new List<KeyValuePair<string, string>>();
            foreach (var spec in meta.ExtraDataExcludeRows)
            {
                var slash = spec.IndexOf('/');
                var eq = spec.IndexOf('=');
                if (slash <= 0 || eq <= slash + 1)
                {
                    throw new LabFormatException($"extraDataExcludeRows 的格式必须是 表名/字段名=字段值：{spec}");
                }

                if (string.Equals(spec.Substring(0, slash), table, StringComparison.Ordinal))
                {
                    dropRules.Add(new KeyValuePair<string, string>(spec.Substring(slash + 1, eq - slash - 1), spec.Substring(eq + 1)));
                }
            }

            if (dropRules.Count == 0)
            {
                return null;
            }

            var root = LabJson.ParseObject(text, table);
            var rewritten = new JsonObjectBuilder();
            for (var i = 0; i < root.Count; i++)
            {
                var entry = root[i];
                if (!string.Equals(entry.Key, "rows", StringComparison.Ordinal) || !(entry.Value is JsonArray rows))
                {
                    rewritten.Add(entry.Key, entry.Value);
                    continue;
                }

                var newRows = new List<JsonValue>();
                foreach (var row in rows)
                {
                    if (row is JsonObject dropCandidate && MatchesAny(dropCandidate, dropRules))
                    {
                        continue;
                    }

                    newRows.Add(row);
                }

                rewritten.Add(entry.Key, new JsonArray(newRows));
            }

            return LabJson.Write(rewritten.Build());
        }

        private static bool MatchesAny(JsonObject row, List<KeyValuePair<string, string>> rules)
        {
            foreach (var rule in rules)
            {
                if (row.TryGetValue(rule.Key, out var value) && value is JsonString text
                    && string.Equals(text.Value, rule.Value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class ReadOnlyDiskFileSystem : IFileSystem
        {
            public string GetUserDataDir() => Directory.GetCurrentDirectory();

            public string GetContentRootDir() => Directory.GetCurrentDirectory();

            public string? ReadText(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

            public bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

            public IReadOnlyList<string> ListFiles(string dirPath)
            {
                if (!Directory.Exists(dirPath))
                {
                    return Array.Empty<string>();
                }

                var normalizedDir = dirPath.Replace('\\', '/').TrimEnd('/');
                var prefixLength = normalizedDir.Length + 1;
                var results = new List<string>();
                foreach (var file in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories))
                {
                    results.Add(file.Replace('\\', '/').Substring(prefixLength));
                }

                results.Sort(StringComparer.Ordinal);
                return results;
            }

            public bool WriteTextAtomic(string path, string content) =>
                throw new NotSupportedException("只读磁盘文件系统不修改数据目录");

            public bool DeleteFile(string path) =>
                throw new NotSupportedException("只读磁盘文件系统不修改数据目录");
        }
    }

    /// <summary>整表剔除若干表的数据来源包装（只改 <see cref="ListTables"/>，不动被包装来源）。</summary>
    public sealed class TableFilterDataSource : IDataSource
    {
        private readonly IDataSource _inner;
        private readonly HashSet<string> _exclude;

        public TableFilterDataSource(IDataSource inner, IEnumerable<string> excludeTables)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _exclude = new HashSet<string>(excludeTables ?? throw new ArgumentNullException(nameof(excludeTables)), StringComparer.Ordinal);
        }

        public string? Root => _inner.Root;

        public IReadOnlyList<DataTableSource> ListTables()
        {
            var result = new List<DataTableSource>();
            foreach (var table in _inner.ListTables())
            {
                if (!_exclude.Contains(table.TableName))
                {
                    result.Add(table);
                }
            }

            return result;
        }
    }
}
