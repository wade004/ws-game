#nullable enable
// VerticalSliceTests：U3-3 灰盒竖切全流程测试（阶段 4 验收标准 1、4、5）。
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Adapter.Unity.Shell;
using Core.Foundation.Common;
using Core.Foundation.Common.Json;
using Core.Foundation.SaveSystem;
using Core.Foundation.SimLoop;
using NUnit.Framework;
using Presentation.Shell;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Adapter.Unity.Tests.Runtime
{
    public sealed class VerticalSliceTests : PlayModeTestBase
    {
        private const string SceneName = "Shell";
        private const string TierId = "diff.sample_story";
        private const string AttackSkillId = "skill.sample_strike";
        private const string Skill1Id = "skill.sample_burn";
        private const string BoltSkillId = "skill.sample_bolt";

        private static IEnumerator LoadShellScene()
        {
            SceneManager.LoadScene(SceneName);
            // 判断记录（与 GreyBoxTests.cs 同款）：仅等两帧 Update 不保证旧场景的 MonoBehaviour 完全卸载/停止泫化（实测复现过旧 GreyBox 场景的 GameFoundationBootstrap.FixedUpdate 在切换到 Shell.unity 后仍多跳一帧）；多等两次 WaitForFixedUpdate 与 GreyBoxTests.LoadGreyBoxScene 同一惯例。
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        private static ShellRoot RequireShellRoot()
        {
            var root = Object.FindFirstObjectByType<ShellRoot>();
            Assert.IsNotNull(root, "场景里应当有且仅有一个 ShellRoot 实例");
            return root!;
        }

        private static IEnumerator EnterInWorld(ShellRoot shell, string slotSuffix)
        {
            shell.Framework.Presentation.Shell.NewGame(new Id($"game.sample.slot_{slotSuffix}"), new Id(TierId), null);
            var guard = 1000;
            while (shell.Framework.Presentation.Shell.Page != ShellPage.InWorld && guard-- > 0)
            {
                yield return null;
            }

            // 判断记录：见 UiSuiteTests.EnterInWorld 同款判断记录——场景资源后台读取偶发失败时
            // 重试一次（换新槽位）。
            if (shell.Framework.Presentation.Shell.Page != ShellPage.InWorld)
            {
                shell.Framework.Presentation.Shell.NewGame(new Id($"game.sample.slot_{slotSuffix}_retry"), new Id(TierId), null);
                guard = 200;
                while (shell.Framework.Presentation.Shell.Page != ShellPage.InWorld && guard-- > 0)
                {
                    yield return null;
                }
            }

            Assert.AreEqual(ShellPage.InWorld, shell.Framework.Presentation.Shell.Page);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
        }

        private static void Cast(ShellRoot shell, string skillId) => shell.Framework.CastSkill(new Id(skillId));

        // 判断记录（PlayMode 全量门禁 283/284 根治：PRES180 对 TMP 缺字警告的顺序依赖，根因与
        // 对策）——本条判断记录只放这一处，两条用例（下方 FullVerticalSlice_.../PRES180_...）
        // 死亡飘字前各自调用一次本方法，均引用这里，不重复贴。
        //
        // 根因（已用 Unity 6000.3.23f1 随包源码逐行核实，不是猜测）：死亡飘字固定文案取
        // l10n.combat.sample_death_text（"阵亡"），由生产代码 Runtime/Presentation/
        // FloatingTextReceiver.cs 的 Show() 落到一个 Queue&lt;TextMeshPro&gt; 对象池上；这个池子
        // 挂在 FrameworkResidentHost（DontDestroyOnLoad，Bootstrap() 只在首次 Awake 执行一次，见
        // 该类型判断记录）身上，跨整个 -runTests 进程、跨全部 PlayMode 用例只构造一次、循环复用
        // 同一批 TextMeshPro 组件——Show() 只是把回收的旧实例 SetActive(true) 后 tmp.text = text，
        // 从未清空过旧文本。而 TMPro.TMP_Text.text 的 setter（Library/PackageCache/
        // com.unity.ugui@*/Runtime/TMP/TMP_Text.cs 第 116 行起）在"新值与当前 m_text 长度、内容都
        // 相同"时直接 return，不调用 SetVerticesDirty()/SetLayoutDirty()——也就不会重新走
        // GenerateTextMesh 里逐字符查字形的流程，TMP 缺字警告正是在那条路径里逐字符
        // Debug.LogWarning 出来的（TextMeshPro.cs 的 MISSING CHARACTER HANDLING 分支）。只要对象池
        // 里曾经有某个实例显示过一次"阵亡"（哪怕早因生命周期结束被 SetActive(false) 回收），下次不
        // 管是本用例还是另一条用例的死亡再取出同一个实例、再设一遍同样的"阵亡"，setter 的早退会让
        // 这次调用完全不触发字形查找，两条警告都不会再出现——这与 TMP_FontAsset 的字形缓存
        // （characterLookupTable，只缓存真正找到的字符，从未缓存过"阵"/"亡"这两个从未在占位字体里
        // 出现过的码点，已用 GetInstanceID()/表项数量核实前后不变）无关，纯粹是 TMP_Text 组件实例
        // 级别的"新旧文本相同则跳过重排版"优化，与跨用例复用的对象池叠加后产生的效果——具体哪个用例
        // 收到警告、哪个收不到，取决于死亡那一刻 Queue 里排到队头的具体是哪个实例，随执行顺序（本套件
        // 按 NUnit 默认顺序 Feedback_* 先于 FullVerticalSlice_* 先于 PRES180_*）变化，不受任何一条
        // 用例自己控制。
        //
        // 对策：死亡飘字必然发生之前，把当前场景里全部 TextMeshPro 组件（含对象池中因
        // SetActive(false) 已停用、默认的 FindObjectsByType 查询会跳过的实例——必须显式传
        // FindObjectsInactive.Include，否则恰好查不到这些实例）的 .text 重置为空字符串。空串与
        // "阵亡"长度必然不同，保证 setter 早退判定失败，接下来无论对象池吐出哪个实例显示"阵亡"，
        // 都会真实走一遍生成网格/查字形流程——两条警告因此与执行顺序、对象池此前的复用历史完全无关，
        // 每次都必然触发。只重置 .text 字段，不触碰生产代码/对象池实现本身；此刻场景里其它正在播放
        // 的飘字（如战斗过程中的伤害数字）被清空不影响任何断言——两条用例都只检查
        // FloatingText.SpawnedCount 计数或是否发生了飘字，不检查具体文本内容。
        private static void ResetFloatingTextBeforeDeathWarning()
        {
            var all = Object.FindObjectsByType<TMPro.TextMeshPro>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var tmp in all)
            {
                tmp.text = string.Empty;
            }
        }

        private static readonly string[] AnimStates = { "idle", "move", "attack", "cast", "hit", "death" };

        // 判断记录（PlayMode 全量门禁失败 2/3、3/3 根治，分诊报告
        // scratchpad/triage-playmode-1.44.0.md；本条判断记录只放这一处，两条用例（下方
        // FullVerticalSlice_.../PRES180_...）EnterInWorld 之后各自调用一次本方法，均引用这里，不
        // 重复贴）：
        //
        // 根因：本批"接线：适配层转发前缀路由入口"（3b87482）让 UnityResourceLoader.ResolveEffectDir
        // 把 sprite_anim.* 前缀正确解到 AssetRefConventions.SpriteAnimDir 目录；同一批"迁移：
        // ADR-0038 前缀契约数据迁移"（08e069a）新增了 assets/_sample/sprite_anim/
        // sample_hero_{idle,move,attack,cast,hit,death}/ 六个真实占位帧集目录（各含
        // atlas.png+frames.json，见 toolchain/import_sample_assets.py 的 SPRITE_ANIM_CLIPS/
        // _gen_sprite_anim_clip）。两者叠加后，UnityViewFactory.RequestAnimClipUpgrade 发起的
        // LoadAsync 第一次真正加载成功（此前该目录不存在，必然加载失败）——这是 sprite 型角色动画
        // 的一次真实功能修复，不是缺陷（详见 UnityResourceLoader.cs/UnityViewFactory.cs 判断记录与
        // 分诊报告失败 2/3、3/3 一节的完整根因链，此处不重复贴）。旧版本用 LogAssert.Expect 预期
        // "加载失败"警告的写法，断言前提已被同批改动证伪。
        //
        // 改法（治根因，不是删 Expect 了事）：改为轮询 + 正面断言"该状态确实从真实资源文件加载
        // 成功"——用 UnityResourceLoader.TryGetEffect 是否命中作为判据：只有
        // RequestAnimClipUpgrade 加载成功回调（RegisterClipFromEffect）才会把资源写进
        // UnityResourceLoader._effects 缓存；内部软件级单帧退化（RegisterSingleFrameClip + 静态
        // FallbackFrame）完全不经过 ResourceLoader，TryGetEffect 必然落空。不能靠帧数区分两者——
        // toolchain/import_sample_assets.py 当前只生成 1 帧占位（frames.json 的 frames 数组只有
        // 一个元素），与软件级单帧退化的帧数恰好相同；TryGetEffect 命中才是本次要验证的下游正确
        // 行为，能防住"资源又加载不到"（ResolveEffectDir 路由改错/占位帧集目录被误删）这一类未来
        // 回归。轮询而非立即断言：LoadAsync 把文件读取丢进后台线程，真正写入 _effects 缓存要等到
        // 下一次主线程 Tick（同 PlayModeIsolation.cs PendingLoadCount 判断记录），不能假设
        // EnterInWorld 返回时加载已经落地。
        //
        // 与 PRES180 历史回归（architecture/落地计划/排查复盘-2026-09-15-PlayMode-PRES180.md）的
        // 关系：该复盘的真因是"新游戏"入口不回满玩家资源池，导致 PRES180 末尾的 Assert.IsTrue(died)
        // 真实失败、又被 Unity Test Framework 的日志检查机制覆盖成一句无关警告——与本方法验证的
        // "动画资源是否加载成功"是两个完全独立的维度。本方法不触碰 PlayModeIsolation 的资源池回满
        // 逻辑（已在该文件"判断记录（新增：跨用例回满玩家资源池…"一节根治），也不改变 died 断言
        // 本身、不放宽 400 次攻击预算，PRES180 原本要防住的"死亡断言被日志检查覆盖"回归仍由 died
        // 断言 + 资源池回满两处共同兜底，未被本次改动削弱。
        private static IEnumerator AssertAnimResourcesLoadedSuccessfully()
        {
            var unityLoader = Adapter.Unity.EngineAdapter.UnityEngineHost.Ensure().ResourceLoader;

            var guard = 300;
            while (guard-- > 0)
            {
                var allLoaded = true;
                foreach (var state in AnimStates)
                {
                    if (!unityLoader.TryGetEffect(new Id($"sprite_anim.sample_hero_{state}"), out _))
                    {
                        allLoaded = false;
                        break;
                    }
                }
                if (allLoaded)
                {
                    break;
                }
                yield return null;
            }

            foreach (var state in AnimStates)
            {
                var resourceRef = new Id($"sprite_anim.sample_hero_{state}");
                Assert.IsTrue(unityLoader.TryGetEffect(resourceRef, out var effect),
                    $"状态 \"{state}\" 引用的动画资源 \"{resourceRef}\" 应当已经从真实占位帧集" +
                    "（assets/_sample/sprite_anim/）加载成功，不再是此前\"必然加载失败\"的旧行为" +
                    "（300 帧内仍未加载完成，判定为真正的回归）");
                Assert.Greater(effect.Frames.Length, 0,
                    $"状态 \"{state}\" 加载成功的动画资源至少应有一帧");
            }
        }

        // 判断记录（收边任务 3：本套件此前独有的 TearDown 已收敛到
        // Tests/Runtime/PlayModeIsolation.cs，由基类 PlayModeTestBase 统一调用——世界清空 +
        // AppState 复位 MainMenu（原"根治残留光环命中已清空世界"判断记录）、ResourceLoader
        // PendingLoadCount 轮询归零（原"根治相邻重负载用例的迟到异步日志"判断记录）两条逻辑与
        // 本套件此前的写法逐字一致，完整判断记录见 PlayModeIsolation.TearDownAfterTest 顶部注释，
        // 不在本文件重复）。

        [UnityTest]
        public IEnumerator FullVerticalSlice_NewGame_Move_Attack_Skill_Death_Save_Load_NoUnexpectedExceptions()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();

            // EnterInWorld 会为玩家（creature.sample_hero）挂接默认动画，data/_sample/display/
            // display.anim_set.sample_hero 声明了 idle/move/attack/cast/hit/death 六个状态的
            // resource_ref（sprite_anim.sample_hero_*，ADR-0038 数据迁移后前缀）——本批改动后这些
            // 资源已经真实可加载成功，验证方式见 AssertAnimResourcesLoadedSuccessfully 判断记录
            // （不再是此前"预期加载失败"的写法，那份旧判断记录已随断言前提一起过期）。
            yield return EnterInWorld(shell, "vslice");
            yield return AssertAnimResourcesLoadedSuccessfully();

            var playerId = shell.Framework.PlayerId;
            var startX = shell.Framework.World.GetEntity(playerId)!.Position.X;

            // 移动。
            for (var i = 0; i < 30; i++)
            {
                shell.Framework.Gameplay.Carriers.Movement.Request(Core.Carriers.Unit.MoveRequest.InDirection(playerId, new Vec2(1, 0)));
                yield return new WaitForFixedUpdate();
            }
            Assert.Greater(shell.Framework.World.GetEntity(playerId)!.Position.X, startX, "移动后玩家 X 坐标应当增大");

            Assert.IsTrue(shell.Framework.BeastEntityId.HasValue, "应当已经生成示例生物");
            var beastId = shell.Framework.BeastEntityId!.Value;

            // 技能 1（skill.sample_burn）先单独施放一次并等待其彻底结算完，避免"在场景切换/
            // 世界清空后才结算的延迟施法对着已消失目标抛异常"这一已发现的 core/ 时序缺口（同
            // Feedback_CritDamage_TriggersFreeze 判断记录）与后续普攻循环/存读档 ClearAll 产生
            // 时序耦合。
            //
            // 判断记录（U3 排障：原先固定等 60 帧不够，本身就是测试写错）：data/_sample/skill/
            // skill.aura_def.json 里 skill.aura_def.sample_burn 的 duration 是 6.0（秒），
            // periodic_damage 的 interval 是 1.0——按 Time.fixedDeltaTime 默认 0.02s 换算，光环
            // 完整结算需要约 300 个固定 tick 才会真正过期移除，原先写的 60 帧（约 1.2 秒）只够等
            // 到第一次周期伤害，光环本身远未过期。这在"死亡检测逻辑本身有 bug、测试提前因断言
            // 失败中止"时不会暴露（旧断言 World.GetEntity/Lifecycle 判断永远等不到"死亡"，协程在
            // 走到这一步之前就已经因为下面的 Assert.IsTrue(died,...) 失败而终止，未使用完的
            // instance），一旦按 IUnitAccess.IsAlive 正确判定死亡后测试能继续往下走到本用例末尾
            // 的存档->篡改->读档环节（SceneRouter.LoadScene 会 world.ClearAll()），仍在计时的
            // sample_burn 光环实例下一次 periodic tick 时会对着已经被 ClearAll 移出
            // IWorldSim 的旧生物 id 结算，命中 WorldUnitAccess.Require 抛
            // InvalidOperationException（崩溃到下一条不相关用例，实测复现于
            // YSorting_TwoEntitiesWithDifferentY_SortingOrderReflectsY）。改为轮询
            // IAuraQuery.HasAura 直到光环真正消失（不再硬编码帧数猜测持续时间），从根上避免"光环
            // 还没到期就往下走"这一测试自身的时序错误。
            var burnAuraDefId = new Id("skill.aura_def.sample_burn");
            Cast(shell, Skill1Id);
            var burnGuard = 500;
            while (shell.Framework.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(beastId, burnAuraDefId) && burnGuard-- > 0)
            {
                yield return new WaitForFixedUpdate();
            }
            Assert.IsFalse(shell.Framework.Gameplay.Carriers.Rules.Skill.AuraQuery.HasAura(beastId, burnAuraDefId), "skill.aura_def.sample_burn 应当能在 500 个固定 tick（远大于其 6 秒 duration）内彻底结算完");
            yield return null;

            // 普攻，直到目标死亡（unit.died）——普攻 cast_time=0，不产生"在途施法"。
            //
            // 判断记录（U3 排障：本用例此前用 World.GetEntity(beastId)==null || Lifecycle !=
            // Active 判断"死亡"，这是错的）：core/carriers/unit/core/WorldUnitAccess.cs
            // SetAlive 方法顶部注释明确记录了框架的既定设计——"死亡是逻辑状态，不是生命周期状态
            // ……继续以 alive = false 的形态存在于世界模拟中，直到刷新表/复活策略另行处理"，
            // 即战斗死亡不会立即把实体从 IWorldSim 里摘除（Entity.Lifecycle 保持 Active、
            // World.GetEntity 仍能查到），"尸体"要等刷新表/复活策略在后续某个时机才处理。本用例
            // 原先的判断条件因此永远不会为真（除非用例自己等到刷新点重新判定），实测复现：普攻
            // 400 次预算内生物从未被判定为"死亡"，断言必然失败——根因不是伤害不够或命中率问题
            // （示例数据 skill.sample_strike base_value=15 > creature.sample_beast 的
            // stat.stamina=8 换算出的生命值上限，正常一击即可致命），而是判断死亡的信号选错了。
            // 改用 IUnitAccess.IsAlive（Core.Rules.Combat.Resolver 结算落地生命值 <= 0 时会同步
            // 调 SetAlive(id, false)，见该类型"步骤 8：落地"）才是与框架文档一致的死亡信号；
            // World.GetEntity(beastId) == null 仍保留作防御性判断（万一将来刷新表提前把尸体摘除）。
            // W3b 发现补齐（不属于本用例历史两处已根治阻碍之一，见方法末尾判断记录）：
            // feedback.sample_death 的死亡飘字取 l10n.combat.sample_death_text（中文"阵亡"，见
            // data/_sample/l10n/l10n.text.json），占位字体 LiberationSans SDF（TMP 自带默认字体，
            // 见包 README"字体"一节）不含 CJK 字形，TextMeshPro 会记一条
            // Debug.LogWarning（非本模块代码触发，是 TMP 包内部对缺字形的降级处理）——这不是本用例
            // 覆盖范围内任何契约的违反（占位字体本就不含 CJK 字形是已知的占位资产限制，见 14 号文档
            // "Noto Sans CJK SC" 勘误，真正的 CJK 字体尚未接入占位资产管线），因此预期并放行这一条
            // 警告，不放宽 LogAssert.NoUnexpectedReceived() 对其它任何未预期日志的拦截力。
            // "阵亡"两个字都不在占位字体字形表内，TMP 逐字符各记一条警告（"阵"阵、"亡"亡），
            // 因此需要预期两条，不是一条。是否真的触发这两条警告取决于飘字对象池此刻吐出的具体
            // 实例此前是否已经显示过同样的"阵亡"文案（与执行顺序耦合），先调用
            // ResetFloatingTextBeforeDeathWarning() 把这一变量钉死（判断记录见该方法顶部），使
            // 警告必然触发、与顺序无关。
            ResetFloatingTextBeforeDeathWarning();
            var missingGlyphWarning = new System.Text.RegularExpressions.Regex(
                @"was not found in the \[LiberationSans SDF\] font asset");
            LogAssert.Expect(UnityEngine.LogType.Warning, missingGlyphWarning);
            LogAssert.Expect(UnityEngine.LogType.Warning, missingGlyphWarning);

            var floatingTextBefore = shell.Framework.FloatingText.SpawnedCount;
            var died = false;
            for (var attempt = 0; attempt < 400 && !died; attempt++)
            {
                Cast(shell, AttackSkillId);
                yield return new WaitForFixedUpdate();
                yield return null;
                var beastEntity = shell.Framework.World.GetEntity(beastId);
                died = beastEntity == null || !shell.Framework.Gameplay.Carriers.Units.IsAlive(beastId);
            }
            Assert.IsTrue(died, "持续普攻/施放技能后，示例生物应当死亡（IUnitAccess.IsAlive 应变为 false）");
            Assert.Greater(shell.Framework.FloatingText.SpawnedCount, floatingTextBefore, "战斗过程中应当至少产生过一次飘字");
            Assert.Greater(shell.Framework.Flash.TriggerCount, 0, "示例生物死亡（unit.died）应当至少触发一次闪白反馈（feedback.sample_death）");

            // H4 补齐（判断记录：掉落物 View 创建用确定性等待，不是固定帧数，见方法末尾恢复
            // LogAssert.NoUnexpectedReceived() 的判断记录）：CreatureDeathLootListener 订阅
            // unit.died 同步调用 LootHost.Drop，但 WorldSim.AddEntity 产生的 entity.created 走
            // IEventBus.Enqueue（排队，不立即派发，见该事件类型注释），具体在哪一次
            // DispatchPending/哪一个 tick 边界送达 ViewBinder 不是本用例需要关心的细节——轮询
            // "掉落物实体已经在世界里 且 ViewBinder 已经为它绑定 View"直到成立，比硬编码固定帧数
            // 更贴合"用确定性条件代替时间猜测"的既有测试哲学（同本方法上面 burnGuard/died 两处轮询
            // 惯例）。
            var lootGuard = 300;
            Id? lootEntityId = null;
            while (lootGuard-- > 0)
            {
                var lootEntities = shell.Framework.World.QueryEntities(new EntityFilter(kind: EntityKinds.Loot));
                if (lootEntities.Count > 0)
                {
                    lootEntityId = lootEntities[0].EntityId;
                    if (shell.Framework.Presentation.ViewBinder.TryGetView(lootEntityId.Value, out _))
                    {
                        break;
                    }
                }

                yield return new WaitForFixedUpdate();
                yield return null;
            }
            Assert.IsTrue(lootEntityId.HasValue, "示例生物死亡后应当结算出至少一件掉落物（creature.sample_beast 的 loot_table_ref）");
            Assert.IsTrue(
                shell.Framework.Presentation.ViewBinder.TryGetView(lootEntityId!.Value, out _),
                "掉落物实体应当已经绑定 View——data/_sample/display/display.map.json 已补齐 " +
                "display.map.sample_loot_pile 行（logical_id=loot.generic_pile），" +
                "DroppedLootEntity.TemplateId 现固定为该 id（见该类型判断记录）");

            // 存档 -> 篡改状态 -> 读档 -> 状态恢复。
            var slotId = new Id("game.sample.slot_vslice");
            var saveResult = shell.Framework.Presentation.Shell.OverwriteSlot(slotId, playTimeSeconds: null, displaySummary: null);
            Assert.IsTrue(saveResult.Success, "存档应当成功");

            var positionBeforeTamper = shell.Framework.World.GetEntity(playerId)!.Position;
            shell.Framework.World.GetEntity(playerId)!.Position = Vec2.Zero;

            // 判断记录：读档会切场景、重新为玩家挂接默认动画，但 UnityViewFactory.
            // _pendingAnimResourceLoads 对 sample_hero 六个状态的加载尝试早已在上方 EnterInWorld
            // 阶段各自发起过一次且永不移除（见方法开头判断记录），这里不会重复发起加载、也不会重复
            // 记警告，不需要再预期一遍。
            var loadResult = shell.Framework.Presentation.Shell.LoadGame(slotId);
            Assert.IsTrue(loadResult.Status == LoadStatus.Loaded || loadResult.Status == LoadStatus.LoadedFromBackup, $"读档应当成功，实际：{loadResult.Status}，{loadResult.Message}");
            var guard = 1000;
            while (shell.Framework.Presentation.Shell.Page != ShellPage.InWorld && guard-- > 0) yield return null;
            yield return new WaitForFixedUpdate();

            var positionAfterLoad = shell.Framework.World.GetEntity(playerId)!.Position;
            Assert.AreEqual(positionBeforeTamper.X, positionAfterLoad.X, 0.05, "读档后玩家位置应当恢复为存档时的状态");

            // H4 收官：恢复 LogAssert.NoUnexpectedReceived() 收尾检查（见方法上方两处判断记录）。
            // 此前两处阻碍均已根治：
            // 1）"UnityAudio.PlaySfx 占位音效未加载"一项此前已由 FrameworkResidentHost.
            //    PreWarmSfxResources 解决（世界装配阶段提前 LoadAsync，见该方法判断记录）。
            // 2）"掉落物没有匹配 DisplayInfo，UnityViewFactory 退化为空视图并记警告"——
            //    data/_sample/display/display.map.json 已补 display.map.sample_loot_pile 行
            //    （logical_id=loot.generic_pile），core/gameplay/loot.DroppedLootEntity 新增
            //    GenericDisplayTemplateId 常量、LootHost.Drop 把它写进新建实体的 TemplateId（此前
            //    从未设置，退化成逐实例不同的 EntityId，不可能有任何 display.map 静态行与之匹配）；
            //    "掉落结算与其表现层 View 创建之间没有确定性先后保证"这一时序缺口，改为本方法上方
            //    新增的确定性轮询（等 ViewBinder 真正绑定掉落物 View）从测试侧根治，不再猜测固定
            //    帧数、也不需要放宽 LogAssert 的拦截力。
            LogAssert.NoUnexpectedReceived();
        }

        // PRES-180 根治验收（architecture/落地计划/audit-e070e3f-20260908/presentation/
        // presentation-findings.md"存档抑制与掉落物 View 候选"）：同图（不切场景）读档路径下，
        // Core.Foundation.SaveSystem.SaveSystem.Load 把逐段 Load 包在 IEventBus.SuppressDispatch
        // 作用域内，作用域内经 Enqueue/PublishImmediate 提交的事件被直接丢弃——若读档前掉落物已经
        // 从 WorldSim/ViewBinder 消失（本用例用真实 LootHost.ClearDroppedExcept(空集合) 复现，与
        // architecture/落地计划/audit-e070e3f-20260908/presentation/SaveLootViewBindingProbe.cs
        // 同一手法），DroppedLootPersistable.Load 期间经 LootHost.RestoreDropped → IWorldSim.
        // AddEntity 把掉落物恢复到 WorldSim 时产生的 entity.created 会被丢弃、永不补发。本用例用
        // 真实 FrameworkResidentHost/GameFoundationBootstrap 整套装配跑一遍：验证根治后
        // Presentation.ViewBinding.ViewBinder 订阅 save.loaded 做的全量对账，能让掉落物读档后立即
        // 拥有一个真实 View（不是本套件其它用例用到的 PresentationCommon 单测 stub 工厂，这里走
        // UnityViewFactory 真实产出 UnitySpriteView/NullView）。
        [UnityTest]
        public IEnumerator PRES180_SameMapLoad_DroppedLootView_ReconciledAfterSuppressedEntityCreated()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();

            // 同 FullVerticalSlice_..._Save_Load_... 判断记录（AssertAnimResourcesLoadedSuccessfully
            // 顶部）：sprite_anim.sample_hero_* 六个状态本批改动后应当真实加载成功，不再预期"加载
            // 失败"警告。
            yield return EnterInWorld(shell, "pres180");
            yield return AssertAnimResourcesLoadedSuccessfully();

            Assert.IsTrue(shell.Framework.BeastEntityId.HasValue, "应当已经生成示例生物");
            var beastId = shell.Framework.BeastEntityId!.Value;

            // 同 FullVerticalSlice_..._Save_Load_... 判断记录：占位字体不含 CJK 字形，死亡飘字
            // "阵亡"两个字各记一条 TMP 缺字形警告；是否真的触发与飘字对象池此刻吐出的实例此前的
            // 复用历史耦合（本用例此前在全量门禁里稳定失败正是因为 FullVerticalSlice 先跑一遍
            // "阵亡"、消耗了对象池里唯一会再被复用的实例，本用例这里再设同样的文案不再触发字形
            // 查找），先调用 ResetFloatingTextBeforeDeathWarning() 钉死这一变量（判断记录见该方法
            // 顶部），使警告必然触发、与顺序无关，单独用 -testFilter 跑本用例同样成立。
            ResetFloatingTextBeforeDeathWarning();
            var missingGlyphWarning = new System.Text.RegularExpressions.Regex(
                @"was not found in the \[LiberationSans SDF\] font asset");
            LogAssert.Expect(UnityEngine.LogType.Warning, missingGlyphWarning);
            LogAssert.Expect(UnityEngine.LogType.Warning, missingGlyphWarning);

            var died = false;
            for (var attempt = 0; attempt < 400 && !died; attempt++)
            {
                Cast(shell, AttackSkillId);
                yield return new WaitForFixedUpdate();
                yield return null;
                var beastEntity = shell.Framework.World.GetEntity(beastId);
                died = beastEntity == null || !shell.Framework.Gameplay.Carriers.Units.IsAlive(beastId);
            }
            Assert.IsTrue(died, "持续普攻后示例生物应当死亡");

            // 确定性轮询掉落物实体与 View 都已就绪（同 FullVerticalSlice_... 判断记录）。
            var lootGuard = 300;
            Id? lootEntityId = null;
            while (lootGuard-- > 0)
            {
                var lootEntities = shell.Framework.World.QueryEntities(new EntityFilter(kind: EntityKinds.Loot));
                if (lootEntities.Count > 0)
                {
                    lootEntityId = lootEntities[0].EntityId;
                    if (shell.Framework.Presentation.ViewBinder.TryGetView(lootEntityId.Value, out _))
                    {
                        break;
                    }
                }

                yield return new WaitForFixedUpdate();
                yield return null;
            }
            Assert.IsTrue(lootEntityId.HasValue, "示例生物死亡后应当结算出至少一件掉落物");
            var lootId = lootEntityId!.Value;
            Assert.IsTrue(shell.Framework.Presentation.ViewBinder.TryGetView(lootId, out _), "掉落后、存档前应当已经绑定 View");

            // 存档：这份存档记录的是"掉落物仍在地上"这一刻的状态。
            var slotId = new Id("game.sample.slot_pres180");
            var saveResult = shell.Framework.Presentation.Shell.OverwriteSlot(slotId, playTimeSeconds: null, displaySummary: null);
            Assert.IsTrue(saveResult.Success, "存档应当成功");

            // 复现 PRES-180 前提（同 SaveLootViewBindingProbe.cs 判断记录"Simulate an already-cleared
            // same-map world"）：调用真实 LootHost.ClearDroppedExcept(空集合) 让掉落物从
            // LootHost/WorldSim 的跟踪表消失（真实业务方法，不经 SuppressDispatch，entity.destroyed
            // 正常派发，ViewBinder 正常销毁 View——这一步本身不是缺陷，只是复现前提）。
            shell.Framework.Gameplay.Loot.ClearDroppedExcept(System.Array.Empty<Id>());
            var clearGuard = 60;
            while (shell.Framework.World.GetEntity(lootId) != null && clearGuard-- > 0)
            {
                yield return new WaitForFixedUpdate();
                yield return null;
            }
            Assert.IsNull(shell.Framework.World.GetEntity(lootId), "复现前提：读档前掉落物实体应当已从 WorldSim 移除");
            Assert.IsFalse(shell.Framework.Presentation.ViewBinder.TryGetView(lootId, out _), "复现前提：读档前 View 应当已被销毁");

            // 读档：目标地图与当前地图相同（不切场景），DroppedLootPersistable.Load 会经
            // LootHost.RestoreDropped 把这件掉落物重新 AddEntity 回 WorldSim——PRES-180 根治前，
            // 由此产生的 entity.created 被 SaveSystem.Load 的 SuppressDispatch 作用域丢弃，
            // ViewBinder 永远收不到通知，逻辑实体存在但没有 View；根治后 ViewBinder 订阅
            // save.loaded 做一次全量对账，Load() 返回时 View 已经补建完毕，不依赖任何额外 tick。
            var loadResult = shell.Framework.Presentation.Shell.LoadGame(slotId);
            Assert.IsTrue(
                loadResult.Status == LoadStatus.Loaded || loadResult.Status == LoadStatus.LoadedFromBackup,
                $"读档应当成功，实际：{loadResult.Status}，{loadResult.Message}");
            var guard = 1000;
            while (shell.Framework.Presentation.Shell.Page != ShellPage.InWorld && guard-- > 0) yield return null;
            Assert.AreEqual(ShellPage.InWorld, shell.Framework.Presentation.Shell.Page);
            yield return new WaitForFixedUpdate();

            Assert.IsNotNull(shell.Framework.World.GetEntity(lootId), "同图读档后掉落物逻辑实体应当恢复到 WorldSim");
            Assert.IsTrue(
                shell.Framework.Presentation.ViewBinder.TryGetView(lootId, out var reconciledView),
                "PRES-180 根治点：同图读档完成的这一刻，掉落物应当已经拥有一个真实 View（ViewBinder 订阅 " +
                "save.loaded 做的全量对账补建），不需要任何额外操作或等待");
            Assert.IsNotNull(reconciledView, "补建的 View 不应为 null");

            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Feedback_CritDamage_TriggersFreeze()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return EnterInWorld(shell, "critbind");
            Assert.IsTrue(shell.Framework.BeastEntityId.HasValue);

            // 判断记录（改为合成事件直接验证绑定，不再靠战斗随机数真的命中暴击）：暴击本身只有
            // 5% 基础概率（见 data/_sample/combat/combat.hit_table_config.json），实测跨数十代生物
            // 数千次普攻仍可能因固定 RNG 种子在这条特定测试路径上一次都不触发（概率事件，不代表
            // 绑定有问题）；同时反复"死亡→新游戏重生"会让不同用例之间产生额外的场景切换/延迟施法
            // 时序耦合（另一判断记录：技能施法有 cast_time 的技能可能跨场景切换后才结算，对着已经
            // 因 ClearAll 被清空的旧目标抛异常——这属于另一处已发现但暂不深入修复的 core/ 时序缺口）。
            // 阶段 4 验收标准 4 真正要验证的是"feedback.binding 的暴击分支规则确实接到 freeze 动作
            // 上"——直接经本类型测试专用的 Bus 属性发布一条 is_crit=true 的 combat.damage_dealt
            // 合成事件，绕开战斗随机数，直接、确定性地验证这条绑定关系本身是正确的，比依赖概率更
            // 贴合"验证绑定"这一验收目的。
            var freezeBefore = shell.Framework.Freeze.TriggerCount;
            shell.Framework.Bus.PublishImmediate(new Core.Rules.Common.CombatDamageDealtEvent(
                shell.Framework.PlayerId, shell.Framework.BeastEntityId!.Value, new Id("school.physical"),
                amount: 15, isCrit: true, hitResult: Core.Rules.Common.HitResult.Crit));
            yield return null;

            Assert.Greater(shell.Framework.Freeze.TriggerCount, freezeBefore, "合成一条 is_crit=true 的 combat.damage_dealt 事件后，应当触发 feedback.sample_crit_damage 的 freeze 动作");
        }

        [UnityTest]
        public IEnumerator Feedback_NormalDamage_TriggersFloatingText()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return EnterInWorld(shell, "normal");
            Assert.IsTrue(shell.Framework.BeastEntityId.HasValue);

            var before = shell.Framework.FloatingText.SpawnedCount;
            for (var attempt = 0; attempt < 60 && shell.Framework.FloatingText.SpawnedCount == before; attempt++)
            {
                Cast(shell, AttackSkillId);
                yield return new WaitForFixedUpdate();
                yield return null;
            }
            Assert.Greater(shell.Framework.FloatingText.SpawnedCount, before, "命中后应当至少产生一次飘字（feedback.sample_normal_damage/sample_crit_damage 均含 floating_text 动作）");
        }

        /// <summary>
        /// 收边任务补齐（步骤 3：Unity 侧对齐——收边 I1 落地 <c>core/carriers/projectile</c> 后，
        /// 灰盒/竖切场景里施放 <c>skill.sample_bolt</c> 应看到投射物视图，见 <c>UnityViewFactory</c>
        /// 对 <c>projectile</c> 种类走 sprite 视图、<c>display.map.sample_bolt</c> 已在 I1 补齐）：
        /// 施放投射物技能 → 投射物实体出现（<c>EntityKinds.Projectile</c>）→ <c>ViewBinder</c>
        /// 为它绑定 View → 命中目标后（<c>ProjectileHost</c> 结算 <c>on_hit_effects</c> 并销毁自身，
        /// 见 07 载体层判断记录）实体与其 View 一并移除。惯例同上方"掉落物 View 创建"确定性轮询
        /// （不猜固定帧数，等条件真正成立）。
        /// </summary>
        [UnityTest]
        public IEnumerator Projectile_CastBoltSkill_ShowsProjectileView_ThenRemovedOnHit()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return EnterInWorld(shell, "projectile");
            Assert.IsTrue(shell.Framework.BeastEntityId.HasValue, "应当已经生成示例生物（skill.sample_bolt 的目标）");

            // 判断记录（不能只施法一次）：EnterInWorld 结束时示例生物刚生成不久，entity.created
            // 是 Enqueue（排队，不立即派发，见该事件类型注释）——EntitySpatialSyncHost 要等它派发
            // 完才会把新实体计入空间索引，第一次尝试施法可能命中 target_shape_ref 解析不到目标
            // （CastFailureReason.NoValidTarget），惯例同上方"普攻直到目标死亡"/"飘字"两处循环：
            // 每次循环都重新施法，不是只施法一次后被动轮询结果。
            var spawnGuard = 120;
            Id? projectileEntityId = null;
            while (spawnGuard-- > 0)
            {
                var projectiles = shell.Framework.World.QueryEntities(new EntityFilter(kind: EntityKinds.Projectile));
                if (projectiles.Count > 0)
                {
                    projectileEntityId = projectiles[0].EntityId;
                    if (shell.Framework.Presentation.ViewBinder.TryGetView(projectileEntityId.Value, out _))
                    {
                        break;
                    }
                }

                Cast(shell, BoltSkillId);
                yield return new WaitForFixedUpdate();
                yield return null;
            }
            Assert.IsTrue(projectileEntityId.HasValue, "施放 skill.sample_bolt 后应当生成一个 EntityKinds.Projectile 实体");
            Assert.IsTrue(
                shell.Framework.Presentation.ViewBinder.TryGetView(projectileEntityId!.Value, out _),
                "投射物实体应当已绑定 View（EntityKindMapping.TryMap 把 EntityKinds.Projectile 映射到 " +
                "ViewKind.Projectile，UnityViewFactory 按该种类创建 sprite 视图，display.map.sample_bolt 已登记）");

            // 命中后销毁：实体从 World 移除，其 View 也随之移除。
            var hitGuard = 300;
            while (shell.Framework.World.GetEntity(projectileEntityId.Value) != null && hitGuard-- > 0)
            {
                yield return new WaitForFixedUpdate();
            }
            Assert.IsNull(shell.Framework.World.GetEntity(projectileEntityId.Value), "投射物命中目标后应当被销毁移除（ProjectileHost 结算 on_hit_effects 后 despawn 自身）");

            yield return null; // 让 entity.destroyed 事件（Enqueue）派发到 ViewBinder 完成 View 移除。
            Assert.IsFalse(
                shell.Framework.Presentation.ViewBinder.TryGetView(projectileEntityId.Value, out _),
                "投射物实体销毁后，ViewBinder 应当已移除其对应 View");
        }

        [UnityTest]
        public IEnumerator YSorting_TwoEntitiesWithDifferentY_SortingOrderReflectsY()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return EnterInWorld(shell, "ysort");
            Assert.IsTrue(shell.Framework.BeastEntityId.HasValue);

            // 判断记录：玩家默认出生点 Vec2.Zero 与示例生物出生点 (5,0)（见 encounter.def/spawn.table
            // 均为 y=0）Y 坐标初始相同，本用例需要一个明确、足够大的 Y 差值才能判定排序，因此显式
            // 向 +Y 方向移动玩家若干 tick，制造真实的高度差，而不是依赖两者出生点恰好不同。
            for (var i = 0; i < 40; i++)
            {
                shell.Framework.Gameplay.Carriers.Movement.Request(Core.Carriers.Unit.MoveRequest.InDirection(shell.Framework.PlayerId, new Vec2(0, 1)));
                yield return new WaitForFixedUpdate();
            }
            yield return null;

            var playerPos = shell.Framework.World.GetEntity(shell.Framework.PlayerId)!.Position;
            var beastPos = shell.Framework.World.GetEntity(shell.Framework.BeastEntityId!.Value)!.Position;
            Assert.Greater(System.Math.Abs(playerPos.Y - beastPos.Y), 0.5, "测试前置：玩家与示例生物的 Y 坐标应当有明显差异（否则本用例无法判定排序）");

            var playerGroup = FindClosestSortingGroup(playerPos);
            var beastGroup = FindClosestSortingGroup(beastPos);
            Assert.IsNotNull(playerGroup);
            Assert.IsNotNull(beastGroup);

            // 09/包 README 排序公式：sortingOrder = layer*100000 - round(sortY*1000)，Y 越大排序值越小。
            if (playerPos.Y > beastPos.Y)
            {
                Assert.Less(playerGroup!.sortingOrder, beastGroup!.sortingOrder, "Y 更大的实体（玩家）sortingOrder 应当更小");
            }
            else
            {
                Assert.Greater(playerGroup!.sortingOrder, beastGroup!.sortingOrder, "Y 更大的实体（生物）sortingOrder 应当更小，即玩家应当更大");
            }
        }

        private static UnityEngine.Rendering.SortingGroup? FindClosestSortingGroup(Vec2 worldPos)
        {
            var target = new Vector3((float)worldPos.X, (float)worldPos.Y, 0f);
            return Object.FindObjectsByType<UnityEngine.Rendering.SortingGroup>(FindObjectsSortMode.None)
                .OrderBy(g => Vector3.Distance(g.transform.position, target))
                .FirstOrDefault();
        }

        [UnityTest]
        public IEnumerator Paperdoll_LayerOrder_MatchesDisplayMapDeclaredOrder()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return EnterInWorld(shell, "paperdoll");

            // display.map.sample_hero 声明 paperdoll_layers: ["body", "hand_main", "head"]（见
            // data/_sample/display/display.map.json），越靠后的层应当以更高的 sortingOrder 叠加在
            // 上层（人工核对步骤见包 README"人工验收清单"：在编辑器里选中玩家 LayersRoot 下的三个
            // SpriteRenderer，确认 Inspector 里的 Sorting Order 数值满足 body < hand_main < head）。
            // 前缀 "layer.creature_sample_hero__" 来自该行 sprite_set_id="sprite.creature.sample_hero"
            // （去掉 "sprite." 前缀、点号换下划线，见 SpriteViewBase.ResolveLayerResourceId）。
            //
            // 判断记录（2026-09-19 隐性顺序依赖根治，见 architecture/落地计划/
            // 排查复盘-2026-09-19-PlayMode-全局缓存清理反例.md"正面做法"一节）：三层纸娃娃资源各自是
            // 独立的资源引用（layer.creature_sample_hero__front__body/hand_main/head），经
            // UnityResourceLoader 异步加载（后台线程读取 + 每帧 Tick 在主线程完成）。EnterInWorld
            // 返回时不保证这些 LoadAsync 请求已经回调完成——此前本用例进入世界后立即快照
            // FindObjectsByType<SpriteRenderer>，单独跑（-testFilter）时资源大概率还没加载完成、
            // renderers.Count 为 0，只有全量门禁里更早的用例（如本套件的
            // FullVerticalSlice_.../PRES180_...，二者都会先 EnterInWorld 挂接同一个玩家外形）已经把
            // 这三份资源"预热"进跨用例共享的 UnityResourceLoader 缓存（DontDestroyOnLoad 单例）才
            // 侥幸通过——这是一处隐性执行顺序依赖，不是真的验证了纸娃娃层的渲染结果。改为轮询等待
            // （同 GreyBoxTests.PlayerView_ExistsAndLayerResourcesLoaded_NotPlaceholder 既有惯例），
            // 超时给出"等了多久、还差几层"的明确失败信息，不静默通过、不无限等待。
            List<SpriteRenderer> renderers = new List<SpriteRenderer>();
            var paperdollLoadTimeout = 5f;
            var paperdollLoadWaited = 0f;
            while (paperdollLoadTimeout > 0f)
            {
                renderers = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None)
                    .Where(r => r.sprite != null && r.sprite.name.StartsWith("layer.creature_sample_hero__"))
                    .ToList();
                if (renderers.Count >= 2)
                {
                    break;
                }
                yield return null;
                var delta = Time.unscaledDeltaTime > 0f ? Time.unscaledDeltaTime : 0.02f;
                paperdollLoadTimeout -= delta;
                paperdollLoadWaited += delta;
            }
            Assert.GreaterOrEqual(renderers.Count, 2,
                "玩家纸娃娃层应当至少渲染出两层精灵：等待 layer.creature_sample_hero__ 系列纸娃娃层" +
                $"精灵资源异步加载完成 {paperdollLoadWaited:F2}s 后仍只找到 {renderers.Count} 层");

            var bodyLayer = renderers.FirstOrDefault(r => r.sprite.name.EndsWith("__body"));
            var handLayer = renderers.FirstOrDefault(r => r.sprite.name.EndsWith("__hand_main"));
            var headLayer = renderers.FirstOrDefault(r => r.sprite.name.EndsWith("__head"));
            Assert.IsNotNull(bodyLayer, "应当能找到 body 层精灵");

            if (handLayer != null)
            {
                Assert.Less(bodyLayer!.sortingOrder, handLayer.sortingOrder, "hand_main 层应当叠加在 body 层之上（sortingOrder 更大）");
            }
            if (headLayer != null)
            {
                var reference = handLayer != null ? handLayer.sortingOrder : bodyLayer!.sortingOrder;
                Assert.Less(reference, headLayer.sortingOrder, "head 层应当叠加在其之前的层之上（sortingOrder 更大）");
            }
        }

        [UnityTest]
        public IEnumerator Pause_StopsWorldSimTick_MovementDoesNotAdvance()
        {
            yield return LoadShellScene();
            var shell = RequireShellRoot();
            yield return EnterInWorld(shell, "pausetick");

            shell.Framework.Presentation.UiIntents.Pause();
            yield return null;

            var positionAtPause = shell.Framework.World.GetEntity(shell.Framework.PlayerId)!.Position;
            for (var i = 0; i < 10; i++)
            {
                shell.Framework.Gameplay.Carriers.Movement.Request(Core.Carriers.Unit.MoveRequest.InDirection(shell.Framework.PlayerId, new Vec2(1, 0)));
                yield return new WaitForFixedUpdate();
            }
            var positionAfterPausedTicks = shell.Framework.World.GetEntity(shell.Framework.PlayerId)!.Position;
            Assert.AreEqual(positionAtPause.X, positionAfterPausedTicks.X, 0.0001, "AppState.Pause 期间 FrameworkResidentHost 不应推进 WorldSim.Tick，玩家位置不应变化");

            shell.Framework.Presentation.UiIntents.Resume();
        }
    }
}
