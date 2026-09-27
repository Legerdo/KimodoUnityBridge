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
PointConstraint = ReferencePose + ChannelOverrides
```

- `ReferencePose` 是当前场景上下文中已经确认的 External Pose 引用：
  `{ "track": "<pose track>", "index": <non-negative integer> }`。
- `ChannelOverrides` 是这个采样点要额外覆盖的 Root2D 平面位置/朝向或末端执行器目标。

Reference Pose 是约束的起始状态，不是可选的装饰数据。它提供根节点完整变换、
身体肌肉、双脚以及需要的末端数据，使约束不会退化为隐含的绑定姿态、`root Y=0`
或零旋转。

## 2. 统一生命周期

1. 使用 `pose_get` 从当前真实角色和 Animator/Timeline 上下文采样 Pose；没有可用源
   Clip 时使用 `scene_time_seconds`。
2. 检查返回的根高度、双脚、身体姿态和目标末端。需要修改目标时，使用
   `pose_set` 创建派生 Pose。
3. 只有经过确认的 `{track,index}` 才能进入 generation 请求。
4. 在目标帧引用该 Pose，并只声明需要覆盖的通道。
5. 生成完成后，按同样的帧和 Pose 证据验证结果；生成请求本身不能反向修改 Pose。

## 3. 强制规则

- `fullbody.pose` 必须存在，并且必须指向当前场景中的 External Pose。
- `root2d.pose` 必须存在。`root2d` 可以只使用参考 Pose 的根状态，也可以额外提供
  `position: [x,z]` 和 `heading: [x,z]`；两者必须成对出现。
- `root_effect.root2d.pose` 必须存在；它是 Root2D 支撑状态的引用。
- `root_effect` 至少需要一个 `left_hand`、`right_hand`、`left_foot` 或
  `right_foot`。每个末端字段也必须包含自己的 `pose` 引用，用于提供该末端的目标
  变换。
- 独立的 `left_hand`、`right_hand`、`left_foot` 和 `right_foot` 约束同样必须包含
  `pose`。
- 禁止从未声明的当前姿态、场景默认姿态、`root Y=0` 或零四元数隐式补全约束。
- `root_effect` 不能和同一约束项中的 `fullbody`、`root2d` 或独立末端字段混用。
  如果同一帧需要多个通道，应使用一个 `root_effect`，或使用多个独立点约束让系统
  按通道合并。
- `root_path` 是分析得到的路径句柄，不是点约束。它不替代点约束的 Reference Pose，
  但会在生成时编译成 Root2D 路径样本。
- `inout` 是从已完成 Clip 自动采样的边界上下文指令，不是用户提交的点约束；它由
  采样器生成内部 FullBody 数据，不要求用户为每个边界手写 `pose`。

缺少必需的 Pose、Pose 引用失效、Pose 没有有效采样数据或末端目标缺失时，命令必须
拒绝请求并返回错误；不得自动降级为无 Pose 约束。

## 4. 约束类型

### FullBody

```json
{
  "frame": 90,
  "fullbody": {
    "pose": { "track": "Character Pose", "index": 3 }
  }
}
```

使用 Reference Pose 的身体肌肉、根变换和脚部通道作为完整身体约束。该类型没有
独立的直接位置快捷方式。

### Root2D

```json
{
  "frame": 60,
  "root2d": {
    "pose": { "track": "Character Pose", "index": 1 },
    "position": [2.0, 5.0],
    "heading": [0.0, 1.0]
  }
}
```

Reference Pose 保留根高度和支撑姿态；`position` 与 `heading` 只覆盖根的 XZ 平面
位置和水平朝向。Root2D 的导出通道仍然是根平面通道，不会因此变成 FullBody 约束。

### RootEffect

```json
{
  "frame": 120,
  "root_effect": {
    "root2d": {
      "pose": { "track": "Character Pose", "index": 4 },
      "position": [2.4, 5.0],
      "heading": [0.0, 1.0]
    },
    "right_hand": {
      "pose": { "track": "Character Pose", "index": 5 }
    }
  }
}
```

RootEffect 是公共语义名称，内部仍映射到 `mix`，不新增底层 solver 类型。求解顺序
是先应用 Root2D 支撑根，再应用声明的末端执行器。这样可以在同一采样点表达“角色
位于这里，并且右手到达这里”。

### Standalone effectors

```json
{
  "frame": 120,
  "right_hand": {
    "pose": { "track": "Character Pose", "index": 5 }
  }
}
```

独立末端约束只覆盖对应的手或脚通道，但仍以自己的 Reference Pose 作为目标来源。
它不会自动提供根位置或完整身体支撑；需要两者时，应使用同帧 FullBody/Root2D，或
直接使用 RootEffect。

## 5. 同帧合并和优先级

同一帧的多个点约束按通道合并：

1. `fullbody` 提供身体肌肉、根完整变换和双脚支撑。
2. `root2d` 覆盖根的平面位置和水平朝向。
3. 独立末端约束覆盖对应的手脚目标。
4. `root_effect` 同时提供第 2 和第 3 项，并在内部作为 `mix` 导出。

同一通道被多个样本声明时，按样本顺序保留最后一个有效值。不同通道可以在同一帧
组合，但 `root_effect` 自身不能再和同项的兄弟约束字段组合。

## 6. 苹果拾取的推荐编排

对接近、弯腰、伸手和抓取等关键帧，先分别获取或派生支撑 Pose，再生成约束：

```text
pose_get(scene/timeline time)
    -> inspect root/feet/body
    -> pose_set for hand target when needed
    -> confirmed {track,index}
    -> fullbody or root2d/root_effect constraint
```

推荐使用：

- 接近和停止：`root2d` + 站立/行走支撑 Pose；
- 下沉和伸手：`fullbody` 或 RootEffect 的 Root2D 支撑 Pose；
- 抓取：`root_effect`，其中 `root2d.pose` 提供身体支撑，`right_hand.pose` 提供苹果
  接触目标；
- 抬起和持有：后续 FullBody 或 RootEffect Pose，确保手目标随角色继续移动。

所有 Pose、约束和生成结果都必须使用当前场景返回的安全名称和 `{track,index}`，不得
从 archive、旧 Timeline 或文件路径猜测引用。
