using System;
using System.Collections.Generic;
using System.IO;
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
}
