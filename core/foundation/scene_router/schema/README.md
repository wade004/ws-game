# `world.map` 被本模块读取的字段

对应 [01_分层与依赖.md](../../../../architecture/01_分层与依赖.md) L0 模块表 `scene_router` 行、
[05_对象模型与世界.md](../../../../architecture/05_对象模型与世界.md) 第 4.1 节 `world.map` 字段表。

**归属说明**：`world.map` 整表按 04/05 归属 L4（对象模型与世界），不是本模块（L0
`scene_router`）拥有的表。本模块只是场景路由流程需要读取其中几个字段，因此暂时在
`core/WorldMapSchema.cs` 登记一份**只包含本模块读取字段**的 `TableSchema`，供
`SceneRouter`/`SceneDescriptor` 独立运行与测试；待 L4 对象模型与世界模块落地时，应将
`world.map` 完整字段表（含 `regions`/`teleport_points`/`music_ref`/`allowed_difficulties`）
迁移到该模块，并从本模块移除这份临时登记（与 `data_registry/core/BuiltinSchemas.cs`
"暂存处，届时应移除"同一惯例）。

## 字段（本模块读取的子集）

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | `world.<地图名>`，`SceneDescriptor.Id` |
| `scene_ref` | String | 是 | 场景资源引用（不含路径），由引擎适配层解析加载；`SceneDescriptor.SceneRef`，`SceneRouter` 据此发起 `IResourceLoader.LoadAsync` |
| `nav_ref` | String | 是（05 原文） | 导航资源引用（可行走区域、遮挡层数据）；`SceneDescriptor.NavRef` 在本模块 C# 类型层面暴露为可空，见 `../README.md` 判断记录 1 |
| `spawn_points` | List\<{id, position, facing}\> | 是 | 玩家出生点/复活点清单；本模块只读取第 0 个元素的 `position` 作为 `SceneDescriptor.DefaultSpawnPosition`（"默认出生点"取列表第一条，见 `../README.md`） |

## 本模块不读取的字段（归属 05/L4，见上方归属说明）

`regions`、`teleport_points`、`music_ref`、`allowed_difficulties`——这些字段可以出现在
`world.map` 的 JSON 数据里，`DataRegistry` 的字段校验只检查已登记字段，不会因为记录里存在
未登记字段而报错（见 `../README.md` 判断记录 2），因此不影响本模块读取上表四个字段。

## 场景路由标准流程涉及的字段用途（见 03_运行时骨架.md 第 6 节）

- 步骤 1：`SceneRouter.LoadScene(sceneId)` 按 `id` 在 `world.map` 里查找记录，不存在则拒绝。
- 步骤 3：对 `scene_ref`（必有）与 `nav_ref`（非空时）逐个发起 `IResourceLoader.LoadAsync`。
- 步骤 5（本模块不做，见 `../README.md`"不负责什么"）：`spawn_points` 的完整刷新处理属于
  `spawn_system`/L4，本模块只抽取 `DefaultSpawnPosition` 一个字段供 `SceneDescriptor` 携带。
