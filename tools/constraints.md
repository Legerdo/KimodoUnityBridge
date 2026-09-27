---
name: kimodo-constraints
description: Canonical protocol for reference-Pose-backed generation constraints.
---

# Constraint protocol / 约束协议

本文档是 `kimodo_generate_animation` 点约束的唯一规范。`generation.md`、
`pose.md` 和命令帮助只描述入口与示例；遇到冲突时，以本文档为准。

## 1. 核心模型

每个点约束都由两部分组成：

```text
PointConstraint = { frame, type, pose?, root2d?, effect? }
```

- `ExplicitPose` 是当前场景上下文中已经确认的 External Pose 引用：
  `{ "track": "<pose track>", "index": <non-negative integer> }`。
- `AvatarRestPose` 是目标角色 Avatar skeleton 的 bind/rest pose。省略 `pose` 时由系统
  创建它，并在响应中记录 `avatar_rest` warning。
- `type` 是唯一的约束类型判别字段：`fullbody`、`root2d` 或 `root_effect`。
- `root2d` 和 `effect` 是并列覆盖字段；三种类型不互相嵌套。

Pose 是约束的起始状态，不是可选的装饰数据。显式 Pose 或 AvatarRestPose 都提供根节点
完整变换、身体肌肉、双脚以及需要的末端数据，使约束不会退化为隐含的当前场景姿态、
`root Y=0` 或零旋转。

## 2. 统一生命周期

1. 使用 `pose_get` 从当前真实角色和 Animator/Timeline 上下文采样 Pose；没有可用源
   Clip 时使用 `scene_time_seconds`。
2. 检查返回的根高度、双脚、身体姿态和目标末端。需要修改目标时，使用
   `pose_set` 创建派生 Pose。
3. 显式 Pose 只有经过确认的 `{track,index}` 才能进入 generation 请求；省略 Pose 时由
   Avatar skeleton 生成 `avatar_rest`，并在 generation 响应中记录 warning。
4. 在目标帧引用该 Pose，并只声明需要覆盖的通道。
5. 生成完成后，按同样的帧和 Pose 证据验证结果；生成请求本身不能反向修改 Pose。

## 3. 强制规则

- `fullbody` 必须提供 `pose`，用于完整身体参考。
- `root2d` 必须提供 `root2d.position: [x,z]` 和 `root2d.heading: [x,z]`；`pose` 可省略。
- `root_effect` 必须同时提供 `root2d` 和 `effect`；`effect` 至少包含一个手或脚目标，
  每个目标必须提供 world-space `position`，旋转可选；`pose` 可省略。
- 禁止从未声明的当前姿态、场景默认姿态、`root Y=0` 或零四元数隐式补全约束。
- 三种点约束由 `type` 严格区分，不能在同一项中混用字段。需要 Root 与 Effect
  同时存在时使用 `type: "root_effect"`，不要再嵌套另一个 constraint 类型。
- `root_path` 是分析得到的路径句柄，不是点约束。它不替代点约束的 Reference Pose，
  但会在生成时编译成 Root2D 路径样本。
- `inout` 是从已完成 Clip 自动采样的边界上下文指令，不是用户提交的点约束；它由
  采样器生成内部 FullBody 数据，不要求用户为每个边界手写 `pose`。

显式 Pose 引用失效、Pose 没有有效采样数据或末端目标缺失时，命令必须拒绝请求。
缺省 Pose 必须明确记录为 `avatar_rest` warning，不得静默伪装成用户确认的 Pose。

## 4. 约束类型

### FullBody

```json
{
  "frame": 90,
  "type": "fullbody",
  "pose": { "track": "Character Pose", "index": 3 }
}
```

使用 Reference Pose 的身体肌肉、根变换和脚部通道作为完整身体约束。该类型没有
独立的直接位置快捷方式。

### Root2D

```json
{
  "frame": 60,
  "type": "root2d",
  "pose": { "track": "Character Pose", "index": 1 },
  "root2d": {
    "position": [2.0, 5.0],
    "heading": [0.0, 1.0]
  }
}
```

Reference Pose（或省略时的 Avatar rest pose）保留根高度和支撑姿态；`position` 与 `heading` 只覆盖根的 XZ 平面
位置和水平朝向。Root2D 的导出通道仍然是根平面通道，不会因此变成 FullBody 约束。

### RootEffect

```json
{
  "frame": 120,
  "type": "root_effect",
  "pose": { "track": "Character Pose", "index": 4 },
  "root2d": {
      "position": [2.4, 5.0],
      "heading": [0.0, 1.0]
    },
  "effect": {
    "right_hand": {
      "position": [2.55, 0.95, 5.35]
    }
  }
}
```

RootEffect 是公共语义名称，内部仍映射到 `mix`，不新增底层 solver 类型。求解顺序
是先应用 Root2D 支撑根，再应用声明的末端执行器。这样可以在同一采样点表达“角色
位于这里，并且右手到达这里”。

## 5. Effector validation

`effector_validate` 在生成 `root_effect` 前验证一个末端目标。它使用当前角色的
Avatar、显式 Pose 或 `avatar_rest`、可选 Root2D 覆盖，并运行与生成相同的 Humanoid IK
管线。结果包含 `position_error`、可选的 `rotation_error_degrees`、几何 reach envelope
和 `valid|unreachable|invalid` 状态。

```json
{
  "character": "<character>",
  "effector": "right_hand",
  "pose": { "track": "<pose track>", "index": 5 },
  "target": { "position": [0.25, 1.08, 0.42] },
  "options": {
    "position_tolerance": 0.03,
    "rotation_tolerance_degrees": 15,
    "project_if_unreachable": true
  }
}
```

缺省 `pose` 时结果会设置 `pose_source: "avatar_rest"`、`pose_defaulted: true` 并返回
warning。`projected_target` 只是建议值，不会自动修改目标或生成请求；调用方必须明确
接受后再写入 Pose 或 RootEffect。验证 API 不修改场景角色、Pose 或动画资产。

## 6. 同帧合并和优先级

同一帧的多个点约束按通道合并：

1. `fullbody` 提供身体肌肉、根完整变换和双脚支撑。
2. `root2d` 覆盖根的平面位置和水平朝向。
3. `root_effect` 同时提供 Root2D 和末端目标，并在内部作为 `mix` 导出。

同一通道被多个样本声明时，按样本顺序保留最后一个有效值。不同通道可以在同一帧
组合，但单个 constraint 项只能有一个 `type`。

## 7. 苹果拾取的推荐编排

对接近、弯腰、伸手和抓取等关键帧，先分别获取或派生支撑 Pose，再生成约束：

```text
pose_get(scene/timeline time)
    -> inspect root/feet/body
    -> pose_set for hand target when needed
    -> confirmed {track,index}
    -> effector_validate against apple world position
    -> fullbody or root2d/root_effect constraint
```

推荐使用：

- 接近和停止：`root2d` + 站立/行走支撑 Pose；
- 下沉和伸手：`fullbody` 或 RootEffect 的 Root2D 支撑 Pose；
- 抓取：`type: "root_effect"`，其中顶层 `pose` 提供身体支撑，`root2d` 提供根覆盖，
  `effect.right_hand.position` 提供苹果接触目标；
- 抬起和持有：后续 FullBody 或 RootEffect Pose，确保手目标随角色继续移动。

所有 Pose、约束和生成结果都必须使用当前场景返回的安全名称和 `{track,index}`，不得
从 archive、旧 Timeline 或文件路径猜测引用。
