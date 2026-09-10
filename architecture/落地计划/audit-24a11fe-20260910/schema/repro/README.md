# Portable reproduction runner

运行环境：Windows PowerShell、.NET 8 SDK/runtime。tracked 冻结源码未改动；准备阶段早期构建
曾在冻结目录生成 ignored `bin/obj`，最终 runner 已改为先镜像源码到本输出目录再构建，避免
继续写入冻结目录。runner 日志写入本目录的 `raw/`，产品源、旧 audit 和发布目录没有写入。
归档可排除 `build/` 及 runner 生成的 `repro/obj/`；构建产物只落在本输出目录。

建议归档集合为 `schema-findings.md`、`scope-coverage.md`、`repro/` 和 `raw/`；本轮验证用的
`build/`、`runner-check*`、`runner-test*` 均为临时重建产物，归档时排除即可，保留原目录不变。

```powershell
# Use a fresh directory for each run; runners refuse to overwrite raw logs.
$out = 'D:\workespace\ws-game-artifacts\audit-24a11fe-20260910\schema\rerun-YYYYMMDD-HHMMSS'
$frozen = 'D:\workespace\ws-game-artifacts\audit-24a11fe-frozen'
New-Item -ItemType Directory -Force -Path "$out\repro" | Out-Null
Copy-Item 'repro\*.cs','repro\*.csproj','repro\run-*.ps1','repro\prepare-source.ps1' "$out\repro"
& "$out\repro\run-schema-consumer.ps1" -FrozenRoot $frozen -OutputRoot $out
& "$out\repro\run-presentation-consumer.ps1" -FrozenRoot $frozen -OutputRoot $out
& "$out\repro\run-validator.ps1" -FrozenRoot $frozen -OutputRoot $out
```

`SchemaConsumer` 是独立 .NET consumer，源码与项目文件在 `repro/`；runner 先把冻结树的源码镜像到
`OutputRoot/build/source`，再用 `SourceRoot` ProjectReference 构建，不依赖归档中的 DLL，也不写入冻结树。
它覆盖 Tokenize/parser span、整数和
大数溢出、FieldRange 非有限边界、正式 JSON `1e309/-1e309`、RecordRegistry load，以及
JsonReader/JsonWriter round trip。

`PresentationConsumer` 是独立 .NET consumer，源码与项目文件在 `repro/`；runner 用同一个输出内的
源码镜像递归构建当前 Presentation/Common 依赖。运行命令：

```powershell
& "$out\repro\run-presentation-consumer.ps1" -FrozenRoot $frozen -OutputRoot $out
```

它覆盖全注册 schema 的 SchemaAudit（空 allowlist 与冻结 allowlist）、field range export、
恶意 String Range 诊断，以及 `camera_profile.WithDomain("camera")` 配合实际
`camera_profile.default` ID 的 ReferenceDomain 运行边界。

已采集的原始文件：`raw/schema-consumer.log`、`raw/presentation-consumer.log`、
`raw/validator-schema-audit.json`、`raw/validator-sample-list.json`、rerun logs 及 build logs。

## Build provenance

* frozen HEAD: `24a11fe28f9647cd532c41f56f7ab18c00fb8516`
* version: `1.16.1`
* `build/foundation/Core.Foundation.dll` SHA-256:
  `A26AAA1BA99FE4614C52D2942EC48DEEDFB49D8295A3520DF816E6F39B8FCE7D`
* `build/presentation/Presentation.Common.dll` SHA-256:
  `15664B98D4496FEFFDFA152A83629E2E5C0262A2D42941F1395F985707537668`
* `build/validator/Validator.dll` SHA-256:
  `B9A1D043749B69ED3D1AEB501DDB113057DC500A0D821B8EB7600CCAFCD92C23`
* `build/consumer/SchemaConsumer.dll` SHA-256:
  `C2101A7D7491DCDDC4C8A05176E7E81B09F14501A1F2299BBDDC6223EDC21642`
* `build/pconsumer/PresentationConsumer.dll` SHA-256:
  `B9BF9A0F44040C39BD52AD5959ED612BCADA4A14B6F0BF971474AB02746D8B29`

归档前的 portable runner-check3 使用 `repro/` 源码从冻结树镜像重建并顺序运行三个 runner，
结果为 `ALL_RUNNERS_PASS`；其临时 DLL/log 位于 `runner-check3/`，未覆盖本目录 `raw/`。
