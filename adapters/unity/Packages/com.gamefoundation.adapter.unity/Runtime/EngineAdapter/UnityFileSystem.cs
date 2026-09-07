#nullable enable
// UnityFileSystem：IFileSystem 的 Unity 引擎实现。
//
// 用户数据目录 = Application.persistentDataPath（Unity 官方跨平台约定的用户数据落盘位置）。
// 内容根目录（ADR-0016 决策 8 新增 GetContentRootDir）= Application.streamingAssetsPath/
// GameFoundation（与 UnityResourceLoader.RootDir 同一个根，见该类型顶部注释"资源 id → 相对路径
// 映射规则"）。本实现内部把 IFileSystem 的相对路径参数拼接到"当前根"下（用 '/' 分隔，与 02 第
// 1.6 节 listFiles 的相对路径分隔约定一致）——"当前根"由构造参数 <see cref="ReadOnlyContentMode"/>
// 决定，两种模式各对应一个独立实例，同一个实例内不做"按路径前缀判断该走哪个根"的动态路由
// （见判断记录 1）。
//
// 判断记录 1（合并 StreamingAssetsFileSystem，取代"两个几乎重复的类"）：阶段 4 U2 曾经另建一个
// `Adapter.Unity.Bootstrap.StreamingAssetsFileSystem` 类专门承担"只读内容根"这一半职责，
// 与本类型的 ReadText/Exists/ListFiles 实现逐字重复，只是根目录与写入行为不同（该类型只读，
// 且 GetContentRootDir 尚不存在时借用 GetUserDataDir 顶替，见该类型历史注释）。ADR-0016 给
// IFileSystem 补上 GetContentRootDir 之后，两个类的唯一实质差异（"根目录 + 是否允许写"）可以
// 收敛成一个构造参数：本类型现通过 <see cref="ReadOnlyContentMode"/> 在同一份实现里承担两种
// 角色——默认（false）是原 UnityFileSystem 的"用户数据、可写"模式；传 true 时是原
// StreamingAssetsFileSystem 的"只读内容根"模式（根目录换成 StreamingAssets，
// WriteTextAtomic/DeleteFile 恒返回 false，语义与 ADR-0016 决策 8"内容根下写入/删除返回 false"
// 完全一致，不再是专门为"只读"发明的另一套返回值）。`StreamingAssetsFileSystem.cs` 已删除，
// 两处原有调用方（GameFoundationBootstrap、FrameworkResidentHost）改为
// `new UnityFileSystem(readOnlyContentMode: true)`。
//
// 判断记录 2（GetContentRootDir 在两种模式下都返回同一个值）：无论 <see cref="ReadOnlyContentMode"/>
// 是否为 true，GetContentRootDir() 都返回 StreamingAssets 内容根——即便当前实例是"用户数据"模式，
// 调用方仍可能需要查询"内容根在哪"这个只读信息（例如诊断/日志），这与 GetUserDataDir() 在
// ReadOnlyContentMode=true 时仍如实返回 persistentDataPath（而不是抛异常或返回内容根）是同一个
// 原则："两个访问器各自回答自己的问题，不因为当前实例的读写模式而隐藏另一半信息"。
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

        /// <summary>true 时本实例是"只读内容根"模式（取代原 StreamingAssetsFileSystem，见类型顶部
        /// 判断记录 1）：<see cref="ReadText"/>/<see cref="Exists"/>/<see cref="ListFiles"/> 相对
        /// <see cref="GetContentRootDir"/> 解析路径，<see cref="WriteTextAtomic"/>/
        /// <see cref="DeleteFile"/> 恒返回 false；false（默认）时是原 UnityFileSystem 的"用户数据、
        /// 可写"模式，相对 <see cref="GetUserDataDir"/> 解析路径。</summary>
        public bool ReadOnlyContentMode { get; }

        private readonly string _contentRoot;

        /// <summary>
        /// N06 根治（architecture/落地计划/audit-68c9bed-20260907/code-review.md）：全局可选覆盖
        /// "用户数据根"（<see cref="GetUserDataDir"/>/写入可写模式下的 <see cref="CurrentRoot"/>），
        /// 默认为 <c>null</c>（沿用生产行为，即 <see cref="UnityEngine.Application.persistentDataPath"/>，
        /// 真实玩家存档目录）。
        /// <para>
        /// 判断记录：本字段是 <c>static</c>，不是构造参数——生产代码与 PlayMode 测试里散落着大量
        /// <c>new UnityFileSystem(...)</c> 调用点（<c>SaveSystem</c>、各测试夹具各自装配自己的
        /// <c>ISaveSystem</c>），要求每一处都改签名去接一个"测试专用根目录"参数、并让每条测试都
        /// 记得传，代价既大又容易漏传（漏传的那一条测试就会退回污染真实目录）。改为"装配级一次性
        /// 全局覆盖"：<c>GlobalPlayModeTestSetup</c>（<c>[SetUpFixture]</c>，见该类型）在
        /// <c>[OneTimeSetUp]</c> 时把本字段设为一个专属测试子目录（
        /// <c>&lt;persistentDataPath&gt;/_playmode_tests/</c>），覆盖对本 PlayMode 测试装配下
        /// 之后新建的**全部** <see cref="UnityFileSystem"/> 实例统一生效（不管由谁在哪个测试
        /// 夹具里 new 出来），<c>[OneTimeTearDown]</c> 时清空该目录并把本字段复位为
        /// <c>null</c>——生产运行（从未调用 <c>GlobalPlayModeTestSetup</c>）本字段永远是
        /// <c>null</c>，行为与本次改动前完全一致，真实玩家存档目录永不被本机制触碰。
        /// </para>
        /// </summary>
        public static string? UserDataRootOverride { get; set; }

        public UnityFileSystem(bool readOnlyContentMode = false, string? contentRoot = null)
        {
            ReadOnlyContentMode = readOnlyContentMode;
            _contentRoot = contentRoot ?? Path.Combine(Application.streamingAssetsPath, "GameFoundation");
        }

        /// <summary>真实用户数据根：<see cref="UserDataRootOverride"/> 未设置（生产运行的唯一状态）
        /// 时是 <see cref="UnityEngine.Application.persistentDataPath"/>，设置时（仅 PlayMode 测试
        /// 装配级 setup 会设置，见该字段判断记录）改用覆盖值。</summary>
        private static string EffectiveUserDataDir => UserDataRootOverride ?? Application.persistentDataPath;

        public string GetUserDataDir() => EffectiveUserDataDir;

        /// <summary>只读内容根目录（数据表、静态资产的落盘位置），与 <see cref="GetUserDataDir"/>
        /// 分离（ADR-0016 决策 8）。两种模式下都返回同一个值，见类型顶部判断记录 2。</summary>
        public string GetContentRootDir() => _contentRoot;

        private string CurrentRoot => ReadOnlyContentMode ? _contentRoot : EffectiveUserDataDir;

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

        /// <summary>内容根模式下恒返回 false，不真正执行写入、不抛异常（ADR-0016 决策 8）。</summary>
        public bool WriteTextAtomic(string path, string content)
        {
            if (ReadOnlyContentMode)
            {
                return false;
            }

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

        /// <summary>内容根模式下恒返回 false，不真正执行删除、不抛异常（ADR-0016 决策 8）。</summary>
        public bool DeleteFile(string path)
        {
            if (ReadOnlyContentMode)
            {
                return false;
            }

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

        private string ResolveFullPath(string relativeOrPath)
        {
            if (string.IsNullOrEmpty(relativeOrPath))
            {
                return CurrentRoot;
            }

            // path 可能是调用方拼出的以 '/' 分隔的相对路径；统一交给 Path.Combine 处理，
            // Windows/Unix 分隔符都能被正确解析。
            return Path.Combine(CurrentRoot, relativeOrPath.Replace('/', Path.DirectorySeparatorChar));
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
