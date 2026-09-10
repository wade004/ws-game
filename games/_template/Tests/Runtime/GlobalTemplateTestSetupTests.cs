#nullable enable
// GlobalTemplateTestSetupTests：回归测试，稳定复现 PRES-118-CAMERA 复核阶段定位的根因——
// GlobalTemplateTestSetup（GameTemplateSmokeTests.cs）此前的实现只清理真实
// Application.persistentDataPath/saves 目录下 "game.template." 前缀的顶层文件，同一台开发机上
// toolchain/consumer_smoke.ps1/check.ps1 的 -gf-smoke/-gf-smoke-discrete 独立版 Player 步骤
// 反复写入的 "game.sample.*"/裸 "slot.*" 前缀存档槽、以及任何遗留在 backups/ 子目录里的备份
// 都不会被清理，跨越多轮本地重跑不断累积，最终把 SaveSystemOptions.MaxSlots（默认 20）顶满，
// 命中 SaveFailureReason.SlotLimitReached——真实复现见
// PRES118_TemplateContractTests.GameBootstrap_NewGame_CameraFollowsPlayer_AfterInWorld 在完整
// check.ps1 全量门禁里的失败记录（"RequestNewGame 应当成功 / Expected: True / But was: False"）。
// 本测试套件与 Adapter.Unity.Tests.Runtime.GlobalPlayModeTestSetupTests 是同一类回归的镜像版本，
// 只验证纯文件系统操作与静态路径事实，不依赖真实场景/世界装配。
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Game.Template.Tests
{
    public sealed class GlobalTemplateTestSetupTests
    {
        private string _tempSavesDir = null!;

        [SetUp]
        public void SetUp()
        {
            _tempSavesDir = Path.Combine(Path.GetTempPath(), "gf_template_saves_cleanup_test_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_tempSavesDir);
            Directory.CreateDirectory(Path.Combine(_tempSavesDir, "backups"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempSavesDir))
            {
                Directory.Delete(_tempSavesDir, recursive: true);
            }
        }

        /// <summary>核心回归断言：修复前的实现（只清"game.template."前缀顶层文件）对着这份布局跑完
        /// 会剩下 game.sample./裸 slot. 前缀的顶层文件与 backups/ 子目录里的文件——本测试对着修复后的
        /// <see cref="GlobalTemplateTestSetup.ClearAllSaveArtifacts"/> 断言这些一个不剩。</summary>
        [Test]
        public void ClearAllSaveArtifacts_RemovesFilesAcrossAllPrefixesAndBackupsSubdir()
        {
            // 顶层：game.template. 前缀（修复前唯一会被清理的一类）。
            WriteDummy("game.template.slot_smoke.json");
            WriteDummy("game.template.pres118_camera.json");

            // 顶层：其它前缀——修复前的实现完全不会碰它们，是真正的累积源头
            // （consumer_smoke.ps1/-gf-smoke 独立版 Player 写入的存档，见本类型判断记录）。
            WriteDummy("game.sample.slot_1.json");
            WriteDummy("slot.smoke.json");
            WriteDummy("slot.smoke_discrete.json");

            // backups/ 子目录下的备份（CountSlots 本就排除在配额之外，但清理逻辑仍应物理删除）。
            WriteDummy(Path.Combine("backups", "game.sample.slot_1.bak1.json"));

            var totalBefore = Directory.GetFiles(_tempSavesDir, "*", SearchOption.AllDirectories).Length;
            Assert.AreEqual(6, totalBefore, "测试前置条件：应当已落盘 6 个文件（覆盖全部前缀 + backups/ 子目录）");

            var removed = GlobalTemplateTestSetup.ClearAllSaveArtifacts(_tempSavesDir);

            Assert.AreEqual(6, removed, "应当报告删除了全部 6 个文件，不因前缀/所在子目录而遗漏");

            var remaining = Directory.GetFiles(_tempSavesDir, "*", SearchOption.AllDirectories);
            CollectionAssert.IsEmpty(remaining, "清理后 saves/ 目录树下不应再有任何文件残留（含 backups/ 子目录）");
        }

        [Test]
        public void ClearAllSaveArtifacts_DirectoryDoesNotExist_ReturnsZeroWithoutThrowing()
        {
            var missingDir = Path.Combine(_tempSavesDir, "does_not_exist");

            var removed = GlobalTemplateTestSetup.ClearAllSaveArtifacts(missingDir);

            Assert.AreEqual(0, removed);
            Assert.IsFalse(Directory.Exists(missingDir), "不存在的目录不应被本方法意外创建出来");
        }

        /// <summary>本用例只做纯字符串层面的校验（不调用 OneTimeSetUp/OneTimeTearDown 本身——那两个
        /// 方法已经在本次 -runTests 调用里被 NUnit 自动跑过一次、覆盖着当前进程全程有效的
        /// <c>UnityFileSystem.UserDataRootOverride</c>，本测试方法内不应该改动这个全局状态，避免
        /// 影响同一次运行里的其它测试夹具），只验证"目标路径长什么样"这一静态事实，且与
        /// Adapter.Unity.Tests.Runtime.GlobalPlayModeTestSetup 用的子目录名不同（见本类型判断
        /// 记录：二者按 NUnit 装配顺序先后运行，用不同目录名便于诊断日志区分，不代表两者可能同时
        /// 生效）。</summary>
        [Test]
        public void TestUserDataRoot_IsSubdirectoryOfRealPersistentDataPath_NotTheDirectoryItself()
        {
            var testRoot = GlobalTemplateTestSetup.TestUserDataRoot;
            var realRoot = Application.persistentDataPath;

            Assert.AreNotEqual(realRoot, testRoot, "专属测试目录不能与真实存档根目录相同");
            StringAssert.StartsWith(realRoot, testRoot, "专属测试目录必须是真实存档根目录下的子目录，不能是无关路径");
            Assert.AreEqual("_playmode_tests_template", Path.GetFileName(testRoot));
            Assert.AreNotEqual("_playmode_tests", Path.GetFileName(testRoot),
                "必须与 Adapter.Unity.Tests.Runtime.GlobalPlayModeTestSetup 的专属目录名不同，见本类型判断记录");
        }

        /// <summary>核心回归：本次 -runTests 调用已经跑过一次 <c>GlobalTemplateTestSetup.
        /// OneTimeSetUp</c>（NUnit 装配级钩子，早于本方法运行）——到本测试方法执行的这一刻，
        /// <c>UnityFileSystem.UserDataRootOverride</c> 应该已经被重定向到
        /// <see cref="GlobalTemplateTestSetup.TestUserDataRoot"/>，而不是保持默认 <c>null</c>
        /// （那样本套件全部测试期间新建的 GameBootstrap/SaveSystem 都会写到真实存档目录）。</summary>
        [Test]
        public void UserDataRootOverride_AlreadyRedirectedByAssemblyLevelOneTimeSetUp()
        {
            Assert.AreEqual(
                GlobalTemplateTestSetup.TestUserDataRoot,
                Adapter.Unity.EngineAdapter.UnityFileSystem.UserDataRootOverride,
                "本装配的 [SetUpFixture].OneTimeSetUp 应该已经在任何测试运行前把用户数据根重定向到专属测试目录");
        }

        private void WriteDummy(string relativePath)
        {
            var fullPath = Path.Combine(_tempSavesDir, relativePath);
            File.WriteAllText(fullPath, "{}");
        }
    }
}
