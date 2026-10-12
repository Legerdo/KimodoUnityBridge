---
name: kimodo-constraints
description: Public constraint types resolved to one canonical concrete constraint family.
---

生成命令的 point constraint 不接受 `type` 字段。`constraints[].frame` 是生成 Clip 内的 60 FPS 帧；`pose_get.source.timeline_frame_60` 则是 Timeline-global 帧，二者不可混用。命令输入中的每个 point constraint 根据 `pose`、`root2d` 和 `effect` 字段解析为 canonical sample；同一帧的 Editor/runtime pass 会在底层按 channel 合成为一个 SampleResult，之后 raw constraint 序列化再把 Root2D root 通道折叠进同帧 FullBody/EndEffector 行。

命令边界仍然可以拒绝用户显式重复的 point frame 或 root_path 碰撞，这是输入诊断规则，不代表底层 SampleResult 或 raw constraint 不能复合。内部自动采样、Timeline pass 和 authored constraint 允许同帧叠加；同一 channel 的覆盖按 creation order 的最后值生效。

```json
{
  "frame": 90,
  "pose": { "track": "Character.Poses", "index": 0 },
  "root2d": { "position": [2.4, 5.0], "heading": [0, 1] },
  "effect": { "right_hand": { "position": [2.55, 0.95, 5.35] } }
}
```

- 仅提供 `pose` 时归类为 FullBody。
- 提供 `root2d` 时必须同时提供 `position` 和 `heading`；可与 `pose` 合并。
- 提供 `effect` 时至少包含一个手/脚目标；可与 `pose`、`root2d` 合并。

`root_path` 在 `constraints` 数组中的 `frame` 是第一个路径约束帧，省略时为 0；生成器只在这个起点和输出 Clip 的最后一帧创建两个 Root2D 样本。起点、终点与已有 point constraint 重合时，命令拒绝请求。两端样本不会锁定中间每一帧的路线。

转换只改变约束模式和显式 channel mask，不会清空或重建 `rootTQ`、`leftFootTQ`、`rightFootTQ`，也不会覆盖未提供的支撑 Pose 通道。Root path 的两个生成样本归类为 Root2D。
