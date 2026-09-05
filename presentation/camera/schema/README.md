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

## 数据行解析

`CameraSchemas.Profile`（`schema/CameraSchemas.cs`）登记 schema，`CameraProfile.FromRecord`
（`contracts/CameraProfile.cs`）从已加载的 `DataRecord` 构造强类型 `CameraProfile`（P4-2 补齐，取代
P4-1"本模块不做什么"一节原先记录的契约缺口）。字段解析惯例同
`Core.Foundation.DisplayInfo.DisplayInfo.FromRecord`：必填字段用 `GetXxx`（缺失时
`DataFieldException`），可选字段用 `TryGetXxx`；`bounds`/`shake_presets` 是嵌套 JSON 结构，04 未给出
专用访问器，按 `DisplayInfo.FromRecord` 解析 `mirror_pairs`/`anchor_points` 的同一惯例手动展开
`JsonObject`/`JsonArray`。`shake_presets` 每项的 `id` 字段随内容表 `id` 命名惯例点分书写，但不受
`camera_profile` 表自身的"id domain 前缀必须等于表名"这条 `primary_key` 校验约束——它是嵌套在
`Array` 字段里的普通对象属性，不是独立注册的表行。测试见
`presentation/camera/tests/CameraProfileFromRecordTests.cs`。

## 本模块不做什么

- `shake_presets.frequency` 字段随表结构一并登记，但 `ICamera.Shake(intensity, durationSeconds)`
  本身没有频率参数（见 02 第 1.13 节），`CameraHost.Shake` 当前不传递该字段，只是随数据行保留，
  供未来接口扩展或引擎侧自行按约定解释；`CameraProfile.FromRecord` 未声明 `frequency` 时按 `0.0`
  兜底（09 原文未规定缺省值，判断记录）。
