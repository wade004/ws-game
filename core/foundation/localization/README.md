# L0 基础层 · localization 本地化

职责：文本与资源的语言切换——按当前语言取文案、变量代入、缺失文案回退、切换语言（见
[01_分层与依赖.md](../../../architecture/01_分层与依赖.md) L0 模块表 `localization` 行、
[03_运行时骨架.md](../../../architecture/03_运行时骨架.md) 第 9 节 `L10nHost` 签名、
[04_数据与内容管线.md](../../../architecture/04_数据与内容管线.md) 第 7.2 节）。

依赖：`core/foundation/common`（`Id`）、`core/foundation/event_bus`（`IEvent`、`IEventBus`）、
`core/foundation/data_registry`（`IDataRegistryView`、`DataRecord`、`TableSchema` 等）与
.NET 标准库；不引用任何引擎适配层实现、不使用系统时间、不使用多线程、不使用系统级
`Random`、不使用反射。

## 目录

```
localization/
  README.md
  contracts/
    Events.cs             L10nEventKeys、L10nLanguageChangedEvent
    IL10nDiagnostics.cs   IL10nDiagnostics
    IL10nHost.cs          IL10nHost
    L10nOptions.cs        MissingKeyPolicy、L10nOptions
    L10nSchemas.cs        l10n.locale / l10n.text 的 TableSchema（从 data_registry 迁入）
  core/
    L10nHost.cs             IL10nHost 默认实现
    InMemoryL10nDiagnostics.cs
  schema/
    README.md               l10n.locale / l10n.text 字段说明、迁移判断记录
  tests/
    L10nHostTests.cs
```

## 设计要点与判断记录

1. **`l10n.locale`/`l10n.text` 两张表的 `TableSchema` 从 `data_registry/core/BuiltinSchemas.cs`
   迁入本模块**：见 `schema/README.md`"判断记录"一节。`data_registry` 侧只保留一处指向本
   模块的注释，字段定义未改变，`data_registry` 的测试改为引用 `L10nSchemas.Locale`/
   `L10nSchemas.Text`。

2. **`IL10nHost` 在 03 第 9 节签名之外补充 `HasText`/`SupportedLocales`/`DefaultLocale`**：
   03 第 9 节只给出 `text`/`setLocale`/`getLocale` 三个方法，未给出"如何得知一个键是否有
   文案"（例如 UI 想按"存在才显示"的逻辑条件渲染一个可选提示，不想为此触发一次带缺失警告
   的 `Text` 调用）、"如何得知当前支持哪些语言/默认语言是谁"（设置界面的语言下拉列表需要
   枚举支持语言）。任务书显式拍板这三个补充成员，与 input_map 补充 `Update` 等方法是同一类
   "接口签名未逐项排他性限定 ⇒ 允许按需要补充"处理。

3. **`HasText` 与 `Text` 共用同一条"当前语言 → 回退链 → 默认语言"解析路径
   （`TryResolve`），语义上只回答"找不找得到"，不涉及 `MissingKeyPolicy`**：`MissingKeyPolicy`
   是"找不到之后怎么兜底返回值"的策略，与"是否存在真实文案"是两个不同的问题——
   `HasText` 恒定回答后者，不受 `MissingKeyPolicy` 取值影响。

4. **默认语言是"最终兜底"，即使某语言自己声明的回退链没有走到默认语言**：04 第 7.2 节原文
   "某语言缺文本时回退到默认语言"，未明确这是否要求回退链必须显式包含默认语言。本模块选择
   "默认语言总是最后再兜底查一次"，即回退链是否包含默认语言不影响最终一定会尝试默认语言这
   一步，语义上更贴近"注册了一种语言支持后不必人人都手工把 `fallback` 显式接到默认语言"。

5. **`is_default=true` 必须恰好一条、回退链成环（含自我回退）在构造期直接抛
   `ArgumentException`**：均为任务书显式拍板；构造期一次性校验完，运行期 `Text`/`HasText`/
   `SetLocale` 不再重复做这两项检查，`TryResolve` 内的成环判断只是防御性兜底（正常路径下
   构造期已经保证不会走到）。

6. **`SetLocale` 对未声明的语言抛 `ArgumentException`，只有语言实际发生变化才发
   `L10nLanguageChangedEvent`**：与 `IInputMapHost.Rebind`（业务分支返回 `bool`）不同，
   `SetLocale` 传入一个未声明的语言属于"调用方拼错语言 id"这类编程错误而非正常业务分支，
   选择立即抛异常暴露；"未变化不发事件"是常见的变更通知惯例，避免下游订阅方收到"从 A 切到
   A"这种空变更通知。

## 诊断

`IL10nDiagnostics`（默认实现 `InMemoryL10nDiagnostics`）记录：缺失文本键（沿完整解析路径均
未找到）、变量代入时缺失变量（`L10nOptions.WarnOnMissingVar` 开启时）两类警告。不依赖任何
引擎适配层接口。

## 基础架构提供 / 游戏层提供

| 能力 | 基础架构提供 | 游戏层提供 |
|---|---|---|
| 文本查询、回退链解析、变量代入、语言切换的实现机制 | 是 | 具体语言清单、回退链、文本内容（`l10n.locale`/`l10n.text` 数据行） |
| `l10n.language_changed` 事件 | 是 | 订阅该事件做具体呈现（如切换界面字体/排版方向） |
| 缺失文案回退策略的三种取值 | 是 | 选用哪一种（`L10nOptions.MissingKeyPolicy`） |
