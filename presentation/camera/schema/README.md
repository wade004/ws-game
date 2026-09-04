# `camera_profile` 表字段表

对应 [01_分层与依赖.md](../../../architecture/01_分层与依赖.md) L5 模块表 `camera` 行"主要数据表：
camera_profile"、[09_表现层.md](../../../architecture/09_表现层.md) 第 3.5、8 节。强类型 C# 结构见
`../contracts/CameraProfile.cs`（`CameraProfile`/`CameraBounds`/`ShakePreset`）。

| 字段 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `id` | Id | 是 | 镜头档位 id |
| `pitch_degrees` | Number | 是 | 固定俯角，经 `ICamera.Configure` 设定 |
| `yaw_degrees` | Number | 是 | 固定水平朝向，经 `ICamera.Configure` 设定 |
| `zoom_min` | Number | 是 | 允许缩放范围下限 |
| `zoom_max` | Number | 是 | 允许缩放范围上限 |
| `zoom_default` | Number | 是 | `Configure` 时应用的初始缩放，须落在 `[zoom_min, zoom_max]` |
| `follow_lerp` | Number | 是 | 跟随平滑系数，传给 `ICamera.Follow(planePos, smoothing)` |
| `bounds` | Optional\<{min: Vec2, max: Vec2}\> | 否 | 跟随边界，`Update` 时把目标位置夹到边界内 |
| `shake_presets` | List\<{id: Id, amplitude: Number, duration: Number, frequency: Number}\> | 否 | 震屏档位清单，供 `CameraHost.Shake(id)` 按 id 查找 |

## 本模块不做什么

- 不提供 `camera_profile` 表的 `DataRegistry`/`FromRecord` 数据行解析器——本任务（P4-1）只要求给出
  表 schema 与强类型 C# 结构，数据加载器留待具体游戏接入阶段按 `core/foundation/data_registry`
  惯例补充（见模块 README"契约缺口"一节）。
- `shake_presets.frequency` 字段随表结构一并登记，但 `ICamera.Shake(intensity, durationSeconds)`
  本身没有频率参数（见 02 第 1.13 节），`CameraHost.Shake` 当前不传递该字段，只是随数据行保留，
  供未来接口扩展或引擎侧自行按约定解释。
