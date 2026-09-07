#nullable enable
// GlobalPlayModeTestSetup：PlayMode 测试装配级清理（U3 排障新增）。
//
// 判断记录（根因：存档槽会累积在一个真实、跨运行持久化的操作系统目录里，不会随每次 Editor
// 测试运行自动清空）：ISaveSystem.Save 的目标文件系统是 UnityFileSystem.GetUserDataDir()（即
// Application.persistentDataPath），这是宿主机上一个真实存在、长期保留的文件夹，不是每次
// -runTests 都会重置的沙箱；本测试装配下多条用例（ShellFlowTests/VerticalSliceTests/
// UiSuiteTests 等）各自用固定的槽位 id（如 "game.sample.slot_vslice"）反复调用
// NewGame -> ISaveSystem.Save，跨多次 Editor 测试运行不断在磁盘上新增文件；ISaveSystem.Save
// 对"目标槽不存在 + 已存在槽数量达到 SaveSystemOptions.MaxSlots（默认 20）"时返回
// SaveFailureReason.SlotLimitReached（见 core/foundation/save_system/core/SaveSystem.cs
// Save 方法），这是存档系统按设计工作的正常拒绝路径，不是 bug。
//
// 实测复现（本次排障过程）：在 Application.persistentDataPath/saves 下发现恰好 20 个（含历史
// 调试反复尝试留下的 "slot_crit"/"slot_crit_0".."slot_crit_8" 等槽位）非备份 .json 槽文件，
// 命中上限；ShellFlowTests.DeleteSlot_RemovesSlotFromSaveSlotsViewModel（上一次运行的
// DeleteSlot 已经把自己的槽删除，本次运行时它是"新槽"）与
// VerticalSliceTests.Feedback_CritDamage_TriggersFreeze（槽位 "game.sample.slot_critbind"
// 从未被成功创建过，也是"新槽"）各自都在这一步命中上限，Presentation.Shell.ShellHost.NewGame
// 在 _saveSystem.Save(...).Success 为 false 时直接 return false（见该类型源码），根本不会走到
// _sceneRouter.LoadScene——加诊断日志实测证实：整个测试运行里，scene.sample_field/
// nav.sample_field 两个资源的每一次加载都在下一帧内成功，从未观察到一次真实的资源加载失败。
// 上一位 agent"场景资源加载偶发失败、回退 MainMenu"的判断记录是误判：真正卡住的是存档槽配额，
// 与场景资源加载无关（唯一的"读取失败"诊断日志来自 UnityResourceLoaderTests 故意构造的缺失
// 资源用例，与 NewGame 流程无关）。而 VerticalSliceTests.FullVerticalSlice_...
// 用的槽位 "game.sample.slot_vslice" 恰好是历史累积中已经存在的槽（已存在的槽不受 MaxSlots
// 限制，只有"新建槽"才受限），所以它的 NewGame/EnterInWorld 不受本问题影响、能正常进入
// InWorld——该用例的失败是另一个独立问题（示例生物未在给定 tick 数内死亡），与本类型无关。
//
// 判断记录（修法：装配级 OneTimeSetUp 清空示例存档槽，不是放宽 core 的配额策略）：MaxSlots
// 配额本身是存档系统的既定行为（见 core/foundation/save_system/README.md"已存在槽数已达
// MaxSlots"分支），不是本任务允许改动的契约，也不该改——这是为真实游戏設计的合理上限。真正该
// 修的是"测试环境卫生"：PlayMode 测试不应该把状态残留进一个跨越多次独立测试运行、会不断累积
// 的真实操作系统目录里。
//
// 判断记录 2（第四方深度审核 followup-2026-09-07b，根治"只清 game.sample. 前缀不够"）：本文件
// 最初的实现只清空 "game.sample." 前缀的槽文件，理由是"避免误删同一台宿主机上其它非本示例数据
// 集的存档"。但 Application.persistentDataPath 由 companyName+productName 派生（见
// ProjectSettings.asset），是本 Unity 工程私有、专供 Editor/PlayMode 测试使用的目录——不存在
// 一个"其它真实游戏"会共用这个目录，那条顾虑不成立；与此同时，本装配（同一条 -runTests 命令
// 覆盖的全部 Adapter.Unity.Tests.Runtime 夹具）里还有大量测试用完全不同的前缀新建存档槽——
// 例如 game.template.slot_*（TemplateSmokeRunner/模板相关用例）、裸 slot.*（
// SmokeRunner/审核复现用例的 slot.audit_blockers_*、slot.smoke*）——一律不在 "game.sample."
// 前缀之内，原实现完全不会清理它们，导致它们跨越"同一台开发机上历次独立 -runTests 调用"不断
// 累积（此前判断记录里提到的"历史调试反复尝试留下的 slot_crit_0..slot_crit_8"就是这类残留的
// 一个例子）。再叠加 FND-01（core/foundation/save_system/core/SaveSystem.cs）把备份文件迁到
// 独立的 saves/backups/ 子目录、CountSlots 相应移除了按文件名猜测的 IsBackupFileName 过滤——
// 迁移前遗留在 saves/ 顶层的旧格式 "<slot>.bakN.json" 备份文件不会被本清理找到（既不匹配
// "game.sample." 前缀，也早于本次迁移写入），现在会被新版 CountSlots 当成一个个真实槽计入配额。
// 两者叠加，"game.sample." 之外的槽 + 迁移前遗留的旧格式备份，会在同一台开发机上把基线槽数顶到
// 逼近 SaveSystemOptions.MaxSlots（默认 20）；一次完整 -runTests（165+ 条用例，跨
// ShellFlowTests/UiSuiteTests/DiscreteCombatTests/审核复现夹具等）本身又会新建数十个不重复的
// "game.sample.slot_*" 槽——两者相加必然在执行序列中某个固定点越过 20，命中
// SaveFailureReason.SlotLimitReached，且此后同一次运行里任何"新建槽"都会持续失败（配额只增不减，
// 除非某条用例自己的 TearDown 恰好删槽）。这正是 VerticalSliceTests 四条用例
// （Paperdoll_LayerOrder_.../Pause_StopsWorldSimTick_.../Projectile_CastBoltSkill_.../
// YSorting_TwoEntitiesWithDifferentY_...）"单独跑（-testFilter VerticalSliceTests，7/7 全绿）
// 必过，混在完整 165+ 条 PlayMode 套件里跑必以同样 4 条、同样 'Expected: InWorld, But was:
// MainMenu' 失败"的根因——ShellHost.NewGame 在 _saveSystem.Save(...).Success 为 false 时直接
// return false（源码见该类型），根本不会走到 _sceneRouter.LoadScene，与场景资源加载、
// SceneRouter 代际隔离、WorldSim.Dispose/SimTimers.Clear 均无关（这三处经本轮复核未发现与本问题
// 相关的缺陷）。
//
// 修法：把清理范围从"只清 game.sample. 前缀的顶层文件"扩大为"整个 saves/ 目录树全清空"
// （<see cref="ClearAllSaveArtifacts"/>，含 backups/ 子目录与任何历史遗留的旧格式备份文件），
// 让每次 -runTests 调用都从同一个已知的空存档起点开始，彻底不受宿主机历史累积槽位数量影响。
// 回归测试：GlobalPlayModeTestSetupTests.ClearAllSaveArtifacts_RemovesFilesAcrossAllPrefixesAndBackupsSubdir
// （同目录）直接调用本文件导出的 <see cref="ClearAllSaveArtifacts"/>，构造"game.sample./
// game.template./裸 slot. 前缀 + backups/ 子目录 + 顶层旧格式 .bakN.json"的完整落盘布局，断言
// 清理后一个不剩——稳定复现"只清一个前缀不够"这一原始缺陷（该测试在本次修复前对着修复前的清理
// 范围断言会失败）。
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Adapter.Unity.Tests.Runtime
{
    /// <summary>NUnit 装配级清理：见文件顶部判断记录。作用范围是本命名空间
    /// （<c>Adapter.Unity.Tests.Runtime</c>）下的全部测试夹具，在其中任何一条测试运行之前
    /// 执行一次。</summary>
    [SetUpFixture]
    public sealed class GlobalPlayModeTestSetup
    {
        [OneTimeSetUp]
        public void ClearSampleSaveSlotsBeforeAnyTestRuns()
        {
            var savesDir = Path.Combine(Application.persistentDataPath, "saves");
            var removed = ClearAllSaveArtifacts(savesDir);
            Debug.Log($"[GlobalPlayModeTestSetup] 清理存档目录：目录={savesDir}，删除文件数={removed}");
        }

        /// <summary>递归清空 <paramref name="savesDir"/> 下的全部文件（正式槽 + backups/
        /// 子目录里的备份，以及任何历史遗留在顶层的旧格式 <c>&lt;slot&gt;.bakN.json</c>），
        /// 不按文件名前缀筛选——见文件顶部判断记录 2"为什么只清一个前缀不够"。<paramref
        /// name="savesDir"/> 目录本身不存在时视为"已经是空存档起点"，直接返回 0，不创建目录
        /// （<see cref="Core.Foundation.EngineAdapter.IFileSystem.WriteTextAtomic"/> 的实现会在
        /// 真正需要写入时自行创建，见该实现判断记录）。供 <see cref="ClearSampleSaveSlotsBeforeAnyTestRuns"/>
        /// 与 <c>GlobalPlayModeTestSetupTests</c> 回归测试共用，internal 可见性足够（同一
        /// Adapter.Unity.Tests.Runtime 程序集内）。</summary>
        internal static int ClearAllSaveArtifacts(string savesDir)
        {
            if (!Directory.Exists(savesDir))
            {
                return 0;
            }

            var removed = 0;
            foreach (var file in Directory.GetFiles(savesDir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(file);
                    removed++;
                }
                catch
                {
                    // 判断记录：清理失败（例如文件被占用）不阻断测试运行——尽力而为，最坏情况
                    // 退化回清理前的既有行为（可能命中 SlotLimitReached），不应该让整个测试运行
                    // 因为一次尽力而为的清理失败而无法启动。
                }
            }

            return removed;
        }
    }
}
