// StubFileSystem：IFileSystem 的最小可用桩实现——内存文件系统，不接触真实磁盘。
// 用途：测试存档写读往返、原子写入失败语义，而不依赖真实文件 I/O 与操作系统路径规则。
// 与真实实现的差异：路径只是字典的 key（用 '/' 作为目录分隔符约定），不做任何路径规范化
// 或权限检查；WriteTextAtomic 的"原子性"退化为"内存字典赋值要么成功要么不发生"，
// FailNextWrite() 是测试专用开关，用来模拟"下一次写入失败但旧内容保持不变"。
// 内容根（GetContentRootDir，见 ADR-0016 决策 8）用固定前缀 "content://" 与
// GetUserDataDir()（"user://"）区分开；测试用 SeedContent 直接把数据表/静态资产写进内存字典
// （绕开只读限制），运行时代码对内容根前缀下的路径调用 WriteTextAtomic/DeleteFile 一律返回
// false、不改变字典内容，语义与真实实现一致（内容根只读，不因误用而报异常）。
using System;
using System.Collections.Generic;
using Core.Foundation.EngineAdapter;

namespace Adapters.Stub
{
    public sealed class StubFileSystem : IFileSystem
    {
        private const string ContentRoot = "content://";

        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.Ordinal);
        private bool _failNextWrite;

        public string GetUserDataDir() => "user://";

        public string GetContentRootDir() => ContentRoot;

        public string? ReadText(string path)
        {
            return _files.TryGetValue(path, out var content) ? content : null;
        }

        public bool WriteTextAtomic(string path, string content)
        {
            if (IsUnderContentRoot(path))
            {
                return false;
            }

            if (_failNextWrite)
            {
                _failNextWrite = false;
                return false;
            }

            _files[path] = content;
            return true;
        }

        public bool Exists(string path) => _files.ContainsKey(path);

        /// <summary>测试用：把内容直接写入内容根下的路径（绕开只读限制，模拟"已存在的内容"）。</summary>
        public void SeedContent(string path, string content) => _files[path] = content;

        private static bool IsUnderContentRoot(string path) =>
            path.StartsWith(ContentRoot, StringComparison.Ordinal);

        // 实现级约定（02_引擎适配层.md 第 1.6 节未限定 ListFiles 的路径形态，本仓库拍板见
        // core/foundation/engine_adapter/README.md"IFileSystem"一节）：返回 dirPath 之下
        // （递归）全部文件，路径相对 dirPath、用 '/' 分隔、按序数排序——不是完整 key。
        public IReadOnlyList<string> ListFiles(string dirPath)
        {
            var prefix = dirPath.Length == 0 || dirPath.EndsWith("/", StringComparison.Ordinal)
                ? dirPath
                : dirPath + "/";

            var results = new List<string>();
            foreach (var key in _files.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal) && key.Length > prefix.Length)
                {
                    results.Add(key.Substring(prefix.Length));
                }
            }

            results.Sort(StringComparer.Ordinal);
            return results;
        }

        public bool DeleteFile(string path)
        {
            if (IsUnderContentRoot(path))
            {
                return false;
            }

            return _files.Remove(path);
        }

        /// <summary>测试用：让下一次 WriteTextAtomic 调用失败并保持旧内容不变，随后自动复位。</summary>
        public void FailNextWrite() => _failNextWrite = true;
    }
}
