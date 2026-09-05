#nullable enable
// FileSystemScenarios：IFileSystem 契约一致性场景（见 02_引擎适配层.md 第 1.6 节 / ADR-0016 决策 8）。
//
// 判断记录（路径命名）：全部场景用 "conformance/" 前缀 + 随机后缀拼出用户数据目录下的路径，
// 避免与同一进程内其它测试用例、或宿主机上同一用户数据目录里已经存在的文件相撞（Unity 侧的
// 用户数据目录是跨多次 Editor/PlayMode 运行持久化的真实操作系统目录，见
// GlobalPlayModeTestSetup.cs 判断记录同类考量）。场景结束后尽力清理自己写下的文件，清理失败不
// 影响断言结果本身。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Core.Foundation.EngineAdapter;

namespace Adapters.Conformance
{
    public static class FileSystemScenarios
    {
        public static readonly IReadOnlyList<ConformanceScenario<IFileSystem>> All = new[]
        {
            new ConformanceScenario<IFileSystem>("WriteTextAtomic_ReadText_往返一致", WriteThenRead_RoundTrips),
            new ConformanceScenario<IFileSystem>("DeleteFile_删除后Exists为false_再次删除返回false", DeleteFile_RemovesFile_ThenReturnsFalse),
            new ConformanceScenario<IFileSystem>("ListFiles_返回相对路径且按序数排序", ListFiles_ReturnsSortedRelativePaths),
            new ConformanceScenario<IFileSystem>("内容根目录_写入与删除一律返回false", ContentRoot_WriteAndDelete_ReturnFalse),
            new ConformanceScenario<IFileSystem>("原子写入失败时旧内容保持不变", WriteFailure_KeepsOldContentUnchanged),
        };

        private static string UniquePath(string prefix) => $"conformance/{prefix}_{Guid.NewGuid():N}.json";

        private static IEnumerator WriteThenRead_RoundTrips(IFileSystem fs, IConformanceAssert assert, ConformanceContext ctx)
        {
            var path = UniquePath("roundtrip");
            const string content = "{\"conformance\":true}";

            assert.False(fs.Exists(path), "写入前 Exists 应为 false");
            var writeOk = fs.WriteTextAtomic(path, content);
            assert.True(writeOk, "对用户数据目录下的新路径调用 WriteTextAtomic 应返回 true");
            assert.True(fs.Exists(path), "写入后 Exists 应为 true");
            assert.Equal(content, fs.ReadText(path), "ReadText 应返回刚写入的内容");

            fs.DeleteFile(path);
            yield break;
        }

        private static IEnumerator DeleteFile_RemovesFile_ThenReturnsFalse(IFileSystem fs, IConformanceAssert assert, ConformanceContext ctx)
        {
            var path = UniquePath("delete");
            fs.WriteTextAtomic(path, "x");

            assert.True(fs.DeleteFile(path), "删除一个存在的文件应返回 true");
            assert.False(fs.Exists(path), "删除后 Exists 应为 false");
            assert.False(fs.DeleteFile(path), "再次删除一个已不存在的文件应返回 false");
            yield break;
        }

        private static IEnumerator ListFiles_ReturnsSortedRelativePaths(IFileSystem fs, IConformanceAssert assert, ConformanceContext ctx)
        {
            var dir = $"conformance/list_{Guid.NewGuid():N}";
            var pathB = $"{dir}/b.json";
            var pathA = $"{dir}/sub/a.json";

            fs.WriteTextAtomic(pathB, "b");
            fs.WriteTextAtomic(pathA, "a");

            var listed = fs.ListFiles(dir);
            var expected = new[] { "b.json", "sub/a.json" }.OrderBy(s => s, StringComparer.Ordinal).ToArray();
            var actual = listed.OrderBy(s => s, StringComparer.Ordinal).ToArray();

            assert.Equal(expected.Length, actual.Length, $"ListFiles 应恰好返回 {expected.Length} 个文件，实际 {actual.Length} 个");
            for (var i = 0; i < expected.Length; i++)
            {
                assert.Equal(expected[i], actual[i], $"ListFiles 第 {i} 项路径不匹配");
            }

            fs.DeleteFile(pathB);
            fs.DeleteFile(pathA);
            yield break;
        }

        private static IEnumerator ContentRoot_WriteAndDelete_ReturnFalse(IFileSystem contentRootFs, IConformanceAssert assert, ConformanceContext ctx)
        {
            var contentRoot = contentRootFs.GetContentRootDir();
            assert.NotNull(contentRoot, "GetContentRootDir 不应返回 null");

            // 判断记录：路径必须真正落在 GetContentRootDir() 之下——桩实现按路径前缀（"content://"）
            // 判定是否属于内容根，裸文件名不会命中；Unity 的内容根实例按"构造时的模式"整体判定，
            // 与路径无关，两侧都能接受本写法（见 adapters/conformance/README.md 判断记录 4）。
            var path = contentRoot!.EndsWith("/") ? contentRoot + "conformance_content_probe.json" : contentRoot + "/conformance_content_probe.json";
            assert.False(contentRootFs.WriteTextAtomic(path, "x"), "内容根目录下调用 WriteTextAtomic 应恒返回 false（ADR-0016 决策 8）");
            assert.False(contentRootFs.DeleteFile(path), "内容根目录下调用 DeleteFile 应恒返回 false（ADR-0016 决策 8）");
            yield break;
        }

        private static IEnumerator WriteFailure_KeepsOldContentUnchanged(IFileSystem fs, IConformanceAssert assert, ConformanceContext ctx)
        {
            if (ctx.SimulateNextWriteFailure == null)
            {
                assert.Skip("当前实现无法确定性模拟一次写入失败（ConformanceContext.SimulateNextWriteFailure 为空）");
                yield break;
            }

            var path = UniquePath("write_failure");
            const string oldContent = "old-content";
            fs.WriteTextAtomic(path, oldContent);

            ctx.SimulateNextWriteFailure();
            var writeOk = fs.WriteTextAtomic(path, "new-content-should-not-land");
            assert.False(writeOk, "模拟写入失败时 WriteTextAtomic 应返回 false");
            assert.Equal(oldContent, fs.ReadText(path), "写入失败后旧内容应保持不变");

            fs.DeleteFile(path);
        }
    }
}
