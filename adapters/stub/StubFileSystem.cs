// StubFileSystem：IFileSystem 的最小可用桩实现——内存文件系统，不接触真实磁盘。
// 用途：测试存档写读往返、原子写入失败语义，而不依赖真实文件 I/O 与操作系统路径规则。
// 与真实实现的差异：路径只是字典的 key（用 '/' 作为目录分隔符约定），不做任何路径规范化
// 或权限检查；WriteTextAtomic 的"原子性"退化为"内存字典赋值要么成功要么不发生"，
// FailNextWrite() 是测试专用开关，用来模拟"下一次写入失败但旧内容保持不变"。
using System;
using System.Collections.Generic;
using Core.Foundation.EngineAdapter;

namespace Adapters.Stub
{
    public sealed class StubFileSystem : IFileSystem
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.Ordinal);
        private bool _failNextWrite;

        public string GetUserDataDir() => "user://";

        public string? ReadText(string path)
        {
            return _files.TryGetValue(path, out var content) ? content : null;
        }

        public bool WriteTextAtomic(string path, string content)
        {
            if (_failNextWrite)
            {
                _failNextWrite = false;
                return false;
            }

            _files[path] = content;
            return true;
        }

        public bool Exists(string path) => _files.ContainsKey(path);

        public IReadOnlyList<string> ListFiles(string dirPath)
        {
            var prefix = dirPath.Length == 0 || dirPath.EndsWith("/", StringComparison.Ordinal)
                ? dirPath
                : dirPath + "/";

            var results = new List<string>();
            foreach (var key in _files.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    results.Add(key);
                }
            }

            results.Sort(StringComparer.Ordinal);
            return results;
        }

        public bool DeleteFile(string path) => _files.Remove(path);

        /// <summary>测试用：让下一次 WriteTextAtomic 调用失败并保持旧内容不变，随后自动复位。</summary>
        public void FailNextWrite() => _failNextWrite = true;
    }
}
