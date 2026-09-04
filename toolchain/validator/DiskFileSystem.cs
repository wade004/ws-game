using System;
using System.Collections.Generic;
using System.IO;
using Core.Foundation.EngineAdapter;

namespace Toolchain.Validator
{
    /// <summary>
    /// <see cref="IFileSystem"/> 的只读磁盘实现，供本命令行工具直接读取仓库里真实的数据表 JSON
    /// 文件。<c>adapters/stub</c> 只提供内存实现（<c>StubFileSystem</c>），不接触真实磁盘（见该类型
    /// 文件头注释）；本工具需要对 <c>--data-root</c> 指向的真实目录跑 <c>DataRegistry.LoadAll</c>，
    /// 因此在工具自己的目录下实现一个最小只读磁盘版本，不放进 <c>core/</c>（校验器不是引擎适配层
    /// 契约的正式实现，只是命令行工具的私有基础设施）。
    /// <para>
    /// <see cref="ListFiles"/> 语义必须与 <c>StubFileSystem</c>/<c>FileSystemDataSource</c> 依赖的
    /// "实现级约定"完全一致（见 <c>core/foundation/data_registry/README.md</c>
    /// "IFileSystem.ListFiles 依赖的实现级约定"）：返回 <c>dirPath</c> 之下递归全部文件、相对
    /// <c>dirPath</c>、用 <c>/</c> 分隔、按序数排序。
    /// </para>
    /// <para>
    /// 判断记录：<see cref="WriteTextAtomic"/>/<see cref="DeleteFile"/> 本工具永远不会调用——校验器
    /// 只读数据目录，不回写；两者一律抛 <see cref="NotSupportedException"/>，不静默返回
    /// false（呼应 11_工程规范与测试.md"不做静默降级"：一旦调用方误用了写入路径，应该立刻在开发期
    /// 暴露出来，而不是悄悄"假装写入失败"）。
    /// </para>
    /// </summary>
    internal sealed class DiskFileSystem : IFileSystem
    {
        public string GetUserDataDir() => Directory.GetCurrentDirectory();

        public string? ReadText(string path)
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

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
                var normalizedFile = file.Replace('\\', '/');
                results.Add(normalizedFile.Substring(prefixLength));
            }

            results.Sort(StringComparer.Ordinal);
            return results;
        }

        public bool WriteTextAtomic(string path, string content) =>
            throw new NotSupportedException("DiskFileSystem 是只读实现，本工具不修改数据目录");

        public bool DeleteFile(string path) =>
            throw new NotSupportedException("DiskFileSystem 是只读实现，本工具不修改数据目录");
    }
}
