using System;
using System.Collections.Generic;
using Core.Foundation.EngineAdapter;

namespace Core.Foundation.DataRegistry
{
    /// <summary>
    /// <see cref="IDataSource"/> 的 <see cref="IFileSystem"/> 实现：遍历
    /// <c>fs.ListFiles(rootDir)</c> 中以 <c>.json</c> 结尾的项，表名取文件名（去掉 <c>.json</c>
    /// 扩展名，不含目录部分）；文件名与文件内顶层 <c>table</c> 字段是否一致由
    /// <see cref="DataRegistry"/> 加载期的 <c>envelope</c> 校验负责，本类不做该检查。
    /// <para>
    /// <c>ListFiles</c> 语义判断记录：02_引擎适配层.md 第 1.6 节只给出签名
    /// <c>listFiles(dirPath): List&lt;String&gt;</c>，未规定返回值是绝对路径还是相对
    /// <c>dirPath</c> 的路径、是否递归。本模块按"实现级约定"（见
    /// <c>core/foundation/engine_adapter/README.md</c>"IFileSystem"一节）采用：返回
    /// <c>rootDir</c>之下（递归）全部文件、相对 <c>rootDir</c>、
    /// 用 <c>/</c> 分隔、按序数排序——因此本类把 <c>rootDir</c> 与每个相对路径拼接回完整路径
    /// 后才传给 <see cref="IFileSystem.ReadText"/>。
    /// </para>
    /// </summary>
    public sealed class FileSystemDataSource : IDataSource
    {
        private const string JsonExtension = ".json";

        private readonly IFileSystem _fs;
        private readonly string _rootDir;

        public FileSystemDataSource(IFileSystem fs, string rootDir)
        {
            _fs = fs ?? throw new ArgumentNullException(nameof(fs));
            _rootDir = rootDir ?? throw new ArgumentNullException(nameof(rootDir));
        }

        public IReadOnlyList<DataTableSource> ListTables()
        {
            var relatives = _fs.ListFiles(_rootDir);
            var result = new List<DataTableSource>();

            foreach (var rel in relatives)
            {
                if (!rel.EndsWith(JsonExtension, StringComparison.Ordinal))
                {
                    continue;
                }

                var lastSlash = rel.LastIndexOf('/');
                var fileName = lastSlash < 0 ? rel : rel.Substring(lastSlash + 1);
                var tableName = fileName.Substring(0, fileName.Length - JsonExtension.Length);

                var fullPath = CombinePath(_rootDir, rel);
                result.Add(new DataTableSource(tableName, fullPath, () => ReadTextOrThrow(fullPath)));
            }

            return result;
        }

        private string ReadTextOrThrow(string fullPath)
        {
            var text = _fs.ReadText(fullPath);
            if (text == null)
            {
                throw new InvalidOperationException($"文件不存在或读取失败：{fullPath}");
            }
            return text;
        }

        private static string CombinePath(string root, string relative)
        {
            if (root.Length == 0) return relative;
            return root.EndsWith("/", StringComparison.Ordinal) ? root + relative : root + "/" + relative;
        }
    }
}
