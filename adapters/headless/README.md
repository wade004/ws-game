# 无头适配层（分发形态）

本目录只在分发产物（`dist/<ver>/adapters/headless/`）中存在，源码仓库里不放编译产物——本文件是
随 `build.ps1 -Dist`/`-Release` 一起拷进 dist 的说明文档源文件（`toolchain/registry/manifests/
adapter-headless/README.md` 是同一份信息面向私服 npm 包通道的独立版本，两条通道并存，见根
`README.md`"私服通道"一节）。

## 是什么

`Adapters.Stub.dll`——引擎适配层（L-1）的桩实现，确定性、无渲染、无输入（见
`architecture/02_引擎适配层.md`）。[ADR-0018](../../architecture/adr/0018-编辑器随游戏走与框架为此提供的交付物.md)
决策第 3 条起，本程序集由"仅测试用"转正为框架正式交付物，对外称"无头适配层"：供不接真实引擎、
不需要渲染/输入的宿主（自动化测试、CI、内容编辑器的数值沙盘等）使用，与框架自身测试用的是
同一份实现，结果一致性由此保证。

## 依赖哪几个核心 DLL

`Adapters.Stub.dll` 只引用 `Core.Foundation.dll`（六个核心 DLL 之一，见 `MANIFEST.txt`
`[core_assemblies]` 段），不依赖其余五个核心 DLL，也不依赖任何具体引擎的程序集。宿主进程按需要
额外引用 `Core.Numbers.dll`/`Core.Rules.dll`/`Core.Carriers.dll`/`Core.Gameplay.dll`/
`Presentation.Common.dll`（同目录 `adapters/unity/Packages/com.gamefoundation.adapter.unity/
Runtime/Plugins/Core/`，或本 zip 快照同一路径）。

## 怎么在无头宿主里使用

```csharp
using Adapters.Stub;

var engine = new StubEngine(); // 一次性构造全部 13 个引擎适配层接口的桩实例
engine.Clock.Advance(1.0 / 60.0);
engine.FileSystem.WriteTextAtomic("save/slot1.json", "{}");
```

`StubEngine` 暴露的各接口具体类型（`StubClock`/`StubFileSystem`/…）与桩清单见
`adapters/stub/README.md`（源码仓库内该目录是本程序集的源码与完整说明，本文件只是分发产物内的
精简指引）。
