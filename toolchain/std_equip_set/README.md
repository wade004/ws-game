# 框架级占位装备集（`gen_std_equip_set.py`，手感设计/06 第 2 节、08）

程序绘制的占位装备集：**单手剑、双手巨剑、匕首、弓、法杖、胸甲**各一个完整的 [08 装备资产包](../../architecture/手感设计/08_装备与UI资产契约.md)
（图标 + 纸娃娃静态层图 + 逐层剪辑 + 外观映射 + 武器表现档案 + `feel.weapon` + 音效材质行），外加框架占位界面皮肤包 `skin.default`。
它是 `import_assets.py equip`（装备完整性校验）的参照实现：框架占位装备集必须零错误零警告，由门禁步骤 `equip_pack_check` 与
`validate_equip_data` 守住。不使用任何 AI 生图。

## 用法

```
python toolchain/gen_std_equip_set.py [--assets-out assets/_placeholder] [--data-out data/_equip]
                                      [--only std_dagger,...] [--clean] [--sheet <拼图.png>]
python toolchain/gen_std_equip_set.py --check      # 只读：跑 import_assets.py equip
```

产物入库（同 `std_dummy_poses`，确定性生成，有测试）。`--sheet` 的拼图只留本地。

## 产物布局

```
assets/_placeholder/
  icons/item/<物品名>.png                                       64x64，不含品质框，四周留透明边距
  sprites/item_<物品名>/<方向档>/<层名>.png                     静态层图（ADR-0071，逐层剪辑缺失时的回落）
  sprite_anim/item_<物品名>__<剪辑名>__<方向档>__<层名>/        逐层剪辑（ADR-0100 装备层候选，带方向一级；atlas.png + frames.json）
  ui/skin/default/…                                             占位皮肤包（清单见 asset_import/skin_pack.py 的 expected_items）
  std_equip_set.json                                            规格：每件装备的层、族、覆盖的剪辑，皮肤文件清单
data/_equip/                                                    item.* / display.* / feel.weapon（补三把）/ feel.calibration / sfx.def /
                                                                ui_layout_definition / l10n.*
```

方向档：8 方向的 5 个 canonical 档（`front`/`front_side_r`/`side_r`/`back_side_r`/`back`），左三档靠 `display.map.mirror_pairs` 镜像回填。
剪辑与帧时序复用假人姿势集（`std_dummy_poses`）：装备层与身体层同一时间线，逐帧跟着身体走。

| 物品 | 层 | 姿势族 | 逐层剪辑键数 | `feel.weapon` / 武器表现 id | 音效材质 |
|---|---|---|---|---|---|
| `item.std_sword_1h` | `hand_main` | `1h` | 18 | `sword_1h`（`data/_feel` 已有） | `metal_light` |
| `item.std_greatsword` | `hand_main` | `2h` | 17 | `greatsword`（`data/_feel` 已有） | `metal_heavy` |
| `item.std_dagger` | `hand_main` | `1h` | 18 | `dagger`（本数据根补） | `metal_light` |
| `item.std_bow` | `hand_main` | `2h` | 17 | `bow`（本数据根补） | `wood` |
| `item.std_staff` | `hand_main` | `2h` | 17 | `staff`（本数据根补） | `wood` |
| `item.std_chestplate` | `chest` | — | 33（全部键） | — | — |

## 判断记录

1. **数据根 `data/_equip` 单独成根，不进合并根与游戏根**（同 `data/_lab`）：校验时按"框架根 + `data/_feel` + `data/_equip`"三根叠加（`feel.weapon`
   行靠 `data/_feel` 解析，`feel.calibration` 由本根补一行）。数据表由 [`data.py`](data.py) 从 [`config.py`](config.py) 的物品清单推出，与资产生成器同源。
2. **姿势族取假人姿势集里已有的族名，不另起族名**：匕首取 `1h`，弓、法杖仍取 `2h`（假人姿势集后来补了 `bow`/`staff` 等族，本装备集沿用 `1h`/`2h` 不改，换族要同时改 `feel.weapon` 行与本集）。因此框架
   `data/_feel/feel/feel.weapon.json` 里原先的 `family: "sword_1h"`/`"greatsword"` 改成 `1h`/`2h`——`feel.weapon.family` 就是 04 第 2 节姿势键武器族段
   的取值，写成别的名字在假人姿势集里找不到任何键（装备完整性校验会报 `equip_family_without_pose_keys`）。
3. **一件装备要出哪些层剪辑由"姿势解析会落到哪些键"推出**：武器层 = 该族的族键 + 姿势集里没有该族变体的无族键（受击/死亡/跳跃/施法/闪避，武器仍持在手里）；
   护甲（身体跟随层）= 姿势集全部键（别名键按资源去重）。生成器从假人 `ClipDef` 独立推，校验器从 `display.anim_set` 数据行推，测试断言两边一致。
4. **全量出必备 + 推荐键**（假人姿势集补齐可选键与五个武器族后，每件的层剪辑键数随之增加——`block` 并入派生的族状态，护甲层取姿势集全部 94 份剪辑）：让"删掉一个推荐键 → 恰好 1 条警告"在零警告基线上有意义；代价是文件数与体积随姿势集增长（当前 `item_std_*` 约 2000 个文件，M3-D 新增约 2 MB）。
5. **图标不含品质框**（08 第 2 节、14 第 7 节）：与旧占位图标（`icons/icon_placeholder_*`，带品质边框）不同；品质框由皮肤包的 `quality_frame/<品质>.png` 叠加。
6. **皮肤包路径与 `skin_ref`**：`skin_ref = skin.<名>` → `assets/<数据集>/ui/skin/<名>/`；缺省占位皮肤 `skin.default`。清单只有 `skin_pack.expected_items` 一份，生成器与校验器共用，不会漂移。
7. **材质行用 id 约定 `sfx.<层>.<材质>`（层为 `swing`/`impact`），也认行内 `material` 字段**：`sfx.def` schema 目前没有材质/档位字段，不改 schema，先用 id 约定
   登记；复用 `assets/_placeholder/sfx/` 现有占位音频。
8. **装备的几何由 `skeleton.render(item_fn=…)` 摆进同一套姿势**：不另写渲染器；`skeleton.build_parts` 的第二返回值新增了主手/武器方向/躯干基等键（纯新增，既有调用方只取 `ankle_*/hand_*`）。

## 已知限制

- 占位装备是灰度块体 + 简单图标，只证明契约接线，不是美术；弓/法杖没有自己的姿势族（借 `2h`），持握姿势不贴合。
- 没有 `model` 型占位装备（槽位网格/挂点模型）：`model` 型的槽位/挂点命名校验有单元测试覆盖（`tests/test_equip_pack.py`），但框架没有占位模型资源。
- 换装场景与 `equip_cycle` 脚本、换装拼图不在本切片（06 第 3.6 节，引擎宿主侧）。
- `item.slot_definition` 没有"有无外观"字段，校验把所有装备类槽位的物品都当有外观（见 `equip_pack.py` 判断记录 5）。
