# L0 基础层 · rng 分流随机源

职责：按用途分流生成可复现的伪随机数（`RngHost` 契约，见
[03_运行时骨架.md](../../../architecture/03_运行时骨架.md) 第 9 节）。不同用途（掉落流、AI 流、
技能流……）各自持有独立的随机流，互不干扰；每条流的内部状态可被存档系统读出与写回
（见 [10_存档与持久化.md](../../../architecture/10_存档与持久化.md) 第 2.4 节
`stream_states: Map<String, RngStreamState>`），保证"同种子同输入同结果"的确定性回放要求
（见同文档第 8 节、[落地方案与分阶段计划.md](../../../architecture/落地计划/落地方案与分阶段计划.md)
第 4.1 节）。

依赖：只依赖 `core/foundation/common`（`Id`）与 .NET 标准库；不引用任何其他模块、任何引擎
适配层实现。

不负责什么：

- 不提供任何"业务含义"的随机接口（掉落表抽取、AI 权重抽取等），那些是更上层模块基于
  `IRngHost.Next`/`NextInt` 自行组合的职责；本模块只提供分流的底层数值随机源。
- 不做存档文件的读写，只提供 `RngStreamState` 这个可被存档系统序列化的值类型与
  `getStreamState`/`setStreamState` 两个契约方法；持久化格式与时机是
  `core/foundation/save_system` 的职责。
- 不使用任何系统级非确定随机源、系统挂钟时间、`string.GetHashCode()`（.NET Core 下逐进程
  随机化）、多线程或锁——全架构要求核心模拟可确定性复现，见
  [00_架构总则.md](../../../architecture/00_架构总则.md)。

## 目录

```
rng/
  README.md
  contracts/   RngStreamState.cs IRngHost.cs
  core/        Xoshiro256StarStar.cs SeedDerivation.cs RngHost.cs
  tests/       RngHostTests.cs
```

（本模块无数据表，不设 `schema/` 子目录。）

## 设计要点

- **算法**：每条流内部是一个独立的 xoshiro256** 生成器（Blackman & Vigna 公开算法，
  256 位状态、无系统依赖、速度快、统计质量足以满足游戏随机场景），状态即 4 个 `ulong`。
- **懒创建与派生**：流首次被访问时才创建，初始状态由主种子与流 `Id` 派生：
  `seed = SplitMix64(masterSeed ^ Fnv1a64(stream.Value 的 UTF-8 字节))`，再用同一个
  SplitMix64 生成器连续产出 4 个 `ulong` 作为 xoshiro256** 的初始状态（若结果恰好全零，
  则替换为一个固定非零兜底值，避免生成器困在全零状态）。派生只依赖 `masterSeed` 与
  `stream.Value` 两个确定性输入，不使用 `string.GetHashCode()`。
- **`Next`**：用生成器产出的 `ulong` 取高 53 位构造 `[0,1)` 区间的 `double`
  （`(x >> 11) * (1.0 / (1UL << 53))`），保证均匀分布且不出现浮点舍入导致的 1.0 边界值。
- **`NextInt`**：闭区间 `[min, max]`，用拒绝采样消除取模偏差（丢弃会导致不均匀分布的
  "多余尾部"随机值后再取模），不使用 `value % range` 直接取模的有偏写法；`min > max`
  抛 `ArgumentException`。
- **状态往返**：`RngStreamState` 是 `readonly struct`，四个 `ulong` 字段可直接比较相等；
  `ToString()`/`Parse()`/`TryParse()` 提供 `"s0-s1-s2-s3"`（每段 16 位十六进制、小写、
  定长补零）的稳定文本形式，供存档系统序列化使用。
- **`Streams`**：已创建过的流按 `Id` 的序数（ordinal）顺序排列，供存档系统遍历写出全部
  `stream_states`。
- **`Reset`**：清空全部已创建的流并更换主种子，用于开新档/读档前重建。

## 基础架构提供 / 游戏层提供

| 能力 | 基础架构提供 | 游戏层提供 |
|---|---|---|
| 分流随机源机制、xoshiro256** 实现、状态持久化文本形式 | 是 | 无 |
| 随机源分流粒度（全局/按系统/按对象，即游戏层用什么样的 `Id` 命名各条流） | 提供机制 | 具体流命名与用途划分（如 `"loot"`、`"ai"`、`"skill"`，或更细粒度） |
