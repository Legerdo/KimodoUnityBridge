---
name: kimodo-pose
description: Create and edit External Pose slots for explicit Humanoid pose constraints.
---

# Pose tool / Pose 工具

负责 `pose_get`、`pose_set` 以及按需拆分使用的 `pose_set_root_transform` 和 `pose_set_muscle`。

## Pose loop / Pose 闭环

Analysis candidates are not generation inputs. They first pass through the
implementation's pose conversion or materialization step, then inspection and
explicit selection:

```text
AnalysisCandidate -> PoseCandidate -> inspect -> confirmed|disabled
                  -> ConfirmedPoseSet -> generation constraints
```

Pose sources may be combined. A source can provide a whole pose, a body region,
or a contact relationship. Record its role and scope instead of copying an
entire source pose implicitly:

```json
{
  "pose": { "track": "<returned track>", "index": 0 },
  "role": "start|contact|impact|end|loop_seam",
  "scope": ["fullbody", "left_hand", "right_hand", "root2d"],
  "status": "candidate|confirmed|disabled",
  "evidence": ["tile:<id>"]
}
```

Adding, editing, or disabling a Pose creates a derived PoseSet for the next
iteration. It does not mutate the previous Clip. Only `confirmed` Pose
references may be passed to generation, and only when the request explicitly
asks to use them.

`transform_capture` 接受 `pose_get` 返回的 `pose: {track,index}`。在 `transforms`
中使用特殊 key `@pose` 表示该 Pose 的角色，`@pose/<相对骨骼路径>` 表示该角色局部；
同一数组中加入桌子、苹果等场景层级路径即可在真实世界坐标下合拍。返回四视图
`image_path`、Pose 来源和 `evaluated_joints_world`，截图不会移动原角色，渲染可见性会恢复。
该命令不接受 `frames`；要检查动画指定帧，先 `pose_get`，再用返回的 Pose 引用截图。

```json
{
  "pose": { "track": "<pose_get 返回的 track>", "index": 0 },
  "transforms": ["@pose", "Desk", "Desk/Apple"],
  "resolution": 768
}
```

- 仅对当前场景上下文中明确指定的 Humanoid Clip 采样。
- 只复用运行时返回的 `{track,index}` Pose 引用。
- 编辑命令都创建派生 Pose，不覆盖已存在的 Pose。
- Pose 引用只有在用户明确要求约束时才传入 generation；不能把分析选帧自动当作约束。

`pose_get` 只支持一个绝对的 Timeline-global 60 FPS 帧，或两个无时间的读取模式：

```json
{ "source": { "character": "<character>", "timeline_frame_60": 90 } }
```

```json
{ "source": { "character": "<character>", "mode": "scene_pose" } }
```

```json
{ "source": { "character": "<character>", "mode": "skeleton_rest_pose" } }
```

三种模式互斥。`scene_pose` 读取当前场景 Animator 的原始姿态，
`skeleton_rest_pose` 读取 Avatar 保存的骨架静止姿态；它们不接受时间字段。
如果要取某个 Clip 的某一帧，先用 `animation_analyze` 取得返回 `clip` 的
`start_timeline_frame_60`，再加上 Clip 内的帧偏移后调用 `pose_get`。命令不再接受
`timeline_time_seconds`、`clip_time_seconds` 或 `scene_time_seconds`，也不接受局部坐标系
的时间寻址。约束如何使用返回的 `{track,index}`，见 [`constraints.md`](constraints.md)。
`root2d` 与带 `effect` 的约束的 Pose 可省略；仅使用 FullBody 通道时需要显式 Pose。写入末端目标前，
提交生成前由统一的非阻断 generate check 检查其可达性。

生成约束时，只将已确认的 `{track,index}` 传入请求。三种约束的完整协议见
[`constraints.md`](constraints.md)。

`pose_set` 可在一次调用中同时更新 root 和 muscles，至少提供其中一项。输出至少包含新的 `{track,index}`、来源角色/Clip/全局帧，以及未验证项目。

`pose_set` 也支持 `effector` 对象，用于更新末端执行器目标。键名为 `left_hand`、`right_hand`、`left_foot`、`right_foot`，每个值至少包含一个 `position: [x,y,z]` 或 `rotation: [x,y,z,w]`；只提交的通道会覆盖原值，并标记对应的有效性。例如：

```json
{
  "pose": { "track": "Character Pose", "index": 12 },
  "effector": {
    "left_hand": { "position": [0.2, 1.1, 0.4] },
    "right_foot": { "rotation": [0, 0, 0, 1] }
  }
}
```
