# fonts/ 占位字体

本目录内含实际字体文件（联网下载，用户已确认），供 UI 套件与游戏外壳在正式游戏
字体到位前作为默认字体使用。

## 字体信息

| 项目 | 内容 |
|---|---|
| 字体名（family/style） | Noto Sans CJK SC / Regular |
| 文件 | `noto_sans_cjk_sc.otf`（原始下载文件名为 `NotoSansCJKsc-Regular.otf`，已重命名为与资源 id
  `font.noto_sans_cjk_sc` 对应的裸名，见下"用途/引用名"；文件内容/sha256 未变） |
| 字重 | Regular（常规字重，本目录只含此一档） |
| 来源仓库 | [notofonts/noto-cjk](https://github.com/notofonts/noto-cjk)（Google 主导的 Noto CJK 开源字体项目），主分支 `Sans/OTF/SimplifiedChinese/NotoSansCJKsc-Regular.otf` |
| 许可 | SIL Open Font License 1.1（开源、可随游戏一同分发；分发时必须附带许可证全文，见 `LICENSE-OFL.txt`，不得单独抽出许可证文本删除） |
| 许可证文件 | `LICENSE-OFL.txt`（来源同仓库 `Sans/LICENSE`） |
| 文件大小 | noto_sans_cjk_sc.otf: 16,437,364 字节（约 15.7 MiB） |
| sha256（noto_sans_cjk_sc.otf） | `2c76254f6fc379fddfce0a7e84fb5385bb135d3e399294f6eeb6680d0365b74b` |
| sha256（LICENSE-OFL.txt） | `6a73f9541c2de74158c0e7cf6b0a58ef774f5a780bf191f2d7ec9cc53efe2bf2` |

覆盖中文常用字集（GB2312/GBK 常用字）、拉丁字符与常见标点，适合 UI 与对话文本。
已用 Pillow 加载验证（`ImageFont.truetype(...).getname()` 返回
`('Noto Sans CJK SC', 'Regular')`）并渲染中英混排+数字样例文本，非空白。

## 用途 / 引用名

作为 UI 套件与游戏外壳（shell）默认字体使用，同时也是引擎适配层"字体资源 id → 路径"约定
（见 `adapters/unity/Packages/com.gamefoundation.adapter.unity/README.md`"资源 id → 路径规则"）
的落地样例：引用名 `font.noto_sans_cjk_sc` 去掉 `font.` 前缀、点号换下划线后得到裸名
`noto_sans_cjk_sc`，对应本目录文件 `noto_sans_cjk_sc.otf`（`build.ps1 -SyncContent` 同步进
`adapters/unity/Assets/Framework/Resources/Fonts/` 后，由 `UnityResourceLoader`/
`UnityUISurface` 按同一条规则解析）。正式游戏替换字体时只需替换该裸名对应的底层文件，不改动
布局定义/引用名本身（呼应 [`../../architecture/04_数据与内容管线.md`]
(../../architecture/04_数据与内容管线.md) 第 7 节"逻辑 id 与底层资源解耦"的一贯约定）。

## 更换方式

1. 将新字体文件放入本目录（或替换现有文件），保持 `noto_sans_cjk_sc.otf`
   等裸文件名（对应引用名 `font.noto_sans_cjk_sc`）不变，仅替换其指向的底层文件内容；
   如需换用不同的引用名，裸文件名需同步改名以保持二者对应。
2. 若新字体许可证要求随分发附带许可证文本，一并放入本目录并在本文件更新许可、
   来源、sha256 等信息。
3. 如需裁剪字符子集以减小体积，裁剪后的字重与格式（ttf/otf/woff2 等）需在游戏
   接入记录中登记，并在本文件同步更新文件大小与 sha256。
4. 更新上级 [`../README.md`](../README.md) 中的资源清单说明（若涉及）。
