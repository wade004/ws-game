# 通用占位资产包

本目录存放通用占位表现资产：色块角色、方块怪、简易特效、占位音效、开源可商用字体（待加入）。
目的是让任何新游戏在第一天就能跑起来一个可玩的灰盒竖切，不必等正式美术资产到位
（呼应 [ADR-0014](../../architecture/adr/0014-资产契约导入工具与UI套件是框架交付物.md)、
[11_工程规范与测试.md](../../architecture/11_工程规范与测试.md) 第 1、2.1 节）。

全部资源由 [`toolchain/gen_placeholder_assets.py`](../../toolchain/gen_placeholder_assets.py)
用 Pillow + Python 标准库确定性生成，不含任何外部下载素材（字体除外，见 `fonts/README.md`）。

- 正式游戏的正式资产放各自游戏仓库自己的 `assets/<game>/` 下，替换占位资产时不改变
  `display.map`（见 [`../../architecture/04_数据与内容管线.md`](../../architecture/04_数据与内容管线.md) 第 7 节）
  等表引用的逻辑 id，只替换底层资源文件。
- 资产的具体规格（分辨率、格式、命名、挂点约定等）以 `architecture/` 下相关文档
  （04 第 7 节、09 表现层第 3.2~3.4、5 节、14_资产规格书模板）为准，本文件只做资源清单与引用建议。

## 目录树

```
assets/_placeholder/
  README.md                        本文件
  MANIFEST.json                    本次生成的文件清单（路径/字节数/sha256/尺寸等）
  smoke/                           ComfyUI 冒烟记录（历史遗留，本工具不触碰）
  sprites/
    placeholder_hero/              8 方向纸娃娃角色（右 5 档直接绘制，左 3 档靠镜像回填）
      <direction>/body.png, hand_main.png, head.png   （仅 5 个已绘制方向含图片）
      anchors.json                 每方向 root/hand_main/head/overhead 像素锚点 + mirror_pairs
    placeholder_beast/              8 方向单层方块怪
      <direction>/body.png
      anchors.json
    placeholder_chest/              closed.png / open.png
    placeholder_door/               closed.png / open.png
  icons/                            5 种图标 × common/rare 两种品质配色
  vfx/
    hit_spark/  burn/  cast_circle/
      frame_00.png..frame_07.png, atlas.png, frames.json
  sfx/                               9 个短音效 .wav（44.1kHz/16bit/单声道，峰值 -12dBFS）
  ui/                                九宫格面板、按钮三态、血条背景/填充、背包格
  maps/placeholder_field/           ground.png / overlay.png / nav_hint.png
  fonts/README.md                   字体候选说明（本任务不下载字体文件）
```

## 建议引用 id / 资源引用名

以下为供 `display.map`（04 第 7.1 节）与 `vfx.def`/`sfx.def`（09 第 5 节）参照的建议命名；
`sprite_set_id`/`icon_id` 是自由字符串资源引用（不受 Id 域名规则约束），`vfx_id`/`sfx_id`
是 `vfx`/`sfx` 域下的内容 Id（须符合 04 第 2 节 id 规范）。

| 资源 | 类型 | 建议引用名 |
|---|---|---|
| 色块英雄纸娃娃 | `sprite_set_id` | `sprite.creature.placeholder_hero` |
| 方块怪 | `sprite_set_id` | `sprite.creature.placeholder_beast` |
| 宝箱物件 | `sprite_set_id` | `sprite.gobj.placeholder_chest` |
| 门物件 | `sprite_set_id` | `sprite.gobj.placeholder_door` |
| 武器图标 | `icon_id` | `icon.item.placeholder_blade` |
| 药水图标 | `icon_id` | `icon.item.placeholder_tonic` |
| 代币图标 | `icon_id` | `icon.item.placeholder_token` |
| 打击技能图标 | `icon_id` | `icon.skill.placeholder_strike` |
| 灼烧技能图标 | `icon_id` | `icon.skill.placeholder_burn` |
| 命中打击特效 | `vfx_id` | `vfx.placeholder_hit_spark` |
| 灼烧循环特效 | `vfx_id` | `vfx.placeholder_burn` |
| 施法法阵特效 | `vfx_id` | `vfx.placeholder_cast_circle` |
| 打击音效（两变体） | `sfx_id` | `sfx.placeholder_hit_01`、`sfx.placeholder_hit_02` |
| 挥击音效 | `sfx_id` | `sfx.placeholder_swing_01` |
| 施法音效 | `sfx_id` | `sfx.placeholder_cast_01` |
| 死亡音效 | `sfx_id` | `sfx.placeholder_death_01` |
| UI 点击音效 | `sfx_id` | `sfx.placeholder_ui_click_01` |
| UI 开启音效 | `sfx_id` | `sfx.placeholder_ui_open_01` |
| 拾取音效 | `sfx_id` | `sfx.placeholder_pickup_01` |
| 升级音效 | `sfx_id` | `sfx.placeholder_level_up_01` |
| 占位地图（草地场景） | 直接按路径引用（无内容 Id） | `maps/placeholder_field/{ground,overlay,nav_hint}.png` |
| UI 套件基础皮肤 | 直接按路径引用（无内容 Id） | `ui/{panel_9slice,button_normal,button_hover,button_pressed,bar_bg,bar_fill,slot}.png` |

## 重新生成命令

```
python toolchain/gen_placeholder_assets.py --out assets/_placeholder --seed 1
python toolchain/gen_placeholder_assets.py --out assets/_placeholder --check
```

同一 `--seed` 多次运行产出字节完全一致（确定性生成，不依赖系统时间/外部素材）。

## 许可

本目录全部图片/音效资源均由脚本程序化生成，不含任何第三方素材，视同 **CC0（公有领域等效）授权**，
可在任意项目中自由使用、修改、再分发，无需署名。`fonts/` 除外——字体候选见其自身 `README.md`，
待正式加入时另行登记其原始许可（预期 SIL OFL 1.1）。

## 当前生成状态

- 文件总数：89
- 总体积：约 0.24 MB
- 详细清单见同目录 `MANIFEST.json`（每个文件的路径、字节数、sha256、尺寸/时长等元信息）
