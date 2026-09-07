# Validation record

验证对象：`D:\workespace\ws-game` HEAD `5e779c600d7dd3c9ef995d34e844c48641f20a85`（VERSION `1.1.0`）。验证日期：2026-09-07。源仓库严格只读；执行副本为 `source-zip/`，由 `git archive --format=zip HEAD` 解压，归档 SHA-256 为 `CE97D5187B1298CBD843C0D50362C260155DB44EFF2DE3CDC554FFD8BD2955ED`，解压文件数 1912，与 HEAD tree 文件数一致。

## 执行环境与入口

- `Directory.Build.props`：`netstandard2.1`、C# 9、Nullable、TreatWarningsAsErrors、Deterministic。
- `Core.sln` 包含六个测试工程（Foundation、Numbers、Rules、Carriers、Gameplay、Presentation.Common），以及对应类库、Adapters.Stub、Validator，共 13 个 solution 项目。
- Python `3.13.9`，.NET SDK `8.0.424` / runtime `8.0.30`，npm `11.12.1`。
- 仓库门禁入口：`check.ps1 -SkipUnity -ArtifactsPath <isolated> -LogFile <log>`。未启动 Unity；Unity 编译、EditMode、PlayMode、独立版/冒烟、消费方演练均按 `-SkipUnity` 记录为 SKIP。

## 可运行验证

| 命令/步骤 | 结果 | 退出码 | 耗时 | 证据 |
|---|---:|---:|---:|---|
| `dotnet build Core.sln -c Release --artifacts-path dotnet-artifacts` | PASS；0 warning/0 error | 0 | 5330 ms | `dotnet-build.log` |
| `dotnet test Core.sln -c Release --no-build --artifacts-path dotnet-artifacts` | PASS；6/6，2238/2238 通过 | 0 | 6218 ms | `dotnet-test.log` |
| `check.ps1 -SkipUnity` 全量非 Unity 门禁 | PASS；14 PASS，6 SKIP | 0 | 32440 ms（汇总 31.9 s） | `check.log`, `check-harness.log` |
| 合并根 `validate_data.py` | PASS；59 tables / 276 records / 0 errors / 0 warnings / 1 override | 0 | 3.2 s | `check.log` |
| 框架根 `validate_data.py --data-root data/_framework` | PASS；5 tables / 122 records / 0 errors / 1 warning（缺 l10n 时跳过文本键检查） | 0 | 1.3 s | `check.log` |
| `gen_event_constants.py --check` | PASS；88 常量一致 | 0 | 0.1 s | `check.log` |
| `gen_placeholder_assets.py --check` | PASS；92/92 | 0 | 0.1 s | `check.log` |
| `import_assets.py check --dataset _sample` | PASS；0 问题 | 0 | 0.1 s | `check.log` |
| `python -m pytest toolchain/tests -q` | PASS；47 passed | 0 | 1.7 s | `check.log` |
| 禁用词两项、版本一致性 | PASS | 0 | 2.8 s + 0.1 s | `check.log` |
| `build.ps1 -SkipTests` DLL/数据同步 | PASS；6 DLL 与内容镜像均完成核对 | 0 | 3.1 s | `check.log` |
| 包清单一致性（3 包版本 + `npm pack --dry-run` 排除项） | PASS | 0 | 9.7 s | `check.log` |

六个测试程序集分项计数：Tests.Foundation 642、Tests.Numbers 106、Tests.Carriers 288、Tests.Rules 348、Tests.PresentationCommon 410、Tests.Gameplay 444；总计 2238。Gameplay Perf 类别包含在无 filter 的全量测试中。

## 独立机制探针（非 Unity Runtime）

`probes/unityaudio/` 编译并运行真实 HEAD 的 `UnityAudio.cs`，仅替换 `UnityEngine` 与 `UnityResourceLoader` 为最小 stub；无 Unity 编辑器、无真实音频设备。原始日志：`unityaudio-probe-restore.log`、`unityaudio-probe.log`。

- `PlayMusic(music.a, 1.0)` 后真实 B source 正在播放但 volume=0；`Tick(0.5)` 将 A 的 volume 设为 0.5，B 仍为 0。
- `StopMusic(0)` 后 A=false、B=true，当前淡入曲目未停止。
- SFX 自然结束模拟后 pool=1、`Active=true`；第二次播放 pool=2，说明 `Tick` 不按 `AudioSource.isPlaying` 回收自然结束的 slot。

上述输出是源码机制证据，不能替代 Unity PlayMode 证据。探针退出码 0（运行耗时 787 ms）；首次 `--no-restore` 缺资产文件退出码 1，随后 restore 退出码 0，属于探针初次构建准备步骤。

## 发行快照只读核验

真实源仓库已有 `dist/1.1.0/MANIFEST.txt`、`dist/ws-game-1.1.0.lock`、`dist/ws-game-1.1.0.zip`，均只读检查，未执行发布命令。

- MANIFEST `git_commit=5e779c6` 与真实 HEAD 前 7 位一致。
- lock `version=1.1.0`、`git_commit=5e779c6`；六个 DLL 的 manifest hash、lock hash 与 `dist/1.1.0/adapters/unity/Packages/com.gamefoundation.adapter.unity/Runtime/Plugins/Core/` 下实际文件全部一致。
- 适配层包共 181 文件；真实源码与 dist 文件集合一致，只有 `package.json` 字节级差异（dist 多 5 个 CRLF 结尾字节），解析后的 JSON 内容一致。证据：`dist-verification.log`。
- 真实 zip 已解压至 `zip-extract/ws-game-1.1.0/`，直接执行包内 `toolchain/validate_data.py --data-root data/_framework`：退出码 0，5 tables / 122 records / 0 errors / 1 warning。证据：`zip-framework-validate.log`。包内首次编译 Validator 有 3 个 CS8632 warning；校验本身通过。

隔离副本不含 `.git`，因此 `check.ps1` 内部 `build.ps1 -SyncOnly -Dist auto` 在生成隔离 dist 的 git 元数据时打印过 `fatal: not a git repository`，并生成 unknown commit 文本；这不影响门禁退出码，但该隔离 MANIFEST 不作为发行提交证据。真实 dist 的 MANIFEST/lock 已用真实源 HEAD 单独核对如上。

## Unity 边界与源状态

本次没有 Unity 可执行验证：Unity 编译、EditMode、PlayMode、独立版构建及两种冒烟、消费方演练均 SKIP。因而不能据此宣称 Unity 编译、渲染、输入、音频设备、场景导入、性能或发布运行时通过。

最终回查：真实源 `git rev-parse HEAD` 仍为 `5e779c600d7dd3c9ef995d34e844c48641f20a85`；`git status --porcelain=v1` 为空（0 行）。

## 追加边界验证

- `frameanim-probe.log`：编译并运行 `source-zip/presentation/render/core/FrameAnimPlayer.cs`、`FrameAnimClip.cs` 和 `IFrameAnimPlayer.cs` 的真实 HEAD 源码。10 帧、10 fps、`hit_frame=2`，`Play` 后 `Update(0.35)` 的 `CurrentFrame=3`，FrameChanged 序列为 `0,3`，动画事件为空（`hit_event_count=0`）。退出码 0，耗时 1044 ms。该实现只对推进后精确落到的帧触发关键帧，跨过 frame 2 不补发；这是引擎无关源码探针，不是 Unity Runtime。
- `upm-framework-validate.log`：从真实 `dist/1.1.0/packages/com.gamefoundation.toolchain-1.1.0.tgz` 和 `com.gamefoundation.framework-data-1.1.0.tgz` 解压到独立目录，调用包内 `Tools~/validate_data.py`，数据根为包内 `Data~/data/_framework`，未使用源树 `data/`。结果退出码 0，5 tables / 122 records / 0 errors / 1 warning，耗时 1772 ms；Validator 首次编译有 3 个 CS8632 warning。此项验证包内 UPM 工具布局和独立 validator 路径，不等于 Unity UPM 消费或 Unity 编辑器运行。
