# ADR-0038 落地：资源引用类别前缀契约/API/校验/工具链实现记录

分支：`feat/prefix-contract-impl`（基于 `design/0038-prefix-contract` @ 99276c7）
范围：仅契约/API/校验/工具链，不改数据、不改引擎适配层真实实现（ADR-0038 决策 8）

## Step 0：equip_visual.mesh_ref sprite 型语义核实

结论：**不等价**于 `sprite_set_id`/`SpriteSetDirectory` 约定。

证据：
- `SpriteSetDirectory` 推导出的是"目录"（多文件：各方向档位/各层各一个位图），消费者按
  `<dir>/<direction>/<layer>.png` 逐个加载——见 `architecture/落地计划/audit-6739f50-20260909/
  presentation/SpriteViewBase.cs` `RebuildEquippedLayers`（约 195～234 行）按 `mesh_ref` 解析出的
  纸娃娃层资源，实际消费路径是单个扁平文件，不是目录展开。
- 现有引擎适配层 `UnityResourceLoader.ResolvePath`/`IsLayerCategory`（约 890～947 行）对纸娃娃层
  类别的解析规则与 `Scene`/`NavMesh`/`Sprite` 等既有种类平级，独立处理，不经过
  `SpriteSetDirectory` 的目录展开逻辑。
- 纸娃娃层资源 id 需要携带"层级"信息（哪一层：body/head/hair/...），`sprite_set_id` 本身不携带
  层级语义（层级是 `paperdoll_layers` 数组另外登记的），二者取值形态与语义均不同。

处理：按 ADR-0038 决策 4 附带条款，新增独立类别前缀 `paperdoll`，独立推导方法
`PaperdollLayerFile`/`paperdoll_layer_file`（单个扁平文件，`paperdoll/<name>.png`）。

## 最终类别前缀集合（8 个）

| 前缀 | 路径空间 | 方法 |
|---|---|---|
| `sprite` | 资产根相对 | `SpriteSetDirectory` |
| `icon` | 资产根相对 | `IconFile` |
| `vfx` | 资产根相对 | `VfxResourceDir` |
| `sfx` | 资产根相对 | `SfxResourceFile` |
| `sprite_anim`（新） | 资产根相对 | `SpriteAnimDir`（结构同 vfx） |
| `paperdoll`（新） | 资产根相对 | `PaperdollLayerFile` |
| `anim` | 引擎侧逻辑路径 | `AnimClipLogicalPath` |
| `model` | 引擎侧逻辑路径 | `ModelLogicalPath` |

## Step 1/2：路径推导 + 路由入口

- C#：`core/foundation/engine_adapter/contracts/AssetRefConventions.cs`
  - 新增 `SpriteAnimDir`/`PaperdollLayerFile`
  - 新增 `enum AssetRefPathSpace { AssetRootRelative, EngineLogicalPath }`
  - 新增 `ResolvePathSpace(Id) -> (AssetRefPathSpace, string)`，switch 按前缀分派，unknown/无点号均
    `throw ArgumentException`（消息含收到的前缀与 `KnownCategories` 全集）
  - 新增 `KnownCategories`（8 项只读列表）
- Python：`toolchain/asset_import/ref_conventions.py` 镜像实现（`ValueError` 替代
  `ArgumentException`），`KNOWN_CATEGORIES`、`AssetRefPathSpace(enum.Enum)`、`resolve_path_space`。
- 跨语言对照测试：`core/foundation/engine_adapter/tests/AssetRefConventionsTests.cs`（52/52）、
  `toolchain/tests/test_ref_conventions.py`（53/53），同一组输入/期望字符串，互不调用。

## Step 3：字段级校验规则

- `FieldSchema.WithAllowedRefCategories(params string[])`（`core/foundation/data_registry/
  contracts/FieldSchema.cs`）：仅 `Id`/`IdList` 种类可用，设置一次，重复设置/种类不符抛异常。
- 登记：
  - `display.anim_set.clips.<clip>.resource_ref` → `{"anim", "sprite_anim"}`
  - `display.weapon_style.auto_attack_anim`/`cast_anim_override.<value>` → 同上
  - `display.equip_visual.mesh_ref` → `{"model", "paperdoll"}`
  - `vfx.def.resource_ref` → `{"vfx"}`
  - `sfx.def.resource_ref`/`variants` → `{"sfx"}`
- 新规则 `core/foundation/data_registry/core/RefCategoryFieldRule.cs`：检查名 `field_ref_category`；
  通用、表无关，递归走 `FieldSchema.Fields`/`.Item`/`.Map.ValueSchema` 找到登记了
  `AllowedRefCategories` 的字段，逐值先查类别前缀是否属于 `AssetRefConventions.KnownCategories`
  （不合法 → 报错，消息含前缀与合法集合），再查是否属于该字段自己的允许子集（不属于 → 报错，消息
  含该字段允许集合）。只检查前缀合法性，不检查文件存在性。
- 单测：`core/foundation/data_registry/tests/RefCategoryFieldRuleTests.cs`，12 个用例（命中/未命中
  ×多种字段场景，含子结构递归场景）。

### 为什么默认不注册（关键设计决策）

样例数据 `data/_sample/display/display.equip_visual.json` 中 `sample_hero_hat` 一行的
`mesh_ref` 取值仍是迁移前的 `sprite.item.sample_hero_hat_test`（遗留前缀，未迁移到本次新增的
`paperdoll` 前缀）。若把 `field_ref_category` 设为无条件注册，这一行会立刻报错，拖垮
`validate_data.py --strict` 门禁——但本任务边界明确"不改数据、不放宽规则"，且数据迁移是下一个
任务的范围。

处理：新增 `ContentValidationOptions.EnableRefCategoryCheck`（`presentation/assembly/
ContentValidationAssembly.cs`，默认 `false`），仅当显式开启时才 `RegisterValidationRule`；
`toolchain/validator/Program.cs` 新增 CLI 开关 `--enable-ref-category-check`。规则本身完整实现、
完整测试，只是不在默认路径下运行——待数据迁移完成后把默认值改为 `true`（或直接去掉开关）即可无痛
转正，不需要改规则逻辑本身。

验证（人工跑编译后的 Validator.dll）：
1. `--enable-ref-category-check` 对 `_sample` 数据跑，确认命中该行、报出 `field_ref_category`。
2. 不加该参数、加 `--strict` 对 `_sample` 数据跑，确认 exit 0（不受影响）。

## Step 4：工具链存在性检查（display_anim 域）

`toolchain/asset_import/check_cmd.py`：
- `DEFAULT_DOMAINS = ("sprite", "vfx", "sfx", "world")`，`ALL_DOMAINS = DEFAULT_DOMAINS +
  ("display_anim",)`；`_parse_only(None)` 返回 `set(DEFAULT_DOMAINS)`。
- 新增 5 个检查名：`display_anim_ref_category_invalid`、
  `display_anim_sprite_anim_atlas_missing`、`display_anim_sprite_anim_frames_json_missing`、
  `display_anim_paperdoll_file_missing`、`display_anim_asset_missing`（遗留前缀兜底）。
- 核心函数 `_check_display_anim_ref`：先 `resolve_path_space`；`ENGINE_LOGICAL_PATH` 直接跳过
  （同 ADR-0037 决策 3）；`sprite_anim` 检查 `atlas.png`+`frames.json`；`paperdoll` 检查单文件；
  其余（遗留前缀）按有无扩展名分文件/目录做最小存在性核对。
- `_check_anim_set_row`/`_check_weapon_style_row`/`_check_equip_visual_row` 分别取三张表对应字段
  调用上面的核心函数，接入 `run()`（`"display_anim" in only` 时）。

**为什么 `display_anim` 不进默认集合**：同上，`sample_hero_hat.mesh_ref` 遗留前缀数据未迁移，
纳入默认集合会让 `check` 子命令默认跑法报错。已验证：`--only display_anim` 单独跑对 `_sample`
报出恰好 1 条问题（`display_anim_asset_missing`，`sample_hero_hat`）；不加 `--only`（默认跑法）
对 `_sample` 报 0 问题、exit 0，不受影响。

## Step 5：测试与文档

- Python 新增：
  - `toolchain/tests/test_ref_conventions.py`：`sprite_anim_dir`/`paperdoll_layer_file`/
    `resolve_path_space`（含 dispatch 全表 8 项 + unknown 前缀报错 + 无点号报错）。
  - `toolchain/tests/test_import_assets.py`：新增 `CheckDisplayAnimRowUnitTest`（12 个用例，直接
    单测 `_check_anim_set_row`/`_check_weapon_style_row`/`_check_equip_visual_row`，覆盖
    engine-logical 跳过、sprite_anim 缺失两文件、sprite_anim 齐全、weapon_style 两字段、
    paperdoll 缺失/齐全、model 前缀跳过、遗留前缀兜底、未知前缀）；`DisplayAnimDomainDefaultsTest`
    （4 个用例，覆盖 `DEFAULT_DOMAINS`/`ALL_DOMAINS`/`_parse_only`/CLI `--json` 端到端）。
- C# 新增：见上 `AssetRefConventionsTests.cs`（52/52）、`RefCategoryFieldRuleTests.cs`（12/12）；
  修复既有 4 个因新增第五条可选规则而失配的既有测试（`presentation/assembly/tests/
  ContentValidationAssemblyTests.cs`：`OptionalRuleNames_IsFixedFourEntryList`、
  `Run_EnabledDisabledOptionalRuleChecks_CorrespondToRuleNameLists`、
  `Run_DefaultOptions_BothOptionalRulesEnabledByDefault`、
  `Run_WithBothOptionalRuleDependencies_BothEnabled_AndDisplayMapCoverageRuleActuallyFires`）——
  全部改为在 `DisabledOptionalRules`/`OptionalRuleNames` 断言列表末尾追加 `"RefCategoryFieldRule"`。
- 文档：
  - `architecture/14_资产规格书模板.md` 变更记录新增一行 + 第 1.2 节新增段落（类别前缀→路径空间
    对照表、`sprite_anim`/`paperdoll` 说明、路由入口说明）。
  - `toolchain/README.md`：新增 `--enable-ref-category-check` CLI 说明段；`check` 子命令 `--only`
    描述更新为含 `display_anim`；新增 `display_anim` 域整段说明；检查名清单追加 5 个新检查名。
  - `architecture/04_数据与内容管线.md`：§5 校验项清单新增"资源引用类别前缀合法"行（紧跟"装备呈现
    字段组条件必填"之后）；变更记录表新增一行（2026-09-19，ADR-0038）。
  - ADR-0037 文件头"取代说明"：勘察确认此前会话已完成（顶部状态行已有 ADR-0038 决策 7 取代说明），
    本次未重复改动。
- CHANGELOG.md：`[Unreleased]` 新增"破坏性变更"小节（按 ADR-0039 决策 2 第 2/3 类，含逐字段
  before/after 迁移说明占位、已知受影响样例数据行、数据根同步现状说明）+ "新增"小节（全部新
  方法/入口/规则/检查名）+ "未完成/后续任务"小节（数据迁移、消费方通知、两处默认集合转正、引擎
  适配层接线）。

## Step 6：验证结果

- `python -m pytest toolchain/tests -q`：**304 passed, 5 skipped**（0 failed）。
- `dotnet build Core.sln --artifacts-path scratchpad/build_prefix_contract`：**0 警告，0 错误**。
- `dotnet test Core.sln --artifacts-path scratchpad/build_prefix_contract`：
  Tests.Numbers 270、Tests.Foundation 1254、Tests.Rules 781、Tests.Carriers 615、
  Tests.PresentationCommon 657、Tests.Gameplay 863、Tests.Sim 127——**全部通过，0 失败**
  （首次跑出 4 个失败，均为既有测试断言"可选规则固定四项"因本次新增第五条可选规则而失配，已按上方
  "Step 5"所述方式修复，非放宽断言、非改动业务逻辑）。
- `./check.ps1 -SkipUnity`：见 `scratchpad/build_prefix_contract/check_prefix_contract.log`
  （运行中/结果见本文件末尾补充，或另行查看该日志文件）。

## 未完成事项（明确移交下一任务）

1. 数据迁移：`data/_sample`（及其余数据根核对）内 `anim.*`（sprite 型消费）→ `sprite_anim.*`，
   `mesh_ref` 的 `sprite.*` → `paperdoll.*`。已知受影响样例行：
   `display.equip_visual.sample_hero_hat`（`mesh_ref`）、`display.weapon_style.
   sample_model_sword` 及其挂接的 `display.anim_set` 剪辑（`auto_attack_anim`/
   `cast_anim_override`，具体清单需下一任务重新逐行核对，未穷尽勘察）。
2. 数据迁移完成后：`ContentValidationOptions.EnableRefCategoryCheck` 默认值改 `true`（或去掉
   开关直接无条件注册）；`check_cmd.py` 的 `display_anim` 域并入 `DEFAULT_DOMAINS`。
3. 按 ADR-0039 决策 3，补齐消费方通知文档。
4. 引擎适配层真实实现改为调用 `ResolvePathSpace`（ADR-0038 决策 8，本次明确不做）。
