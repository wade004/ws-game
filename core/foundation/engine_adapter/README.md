# L-1 引擎适配层 Engine Adapter · engine_adapter 契约

职责：给出 `02_引擎适配层.md` 定义的 13 个中立接口的 C# 签名——只有接口与其参数/返回值用到的
句柄、参数、枚举类型，**没有任何实现**。运行时其余各层（L0~L5）只依赖本目录里的接口类型，
不知道背后是哪种引擎实现；每种引擎各有一套完整实现（不在本目录范围内，见
`adapters/<engine_name>/`），供测试与 CI 使用的最小可用实现见 `adapters/stub/`。

依赖：仅依赖 `core/foundation/common`（`Id`、`Vec2`、`Callback`、`SubscriptionHandle`）与
.NET 标准库；不引用任何其他模块，尤其不引用 `adapters/` 下任何具体实现或 `games/` 下任何内容
（见 `01_分层与依赖.md` 第 3 节依赖矩阵：L-1 不得依赖 L0 以上任何东西，本目录属于 L-1，
仅向下依赖 `common` 这一份最基础的原语类型，不构成对 L0 其余模块的依赖）。

不负责什么：

- 不提供任何接口的实现（无论真实引擎还是桩），实现分别属于 `adapters/<engine_name>/` 与
  `adapters/stub/`。
- 不出现任何具体引擎、语言、框架的符号——**接口签名以 `02_引擎适配层.md` 为准，本目录不得
  出现任何具体引擎符号**。
- 不定义任何游戏规则或数值（技能、战斗、属性等），那些是 L1~L4 的职责。
- 不做数据引用完整性校验，那是 `DataRegistry`（`core/foundation/data_registry`，本阶段未创建）
  的职责。

## 目录

```
engine_adapter/
  README.md
  contracts/   IWindow.cs IClock.cs IRenderer2D.cs IAudio.cs IInput.cs IFileSystem.cs
               IResourceLoader.cs INavigation2D.cs ISpatialQuery.cs IUISurface.cs
               IPlatform.cs IRenderer3D.cs ICamera.cs
  tests/       StubClockTests.cs StubFileSystemTests.cs StubSpatialQueryTests.cs
               （对 adapters/stub 桩实现的验证；本模块自身不含可独立测试的逻辑，
               契约文件只有类型定义，没有行为，因此把"契约是否可用"的测试放在
               对桩实现的验证上，见 11_工程规范与测试.md 第 6 节测试分层）
```

## 13 个接口与文档章节对应表

| 接口 | 契约文件 | 02 文档章节 | 可选性 |
|---|---|---|---|
| `IWindow` | `contracts/IWindow.cs` | 第 1.1 节 | 必需 |
| `IClock` | `contracts/IClock.cs` | 第 1.2 节 | 必需 |
| `IRenderer2D` | `contracts/IRenderer2D.cs` | 第 1.3 节 | 必需 |
| `IAudio` | `contracts/IAudio.cs` | 第 1.4 节 | 必需 |
| `IInput` | `contracts/IInput.cs` | 第 1.5 节 | 必需 |
| `IFileSystem` | `contracts/IFileSystem.cs` | 第 1.6 节 | 必需 |
| `IResourceLoader` | `contracts/IResourceLoader.cs` | 第 1.7 节 | 必需 |
| `INavigation2D` | `contracts/INavigation2D.cs` | 第 1.8 节 | 可选 |
| `ISpatialQuery` | `contracts/ISpatialQuery.cs` | 第 1.9 节 | 必需 |
| `IUISurface` | `contracts/IUISurface.cs` | 第 1.10 节 | 必需 |
| `IPlatform` | `contracts/IPlatform.cs` | 第 1.11 节 | 可选 |
| `IRenderer3D` | `contracts/IRenderer3D.cs` | 第 1.12 节 | 条件必需（仅 model 型外形需要）|
| `ICamera` | `contracts/ICamera.cs` | 第 1.13 节 | 必需 |

接口签名以 `02_引擎适配层.md` 为准，本目录不得出现任何具体引擎符号。

### `IFileSystem.ListFiles` 语义（实现级约定，02 未限定）

`02_引擎适配层.md` 第 1.6 节只给出签名 `listFiles(dirPath: String): List<String>`，未规定返回
值是绝对路径还是相对 `dirPath` 的路径、是否递归子目录。数据目录约定为
`data/<dataset>/<domain>/<table>.json`（见 `data/README.md`），`data_registry`
（`core/foundation/data_registry/core/FileSystemDataSource.cs`，T1-4）需要列举 `<dataset>/`
下全部表文件，因此本仓库拍板以下实现级约定，供全部 `IFileSystem` 实现遵循：

- **递归**：返回 `dirPath` 之下递归全部文件（含子目录中的文件），不含目录条目本身。
- **相对路径**：每个结果路径相对 `dirPath`，不含 `dirPath` 本身的前缀。
- **分隔符**：统一用 `/` 分隔，不用平台相关的 `\`。
- **排序**：按序数（ordinal）排序，保证同一批文件每次调用结果顺序一致。

`adapters/stub/StubFileSystem.cs` 的 `ListFiles` 已按此约定实现（内存文件系统按路径前缀匹配、
截取前缀后的剩余部分即可）；各引擎的真实实现（`adapters/<engine_name>/`）需要同样遵循，
否则 `FileSystemDataSource` 在该引擎上会解析出错误的表名。

## 类型映射摘要

| 中立记法 | C# 类型 | 备注 |
|---|---|---|
| `Bool` | `bool` | |
| `Int` | `int` | |
| `Number` | `double` | 全架构数值统一 double，不与 float 混用 |
| `String` | `string` / `string?` | 依 `Optional<String>` 与否决定是否可空 |
| `Id` | `Core.Foundation.Common.Id` | |
| `Vec2` | `Core.Foundation.Common.Vec2` | |
| `Handle` | 各接口私有的 `readonly struct XxxHandle` | 如 `SpriteHandle`、`ModelHandle`、`SfxHandle`、`ParticleHandle`；`int Value`，不用 `object` |
| `List<T>` | `IReadOnlyList<T>` | 入参出参一致 |
| `Map<K,V>` | `IReadOnlyDictionary<K,V>` | |
| `Optional<T>` | 引用类型 `T?`；值类型 `T?`（`Nullable<T>`）| |
| `Callback` | 具名 `delegate` | 每个回调按参数单独定义，不用裸 `Action`/`Func` |
| 枚举 `a\|b\|c` | C# `enum`，值名 PascalCase | |
