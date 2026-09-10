# 变更日志

本文件记录 ws-game（游戏技术基础架构框架仓库）各构建产物版本号之间的变更，格式遵循
[Keep a Changelog](https://keepachangelog.com/) 惯例；版本号遵循语义化版本
（[SemVer](https://semver.org/)）：`MAJOR.MINOR.PATCH`——MAJOR 表示不兼容变更（走 ADR 审批的
契约签名变化、存档格式不兼容、数据表字段删改）；MINOR 表示向后兼容的新增能力；PATCH 表示缺陷
修复与文档勘误。单一版本源见仓库根 `VERSION` 文件；版本号与发布流程见根 `README.md`"版本与发布"
一节。

### 编辑器相关契约

ADR-0018 决策 4 要求：本仓库对"编辑器项目（独立仓库，随具体游戏走，见
[ADR-0018](architecture/adr/0018-编辑器随游戏走与框架为此提供的交付物.md)）依赖的契约面"发生的
变更，单独标注、汇总索引在此小节，供编辑器项目维护者只看这一类条目即可判断新版本是否需要跟改
（不需要通读全部版本条目）——每条给出条目所在的版本区间与简述，详情见对应版本正文。

- **F1（1.13.0）**：`FieldSchema` 新增可选子结构登记
  （`Fields`/`Item`/`Variants`，`Object`/`Array` 两种字段种类）与 `VariantSchema` 契约类型；
  `DataRegistry` 按登记递归校验，新增检查名 `variant_discriminator`/`substructure_depth`/
  `unknown_subfield`；`toolchain/validator --json` 输出新增
  `disabled_optional_rules`/`enabled_optional_rules` 字段。
- **F2（1.13.0）**：新增核心库校验装配入口
  `Presentation.Assembly.ContentValidationAssembly.Run`/`CreateRegistry`（`toolchain/validator`
  与编辑器基础套件共用同一份装配代码）；新增第四个私服包 `com.gamefoundation.adapter.headless`
  （无头适配层，随构建产物分发，供内容编辑器等无头宿主使用）。
- **F3（1.13.0）**：新增元数据门禁
  `Presentation.Assembly.SchemaAudit`/`toolchain/validator --schema-audit`（六项检查，见下方
  `[1.13.0]` 正文"F3"小节）；`DataRegistry` 新增只读属性 `RegisteredSchemas`；开发期数据
  热重载标准实现 `games/_template/Runtime/DataHotReload.cs`（`Reload` 成功/失败经事件总线补发
  `data.load_completed`/`data.validation_failed`）；`.github/workflows/release.yml` 四包修正
  （编辑器项目若参照本仓库发布工作流的必需附件集合，需同步补齐第四个 `.tgz`）。
- **E1～E4 交付面变化（1.15.0，消费方反馈处理，
  [消费方反馈-2026-09-10-编辑器.md](architecture/落地计划/消费方反馈-2026-09-10-编辑器.md)）**：
  Release 新增预编译 `toolchain/validator`（附隔离用的空 `Directory.Build.props`）作为发布附件，
  编辑器项目不再需要自行现场编译 validator；`toolchain/get_framework.ps1` 改为自包含（不再
  dot-source 同目录 `_hash.ps1`），可单独下载使用；`ws-game.lock` 的 `source` 字段不再写本机
  绝对路径（`local_path` 改为 `zip_file_name`）；新增可选附件 `ws-game-<ver>-samples.zip` 与
  `get_framework.ps1 -WithSamples` 参数，用于下载并合并落地验收数据集样例。
- **E5（1.15.0）**：[ADR-0020](architecture/adr/0020-表达式词法器纳入公开契约.md)——
  `core/foundation/expr` 的 `ExprLexer`/`ExprToken`/`ExprTokenKind` 由 `internal` 改为
  `public`，`ExprLexer.Tokenize(string)` 成为公开的词法切分入口，编辑器等工具做语法高亮应改用
  该入口，不再自行正则切词。
- **E8（1.15.0）**：`toolchain/validate_data.py` 新增 `--json`，把骨架检查结果与
  `toolchain/validator --json` 输出合并为一份结构化 JSON 打印到标准输出（人类可读诊断改走标准
  错误），结构见脚本头部 docstring 与 `data/README.md`。
- **E10（1.15.0）**：`IDataRegistryView` 新增只读属性 `RecordCount`（带默认实现，按
  `Tables`/`GetAll` 求和，不要求已有实现类改动，不构成 ABI 破坏），`CreateRegistry` 路径（调用方
  自行持有 registry、自行调用 `Reload`）现在也能拿到精确记录计数，不必自行遍历求和。
- **ADR-0021（1.15.0，消费方反馈处理，
  [消费方反馈-2026-09-10-技能效果参数范围.md](architecture/落地计划/消费方反馈-2026-09-10-技能效果参数范围.md)）**：
  `FieldSchema` 新增可选 `Range` 字段（Number/Int 字段的取值范围登记），`toolchain/validator
  --list-tables --json` 每张表新增 `field_ranges` 导出，编辑器可据此在数值输入控件上就地校验，
  不必等到一次完整加载校验才发现越界。

## [Unreleased]

（尚未发布的变更累积在此，随下一次 `build.ps1 -Release` 归档为对应版本号的条目。）

### 修复

- 修复 `core/foundation` XML 文档注释：WorldMapSchema 未闭合标签与 cref/paramref 漂移（`ace367d`）。

## [1.15.0] - 2026-09-10

第十五轮外部审核（codex，基线 `76d16a5`/v1.14.0 自身，`architecture/落地计划/audit-76d16a5-20260910/`）
修复条目、消费方（内容编辑器项目）反馈 E1～E11、[ADR-0021](architecture/adr/0021-字段登记表纳入数值范围约束.md)
（字段登记表数值范围约束）、[ADR-0020](architecture/adr/0020-表达式词法器纳入公开契约.md)（表达式词法器
公开，随 E5 落地）、打断免疫统一门（消费方反馈 C06-PRE-01）、私服脚本 `start_registry.ps1` 的 `-Stop`
误拒根治均在本版本收口；核实表见 `architecture/落地计划/audit-76d16a5-20260910/followup-2026-09-10b.md`。

### 新增

- 字段登记表新增数值范围约束（[ADR-0021](architecture/adr/0021-字段登记表纳入数值范围约束.md)）：
  `FieldSchema` 对 Number/Int 字段支持可选范围登记（`FieldRange`），加载期新增 `field_range` 检查项；
  元数据门禁新增 `field_range_kind` 自洽检查；`toolchain/validator --list-tables --json` 新增
  `field_ranges` 导出。【编辑器相关契约】
- 18 处字段登记数值范围（依据见各字段旁 ADR-0021 判断记录注释），修复消费方反馈的"内容错误在加载期
  未被拦截"问题：`item.template.stack_size`、`econ.currency.cap`、`econ.vendor` 售卖条目
  `price_amount`/`stock_limit`、`loot.table` 的 `count_range.min`/`pick_count`/`guaranteed_min`、
  `stat.rating_conversion.points_per_percent`、`combat.hit_table_config` 各判定分支
  `base`/`crit_multiplier_base`/`glancing_damage_pct`、`combat.resist_curve` 的
  `reduction`/`max_reduction`、`skill.def` 的 `apply_aura` 效果 `params.duration_override` 与
  `charges.max`、`skill.aura_def` 周期效果（`periodic_damage`/`periodic_heal`）`params.interval`、
  `skill.proc_def.proc_chance`、`target.chain_def.max_targets`。
- 消费方反馈处理新增能力（逐条见下方"修复"小节"消费方（内容编辑器项目）反馈处理"）：**E4** 新增
  验收数据集附件 `ws-game-<ver>-samples.zip` 与 `get_framework.ps1 -WithSamples`；**E5**
  （[ADR-0020](architecture/adr/0020-表达式词法器纳入公开契约.md)）`ExprLexer`/`ExprToken`/
  `ExprTokenKind` 由 `internal` 改为 `public`，新增 `ExprLexer.Tokenize(string)` 公开入口；**E8**
  `toolchain/validate_data.py` 新增 `--json`；**E10** `IDataRegistryView` 新增只读属性
  `RecordCount`（带默认实现，不构成 ABI 破坏）；**E11** 新增
  `toolchain/format_data.py --schema-order[--check]`。

### 修复

- 根治 `toolchain/registry/start_registry.ps1` 的 `-Stop` 误拒缺陷：`-Detach` 就绪后按端口核实
  发现真正监听端口的 PID 与 `Start-Process` 记录的不一致而重写 `-PidFile`/`.meta.json` 时，改为
  写入真正监听端口的那个进程自身的启动时间（`Get-Process` 查得），不再使用任何"本次调用当下
  时刻"的代理值——此前会导致该进程仍在正常服务却被身份核验条件 (c) 误判为"PID 被系统复用给了
  另一个更早启动的无关进程"而拒绝停止。新增 `toolchain/tests/test_registry_stop_pidfile_rewrite_timestamp.py`
  静态 + 行为级回归。
- 修正外部打断（`interrupt` 效果原语）未查询目标免疫的问题：效果分发入口新增统一免疫门，
  `dispel`/`energize`/`teleport`/`move`/`trigger_spell`/`modify_cooldown`/`add_charge`/
  `learn_skill`/`projectile` 及扩展类效果原语同样补齐了此前只有伤害/治疗分支才有的免疫判定；
  打断免疫（动态光环或静态 `effect.interrupt` 标记）现在会正确保留目标读条/引导，免疫拦截时
  施法者一侧仍正常消耗资源与进入冷却；`apply_aura` 本身的免疫语义未拍板，暂不纳入（控制类光环
  免疫仍由光环宿主单独处理）。（消费方反馈 C06-PRE-01，`73cb55e`/`9cc4442`）
- 第十五轮外部审核（codex，基线 `76d16a5`/v1.14.0 自身，
  `architecture/落地计划/audit-76d16a5-20260910/`）修复条目：
  - **PJ114-01**：恢复 `SkillHost` 17 参数构造签名的兼容入口——1.14.0 新增 `IFactionMatrix?
    factions` 使公开构造签名从 17 参数增至 18 参数时未保留旧参数个数的转发入口，导致 1.13.0 正式
    consumer 只换 1.14 正式 DLL、不重新编译即抛 `MissingMethodException`，与本文件 1.14.0 迁移
    说明"无需重编译即可运行"矛盾；本次恢复后旧编译 consumer 换 DLL 重新可用，提交 `c35543d`。
    同批额外发现并修复了同一根因的 `Presentation.ViewBinder` 7 参数构造 façade 缺失（新增
    `EquipmentVisualSource` 可选参数导致的同类破坏），提交同为 `c35543d`。
  - **PJ114-02**：ABI 门禁在基线发行包缺失时不再记为 `PASS`——`toolchain/abi_probe.ps1` 缺基线时
    改为在门禁汇总中显式输出可见的 `SKIP`；面向正式发布的门禁路径新增严格模式（缺基线即判定门禁
    失败）；新增独立于"旧编译消费方换新库实跑"之外的"公开 API 表面差异"门禁（以最后已知兼容基线
    为准，比对公开类型/成员删除、签名变化、接口新增无默认实现成员）；探针落盘保存基线包/库文件
    校验值、消费方程序集校验值与两次运行结果（提交 `f0e5cd8` 新增 `toolchain/abi_surface`
    工具本身；提交 `b3c898e` 接线 `check.ps1` 可见 SKIP/`-AbiStrict`/持续集成环境下载基线包）。
  - **PJ114-03**：`toolchain/abi_probe.ps1` 的显式已有输出目录不再被无边界递归删除——改为默认生成
    唯一目录，显式传入的已有目录直接拒绝或经专用临时根与唯一前缀验证，提交 `f0e5cd8`。
  - **CORE114-01**：`QuestHost` reload 后活跃任务目标数组不再可能越界——旧定义目标数减少/增加/
    重排时迁移已有进度计数，结构不兼容时原子拒绝该次 reload，提交 `89b1b69`。
  - **CORE114-02**：`EconomyHost` 既有 vendor/item 从 `none` reload 为 `timer` 补货策略时，正确
    初始化/重采样计时器（此前遗留 `null` 计时器导致库存永久停留在 0），并守恒切换前已消耗的库存
    进度，提交 `89b1b69`。
  - **CORE114-03**：`StatHost` 派生值缓存随 reload 一致失效并按新定义重算，不再出现同一新定义下
    因访问顺序不同而返回不同历史遗留值，提交 `89b1b69`。
  - **CORE114-04**：`ExprValueJson.IsValid` 捕获非法 `$id`（不再冒泡 `ArgumentException`），正式
    `ContentValidationAssembly` 对 Quest/Dialog world flag value 的非法形状输出可定位的 blocking
    报告项，提交 `89b1b69`。
  - **P3**：`core/gameplay/common/contracts/RewardSchemaFields.cs:9-13` 过时注释（仍称
    `FieldSchema` 不能表达嵌套）更正为描述当前 Object/复合 schema 登记能力的实际范围，提交
    `89b1b69`。
  - 套装缓存（Equipment set cache）候选排查：补充 resident equip→reload→unequip 独立负例测试，
    确认存在与 CORE114-03 同构的派生缓存分歧（`EquipmentHost.RecomputeSetBonuses` 未释放"孤儿"
    套装门槛光环句柄），已按排查结论修复，提交 `89b1b69`。
  - 归档、核实表与逐项判断/修复位置/验收见
    `architecture/落地计划/audit-76d16a5-20260910/followup-2026-09-10b.md`。
- 消费方（内容编辑器项目）反馈处理，逐条现象/根因/处理方式/验证结果见
  [消费方反馈-2026-09-10-编辑器.md](architecture/落地计划/消费方反馈-2026-09-10-编辑器.md)：
  - **E1**：`toolchain/validator` 现场编译受消费方 `Directory.Build.props` 影响导致校验失败——
    Release 现附带预编译的 `validator/bin/`（附隔离用的空 `Directory.Build.props`），
    `validate_data.py` 改为优先执行预编译产物，找不到才退回现场编译，提交 `dc6e2e5`。
  - **E2**：`get_framework.ps1` 依赖同目录 `_hash.ps1` 造成"先有鸡还是先有蛋"式引导死锁——改为
    内联 `Get-Sha256FileHash` 函数体，脚本自包含、可单独下载使用，提交 `dc6e2e5`。
  - **E3**：`ws-game.lock` 的 `source.local_path` 写入本机绝对路径导致换机器出现无意义
    diff——改写为相对的 `zip_file_name`，判定"是否需要改写锁文件"改为只比较引用内容字段，
    提交 `dc6e2e5`。
  - **E4**：验收数据集 `data/_sample`/`assets/_sample` 不随 zip 分发——新增独立附件
    `ws-game-<ver>-samples.zip` 与 `get_framework.ps1 -WithSamples` 下载合并落地，提交
    `dc6e2e5`。
  - **E5**：Expr 词法器不在公开契约内——新增 [ADR-0020](architecture/adr/0020-表达式词法器纳入公开契约.md)，
    `ExprLexer`/`ExprToken`/`ExprTokenKind` 改为公开，新增 `ExprLexer.Tokenize(string)` 入口，
    提交 `396afbc`。
  - **E6**：示例数据 `quest.sample_hunt` 触发"疑似引用拼写错误"误报——根治
    `ExprValidator` 规则本身（实参位置期望类型恰为 `Id` 时不再检查拼写），不是改数据迁就，
    提交 `3deba37`。
  - **E7**：`toolchain/gen_placeholder_assets.py` 在 Windows 上写出 CRLF——6 处 `write_text`
    补齐 `newline="\n"`，新增静态扫描测试防回归，`check.ps1` 新增"工作树文本文件无 CR"步骤，
    提交 `3deba37`。
  - **E8**：`validate_data.py` 未透传 `--json`——新增 `--json`，骨架检查与
    `toolchain/validator --json` 输出合并为一份结构化 JSON，提交 `3deba37`。
  - **E9**：`--data-root` 相对路径按脚本安装位置解析而非调用方当前工作目录——改为按
    `Path.cwd()` 解析，提交 `3deba37`。
  - **E10**：`ContentValidationAssembly.CreateRegistry` 路径拿不到记录计数——`IDataRegistryView`
    新增带默认实现的 `RecordCount` 只读属性（不破坏 ABI），提交 `3deba37`。
  - **E11**：示例数据字段顺序与 schema 声明顺序不一致——新增
    `toolchain/format_data.py --schema-order[--check]`，对 `data/_sample` 6 个文件重排并纳入
    `check.ps1` 门禁，提交 `3deba37`。
  - 逐条复现、根因、验证结果与迁移提示见
    `architecture/落地计划/消费方反馈-2026-09-10-编辑器.md`（该文档本身提交 `f3f2d20`）。

### 文档与门禁

- 新增文档：`architecture/落地计划/消费方反馈-2026-09-10-编辑器.md`（E1～E11 逐条归档，提交
  `f3f2d20`）、`architecture/落地计划/消费方反馈-2026-09-10-技能效果参数范围.md`（ADR-0021 消费方
  反馈归档）、[ADR-0020](architecture/adr/0020-表达式词法器纳入公开契约.md)、
  [ADR-0021](architecture/adr/0021-字段登记表纳入数值范围约束.md)、
  `architecture/落地计划/audit-76d16a5-20260910/followup-2026-09-10b.md`（第十五轮审核核实表）。
- 顶部"编辑器相关契约"索引：E1～E4、E5、E8、E10、ADR-0021 各条版本标注由"Unreleased"改为
  "1.15.0"（详见文首索引小节，与本版本正式发布对齐）。
- 门禁新增：`check.ps1`"工作树文本文件无 CR"步骤（消费方反馈 E7）、`data/_sample
  --schema-order --check` 步骤（消费方反馈 E11）；`build.ps1 -Release` 第 5 步固定传
  `-AbiStrict`（PJ114-02 根治，见下方"兼容声明"）。新增回归测试
  `toolchain/tests/test_registry_stop_pidfile_rewrite_timestamp.py`、
  `core/foundation/data_registry/tests/FieldRangeValidationTests.cs`、
  `core/rules/skill/tests/SkillEffectParamRangeTests.cs`。

### 迁移说明

面向消费方（游戏侧工程、内容编辑器等无头宿主），逐条可执行：

a. **Release 附件集合由 6 件变为 9 件**：`ws-game-<ver>.zip`、`ws-game-<ver>.lock`、
   `ws-game-<ver>-samples.zip`、四个私服包 `.tgz`
   （`com.gamefoundation.adapter.unity`/`com.gamefoundation.framework-data`/
   `com.gamefoundation.toolchain`/`com.gamefoundation.adapter.headless`）、自包含的
   `get_framework.ps1`、`_hash.ps1`（兼容旧还原脚本）。消费方可以直接从 Release 页面下载
   `get_framework.ps1` 单个文件使用，不必先解压主 zip 再取脚本。
b. **锁文件 `ws-game.lock` 的 `source` 字段不再含绝对路径**（`local_path` 改为相对的
   `zip_file_name`），新增 `samples.sha256`（`-WithSamples` 校验用）与 `validator_dlls`/
   `headless_dlls` 预编译产物哈希字段；判定"是否需要改写本机锁文件"改为只比较这些引用内容字段，
   不再比较路径本身；不含这些新字段的旧锁文件仍可读（跳过对应校验并提示，向后兼容）。
c. **新增 `get_framework.ps1 -WithSamples`**：下载并按锁文件 `samples.sha256` 校验、解压合并
   `ws-game-<ver>-samples.zip`（含 `data/_sample`/`assets/_sample` 验收数据集）到本地落地目录。
d. **`validate_data.py` 优先运行预编译 validator**（找不到才退回现场编译，规避消费方自带
   `Directory.Build.props` 干扰）；新增 `--json`（骨架检查与 `toolchain/validator --json` 输出
   合并为一份结构化 JSON 打印到标准输出，人类可读诊断改走标准错误）；`--data-root` 相对路径改为
   按**调用方当前工作目录**解析（此前按脚本安装位置解析）——旧用法若依赖"相对脚本目录"这一行为，
   需要相应调整调用处的相对路径或改传绝对路径。
e. **`FieldSchema` 新增范围登记与 `field_range` 检查**：已登记范围的字段若消费方数据取值越界，
   会在加载期被阻断。本版本登记范围的字段共 18 处，分布在 11 张表：`item.template`
   （`stack_size`）、`econ.currency`（`cap`）、`econ.vendor`（售卖条目 `price_amount`/
   `stock_limit`）、`loot.table`（`count_range.min`/`pick_count`/`guaranteed_min`）、
   `stat.rating_conversion`（`points_per_percent`）、`combat.hit_table_config`（判定分支
   `base`/`crit_multiplier_base`/`glancing_damage_pct`）、`combat.resist_curve`
   （`reduction`/`max_reduction`）、`skill.def`（`apply_aura` 效果 `params.duration_override`/
   `charges.max`）、`skill.aura_def`（周期效果 `params.interval`）、`skill.proc_def`
   （`proc_chance`）、`target.chain_def`（`max_targets`）；每处范围的依据见对应 schema 文件里
   紧邻 `.WithRange(...)` 调用的 ADR-0021 判断记录注释。`toolchain/validator --list-tables
   --json` 每张表新增 `field_ranges` 导出，供编辑器就地校验数值输入控件。
f. **`ExprLexer.Tokenize(string)` 公开**：`core/foundation/expr` 的 `ExprLexer`/`ExprToken`/
   `ExprTokenKind` 由 `internal` 改为 `public`，编辑器等工具做语法高亮应改用该入口，不再自行
   正则切词。
g. **`IDataRegistryView.RecordCount` 新增默认实现**：`CreateRegistry` 路径（调用方自行持有
   registry、自行调用 `Reload`）现在也能拿到精确记录计数，不必自行遍历求和；不要求已有实现类
   改动，不构成 ABI 破坏。
h. **打断免疫门扩展到全部效果原语**（`apply_aura` 除外）：`dispel`/`energize`/`teleport`/
   `move`/`trigger_spell`/`modify_cooldown`/`add_charge`/`learn_skill`/`projectile` 及扩展类
   效果原语现在同样受打断免疫判定约束——依赖"免疫不拦打断/驱散/位移"这一旧行为（即这些效果
   此前对免疫目标同样生效）的内容需要复核。
i. **定义热重载语义补强**：`QuestHost` reload 后活跃任务目标数组迁移已有进度计数（结构不兼容时
   原子拒绝该次 reload）；`EconomyHost` 补货策略从 `none` 改为 `timer` 时正确初始化计时器；
   `StatHost`/`EquipmentHost` 派生值与套装门槛缓存随 reload 一致失效重算。均不需要调用方改动
   既有调用点。
j. **`SkillHost`/`Presentation.ViewBinder` 旧构造签名重新恢复为 `[Obsolete]` 兼容重载**：
   1.14.0 曾意外破坏这两处的旧构造签名（见下方"兼容声明"），本版本恢复；旧编译 consumer 换
   1.15.0 正式 DLL 不需要重新编译。

### 兼容声明

本版本对 1.12.0/1.13.0/1.14.0 旧编译消费方保持二进制兼容：依据是 `toolchain/abi_probe.ps1`
严格模式下的两项独立验证——① 1.12.0 基线 consumer 程序集不重新编译、直接换上本版本正式 DLL 实跑
通过；② 独立的公开 API 表面差异比对（`toolchain/abi_surface`，以最后已知兼容基线为准）输出
`breaks=0`；`build.ps1 -Release` 发布门禁固定传 `-AbiStrict`（PJ114-02 根治），缺基线或探针未
真正跑起来会使门禁判定失败而不是静默放行。

**1.14.0 的兼容声明曾不成立**：该版本"迁移说明"所述"旧编译的 1.12 consumer 换正式 1.14 DLL、不
重新编译即可继续运行"这一结论，对 `SkillHost` 构造签名不成立——`IFactionMatrix? factions` 参数的
加入使旧有的 17 参数构造签名一并消失，同批还漏了 `Presentation.ViewBinder` 7 参数构造 façade
（`EquipmentVisualSource` 可选参数加入导致同类破坏）；第十五轮外部审核以独立 ABI 探针实跑复现
（1.13.0 正式 consumer 只换 1.14 正式 DLL、不重新编译，退出码由 0 变为 11，报告
`MissingMethodException`）。按 [12_扩展与变更流程.md](architecture/12_扩展与变更流程.md) 第 4 节
"发布不可变"约定，已发布的 `[1.14.0]` 小节正文不回头修改；本版本（PJ114-01）恢复两处兼容入口并以
独立探针实跑证明，上方"迁移说明"j 项记录修复结果。

### 版本判据说明

新增能力（字段数值范围登记与 `field_range`/`field_ranges` 导出、打断免疫门扩展、定义热重载语义
补强、若干消费方交付面改进）与修复（`SkillHost`/`ViewBinder` 兼容 façade 恢复、ABI 门禁
SKIP/严格模式、热重载缓存/库存/目标数组等第十五轮审核问题、`-Stop` 误拒、消费方反馈
E1～E11）——均不改变任何现有公开签名的必填参数个数（新增字段登记与只读属性均可选/带默认实现）、
不删改任何数据表既有字段、不改变存档格式，按 SemVer 判定为 MINOR。

## [1.14.0] - 2026-09-10

第十四轮（修订版）审核修复（codex，基线 `c9ff301`/v1.13.0，
`architecture/落地计划/audit-c9ff301-20260909/`）：6 项 P2 + 2 项静态差距（`ISkillHost.FindUnits`、
VFX 锚点持续跟随）+ 3 项 P3 + 文档/能力索引均已核实并修复，核实结论见该目录
`followup-2026-09-10.md`。核心侧修复提交 `c65258e`，表现/模板侧修复提交 `775de54`，文档与门禁提交
`4a60ca2`，审计归档提交 `acbdf99`。

### 新增/恢复

- **ABI 兼容 façade**（`[Obsolete]`，内部委托给现有实现，行为与 1.12 完全一致）：
  `Core.Foundation.DataRegistry.FieldSchema` 七参数构造函数；六个已删除公开规则类型
  `Core.Carriers.Gobj.GobjOnUseKindRule`/`GobjLockRequirementFieldGroupRule`、`Core.Rules.Skill.
  EffectKindRegisteredRule`/`CostEntryShapeRule`/`ChargesShapeRule`、`Core.Gameplay.AreaTrigger.
  AreaTriggerShapeKindRule`；两处构造签名收窄的兼容重载 `Core.Gameplay.Economy.
  EconomyContentValidationRule(IExprSchema?)`/`Core.Gameplay.Loot.LootContentValidationRule
  (IExprSchema?)`。
- `Presentation.Render.IEquipmentVisualResettable`（新接口，`SpriteViewBase`/
  `Adapter.Unity.Presentation.UnityModelView` 实现）：装备表现 SaveLoaded 读档对账先清旧外观再
  应用快照。
- `Presentation.VfxSfx.Contracts.IParticleRepositioner`（新可选能力接口，`Adapter.Unity.
  EngineAdapter.UnityRenderer2D` 实现）：VFX anchor/socket 降级场景的锚点持续跟随；`VfxPlayer.
  Update` 默认调用，未实现该接口的引擎适配层行为与改动前一致。
- `ISkillHost.FindUnits` 真实现（此前恒返回空列表）：委托 `ISpatialQuery.QueryShape` + 新增
  `Shape.WithOrigin`，按 `UnitFilter` 全维度过滤，生产装配 `RulesAssembly` 默认注入 `Spatial`/
  `Factions`。
- 定义缓存热重载失效（十处，"构造期一次性读 registry 建索引、之后只读"模式统一订阅
  `DataLoadCompletedEvent`）：`SkillDefCache`（新增 `InvalidateAll`）、`InventoryHost`/
  `EquipmentHost`、`QuestHost`/`DialogHost`（均新增 `Reload` 方法 + `GameplayAssembly` 转发）、
  `ArchetypeRegistry`、`StatHost`、`LootHost`、`EconomyHost`（新增 `Reload` 方法 +
  `RulesAssembly` 转发）；均不触碰运行期状态，`EconomyHost` 的限量库存额外保留已消耗进度不重置。
- schema 新增字段：`GobjLockWorldFlagExpectedRule`（`world_flag.expected` 缺失/错型 report 阶段
  拒绝）、`ExprValueJson.IsValid`（Quest `rewards.world_flags[].value`/Dialog
  `set_flag.params.value` 联合形状校验）、`SkillSchemas.PeriodicParamsCase` 补
  `scaling_stat: Reference(stat.definition)`、`WorldMapSpawnPointsValidationRule`（出生点/传送点
  元素语义边界）、`games/_template/Runtime/DataHotReload.cs` 新增订阅 `Deleted` 文件事件（删除
  override 自动回落）。
- `toolchain/abi_probe.ps1` + `toolchain/abi_probe/`（消费方探针工程）+
  `toolchain/abi_probe_baseline.txt`：发布前 ABI 探针，纳入 `check.ps1` 全量步骤（`-Quick` 跳过）。

### 修复

第十四轮（修订版）审核 6 项 P2（ABI/API 兼容性、Gobj `world_flag.expected`、SkillHost 热重载缓存、
Quest world flag reward 联合校验、装备表现 SaveLoaded、开发期热重载删除覆盖回落）+ 2 项静态差距
（`ISkillHost.FindUnits`、VFX 锚点持续跟随）+ 3 项 P3（ADR-0019 门禁载体文案、周期效果
`scaling_stat` metadata、WorldMap 点元素语义边界）+ 额外发现（第六个退役类型、两处构造签名收窄、
四处同类缓存候选）——逐条判断、复现、修复位置、验收结果见
`architecture/落地计划/audit-c9ff301-20260909/followup-2026-09-10.md` 核实表，不在本条目重复展开。

### 文档

- 能力索引（`architecture/落地计划/落地方案与分阶段计划.md`"能力边界与未默认接入能力索引"一节）
  分类口径由四类扩到六类，新增**游戏责任**；天赋点数管理、位移轨迹碰撞、新局完整重置、escort
  自动路线等条目按责任重分类；导航跨帧预算、空间查询完整索引化改归**明确非目标**；
  `ISkillHost.FindUnits`、VFX 锚点持续跟随两行随本版本实现完成，从索引表本体移出、归档进"已修复
  历史项"小节；根 `README.md` 能力边界段同步。
- `architecture/05_对象模型与世界.md`（WorldMap 点元素语义勘误）、`architecture/09_表现层.md`
  （装备外观对账、VFX 跟随实现现状）、`architecture/13_新游戏接入指南.md`（热重载删除语义）、
  `architecture/adr/0019-复合字段子结构登记为机器可读schema.md`（追加修订记录）、
  `architecture/11_工程规范与测试.md`（ABI 探针现状更正为已实现）均按 12 §5"细节勘误直接修订、
  版本号不变"处理。

### 迁移说明

- 旧编译的 1.12 consumer 换正式 1.14 DLL、**不重新编译**即可继续运行，不再出现
  `MissingMethodException`（已随 `toolchain/abi_probe.ps1` 纳入发布前检查，基线 1.12.0 本机实跑
  通过）。
- 使用上方"新增/恢复"小节列出的退役类型/兼容重载的**源码**在重新编译时会收到 `[Obsolete]` 编译期
  警告，提示改用对应的声明式 schema 登记（`VariantSchema`/`Fields`）或无参构造函数；这些 façade
  至少保留一个 MINOR 发布周期，下一个 MINOR 周期后可能移除（届时另行走
  [12_扩展与变更流程.md](architecture/12_扩展与变更流程.md) 走 MAJOR 流程）。显式使用兼容重载可能
  与内建结构校验/新增业务规则对同一处坏数据重复报告（check 名不同，不掩盖问题，只是冗余）。
- 定义缓存热重载失效对调用方透明，无需改动任何既有调用点；`DataLoadCompletedEvent` 触发后
  resident host 下一次访问即可见新定义，运行期状态（背包物品、任务进度、对话会话、已应用光环/
  属性、已注册单位的当前资源值、`EconomyHost` 未变化商品的限量库存剩余）不受影响。
- VFX 跟随对自定义 `IRenderer2D` 引擎适配层是**可选**接口（`IParticleRepositioner`）：未实现该
  接口的引擎适配层行为与改动前完全一致，不需要任何改动即可继续编译运行；需要跟随效果的引擎适配层
  实现方可自行实现该接口接入。

### 版本判据说明

新增能力（ABI 探针、两个新接口、`FindUnits` 真实现、十处热重载缓存刷新、四个 schema 新字段）与
恢复向后兼容（ABI façade 恢复对已编译 1.12 consumer 的二进制兼容）——均不改变任何现有公开签名、
不删改任何数据表字段、不改变存档格式，按 SemVer 判定为 MINOR。

## [1.13.0] - 2026-09-09

ADR-0018（编辑器随游戏走：基础套件按版本消费框架，无头适配层与校验装配入口列为框架交付物）、
ADR-0019（复合字段子结构登记为机器可读 schema，含按 kind 分派的变体）拍板落库（`88f31f8`）；
ADR-0019 F1 复合字段子结构登记与递归校验分三段收口——F1a 技能域首批登记与 `FieldSchema`/
`DataRegistry` 契约扩展（`e2fc928`）、F1b L4 玩法层九个模块登记（`cba913b`）、F1c L3/L1/L2 其余/
L0/L5 登记，首批登记范围收口（`e50339a`）；ADR-0018 决策 3 F2 无头适配层交付与核心库校验装配入口
`ContentValidationAssembly`（`7ddbeac`）；F3 元数据门禁 `SchemaAudit`、开发期数据热重载标准实现、
`.github/workflows/release.yml` 四包修正（`3456e94`）。均属向后兼容的新增能力与契约扩展，无数据表
字段删改、无存档格式变更（判据见下"版本判据说明"）。

### 文档

- ADR-0018（编辑器随游戏走：基础套件按版本消费框架，无头适配层与校验装配入口列为框架交付物）、
  ADR-0019（复合字段子结构登记为机器可读 schema，含按 kind 分派的变体）拍板落库，收入
  `architecture/adr/`，索引见 `architecture/adr/README.md`。
- `architecture/04_数据与内容管线.md` 升级 v5 → v6：新增 3.2 节"复合字段子结构登记"（`Object`
  字段可登记 `fields`、`Array` 字段可登记 `item`、按判别字段分派的复合字段登记为 `variants`，
  以 `skill.def.effects` 的 `school_damage`/`apply_aura`/`projectile` 三个变体为记法示例），
  第 5 节校验项清单新增"复合字段子结构合法"与"变体表与原语注册集合一致"两项。
- `architecture/13_新游戏接入指南.md`、`architecture/12_扩展与变更流程.md` 补充勘误（版本号不
  变）：13 第 1 节前置动作后补一步可选项——若该游戏需要内容编辑器，复制编辑器项目提供的编辑器
  模板到游戏仓库；12 第 2 节新增原语审批流程"再改注册表"一步补充同时登记该原语的变体子结构。
- `architecture/落地计划/落地方案与分阶段计划.md` 3.5 节"四条消费通道"表新增"无头适配层与校
  验装配入口"一行，3.5.1 节补一句"三个包"归属未决说明。
- `editor/docs/编辑器产品文档.md` 升级 v2 → v2.1：全文 ADR-0018/0019 引用由"草案，待拍板"改为
  正式链接，附录 F 等处措辞同步改为已拍板。
- `architecture/04_数据与内容管线.md` 勘误（F1a 落地对齐，版本号不变）：3.2 节示例改正为真实
  `{kind, params: {...}}` 形状，补充 `CommonFields`、惰性递归中立记法、递归深度上限与"分层边界"
  说明；第 5 节两行说明补齐三个新增检查名（`variant_discriminator`/`substructure_depth`/
  `unknown_subfield`）。
- ADR-0018 决策 3 落地（F2）文档同步：`architecture/04_数据与内容管线.md` 勘误（版本号不变）——
  5.1 节"校验覆盖矩阵" `SpawnSummonOnlyCreatureRule`/`DisplayMapCoverageRule` 两行补充"经校验
  装配入口显式声明"；`architecture/11_工程规范与测试.md` 勘误（版本号不变）——目录树
  `adapters/stub/` 一行、第 6 节"桩适配层专供测试与 CI"一句均改为"同时是框架交付物（ADR-0018）"；
  `architecture/落地计划/落地方案与分阶段计划.md` 3.5.1 节"三个包"改"四个包"，补第四行，去掉
  F1 时补的"归属未决"说明改为已决定新增包。`adapters/stub/README.md` 首段改写为框架交付物说明；
  新增 `adapters/headless/README.md`（zip 通道说明文档源文件）。根 `README.md`、
  `toolchain/README.md`（validator 一节）、`data/README.md`（"与校验器的关系"一节）、
  `toolchain/registry/README.md`、`presentation/assembly/README.md`（新增"校验装配入口"一节）
  同步更新。`editor/docs/编辑器产品文档.md` 第 4.3 节状态改"已落地（1.13.0 起）"、第 4.4 节状态
  改"已落地"（仅改状态句，html 版本本轮未重生成，待下次文档转换一并同步）。

### 新增

- ADR-0019 F1a：`FieldSchema` 新增可选子结构登记（`Fields`/`Item`/`Variants`，仅对
  `Object`/`Array` 两种字段种类生效），新增 `VariantSchema` 契约类型（按判别字段分派的变体：
  `Discriminator`+`Cases`+可选 `CommonFields`）；`DataRegistry` 按登记递归校验必填/类型/枚举/
  引用/表达式可解析，新增三个检查名 `variant_discriminator`/`substructure_depth`/
  `unknown_subfield`（后者默认关闭，`DataRegistryOptions.UnknownSubfieldSeverity` 可开启为
  Warning）；`FieldSchema` 支持 `itemFactory`/`variantsFactory` 惰性求值，支撑
  `projectile.params.on_hit_effects` 一类自引用结构。
- 技能域（`skill.*`）首批登记：`skill.def.effects`（19 种效果原语）、`skill.aura_def.effects`
  （10 种光环效果类型）均登记为按 `kind` 分派的 `Variants`，逐个原语的 `params` 结构对照运行时
  解析代码核对（`EffectDispatcher`/`ProjectileHost`/`AuraHost`/三个 `IEffectExtension` 实现）；
  `skill.def.cost`/`charges`、`skill.spell_mod_def.affects`、`skill.book.entries` 均登记子结构。
  详见 `core/rules/skill/schema/README.md`"效果原语参数表"/"光环效果参数表"。
- `core/foundation/data_registry/tests/SubstructureValidationTests.cs`（32 条新测试，覆盖递归
  必填/类型、Variants 判别、子层 Reference/Expr、自引用递归与深度上限、`unknown_subfield`、
  路径格式、构造期非法组合）、`core/rules/skill/tests/SkillSchemaCoverageTests.cs`（锁定变体键
  集合与 `EffectKindNames`/`AuraEffectKindNames` 全集一致）。
- ADR-0019 F1b：L4 玩法层（`core/gameplay/`）九个模块的复合字段子结构登记（延续 F1a 的技能域，
  收口"首批登记"范围）：
  - `loot.table.groups` 登记为 `Item`（`roll_mode: Enum(chance_each|weighted_pick_one)`、
    `pick_count: Int?`、`entries: [{ref: Id, weight_or_chance: Number, condition: Expr?,
    count_range: {min: Int, max: Int}}]`，`ref` 跨类型指向 `item.template`/`loot.table` 退回
    `Id`）；`econ.vendor.sell_items` 登记为 `Item`（`item_id: Id`、
    `price_currency_id: Reference(econ.currency)`、`price_amount: Int`、`stock_limit: Int?`、
    `restock_policy: Enum(on_map_enter|timer)?`、`restock_timer: Number?`）；
    `world.flag_schema.allowed_values` 因元素类型随同记录 `kind` 字段动态变化（判别字段与被
    判别数组不同层，`Item`/`Variants` 均无法表达）且无运行时解析代码可作依据，本轮不登记。
  - `quest.def.objectives` 按 `type` 登记为 `Variants`（kill/collect/interact/explore/escort/
    event/cast/talk 八种目标类型，`collect`/`interact`/`explore`/`cast` 四处 `target_ref` 升级
    为 `Reference`）；新增公开可复用结构 `Core.Gameplay.Quest.QuestSchemas.RewardsFields`
    （`items`/`xp`/`currency`/`skills`/`world_flags`/`talent_points`），`quest.def.rewards`
    改为直接带 `Fields` 登记。
  - `dialog.gossip_menu.options`（`{text_key: TextKey, visible_if: Expr?, actions: Array?}`）、
    `options[].actions` 按 `kind` 登记为 `Variants`（十种动作，`quest_accept`/`quest_turn_in`/
    `start_encounter`/`cast_skill`/`start_story` 五处升级为 `Reference`）、
    `dialog.story_tree.nodes`/`nodes[].branches` 均登记子结构；`achv.def.criteria` 按 `type`
    登记为 `Variants`（六种条件类型），`achv.def.rewards` 直接复用
    `QuestSchemas.RewardsFields`（同一静态实例）。
  - `encounter.def.units`/`waves`/`phases`/`arena_rules`/`initiative_override` 登记子结构——
    `arena_rules.bounds_shape` 按 `kind` 登记为 `Variants`（circle/cone/line/rect 四种形状）；
    `units[].template_ref` 升级为 `Reference(creature.template)`，
    `initiative_override.params.initiative_stat` 升级为 `Reference(stat.definition)`。
  - `area.trigger_def.shape` 按 `kind` 登记为 `Variants`（circle/cone/line/rect）；`params`
    因判别字段 `trigger_type` 与 `params` 不同层（`Variants` 机制表达不了），改登记为 `Fields`
    （`target_map`/`spawn_point`/`encounter_ref`/`hook_id`，均退回 `Id`）。`spawn.table` 勘察
    确认全部字段为标量，无可登记的复合字段（`respawn_policy`/`respawn_timer` 同属判别字段与被
    判别字段不同层，`SpawnRespawnPolicyFieldGroupRule` 保留不退役）。
  - 上述九个模块共九个子结构登记表文档：`core/gameplay/loot/README.md`、
    `core/gameplay/economy/README.md`、`core/gameplay/world_state/schema/README.md`、
    `core/gameplay/quest/schema/quest.def.md`、
    `core/gameplay/dialog/schema/dialog.gossip_menu.md`/`dialog.story_tree.md`、
    `core/gameplay/achievement/schema/README.md`、`core/gameplay/encounter/schema/README.md`、
    `core/gameplay/spawn/schema/README.md`、`core/gameplay/area_trigger/schema/README.md`。
- ADR-0019 F1b 新增测试：`core/gameplay/loot/tests/LootSchemaCoverageTests.cs`（10 条）、
  `core/gameplay/economy/tests/EconomySchemaCoverageTests.cs`（8 条）、
  `core/gameplay/world_state/tests/WorldStateSchemaCoverageTests.cs`（2 条）、
  `core/gameplay/quest/tests/QuestSchemaCoverageTests.cs`（19 条）、
  `core/gameplay/dialog/tests/DialogSchemaCoverageTests.cs`（17 条）、
  `core/gameplay/achievement/tests/AchievementSchemaCoverageTests.cs`（15 条）、
  `core/gameplay/encounter/tests/EncounterSchemaCoverageTests.cs`（13 条）、
  `core/gameplay/spawn/tests/SpawnSchemaCoverageTests.cs`（2 条）、
  `core/gameplay/area_trigger/tests/AreaTriggerSchemaCoverageTests.cs`（12 条，含 2 条从
  `AreaTriggerValidationRuleTests.cs` 迁移），覆盖变体键集合与运行时枚举/注册集合一致性、子
  结构命中/坏形状、退役规则不双报的回归。

- ADR-0019 F1c：L3 载体层（`core/carriers/`）、L1 数值层（`core/numbers/`）、L2 其余模块
  （`core/rules/ai|targeting|combat`）、L0（`core/foundation/display_info|input_map|
  scene_router`）、L5 表现层（`presentation/`）复合字段子结构登记，收口 ADR-0019 首批登记范围：
  - L3：`item.template.stats`（`{stat: Reference(stat.definition), op: Enum(flat|pct|mult),
    value: Number}`，`op` 枚举以 `EquipmentHost.ParseOp` 为准，与 07 原文示例 `flat|pct` 不同）、
    `grants`（`skills`/`auras` 分别登记为 `Reference(skill.def)`/`Reference(skill.aura_def)`，
    L3 引用 L2 程序集合法）、`weapon_profile`、`requirements`；`item.set.bonuses`（`aura_ref`
    升级为 `Reference(skill.aura_def)`）、`item.budget_curve.entries`；`gobj.template.type_data`
    因判别字段 `kind` 与被判别对象不同层，登记为 `Fields`（十种 kind 字段并集，`GobjTypeData
    FieldGroupRule` 不退役）而非 `Variants`；`on_use`/`gobj.lock.requirement` 判别字段与被判别
    对象同层，登记为 `Variants`（`item_id` 升级为同层 `Reference(item.template)`）；
    `creature.template.base_stats` 为 `Map<stat_id, Number>`，按通用规则不登记。
  - L1：`stat.rating_conversion.entries`、`prog.level_curve.entries`（`growth` 为 Map 不登记）、
    `arch.talent_tree.nodes`（`grants` 未被任何运行时代码进一步解析，不登记子结构）、
    `arch.power_type.max_source` 登记为 `Variants`（`fixed`/`stat`，`stat` 升级为同程序集
    `Reference(stat.definition)`）；`arch.class.base_stats`/`arch.race.stat_mods` 为 Map，不登记。
  - L2：`ai.rotation.entries`（`priority`/`condition`/`skill_id` 均按运行时直接强转判断记录为
    必填，偏离方案"priority?"示例；`skill_id` 升级为 `Reference(skill.def)`）、
    `ai.patrol_path.points`（`FieldKind.Vec2` 元素）、`ai.behavior_profile.transitions` 为
    Map，不登记；`target.chain_def.shape` 登记为 `Variants`（circle/cone/line/rect）、`filters`
    登记 `Item`（`FieldKind.String`，不登记为 `Expr`——内置简写如 `relation:hostile` 不是合法
    Expr 语法）、`sort_by` 登记 `Fields`；`combat.hit_table_config` 六个分支登记共用 `Fields`
    （`stat` 升级为 `Reference(stat.definition)`）、`combat.resist_curve.entries`。
  - L0：`display.map.mirror_pairs`/`paperdoll_layers` 登记 `Item`（`anchor_points`/
    `default_slot_meshes`/`material_params` 为 Map，不登记）；`found.input_action.
    default_bindings` 登记 `Item`；`world.map.spawn_points`/`teleport_points` 共用同一份
    `{id?:String, position?:{x,y}}` 元素结构（`facing` 全仓库无运行时读取代码，不登记）；
    `found.time_model.grid_snap` 全仓库无运行时读取代码，本轮不登记（偏离方案建议）。
  - L5：`camera_profile.bounds`/`shake_presets` 登记 `Fields`/`Item`；
    `feedback.binding.actions` 登记为 `Variants`（六种动作，`style_id` 升级为同表
    `Reference(feedback.floating_text_style)`；`vfx_id`/`sfx_id`/`profile_id` 退回 `Id`）；
    `shell_menu_definition.entries` 登记 `Item`（`target_panel` 升级为同层
    `Reference(ui_layout_definition)`，`text_key` 升级为 `FieldKind.TextKey`）；
    `ui_layout_definition.fields`（非同构 Map，是引擎适配层解释的不透明 blob）、
    `display.weapon_style.cast_anim_override`/`impact_vfx_override`（`Id→Id` Map）按通用规则
    均不登记。
  - `item.affix.effects`（全仓库搜索无任何运行时解析代码，07 原文标注"本版不实现具体效果"）本轮
    不登记，偏离任务书"若要复用 `EffectsItemSchema`"的示例（该示例以"字段已被实际解析"为前提，
    与运行时现状不符）。
  - 新增测试：`core/carriers/gobj/tests/GobjSchemaCoverageTests.cs`（11 条）、
    `core/carriers/item/tests/ItemSchemaCoverageTests.cs`（13 条）、
    `core/numbers/stat_block/tests/StatSchemaCoverageTests.cs`（2 条）、
    `core/numbers/progression/tests/ProgSchemaCoverageTests.cs`（2 条）、
    `core/numbers/archetype/tests/ArchSchemaCoverageTests.cs`（2 条）、
    `core/numbers/power_set/tests/PowerSchemaCoverageTests.cs`（4 条）、
    `core/rules/targeting/tests/TargetSchemaCoverageTests.cs`（5 条）、
    `core/rules/combat/tests/CombatSchemaCoverageTests.cs`（5 条）、
    `core/foundation/display_info/tests/DisplaySchemaCoverageTests.cs`（3 条）、
    `core/foundation/input_map/tests/InputActionSchemaCoverageTests.cs`（2 条）、
    `core/foundation/scene_router/tests/WorldMapSchemaCoverageTests.cs`（2 条）、
    `presentation/camera/tests/CameraSchemaCoverageTests.cs`（3 条）、
    `presentation/feedback_binder/tests/FeedbackActionsSchemaCoverageTests.cs`（4 条）、
    `presentation/shell/tests/ShellMenuSchemaCoverageTests.cs`（3 条），另有 `core/rules/ai`
    既有 `AiContentValidationRuleTests.cs` 补齐 `skill.def` 假数据依赖，覆盖变体键集合一致性、
    子结构命中/坏形状、退役规则不双报的回归。

- ADR-0018 决策 3（编辑器随游戏走：框架侧交付物）——无头适配层交付：`Adapters.Stub`（对外称
  "无头适配层"）由此前"仅测试用、不对外发布"转正为框架正式交付物：
  - zip 通道：`build.ps1 -Dist`/`-Release` 新增拷贝 `Adapters.Stub.dll` + 说明文档到
    `dist/<ver>/adapters/headless/{Adapters.Stub.dll, README.md}`；`MANIFEST.txt` 新增
    `[headless_assemblies]` 段（sha256）；`dist/ws-game-<ver>.lock` 新增 `headless_dlls`
    字段（`{"Adapters.Stub.dll": <sha256>}`）。`toolchain/get_framework.ps1` 校验该字段存在
    时的哈希（老锁文件没有该字段时跳过并提示，向后兼容）。
  - 私服通道：新增第四个可发布包 `com.gamefoundation.adapter.headless`
    （`toolchain/registry/manifests/adapter-headless/`），内容放包内 `Lib~/`；
    `toolchain/registry/registry.json`/`build.ps1` 5.15 节/`check.ps1`"包清单一致性"步骤同步
    为四个包；该包不是 Unity 依赖，`get_framework.ps1 -FromRegistry` 不写入游戏工程
    `Packages/manifest.json`，只登记进 `ws-game.lock` 的 `source.optional_packages` 字段并
    在输出中提示对应 `npm install` 命令。
  - `toolchain/tests/test_package_name_consistency.py`（新增）：断言四个包名在
    `registry.json`/`build.ps1`/`check.ps1`/`get_framework.ps1` 四处一致。
- ADR-0018 决策 3——校验装配入口：核心库 `presentation/assembly` 新增单一公开入口
  `ContentValidationAssembly`（`ContentValidationOptions`/`ContentValidationRun`），把此前只
  内联在 `toolchain/validator/Program.cs` 里的"汇总注册目录 + 可选规则接线参数 + 装配选项"
  逻辑抽出，供 `toolchain/validator` 与编辑器基础套件（独立消费方项目）共用同一份装配代码：
  - `ContentValidationAssembly.Run(sources, options?)`：一次性完成建 registry、注册全部
    L0～L5 `TableSchema`/`IValidationRule`（含可选规则 `SpawnSummonOnlyCreatureRule`/
    `DisplayMapCoverageRule` 按 `options` 是否提供接线参数决定是否注册）、`LoadAll`、汇总结果
    （含 `DisabledOptionalRules`/`EnabledOptionalRules` 如实汇报未启用的可选规则，不静默跳过）。
  - `ContentValidationAssembly.CreateRegistry(primary, options, out disabledOptionalRules)`：
    只做到"注册完成、不加载"，供需要先持有 registry 的宿主（如编辑器）使用。
  - `toolchain/validator` 改为只做参数解析 + 调用该入口 + 打印；新增可选参数
    `--display-map-sources <table:idField,...>`；`--json` 输出新增 `disabled_optional_rules`/
    `enabled_optional_rules` 数组，文本输出末尾追加一行 `optional rules disabled: ...`
    （既有输出的其余部分逐字节兼容）。
  - 新增测试 `presentation/assembly/tests/ContentValidationAssemblyTests.cs`（5 条）：固定
    可选规则清单、默认选项下两条均未启用、接线后均启用且真实生效（`DisplayMapCoverageRule`
    构造真实触发场景）、`WarningsBlock` 下警告置 `IsBlocking`、与直接调用
    `PresentationSchemaCatalog.RegisterAll` 得到的问题集合一致。

### 行为变更与迁移说明

- ADR-0019 F1c：以下手写结构校验按登记覆盖情况退役/收窄（检查名集合变化，均属 MINOR 级，无数据
  表字段删改）：
  - `core/carriers/gobj/schema/GobjValidationRules.cs`：`GobjOnUseKindRule`（检查名
    `gobj_on_use_kind`）/`GobjLockRequirementFieldGroupRule`（检查名
    `gobj_lock_requirement_field_group`）两条纯结构规则整条删除，改由 `on_use`/`requirement`
    的 `Variants` 登记（`variant_discriminator`/`required_field`/`field_type`）完全覆盖；
    `GobjTypeDataFieldGroupRule`（判别字段 `kind` 与 `type_data` 不同层）不退役。
  - `core/rules/ai/core/AiContentValidationRule.cs`：`ValidateRotations` 的元素非对象/
    `priority`/`condition`/`skill_id` 缺失或类型不符四类结构检查、`condition` 的 Expr 解析
    （检查名 `expr_parsable`）整段删除——`condition` 登记为 `FieldKind.Expr` 后与顶层字段共用
    同一份 `DataRegistry.ValidateExprField` 实现，继续跑自定义域 Schema 解析会与内置校验对同一
    条坏数据双报；仅保留 `priority` 同表内不重复这条纯业务判断（数组内多元素互相比较，登记层
    表达不了）。`ValidateBehaviorProfiles`（`transitions` 是 Map，未登记子结构）/
    `ValidatePatrolPaths`（`points` 数量 `>= 2`）未改动。
  - `core/rules/targeting/core/ChainDefValidationRule.cs`：`filters` 元素非字符串的检查
    （检查名 `target_filter_type`）整条删除，改由 `filters` 的 `Item`（`field_type`）覆盖；
    内置简写识别 + Expr 解析（`target_filter_expr_parsable`）、`source` 已注册、`fallback`
    成环三项业务判断未改动。
  - `core/rules/combat/schema/CombatValidationRules.cs`：`CombatResistCurveValidationRule` 的
    `entries[]` 元素非对象/`value`/`reduction` 缺失或类型不符检查（检查名
    `resist_curve_entries_shape`）整条删除，改由 `entries` 的 `Item`
    （`required_field`/`field_type`）覆盖；单调性、`kind=table` 时非空、`kind=saturation` 时
    `k` 必填且为正、`max_reduction` 区间四项业务判断（判别字段 `kind` 与 `entries`/`k` 是表
    顶层平级字段，`Variants` 不适用）未改动。`CombatHitTableValidationRule` 未改动（六个分支
    此前无对应结构检查，本轮子结构登记是纯新增覆盖）。
  - 已通过既有内容数据（`data/_sample`、`data/_framework`）0 错误验证，`data/_sample/stat/
    stat.definition.json`/`stat.rating_conversion.json`/`l10n/l10n.text.json` 补充此前缺失的
    `stat.dodge_rating`（连同其 `stat.rating.dodge` 评级曲线与 `l10n.stat.dodge_rating.name`
    文本键）——`combat.hit_table_config.dodge.stat` 一直引用这个未登记过的属性 id，此前因
    `stat` 字段未登记子结构、这条引用完整性检查从未真正跑过，本轮修复样例数据而非放宽登记。
  - 大量既有单元测试夹具（`core/carriers/item|gobj`、`core/rules/ai`、
    `core/gameplay/assembly` 等）内联的假 `skill_id`/`item_id`/`stat`/`style_id` 等 id 此前
    不受跨表引用完整性约束，本轮子结构登记升级为 `Reference` 后需要夹具补充匹配的最小合法数据
    （`skill.def`/`item.template`/`stat.definition`/`feedback.floating_text_style`/
    `target.chain_def` 等），测试断言的业务行为本身未变化。
- 嵌套 `Expr` 字段现经登记递归校验（`DataRegistry.ValidateFieldValue` 对子层 `FieldKind.Expr`
  与顶层共用同一份 `ValidateExprField` 实现，见 F1a），ADR-0015"疑似引用拼写错误"警告会在嵌套
  表达式上出现：`data/_sample` 的 `dialog.gossip_menu` 现报 3 条此类警告（`dialog.sample_hunter`
  的 `options[].visible_if` 对 `"quest.sample_hunt"` 的引用），均为对 `quest.*` 内容 id 的合法
  引用，属规范内的警告级噪音，不阻断（`toolchain/validate_data.py`/`check.ps1 -Quick` 均已确认
  0 错误通过）；`games/_template/validate.ps1 -Strict` 或 `validate_data.py --strict` 下警告会
  阻断，游戏侧启用严格模式前需核对并按需给 `quest.sample_hunt` 补一条真实引用或调整措辞消除
  误报。
- `core/rules/skill/schema/SkillValidationRules.cs`：`EffectKindRegisteredRule`/
  `CostEntryShapeRule` 两条手写结构校验规则整条退役，`ChargesShapeRule` 的结构部分退役、唯一
  业务判断（`charges.max >= 1`）收窄保留为新规则 `ChargesMaxAtLeastOneRule`——三者要检查的坏
  形状全部由 `SkillSchemas.Def`/`AuraDef` 的子结构登记 + `DataRegistry` 递归校验覆盖，不再需要
  单独注册（`core/rules/assembly/RulesSchemaCatalog.cs` 同步调整注册列表）。已通过既有内容数据
  （`data/_sample`、`data/_framework`）0 错误 0 警告验证，无需修改任何样例数据行。
- 若游戏层曾自行 `RegisterValidationRule(new EffectKindRegisteredRule())` 等注册上述已删除的
  规则类型，需要移除对应调用（改由 `SkillSchemas` 的登记自动覆盖同等检查）；曾依赖
  `unknown_effect_kind`/`effect_kind_missing`/`effect_entry_not_object`/
  `cost_entry_not_object`/`cost_power_type_invalid`/`cost_amount_invalid`/`charges_max_missing`/
  `charges_recharge_time_missing`/`charges_recharge_time_not_number` 这些检查名做过滤/展示的
  下游工具（如内容编辑器/CI 报告），改为识别 `required_field`/`field_type`/
  `variant_discriminator` 三个检查名（属 MINOR 级：新增能力、既有检查名未删改，但检查名集合
  的组成发生了变化）。
- `skill.def.effects[].params` 内以下字段的引用严格度按判断记录做了收窄（未按 04 §3.2 示例
  登记为强 `Reference`/必填，理由见 `core/rules/skill/schema/SkillSchemas.cs`/`README.md`
  对应判断记录）：`trigger_spell`/`modify_cooldown`/`add_charge`/`learn_skill` 的 `skill_id`
  按 `Id` 登记（不做跨表存在性校验）；`summon.creature_template`/`create_item.item_template`/
  `set_world_flag.flag_key`/`script.hook_id` 按 `Id` 登记且非必填。
- ADR-0019 F1b：以下手写结构校验按登记覆盖情况退役/收窄（检查名集合变化，均属 MINOR 级，无数据
  表字段删改）：
  - `core/gameplay/loot/core/LootContentValidationRule.cs`：不再委托 `LootTableParser.Parse`
    报告结构性坏形状，收窄为五类登记表达不了的业务判断（`ref` 领域+存在性、`weight_or_chance`
    区间、`count_range` 区间、`pick_count`、`guaranteed_min`）+ 嵌套引用成环检查。
  - `core/gameplay/economy/core/EconomyContentValidationRule.cs`：不再委托
    `EconomyDataParser` 报告结构性坏形状；`sell_items[].price_currency_id` 手写的货币存在性
    核对整条退役，改由 `reference_integrity` 覆盖；收窄为三类业务判断（`price_amount`/
    `stock_limit` 非负、`restock_policy=timer` 时 `restock_timer` 必填且为正）。
  - `core/gameplay/quest/core/QuestContentValidationRule.cs`：不再整条委托
    `QuestDefinition.FromRecord` 把任意解析异常打包报告，收窄为七类业务判断（`objectives`
    数量/`count` 约束、kill/escort 的 `target_ref` domain 弱校验、`rewards` 数值范围/存在性）；
    structural 类问题改由 `DataRegistry` 独立报告。
  - `core/gameplay/dialog/core/DialogContentValidationRule.cs`：gossip_menu 侧整条委托解析异常
    的检查退役；story_tree 侧检查名从笼统的 `dialog_content` 拆分为
    `story_tree_min_nodes`/`story_tree_duplicate_node_id`/`story_tree_dangling_next_node`/
    `story_tree_cycle` 四个更具体的检查名（图结构业务判断保留，未退役）。
  - `core/gameplay/achievement/core/AchievementContentValidationRule.cs`：`criteria[].type`
    合法性、`observe_event`/`count` 缺失/格式检查退役（改由结构登记覆盖）；新增两项此前完全无
    校验覆盖、只在运行期构造时才崩溃的检查 `achv_criteria_min_count`/
    `achv_criterion_count_positive`；`observe_event` 成员资格检查收窄为
    `achv_observe_event_unregistered`。
  - `core/gameplay/encounter/core/EncounterContentValidationRule.cs`：`waves[].trigger_condition`/
    `phases[].enter_condition` 两处手写 Expr 可解析检查整条删除，改由结构登记的
    `FieldKind.Expr` 子字段经 `DataRegistry` 递归校验覆盖（且更严格，额外跑
    `ExprValidator.Validate` 静态校验）；`units[]` 二选一、`spawn_ref`/`spawn_refs[]` 的
    domain 校验（跨字段/跨表业务判断）保留。
  - `core/gameplay/area_trigger/schema/AreaTriggerValidationRules.cs`：`AreaTriggerShapeKindRule`
    （`shape.kind` 合法性检查，检查名 `area_trigger_shape_kind`）整条退役，由 `Variants` 内置的
    `variant_discriminator` 检查完全覆盖；`AreaTriggerParamsFieldGroupRule`（按 `trigger_type`
    决定 `params` 哪些字段必填的业务判断，判别字段与被判别对象不同层、登记层无法覆盖）保留。
    `core/gameplay/assembly/GameplaySchemaCatalog.cs` 的 `RegisterAreaTriggerSchemas` 同步删除
    对应注册行。
  - `core/gameplay/spawn`：勘察确认 `spawn.table` 全部字段为标量（判别字段
    `respawn_policy`/`respawn_timer` 与其它模块的 shape/params 同构、不同层），
    `SpawnRespawnPolicyFieldGroupRule`/`SpawnContentRefRule`/`SpawnSummonOnlyCreatureRule` 均
    因结构上无法登记覆盖或属跨表业务判断而原样保留，本轮不退役任何规则。
  - 已通过既有内容数据（`data/_sample`、`data/_framework`、`games/_template/data`）0 错误验证，
    未修改任何样例数据文件。
- 曾依赖旧的单一检查名（`loot_content`/`economy_content`/`quest_content`/`dialog_content`/
  `achievement_content`/`encounter_content`/`area_trigger_shape_kind`）做过滤/展示的下游工具，
  改为识别上述新检查名，以及结构层既有检查名
  `required_field`/`field_type`/`variant_discriminator`/`reference_integrity`/`expr_parsable`。
- `quest.def.objectives[].target_ref`：`kill`/`escort` 两处按判断记录退回 `Id`（不做跨表存在性
  检查——既有测试用不落 `creature.template` 内容表的 id 驱动 kill 目标）；
  `collect`/`interact`/`explore`/`cast` 四处升级为 `Reference`，分别指向
  `item.template`/`gobj.template`/`area.trigger_def`/`skill.def`。
- `dialog.gossip_menu.options[].actions[]`：`vendor`/`teleport`/`set_flag`/`script` 四处 `ref`
  退回 `Id`（分别因无登记表/非 DataRegistry 表/`world.flag_schema` 非运行态/`found.hook` 无实现
  级 schema）；`achv.def.criteria[].observe_event`/`target_ref` 均未登记为 `Reference`（既有测试
  用最小 registry 驱动大量用例，登记会误报 `reference_integrity`）。
- `encounter.def.units[].template_ref` 升级为 `Reference(creature.template)`；
  `units[].spawn_ref`、`waves[].spawn_refs[]`、`phases[].on_enter_hook` 保持 `Id`（前二者刻意
  经 `SpawnRequester` 与 `core/gameplay/spawn` 决耦，后者 `found.hook` 无实现级 schema）。
- ADR-0018 决策 3：`ws-game.lock`（zip 通道）新增可选字段 `headless_dlls`（老锁文件没有该字段
  时 `toolchain/get_framework.ps1` 跳过对应哈希校验并提示，不报错、不阻断，向后兼容）；
  `-FromRegistry` 通道写入的 `ws-game.lock` 新增 `source.optional_packages` 字段（可选包清单，
  当前只含 `com.gamefoundation.adapter.headless`），不影响既有 `source.packages` 字段语义
  （仍是写入游戏工程 `Packages/manifest.json` 的三个 Unity 依赖）。
- `toolchain/validator --json` 输出新增 `disabled_optional_rules`/`enabled_optional_rules`
  两个数组字段（追加在既有字段之后）；文本输出末尾新增一行
  `optional rules disabled: <逗号分隔的规则名，或 none>`；新增可选命令行参数
  `--display-map-sources <table:idField,...>`。既有字段/行的内容与含义不变。
- `check.ps1`"包清单一致性"步骤核对的包数量由三个改为四个（新增
  `com.gamefoundation.adapter.headless`），`build.ps1 -Dist`/`-Release` 组装出的 `packages/`
  目录与 `.tgz` 数量同步由三个改为四个。`.github/workflows/release.yml` 涉及三个 `.tgz` 的
  多处硬编码已于 F3 补齐（见下"F3：元数据门禁、开发期数据热重载、release.yml 四包修正"小节），
  此前"已知限制，留待后续单独跟进"的说明不再适用。
- F3（ADR-0018 决策 3/5、ADR-0019 决策 4）：
  - **元数据门禁**：新增 `presentation/assembly/SchemaAudit.cs`（`Presentation.Assembly.SchemaAudit`），
    对全部已登记 `TableSchema`/`FieldSchema` 结构声明本身做静态审计（不加载任何数据），六项检查
    ——`missing_description`/`composite_without_substructure`/`allowlist_entry_unused`/
    `reference_target_unknown`/`variant_shape`/`unschematized_table`；`DataRegistry` 新增
    只读属性 `RegisteredSchemas`（具体类，不改 `IDataRegistry`/`IDataRegistryView` 接口签名）。
    `toolchain/validator` 新增 `--schema-audit [--allowlist <path>] [--json]` 命令行模式；
    `check.ps1` 新增步骤"元数据门禁：validator --schema-audit"，`-Quick` 下也跑。新增白名单文件
    `toolchain/schema_audit_allowlist.json`（20 条，均为动态键 Map 型对象或模块显式声明"暂不
    解析"的扩展位，逐条写明 reason）；首次审计跑出的全部 `missing_description`（452 处原始命中，
    经修复"自引用 schema 环检测"缺陷后去重为 227 处真实字段）已在对应 `*Schemas.cs` 文件里真正
    补齐中文描述，不是加白名单绕过。`encounter.def.rewards` 改为复用 `quest.def`/`achv.def` 已有
    的 `QuestSchemas.RewardsFields`（此前是裸 `Object`，无 `Fields` 子结构）。
  - **开发期数据热重载标准实现**：新增 `games/_template/Runtime/DataHotReload.cs`（仅
    `UNITY_EDITOR || DEVELOPMENT_BUILD` 下编译为有效实现，其余情况是空壳，发布构建零开销）；
    `GameOptions` 新增口味开关 `EnableDataHotReload`（默认 `true`）；`GameBootstrap` 在数据加载
    成功后按该开关挂载，监视框架数据根与游戏数据根下 `*.json`，去抖 300ms 后调用
    `DataRegistry.Reload`，成功/失败分别补发 `data.load_completed`/`data.validation_failed`
    （`Reload` 本身不发这两个事件，只有 `LoadAll` 会发）。
  - **`.github/workflows/release.yml` 四包修正**：`Check for existing release assets`/
    `Repair missing assets from existing zip` 两步的必需附件集合由"三个 `.tgz`/五个附件"补齐为
    "四个 `.tgz`/六个附件"（新增 `com.gamefoundation.adapter.headless-<ver>.tgz`）。
    `toolchain/tests/test_package_name_consistency.py` 新增一条测试，把 `release.yml` 的
    `$requiredNames`/`$requiredPaths` 纳入四处一致性核对（此前只核对
    `registry.json`/`build.ps1`/`check.ps1`/`get_framework.ps1` 四处，`release.yml` 曾是漏网
    的第五处）。
- **迁移说明（元数据门禁，F3）**：`check.ps1` 新增的"元数据门禁"步骤从此会阻断提交——任一已登记
  `FieldSchema` 缺描述（`missing_description`）、或 `Object`/`Array` 字段未登记子结构且未在白
  名单声明（`composite_without_substructure`）都会让该步骤以非零退出码失败，`-Quick` 子集同样
  跑这一步（秒级，不需要 Unity/构建产物）。游戏侧若有自定义 `TableSchema` 经
  `IDataRegistry.RegisterSchema` 登记进与框架共用的同一个 `registry`（例如
  `PresentationSchemaCatalog.RegisterAll` 之后再补注册游戏专属表），这些自定义表同样会被本仓库
  的 `SchemaAudit`/`check.ps1` 步骤审计到（若游戏侧直接复用本仓库的 `check.ps1`）——升级后首次
  遇到阻断，按检查名分两种处理：`missing_description` 必须给对应 `FieldSchema` 补一句中文描述，
  不能靠白名单绕过；`composite_without_substructure` 若确认是动态键 Map 型对象/暂不支持结构化
  登记的字段，在 `toolchain/schema_audit_allowlist.json`（或游戏侧自己维护的等价白名单文件，
  经 `--allowlist <path>` 指定）新增一条 `{"table", "field", "reason"}`，`reason` 必填、必须
  写清楚为什么这个字段暂时/永久不适合登记子结构，白名单条目不再对应任何真实命中时
  `allowlist_entry_unused`（Warning，不阻断）会提醒清理。
- `games/_template/`"复制为新游戏：改哪几处"新增第四个 asmdef
  `Tests/Editor/Game.Template.EditorTests.asmdef`（F3 随 `DataHotReloadEditModeTests.cs` 一并新增，
  此前遗漏在改名清单外）：复制模板改名时需一并处理该文件的文件名、`"name"` 与 `references` 数组，
  否则改名后的消费方工程编译报 `DataHotReload` 找不到（`toolchain/consumer_smoke.ps1` 同步补齐，
  见该脚本 `$asmdefRenames`）；`games/_template/README.md` 目录清单与改名步骤已同步更新。
- `DataHotReload.ProcessPendingChangesForTests`（原 `internal` + 程序集级
  `InternalsVisibleTo("Game.Template.EditorTests")`）改为公开方法
  `DataHotReload.ProcessPendingChanges()`：模板改名后程序集名变化会让硬编码旧程序集名的
  `InternalsVisibleTo` 失效，改为公开方法即可在改名后继续被测试/编辑器/无头宿主调用，与
  `TemplateSmokeRunner` 公开而非 internal 的既有判断一致；`Update()` 内部改为调用同一个公开方法，
  行为未变。若游戏层曾直接调用 `ProcessPendingChangesForTests`，改调 `ProcessPendingChanges`。

### 已知问题：ABI/API 兼容性（第十四轮修订版审核，2026-09-09，基线 `c9ff301`；已在 1.14.0 收口）

外部审核（`architecture/落地计划/audit-c9ff301-20260909/`，独立 consumer probe，见
`docs-project/api-compat/api-compat.log`）核实：本版本对已编译旧 consumer **不是**二进制兼容——
`FieldSchema` 的 1.12 七参数公开构造函数
`FieldSchema(string, FieldKind, bool, IReadOnlyList<string>, string, string, string)` 在 1.13
正式 `Core.Foundation.dll` 中已不存在，旧编译产物只替换正式 DLL、不重新编译，运行期抛
`System.MissingMethodException`；`core/carriers/gobj/schema/GobjValidationRules.cs`/
`core/rules/skill/schema/SkillValidationRules.cs` 一并整条删除的
`GobjOnUseKindRule`/`GobjLockRequirementFieldGroupRule`/`EffectKindRegisteredRule`/
`CostEntryShapeRule`/`ChargesShapeRule` 五个公开规则类型同属已删除的公开签名（源码重编译可绕开
`FieldSchema` 新增的可选参数、但不能让旧签名重新出现，也不能恢复已删除的公开类型）。上一条
"版本判据说明"曾表述为"无任何公开签名删改"，与上述探针结果不一致，此处更正为准确口径：**本版本
按 SemVer 声明为 MINOR，但对已编译的旧 1.12 consumer 存在已知二进制不兼容**，源码级消费方按上方
"行为变更与迁移说明"逐条改造仍可正常编译通过。

**核实补充（第十四轮修订版审核收口，2026-09-10）**：核心侧全仓复查（`git diff v1.12.0 v1.13.0 --
'*.cs'` 扫公开签名）另发现审计报告未点名的两处：（1）`core/gameplay/area_trigger/schema/
AreaTriggerValidationRules.cs` 的 `AreaTriggerShapeKindRule` 同一批 ADR-0019 退役、同属整条删除的
公开规则类型（第六个，五个之外的遗漏）；（2）`core/gameplay/economy/core/
EconomyContentValidationRule.cs`/`core/gameplay/loot/core/LootContentValidationRule.cs` 的 1.12
构造签名 `(IExprSchema? conditionSchema = null)` 均被收窄为隐式无参构造——不是整条类型删除，但同样
属于物理签名不兼容（可选参数默认值在旧调用点编译期内联进 IL，换 DLL 不重编译同样
`MissingMethodException`）。以上"六个已删除类型 + 两处构造签名收窄"已在 **1.14.0** 全部恢复为
`[Obsolete]` 兼容 façade，详见下方 `[1.14.0]` 正式条目；本小节保留作为 1.13.0 版本本身的已知状态
记录，不因后续版本修复而删除。

### 版本判据说明

新增能力与向后兼容的契约扩展——`FieldSchema` 新增可选子结构登记（`Fields`/`Item`/`Variants`，
仅对既有 `Object`/`Array` 字段种类新增可选行为，未登记的字段不受影响）、新增
`ContentValidationAssembly`/`SchemaAudit` 两个核心库公开入口、新增第四个私服包
`com.gamefoundation.adapter.headless`、`ws-game.lock` 新增可选字段
`headless_dlls`/`source.optional_packages`、`DataRegistry` 新增只读属性
`RegisteredSchemas`（具体类新增成员，不改 `IDataRegistry`/`IDataRegistryView` 接口签名）、
`games/_template` 新增模板热重载标准实现（`DataHotReload.cs`，仅编辑期/开发构建生效，发布构建
零开销空壳）。退役的手写结构校验规则（`GobjOnUseKindRule`/`EffectKindRegisteredRule` 等，见上
"行为变更与迁移说明"）改由登记层递归校验完全覆盖同等检查，属检查名集合变化，不影响校验结论；
无任何数据表字段删改，无任何存档格式变更。**"无任何公开签名删改"一句已按上方"已知问题：ABI/API
兼容性"小节更正**——`PlayerVitalsPersistable` 构造函数收窄发生在 1.12.0、本版未涉及是准确的，但
`FieldSchema` 七参数构造函数、六个手写规则类型（含核实补充发现的 `AreaTriggerShapeKindRule`）与
两处构造签名收窄（`EconomyContentValidationRule`/`LootContentValidationRule`）确在本版本删除/改签，
属已知的二进制不兼容，不属于"公开签名零删改"。按 SemVer 判定为 MINOR（新增能力占多数；已知的二进制
兼容缺口已在 1.14.0 通过恢复 façade 收口，见上方小节与 `[1.14.0]` 正式条目）。

## [1.12.0] - 2026-09-09

第十五方深度审核（codex 第十三轮，基线 `6739f50`，即 1.11.0 发布提交）2 项核心侧确认缺陷
（CORE-111-01、TP-111-01）+ 3 项表现/引擎侧确认缺陷（UI-111-01、NAV-111-01、SPATIAL-111-01）
逐条核实并根治，另处理若干核心侧文档/注释勘误、02/09 性能约定改写为准确边界、能力索引新增
"暂不落地（用户拍板）"分类并归入编辑器工具、补齐此前遗漏的能力索引行。逐条核实表、判断记录、
验收结果见
[audit-6739f50-20260909/followup-2026-09-09b.md](architecture/落地计划/audit-6739f50-20260909/followup-2026-09-09b.md)。
均属核心存档/传送缺陷修复、表现层缓存缺陷修复、引擎适配层寻路/空间查询缺陷修复与文档口径修正，
无数据表字段删改；存档格式有向后兼容的新增字段（见下"行为变更与迁移说明"）。

### 新增

- **`Core.Numbers.PowerSet.PowerHost.GetRegisteredPowerTypes(Id unitId)`/`IsInCombat(Id unitId)`**
  （新增公开方法，CORE-111-01 根治）：枚举某单位已注册的全部资源类型 id、查询当前进出战斗运行态；
  均只加在具体类 `PowerHost` 上，不进 `IPowerHost` 契约，不影响本仓库其它独立实现该接口的测试假
  类型。
- **`player.vitals` 存档段新增 `in_combat`（`Bool`）/`powers`（`Map<Id, Number>`）两个字段**
  （CORE-111-01 根治）：覆盖该单位读档前那一刻已注册的全部资源池当前值与进出战斗运行态，不再只
  挑生命值一种资源；旧字段 `health` 继续保留写入（供仍直接读取旧字段名的外部工具过渡使用）。
- **`Core.Gameplay.Assembly.PlayerVitalsPersistable(PlayerUnit, IPowerHost)`**（源码兼容重载，
  标记 `[Obsolete]`）：CORE-111-01 把生产构造函数第二参数类型从 `IPowerHost` 收窄为具体
  `PowerHost` 后，为保持本仓库之外可能存在的消费方源码兼容而补回的旧签名重载，仅做类型收窄转发
  （传入的 `IPowerHost` 若实际不是 `PowerHost` 实例则抛出明确的 `ArgumentException`）；框架自身
  生产装配点已经改用具体类型重载，不受影响。
- **"能力边界与未默认接入能力索引"表新增第四类分类"暂不落地（用户拍板）"**：区别于"未实现"
  （框架当前无对应运行期逻辑）与"明确非目标"（已有决策记录判定本版不展开）——本类特指"框架侧
  机制/产品方案已就绪或不构成技术障碍，但用户已明确拍板本阶段不安排落地"；编辑器工具（GF 内容
  编辑器）一行由"未实现"改列此类（产品文档已完成，实现按用户 2026-09-09 拍板暂缓）。索引表另
  按第十三轮审计报告补齐此前遗漏的五行——`ISkillHost.FindUnits`、位移轨迹碰撞、VFX 锚点持续
  跟随、nested teleport 元素/引用完整性校验、导航跨帧请求预算、空间查询完整索引化（均"未实现"）、
  `SpawnSummonOnlyCreatureRule` 查询接入（"已实现未默认接线"）。

### 修复

- **失败/成功读档回滚快照未覆盖 Health 之外的资源池当前值与进出战斗运行态（CORE-111-01，P2）**：
  见上"新增"两项。`PlayerVitalsPersistable.Save`/`Load` 泛化为覆盖全部已注册资源池当前值与
  `in_combat`，正向读档与失败回滚重放路径共用同一份 `Load` 实现；旧存档只有 `health` 字段时仍可
  正常读取（其余资源池按各自 `start_full`/默认规则初始化，不受影响）。
- **跨图传送先改写玩家字段再调用场景路由，路由拒绝（含 Loading 期间收到的第二个跨图请求）时异常
  被吞掉、字段已提交、场景与玩家 map/位置永久分叉（TP-111-01，P2）**：
  `GameplayAssembly.ApplyResolvedTeleport` 改为先尝试 `ISceneRouter.LoadScene`，只有路由未抛异常
  （确实接受本次导航请求）之后才提交 `entity.MapId`/位置；对"未知地图"与"当前不允许转入
  Loading"（含 Loading 中的第二次跨图请求）两类路由拒绝，采用默认"拒绝"策略——不排队、不重试，
  本次传送不产生任何字段副作用，交由上层按自己的重试/提示策略处理。同图内传送（不切地图）与未
  装配场景路由的场景不受影响。
- **视图模型读档后不重建缓存，需等待下一次手动 `Refresh` 才与宿主数据一致（UI-111-01，P2）**：
  `InventoryViewModel`/`ActionBarViewModel`/`CharacterStatsViewModel`/`DialogViewModel`/
  `HudViewModel`/`QuestLogViewModel`/`ShopViewModel`/`SkillBookViewModel` 八个视图模型的构造函数
  新增订阅 `SaveEventKeys.SaveLoaded`；`SaveSlotsViewModel` 本就已订阅；`PauseMenuViewModel`/
  `SettingsViewModel` 核对后确认与存档数据无关，不需要订阅。
- **Unity 导航窄通道：通道宽度窄于两级采样网格但直线本身畅通时 `FindPath` 仍返回 `null`
  （NAV-111-01，P2）**：`UnityNavigation2D.FindPath` 拆出私有 `FindPathViaGrid`（原逻辑不变），
  网格寻路彻底失败后新增"直线直达"兜底（新增私有 `SegmentHasClearContact`，要求线段与全部阻挡
  矩形完全无接触，比既有 `SegmentBlocked` 更严格，不越权覆盖 A* 已有的"禁止切角"结论）。
- **Unity 空间范围/锥形查询：候选桶范围未按被查询实体自身半径扩张，跨桶边界的大半径实体被漏选
  （SPATIAL-111-01，P2）**：`UnitySpatialQuery.QueryRadius`/`QueryCone` 的候选桶范围改为"查询
  半径/范围 + `MaxRadiusHint()`"（索引内最大实体半径）；`QueryRect`/`QueryShape`/`Nearest` 本就
  不经分桶（全量线性扫描），不受影响。
- **`architecture/02` §1.8 `findPath`/§1.9 `ISpatialQuery` 的"性能约定"过度承诺**："运行时应
  支持跨帧分摊、不得要求单帧内同步返回"与"查询应基于空间索引而非线性扫描"均与已知参考实现不符
  （跨帧预算未实现；`QueryRect`/`Nearest`/桩实现均为线性扫描），改写为准确的"边界"表述，不改变
  契约签名，版本号不变。
- **`architecture/09` §7.2 补充读档场景的视图模型缓存重建规则**：与 §4.4
  `IWeaponStyleSource` 缓存失效时机同一判断记录，版本号不变。
- 若干核心侧文档/注释现状化，均不改变任何运行期行为：`ArchSchemas.cs`/archetype `README.md`
  的 `passive_auras`/`skill_book_ref` 说明（`stat_block`/`power_set`/L2 `skill` 均早已实现，
  "不声明 Reference"的真正理由是分层边界与接口能力边界，不是对方模块尚未实现）；
  `PowerTickHandler.cs`/`IPowerDiagnostics.cs` 的过期判断记录（"本项目暂不启用离散时间模型"
  改判为"ADR-0013/sim_loop 已有基础离散调度，这只是本资源处理器自己的连续-only 边界"）；
  `UnityViewFactory.cs`/`GameFoundationBootstrap.cs` 的过期判断记录（"ViewBinder/CameraHost
  不支持退订"，`ViewBinder` 现已实现 `IDisposable`）。

### 行为变更与迁移说明

- **`player.vitals` 存档段格式扩展（向后兼容）**：新增 `in_combat`/`powers` 字段；读档时优先用
  `powers` 字段（存在即覆盖同一集合，缺失的当前值项静默跳过），只有 `powers` 字段完全缺失（旧
  格式存档）才退回旧的仅 `health` 路径。无需离线迁移脚本，读档即完成透明升级；旧档写回后自动
  变为新格式（不会自动降级回旧格式）。
- **`PlayerVitalsPersistable` 构造函数收窄**：生产构造函数第二参数类型从 `IPowerHost` 收窄为
  具体 `PowerHost`。本仓库之外若有消费方直接以旧签名 `new PlayerVitalsPersistable(player,
  someIPowerHost)` 构造，源码仍可编译（改走新增的 `[Obsolete]` 兼容重载），但要求传入的实例实际
  是 `PowerHost`（`IPowerHost` 目前唯一的生产实现）；传入其它实现会在构造期抛出
  `ArgumentException`（此前能编译通过，但也无法正常调用 CORE-111-01 新增的
  `GetRegisteredPowerTypes`/`IsInCombat`）。
- **跨图传送加载期间不再"静默接受第二次请求并覆盖玩家字段"**：升级前 Loading 中收到第二个跨图
  传送请求会让玩家 `MapId`/位置被改写成第二个请求的目标，即便该请求本身被路由拒绝、场景最终仍
  停在第一个请求的目标地图（字段与场景永久分叉）；升级后第二个请求整体不产生任何字段副作用。
  **依赖旧行为（Loading 期间发起的传送请求仍会生效于玩家字段）的调用方需要重新评估**——正确用法
  是等待首个请求完成（或失败）后再发起下一次传送。
- **自定义 `ISpatialQuery` 实现**：若游戏层提供了自己的 `ISpatialQuery` 实现（而非使用
  `UnitySpatialQuery`/`StubSpatialQuery`），且该实现也做了按桶（网格分区）加速的候选筛选，建议
  对照 SPATIAL-111-01 的根治手法核对候选桶范围是否已按被查询实体自身半径扩张，避免同一漏选问题。
- **`architecture/02`/`09` 两处"性能约定"改写为"边界"表述**：不改变任何契约签名或运行期行为，
  仅文档措辞更准确地反映当前参考实现的真实能力边界（跨帧分摊寻路预算、空间索引完整覆盖仍是
  "未实现"能力索引项，见上"新增"能力索引表更新）。

### 版本判据说明

新增公开成员（`PowerHost.GetRegisteredPowerTypes`/`IsInCombat`、`PlayerVitalsPersistable` 源码
兼容重载）与存档段向后兼容的新增字段（`player.vitals` 的 `in_combat`/`powers`），以及已记录在案
的行为修正（传送提交时机、视图模型缓存重建、导航/空间查询候选筛选）——按 SemVer 判定为 MINOR。

## [1.11.0] - 2026-09-09

第十四方深度审核（codex 第十二轮，基线 `ac3b622`，即 1.10.0 发布提交）3 项核心侧确认缺陷
（CORE-110-01～03）+ 2 项引擎侧确认缺陷（NAV-110-01/02）+ 1 项表现侧确认缺陷（PRES-110-01）逐条
核实并根治，另拍板落地方向移动导航阻挡契约、补齐 WorldMap 四字段 schema 登记、找回被
`.gitignore` 误伤的审计证据并收紧相对链接检查覆盖范围。逐条核实表、判断记录、验收结果见
[audit-ac3b622-20260909/followup-2026-09-09.md](architecture/落地计划/audit-ac3b622-20260909/followup-2026-09-09.md)。
均属核心存档/规则/载体层缺陷修复、引擎适配层寻路缺陷修复与工具链/归档完整性补强，无数据表字段
删改，无存档格式变更。

### 新增

- **`Core.Numbers.StatBlock.StatHost.ResetBase(Id unitId, Id stat)`**（新增公开方法，CORE-110-02
  根治）：清除某单位某属性此前显式 `SetBase` 过的值，恢复成"从未显式设置过"（即
  `stat.definition.default_base`）；不是 `IStatHost` 接口成员，不影响任何既有 `IStatHost` 假实现。
- **`WorldMapSchema`（`world.map`）四字段补齐类型校验**：`regions`（`IdList`）、
  `teleport_points`（`Array`）、`music_ref`（`String`）、`allowed_difficulties`（`IdList`），均为
  可选字段，按 `05_对象模型与世界.md` 第 4.1 节原文类型/必填性登记，只做类型校验、不做引用完整性
  校验（无独立登记表可查）；`data/_sample`/`data/_framework` 现有 `world.map` 记录均未使用这四个
  字段，登记不影响既有数据。
- **`UnityNavigation2D` 端点接合/薄障碍兜底**：`FindPath` 起止格量化后若格中心恰好落在阻挡区域
  内部，新增私有 `ResolveEntryCell` 在其 8 邻居里找一个可行走且与精确端点直连不受阻的格子接合
  （NAV-110-01 根治）；`FindPath` 收尾复核（逐段 `Raycast`）未通过时，新增私有
  `FindPathWithFineGrid` 用半格尺寸 + 精确占用判定的独立细网格重新完整寻路一次作为兜底，仍不通过
  才是真正无路可走（NAV-110-02 根治）——均为类型内部私有实现细节，`INavigation2D` 契约签名不变。
- **`EquipmentWeaponStyleSource` 订阅 `save.loaded`**（PRES-110-01 根治）：构造函数新增订阅，收到
  后整表清空内部风格缓存，下一次查询对全部实体重新解析真实 `EquipmentHost` 状态。
- **方向移动导航阻挡契约拍板落地（NAV-DOC-02）**：`MovementTickHandler.ApplyDirectionalMove` 新增
  对候选终点的 `Raycast`/`IsWalkable` 检查，与目标类移动共用同一套统一可通行规则（受阻按
  `MovementOptions.ArrivalEpsilon` 截断到入射点前，截断后仍不可行走则不动）；`architecture/05` 第
  6.1 节同步勘误。

### 修复

- **失败读档回滚后派生状态（评级换算属性/光环/资源池上限-当前值）不恢复（CORE-110-01，P2）**：
  `SaveSystem.RollbackLoadedSections` 此前只覆盖字段本身（`IPersistable.Load(快照)`），不重新调用
  `IDerivedStateRebuilder.OnSectionLoaded`——回滚只恢复字段不等于恢复由字段推导出的派生状态。改为
  对每个成功回滚的段按与正常读档相同的正向顺序重放一次 `OnSectionLoaded`；失败分支的回滚集合额外
  总是尝试把 `player.vitals`（资源池当前值唯一权威段）纳入，即便它本不在这次失败读档实际触碰过的
  段列表里。
- **同图跨职业读档只覆盖新旧共同基础属性键，旧职业独有键与资源类型集合未清理（CORE-110-02，P2）**：
  `RulesAssembly.ReloadArchetypeAndRace` 此前完全忽略 `previousClassId` 参数。改为按"旧种族修正 →
  旧职业独有基础键（`StatHost.ResetBase`）→ 新职业完整基础键 → 新种族修正/光环 → 资源类型对账"
  顺序根治（资源类型对账必须放最后，因为 `PowerHost.RegisterUnit` 按注册时刻的属性聚合值初始化
  上限）。
- **同一 tick 内多条 move 意图逐条重复推进位移（CORE-110-03，P2）**：`MovementTickHandler.Execute`
  此前对本 tick 存活的每一条 move 意图各自调用一次 `ApplyIntent`，同一单位同一 tick 提交 N 条意图
  会把固定的 dt 重复消费 N 次。落地公共语义"每单位每 tick 只积分一次，同 tick 多条 move 意图最后
  一条生效"：先扫描每单位本 tick 最后一条存活意图的下标，第二趟遍历只在命中该下标时调用一次
  `ApplyIntent`（用两趟遍历、不依赖 `Dictionary` 迭代顺序，保证单位间处理顺序与修复前一致）。
- **Unity 导航端点所在采样格中心被阻挡时 `FindPath` 拒绝可达路径（NAV-110-01，P2）**：见上"新增"
  `ResolveEntryCell`。
- **Unity 网格 A* 对窄于采样间距的薄墙视而不见，收尾防线命中即直接返回 `null`、不尝试绕路
  （NAV-110-02，P2）**：见上"新增"`FindPathWithFineGrid`。
- **读档后 `EquipmentWeaponStyleSource` 缓存未随 `SaveSystem.Load` 抑制作用域内重放的装备事件失效
  （PRES-110-01，P2）**：见上"新增"`save.loaded` 订阅。核对同类缓存来源
  `presentation/render/core/EquipmentVisualSource.cs`：理论上受同一抑制作用域问题影响，但不是同一
  种缺陷形状（无状态查询缓存 vs. 已应用到具体 View 实例的状态日志），不外推入本次修复范围，留待
  该模块自己的复现证据立项。
- **归档证据被 `.gitignore` `**/[Ll]ogs/` 规则误伤**：该规则未加路径前缀，连带忽略了
  `architecture/落地计划/audit-*/**/logs/` 这类要长期留存的审计证据目录；收窄为
  `adapters/unity/**/[Ll]ogs/`（只限定 Unity 工程自身日志缓存）。找回 e070e3f/3224ca1/8160178/
  c86bfa9 四个既往归档目录下共 30 处因此漏收录的证据文件；2 处原件已随会话清理彻底丢失（presentation
  动画回归证据）的链接如实标注"原件未归档"并登记进新增的 `toolchain/tests/.linkcheck-ignore` 白
  名单。
- **相对链接检查此前对 `audit-*` 目录整段排除，从未扫描审计报告内的证据链接**：
  `toolchain/tests/test_markdown_relative_links.py` 取消该排除，改为逐条判断（`.gitignore` 覆盖
  路径照旧跳过；新增识别 `path.cs:123` 源码行号引用记法并剥离后缀再判存在性；显式豁免走新增的
  `.linkcheck-ignore` 白名单）。
- **`MoveStopReason.BlockingChanged` XML 注释错误地把 `PathFailurePolicy.Stop` 失败归为该原因**（
  实际触发 `PathFailed`）、`LootExpiryTickHandler.cs:35` 旧注释与当前真实局部语义不符：均已改正，
  不改变任何运行期行为。

### 行为变更与迁移说明

- **同 tick 多条 move 意图的位移语义变更**：升级前同一单位同一 tick 提交 N 条 move 意图会把固定
  dt 重复消费 N 次（等价于"叠加位移"）；升级后固定为"最后一条生效，dt 只消费一次"。**依赖旧行为
  （连续提交多条 move 意图来叠加本 tick 位移量）的调用方需要改为一次性提交携带最终目标/方向的单条
  意图**——这不是框架推荐或文档化过的用法，是此前实现的副作用；`OnMoveStopped(Replaced)` 触发
  语义不变（至多一次，取决于上一 tick 是否已有路径，与本 tick 内提交过几条被丢弃的意图无关）。
- **方向移动现在遵守导航阻挡**：升级前 `ApplyDirectionalMove` 只检查同帧其它单位阻挡，从不查询
  `INavigation2D.IsWalkable`/`Raycast`，可以穿过地形阻挡；升级后与目标类移动共用统一可通行规则，
  受阻会被截断或完全不位移。**依赖旧行为（方向移动可以穿越导航阻挡区域）的调用方需要重新评估**——
  未装配 `INavigation2D` 的场景（`_navigation == null`）两项检查全部跳过，行为与升级前完全一致。
- **失败读档回滚后派生状态重建**：升级前失败读档回滚只恢复字段本身，评级换算属性/光环/资源池
  上限可能停留在读档失败前的陈旧值；升级后回滚会重放 `IDerivedStateRebuilder.OnSectionLoaded`。
  自定义 `IDerivedStateRebuilder` 实现需保证该回调幂等/可重入（此前只在成功路径被调用，现在也会
  在失败回滚路径被调用）。
- **跨职业读档清理旧职业基础键与资源类型**：升级前同图切换职业只覆盖新旧共同基础属性键，旧职业
  独有的基础属性键与资源类型（如法力）残留；升级后会清理干净。依赖"残留旧职业独有属性/资源类型"
  这一此前未文档化行为的调用方需要重新评估。
- **`.gitignore` `**/[Ll]ogs/` 规则收窄为 `adapters/unity/**/[Ll]ogs/`**：仓库根或
  `architecture/`/`core/` 等位置下字面叫 `logs` 的目录不再被自动忽略；若本机有依赖旧忽略范围
  存放临时文件的习惯，需要自行清理或改用其它已忽略目录（如 `bin/`）。

### 版本判据说明

- MINOR：`StatHost.ResetBase` 是新增公开方法（非接口成员）；`WorldMapSchema` 四字段均为可选字段
  的类型校验补齐，不影响既有数据；`UnityNavigation2D`/`EquipmentWeaponStyleSource` 新增成员均为
  类型内部实现细节，契约接口签名不变；三处"行为变更"均是缺陷修复（此前行为未被文档化为预期契约、
  且与既有文档/契约表述矛盾），按仓库既有版本判据惯例（见 1.9.0/1.10.0 条目）计入 MINOR 而非
  MAJOR。无删改既有公开签名，无存档格式变更，无数据表字段删改。

## [1.10.0] - 2026-09-09

导航与移动公共接口补齐（W9，响应游戏侧 5 项需求：停止/取消接口、端点契约与精确接合、寻路与
Raycast 拐角判定统一、寻路失败公共处理契约、动态阻挡后现有路径处理），逐项落地见下"新增"/
"生命周期与事件顺序"/"迁移说明"三节；核心侧与引擎侧改动、`core` 六工程 2533/2533 与 Unity
EditMode 60/60、PlayMode 263/263 验收过程中另发现并根治两处真实缺陷（非本次新增功能，见"修复"
一节）。均属核心载体层/引擎适配层能力补齐，无数据表字段删改，无存档格式变更。

### 新增

- **`Core.Carriers.Unit.MovementHost.Stop(Id unitId)`**（需求 1 落地）：经 `IWorldSim.SubmitIntent`
  提交一条 `Kind == "move_stop"` 的意图，下一次移动与导航阶段生效；不检查 `MovementLocked`/
  `NoMove`（停止不受控制效果限制）；同一 tick 内幂等（重复 `Stop` 只触发一次 `OnMoveStopped`）。
- **`Core.Carriers.Unit.MovementHost.OnMoveStopped`**（需求 1 落地）：
  `delegate void MoveStoppedHandler(Id unitId, Vec2 position, MoveStopReason reason);`，
  `enum MoveStopReason { Requested, PathFailed, BlockingChanged, Replaced }`——分别对应主动
  `Stop`、按 `PathFailurePolicy.Stop` 因寻路/重算失败停止、按 `BlockingChangePolicy.Stop` 因阻挡
  变化停止、新 `Request` 替换仍存在的旧路径。
- **`Core.Carriers.Unit.MovementHost.OnMoveFailedDetailed`**（需求 4 落地）：
  `delegate void MoveFailedDetailedHandler(Id unitId, Vec2 from, Vec2 to, MoveFailReason reason);`，
  `enum MoveFailReason { NoPath, BlockingChanged }`；与既有 `OnMoveFailed`（签名/触发时机不变）在
  同一失败点同时触发，是补充而非替代。
- **`Core.Carriers.Unit.MovementOptions.PathFailurePolicy`**（需求 4 落地，新增顶层枚举
  `{ KeepOldPath（默认）, Stop }` + 同名属性）：寻路失败或阻挡重算失败时的公共处理策略。
- **`Core.Carriers.Unit.MovementOptions.BlockingChangePolicy`**（需求 5 落地，新增顶层枚举
  `{ Replan（默认）, Revalidate, Stop, Ignore }` + 同名属性）：导航阻挡发生变化后，对单位已持有
  的现存路径的处理策略。
- **`Core.Carriers.Unit.MovementState.NavVersion`**（需求 5 落地，`int`，构造函数追加末位可选
  参数 `navVersion = 0`，源码兼容）：记录该单位当前路径建立时所依据的导航阻挡版本号，供阻挡重验
  比对。
- **`Core.Foundation.EngineAdapter.INavigation2D.GetBlockingVersion(Id mapId) => 0`**（需求 5
  落地，默认接口成员）：每次 `SetBlocking`/`Clear`/`BuildNavMesh` 使某地图可行走判定结果变化时
  递增（各 `mapId` 独立计数）；返回 0 表示不支持版本追踪，调用方对 0 视为"不做自动重验"——未
  重写本成员的既有实现（含未来新增的引擎适配层实现）源码/二进制兼容，行为等价于"不支持版本
  追踪"。`Adapters.Stub.StubNavigation2D`、Unity `UnityNavigation2D` 均已实现真实的按地图计数。
- **`INavigation2D.FindPath` 端点契约精确化**（需求 2 落地，契约文档 + `StubNavigation2D`/
  `UnityNavigation2D` 网格实现同步）：`from`/`to` 任一不可行走返回 `null`（优先于零长度判断）；
  `|from-to| <= 1e-6` 返回单元素路径 `[from]`；成功路径 `path[0]` 精确等于 `from`、
  `path[^1]` 精确等于 `to`（网格路径与精确端点的"接合段"复用与 `Raycast` 相同的判定，接合失败
  退化到相邻可行走格或返回 `null`）。
- **`Raycast`/`FindPath` 统一可通行规则**（需求 3 落地，契约文档 + `StubNavigation2D`/
  `UnityNavigation2D` 同步）：线段与阻挡区域**内部**相交才受阻，仅边界/角点相切不算受阻；
  `FindPath` 返回路径的每一段 `Raycast` 必为 `null`；网格实现对角相邻格仅当两个正交邻居都可
  行走时才允许联通（禁止切角）。
- **`adapters/conformance` 一致性场景**：`Navigation2DScenarios` 新增 5 个场景（端点精确接合、
  零长度路径、不可行走端点返回 null、双矩形拐角每段 `Raycast` 不受阻、`GetBlockingVersion`
  随三类变更操作递增——该场景对返回恒为 0 的实现走 `assert.Skip`，视为合法退化），
  `INavigation2D` 场景数由 4 增至 9，跨桩实现与 Unity 实现同源驱动。

### 生命周期与事件顺序

`Core.Carriers.Unit.MovementTickHandler.Execute` 按固定四步推进（原三步基础上插入阻挡重验一
步，未改变既有推进逻辑本身）：

1. **停止**：处理本 tick 全部 `move_stop` 意图——清空 `CurrentPath`、状态收回 `Idle`，丢弃同一
   tick 内在它之前提交的该单位 `move` 意图（之后提交的照常生效）；确有路径或被丢弃的意图时触发
   `OnMoveStopped(Requested)` 一次，否则静默（幂等）。
2. **移动**：处理存活的 `move` 意图——零长度目标不建路径、不动、不回调；寻路失败触发
   `OnMoveFailed` + `OnMoveFailedDetailed(NoPath)`，按 `PathFailurePolicy` 处理；新路径替换仍
   存在的旧路径时触发 `OnMoveStopped(Replaced)`，随后立即推进本 tick 位移。
3. **阻挡重验**（仅未被前两步处理、仍持有路径的单位）：比较 `GetBlockingVersion` 与
   `MovementState.NavVersion`，不同则按 `BlockingChangePolicy` 处理（`Replan` 直接重算；
   `Revalidate` 先逐段 `Raycast`、受阻再委托重算；`Stop` 直接清空并触发
   `OnMoveStopped(BlockingChanged)`；`Ignore` 不处理）；重算/重验失败触发
   `OnMoveFailed` + `OnMoveFailedDetailed(BlockingChanged)`，再按 `PathFailurePolicy`（`Stop`
   分支触发 `OnMoveStopped(PathFailed)`，与"直接因阻挡变化而停止"的 `BlockingChanged` 原因区分
   开）。
4. **推进**：`ContinuePathCore`（逻辑不变）。

重入安全：`Stop`/`Request` 都只是 `SubmitIntent`（下一 tick 生效），回调内同步调用二者不会在本次
`Execute` 内递归触发新的失败/停止回调。

### 修复

- **`ReplanPath` 重算失败时未推进 `NavVersion`，导致同一次阻挡变化在后续每个 tick 都重复触发一
  次 `OnMoveFailedDetailed`**：默认 `PathFailurePolicy.KeepOldPath`（旧路径原样保留）分支下，
  `ReplanPath` 重算失败只触发了失败回调，没有把 `MovementState.NavVersion` 前移到本次读到的
  `currentVersion`，下一个 tick 阻挡重验比较仍判定"版本已变化"，对同一次阻挡变化重新调用一次
  `ReplanPath`——再次失败、再次回调，此后每个 tick 都重复，直到阻挡状况本身改变。改为该分支下
  显式把 `NavVersion` 前移到 `currentVersion`（语义与"未受阻分支只更新版本号"一致，标记"已经按
  这个版本处理过，虽然重算失败"）；`Stop` 分支路径已被清空，不受影响。新增回归测试
  `MovementTickHandlerTests.BlockingChangePolicy_ReplanFails_DefaultKeepOldPathPolicy_
  DoesNotRepeatFailureEachTick`（默认策略下重算失败后再跑 10 个 tick，失败计数仍为 1，修复前会
  变成 11）。
- **Unity PlayMode 新增测试夹具耗尽跨批次共享的存档槽配额，连带导致同一批次里无关用例静默失败**：
  `SaveSystemOptions.MaxSlots`（默认 20）跨整个 `-runTests` 单次批处理进程共享、只增不减；新增的
  `MovementStopAndBlockingPlayModeTests` 最初每条用例各建一个独一无二的新槽，把既有用例累计已接近
  上限的运行推过 20，导致按夹具名排在更后面的 `VerticalSliceTests` 5 条用例在尝试新建槽时命中
  `SaveFailureReason.SlotLimitReached`，`ShellHost.NewGame` 因 `_saveSystem.Save(...).Success` 为
  `false` 直接 `return false`，不会走到 `_sceneRouter.LoadScene`（单独跑各夹具都各自全绿，只有混
  在完整套件里跑才复现，与仓库既有 `GlobalPlayModeTestSetup.cs` 描述的历史根因同一模式）。改为
  本套件全体用例改用同一个共享存档槽 id——第一次调用消耗 1 份新建配额，此后每条用例的 `NewGame`
  对同一个已存在的槽只是覆盖重写，不再消耗新配额；`PlayModeIsolation.TearDownAfterTest` 已在每条
  用例结束时 `World.ClearAll`，复用同一槽 id 不影响各用例世界状态隔离。

### 迁移说明

- **默认口味下行为逐位一致**：`PathFailurePolicy.KeepOldPath` + `BlockingChangePolicy.Replan`
  是默认值，且未显式实现 `GetBlockingVersion` 的导航实现恒返回 0（阻挡重验整体不生效）——升级
  前后在默认配置下行为逐位一致，回归测试覆盖全部既有用例（`core` 六工程与 Unity EditMode/
  PlayMode 既有用例原样通过，未修改任何既有断言）。
- **既有 `OnMoveFailed` 保留，不是替代关系**：签名与触发时机均不变，新增的 `OnMoveFailedDetailed`
  在同一失败点额外触发，只订阅旧事件的调用方无需任何改动。
- **自定义 `INavigation2D` 实现若要启用自动阻挡重验，需要显式实现 `GetBlockingVersion`**：该成员
  是默认接口方法，不实现不影响编译，但也不会得到自动重验能力（等价于"未支持"，不是"选择
  `BlockingChangePolicy.Ignore`"）——需要在自身的 `SetBlocking`/`Clear`/`BuildNavMesh` 落地方法
  内对相应 `mapId` 递增一个私有版本号计数器并在本方法中返回。
- **`FindPath` 端点精确化对依赖"格子中心"输出的调用方有影响**：升级前部分网格实现可能返回贴近
  网格中心而非精确等于传入 `from`/`to` 的路径端点；升级后 `path[0]`/`path[^1]` 精确等于调用方
  传入的浮点坐标。若调用方此前对首/末点做过"对齐到格子中心"之类的后处理补偿，该后处理现在是
  多余的（不会再有偏差需要补），可以安全移除，不移除也不会出错（幂等对齐同一点）。
- **`Raycast`/`FindPath` 边界相切语义修正（`StubNavigation2D.ClipAxis` 由闭区间改为开区间）**：
  升级前贴边/擦角（线段与阻挡矩形边界或角点相切、不进入内部）会被判定为"受阻"；升级后改为"内部
  相交才受阻，边界/角点相切不算受阻"，与"网格路径的每一段 `Raycast` 必为 `null`"这一契约保持
  一致（此前贴边场景下二者可能矛盾）。依赖旧行为（把贴边当受阻）的调用方需要重新评估——
  `IsWalkable`（点包含判定）未改动，仍是闭区间，本次统一可通行规则的范围限定于线段判定
  （`Raycast`/`FindPath`），不涉及单点判定。
- **`games/_template`/`architecture/13` 未新增对应口味配置项**：`GameOptions.BuildMovementOptions()`
  目前只接了 `DiscreteTurnEquivalentSeconds`/`UnitBlocking` 两项，未暴露
  `PathFailurePolicy`/`BlockingChangePolicy` 作为口味配置项；两个策略经 `MovementOptions` 在
  装配根（`GameBootstrap`/组合根构造 `MovementOptions` 处）直接配置，默认值即为框架推荐值，游戏
  层如需覆盖自行在装配根按需传入，不是缺失能力。

### 版本判据说明

- MINOR：`MovementHost.Stop`/`OnMoveStopped`/`OnMoveFailedDetailed` 均为新增公开成员；
  `MovementOptions`/`MovementState` 均只新增属性/带默认值的可选构造参数；
  `INavigation2D.GetBlockingVersion` 是默认接口方法；`FindPath`/`Raycast` 的契约精确化与边界
  语义修正均不改变方法签名，且默认口味 + `GetBlockingVersion` 恒为 0 时行为与升级前逐位一致。
  无删改既有公开签名，无存档格式变更。

## [1.9.0] - 2026-09-09

第十三方深度审核（codex 第十一轮，基线 `e070e3f`，即 1.8.0 发布提交）3 项确认缺陷
（CORE-180-01～03）+ 1 项候选转已确认（CORE-180-CAND-01）+ 1 项表现层候选转已确认（PRES-180）逐条
核实并根治，另处理 5 项文档漂移/工程证据勘误。逐条核实表、文档更新与能力分类处理、验收结果见
[audit-e070e3f-20260908/followup-2026-09-08g.md](architecture/落地计划/audit-e070e3f-20260908/followup-2026-09-08g.md)。
均属核心存档/规则层缺陷修复与表现层读档对账补强，无数据表字段删改，无存档格式变更。

### 新增

- **`Core.Foundation.SaveSystem.IDerivedStateRebuilder`**（新增契约，`BeforeLoad()`/
  `OnSectionLoaded(string sectionKey)`，CORE-180-01 根治）：可选注入到 `SaveSystem` 的回调，完全
  绕开事件总线（不产生任何可观察事件，不受 `SuppressDispatch` 影响）；`SaveSystem.Load` 在逐段读档
  前调用一次 `BeforeLoad()`，每段成功 `Load()` 后立即调用一次 `OnSectionLoaded(sectionKey)`，供
  装配根在读档期间就地重算评级换算属性、资源池上限等派生缓存，不等读档全部完成、不依赖事件重放。
- **`Core.Foundation.SaveSystem.ISaveSystem.SetDerivedStateRebuilder`**（默认接口方法，默认为
  no-op，CORE-180-01 根治）：注入上述重建器；自定义 `ISaveSystem` 实现方无需新增任何代码即可编译
  通过。
- **`Core.Rules.Assembly.RulesAssembly.ReloadArchetypeAndRace`**（新增公开方法，CORE-180-03 +
  CORE-180-CAND-01 根治）：同图读档后重新聚合单位的种族属性修正/被动光环与职业基础属性，不重复
  调用 `PowerHost.RegisterUnit`（避免对已注册单位抛"不能重复注册"异常）；种族切换时精确移除旧种族
  来源的属性修正、按引用计数递减释放旧种族被动光环，覆盖写入职业基础属性。
- **`Presentation.Common.ISimSnapshot.GetAllEntityIds`/`GetRawKind`**（新增只读成员，均为默认接口
  方法，PRES-180 根治，2026-09-09 版本判据勘误后改写）：`GetAllEntityIds()` 返回当前存活实体 id
  全量列表（默认返回空集合），`GetRawKind(Id)` 返回映射前的原始 `Entity.Kind` 字符串（默认返回
  `null`）；供 `ViewBinder` 在 `save.loaded` 后与已绑定 View 表做全量对账。唯一生产实现
  `WorldSimSnapshot` 已同步覆盖为真实实现；自定义 `ISimSnapshot` 实现方无需新增任何代码即可编译
  通过——未覆盖这两个成员时 `ViewBinder.OnSaveLoaded` 的对账安全退化为"只销毁已不存在实体的
  View，跳过按存活实体补建 View"，详见下"迁移说明"与 `ISimSnapshot`/`ViewBinder.OnSaveLoaded`
  源码判断记录。
- **`ViewBinder` 读档后视图对账**（PRES-180 根治）：构造函数新增订阅 `save.loaded`
  （`SaveEventKeys.SaveLoaded`），收到后立即（同步，不等下一次 `sim.tick_finished`）做一次双向全量
  对账——`ISimSnapshot.Exists` 为假但仍持有绑定的按 `OnEntityDestroyed` 销毁，`GetAllEntityIds()`
  中存在但未绑定的按 `OnEntityCreated` 补建，补齐"读档期间 `SuppressDispatch` 抑制丢弃
  `entity.created`/`entity.destroyed`导致 View 与逻辑实体不一致"这一缺口，幂等（重复读档不重复
  创建/销毁）。

### 修复

- **成功读档后评级换算属性/资源池上限未重算（CORE-180-01，P1）**：`SaveSystem.Load` 整段包在
  `IEventBus.SuppressDispatch` 抑制作用域内，`stat.changed`/`PowerChanged` 等事件被抑制丢弃，导致
  依赖这些事件重算的评级换算属性、资源池上限恢复到读档前的旧值，即便等级/装备等原始字段已正确
  恢复。`player.vitals` 段按"存档值与当前值差额"调用 `ModifyPower` 时若上限仍是旧值，差额被 clamp
  到旧上限，`RecomputeMax` 只在 `Current > newMax` 时下调、不会在 `newMax` 变大时补回 `Current`，
  当前值即便后续再重算上限也不会跟着回升。改为经 `IDerivedStateRebuilder.OnSectionLoaded
  (PlayerEquipment)` 在读到 `player.vitals` 段之前完成一次重算，完全绕开事件总线，不影响既有"读档
  期间业务事件零泄漏"回归。
- **后段读档失败时逆序回滚顺序与依赖方向相反（CORE-180-02，P2）**：`RollbackLoadedSections` 此前
  从后往前遍历，装备重新装备（复用真实 `EquipmentHost.Equip`，内部校验等级需求）先于等级本身被
  回滚恢复，导致等级需求校验用的是本次失败读档写入的低等级值，装备重新装备失败、物品被迫留在
  背包。改为与正常读档同一顺序（从前往后）遍历，回滚本质上变成"再做一次读档，只是文档换成读档前
  的快照"，不需要为回滚单独维护一套依赖顺序规则。
- **同图读档只切换种族/职业字段，未重新聚合对应的属性修正与光环（CORE-180-03 + CAND-01，P2）**：
  `GameplayAssembly.RestoreFromSlot` 判定目标地图与当前地图相同时不触发 `EnterMap`，此前读到
  `player.race_id`/`player.archetype` 段只覆盖字段本身，不会重新聚合旧种族属性修正的移除、新种族
  属性修正/光环的应用，也不会覆盖写入新职业的基础属性。改为经 `IDerivedStateRebuilder.
  OnSectionLoaded(PlayerRaceId)` 触发 `RulesAssembly.ReloadArchetypeAndRace`，同图与跨图路径统一
  覆盖。已知收边范围：新旧职业基础属性键集合不同、或 `power_types` 集合不同时的联动不在本次范围内
  （详见 `RulesAssembly.ReloadArchetypeAndRace` 源码注释与 followup 文档判断记录）。
- **同图读档期间被抑制丢弃的 `entity.created`/`entity.destroyed` 导致 View 与逻辑实体不同步
  （PRES-180，候选转已确认）**：`SaveSystem.Load` 抑制作用域内若某段 `Load`（如
  `DroppedLootPersistable.Load`）往 `WorldSim` 加实体，产生的 `entity.created` 永久丢失，逻辑实体
  已恢复但对应 View 未绑定；`ViewBinder` 此前只在构造期订阅事件，没有读档完成后的补扫入口。改为
  订阅 `save.loaded` 并做全量对账（见上"新增"一节）。

### 迁移说明

- **自定义装配根需注册派生状态重建器**（CORE-180-01/03）：若具体游戏/适配层提供了自定义
  `ISaveSystem`/装配根替代框架默认的 `GameplayAssembly`，且存在依赖事件重算的派生缓存（评级换算
  属性、资源池上限、种族/职业属性修正与光环等），需要实现 `IDerivedStateRebuilder` 并调用
  `ISaveSystem.SetDerivedStateRebuilder` 注册，否则读档后这些派生缓存不会重算，行为等同于本次修复
  之前的框架默认实现。不注册不影响编译（`SetDerivedStateRebuilder` 是默认接口方法），只影响读档后
  派生缓存的正确性。
- **自定义 `ISimSnapshot` 实现可选覆盖两个新成员**（PRES-180，2026-09-09 版本判据勘误后改写）：
  `GetAllEntityIds`/`GetRawKind` 是 C#8 默认接口方法（默认分别返回空集合/`null`），与本版本其它
  新增接口成员同一惯例——任何自定义 `ISimSnapshot` 实现无需新增任何代码即可继续编译通过。
  `GetAllEntityIds()` 语义为"当前存活实体 id 全量列表"，`GetRawKind(Id)` 语义为"映射前的原始
  `Entity.Kind` 字符串，实体不存在返回 null"，均为只读查询、不持有 `Entity` 引用本身（铁律 P1）；
  框架内唯一生产实现 `WorldSimSnapshot` 已同步覆盖为真实实现。若不覆盖，`ViewBinder` 的
  `save.loaded` 全量对账会安全退化：销毁"绑定表里指向已不存在实体"的陈旧 View 这一半不受影响
  （只依赖原有强制成员 `Exists`），"按当前存活实体补建 View"这一半因 `GetAllEntityIds()` 返回
  空集合而天然是空操作——即读档期间被 `SuppressDispatch` 抑制丢弃的 `entity.created` 不会被这类
  自定义实现补扫到，需要完整对账能力的自定义 `ISimSnapshot` 实现方应显式覆盖这两个成员。
- **自定义 `IViewFactory` 实现须保证创建幂等**（PRES-180）：`ViewBinder` 的读档后对账在
  `GetAllEntityIds()` 中存在但未绑定的实体上复用既有 `OnEntityCreated` 路径调用
  `IViewFactory.Create`；若某具体游戏/适配层的 `IViewFactory` 实现对同一实体重复调用 `Create` 会
  产生副作用（如资源重复分配），需要确认其自身具备"同一实体已存在 View 时安全跳过或替换"的幂等
  处理——框架侧 `ViewBinder` 已经用绑定表去重、不会对同一实体重复调用 `Create`，此处针对的是
  `IViewFactory` 实现自身在异常路径下被多次调用时的健壮性。
- **版本判据说明**：本次为 MINOR（`1.8.0` → `1.9.0`）。"新增"一节列出的成员均为新增（新增
  类型/默认接口方法/公开方法/接口成员），无删改既有公开签名；`ISimSnapshot` 新增的
  `GetAllEntityIds`/`GetRawKind` 两个成员（2026-09-09 版本判据勘误后改为）同样是默认接口方法，
  第三方 `ISimSnapshot` 实现方无需新增代码即可继续编译，不构成源码级破坏，按 MINOR 处理不需要
  任何例外说明。（此前一版曾把这两个成员定义为普通接口方法并按"源码级破坏但按 MINOR 处理"的
  例外记录在案，属于版本判据误判——已改为默认接口方法根治，不再需要该例外，具体见上方"新增"/
  "迁移说明"两处改写内容。）

## [1.8.0] - 2026-09-08

第十二方深度审核（codex 第十轮，基线 `8160178`，即本版本发布前的最新提交）4 项发现（CORE-170-01～03、
PRES-170-01）逐条核实并根治，CORE-170-03(a) 审查全仓 `IPersistable` 实现后另发现 8 处同类"先改状态
后校验/边解析边提交"缺陷一并根治。逐条核实表、文档更新与能力分类处理、验收结果见
[audit-8160178-20260908/followup-2026-09-08f.md](architecture/落地计划/audit-8160178-20260908/followup-2026-09-08f.md)。
均属核心规则/存档/玩法层缺陷修复与表现层/引擎适配层缺陷修复，无数据表字段删改，无存档格式变更。

### 新增

- **`Core.Rules.Common.AuraHandleLedger`**（跨来源光环句柄账本，CORE-170-01 根治）：把装备/套装门槛
  加成原有的"跨来源引用计数账本"上移为独立类型，由 `RulesAssembly` 持有单一实例，`CarriersAssembly`
  注入给 `EquipmentHost`，`RulesAssembly` 自身（种族被动）也用同一实例，装备/套装/种族三类来源共享
  同一条账本，互不覆盖对方持有的引用。
- **`Core.Rules.Common.IAuraQuery.TryGetInstanceRef`**（默认接口方法，默认返回 `null`，CORE-170-01
  根治）：按单位与光环定义查询该单位当前是否持有一份有效引用；自定义 `IAuraQuery` 实现方无需新增
  任何代码即可编译通过，默认实现对不参与跨来源账本的测试假实现是安全的等价空实现。
- **`Core.Numbers.Progression.LevelSync`**（委托类型）与 **`Core.Carriers.Unit.WorldUnitAccess.
  SetLevel`**（CORE-170-02 根治）：`ProgressionHost` 在 `RegisterUnit`/`AddXp`/`RestoreState` 三个
  等级确立/变化的时机调用该委托，`CarriersAssembly` 接到 `WorldUnitAccess.SetLevel`（不进
  `IUnitAccess` 接口，仅供组装期委托闭包使用），确立 Progression 为单位等级唯一权威并同步实体
  字段与查询结果。
- **`Core.Foundation.EventBus.IEventBus.SuppressDispatch`**（默认接口方法，默认返回一个 no-op
  `IDisposable`，CORE-170-03(b) 根治）：调用方在 `using` 作用域内产生的领域事件（`Enqueue`/
  `PublishImmediate`）直接丢弃，不派发给订阅者；`SaveSystem.Load` 用它把逐段读档 + 失败回滚整体
  包进抑制作用域，避免回滚期间重放的领域事件被业务消费者（如 `AchievementHost`）误计数。自定义
  `IEventBus` 实现方无需新增任何代码即可编译通过。
- **`Core.Foundation.SaveSystem.SaveSections.KnownOrder` 纳入 `world.gobj_pending_loot`**：此前该
  段未登记进 `KnownOrder`，落入"自定义段"分支按 key 序数排序；现按 10 号文档"7a.
  .../spawn_state/gobj_pending_loot"既有文字顺序固定登记，只影响读档时的段处理顺序，不改变段的
  存在性或字段形状。

### 修复

- **跨图重放后卸装误删种族 aura（CORE-170-01，P2）**：装备/套装/种族共享同一 `auraDef` 时，种族
  被动此前只按 `HasAura` 判断是否需要重放，没有独立的来源账本；卸下装备会连带清除种族来源的同一份
  光环及其属性修正。改为三类来源各自维护"我持有哪个句柄"的簿记，互不代劳。
- **Progression 等级与实体等级分叉（CORE-170-02，P2）**：`ProgressionHost.AddXp`/`RestoreState`
  更新内部等级后未同步 `PlayerUnit.Level`，导致 `WorldUnitAccess.GetLevel` 与规则层查询到的等级
  不一致，等级需求装备可能因此误判 `RequirementNotMet`。改为写入时同步，Progression 是唯一权威。
- **存档失败段自身不回滚，且回滚期间产生的领域事件污染业务消费者（CORE-170-03，P2，两个已确认
  表现）**：(a) `EquipmentPersistable.Load` 及审查全仓 `IPersistable` 实现后另发现的 8 处同类
  缺陷（`AchievementHost`/`SpawnHost`/`WorldState`/`DifficultyHost`/`CurrencyPersistable`/
  `VendorStockPersistable`/`RngStreamsPersistable`/`SkillBindingPersistable`）此前均"先改变运行期
  状态、后校验数据形状"或"边解析边直接调用 live host 写方法"，坏存档会在状态已被部分或全部改动后
  才抛异常，且 `SaveSystem.Load` 此前只把"此前已成功加载"的段纳入回滚列表，抛异常的段自身不在
  其中；现全部改为"先解析校验成临时恢复计划、再一次性提交"，`SaveSystem.Load` 额外把失败段自身
  纳入回滚列表兜底。(b) `SaveSystem.Load` 逆序回滚时会调用 live host 的真实写方法（如
  `EquipmentPersistable.Load` 复用真实 Equip/Unequip 逻辑），产生的真实领域事件被业务消费者
  （`AchievementHost`）当成真实玩家操作再次计数，导致成就进度被错误推高甚至误解锁；现读档与回滚
  期间整体抑制领域事件派发，`SaveMigratedEvent`/`SaveLoadedEvent` 仍在读档完成后正常派发。
- **共享 `AnimationClip` 被空事件配置和跨 factory 状态污染（PRES-170-01，P2）**：`UnityViewFactory.
  RegisterModelClipEvents` 此前对空 `events` 配置直接跳过、不建立任何基线或隔离；首个非空配置从
  当前共享剪辑资产捕获"pristine"快照后把合并结果写回该**共享**资产；承载基线/签名/覆盖状态的三张
  表此前是 factory 实例字段。三者叠加导致同一 factory 内空配置 anim_set 会看到另一个非空配置写入
  的数据事件，新建 factory（典型触发：场景重进）会把旧 factory 写入共享资产的事件误当成美术自带
  基线保留。现改为进程级静态表缓存美术自带基线，任何非空配置都以基线为底合并出一份运行期私有
  副本，只经 `AnimatorOverrideController` 套用到具体 `ModelHandle` 实例，共享剪辑资产自始至终
  不被写入。

### 迁移说明

- **单位等级唯一权威改为 `Core.Numbers.Progression.ProgressionHost`**（CORE-170-02）：直接改写
  `PlayerUnit.Level` 字段而不经 `ProgressionHost.RegisterUnit`/`AddXp`/`RestoreState` 的具体游戏
  代码，其改动会在下一次上述三个方法被调用时被 `LevelSync` 覆盖同步；需要设置单位等级的具体游戏
  代码应统一改走 `ProgressionHost` 相应方法，不要再直接写 `PlayerUnit.Level` 字段。
- **读档与回滚期间领域事件被抑制**（CORE-170-03(b)）：依赖"读档期间正常成功加载某段会让该段产生
  的事件到达外部订阅者"这一行为的具体游戏代码（例如监听 `ItemEquipped` 来更新 UI）需要改为在
  `SaveLoadedEvent`（读档完成后正常派发）到达后按当前状态重建一次，不能再假设读档过程中会收到
  逐段变化事件；`SaveMigratedEvent`/`SaveLoadedEvent` 本身不受影响，仍会正常派发。
- **自定义 `IPersistable` 实现须遵循"先解析校验，再一次性提交"**（CORE-170-03(a)）：`IPersistable.
  Load` 契约注释已更新为要求实现方在触碰任何运行期状态之前完整校验数据形状，只有整份数据校验
  通过才提交；`SaveSystem.Load` 新增的失败段自身回滚只对遵循该约定的实现是安全的幂等 no-op，不
  遵循该约定的自定义实现在读档失败时仍可能残留部分改动的状态，建议对照框架自身 8 处修复的模式
  （见上"修复"一节）同步改造。
- **自定义 `IEventBus`/`IAuraQuery` 实现的新增成员**（CORE-170-03(b)、CORE-170-01）：
  `IEventBus.SuppressDispatch`/`IAuraQuery.TryGetInstanceRef` 均为 C#8 默认接口方法，自定义实现
  方不重写这两个成员即自动获得默认行为（分别为 no-op 抑制作用域、返回 `null`），不需要任何代码
  改动即可继续编译通过；如果自定义 `IEventBus` 实现有自己的事件派发路径且希望"读档抑制"语义生效，
  需要显式实现 `SuppressDispatch` 并让派发路径检查抑制状态。
- **版本判据说明**：本次为 MINOR（`1.7.0` → `1.8.0`）。"新增"一节列出的全部成员均为新增（新增
  类型/默认接口方法/委托/公开方法/`KnownOrder` 登记项），无删改既有公开签名；构造函数新增参数均为
  可选参数且默认值保持既有行为；`SaveSystem.Load` 失败段自身回滚、读档期间事件抑制、单位等级权威
  改为 Progression 是行为契约变更但不改变任何公开类型签名，不构成 MAJOR。

## [1.7.0] - 2026-09-08

第十一方深度审核（codex 第九轮，基线 `85f1f4f`，即本版本发布前的最新提交）10 项发现（AUD-01～05、
种族被动光环跨图、owner/day/vendor 装配扩展点、动画剪辑事件登记契约差异、工具链两条）逐条核实并
根治。逐条核实表、文档更新与边界清单处理、验收结果见
[audit-85f1f4f-20260908/followup-2026-09-08e.md](architecture/落地计划/audit-85f1f4f-20260908/followup-2026-09-08e.md)。
均属核心存档/规则/玩法层缺陷修复、引擎适配层资源合同修复、工具链健壮性加固与文档口径统一；
存档格式变更见下"迁移说明"。

### 新增

- **`Core.Foundation.SaveSystem.SaveSystem.Load` 新增按逆序回滚已成功加载段的机制**（AUD-01
  根治）：某个已注册段 `Load()` 抛异常时，对此前已成功 `Load()` 的段按逆序重新 `Load` 读档前
  快照，尽力恢复到读档前状态；最终 `LoadStatus` 仍是 `PersistableThrew`，回滚不改变这一结果；
  回滚自身失败也只记诊断，继续处理其它段。
- **6 处既有 `IPersistable` 实现补齐缺段清空覆盖面**（AUD-02 根治）：`ItemPersistable.
  InventoryPersistable`/`SkillBindingPersistable`/`VendorStockPersistable`/`DroppedLootPersistable`/
  `PlayerVitalsPersistable`/`ProgressionPersistable` 六处此前缺段（`JsonNull`）时直接返回、不清空
  既有运行期状态，现均已补齐清空逻辑；`UnitPersistable`（`CurrentMapId`/`ArchetypeId`/
  `CurrentPosition` 三段）与 `RngStreamsPersistable` 显式声明 1.6.0 新增的
  `IPersistable.KeepStateWhenSectionMissing => true` 例外并写明理由。
- **`Core.Gameplay.Economy.EconomyHost.SetStock` 新增可选参数 `timerRemaining`；新增
  `GetStockTimerRemaining`**（AUD-03 根治）：商人补货倒计时剩余时间可持久化，原地读档不再继承
  旧计时器；`world.vendor_stock` 段内 `timer` 策略物品条目形状扩展为可选对象
  `{remaining, timer_remaining}`，向后兼容纯数字旧格式。
- **`Core.Carriers.Gobj.GameObjectHost.PendingChestLootSnapshot`/`RestorePendingChestLoot`
  过时别名**（AUD-04 根治）：`[Obsolete]` 转发到 1.6.0 改名后的 `PendingLootSnapshot`/
  `RestorePendingLoot`，1.5.0 风格调用点本版本仍可编译通过（带过时警告），计划下一个 MINOR 版本
  随该窗口期结束一并移除。
- **`Core.Carriers.Unit.PlayerUnit.RaceId`/`Core.Carriers.Unit.UnitPersistable.RaceId`**（新增
  可选存档段 `player.race_id`）与 **`Core.Rules.Assembly.RulesAssembly.ReapplyRacePassiveAuras`**
  （种族被动光环跨图重放根治）：`GameplayAssembly.EnterMap` 已接入调用，此前 `World.ClearAll`
  后种族被动光环消失但种族属性修正不受影响，两者生命周期不一致的缺陷已根治。
- **`Core.Gameplay.Assembly.GameplayAssembly` 构造函数新增 `questOwnerResolver`/
  `questDayProvider`/`vendorOpenRequested` 三个可选参数**（owner/day/vendor 装配扩展点根治）：
  直接转发进内部装配的 `QuestHost`/`DialogHost`（此前恒传 `null`）；`games/_template.GameOptions`
  新增三个同名可选字段、两处引擎适配层随附的示例组合根新增三个同名可选公开属性，均已补齐透传，
  全部默认仍是 `null`，不改变未显式提供时的既有行为。
- **`Core.Foundation.EngineAdapter.IResourceLoader.ResourceKind` 新增 `AnimationClip`**（动画剪辑
  事件登记契约差异根治）：动画剪辑资源经加载器统一登记，`UnityViewFactory.RegisterModelClipEvents`
  不再整体覆盖美术自带事件，改为合并、并按 `anim_set` 的事件配置签名隔离生效范围。
- **`UnityResourceLoader.TryGetOrLoadSlotMesh`**（AUD-05 根治）：`display.equip_visual.mesh_ref`
  资源合同——预制体按槽位子对象/首个网格渲染组件提取网格，独立网格资产可直接使用；缺失/未加载时
  保留当前槽位网格不清空为不可见。
- **`toolchain/_hash.ps1`**（工具链健壮性加固）：共用哈希函数 `Get-Sha256FileHash`，
  `Get-FileHash` 可用时优先用，否则透明退化到 .NET SHA256 流式计算；`get_framework.ps1`/
  `sync_package_content.ps1`/`build.ps1` 三处哈希校验改用该函数。

### 修复

- **旧格式非空 `world.gobj_pending_loot` 读档抛异常且失败段不回滚（AUD-01，P1）**、**六处
  `IPersistable` 实现缺段不清空（AUD-02，P2）**、**商人补货计时器原地读档丢失（AUD-03，P2）**、
  **公开 API 改名无过时别名（AUD-04，P2）**、**样例 model 槽位换装把 prefab 引用当 Mesh 读取、
  槽位网格被清空（AUD-05，P2）**、**种族被动光环跨图重放丢失**、**owner/day/vendor 装配扩展点
  三处示例组合根未透传**、**动画剪辑事件登记整体覆盖美术自带事件、未按 anim_set 隔离**、
  **`toolchain` pytest 子进程按宿主默认编码解码可能崩溃**、**`get_framework.ps1` 等三处直接依赖
  `Get-FileHash` 无兜底**：10 项均为第十一方深度审核（codex 第九轮）发现，逐条根因、复现测试、
  修复位置、验收结果见上文引用的 followup 文档，不在此重复展开。
- **`toolchain/_hash.ps1` 缺少 UTF-8 BOM 导致在部分执行宿主上按系统默认代码页误读脚本源码、触发
  语法解析错误**：发布前 CI 复核发现（本机开发代码页下未复现，另一台系统默认代码页不同的宿主上
  可稳定复现），已补回 BOM（内容不变）并新增门禁测试
  `toolchain/tests/test_powershell_scripts_ansi_safe.py`，把"脚本含非 ASCII 字符必须带 BOM"
  固化为门禁校验项，见 `architecture/11_工程规范与测试.md` 第 8 节对应勘误行与
  `toolchain/README.md` 判断记录。

### 迁移说明

- **`world.gobj_pending_loot` 段读档兼容旧字段名**（AUD-01）：1.5.0 期间产生、字段名与当前版本
  不一致的旧 `pending` 记录，能按值映射的字段会被迁移，无法映射时安全丢弃单条（不影响其它条目或
  其它段），不再抛异常；`Save()` 输出格式不变，仍写 `originKey`。
- **`SaveSystem.Load` 段失败时对此前已成功加载的段按逆序尽力（best-effort）回滚**（AUD-01，行为
  契约变更）：`Load` 开始逐段读取前先对全部已注册段各取一份读档前状态快照；某段 `load()` 抛异常
  后，只对已经取得成功快照的此前成功段按逆序重新 `load(快照)`，尝试恢复到读档前状态；快照缺失、
  回滚自身再次失败、或段间存在联动时仍可能残留部分状态，不保证消除"部分加载"中间态；最终
  `LoadStatus` 枚举值不变，仍是 `PersistableThrew`。回滚不覆盖失败段自身，也不改变
  `PersistableThrew` 这一最终结果。准确合同以 `architecture/10_存档与持久化.md` 当前正文为准。
- **6 处既有 `IPersistable` 实现的缺段行为收紧**（AUD-02）：`ItemPersistable.InventoryPersistable`/
  `SkillBindingPersistable`/`VendorStockPersistable`/`DroppedLootPersistable`/
  `PlayerVitalsPersistable`/`ProgressionPersistable` 六处此前缺段时保留运行期状态不动，现改为清空
  到内容默认态；自行实现 `IPersistable` 且依赖"缺段时保留当前状态不动"这一行为的具体游戏代码，
  需要显式覆盖 1.6.0 新增的 `KeepStateWhenSectionMissing => true`（该新增接口默认方法本身不要求
  任何既有实现新增代码即可通过编译，本条只影响上述 6 处框架自身实现的默认行为）。
- **`world.vendor_stock` 段 timer 物品条目新增可选字段**（AUD-03）：`timer` 策略物品条目形状从
  纯数字变为可选对象 `{remaining, timer_remaining}`；`Load` 完全向后兼容纯数字旧格式，无需游戏侧
  改动。
- **`GameObjectHost.PendingChestLootSnapshot`/`RestorePendingChestLoot` 改名别名（AUD-04）**：本
  版本已补回旧名的 `[Obsolete]` 转发方法（行为与新名完全一致），1.5.0 风格调用点本版本仍可编译
  通过（带过时警告），计划下一个 MINOR 版本随该窗口期结束一并移除；升级到下一个 MINOR 版本前请把
  仍在用的旧名调用点改为新名。
- **`player.race_id` 新存档段**（种族被动光环跨图重放）：新增可选段，旧档缺失时清空为 `null`，
  不影响读档；调用方（游戏引导代码）需要在调用 `RulesAssembly.RegisterUnit` 传入非空 `raceId` 时
  同步写入 `PlayerUnit.RaceId`——框架不自动同步（分层边界，`RulesAssembly`/L2 不知道
  `PlayerUnit`/L3 类型存在）。
- **`display.equip_visual.mesh_ref` 资源合同首次明文**（AUD-05）：引用与 `model_ref` 同一条
  `model` 种类资源；预制体按槽位子对象/首个网格渲染组件提取，独立网格资产可直接使用；缺失/未加载
  须保留当前槽位网格，不得清空为不可见。字段数据形状（04 第 7.1.2 节字段表）不变。
- **`display.anim_set.clips[*].events` 的引擎侧登记行为契约首次明文**（动画剪辑事件登记）：必须
  与剪辑资产原有事件合并、不得整体覆盖；同一 `resource_ref` 被不同事件配置的 `display.anim_set`
  共同引用时必须按 anim_set 隔离生效范围。字段数据形状不变。
- **版本判据说明**：本次为 MINOR（`1.6.0` → `1.7.0`）。"新增"一节列出的全部成员均为新增（新增
  类型/字段/段/可选构造参数/可选公开属性/共用脚本函数），无删改既有公开签名；6 处 `IPersistable`
  实现的缺段行为收紧、AUD-01 的段失败回滚是行为契约变更但不改变任何公开类型签名，不构成 MAJOR。

## [1.6.0] - 2026-09-08

游戏侧复核 1.5.0 发现两处边界并根治：① 框架常驻壳（`FrameworkResidentHost`）命中帧同步开关此前是
私有编译期常量，是三处装配根（另两处 `GameFoundationBootstrap`/`games/_template.GameBootstrap`）
里唯一不支持"具体游戏配置开启"的一处，与包 README"命中帧同步接线步骤"一节对三处装配根一视同仁
的既有描述不符；② 命中帧同步端到端测试（`Tests/Runtime/HitFrameSyncEndToEndTests.cs`）只用一个
远大于默认超时（0.5s）的等待死线判断"VFX 是否终于入队"，未排除"命中帧链路完全断线、只是超时兜底
先一步释放，让断言恰好也通过"这一假通过可能性。均属表现层/引擎适配层缺陷修复与测试加固，无数据
表字段删改，无存档格式变更。

第十方深度审核（codex 第八轮，基线 `3224ca1`，即本版本发布前的最新提交）8 项发现（CR150-01～04、
PR150-01～03、PJ150-01）逐条核实并根治，另根治 1.5.0 遗留的一处已知局限（PR140-04 的"极短 GCD
内连续独立攻击被误合并为同一命中帧同步批次"，本版本补齐 `AttackInstanceId` 链路后消除）。逐条
核实表、上轮九项复核对照、文档处理清单见
[audit-3224ca1-20260908/followup-2026-09-08d.md](architecture/落地计划/audit-3224ca1-20260908/followup-2026-09-08d.md)。
均属核心规则/表现层/引擎适配层缺陷修复、下载脚本安全加固与文档口径统一，无数据表字段删改；
存档格式变更见下"迁移说明"。

### 新增

- **`Presentation.FeedbackBinder.Core.HitFrameSyncReleaseReason`**（枚举，`HitFrame`/`Timeout`）与
  只读诊断属性 `HitFrameSyncPolicy.LastReleaseReason`/`FeedbackBinder.LastHitFrameSyncReleaseReason`：
  命中帧同步等待队列最近一次批次释放究竟是命中帧事件真正到达，还是超时兜底，供测试与诊断直接断言
  区分两条释放路径，不再只能靠"某个副作用计数是否增加"间接判断。
- **`Core.Carriers.Gobj.GameObjectEntity.OriginKey`**（CR150-02，可空 `Id` 属性）：经
  `GameObjectFactory.Spawn` 按"地图+位置+模板"自动合成的稳定摆放位置键，跨运行期实体重建
  （地图卸载/重入分配新实体 id）保持不变；`Spawn` 新增同名可选参数，未显式传入时自动合成，既有
  调用方无需改动。
- **`Core.Foundation.SaveSystem.IPersistable.KeepStateWhenSectionMissing`**（CR150-03，默认接口
  方法，默认返回 `false`）：已注册的存档段在读取的文档里整段缺失时，`SaveSystem.Load` 默认仍会
  调用一次该段 `Load(JsonNull.Instance)`（清空既有运行期状态）；需要保留旧行为（缺段视为"不动
  当前状态"）的具体实现可显式覆盖为 `true`。纯加法，既有 `IPersistable` 实现无需改动即可编译。
- **`Core.Carriers.Gobj.GobjOptions.GatherNodeLootPolicy`**（CR150-04，`GobjLootDeliveryPolicy`
  枚举，默认 `Partial`）：独立于既有 `ChestLootPolicy` 的口味配置项，控制采集节点满包时的交付
  协议（`Reject` 整批回滚可立即重试；`Partial` 部分交付+余量记账）。
- **`Core.Rules.Common.EffectContext.AttackInstanceId`**（可空 `Id`，攻击实例 id 遗留根治）与
  `CombatDamageDealtEvent`/`CombatHealDoneEvent` 同名新字段：`CastPipeline.ExecuteEffectsOnly`
  每次调用固定分配一个全新实例 id，经 `Resolver` 转发到落地伤害/治疗事件，供 `FeedbackBinder`
  按值精确区分同一攻击者的多次独立攻击各自的命中帧同步批次，不再依赖"是否还有未释放批次"这一
  时序代理。均为新增可空字段/新增可选构造参数，默认 `null`，不改变既有调用点在缺省参数下的
  观测行为。
- **`Adapter.Unity.EngineAdapter.AnimStateFinishRelay`**（PR150-02，新增
  `StateMachineBehaviour` 子类，不属于 `IRenderer3D` 契约）：挂到具体引擎适配层动画状态机的
  目标状态上后，把动画系统同步触发的进入/退出事件转发回渲染器，作为既有"外部轮询采样"判断动画
  播放完成的并行判定路径（两者取 OR），修复自动过渡整个落在两次采样之间导致完成事件永久漏发的
  问题；未挂接时完全不影响既有行为，占位资产已随框架预置到五个内建状态。
- **`toolchain/get_framework.ps1` 下载脚本落点边界校验**（PJ150-01，安全加固）：新增锁文件
  `version` 字段格式校验（`-AllowVersionMismatch` 放行分支）与落地目录必须是 `-Target` 严格
  子目录的校验，恶意/畸形值直接 `throw` 不做任何写入/删除；`Expand-Archive` 改为逐条目手动解压
  并同样校验每个条目落点（顺带根治 zip slip）。

### 修复

- **`Adapter.Unity.Shell.FrameworkResidentHost` 命中帧同步开关改为口味配置**：私有编译期常量
  `HitFrameSyncEnabled` 改为与 `GameFoundationBootstrap` 同名同型的
  `[SerializeField] private bool _hitFrameSyncEnabled`——具体游戏可以像摆放 `GameFoundationBootstrap`
  一样在自己的场景里预先放置一个 `FrameworkResidentHost` 组件并在 Inspector 里开启该字段，
  `Ensure()` 会优先复用这个已配置好的实例（`FindFirstObjectByType` 既有查找逻辑）而不是另建一个
  默认值实例；默认仍是 `false`（`LogicDriven`），未预放置时行为与改动前完全一致。
- **命中帧同步端到端测试加固，排除超时兜底假通过**：`Tests/Runtime/HitFrameSyncEndToEndTests.cs`
  既有用例新增断言真实释放原因必须是 `HitFrame`；新增镜像反例
  `ModelAttacker_HitFrameSync_NeverFires_TimesOutWithTimeoutReason`（刻意不播放攻击动画，验证超时
  兜底确实只在命中帧从未到达时才触发、且诊断如实报告 `Timeout`）。
  `presentation/feedback_binder/tests/HitFrameSyncPolicyTests.cs`/`FeedbackBinderHitFrameSyncTests.cs`
  的等价单元测试同步补上释放原因断言，覆盖同一缺口的单元测试层面。
- **跨图重放共享光环误删（CR150-01，P2）**、**宝箱/采集节点余量按运行期实体 id 记账跨重建失联
  （CR150-02，P2）**、**旧档缺失可选段不清空当前台账（CR150-03，P2）**、**满包采集先提交冷却
  再忽略入包失败（CR150-04，P2）**、**新精灵视图创建早于绑定导致初始装备重放被过滤（PR150-01，
  P2）**、**动画完成检测依赖外部轮询采样、自动过渡落在两次采样之间会漏发完成事件（PR150-02，
  P2）**、**挂点缺失时不登记挂接意图、挂点补上后无法重放（PR150-03，P2）**、**下载脚本锁文件
  `version` 字段未经校验即拼入落地路径、可越界写删（PJ150-01，P0 安全）**：8 项均为第十方深度
  审核（codex 第八轮）发现，逐条根因、复现测试、修复位置、验收结果见上文引用的 followup 文档，
  不在此重复展开。

### 迁移说明

- **旧存档缺失可选段时默认清空该段运行期状态**（CR150-03，行为收紧）：`SaveSystem.Load` 此前对
  "已注册但当次读取的文档里整段缺失"的段直接跳过、不调用 `Load`；本版本改为默认仍调用一次
  `Load(JsonNull.Instance)`。已审计全部既有 `IPersistable` 实现（16 个），均已在 `Load` 开头
  显式处理 `JsonNull`（清空重置或无副作用 no-op），当前无一需要调整；自行实现 `IPersistable` 且
  依赖"旧档缺段时保留当前运行期状态不动"这一（未在架构文档承诺过的）旧行为的具体游戏代码，需要
  显式覆盖新增的默认接口方法 `KeepStateWhenSectionMissing => true`。该新增成员是默认接口实现，
  不要求任何既有实现新增代码即可通过编译。
- **`world.gobj_pending_loot` 段 JSON 字段名与值语义变更**（CR150-02，非公开承诺字段的一次性
  调整）：数组元素字段名 `gobjInstanceId`（运行期实体 id）改为 `originKey`（稳定摆放位置键）。
  本段是 1.5.0 新增的可选附加段，此前未在 `architecture/10_存档与持久化.md` 正式登记、未对外
  承诺过字段级兼容；产生于 1.5.0 期间、字段名与当前版本不一致的旧 `pending` 记录，读取时的实际
  处理口径（能按值映射的字段是否迁移、无法映射时如何安全丢弃、单段失败是否回滚）以
  `architecture/10_存档与持久化.md` 当前记载为准，不在本条目重复展开、也不承诺与早期草稿描述
  一致；建议对极少数处于这一窗口期、且确实存在未交付宝箱/采集节点掉落余量的存档，升级前先进入
  相关地图把余量交互清空一次，规避任何处理口径下都可能出现的"旧余量丢失"结果。
- **自定义 `IRenderer3D` 实现建议接入动画状态机的状态退出回调**（PR150-02，非强制）：本版本
  新增的 `AnimStateFinishRelay` 只是既有"外部轮询采样"判断动画完成的一个并行判定路径，不替代
  也不要求废弃采样路径；具体引擎适配层若已经能通过采样正确判断完成（未撞见"自动过渡整个落在两次
  采样之间"这一边界），无需任何改动。若自定义实现同样依赖外部轮询采样判断完成，建议参考本版本
  接入方式补一条由动画系统事件驱动的完成检测路径，避免同一类漏发问题。
- **版本判据说明**：本次为 MINOR（`1.5.0` → `1.6.0`）。"新增"一节列出的全部成员均为新增
  类型/新增可选构造参数/新增可空字段/新增默认接口方法，均不改变既有调用点在缺省参数下的观测
  行为，不删除、不改名任何已有公开签名，不破坏既有调用方编译；`world.gobj_pending_loot` 段的
  字段改名属于上述"非公开承诺字段"的例外说明，不计入 MAJOR 判据（该段本身从未进入过
  `SaveSections.KnownOrder`/架构文档正式登记）。

## [1.5.0] - 2026-09-08

第九方深度审核（codex 第七轮，基线 `c86bfa9`，即 1.4.0 自身）9 项发现（CR140-01～03、
PR140-01～04、PJ140-01～02）逐条核实并根治，另补齐审计未覆盖的两处遗留（宝箱 Partial 余量存读档
持久化、新 View 初始装备外观重放）。逐条核实表、旧 17 项复核对照、文档漂移处理见
[audit-c86bfa9-20260908/followup-2026-09-08c.md](architecture/落地计划/audit-c86bfa9-20260908/followup-2026-09-08c.md)。
均属核心规则/表现层/引擎适配层缺陷修复与能力补齐，无数据表字段删改，无存档格式不兼容变更（新增
`world.gobj_pending_loot` 段是可选附加段；缺段时的实际读取/清空语义以
`architecture/10_存档与持久化.md` 当前记载为准，不在本条目重复展开）。

### 新增

- **`Core.Carriers.Gobj.GobjLootDeliveryPolicy`**（枚举，`Reject`/`Partial`，CR140-01）：
  `GobjOptions.ChestLootPolicy`（默认 `Partial`）决定 `chest` 一次性开箱的交付协议——`Reject`
  经 `IBatchableInventoryHost` 事务整批交付，任一堆放不下即整体回滚，不标记 `open_state`；
  `Partial` 逐堆按实际落地量交付，未交付部分记入进程内台账供下次交互补发。
- **`Core.Carriers.Gobj.GameObjectHost.PendingChestLootSnapshot`/`RestorePendingChestLoot`**
  + 新增 `GobjPendingLootPersistable`（CR140-01 存读档收口）：`Partial` 策略下未交付的宝箱余量
  补齐可选存档段 `world.gobj_pending_loot`（字段名 `pending_loot`）；缺段时的实际读取/清空语义
  以 `architecture/10_存档与持久化.md` 当前记载为准；`GameplayAssembly.RegisterPersistables`
  已注册。
- **`Core.Carriers.Item.EquipmentHost.ReapplyGrants(Id unitId)`**（CR140-02）：按当前 `_equipped`
  记录重放每件装备的 `grants.auras` 并重新核实/施加套装门槛加成，幂等经 `IAuraQuery.HasAura`
  核实；`GameplayAssembly.EnterMap`（post-load 统一钩子）已接入调用。
- **`Core.Carriers.Common.GobjInteractedEvent.ResolvedTeleportTarget`**（`(Id MapId, Vec2
  Position)?`，CR140-03）：跨图传送在 `GameObjectHost.DoTeleport` 判定确实需要跨地图时，随原始
  `TeleportTargetRef` 一并携带已解析结果；`GameplayAssembly` 新增 `ApplyResolvedTeleport` 只负责
  落地，`gobj.interacted` 订阅不再反查模板独立重新解析。
- **`Adapter.Unity.EngineAdapter.UnityResourceLoader.TryLoadModelSync`**（ADR-0017 决策 1 收紧）：
  统一的模型资源缓存优先/未命中同步解析入口，`UnityRenderer3D` 不再直接调用引擎资源读取接口，
  `FinishModelLoad` 异步路径复用同一方法，渲染器只消费已加载资源、不再自行决定加载责任边界。
- **`Adapter.Unity.Presentation.UnitySpriteView` 的 `equipVisualByItemInstanceId` 构造参数**
  （PR140 文档漂移根治）：sprite 外形路线补齐装备外观入口，`UnityViewFactory` sprite 分支已接入
  同一份表；新增示例数据 `data/_sample/display/display.equip_visual.json` 一行。
- **`Presentation.Render.EquipmentSnapshotResolver`/`EquippedItemRef`（窄契约委托）+
  `EquipmentVisualSource.ReplayEquippedForUnit`**（第 0 步补齐，新 View 初始装备外观重放）：按
  单位 id 查询当前全部已装备物品（生产装配根通常包一层
  `EquipmentHost.GetAllEquippedInstances`），供跨图新 View / 存档恢复后创建的 View 在 `CreateView`
  时合成一次 `ItemEquippedEvent` 调用 `OnEvent`，不再要求先等到一次真正的装备/卸装事件才能看见
  已有装备的外观；三处生产装配根（`GameFoundationBootstrap`/`FrameworkResidentHost`/
  `games/_template.GameBootstrap`）已接线。
- **兼容层（源码兼容，`[Obsolete]`，恢复 PJ140-01）**：`Presentation.Render.ICharacterRig.
  HitFrameReached`（默认接口实现，转发到 `IHitFrameEmitter`，探测不到时静默 no-op）；
  `Presentation.Common.ViewKind.GameObject`（与 `Gobj` 数值相同的过时别名）。两者均不要求任何
  既有 `ICharacterRig` 实现/`ViewKind` 消费方改动代码即可继续编译。
- **`HitFrameSyncPolicy.WaitForHitFrame(Id, object batchToken, Action)` 重载 + `event
  Action<Id>? BatchReleased`**（PR140-04）：同一 `batchToken` 的多条等待项视为一个不可拆分批次，
  命中帧或超时都原子释放整批；`FeedbackBinder` 按攻击者维护当前批次 token。

### 修复

9 条逐条判断记录、复现测试、修复位置、验收测试见
[audit-c86bfa9-20260908/followup-2026-09-08c.md](architecture/落地计划/audit-c86bfa9-20260908/followup-2026-09-08c.md)
核实表，概要：
- **核心侧**（CR140-01～03）：宝箱一次性开箱不再在发奖前就永久标记已开、真实满包时不再吞掉/重复
  发放奖励；跨图 `World.ClearAll` 清场后按装备台账重建光环，不再出现"持久装备集合与运行期光环
  脱节"；跨图传送携带已解析结果，不再被内置默认 resolver 二次解析覆盖自定义结果。
- **表现/引擎侧**（PR140-01～04）：Blob 影子固定绕 X 轴转 90° 的旧 XZ 地面约定遗留写法改为随
  当前 XY 地面平面动态朝向相机；异步模型替换恢复 socket 子实例与投影阴影状态，不再销毁挂点子模型
  /阴影状态回退到默认值；Animator 自动过渡在两次检测帧之间完成时不再永久漏发完成事件；同一次
  攻击命中多个目标的命中帧反馈按批次原子释放，不再按 FIFO 逐条错帧/超时。
- **契约兼容性**（PJ140-01）：恢复 `ICharacterRig.HitFrameReached`/`ViewKind.GameObject` 两处
  1.4.0 内直接改名/删除造成的 1.3 消费方源码兼容性破坏。
- **交付流程**（PJ140-02）：Release 附件检测新增 lock 段 `git_commit` 一致性校验，zip 缺失但其余
  必需附件仍存在时直接阻断（不再全量重建后只上传缺失文件，消除"新 zip + 旧附件"混批且无法证明
  同源的窗口）。

### 迁移说明

- **1.3 消费方源码现可直接对 1.4.0 之后的 DLL 编译，无需改动**（PJ140-01 兼容层恢复）：见上
  "新增"一节两处 `[Obsolete]` 兼容成员；两处均计划在下一个 MAJOR 发布中随旧签名一并移除，
  过时成员按 [11_工程规范与测试.md 第 7 节](architecture/11_工程规范与测试.md) 判据至少保留一个
  MINOR 发布周期，本版本是该周期的第一个 MINOR。
- **自定义 `IRenderer3D` 实现的完成事件语义收紧**（PR140-03 收口）：非循环剪辑自然播放完成时必须
  恰好发出一次完成事件（`anim_event.finished`），即便动画状态机在两次 `Tick` 检测帧之间已经自动
  过渡离开目标状态——自实现方若只在"当前状态精确等于目标状态"时才判定完成，会漏发这一窗口内的
  完成事件，导致瞬态状态锁永久残留；`UnityRenderer3D.IsAnimatorStateFinished` 的
  `everEnteredTarget` 记账机制可作为参考实现。
- **gobj 存档新增可选段 `world.gobj_pending_loot`**：字段名 `pending_loot`，数组元素
  `{gobjInstanceId, items:[{templateId, count}]}`；只有使用 `GobjLootDeliveryPolicy.Partial`
  策略且确实产生过未交付余量时才有内容，未注册该段的既有装配根/未升级读取该段的旧存档均不受
  影响（读到 `JsonNull` 视为空表）。
- **版本判据说明**：本次为 MINOR（`1.4.0` → `1.5.0`），"新增"一节列出的全部成员均为新增
  类型/新增可选构造参数/新增枚举/恢复的过时别名或默认实现，不删除、不改名任何已有公开签名，
  不破坏既有调用方编译。

## [1.4.0] - 2026-09-08

两轮修复合并发布：① 游戏侧复核 1.3.0 发现并根治三项问题——model 型动画状态机永久卡死、瞬态状态
同状态重入不重播、框架常驻壳（`FrameworkResidentHost`）未接通 model 型外形（提交 `9f5695d`/
`360ff5f`）。② 第八方深度审核（codex 第六轮，基线 `5c444f1`）17 项发现（PR130-01～08、
PJ130-01～04、CR130-01～05）逐条核实并在当时验证范围内根治，逐条核实表、旧 13 项复核对照、
文档漂移与链接处理见
[audit-5c444f1-20260908/followup-2026-09-08b.md](architecture/落地计划/audit-5c444f1-20260908/followup-2026-09-08b.md)。
均属表现层/引擎适配层/核心规则/交付工具链缺陷修复与能力补齐，无数据表字段删改，无存档格式不兼容
变更（CR130-02 只改变运行期判定逻辑，`player.known_skills` 段 JSON 形状不变）。**收窄说明**：
第七轮外部审核（codex，基线 `c86bfa9`，即本版本自身）复核这 17 项时发现其中两项当时的根治
范围未覆盖全部子路径——CR130-01（购买/拾取事务化）未覆盖一次性宝箱直发路径（新记为
CR140-01，P1）、CR130-05（跨图传送 resolver）只修了同图/null 分支、跨图分支仍被内置 resolver
覆盖（新记为 CR140-03）；这两项不因"17 项全部根治"这句话被视为已闭合，实际状态与验收标准见
[audit-c86bfa9-20260908/AUDIT_REPORT.md](architecture/落地计划/audit-c86bfa9-20260908/AUDIT_REPORT.md)。

### 新增

- **`Presentation.Render.IHitFrameEmitter`**（可选接口，PJ130-04）：承载 `HitFrameReached: Event<Id>`
  命中帧到达事件，`SpriteCharacterRig`/`ModelCharacterRig` 均实现；`ICharacterRig` 本身不再强制要求
  该成员（迁移说明见下）。
- **`IBatchableInventoryHost` 事务覆盖购买/拾取**（CR130-01）：`EconomyHost.Buy`/`Sell`、
  `LootHost.PickUpReject` 涉及"先落地再补偿"的路径改用既有 `IBatchableInventoryHost.BeginBatch()`
  事务，与 `RewardDispatcher.GrantItems`/`QuestHost.TurnIn` 此前已用的惯例统一，失败时连已缓存的
  `item.added`/`item.removed` 事件一并回滚。
- **`SkillHost.LearnSkill` 永久来源参数与 `ForgetAllPermanentGrants`**（CR130-02）：新增
  `LearnSkill(Id unitId, Id skillId, Id sourceId, bool permanent)`；来源分类从"是否等于哨兵"改为
  逐来源记录是否永久；新增 `ForgetAllPermanentGrants(unitId, skillId)` 一次性撤销某技能全部永久
  来源，供 C09 存档替换语义正确覆盖奖励来源技能。
- **`Core.Rules.Skill.ProcHost.RescaleAll(double factor)`**（CR130-03）：混合时间模式切换时同步
  折算 Proc 的 ICD 存量，与 `CooldownTracker`/`AuraHost`/`CastPipeline` 各自的 `RescaleAll` 判断
  记录同款。
- **`Core.Carriers.Common.GobjInteractedEvent.TeleportTargetRef`**（`Id?`，CR130-05）：`on_use` 不
  可分发时 `GameObjectHost.Interact` 算出的传送目标引用随事件一并携带，`GameplayAssembly` 改为直接
  消费该引用，不再反查模板独立重新解析。
- **`Presentation.Assembly.PresentationAssembly` 的 `renderer3D` 参数完整接线**（PR130-06）：该
  构造参数早已声明为可选，三处生产装配根（`GameFoundationBootstrap`/`FrameworkResidentHost`/
  `games/_template.GameBootstrap`）此前只转发给 `UnityViewFactory`、未转发给 `PresentationAssembly`
  的接线遗漏本次补齐。
- **`Presentation.Render.EquipmentVisualSource`**（PR130-07）：新增默认的"实体 → 装备外观"来源
  实现，订阅 `item.added`/`item.equipped`/`item.unequipped` 维护"物品实例 id → 装备外观引用"活
  字典，按 `display.equip_visual.item_id` 索引；`UnityViewFactory` 新增
  `equipVisualByItemInstanceId` 构造参数，三处装配根已接线；新增示例数据
  `data/_sample/display/display.equip_visual.json`。
- **`Core.Foundation.EngineAdapter.UnityRenderer3D.Tick()`/`AnimStateMachine.StateRetriggered`
  事件/`anim_event.finished` 完成事件**（游戏侧复核收口，见上"② 概述"提交 `9f5695d`/`360ff5f`）：
  model 路线动画状态机的完成回调与同状态重入重播通道，详见下"修复"与"迁移说明"。
- **dist 纳入模型占位资源与生成器**（PJ130-02）：`build.ps1` 新增"5.055"节，把
  `adapters/unity/Assets/Resources/GameFoundation/{models,anim_clips}` 与
  `Assets/Editor/GeneratePlaceholderModelAssets.cs` 补进 dist 内适配层包副本；`check.ps1` 包清单
  一致性步骤新增对应必需路径核对。
- **私服停止身份核验**（PJ130-03）：`toolchain/registry/start_registry.ps1` 新增
  `Test-VerdaccioProcessIdentity`（可执行文件名/`CommandLine` 锚点/启动时间三项核验），`-Stop`
  两条路径（PID 文件/端口兜底）均先核验身份，不通过即拒绝停止并非零退出；`-Status` 同步显示核验
  结果。
- **文档相对链接检查测试**（`toolchain/tests/test_markdown_relative_links.py`，交付侧文档漂移
  处理附带产出）：枚举被跟踪 `*.md` 的相对链接并做文件存在性校验，随 `pytest toolchain/tests`
  一并执行。

### 修复

17 条逐条判断记录、复现测试、修复位置、验收测试见
[audit-5c444f1-20260908/followup-2026-09-08b.md](architecture/落地计划/audit-5c444f1-20260908/followup-2026-09-08b.md)
核实表，概要：
- **表现/Unity 侧**（PR130-01/03/04/05/06/07/08）：model 放置与相机投影现共用同一 2.5D 平面、
  影子不随高度抬离地面；武器/技能覆盖剪辑按需登记后再播放，不再因未预注册而无法播放；同一命中帧
  的多条反馈规则原子合批释放；model 缺资源路径落地占位并异步替换，不再直接抛异常；三处生产装配根
  一致转发 `renderer3D` 给 `PresentationAssembly`；model 换装卸载按实例反查精确清理槽位/挂点，不
  再残留挂件。
- **交付与工具链**（PJ130-01/02/03）：Release 缺附件时优先从已验证 commit 一致的旧 zip 原地补齐
  lock/tgz，不再整体重建混入新构建批次；dist 补齐模型占位资源与生成器；registry 停止前核验进程
  身份，不再误杀端口复用的无关进程。
- **契约版本语义**（PJ130-04）：`ICharacterRig.HitFrameReached` 改由可选接口 `IHitFrameEmitter`
  承载，恢复 1.3.0 MINOR 发布号与"不新增强制成员"语义一致。
- **核心规则/玩法**（CR130-01～05）：购买/拾取失败的补偿路径原子化，已派发事件一并回滚；一次性
  奖励技能按来源正确分类为永久/临时并可正确遗忘；混合时间模式折算补齐充能第二窗口、Proc ICD、
  施法学派锁三处遗漏；引导结束后的时间余量不再多结算一跳周期效果；自定义传送 resolver 结果不再
  被内置默认传送或独立重新解析覆盖。

### 迁移说明

- **`ICharacterRig.HitFrameReached` 不再是接口本身的强制成员**（PJ130-04，恢复 1.3.0 引入前的
  兼容性）：任何自定义 `ICharacterRig` 实现无需再实现该事件即可编译；需要命中帧同步的代码改为
  `(rig as IHitFrameEmitter)?.HitFrameReached`，或改持有具体类型（`SpriteCharacterRig`/
  `ModelCharacterRig` 仍直接声明该事件）。`CharacterRigHitFrameSource.RegisterRig` 对不支持
  `IHitFrameEmitter` 的 rig 仍登记成功（`HasRig` 为 true），只是不转发任何事件。
- **`IRenderer3D` 实现新增契约义务**（02 第 1.12 节勘误，游戏侧复核收口引入）：`PlayAnim` 播放的
  非循环剪辑（`loop=false`）自然播放完成时，必须经既有 `OnAnimEvent` 通道额外发出一次约定的
  "播放完成"事件（循环剪辑不发）；具体事件 id 由消费方（表现层装配代码）约定，本仓库 Unity 实现
  固定用 `anim_event.finished`（与 `Presentation.Render.ModelCharacterRig.AnimFinishedEventId`
  逐字相等）。自行实现 `IRenderer3D`（迁移到其它引擎，见 02 第 4 节迁移步骤）的具体游戏，必须在
  自己的实现里补齐这条完成事件，否则 model 型外形的 Attack/Hit/Cast 等瞬态动画状态会永久卡死，
  无法回落。`adapters/conformance/Runtime/Renderer3DScenarios.cs`/`adapters/stub/StubRenderer3D.cs`
  新增对应契约一致性场景与测试专用完成钩子（`CompleteAnimForTest`），供自实现方按同一套场景验证。
- **自定义库存实现建议实现 `IBatchableInventoryHost`**（CR130-01）：未实现该接口的库存宿主，
  `EconomyHost.Buy`/`Sell`、`LootHost.PickUpReject` 会退回历史行为（逐项补偿，不保证已派发事件的
  原子回滚）；建议按 `InventoryHost` 既有实现补齐，获得失败路径的完整原子性。
- **`SkillHost.LearnSkill` 三参调用语义**（CR130-02）：不带 `permanent` 的三参重载
  `LearnSkill(Id,Id,Id)` 现在等价于 `permanent: true`（对绝大多数既有调用方是无感知的行为收紧）；
  生产代码中唯一"曾经依赖三参重载被当作临时来源"的调用方（`Core.Carriers.Assembly.CarriersAssembly`
  装备 `SkillGranter`）已同步改为显式四参调用（`permanent: false`）；自行组装 `SkillHost` 的调用方
  若依赖三参重载表示临时来源，需要同步改为显式四参调用。
- **版本判据说明**：本次为 MINOR（`1.3.0` → `1.4.0`），新增成员（`IHitFrameEmitter`、
  `ProcHost.RescaleAll`、`GobjInteractedEvent.TeleportTargetRef`、`SkillHost` 新重载/新方法、
  `EquipmentVisualSource`、`PresentationAssembly`/`UnityViewFactory` 新构造参数）均为可选/附加成员，
  不破坏既有调用方编译。**收窄**：上一句"不破坏既有调用方编译"仅覆盖上述新增成员，不覆盖下面
  "已知源码兼容性破坏"一条列出的两处改名/删除——那两处已经过独立编译验证证实会破坏未迁移的
  1.3 消费方源码，不属于本条"新增成员均可选"的范围，不应被本条带过。
- **已知源码兼容性破坏与本版恢复的兼容层**（PJ140-01，第七轮外部审核，基线 `c86bfa9` 发现）：
  1.3.0 引入的强制成员 `ICharacterRig.HitFrameReached` 在本版本内被直接从接口移除（迁移到新增的
  可选接口 `IHitFrameEmitter`），以及 `Presentation.Common.Contracts.ViewKind` 的枚举成员
  `GameObject` 被直接改名为 `Gobj`（消除与具体引擎核心类型同名造成的技术名模糊误报，见提交
  `88a0778`）——这两处都不是"新增可选/附加成员"，而是对已发布公开签名的改名/删除，用独立的 1.3
  消费方工程针对真实 1.4.0 DLL 编译可复现失败：`ViewKind.GameObject` 报 `CS0117`、
  `ICharacterRig.HitFrameReached` 报 `CS1061`（复现日志见
  [audit-c86bfa9-20260908/AUDIT_REPORT.md PJ140-01](architecture/落地计划/audit-c86bfa9-20260908/AUDIT_REPORT.md)）。
  按 [11_工程规范与测试.md 第 7 节](architecture/11_工程规范与测试.md) 的判据，公开枚举成员改名/
  删除、接口成员删除同属不兼容变更，本应按 MAJOR 处理或至少保留过时别名/默认实现一个 MINOR
  周期，而不是在同一个 MINOR 发布内直接改名/删除且不留兼容路径。为把当前状态收回到"MINOR 内不
  破坏既有编译"的既有承诺内，本版本恢复了对应的兼容层：`ViewKind` 补回过时别名
  `GameObject`（与 `Gobj` 同值，标记为已过时，建议新代码改用 `Gobj`）；`ICharacterRig` 恢复
  `HitFrameReached` 作为带默认实现的成员（标记为已过时，建议改用 `IHitFrameEmitter`），未覆写该
  默认实现的既有实现类型无需改动即可继续编译。两处兼容层均计划在下一个 MAJOR 发布中随旧签名一并
  移除；具体实现类型与提交见后续修订版本记录。
- 占位模型资产新增 `hit` 状态与 `anim_clips/hit.anim`（`adapters/unity/Assets/Editor/GeneratePlaceholderModelAssets.cs`
  可重复运行生成）；`data/_sample/display/display.anim_set.json` 的
  `display.anim_set.placeholder_biped` 补 `hit` 剪辑声明，供"受击后继续攻击"端到端验收使用。

## [1.3.0] - 2026-09-08

W6 表现能力补齐：补齐"能力边界与未默认接入能力索引"表中长期标记"未接入"的三项表现能力——装备
外观（`model` 型）、武器动画（`auto_attack_anim`/`cast_anim_override`）、关键帧反馈
（`anim_keyframe_driven`）——的引擎无关部分（`presentation/**`）与引擎适配层真实实现
（`adapters/unity/**`），并收口三项能力共同依赖的命中帧同步链路最后一段接线缺口，使其在生产装配根
"默认可接线（开关）"。决策见 [ADR-0017](architecture/adr/0017-模型型外形默认路线补齐与命中帧同步.md)
（模型型外形默认路线补齐与命中帧同步）。另附工具链两项修正。

### 新增能力

- **装备外观（`model` 型外形）**：`ModelCharacterRig`（`presentation/render/core/ModelCharacterRig.cs`）
  从占位收口为真实实现——`ApplyEquipVisual`/`ClearSlot`/`ClearSocket` 驱动装备外观替换、
  `SyncPlacement` 落实八原语基准姿态；`Adapter.Unity.EngineAdapter.UnityRenderer3D` 提供
  `IRenderer3D` 真实实现（模型实例化/骨骼动画播放/动画事件/挂点槽位/材质参数/阴影），资源路径约定
  `Resources/GameFoundation/models/<资源引用 id 去类别前缀>`；占位模型资产
  `Assets/Resources/GameFoundation/models/placeholder_biped.*` 与可重复运行的生成脚本
  `Assets/Editor/GeneratePlaceholderModelAssets.cs` 一并提供。
- **武器动画（`auto_attack_anim`/`cast_anim_override`）**：新增 `IWeaponStyleSource`/
  `EquipmentWeaponStyleSource`（`presentation/vfx_sfx/**`，按实体查其当前装备的武器风格引用，复用
  既有 `item.template.display_ref → display.map.weapon_style_ref` 关联链路，未新增任何数据表字段）；
  `Adapter.Unity.Presentation.AnimClipResolver` 接入该来源与新增的
  `AnimStateMachine.StateChangedWithSkill` 事件，Attack 状态用 `AutoAttackAnim`、Cast 状态按触发
  技能 id 命中 `CastAnimOverride` 时用覆盖剪辑，sprite/model 两条路线共用同一份决策逻辑。
- **关键帧反馈（`anim_keyframe_driven`）**：命中帧统一为 `ICharacterRig.HitFrameReached`（sprite
  经序列帧关键帧、model 经 `IRenderer3D.OnAnimEvent` 命中固定事件 id
  `ModelCharacterRig.HitFrameEventId`）；`feedback.binding` 新增 `sync: "hit_frame"` 字段（默认对
  `combat.damage_dealt` 开启）；`presentation/feedback_binder` 新增 `IHitFrameSource`/
  `CharacterRigHitFrameSource`/`HitFrameSyncPolicy`（等待队列，0.5 秒超时兜底，逻辑结算不受影响，
  只调节呈现时机）。**本次收口**：`PresentationAssembly`/`UnityViewFactory` 均补齐接线参数（见下
  "接口变更"），三处引擎侧装配根默认可用一个口味配置项一键切换，不再需要游戏层手工绕过
  `PresentationAssembly` 自行接线。

### 接口变更

- **新增枚举值** `Core.Foundation.EngineAdapter.ResourceKind.Model`。
- **新增契约成员** `Presentation.Render.ICharacterRig.HitFrameReached`（`event Action<Id>?`，已从
  `SpriteCharacterRig` 专属成员提升进接口本身）。**迁移（破坏性，需自定义实现方补齐）**：任何自定义
  `ICharacterRig` 实现（`SpriteCharacterRig`/`ModelCharacterRig` 两个框架内置实现已补齐）必须新增
  实现本事件成员，否则无法通过编译；不打算支持命中帧同步的实现可以让该事件永不触发（等价于
  `LogicDriven` 策略下的既有行为）。
- **新增事件** `Presentation.Render.AnimStateMachine.StateChangedWithSkill`（携带触发技能 id，与既有
  `StateChanged` 三元组事件并存、`StateChanged` 签名不变）。
- **新增数据字段** `feedback.binding.sync`（可选枚举，当前仅 `"hit_frame"`；未提供时按 `event` 是否
  为 `combat.damage_dealt` 决定默认值，见 `FeedbackRule.Sync` 判断记录）。
- **新增可选构造参数**：
  - `Presentation.FeedbackBinder.Core.FeedbackBinder` 新增 `IHitFrameSource? hitFrameSource = null`；
  - `Presentation.Assembly.PresentationAssembly` 新增 `IHitFrameSource? hitFrameSource = null`（原样
    转发给内部 `FeedbackBinderCore` 同名参数——**本次收口新增**，此前该类型完全没有暴露这个参数）；
  - `Adapter.Unity.Presentation.UnityViewFactory` 新增 `IRenderer3D? renderer3D`、
    `IHitFrameSource? hitFrameSource`、`IWeaponStyleSource? weaponStyleSource`、
    `RenderOptions? renderOptions`（最后一项**本次收口新增**——此前即便别处已把
    `RenderOptions.HitFrameSync` 切到 `AnimKeyframeDriven`，`UnityViewFactory` 构造
    `UnitySpriteView`/`UnityModelView` 时仍从不传这份 `RenderOptions`，rig 构造期实际拿到的永远是
    默认 `LogicDriven`，是比"`PresentationAssembly` 未暴露 `hitFrameSource`"更深一层、本次才发现的
    接线缺口，一并收口）。
  以上均为可选参数，不传时行为与改动前完全一致，不影响既有调用方编译或运行期行为。
- **新增字段** `Presentation.Render.RenderOptions.HitFrameSync`（`HitFrameSyncStrategy`，默认
  `LogicDriven`）、`Presentation.FeedbackBinder.Contracts.FeedbackOptions.HitFrameSync`/
  `HitFrameSyncTimeoutSeconds`（默认 `LogicDriven`/0.5 秒）——两者是同一个口味配置项在渲染侧/反馈
  绑定侧的两个落点，装配层需要保持一致（见下"游戏侧接入步骤"第 6 条）。
- **诊断/测试专用新增成员（非契约）**：`Adapter.Unity.EngineAdapter.UnityRenderer2D.EmitParticleCallCount`
  （累计 `EmitParticle` 调用次数，同 `UnityAudio.PlaySfxCallCount` 一类既有诊断计数惯例）。

### 游戏侧接入步骤（新游戏若要使用以上三项能力）

1. **model 型外形**：`display.map` 填一行 `kind: "model"`，`model_ref` 指向三维模型资源引用 id，
   `anim_set_ref` 指向 `display.anim_set` 表一行（`clips[*]` 声明动画剪辑，`events[*]` 声明关键帧，
   固定名字 `"hit_frame"` 是框架约定的命中帧标记，见 `AnimSetEventsShapeRule` 校验）；`sockets`/
   `slots` 数组声明挂点/换装槽位 id。
2. **模型预制体放置约定路径**：`Resources/GameFoundation/models/<资源引用 id 去类别前缀>`（模型）、
   `Resources/GameFoundation/anim_clips/<资源引用 id 去类别前缀>`（动画剪辑）；挂点/槽位对象命名须
   与 `display.map.sockets`/`slots` 逐字一致（含域前缀）。
3. **武器风格**：`display.weapon_style` 表填一行（`auto_attack_anim`/`cast_anim_override`），经
   `item.template.display_ref → display.map.logical_id → weapon_style_ref` 关联；装配根构造一个
   `Presentation.VfxSfx.Core.EquipmentWeaponStyleSource`（需提供
   `MainHandWeaponTemplateResolver` 委托）传给 `UnityViewFactory` 的 `weaponStyleSource` 参数。
4. **主手槽位 id**：09/04 未定义全局槽位登记表，本次在三处装配根（灰盒 `GameFoundationBootstrap`
   的 `_mainHandSlotId`、模板 `GameOptions.MainHandSlotId`）各暴露一个口味配置项承载，具体游戏按
   自己的装备槽位登记表填入。
5. **命中帧同步开关**：装配根构造一个 `CharacterRigHitFrameSource`，同一个实例分别传给
   `UnityViewFactory` 的 `hitFrameSource` 参数与 `PresentationAssembly` 的 `hitFrameSource` 参数；
   构造**同一个** `RenderOptions` 实例（`HitFrameSync = AnimKeyframeDriven`）分别传给
   `UnityViewFactory` 的 `renderOptions` 参数与 `PresentationAssemblyOptions.RenderOptions`，并把
   `PresentationAssemblyOptions.FeedbackOptions.HitFrameSync` 同步切到 `AnimKeyframeDriven`——五处
   必须两两取同一实例/同一策略值，任一处遗漏或不一致都会让命中帧同步整体或部分失效（完整步骤见
   `adapters/unity` 包 README"命中帧同步接线步骤"一节）。三处框架自带装配根
   （`GameFoundationBootstrap`/`Adapter.Unity.Shell.FrameworkResidentHost`/
   `games/_template.GameBootstrap`）均已按上述步骤接线，各暴露一个布尔口味配置项
   （`_hitFrameSyncEnabled`/`GameOptions.HitFrameSyncEnabled`）一键切换，默认 `false`
   （`LogicDriven`，行为与本次收口前完全一致）。

### 工具链修正

- `toolchain/registry/start_registry.ps1`：私服停止逻辑改为以端口监听进程为准（不再依赖可能已经
  漂移的 pid 文件/进程句柄），新增 `-Status` 查询当前私服运行状态。
- `toolchain/consumer_smoke.ps1`：消费方演练在启动 Unity 前先等待同名残留进程退出，避免与新启动的
  实例互相冲突；冒烟结果落盘为 `consumer_smoke.log`。

### 迁移说明

- **自定义 `ICharacterRig` 实现**必须新增实现 `HitFrameReached` 事件成员（破坏性接口变更，详见上
  "接口变更"）；不需要命中帧同步的实现可以让该事件永不触发。
- 使用框架自带三处装配根（`GameFoundationBootstrap`/`FrameworkResidentHost`/`games/_template.
  GameBootstrap`）的游戏无需任何改动即可编译运行，命中帧同步默认关闭（`LogicDriven`），行为与
  1.2.0 完全一致；需要启用时按上面"游戏侧接入步骤"第 5 条打开对应口味配置项即可。
- 自行组装 `PresentationAssembly`/`UnityViewFactory` 的游戏（未使用框架自带装配根）：新增参数均为
  可选、默认 `null`，不传不影响现有行为，可按需选择性升级到新能力。

## [1.2.0] - 2026-09-08

第七方深度审核（codex 第五轮，基线 `1.1.0`/`5e779c6`，报告见
`architecture/落地计划/audit-5e779c6-20260907/`）13 项主发现（GP26-01～03、FR-01～05、U01～05）+
2 项验证阶段追加复现（同图读档孤儿实体、`skill.def.charges`/`cost[]` 嵌套形状校验缺口）+ WA 报告
记录的 3 条相邻缺口（施放当下按当前时间模式折算冷却/充能/光环 duration、周期累加器同步换算、
`grants.auras` 重复引用数据提醒）+ 本轮补齐的 2 条相邻缺口（`QuestHost.TurnIn` 回滚事务化、
`CastPipeline` 施放当下折算 `cast_time`/`channel_time`/`modify_cooldown` delta）全部核实并根治，
20 条核实表、判断记录、文档漂移处理逐条见
[audit-5e779c6-20260907/followup-2026-09-08.md](architecture/落地计划/audit-5e779c6-20260907/followup-2026-09-08.md)。
本条目记录变更内容与迁移说明。

### 修复（概要，逐条详见 followup 文档）

- 玩法：奖励发放失败回滚现在连已入队的 `item.added`/`item.removed` 事件一并撤销，不再被其它任务
  的 `consumeOnProgress` 目标误当新获得而错误推进进度（GP26-01）；`QuestHost.TurnIn` 步骤 2/3 失败
  的回滚同样改走事务，不再靠"移除又放回"产生虚假 `item.added`（STEP0-1）；直接交互（非 gossip）
  跨地图 teleporter 类物件现在能正确触发场景切换（GP26-03）；同图读档若命中"快照仍在倒计时、
  当下已有孤儿实体"会主动清理孤儿实体，不再与倒计时到期新生实体重复（附加-R14）。
- 规则/技能：套装门槛加成与普通装备 `grants.auras` 现在共享同一份光环来源引用计数，卸装备不再
  误删仍满足门槛的套装光环（GP26-02）；连续/离散时间模式切换现在同步换算技能冷却、光环剩余时间
  （FR-01），且施放/施加**当下**（不只是切换那一刻）就会按当前生效模式正确折算 `cooldown_duration`/
  `charges.recharge_time`/光环 `duration`/周期 `interval`/`cast_time`/`channel_time`/引导
  `tick_interval`/`modify_cooldown` 的 `delta`（WA-GAP-1/2、STEP0-2；`add_charge` 的 `amount` 是
  离散计数不受影响）；`charges.recharge_time<=0` 时充能耗尽后不再永久卡死，改为即时恢复（FR-02）；
  大步长推进不再让光环到期后的时间余量多算周期结算次数（FR-03）；读档恢复等级现在会失效重算评级
  属性缓存（FR-04）；技能 `effects[]`/`charges`/`cost[]` 的嵌套坏字段现在在数据校验阶段就会被拦下
  阻断合入，不再拖到首次施法才崩溃（FR-05 + 附加-Shape）；`item.template.grants.auras` 内重复引用
  同一光环新增数据校验 Warning 提醒（WA-GAP-3）。
- 表现：修正音乐交叉淡入/停止选错音源导致新旧音源互换（U01）；SFX 自然播放结束现在会回收对象池
  位，不再无限增长（U02）；序列帧动画大步长跨帧现在按序补发每一个跨过的关键帧，不再丢失中途命中
  特效（U03）；默认序列帧渲染器（`UnityFrameAnimPlayer` 挂载点）现在正确接入高度偏移/淡出/闪色，
  与纸娃娃层表现一致（U04，此前 WB 初判"无法复现"，WD 复核推翻，见 followup 文档 U04 行"过程"）。
- 交付：Release 工作流附件存在性检查现在核对完整五件套（zip/lock/三个 UPM tgz），部分缺失时只
  补传缺失的文件，不再因为 zip 已存在就整体跳过、遗漏其余附件（U05）。
- 文档：01/03/04/06/07/08/10/13 共 8 份架构文档按 12 §5 格式勘误（L5 查询/命令边界口径统一、
  固定步/计时器描述统一、时间字段"施放当下折算"补充说明、`grants.auras` 契约缺口清单更新等）；
  `core/rules/skill`、`core/carriers/item`、`core/gameplay/{common,quest,assembly}`、
  `core/numbers/progression`、`core/rules/expr_host`、`core/numbers/archetype/schema`、
  `presentation/vfx_sfx`、`core/foundation/sim_loop` 等模块 README 判断记录同步更新；"能力边界与
  未默认接入能力索引"补齐 `day_cycle`/ATB/孤儿检查(`DisplayMapCoverageRule`)/`FeedbackRuleValidator`
  四项，现收录两份审计报告表格给出的全部条目。

### 接口 / 事件 / 数据变更与迁移说明

- **新增事件** `sim.time_model_rescaled`（`Core.Rules.Common.TimeModelRescaledEvent{double Factor}`，
  `PublishImmediate`）：连续/离散模式切换时发出，驱动 `CooldownTracker`/`AuraHost`/`CastPipeline`
  各自的 `RescaleAll`。**迁移**：使用标准 `TimeModelSwitch`/`SkillHost` 装配（`GameplayAssembly`
  默认路径）的游戏无需任何改动，事件已自动接线。若游戏层自行实现了不经过 `TimeModelSwitch` 的
  模式切换逻辑，需要自己在切换点 `PublishImmediate` 这个事件才能让技能冷却/光环/读条正确折算。
- **新增事件** `progression.state_restored`（`Core.Numbers.Progression.ProgressionRestoredEvent
  {Id UnitId, int Level}`，`PublishImmediate`）：读档恢复等级时发出。**迁移**：标准装配无需改动，
  `RulesAssembly` 已默认订阅并转发到 `Stats.RecomputeRatingStats`；若游戏层维护了自己的等级相关
  缓存且未监听 `progression.level_up`，可能需要额外订阅这个新事件。
- **新增接口** `Core.Carriers.Common.IInventoryTransaction`（`Commit()` + `IDisposable`）、
  `IBatchableInventoryHost`（`BeginBatch(): IInventoryTransaction`）：`InventoryHost` 已实现。
  **迁移（需自定义实现方补实现）**：自定义 `IInventoryHost` 实现若不实现 `IBatchableInventoryHost`，
  `RewardDispatcher.GrantItems`/`QuestHost.TurnIn` 会自动回退到旧的"逐项精确量回滚"历史行为，
  行为不变、无需改动；若自定义实现选择实现该接口以获得"失败时事件也一并撤销"的完整保证，
  **必须支持嵌套调用**——`BeginBatch()` 在自身已处于一个未提交/未回滚的事务中时，应返回一个
  "加入外层事务"的透传句柄（其 `Commit`/`Dispose` 均为 no-op，不影响外层事务状态），而不是抛异常：
  `QuestHost.TurnIn` 会持有一个未提交的事务再调用 `RewardDispatcher.Grant`，后者若也需要发放物品
  会再次调用 `BeginBatch()`，两者必须能安全组合，见 `IInventoryTransaction.cs`
  `IBatchableInventoryHost.BeginBatch` 判断记录、`InventoryHost.BeginBatch`/`Transaction` 实现。
- **新增委托/配置** `core/gameplay/spawn/contracts/SpawnOptions.cs` 的 `GobjDespawnerDelegate`/
  `SpawnOptions.GobjDespawner`（可选）：供 `SpawnHost.Load` 在命中"同图读档孤儿实体"场景时移除
  `gobj` 域的孤儿实体（生物域经已持有的 `ICreatureFactory` 处理，无需额外配置）。**迁移**：使用
  `GameplayAssembly` 默认装配的游戏无需改动，已默认注入；自行组装 `SpawnHost` 的游戏若希望获得
  这一修复的完整效果，需要自己注入 `GobjDespawner`，否则保持旧行为（孤儿实体脱离追踪但不移除）
  并记一条诊断，不强制。
- **新增校验规则**（均已在 `RulesSchemaCatalog`/`CarriersSchemaCatalog` 的 `RegisterAll` 注册，
  `toolchain/validator` 自动继承，无需游戏层改动装配代码）：
  - `ChargesRechargeTimeZeroWarningRule`（Warning，check 名 `charges_recharge_time_zero`）
  - `ChargesShapeRule`（**Error**，check 名 `charges_max_missing`/`charges_max_invalid`/
    `charges_recharge_time_missing`/`charges_recharge_time_not_number`）
  - `CostEntryShapeRule`（**Error**，check 名 `cost_entry_not_object`/`cost_power_type_invalid`/
    `cost_amount_invalid`）
  - `ItemGrantsAurasDuplicateRule`（Warning，check 名 `item_grants_auras_duplicate`）
  - `EffectKindRegisteredRule` 行为变更（非新增）：新增 check 名 `effect_entry_not_object`/
    `effect_kind_missing`/`effect_kind_not_string`
  **迁移（需要内容作者关注）**：`ChargesShapeRule`/`CostEntryShapeRule`/`EffectKindRegisteredRule`
  的新增检查项是 **Error 级**——此前能以 0 error 通过 `DataRegistry.LoadAll()`、只在首次施法才
  崩溃的坏数据（`effects[]` 缺 `kind`、`charges`/`cost[]` 内部子字段缺失或类型错误），现在会在
  数据校验阶段直接阻断合入。已存在类似坏数据的内容仓库升级后首次跑 `validate_data.py`/
  `toolchain/validator` 会新增报错，需要修正数据（这些数据即便不修，本来也会在运行时抛异常，
  阻断合入是提前暴露问题，不是收紧了原本合法的用法）。
- **`CooldownTracker.ModifyCooldown` 行为变更**（签名不变）：`delta` 参数现按当前生效时间模式的
  `_currentFactor` 折算后再应用，与 `cooldown_duration` 同一口径。**迁移**：若游戏内容的
  `modify_cooldown` 效果原语按"与该技能 `cooldown_duration` 同一份连续秒 authoring"的惯例填写
  `delta`（本仓库默认假设，多数内容应该已经是这样），无需改动数据，离散模式下的实际效果会比
  修复前更符合直觉；若有内容特意依赖"离散模式下 `delta` 按当前轮数直接解释、不折算"的旧（有缺陷
  的）行为，需要重新核对该效果在离散战斗中的数值表现。`add_charge` 的 `amount` **不**受本次改动
  影响（离散充能计数，不是时间量）。
- **`CooldownTracker`/`AuraHost`/`CastPipeline` 新增公开方法** `RescaleAll(double factor)`：三者
  均由 `SkillHost` 构造期统一订阅 `sim.time_model_rescaled` 并转发，标准装配下不需要游戏层直接
  调用。
- **`UnityRenderer2D` 新增具体类型方法** `GetLayersRoot(SpriteHandle)`、
  `RegisterAnimRootRenderer(SpriteHandle, SpriteRenderer)`（均不进入 `IRenderer2D` 契约）；
  **`UnityFrameAnimPlayer` 新增公开属性** `SpriteRenderer`（只读，转发既有私有访问器）。
  **迁移**：仅供 `UnityViewFactory.AttachDefaultAnimation` 内部使用，游戏层通常无需直接调用；
  自定义 `IRenderer2D` 实现若也想让默认序列帧动画正确响应 height/flash/fade，可参考这一实现模式
  （把序列帧渲染器纳入与纸娃娃层同一套变换/颜色遍历）。
- **`IInventoryHost`/`IAudio`/`IFrameAnimPlayer` 等既有公开契约签名均未变化**（`UnityAudio` 新增
  的 `ActiveMusicSource`/`SfxPoolSize` 等均为 `internal` 测试专用访问器，不进入跨模块契约）。
- **无存档格式变更**：本轮全部修复均不改动任何 `IPersistable.Save()`/`Load()` 段的 JSON 结构。
- **`.github/workflows/release.yml` 行为变更**（CI 逻辑，非代码契约）：附件存在性判定改为核对
  完整五件套，游戏侧消费方无需改动，只影响本仓库自己的发布 CI 行为。

## [1.1.0] - 2026-09-07

第六方深度审核（codex 第四轮，基线 `1.0.0`/`7e63d66`，报告见
`architecture/落地计划/audit-7e63d66-20260907/`）19 条发现（C01～C12 共 12 条代码问题、P01～P07
共 7 条项目/交付问题）全部核实成立并根治，详见
`architecture/落地计划/audit-7e63d66-20260907/followup-2026-09-07d.md`。本条目记录变更内容。

### 修复（概要，逐条详见 followup 文档）

- 存档读档：旧备份候选核对 `meta.slot_id` 归属，避免跨槽误读（C01）；候选筛选核对 meta 必填字段，
  避免语义损坏文件挡住健康备份（C10）。
- 规则/技能：施法来源被销毁后周期效果缩放属性降级为 0、进战判定静默跳过而非抛异常（C02）；吸收
  耗尽的连锁移除正确传递触发深度，纳入 `MaxTriggerDepth` 收敛预算（C03）；装备 Replace 换句柄后
  另一件装备同步迁移引用，不再误清光环（C08）；技能读档改为替换语义而非只增不减（C09）。
- 玩法：Encounter/Achievement 发奖失败后保留可重试状态，不提前提交终态（C04）；`RewardDispatcher`
  按实际落地量回滚，不假设"请求量=落地量"（C05）；任务扣除物品改为先核验总量、不够不碰库存的原子
  操作（C06）；刷新点存档倒计时在同图读档时优先于当下世界状态（C11）；`reload_save` 现在也发布
  复活事件，动画状态机不再卡在死亡态（C12）。
- 表现：VFX/SFX 资源冷加载超时也会完整走完播放完成信号链，`ISfxPlayer` 新增独立时钟入口接入
  Unity 生产帧循环（C07）。
- 交付：Release 工作流在打包前补一步默认构建，干净 checkout 也能出传统路径 DLL（P01）；发行 ZIP
  与 UPM 工具链包的 validator 自包含（编译好的 DLL 或源码引用二选一），不再依赖包内不存在的源码树
  （P02）；同步脚本改清单制，只清理框架自己上次写入的文件，不再误删消费者文件（P03）；
  `get_framework.ps1` 默认严格校验请求版本与本地归档版本一致，不一致需显式 `-AllowVersionMismatch`
  （P04）；发布只推当前分支与本次新建的单个标签，不再固定推 `main` 与全部标签（P05）；私服禁止
  自注册取得发布权限，发布/删包限定到显式发布账号（P06）；sprite/audio/vfx 三类资源的同步路径与
  Unity loader 实际查找路径统一到共享映射表 `toolchain/resource_layout_map.json`（P07）。

### 接口变更与迁移说明

非破坏性增补（C# 默认接口方法，未覆盖的既有实现自动获得历史行为，无需改动）：

- `Core.Carriers.Common.IInventoryHost` 新增 `bool TryAddItem(Id unitId, Id templateId, int count, out int actualCount)`。
- `Core.Rules.Common.IAuraQuery` 新增带触发深度参数的移除重载，以及 `InstanceReplaced` 事件（默认空
  `add`/`remove`）。

破坏性增补（自定义实现方需补实现；仓库内既有实现均已补齐）：

- `Core.Gameplay.Achievement.IAchievementHost` 新增 `IReadOnlyList<Id> RetryPendingRewards(Id unitId)`
  ——仓库内唯一实现 `AchievementHost` 已补齐。
- `Presentation.VfxSfx.Contracts.ISfxPlayer` 新增 `void Update(double dt)`——仓库内 `SfxPlayer` 与
  测试用 `RecordingSfxPlayer`（`presentation/vfx_sfx/tests/AudioLayerVolumeHostTests.cs`）已补齐。

行为变更（签名不变，语义/时序变化，下游若按旧假设编写逻辑需要重新核对）：

- `Core.Gameplay.Death.RespawnPolicy.ReloadSave` 读档成功时现在也经 `IEventBus.Enqueue` 补发一次
  `Core.Rules.Common.UnitRespawnedEvent`（此前只有 `RespawnPoint` 策略发布该事件）。
- 存档段 `player.achievement_state` 每条记录新增可选字段 `pending_reward`（布尔，默认 `false`），
  向后兼容，旧存档缺省该字段按 `false` 处理。
- `Core.Rules.Skill.KnownSkillsPersistable.Load` 改为替换语义（快照未包含的永久技能会被撤销），不
  再是只增不减。
- `toolchain/get_framework.ps1` 新增 `-AllowVersionMismatch` 开关（默认关闭）；不带该开关时请求
  版本与本地归档版本不一致会直接 `throw`，不再仅 warning 后继续落地。
- `build.ps1 -Release`/`-Publish` 的 `git push` 改为推送当前所在分支 + 本次新建的单个标签，不再
  固定推送 `main` 分支与本机全部标签（`--tags`）；在 detached HEAD 下会报错拒绝执行。
- 私服 `toolchain/registry/config.yaml`：`auth.htpasswd.max_users` 由未设置（等价放开自注册）改为
  `-1`（禁止自注册）；`publish`/`unpublish` 权限从 `$authenticated`（任何已认证用户）改为限定显式
  用户名 `ws-game-publisher`。任何依赖"匿名自注册后即可发布"的私服接入脚本需要改用
  `toolchain/registry/init_publisher.ps1` 无人值守建号。

## [1.0.0] - 2026-09-07

首个正式基线版本。此前 `0.1.0`/`0.2.0` 均为落地过程中的里程碑快照（供消费方演练与打包流程自测
使用，未作为正式对外发布版本），`1.0.0` 是阶段 0～5 全部完成、经三轮内部审计与一轮外部深度审核
修复收口后的第一个"可供真实游戏接入"的稳定基线。

### 新增

- **阶段 0～5 全部完成**：环境与仓库骨架、L0 基础层（12 模块）、L1+L2 数值与规则层、L3+L4 载体
  与玩法层、Unity 适配层 + 表现层 + UI 套件、美术管线与资产规格；`Core.sln` 六个测试工程合计
  1550+ 例单测全过，Unity EditMode/PlayMode 测试全绿，独立版无人值守冒烟（连续/离散两种时间
  模型）通过，消费方演练（从零搭建独立于框架源码树的最小 Unity 工程，只以分发包为输入）通过。
- **离散时间模型**：与连续时间模型并列的第二套时间驱动方式（`TurnScheduler`、先攻策略、行动点
  移动预算、回合 HUD 等），玩家意图经 `WorldSim` 路由到调度器，回合结束统一推进计时器。
- **框架级数据目录分层**：`data/_framework/`（事件词汇登记表、输入动作声明等，随分发包交付）与
  `data/_sample/`（框架自测数据，不随分发包交付）分离；`DataRegistry` 支持多根合并加载（主键/
  schema 冲突阻断）。
- **新游戏模板** `games/_template/`：可运行的最小闭环骨架（`GameBootstrap`/`GameOptions`/
  `Editor/GameSceneBuilder`/`data/game/`/`validate.ps1`/PlayMode 冒烟测试），对照 13 号文档口味
  配置项清单逐行落地。
- **资产管线**：`toolchain/import_assets.py` 资产导入工具、`assets/_placeholder/` 通用占位资产
  包、方向档位/纸娃娃分层/序列帧图集等资产契约（14 号文档）。
- **一键门禁** `check.ps1`（22 步）与提交前钩子 `.githooks/pre-commit`（快速子集）、持续集成
  `.github/workflows/ci.yml`（非 Unity 门禁子集）。
- **版本管理方案**：语义化版本、`CHANGELOG.md`、`build.ps1 -Release`/`-DryRun`/`-Publish`、
  维护分支流程（`release/X.Y.x`）、发布工作流 `.github/workflows/release.yml`、游戏侧引用工具
  `toolchain/get_framework.ps1` 与锁文件 `ws-game.lock`。
- **私服交付通道**：与 zip 快照通道并存的第二条消费通道——私有包仓库（`toolchain/registry/`，
  Verdaccio，npm 兼容协议）+ 三个可发布包拆分（`com.gamefoundation.adapter.unity`/
  `com.gamefoundation.framework-data`/`com.gamefoundation.toolchain`）；`build.ps1 -Dist` 新增
  组装三个包 + `npm pack`，`-Release` 新增 `-PublishRegistry [-RegistryUrl]`；
  `toolchain/get_framework.ps1` 新增 `-FromRegistry`；`toolchain/sync_package_content.ps1`
  （新增）同步私服包内容到消费游戏工程；`check.ps1` 新增"包清单一致性"步骤。

### 修复

- **三轮内部文档代码一致性审计**（2026-09-05～2026-09-07）：逐轮核对 00～14 号架构文档与实现的
  一致性，修复审计发现的代码缺失（行动点、`SpellModDimension.Charges`、离散 GCD 接线、死亡复活
  三策略执行主体、种族被动光环应用、回合状态占位值等）与文档勘误，详见
  `architecture/落地计划/文档代码一致性审计_2026-09-05.md`、`_2026-09-06.md`、`_2026-09-07.md`。
- **外部深度审核 33 条发现根治**（分支 `codex/deep-review-b3b91ee-20260907`，报告见
  `architecture/落地计划/audit-b3b91ee-20260907/`）：FND-01～10、GP-01～10、RC-01～11、
  TOOL-01/02 共 33 条经三波并行核实全部成立并根治；排障过程中额外发现并修复 PlayMode 全量套件
  `VerticalSliceTests` 因跨夹具存档槽配额累积导致的隐性失败。
- **缺口收敛 G1/G2/G3**（2026-09-05）：16 条已知契约缺口中 13 条落地解决（`AnchorResolver`、
  `SpawnRequester`、`TeleportResolverDelegate`、`SaveRequesterDelegate`、回合状态显示等），3 条
  设计层判断维持"保留"（非拍板内容或本就只需单点承担的既定设计）。
- 修复：codex 第三轮深度审核 19 条（详见 audit-68c9bed-20260907/followup-2026-09-07c.md）。
- 修复：发布流程先提交后打包，lock/MANIFEST 的 `git_commit` 指向发布提交；写回覆盖
  `packages-lock.json`（`build.ps1 -Release` 首次实跑发现的时序与写回遗漏两处缺陷，根治后
  `1.0.0` 重新发布，详见根 `README.md`"版本与发布"一节）。

### 兼容性说明

- 存档格式：`save_version`（存档信封层字段，迁移链唯一依据）当前为初始版本，尚无历史存档需要
  迁移；后续存档结构不兼容变更须递增 `save_version` 并登记迁移函数（见
  `architecture/10_存档与持久化.md` 第 5 节）。
- 数据表：各表独立的 `schema_version`（见 `architecture/04_数据与内容管线.md`）随本版本一次性
  确定，表结构不兼容变更（字段删改）须递增 `schema_version` 并提供迁移路径。
- 构建产物公开契约：六个核心 DLL（`Core.Foundation`、`Core.Numbers`、`Core.Rules`、
  `Core.Carriers`、`Core.Gameplay`、`Presentation.Common`）随分发包 `dist/1.0.0/` 交付；对外公开
  的 L-1 接口签名（引擎适配层契约）、事件 key、数据表结构、存档结构变更均属 MAJOR 级变更范畴。
- 与 `0.2.0` 的差异：`1.0.0` 不改变任何公开契约或数据结构，只是把此前若干里程碑快照正式确立为
  第一个语义化版本基线，并新增本文件描述的版本管理方案本身（`build.ps1`/`check.ps1`/工作流/
  文档新增的发布相关能力）。

### 从 68c9bed 早期消费者迁移（早于 `0.1.0`/`0.2.0` 快照拉取过框架的消费方需核对）

`1.0.0` 基线包含 codex 第三轮深度审核（`audit-68c9bed-20260907/`）引入的以下破坏性/行为变更，若消费
方在提交 `68c9bed` 或更早时拉取过框架、并自行实现或依赖了下列契约，需要按下表核对：

| 契约/行为 | 变更内容 | 影响范围与迁移动作 |
|---|---|---|
| `Core.Gameplay.Common.IRewardDispatcher.Grant` | 签名由 `void Grant(...)` 改为 `bool Grant(...)`（破坏性签名变更） | 任何直接实现本接口的类型需要补返回值；调用方若忽略返回值仍可编译通过，但拿不到"是否实际发放成功"的信号，建议改为检查返回值以配合 `IQuestHost.TurnIn` 的原子化回滚（发放失败时任务不会被标记 `TurnedIn`，已消耗物品会回滚）。 |
| `Core.Rules.Common.ITargetHost` | 新增方法 `FilterExplicitTargets`（破坏性增补） | 任何直接实现本接口的类型需要补一个实现；仓库内唯一实现 `TargetHost` 已补齐。修复前显式指定的非法目标（如链式过滤要求 undead 但玩家显式指定了非 undead 目标）会被直接放行，修复后统一经该方法校验并按 `NoValidTarget` 失败码拒绝。 |
| `Presentation.FeedbackBinder.Contracts.IFeedbackSink` | 新增事件 `PendingPlaybackChanged`（破坏性增补） | 任何直接实现本接口的类型需要补一个实现（默认空 `add`/`remove` 亦可）。配套 `Presentation.VfxSfx.Contracts.IVfxPlayer`/`ISfxPlayer` 同批新增 `PendingSpawnCountChanged`/`PendingPlayCountChanged` 事件；`FeedbackBinder.TryPublishFinished` 改为"队列空 && 无 merger 待处理 && 无 sink 待处理"三者同时成立才发 `PlaybackFinishedEvent`，此前冷资源（首次加载中）的挂起播放会被误判为已完成。 |

以上三项详见 `architecture/落地计划/audit-68c9bed-20260907/followup-2026-09-07c.md`（N02/N10/N17）。

## [0.2.0]

里程碑快照（供内部打包流程与消费方演练自测使用）。收录工程收尾 K、加固波 J（契约一致性测试套件、
消费方演练脚本、PlayMode 隔离）、第三轮审计修复波（W1～W4）、第四方深度审核修复三波、离散时间
模型引擎侧接线、`found.time_model` 归属勘误、框架级数据目录与新游戏模板等一系列提交；详见
`architecture/落地计划/落地方案与分阶段计划.md`"落地进度记录"各小节与本仓库 `git log`。

## [0.1.0]

首个里程碑快照。收录阶段 0～5 全部完成、缺口收敛 G1/G2/G3、框架收官（离散时间模型初版落地、
文档/数据/门禁收尾）、收边波 I/J1 等提交；详见
`architecture/落地计划/落地方案与分阶段计划.md`"落地进度记录"各小节与本仓库 `git log`。
