#nullable enable
// StreamingAssetsFileSystem：供 GameFoundationBootstrap 读取只读内容数据集（data/_sample 同步进
// StreamingAssets 之后的产物）使用的 IFileSystem 实现，不是包 README"13 个接口实现清单"里登记的
// 那份 UnityFileSystem。
//
// 判断记录（为什么需要第二份 IFileSystem 实现）：Core.Foundation.DataRegistry.FileSystemDataSource
// 的构造签名是 (IFileSystem fs, string rootDir)——core/gameplay/tests/EndToEnd/GameWorldFixture.cs
// 用一个桩 StubFileSystem 装好整个 data/_sample 目录树后正是这样用的（见该文件
// BuildRealSampleSource）。但包 README 里登记的 UnityFileSystem 语义是"用户数据目录"
// （Application.persistentDataPath，见该类型顶部注释、02 第 1.6 节 getUserDataDir）——用于存档/
// 设置这类需要写入的场景；本框架内容数据集（data/_sample、assets/_placeholder）是随包分发的只读
// 内容，路径根是 Application.streamingAssetsPath，与"用户数据目录"是两个完全不同的目录，不应该
// 把只读内容数据集错误地跟存档路径混在一起（也不应该反过来让 UnityFileSystem 的 GetUserDataDir()
// 随便指向 StreamingAssets）。IFileSystem 契约本身不要求"全仓库只有一个实现实例"——
// GameWorldFixture 同样是"main 场景用一个 IFileSystem 实例做存档、测试夹具另建一个指向不同根目录
// 的实例做内容加载"的用法，本类型是同一惯例在 Unity 侧的对应物，只读（Write/Delete 全部返回失败，
// 不抛异常，符合契约"错误约定"），不算是给 13 个 L-1 接口新增第二套"官方实现"。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Core.Foundation.EngineAdapter;
using UnityEngine;

namespace Adapter.Unity.Bootstrap
{
    public sealed class StreamingAssetsFileSystem : IFileSystem
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private readonly string _root;

        /// <param name="root">只读内容根目录的绝对路径；默认
        /// <c>Application.streamingAssetsPath/GameFoundation</c>（与 <c>UnityResourceLoader.RootDir</c>
        /// 同一个根，见该类型顶部注释"资源 id → 相对路径映射规则"）。</param>
        public StreamingAssetsFileSystem(string? root = null)
        {
            _root = root ?? Path.Combine(Application.streamingAssetsPath, "GameFoundation");
        }

        /// <summary>只读文件系统没有"用户数据目录"这个概念；如实返回本实例的只读根目录，供调用方
        /// 诊断用，不代表"可写"。</summary>
        public string GetUserDataDir() => _root;

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

        /// <summary>只读实现：恒失败，不抛异常（符合契约"用 Bool 返回值表达失败"的错误约定）。</summary>
        public bool WriteTextAtomic(string path, string content) => false;

        public bool Exists(string path) => File.Exists(ResolveFullPath(path));

        // 与 UnityFileSystem.ListFiles 同一套"实现级约定"：递归枚举、相对路径、'/' 分隔、按序数
        // 字典序排序，供 FileSystemDataSource.ListTables 使用。
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

        /// <summary>只读实现：恒失败，不抛异常。</summary>
        public bool DeleteFile(string path) => false;

        private string ResolveFullPath(string relativeOrPath)
        {
            if (string.IsNullOrEmpty(relativeOrPath))
            {
                return _root;
            }

            return Path.Combine(_root, relativeOrPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
