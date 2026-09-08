# current17-only 探针用法

`ApiCompatCurrent17Only.csproj` 独立编译 `../ApiCompatProbe.cs`，只引用 current17（1.7 系列）时代的四个 Core DLL（`Core.Foundation`/`Core.Numbers`/`Core.Rules`/`Core.Carriers`），用于验证探针代码本身在旧 API 形状下可编译，不代表当前基线。

`HintPath` 使用参数化 `$(FrameworkRoot)`（与同目录上级的 `../ApiCompatLibrary.csproj` 一致），不再写死机器绝对路径，换工作目录/换机器只需换 `-p:FrameworkRoot=...` 的值即可 restore/build，不用改 csproj 本身。

## 用法

```
dotnet build ApiCompatCurrent17Only.csproj -p:FrameworkRoot="<current17 时代四个 Core DLL 所在目录>"
```

`<current17 时代四个 Core DLL 所在目录>` 需替换为本机实际存放 current17 快照 DLL（`Core.Foundation.dll`/`Core.Numbers.dll`/`Core.Rules.dll`/`Core.Carriers.dll`）的目录——原审计留档的目录名是 `current17zip`（同批归档在 `audit-8160178-20260908/docs-project/api-compat/current17zip/`，若该子目录未随本目录一起留存，需从对应轮次的冻结仓/发布快照重新取出这四个 DLL 后再指定该路径）。未传 `FrameworkRoot` 时 restore/build 会报 `MSB3245`（找不到引用的程序集），属预期失败，不代表探针代码本身有问题。

本目录不需要实际构建即可完成本轮审计跟进；上述命令仅供后续需要时参考。
