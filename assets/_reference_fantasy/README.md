# 参考界面皮肤包（通用奇幻风）

一套真实美术的样例皮肤包，用来让皮肤、图标、纸娃娃静态层这条链路被真实素材走通（[ADR-0152](../../architecture/adr/0152-参考界面皮肤包与真实素材回归.md)）。它**不是**缺省皮肤：占位皮肤仍是 `assets/_placeholder/ui/skin/default`，本目录不进内容同步、不进发布打包，缺省行为与基线不受影响。

## 内容

| 路径 | 说明 |
|---|---|
| `ui/skin/reference_fantasy/` | 皮肤包，清单（`toolchain/asset_import/skin_manifest.json`）全部元素含可选项：8 个槽位框 + 兜底 + 五态、五档品质框、拖拽三态、提示框三件、预览区背景、面板九宫格、按钮五态、`theme.json` |
| `icons/item/` | 样例装备的图标（128 x 128，主体留透明边距） |
| `sprites/item_*/<方向>/<层>.png` | 样例装备的纸娃娃静态层（144 x 144 画布，与占位层几何对齐；逐层动画剪辑仍用占位美术） |
| `_source/` | 可复现的出图与后处理：`prompts.json`（提示词与种子）、`gen_images.py`（本地出图流程的提交脚本）、`refimg.py` + `build_pack.py`（后处理）、`pack_spec.json`（规格与选定种子） |

## 复现

```
python assets/_reference_fantasy/_source/gen_images.py --raw-dir <原始大图目录>     # 需要本地出图服务；原始图不入库
python assets/_reference_fantasy/_source/build_pack.py --raw-dir <原始大图目录>     # 后处理，产物覆盖本目录
```

门禁不跑这两个脚本，只守产物：`python -m pytest toolchain/tests/test_reference_skin_pack.py`（导入校验零错误零警告、真实美术不变量与负例）和 PlayMode 用例 `ReferenceSkinPlayModeTests`。

## 校验

```
python toolchain/import_assets.py equip --assets-dir <含占位资产与本目录的合成目录> --skin-ref skin.reference_fantasy --strict-warnings
```

合成目录 = 占位资产（需要它的 `ui/skin/default` 与 `sprite_anim`）叠上本目录；测试里用目录联接搭出来，见 `test_reference_skin_pack.py::build_composite`。
