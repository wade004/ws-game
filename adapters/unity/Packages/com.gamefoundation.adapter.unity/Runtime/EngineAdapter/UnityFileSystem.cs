#nullable enable
// UnityFileSystem：IFileSystem 的 Unity 引擎实现。
//
// 用户数据目录 = Application.persistentDataPath（Unity 官方跨平台约定的用户数据落盘位置）。
// 本实现内部把 IFileSystem 的相对路径参数拼接到该目录下（用 '/' 分隔，与 02 第 1.6 节
// listFiles 的相对路径分隔约定一致）。
//
// 原子写入判断记录：WriteTextAtomic 按"写临时文件 + File.Replace"实现——先把内容写入同目录下
// 的一个临时文件，再用 File.Replace 原子替换目标文件（File.Replace 在多数文件系统上是单次
// 原子的重命名操作）；目标文件不存在时退化为 File.Move（同目录内的 Move 在 NTFS 上同样是原子
// 重命名）。任一步骤失败时清理临时文件并保持旧内容不变、返回 false，不抛异常穿透到调用方
// （契约"错误约定"要求用 Bool 返回值表达失败）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Core.Foundation.EngineAdapter;
using UnityEngine;

namespace Adapter.Unity.EngineAdapter
{
    public sealed class UnityFileSystem : IFileSystem
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public string GetUserDataDir() => Application.persistentDataPath;

        public string? ReadText(string path)
        {
            var fullPath = ResolveFullPath(path);
            try
            {
                return File.Exists(fullPath) ? File.ReadAllText(fullPath, Utf8NoBom) : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        public bool WriteTextAtomic(string path, string content)
        {
            var fullPath = ResolveFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);

            try
            {
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var tempPath = fullPath + ".tmp_" + Guid.NewGuid().ToString("N");
                File.WriteAllText(tempPath, content, Utf8NoBom);

                try
                {
                    if (File.Exists(fullPath))
                    {
                        // File.Replace：原子地用临时文件替换目标文件，不产生备份文件。
                        File.Replace(tempPath, fullPath, destinationBackupFileName: null);
                    }
                    else
                    {
                        File.Move(tempPath, fullPath);
                    }

                    return true;
                }
                catch
                {
                    TryDeleteQuiet(tempPath);
                    return false;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        public bool Exists(string path) => File.Exists(ResolveFullPath(path));

        // 实现级约定：递归枚举 dirPath 下全部文件，返回相对 dirPath、'/' 分隔、按序数字典序排序的路径，
        // 与 02 第 1.6 节 listFiles 的语义一致（同 adapters/stub/StubFileSystem 的约定）。
        public IReadOnlyList<string> ListFiles(string dirPath)
        {
            var fullDir = ResolveFullPath(dirPath);
            var results = new List<string>();

            if (!Directory.Exists(fullDir))
            {
                return results;
            }

            var files = Directory.GetFiles(fullDir, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var relative = file.Substring(fullDir.Length).TrimStart('\\', '/').Replace('\\', '/');
                results.Add(relative);
            }

            results.Sort(StringComparer.Ordinal);
            return results;
        }

        public bool DeleteFile(string path)
        {
            var fullPath = ResolveFullPath(path);
            try
            {
                if (!File.Exists(fullPath))
                {
                    return false;
                }

                File.Delete(fullPath);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string ResolveFullPath(string relativeOrPath)
        {
            if (string.IsNullOrEmpty(relativeOrPath))
            {
                return Application.persistentDataPath;
            }

            // path 可能是调用方拼出的以 '/' 分隔的相对路径；统一交给 Path.Combine 处理，
            // Windows/Unix 分隔符都能被正确解析。
            return Path.Combine(Application.persistentDataPath, relativeOrPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void TryDeleteQuiet(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 尽力而为的清理，失败不影响 WriteTextAtomic 的"保持旧内容不变"语义。
            }
        }
    }
}
