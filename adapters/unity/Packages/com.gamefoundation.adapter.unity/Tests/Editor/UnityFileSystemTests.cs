#nullable enable
using System.IO;
using Adapter.Unity.EngineAdapter;
using NUnit.Framework;
using UnityEngine;

namespace Adapter.Unity.Tests.Editor
{
    public sealed class UnityFileSystemTests
    {
        private UnityFileSystem _fs = null!;
        private string _subDir = null!;

        [SetUp]
        public void SetUp()
        {
            _fs = new UnityFileSystem();
            _subDir = "test_" + System.Guid.NewGuid().ToString("N");
        }

        [TearDown]
        public void TearDown()
        {
            var full = Path.Combine(Application.persistentDataPath, _subDir);
            if (Directory.Exists(full))
            {
                Directory.Delete(full, recursive: true);
            }
        }

        [Test]
        public void WriteTextAtomic_ThenReadText_ReturnsSameContent()
        {
            var path = _subDir + "/save1.json";
            var ok = _fs.WriteTextAtomic(path, "{\"hp\":10}");

            Assert.IsTrue(ok);
            Assert.AreEqual("{\"hp\":10}", _fs.ReadText(path));
        }

        [Test]
        public void WriteTextAtomic_Overwrite_ReplacesContentAtomically()
        {
            var path = _subDir + "/save2.json";
            _fs.WriteTextAtomic(path, "old");
            var ok = _fs.WriteTextAtomic(path, "new");

            Assert.IsTrue(ok);
            Assert.AreEqual("new", _fs.ReadText(path));
        }

        [Test]
        public void ListFiles_ReturnsRelativeSortedPaths()
        {
            _fs.WriteTextAtomic(_subDir + "/b.json", "b");
            _fs.WriteTextAtomic(_subDir + "/a.json", "a");
            _fs.WriteTextAtomic(_subDir + "/nested/c.json", "c");

            var files = _fs.ListFiles(_subDir);

            CollectionAssert.AreEqual(
                new[] { "a.json", "b.json", "nested/c.json" },
                files);
        }

        [Test]
        public void DeleteFile_RemovesFile_ReadTextReturnsNull()
        {
            var path = _subDir + "/to_delete.json";
            _fs.WriteTextAtomic(path, "x");

            var deleted = _fs.DeleteFile(path);

            Assert.IsTrue(deleted);
            Assert.IsNull(_fs.ReadText(path));
        }

        [Test]
        public void ReadText_NonExistentFile_ReturnsNull()
        {
            Assert.IsNull(_fs.ReadText(_subDir + "/does_not_exist.json"));
        }

        [Test]
        public void GetContentRootDir_DiffersFromGetUserDataDir_InUserDataMode()
        {
            // ADR-0016 决策 8：GetContentRootDir 与 GetUserDataDir 是两个独立目录。
            Assert.AreNotEqual(_fs.GetUserDataDir(), _fs.GetContentRootDir());
            StringAssert.Contains("StreamingAssets", _fs.GetContentRootDir());
        }

        [Test]
        public void ReadOnlyContentMode_WriteTextAtomic_ReturnsFalse_DoesNotThrow()
        {
            var contentFs = new UnityFileSystem(readOnlyContentMode: true);

            var ok = contentFs.WriteTextAtomic(_subDir + "/should_not_write.json", "x");

            Assert.IsFalse(ok);
            Assert.IsFalse(contentFs.Exists(_subDir + "/should_not_write.json"));
        }

        [Test]
        public void ReadOnlyContentMode_DeleteFile_ReturnsFalse_DoesNotThrow()
        {
            var contentFs = new UnityFileSystem(readOnlyContentMode: true);

            Assert.IsFalse(contentFs.DeleteFile(_subDir + "/anything.json"));
        }

        [Test]
        public void ReadOnlyContentMode_ResolvesUnderConfiguredContentRoot()
        {
            var contentRoot = Path.Combine(Application.persistentDataPath, _subDir);
            var contentFs = new UnityFileSystem(readOnlyContentMode: true, contentRoot: contentRoot);

            // 直接用普通模式实例把探针文件写进 contentRoot 对应的目录（等价于测试夹具预置好一份
            // 内容），验证只读模式实例确实从这个自定义 contentRoot 读取，而不是
            // Application.persistentDataPath 根目录。
            _fs.WriteTextAtomic(_subDir + "/probe.json", "{\"ok\":true}");

            Assert.AreEqual("{\"ok\":true}", contentFs.ReadText("probe.json"));
            Assert.AreEqual(contentRoot, contentFs.GetContentRootDir());
        }

        /// <summary>N06 复现与根治（architecture/落地计划/audit-68c9bed-20260907/code-review.md）：
        /// <see cref="UnityFileSystem.UserDataRootOverride"/> 未设置（默认、生产运行的唯一状态）时，
        /// <see cref="UnityFileSystem.GetUserDataDir"/> 必须如实返回真实
        /// <see cref="Application.persistentDataPath"/>——本用例锁定这条"不覆盖时行为不变"的基线，
        /// 防止未来改动误伤生产路径。</summary>
        [Test]
        public void GetUserDataDir_WithoutOverride_ReturnsRealPersistentDataPath()
        {
            Assert.IsNull(UnityFileSystem.UserDataRootOverride, "测试前置条件：本用例不应继承其它用例遗留的覆盖值");
            Assert.AreEqual(Application.persistentDataPath, _fs.GetUserDataDir());
        }

        /// <summary>N06 核心复现与根治：设置 <see cref="UnityFileSystem.UserDataRootOverride"/> 后，
        /// **全部**（包括本用例设置覆盖值之前就已经 new 出来的既有实例——见 <see cref="_fs"/>）
        /// <see cref="UnityFileSystem"/> 实例的 <see cref="UnityFileSystem.GetUserDataDir"/>/写入
        /// 操作都应改用覆盖值，且覆盖值必须不等于真实 <see cref="Application.persistentDataPath"/>
        /// 本身（只能是其下的专属子目录，不能是同一个目录——否则清空覆盖目录等价于清空真实存档
        /// 目录，N06 原始缺陷的本质）。用 try/finally 确保覆盖值不会泄漏到其它测试用例（静态字段，
        /// 见 <see cref="UnityFileSystem.UserDataRootOverride"/> 判断记录）。</summary>
        [Test]
        public void GetUserDataDir_WithOverride_RedirectsAwayFromRealPersistentDataPath_ForAllInstances()
        {
            var overrideRoot = Path.Combine(Application.persistentDataPath, "_playmode_tests_probe_" + System.Guid.NewGuid().ToString("N"));
            Assert.AreNotEqual(Application.persistentDataPath, overrideRoot, "覆盖目录必须是子目录，不能与真实存档目录本身相同");

            try
            {
                UnityFileSystem.UserDataRootOverride = overrideRoot;

                // _fs 是本用例 [SetUp] 里、设置覆盖值 **之前** 就已经构造好的既有实例——覆盖是
                // 全局 static、不是构造期快照，既有实例后续调用同样应该改道。
                Assert.AreEqual(overrideRoot, _fs.GetUserDataDir());
                Assert.AreNotEqual(Application.persistentDataPath, _fs.GetUserDataDir());

                var written = _fs.WriteTextAtomic("probe_under_override.json", "{\"ok\":true}");
                Assert.IsTrue(written);
                Assert.IsTrue(File.Exists(Path.Combine(overrideRoot, "probe_under_override.json")),
                    "写入应该真的落在覆盖目录下，不是真实 persistentDataPath 根目录");
                Assert.IsFalse(File.Exists(Path.Combine(Application.persistentDataPath, "probe_under_override.json")),
                    "不应该在真实 persistentDataPath 根目录下产生任何文件");
            }
            finally
            {
                UnityFileSystem.UserDataRootOverride = null;
                if (Directory.Exists(overrideRoot))
                {
                    Directory.Delete(overrideRoot, recursive: true);
                }
            }
        }
    }
}
