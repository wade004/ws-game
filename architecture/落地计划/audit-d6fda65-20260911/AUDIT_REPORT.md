# ws-game 1.18.0 文档与项目深度审核

审核对象：`d6fda65cd6b00ede5c5f6f606f724d2ab0f60603`，`main`，VERSION `1.18.0`。审核日：2026-09-11（Asia/Tokyo）。原仓库 `D:/workespace/ws-game` 保持只读；测试与报告位于独立工作树/证据目录。本轮没有修改框架产品代码、正式文档、版本或发布物。

本地正式发布包：`D:/workespace/ws-game/dist/ws-game-1.18.0.zip`，SHA256 `1700390fcfc7a86272cc151f49e50d5ca05f7d878d85e8217aeb4b78716bd721`；配套 lock 的版本/提交为 `1.18.0 / d6fda65`。源码隔离重建与这个正式包是不同证据对象，不要求二者 DLL 字节相同。

**结论：框架与具体游戏的主要代码边界成立，但当前版本存在需要修复的通用运行时与交付工具问题，也存在文档能力表与实现漂移。不能据现有自动化通过就判定整套框架无问题。**

本轮没有发现必须把某款游戏的职业、技能数量、剧情、装备成长或主线选择写进框架才能解决的问题。通用机制、默认装配的正确性与具体游戏策略是不同责任；报告只把前两类的实际缺陷放入框架修复单。

## 1. 优先处理的问题

优先级使用 P2（应修复的正常功能/交付缺陷）、P3（文档或低影响一致性问题）。以下是完整清单，不要求读者从历史审核中拼接。每项的输入、实际/预期和边界见分域报告与原始日志。

| ID | 优先级 | 问题与触发 | 影响 | 当前证据 |
|---|---|---|---|---|
| PRES-118-CAMERA | P2 | 新游戏进图完成后，CameraHost 清空跟随目标；生产入口未重新 Follow | 默认主镜头在玩家进入世界后停止跟随；模板相关开关也未实际透传 | 隔离 Unity 6000.3.23f1，真实 FrameworkResidentHost：`before=unit.sample_player`，`after=null`，断言失败 |
| CORE-118-QUEST | P2 | 接取任务→热重载删除定义→跟踪查询；再恢复同 ID、增加目标后更新新目标 | `KeyNotFoundException`；随后 `IndexOutOfRangeException`，开发期热更可破坏常驻任务宿主 | 源码探针及主审引用本地正式 1.18.0 Core DLL 的独立消费程序均复现 |
| CORE-118-CAST | P2 | 施法者死亡/消失已生效，但死亡事件在本次 SkillHost.Update 后派发 | 兜底提前删除 CastState，当前施法和排队请求都收不到应有的结束/清队列通知 | 正式 DLL：先派发对照 `Interrupted=1, QueueCleared=1`；延后派发实际 `0,0` |
| PRES-118-SFX | P2 | 重复播放同一缺失音频，第二次 pending 只能靠 Sfx.Update 超时清理；两个 Bootstrap 未调用它 | 声音请求遗留；离散 wait_for_playback 可持续等待 | 独立 .NET 600 帧等价调用仍 pending=1；显式 Update 对照恢复；真实 Unity 资源加载器重复缺资源行为复现 |
| PRES-118-VIEW | P2 | 同图读档恢复逻辑位置后，已有 View 的插值快照未刷新，期间没有新模拟步 | 暂停/等待输入时继续显示读档前坐标，直到下一次模拟 Tick | 真 SaveSystem + UnitPersistable + WorldSim + ViewBinder：逻辑 `(3,4)`，显示 `(30,40)`；补 Tick 对照恢复 |
| TOOL-118-ABI | P2 | public 实例属性改成同名同类型静态属性 | ABI surface 没编码属性调用形态，错误报告兼容；无法承担文档所称通用门禁 | 旧消费程序只编译一次：旧 DLL `VALUE=7`，新 DLL `MissingMethodException`；surface `breaks=0, RESULT=OK` |
| TOOL-118-LOCK | P2 | Release 已有 zip、lock 缺失，走 release.yml 的缺附件修复分支 | 新生成 lock 丢失 headless_dlls/validator_dlls，使 get_framework 退回兼容旧包的跳过校验路径 | 本地修改副本中的 Adapters.Stub.dll 后，正常 lock 阻断；按 repair 分支构造的 lock 却跳过附加校验并成功落地 |

### 修复位置与验收方向

1. **镜头**：`presentation/camera/core/CameraHost.cs:157–163`、`presentation/assembly/PresentationAssembly.cs:315–322`，以及三个生产装配入口。保留“是否跟随谁/切图是否重置”的可替换策略；默认 Follow 的装配根应在进图后重新建立自己的目标，模板 GameOptions 应真实透传。验收新游戏、切图、读档、reset=false，不能只测初始化后未加载场景的瞬间。
2. **任务热重载**：`core/gameplay/quest/core/QuestHost.cs:201–228,546–558,908,939`。删除定义可隔离保留历史进度，但所有活跃枚举/事件处理必须一致；恢复同 ID 必须迁移/重建目标数组或原子拒绝不兼容变化。不能以“调用者不该查询被删 ID”解释失败，因为 `GetActiveObjectives(unit)` 没传被删任务 ID。
3. **施法终结**：`core/rules/skill/core/CastPipeline.cs:438–448`，并检查 FinishCast 的同类兜底。合并为幂等的终结清理路径，保留每个 CastInstanceId 的结束事件；覆盖死亡已入队但尚未 Dispatch、实体销毁、队列替换与正常中断，避免重复通知。
4. **SFX**：`adapters/unity/.../Runtime/Bootstrap/GameFoundationBootstrap.cs:731–775` 与 `games/_template/Runtime/GameBootstrap.cs:523–568`，对照 `FrameworkResidentHost.cs:842–851`。统一维护调用，帧更新不应依赖模拟推进；同时评估失败 load 的 in-flight 标记清理/重试策略。缺资源应结束等待而不是卡住后续回合。
5. **读档视图**：`presentation/view_binding/core/ViewBinder.cs:298` 起。save.loaded 对留存 View 做位置/朝向快照重建，不能为了刷新画面额外推进世界逻辑；验收无 post-load SimTick、暂停/离散等待、既有 View 身份保持、装备外观同步。
6. **ABI**：`toolchain/abi_surface/SurfaceDumper.cs:113–137` 的属性序列化。纳入 getter/setter 的 static/virtual/final 等必要签名因素，并使 compare 正确判定；保留旧消费程序集实际运行对照。当前问题证明工具漏检，不代表本轮发现 1.18 正式公开属性已发生该变化。
7. **发布锁**：`.github/workflows/release.yml:478–520`，对照 `build.ps1:1389–1396` 和 `toolchain/get_framework.ps1:581–643`。从同一份已验证 zip 重建完整字段集合，含无头/validator DLL；不得重新构建 DLL 混入旧 zip。验收普通 lock 与 repair lock 同等校验，并覆盖被改动或缺失的附加 DLL。

## 2. 文档需要更新与代码尚未实现

完整逐章矩阵、文档文件锚点、修改方向和验收要求见 [doc-code-matrix.md](docs-project/doc-code-matrix.md)。当前最重要的差异：

- **格子吸附尚未实现**：04、13 与 ADR-0013 声明可选 grid_snap/cell_size、范围按格子中心采样，只有字段登记，没有运行消费。sim_loop README 已承认此事实，能力总索引却漏列。它不是某个具体游戏的专属要求；应实现通用策略，或经设计决策明确延期，不能仅改成“游戏责任”。
- **ADR 总清单滞后**：两处总导读仍写 23 条，实际已有 24 条；ADR 子目录索引本身正确。
- **根 README 分类过期**：ATB 已是“延期/预留”，teleport 元素结构已登记；未提供的是目标引用完整性检查。
- **编辑器说明版本不同步**：Markdown v2.5、HTML v2.2、README 宣称同步 v2.1。外部编辑器需要的新增契约不应只出现在其中一版。
- **导入工具责任写反**：14 的表格把工具具体实现放在游戏层；ADR-0014 与当前 toolchain 已将通用实现列为框架交付物。游戏负责具体资源、参数、阈值和专属扩展。
- **武器风格状态混列**：13 将命中帧开关、武器风格来源接线与 Swing/Impact 特效消费混写为“默认关闭/未接线”，需分别描述。
- **战斗算法说明不符实现**：combat README 声称偏斜/格挡必跳过暴击，Resolver 实际可叠加。06 未规定必须互斥，应先确认通用策略、同步文档/测试，不能按某款游戏的习惯直接改算法。
- **运行问题对应的说明也需更新**：任务删除/恢复的注释前提、施法终结假设、ABI 工具保证和发布恢复步骤，必须随行为修复同步，不应只改文字淡化缺陷。

明确不放入框架缺陷单：talent 完整点数管理、具体新局状态、escort 路线、具体游戏的位移轨迹碰撞、剧情/技能/装备内容、外部编辑器程序。Replay、Quest.Update、owner/day/vendor、采集时钟和可选校验规则属已提供机制而需显式接线；item.affix、ATB、day_cycle 等也要按“扩展位/延期/非目标”分别记录。详表列出了这些状态，不将其合并为“全部没实现”。

## 3. 整体项目与解耦结论

主要结构可继续沿用：六个核心/表现类库不依赖 Unity；引擎实现集中在 adapters；游戏接入模板、框架数据、样例数据有不同落点；核心服务通过契约、Options 和回调组装。未发现核心依赖 `ws-game-wow` 等具体游戏实现的引用证据。魔兽系统参考、通用职业/阵营/任务机制或样例占位内容本身不构成具体游戏耦合。

当前主要维护风险是**重复生产装配根发生漂移**和**“历史判断记录”覆盖不了当前真实行为**。SFX 只在一个入口修好、Camera 策略字段未透传，就是这种风险的具体表现。建议统一可复用的逐帧维护/生命周期装配，并保留每个生产入口的契约测试；不需要重写整个框架，也不应把所有场景特例塞进一个游戏专属 Bootstrap。

发布门禁应同时保留三类证据：源码自动化、正式发行物身份/消费测试、引擎实际运行。类型存在、装配接入、运行通过是三层结论；本轮缺陷说明三者不能互相替代。

## 4. 验证证据与边界

主要证据入口：

- [核心审核](core/core-review.md)：模块覆盖、两个核心缺陷、能力边界、补充静态检查。
- [表现与适配审核](presentation/presentation-review.md)：各入口接线、SFX/View/Camera、Unity 用例与日志。
- [执行验证](validation/validation.md)：门禁、正式包身份、ABI 基线、Python 环境、交付实验。
- [ABI 独立探针](docs-project/abi-property/result.json)、[旧消费程序换库输出](docs-project/abi-property/consumer-new.log)、[错误通过的 surface 报告](docs-project/abi-property/surface-report.txt)。
- [正式 Core DLL 独立消费复现](docs-project/core-independent/run.log)。首次工程构造误包含源码的日志单独标为 `invalid-first-source-duplicate.log`，不作正式 DLL 证据；最终工程已移除 Core 源码 Compile 项。
- [主审重新执行的表现探针](docs-project/presentation-independent.log)：复现相同 SFX 等待与直接读档位置差异，读取 Unity XML 确认失败测试与预期。View 问题限定为直接同图 Load 保留既有 View 的路径，不泛化到 Shell 读档重新创建场景的路径。
- [Unity 选测 XML](presentation/playmode-selected.xml)：本轮 10 项，9 通过、1 失败；失败为新增镜头行为 oracle。通过的既有项目包括装备读档重置、命中帧、离散装配与 VFX 跟随。它不是全量 Unity 测试。

已有 .NET 六工程测试本轮 **3208/3208 通过**。针对性探针显示其未覆盖上述全部边界。初次 check.ps1 -SkipUnity 的工具链 pytest 步骤失败；后续环境/重跑结果与缺历史 ABI 包的 SKIP 按 validation 原始记录分别列示，不把首次失败覆盖成“全量门禁 PASS”。

主审收尾时使用原仓库已存在的 **1.12.0 正式 zip** 作为显式 BaselineZip，将本地 **1.18.0 正式六 DLL** 复制成工具要求的独立 ArtifactsPath 布局，补跑严格 ABI 验证（输出 [abi-formal.log](docs-project/abi-formal.log)、[summary.txt](docs-project/abi-formal-112-to118/summary.txt)）。旧消费程序集只编译一次，两次运行均 exit 0；surface 比对 `breaks=0, additions=275`。这证明该旧消费程序对当前正式库的运行兼容性；并不消除上面独立反例证明的 ABI 工具属性盲点。不能再将整轮 ABI 状态概括为“缺基线未跑”。

本轮未做完整 Unity 全套、Standalone 全套、IL2CPP、完整独立 Unity 消费方演练或在线私服验证；不使用旧版本的成功日志代替。本次报告是深度质量审核，不是发布认证，也不是任何具体游戏的验收。

## 5. 后续修复顺序与验收责任

1. 先修通用生命周期与默认装配：镜头、任务 Reload、施法终结、SFX、读档视图。按模块分工，公共 Bootstrap 文件指定唯一整合者，避免并行重复改动。
2. 独立修 ABI/Release 工具；用保留的旧消费程序集、完整 lock 对照验收，不能只加实现同形测试。
3. 更新文档并单独决定 grid_snap 的通用实现或延期。按现有 ADR 流程确认改变的承诺；其余文字勘误无需借机改变设计边界。
4. 最终由 Astra 基于文件与原始证据复核；Luna 可承担有界实现和验证。当前仅交付审核结果，未启动修复或发布。

任何修复都不应添加具体游戏的职业/技能/剧情/经济策略。框架负责使已承诺机制和默认入口正确、可替换、可验证；游戏负责使用这些机制做产品选择。
