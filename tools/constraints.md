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

- `pose`（如提供）是当前场景上下文中的 External Pose 引用：
  `{ "track": "<pose track>", "index": <non-negative integer> }`。
- `type` 是唯一的约束类型判别字段：`fullbody`、`root2d` 或 `root_effect`。
- `root2d` 和 `effect` 是并列覆盖字段；三种类型不互相嵌套。
- 每个项只能包含该 `type` 允许的字段：`fullbody` 只允许 `pose`；`root2d` 允许可选
  `pose` 和必需的 `root2d`；`root_effect` 允许可选 `pose`、必需的 `root2d` 和
  `effect`。未知字段必须拒绝，不能被静默忽略。

显式 Pose 用于为约束指定支撑姿态。需要完整身体姿态时使用 `fullbody` 并提供 Pose。

## 2. 统一生命周期

1. 需要指定支撑姿态时，使用 `pose_get` 从场景、Timeline 或 Clip 采样。
2. 检查并按需使用 `pose_set` 编辑派生 Pose。
3. 在目标帧传入已确认的 Pose 引用，并只声明需要的约束通道。
4. 生成完成后，按动作目标验证结果。

## 3. 强制规则

- `fullbody` 必须提供 `pose`，用于完整身体参考。
- `root2d` 必须提供 `root2d.position: [x,z]` 和 `root2d.heading: [x,z]`；`pose` 可省略。
- `root_effect` 必须同时提供 `root2d` 和 `effect`；`effect` 至少包含一个手或脚目标，
  每个目标必须提供 world-space `position`，旋转可选；`pose` 可省略。
- 三种点约束由 `type` 严格区分，不能在同一项中混用字段。需要 Root 与 Effect
  同时存在时使用 `type: "root_effect"`，不要再嵌套另一个 constraint 类型。
- `root_path` 是分析得到的路径引用，不是点约束；它与点约束共用约束帧空间。
- `inout` 是从已完成 Clip 自动采样的边界上下文指令，不是用户提交的点约束；它由
  采样器生成内部 FullBody 数据，不要求用户为每个边界手写 `pose`。
- Command 输入不合并约束：同一帧只能有一个显式点约束，`root_path` 生成的采样帧也不能
  与显式点约束重叠。冲突必须报错，不能靠数组顺序覆盖。

显式 Pose 引用失效、Pose 没有有效采样数据或末端目标缺失时，命令必须拒绝请求。

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

`position` 与 `heading` 覆盖根的 XZ 平面位置和水平朝向；Root2D 不等同于 FullBody。

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

RootEffect 在同一采样点表达“角色位于这里，并且右手到达这里”。

## 5. Effector validation

`effector_validate` 在生成 `root_effect` 前验证一个末端目标。它接受可选 Pose 和 Root2D
输入；若 generation 覆盖模型，预检结果不代表该模型下的可达性。结果包含 `position_error`、可选的
`rotation_error_degrees`、几何 reach envelope
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

`projected_target` 只是建议值，不会自动修改目标或生成请求。验证不会改写已完成动画。

## 6. 同帧冲突

Command 输入不会把多个点约束按通道合并，也不会按数组顺序覆盖。每个采样帧只能有
一个显式点约束；`root_path` 的采样帧不能与显式点约束重叠，冲突直接拒绝。需要根和
末端目标时，在同一项中使用 `type: "root_effect"`。

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
