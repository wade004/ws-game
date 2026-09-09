# `world.map` 的字段与本模块读取的子集

对应 [01_分层与依赖.md](../../../../architecture/01_分层与依赖.md) L0 模块表 `scene_router` 行、
[05_对象模型与世界.md](../../../../architecture/05_对象模型与世界.md) 第 4.1 节 `world.map` 字段表。

**归属说明**：`world.map` 整表按 04/05 归属 L4（对象模型与世界），不是本模块（L0
`scene_router`）拥有的表。本模块只是场景路由流程需要读取其中几个字段（下表"本模块读取"一列
标是/否），因此暂时在 `core/WorldMapSchema.cs` 登记这份 `TableSchema`，供
`SceneRouter`/`SceneDescriptor` 独立运行与测试；待 L4 对象模型与世界模块落地时，应将
`world.map` 整表迁移到该模块，并从本模块移除这份临时登记（与 `data_registry/core/
BuiltinSchemas.cs`"暂存处，届时应移除"同一惯例）。

DATA-DOC-01 收口（第十二轮外部审核，architecture/落地计划/audit-ac3b622-20260909）后，
`TableSchema` 已登记 05 第 4.1 节全部八个字段并做类型校验（取代旧版本"只登记本模块读取的四个
字段"）；本模块自身仍然只读取前四个，后四个只是类型校验能力补齐，不代表本模块开始消费它们。

## 字段

| 字段 | 类型 | 必填 | 本模块读取 | 说明 |
|---|---|---|---|---|
| `id` | Id | 是 | 是 | `world.<地图名>`，`SceneDescriptor.Id` |
| `scene_ref` | String | 是 | 是 | 场景资源引用（不含路径），由引擎适配层解析加载；`SceneDescriptor.SceneRef`，`SceneRouter` 据此发起 `IResourceLoader.LoadAsync` |
| `regions` | List\<Id\> | 否 | 否 | 该地图内的子区域划分，供 Expr 中 `world` 分组按区域读取标志；归属 L4，未登记引用完整性校验（无独立子区域登记表） |
| `nav_ref` | String | 是（05 原文） | 是 | 导航资源引用（可行走区域、遮挡层数据）；`SceneDescriptor.NavRef` 在本模块 C# 类型层面暴露为可空，见 `../README.md` 判断记录 1 |
| `spawn_points` | List\<{id, position, facing}\> | 是 | 是 | 玩家出生点/复活点清单；本模块只读取第 0 个元素的 `position` 作为 `SceneDescriptor.DefaultSpawnPosition`（"默认出生点"取列表第一条，见 `../README.md`） |
| `teleport_points` | List\<{id, position}\> | 否 | 否 | 供传送类效果/AreaTrigger 引用的命名传送目标；由 `Core.Gameplay.Assembly.TeleportTargetResolver` 真实消费（先查该字段、再查 `spawn_points`），只做数组类型校验，不展开逐条元素形状 |
| `music_ref` | String | 否 | 否 | 背景音乐资源引用；归属 L4 |
| `allowed_difficulties` | List\<Id\> | 否 | 否 | 该地图允许应用的难度档位（见 08）；归属 L4，未登记引用完整性校验（当前无独立难度档位登记表） |

## 场景路由标准流程涉及的字段用途（见 03_运行时骨架.md 第 6 节）

- 步骤 1：`SceneRouter.LoadScene(sceneId)` 按 `id` 在 `world.map` 里查找记录，不存在则拒绝。
- 步骤 3：对 `scene_ref`（必有）与 `nav_ref`（非空时）逐个发起 `IResourceLoader.LoadAsync`。
- 步骤 5（本模块不做，见 `../README.md`"不负责什么"）：`spawn_points` 的完整刷新处理属于
  `spawn_system`/L4，本模块只抽取 `DefaultSpawnPosition` 一个字段供 `SceneDescriptor` 携带。
