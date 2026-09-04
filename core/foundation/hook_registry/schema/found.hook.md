# `found.hook` 字段表

对应 `01_分层与依赖.md` L0 模块表 `hook_registry` 行的主要数据表、`04_数据与内容管线.md`
第 1.1 节表清单里的 `found.hook`（"脚本钩子注册表：钩子 id、触发时机、参数签名"）。

本模块（T1-7a）只提供从内存定义列表构造挂载点登记表的入口
（`IHookRegistry.DeclareFromDefinitions(IEnumerable<HookPointDefinition>)`），不做 JSON
读取；从数据文件读取并转换成 `HookPointDefinition` 列表是数据注册表
（`core/foundation/data_registry`，T1-4）的职责，与 `event_bus` 处理 `found.event_catalog`
同一惯例（见 `core/foundation/event_bus/schema/found.event_catalog.md`"本模块不做什么"一节）。

## 字段

| 字段 | 类型 | 必需 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | 挂载点 id，格式 `found.hook.<name>`（例如 `found.hook.scene_pre_unload`），满足 04 第 2.1 节 id 规范 `^[a-z][a-z0-9_]*(\.[a-z0-9_]+)+$`。 |
| `signature` | string | 是 | 参数签名说明文本，供校验与文档化，例如 `"sceneId: Id"`；不是机器可读的强类型声明，本模块不据此做参数类型强校验，只作为文档呈现给 `HookPointDefinition.Signature`。 |
| `allow_multiple` | bool | 否，默认 `true` | 本挂载点是否允许注册多个回调；`false` 时第二次 `IHookRegistry.Register` 抛异常。 |
| `description` | string \| null | 否 | 挂载点说明（触发时机、用途）。 |

## 示例

```json
{
  "id": "found.hook.scene_pre_unload",
  "signature": "sceneId: Id",
  "allow_multiple": true,
  "description": "场景卸载前触发，存档系统在此完成自动存档（见 03 第 6 节步骤 4）"
}
```

## 与 `WellKnownHooks` 的关系

`contracts/WellKnownHooks.cs` 给出架构文档已点名的两个挂载点 id 常量
（`found.hook.scene_pre_unload`、`found.hook.scene_post_load`，见 03 第 6 节），仅供代码内
按类型安全的方式引用这两个 id 字符串，本身不构成声明——具体 `IHookRegistry.DeclareHookPoint`
调用属于组装场景路由模块（`core/foundation/scene_router`）时的职责，不属于本模块。

## 本模块不做什么

- 不读取 `found.hook.json`（或具体游戏的对应数据文件）；只提供
  `IHookRegistry.DeclareFromDefinitions(IEnumerable<HookPointDefinition>)` 供数据注册表
  转换后批量调用。
- 不知道任何具体挂载点应该在什么时机被 `Invoke`——挂载点的声明与调用时机由使用方
  （`SceneRouter` 等更上层模块）决定，本模块只提供登记与调用机制本身。
