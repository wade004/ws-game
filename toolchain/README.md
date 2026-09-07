# toolchain

跨游戏的工具链目录：数据校验、构建辅助、资产导入等脚本，供本框架及各游戏仓库共用。

数据校验分两道（T2-12 落地）：`validate_data.py` 自己只做第一道骨架级通用检查（信封/表名/id
格式）；第二道真实校验（字段级必填/类型/枚举、引用完整性、文本键存在、Expr 可解析，以及
skill/combat/target/ai 等各模块的专属校验规则）复用 `core/foundation/data_registry.DataRegistry`
与 `core/rules/assembly.RulesSchemaCatalog`，由 `validate_data.py` 以子进程调用
`toolchain/validator`（一个 .NET 控制台工具）完成——两套判断逻辑不重复实现，`core` 内的校验
规则是唯一权威来源。

## 控制台编码

本目录下的命令行工具脚本（`validate_data.py`/`gen_event_constants.py`/
`gen_placeholder_assets.py`/`import_assets.py` 及其 `asset_import` 包）打印的说明/错误信息
都是中文。Windows 控制台默认代码页通常不是 UTF-8——尤其是非交互式场景（CI 运行器的管道
重定向、被其他进程捕获输出等），Python 拿不到真实控制台代码页，会退化为系统 ANSI 代码页
（例如英文版 Windows/GitHub Actions `windows-latest` 运行器上是 `cp1252`），这时 `print()`
遇到中文字符会直接抛 `UnicodeEncodeError` 崩溃，而不只是打印乱码。

统一的修法是 `toolchain/_console.py` 提供的 `ensure_utf8_stdio()`：把 `sys.stdout`/
`sys.stderr` 显式 reconfigure 成 UTF-8，不依赖运行环境的默认代码页；`reconfigure` 在极少数
不支持的重定向目标上会抛 `AttributeError`/`OSError`，静默忽略即可。本目录下全部命令行入口都
在最开始调用它（`toolchain/asset_import/common.py` 的 `setup_utf8_streams()` 也已改为委托
给同一个函数，不再各自维护一份）——新增命令行入口脚本时同样要在 `main()` 最开始调用
`ensure_utf8_stdio()`（顶层脚本用 `sys.path.insert(0, str(Path(__file__).resolve().parent))`
把 `toolchain/` 目录本身放进 `sys.path` 后 `from _console import ensure_utf8_stdio`，惯例见
`toolchain/gen_event_constants.py`；`asset_import` 包内新增模块直接
`from _console import ensure_utf8_stdio`，包入口已把 `toolchain/` 放进 `sys.path`）。

`.github/workflows/ci.yml` 额外在 job 级 `env` 设置了 `PYTHONUTF8: "1"` /
`PYTHONIOENCODING: "utf-8"` 作为双保险；这两个环境变量不能替代 `ensure_utf8_stdio()`——新脚本
忘了调用它、又在这两个环境变量不生效的场合（例如本机开发者直接双击运行、或未来某个调用方
清空了环境变量）运行，仍然会在非 UTF-8 控制台上崩溃。

## 虚拟环境

```
python -m venv toolchain/.venv
```

`toolchain/.venv/` 已在仓库根 `.gitignore` 中忽略，不会被提交。数据校验（`validate_data.py`）
本身无第三方依赖；资产导入工具（`import_assets.py`）需要 Pillow，激活虚拟环境后执行：

```
pip install -r toolchain/requirements.txt
```

`requirements.txt` 只含必需依赖（当前是 Pillow）；`rembg`（`import_assets.py --matting rembg`
用到的可选抠图后端，体积大且需要本机预先准备好 `~/.u2net/*.onnx` 权重才真正可用）拆到单独的
`requirements-optional.txt`，按需再装：

```
pip install -r toolchain/requirements-optional.txt
```

`check.ps1`/CI 一键门禁只依赖 `requirements.txt`（Pillow），不需要 `requirements-optional.txt`。

## 运行校验器

从仓库根目录运行（脚本内部按自身文件路径推导仓库根，从其他目录运行结果相同；第二道校验会用
`subprocess` 以仓库根为工作目录调用 `dotnet run --project toolchain/validator`，需要能在
`PATH` 里找到 `dotnet`）：

```
python toolchain/validate_data.py
```

首次运行第二道校验时，`dotnet run` 会自动编译 `toolchain/validator`（打印 MSBuild 输出），
之后的运行会复用已编译的产物，速度明显加快。

常用参数：

- `--data-root <path>`：默认 `data`（相对仓库根），指定要校验的数据根目录。
- `--dataset <name>`：只校验 `data/<name>/` 下的表，省略则校验 `--data-root` 下全部表（这个
  子目录同时作为两道校验共同的输入范围）。
- `--verbose`：输出更详细的检查过程信息（第一道骨架检查）。
- `--strict`：第二道校验里 Warning 也阻断（透传为 `toolchain/validator --strict`，等价于
  `DataRegistryStrictness.WarningsBlock`）。
- `--skip-dotnet`：只跑第一道骨架检查，跳过第二道（没有安装 .NET SDK 的环境可用）。

## 返回码约定

- `0`：两道检查全部通过，无错误。
- `1`：至少一道检查报出错误（第一道逐行打印到标准输出，汇总
  `[第一道·骨架检查] checked N files, M errors`；第二道的每条问题打印为
  `[severity] table/key/field: check: message` 格式，汇总
  `tables N, records M, errors E, warnings W`，格式定义见 `toolchain/validator/Program.cs`）。
- `2`：命令行参数错误（如指定了不存在的 `--data-root`），或找不到 `dotnet` 可执行文件且未传
  `--skip-dotnet`。

第一道脚本只做骨架级通用检查（信封三键、表名一致、id/key 格式）；04 第 5 节校验器检查项清单
中的引用完整性、枚举合法、表达式可解析、模块专属规则（效果数上限、叠加类别冲突、命中表概率
区间、抗性曲线单调性、目标链来源已注册与无环、AI 优先级唯一性等）均由第二道
`toolchain/validator` 覆盖，见该项目下方说明；两道检查项不重叠，也不重复实现同一条判断逻辑。

## `toolchain/validator`（.NET 真实校验工具）

`toolchain/validator/Validator.csproj`（`net8.0` 控制台项目，已加入 `Core.sln`）：复用
`core/foundation/data_registry.DataRegistry`（加载 + 字段级校验）与
`core/rules/assembly.RulesSchemaCatalog.RegisterAll`（一次性注册 L0～L2 全部 `TableSchema`/
`IValidationRule`/已知外键），对磁盘上的真实数据目录跑一遍完整校验。自带
`DiskFileSystem`（`toolchain/validator/DiskFileSystem.cs`）——`adapters/stub` 的
`StubFileSystem` 只有内存实现，本工具需要读真实文件，因此在工具自己的目录下补一个只读磁盘
实现（不放进 `core/`，也不修改 `adapters/stub` 任何一行）。

直接运行（不经 `validate_data.py`）：

```
dotnet run --project toolchain/validator -- --data-root <dir> [--strict] [--json] [--list-tables]
```

参数：

- `--data-root <dir>`：必填，数据根目录；相对路径按当前工作目录解析（从仓库根运行时等价于
  "相对仓库根"），绝对路径原样使用。
- `--strict`：Warning 也阻断（`DataRegistryStrictness.WarningsBlock`）。
- `--json`：输出机器可读的单行 JSON（`{tables, records, errors, warnings, blocking, issues[]}`），
  不输出人类可读文本行。
- `--list-tables`：额外列出本次加载到的全部表名与记录数（文本模式下逐行打印，JSON 模式下并入
  `tables_list` 字段）。

问题逐条打印为 `[severity] table/key/field: check: message`（`key`/`field` 缺失时对应段落省略），
`severity` 取 `error`/`warning`，`check` 是 04 第 5 节固定的检查项名或各模块 `IValidationRule`
自行命名的检查项名；末尾汇总一行 `tables N, records M, errors E, warnings W`。返回码：`0`
未阻断；`1` 阻断（存在 Error，或 `--strict` 下存在 Warning）；`2` 命令行参数错误（缺
`--data-root`、目录不存在）。

## 生成事件常量（gen_event_constants.py）

读取事件词汇登记表 `data/_sample/found/found.event_catalog.json`，为每一行生成一个
`Core.Foundation.Common.Id` 强类型常量，写入
`core/foundation/event_bus/generated/EventKeys.g.cs`（生成物，不可手改；修改登记表后
重新运行本脚本，见该文件头部注释与 `core/foundation/event_bus/README.md`）。

```
python toolchain/gen_event_constants.py
```

常用参数：

- `--catalog <path>`：事件词汇登记表 JSON 路径，相对仓库根（默认
  `data/_sample/found/found.event_catalog.json`）。
- `--output <path>`：生成文件输出路径，相对仓库根（默认
  `core/foundation/event_bus/generated/EventKeys.g.cs`）。
- `--namespace <ns>`：生成类型所在命名空间（默认 `Core.Foundation.EventBus`）。
- `--check`：不写文件，只比较生成内容与 `--output` 现有文件是否一致；不一致返回码 1。
  **提交门槛**：改过 `found.event_catalog.json` 后应先跑
  `python toolchain/gen_event_constants.py`（覆盖生成文件）再提交；CI/预提交可用
  `python toolchain/gen_event_constants.py --check` 校验生成文件与登记表是否同步，
  不同步则失败。

返回码约定：`0` 成功；`1` 数据错误（信封非法、key 格式非法、重复 key、常量名冲突）或
`--check` 模式下内容不一致；`2` 命令行参数错误（如 `--catalog` 指向不存在的文件）。

## 资产导入工具（import_assets.py）

`toolchain/import_assets.py`（薄入口，实现在 `toolchain/asset_import/` 包）：把出图产物
（精灵集、图标、特效序列帧、音效、地图分层图）规范化落到 `assets/<dataset>/`，并把对应的
`display.map`/`vfx.def`/`sfx.def`/`world.map` 数据行合并写入 `data/<dataset>/`，对应
[11_工程规范与测试.md](../architecture/11_工程规范与测试.md) 第 2.1 节"资产导入工具"与
[落地方案与分阶段计划.md](../architecture/落地计划/落地方案与分阶段计划.md) 第 15 节。字段定义
以 [04_数据与内容管线.md](../architecture/04_数据与内容管线.md) 第 7.1 节（`display.map`）、
[09_表现层.md](../architecture/09_表现层.md) 第 3.2～3.4 节（方向量化机制、镜像字段结构、纸娃娃层、
锚点、影子）与 [14_资产规格书模板.md](../architecture/14_资产规格书模板.md) 第 2 节（方向档位的
权威命名与 `mirror_of` 关系表、第 1.2 节纸娃娃层资源 id 命名模板）为准；`vfx.def`/`sfx.def`
字段以 `presentation/vfx_sfx/schema/VfxSfxSchemas.cs` 登记的 schema 为准（`vfx.def`：
`id`/`category`/`attach_mode`/`lifetime`（可选）/`resource_ref`；`sfx.def`：
`id`/`layer`/`priority`（可选）/`variants`（可选 Id 列表）/`resource_ref`）。

```
python toolchain/import_assets.py <子命令> ...
```

六个子命令（各自 `--help` 查看完整参数）：

- `sprite`：精灵集源目录 -> 规范化精灵资源 + `display.map` 行。
  输入约定：`<src>/<direction_slot>/<layer>.png`（纸娃娃多层）或 `<src>/<direction_slot>.png`
  （单层）；`<src>` 目录名即 `sprite_set_name`；`<src>/icon.png`（可选）随精灵集一并登记图标。
  输出目录为 `assets/<dataset>/sprites/<category>_<sprite_set_name>/`（`sprite_set_id`
  即 `sprite.<category>.<sprite_set_name>` 去掉首段类别前缀 `sprite.` 后把剩余点号换成
  下划线），与运行时资源 id 解析规则（14 第 1.2 节命名模板、
  `adapters/unity/.../UnityResourceLoader.cs`/`SpriteViewBase.ResolveLayerResourceId`）对齐，
  不是只用 `sprite_set_name` 本身。
  方向档位只需要画"canonical"档位（14 第 2.1 节命名表：8 方向下的
  `front`/`front_side_r`/`side_r`/`back_side_r`/`back` 五个；4 方向为
  `front`/`side_r`/`back`；16 方向的具体命名 14 只给延伸规则，本工具按该规则自行扩展出一套
  具体命名，见 `toolchain/asset_import/directions.py`），其余方向档位（`front_side_l`/`side_l`/
  `back_side_l` 等，命名规则是把来源 canonical 档位名中最后一个独立的 `r` 分段替换为 `l`）在
  `--mirror auto`（默认）下自动用水平镜像回填并记入 `mirror_pairs`；`--mirror none` 时缺失档位
  不回填（打印警告，跳过）。**Id 前缀判断记录**（14 第 2.1 节末尾勘误、
  `presentation/common/contracts/DirectionSlots.cs` `IdPrefix`）：`mirror_pairs` 里
  `direction_slot`/`mirror_of` 两个字段是 `Id` 类型，写入 `display.map` 时会自动加上 `"dir."`
  前缀（如 `"dir.front_side_l"`），裸名字（不含点号）不满足 `Id` 格式；文件名、`--anchors` 参数、
  `anchors.json` 标注文件、本节其余提到的"档位名"一律仍是不带前缀的裸名字，两者按
  `toolchain/asset_import/common.py` 的 `to_direction_slot_id` 一一对应，只在拼进 `mirror_pairs`
  时转换一次，不影响文件系统路径。
  `--anchors` 指向的 JSON 文件格式为 `{"<direction_slot>": {"<anchor_name>": [x_px, y_px], ...}}`
  （像素坐标）；缺失档位用 `--anchor-default name=fx,fy`（画布比例，可重复，默认
  `root=0.5,1.0`）回填并记警告；`--trim` 会按裁剪掉的透明边偏移量平移锚点。写入
  `display.map` 的 `anchor_points` 只取"默认档位"（`front`，即 canonical 列表第一个）的锚点，
  按 `--pixels-per-unit`（默认 32）换算成世界单位；每个方向档位的完整锚点另存一份到精灵集自己
  的 `anchors.json`（像素坐标 + 各档位画布尺寸），供后续更细粒度的挂点系统使用。`--scale`/
  `--shadow` 直接写入 `display.map` 对应字段，不改变图像像素；图像的物理缩放不在本工具范围内
  （出图阶段自行控制分辨率）。
- `icon`：一批图标源图 -> 归一化尺寸（等比缩放 + 透明居中垫底，默认 64x64）落到
  `assets/<dataset>/icons/<category>/<name>.png`，打印 `icon.<category>.<name>` 清单（不写数据表，
  `icon_id` 由引用方（如 `sprite`/未来的 `display.equip_visual` 等）自行填写）。
- `vfx`：序列帧目录（按文件名排序的一组 `.png`）-> 图集 `atlas.png` + `frames.json` +
  `vfx.def` 行（`id`/`category`/`attach_mode`/`lifetime`（可选）/`resource_ref`）。
  `--attach-mode` 取值 `world`（按世界坐标播放）/`anchor`（挂接到 sprite 型锚点跟随）/
  `socket`（挂接到 model 型挂点跟随）/`screen`（按屏幕空间坐标播放），默认 `world`，与
  `presentation/vfx_sfx/contracts/VfxAttachMode.cs` 枚举一一对应。
  `frames.json` 结构 `{frame_w, frame_h, fps, frame_duration, loop, frames:
  [{index, x, y, w, h, duration}]}`，与运行时 `ResourceKind.Effect` 加载器
  （`UnityResourceLoader.TryDecodeEffect`）对齐，示例见
  `assets/_placeholder/vfx/burn/frames.json`；不再写旧版 `atlas.json`。`--fps` 决定
  `frame_duration`（`1/fps`）；`--loop` 写 `loop: true`（循环特效，如持续光环），且
  `--loop` 时若省略 `--lifetime` 则该行不写 `lifetime`（循环特效没有固有时长）；非循环时
  仍按 `帧数 / --fps` 推算（除非显式传 `--lifetime`）。
- `sfx`：一批 `.wav`（无压缩 PCM；用标准库 `wave` 读采样率/时长做基本校验，非 wav 或无法解析
  一律报错）作为同一 `sfx.def` id 下的随机变体，扁平复制到
  `assets/<dataset>/sfx/<name>_v<N>.wav`（不是子目录）。行固定写
  `resource_ref: "sfx.<name>_v0"`（第一个源文件）；只有传入 >= 2 个源文件时才写
  `variants`（含 `resource_ref` 本身在内的全部变体 Id 数组），单文件不写 `variants`。
  采样率/时长只用于本命令自身的 PCM 合法性校验并打印到日志，不再进数据表字段。
- `map`：地图分层图源目录（`ground.png`/`overlay.png` 必需，`decal.png`/`nav_hint.png` 可选，见
  14 第 9 节"场景与地图"）-> 规范化落到 `assets/<dataset>/maps/<map>/<layer>.png` + `world.map` 行
  （`id`/`scene_ref`/`nav_ref`/`spawn_points`）。`scene_ref`/`nav_ref` 固定按"类别前缀 + 地图名"
  写成 `scene.<map>`/`nav.<map>`，与 `adapters/unity` 侧 `UnityResourceLoader` 的 `Scene`/
  `NavMesh` 种类解析规则同一套引用 id 命名口径（见该包 README"资源 id → 路径规则"一节）；本工具
  不生成场景/导航资源本身（05 第 4.1 节"导航与碰撞...由引擎适配层侧在场景中手工绘制"）。
  `--spawn x,y[,facing]` 可重复传入覆盖默认出生点（省略时写一条 `<map>.spawn.default`，原点、
  朝向 0）；未识别的分层文件名（非 `ground`/`overlay`/`decal`/`nav_hint`）原样跳过并打印警告。
- `check`：交叉校验 `assets/<dataset>/` 与 `data/<dataset>/display|vfx|sfx|world`——
  `sprite_set_id`/`icon_id` 对应目录/文件、`vfx.def` 的 `resource_ref` 对应 `atlas.png`/
  `frames.json`、`sfx.def` 的 `resource_ref`（必查）与 `variants`（若存在，逐项查且必须包含
  `resource_ref` 本身）各自对应的扁平 `.wav` 文件是否存在、每个精灵集
  同一层跨方向档位尺寸是否一致、锚点是否落在对应方向档位画布范围内、实际落地的方向档位数是否与
  `direction_count` 一致、**`mirror_pairs` 完整性**（每个已落地但不属于 canonical 档位集合的方向
  档位必须有对应的镜像来源声明，声明的来源必须已落地）、**声明锚点缺失**（`display.map.
  anchor_points` 声明的每个锚点必须能在精灵集自己的 `anchors.json` 默认档位标注中找到，对应
  14 第 11 节"缺锚点"校验项）、`world.map` 引用的地图分层图（`map` 子命令产出）是否存在。
  `--only sprite,vfx,sfx,world`（逗号分隔，省略则四项全跑）只跑选定的检查域，供门禁在某个数据集
  只有部分域已接入真实资产时缩小本次运行覆盖范围（不放宽已选中域自身的判断逻辑）。只打印问题
  清单，不写任何文件；返回码 `0`（无问题）/`1`（有问题）。

全部子命令支持 `--dataset`（默认 `_sample`）、`--assets-root`/`--data-root`
（默认仓库 `assets/`/`data/`，可指向任意目录，测试与临时数据集用此覆盖）、`--dry-run`
（`check` 本身不写文件，无需此参数）；图像处理只用 Pillow：`--matting none|rembg|colorkey:#RRGGBB`
——`colorkey` 抠图是本工具自写的容差比色（`--colorkey-tolerance`，默认 32），`rembg` 仅在本机
`~/.u2net/` 下已有权重文件时可用（本工具不会自动联网下载模型权重，未准备好权重直接报错并提示
改用 `none`/`colorkey`）。合并写入 `display.map.json`/`vfx.def.json`/`sfx.def.json`/
`world.map.json` 时：已存在同 `id` 的行整体替换，否则新增，其余行原样保留，整份文件按 `id`
重新排序后整体写出（2 空格缩进、LF、UTF-8 无 BOM，同 `data/README.md` 约定）。

**TOOL-02 收口（第四方深度审核）：id → 物理文件名/资源引用片段的编码改为"点号替换成双下划线"，
不再是"点号替换成单下划线"**——`sfx`/`vfx` 子命令去掉 domain 前缀后，原实现直接
`.replace(".", "_")`，会让"点分段"与"本就带下划线"的不同合法 id 归一到同一个物理文件名：例如
`sfx.fire.hit`（去前缀后 `fire.hit`）与合法的 `sfx.fire_hit`（去前缀后 `fire_hit`）都会得到
`fire_hit`，第二次导入会静默覆盖第一次写出的音频/图集文件，且两条 `sfx.def`/`vfx.def` 记录
最终指向同一份物理资源。现在改为点号替换成双下划线、单个下划线原样保留（`toolchain/
asset_import/common.py` 的 `flatten_id_segment`），与表现层已在用的"结构分隔用双下划线"口径
一致（14 第 1.2 节纸娃娃扁平文件名模板 `<...>__<direction_slot>__<layer_id>`）；写入
`sfx.def`/`vfx.def` 前另加运行期兜底 `check_no_resource_collision`——不同 id 的记录若仍共用
同一条物理 `resource_ref`（含 `variants`），直接报错而不是静默覆盖（同一 id 的重复导入/更新
不算碰撞）。见 `toolchain/tests/test_import_assets.py` 新增碰撞用例。

写完 `sprite`/`vfx`/`sfx`/`map` 后应跑 `python toolchain/import_assets.py check --dataset <name>`
确认资产与数据表互相对得上，再跑 `python toolchain/validate_data.py --dataset <name>` 走完整的
表级校验（04 第 5 节"外形映射存在"等检查项）。

单元测试：`python -m unittest toolchain.tests.test_import_assets -v`（标准库 `unittest`；测试数据
写在系统临时目录，不接触仓库内 `assets/`/`data/`），也可用 `python -m pytest toolchain/tests -q`。

## `data/_sample` 的资产来源（`import_sample_assets.py`）

`data/_sample/display/display.map.json`/`vfx/vfx.def.json`/`sfx/sfx.def.json`/`world/world.map.json`
是框架仓库自身灰盒竖切/PlayMode 测试用的示例数据（见该目录顶层 `README.md`"两类目录"一节）。
这四张表引用的 `assets/_sample/` 资产由驱动脚本 `toolchain/import_sample_assets.py` 统一生成：
它把 `assets/_placeholder/` 的占位素材（外加一张 Pillow 现生成的存档点占位图）依次喂给
`import_assets.py` 的 `sprite`/`icon`/`vfx`/`sfx`/`map` 真实子命令，再把子命令产出的引用字段
（`sprite_set_id`/`icon_id`/`mirror_pairs`/`resource_ref`/`variants`/`lifetime`/`priority`）回写进
`data/_sample` 既有行——四张表每一行的 `id`/`logical_id`/`category` 全程不变。

来源 -> 产物对应关系（完整表见该脚本文件头 docstring）：

| `data/_sample` 行 | 占位素材来源 | 产物 |
| --- | --- | --- |
| `display.map.sample_hero`/`sample_beast` | `assets/_placeholder/sprites/placeholder_{hero,beast}/` 5 个 canonical 方向档位 | `assets/_sample/sprites/creature_sample_{hero,beast}/` |
| `display.map.sample_blade`/`sample_bolt`（共用精灵集） | `assets/_placeholder/icons/icon_placeholder_blade.png` | `assets/_sample/sprites/item_sample_blade/` |
| `display.map.sample_chest`/`sample_loot_pile`（共用精灵集） | `assets/_placeholder/sprites/placeholder_chest/closed.png` | `assets/_sample/sprites/gobj_sample_chest/` |
| `display.map.sample_door` | `assets/_placeholder/sprites/placeholder_door/closed.png` | `assets/_sample/sprites/gobj_sample_door/` |
| `display.map.sample_save_point` | 脚本用 Pillow 确定性生成的占位立柱图 | `assets/_sample/sprites/gobj_sample_save_point/` |
| `vfx.def.sample_cast_circle`/`sample_hit_spark`/`sample_burn` | `assets/_placeholder/vfx/{cast_circle,hit_spark,burn}/frame_*.png` | `assets/_sample/vfx/sample_*/{atlas.png,frames.json}` |
| `sfx.def.sample_hit`/`sample_cast`/`sample_ui_click` | `assets/_placeholder/sfx/*.wav` | `assets/_sample/sfx/sample_*_v<N>.wav` |
| `world.map.sample_field` | `assets/_placeholder/maps/placeholder_field/` | `assets/_sample/maps/sample_field/` |

精灵集目录名规则 `<category>_<sprite_set_name>`（如 `creature_sample_hero`）与
`sprite` 子命令本身的落地规则、运行时 `UnityResourceLoader`/`SpriteViewBase.
ResolveLayerResourceId` 的资源 id 解析规则、[14_资产规格书模板.md](../architecture/14_资产规格书模板.md)
第 1.2 节命名模板三方一致（见上面 `sprite` 子命令说明）。

`build.ps1` 第 4 节"内容数据集同步"把 `assets/_placeholder/{sprites,sfx,vfx}` 与
`assets/_sample/{sprites,sfx,vfx}` 同时同步进 Unity 工作台的
`Assets/StreamingAssets/GameFoundation/{sprites,audio,vfx}`（`Sync-ContentTree` 现支持多个
源目录，后列源目录同名文件覆盖前者）。

重新生成命令（幂等，改了 `assets/_placeholder/` 下的源素材或需要修复 `data/_sample` 引用时重跑
即可）：

```
python toolchain/import_sample_assets.py
```

脚本末尾会自动跑一次 `python toolchain/import_assets.py check --dataset _sample`（全量四域），
非 0 直接失败退出，不会把校验不过的产物留在仓库里。单元测试：
`python -m unittest toolchain.tests.test_import_sample_assets -v`（测试数据写在系统临时目录，
不接触仓库内 `assets/`/`data/`）。
