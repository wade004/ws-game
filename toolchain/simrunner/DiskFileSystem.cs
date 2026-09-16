using System;
using System.Collections.Generic;
using System.IO;
using Core.Foundation.EngineAdapter;

namespace Toolchain.SimRunner
{
    /// <summary>
    /// <see cref="IFileSystem"/> 的只读磁盘实现——照抄 <c>toolchain/validator/DiskFileSystem.cs</c>
    /// 同一惯例（见该文件类型注释判断记录）。<c>adapters/stub</c> 只提供内存实现，本工具需要对
    /// <c>--framework-root</c>/<c>--data-root</c> 指向的真实磁盘目录跑
    /// <c>DataRegistry.LoadAll</c>，因此在自己的目录下放一份最小只读磁盘实现，不放进 <c>core/</c>。
    /// 不与 <c>toolchain/validator</c> 共享同一份类型——两个工具是各自独立的 <c>OutputType=Exe</c>
    /// 控制台工程，互不引用（惯例同两者各自私有 <c>Program.cs</c>），复制这一份薄薄的适配层比新增一
    /// 条跨工具的项目引用更简单，不构成"与 core 内校验逻辑重复实现两套判断"（这里只是文件系统 I/O，
    /// 不含任何数据校验/解析逻辑）。
    /// </summary>
    internal sealed class DiskFileSystem : IFileSystem
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
