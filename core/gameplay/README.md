# L4 玩法层 Gameplay

职责：组合载体与规则，形成可复用的通用玩法机制骨架（不含具体游戏内容）。

依赖：仅直接引用 Core.Carriers（L3）。

## 判断记录

- T-N6-1（ADR-0035 决策 1）：`tests/EndToEnd/GameWorldFixture.Build` 此前就地"组装一整套 L0～L4
  世界"的逻辑（事件目录/总线、`DataRegistry` 装载、`RngHost`、`WorldSim`、`StubSpatialQuery`、
  存档系统、`SimClockHost`、`GameplayAssembly`、玩家单位注册）已上提为框架交付物
  `Core.Sim.HeadlessWorldBuilder`（见 `core/sim/README.md`）。`GameWorldFixture.Build` 现在只保留
  "从磁盘读 `data/_framework`/`data/_sample` 构造 `FileSystemDataSource`"这一段仓库路径相关逻辑，
  其余委托给该装配根；`Fixture` 类型与全部公开字段/方法签名不变，`Tests.Gameplay` 既有用例不受
  影响（仅 `Tests.Gameplay.csproj` 新增对 `Core.Sim.csproj` 的引用，方向仍是"测试工程引用生产
  代码"，不改变本模块自身对外的层次依赖）。
