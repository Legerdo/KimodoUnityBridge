---
name: kimodo-common
description: Shared execution contract for all Kimodo capability tools.
---

# Common tool contract / 公共工具契约

所有工具都遵守同一套输入、证据、状态和报告规则。工具只实现自身能力，不重复定义安装流程。

## Command execution / 命令执行

能力文档中的命令写法是协议层伪代码。实际 CLI 或 `exec_editor_script` 调用必须经过 `KimodoUnityBridge.Command.command_dispatcher`：

```csharp
using Newtonsoft.Json.Linq;
using KimodoUnityBridge.Command;

string schema = command_dispatcher.GetCommandDefinitionsJson();
string help = command_dispatcher.Invoke(
    "kimodo_help",
    new JObject { ["command"] = "kimodo_generate_animation" }.ToString());
string response = command_dispatcher.Invoke(
    "kimodo_generate_animation",
    argumentsJson);
```

命令发现和调用规则：

- 先读实时 schema 或 `kimodo_help`，再选择命令和参数。
- 只使用 schema 的协议命令名；不要搜索 `GenerateAnimationAsset` 等内部方法名。
- `command_kimodo` 是可选的包内便利 facade，不是命令目录，也不是 CLI 发现入口。
- 公开 Bridge API 只覆盖后端生成；需要完整 Editor 资产流程时仍使用对应的协议命令，除非包内已有明确公开 facade。
- 找不到原生能力工具时，报告工具缺失并改用 `exec_editor_script` + `command_dispatcher.Invoke`；不得猜测不存在的独立 CLI 方法。

最小错误诊断：

1. `unknown_command`：命令名不在实时 schema 中，重新读取 `GetCommandDefinitionsJson()`。
2. `invalid_argument`：命令已路由成功，按 `kimodo_help(command=...)` 修正 JSON。
3. 没有 `request_id`：不要开始轮询；先报告命令未接受或返回结构不符合协议。
4. 任务已有 `request_id` 但未终态：固定间隔调用 `kimodo_get_generation`，不要重复提交同一请求。

## Input and scene context

### Parameter tiers / 参数分层

- **Core**：只传完成任务所需的明确对象、范围和动作语义。
- **Advanced**：带有 `x-kimodo-advanced: true` 的参数只在需要覆盖默认值、选择模型/输出或施加路径与 Pose 约束时传入。
- Commands resolve inputs from the active scene; returned names, paths, and pose references are the public handles. Character handles identify the source scene character (`role: "scene"`), which is also the object bound by the Timeline preview.
- `animation_analyze` 只接受一个 Clip，默认生成固定 16:9 的 20-tile composite（宽度由 resolution 决定）；比较多个候选时分别调用分析。
- 需要单张图片时传 `picture: {output: "tile", tile_index: N}`；`tile_index` 从 1 开始，`resolution` 在该模式表示高度，默认 720px，宽度按 4:3 自适应。
- `kimodo_generate_animation` 的普通调用只需要 `character` 与 `prompt`；模型、采样、命名、存储和分析覆盖项均可省略。
- generation 的 point constraints 不使用 `type` 标签；根据 `pose`、`root2d` 和 `effect`
  字段推断约束通道并统一转换为内部 `mix`，遵守 [`constraints.md`](constraints.md)。
- 每次接受生成都会带有 Editor 层共享的非阻断 `check`：检查 `root_trajectory`、
  `constraint_overlap` 和 `effectors`。速度阈值为 7 m/s，加速度阈值为 5 m/s²；warning 不阻止任务。

- 只使用用户明确提供的 source、target、range、pose、path 和 constraint。
- 只要生成请求指定了动作，先检查当前活动场景中的语义匹配动画；“修复/改进/替换/续作/变体 + 指定动作”绝不能跳过这一步。检查结果要记录为上下文证据，不能把发现的动画自动升级为生成约束。
- 输入按需绑定当前活动场景中的 `Animator` 或可渲染 Mesh；角色来源不得从保存的 prefab 路径猜测替代。
- 后续命令只使用运行时返回的安全名称和 `{track,index}` 引用。
- `scene.context` provides Session identity and a compact history index. Retain `animation_id`, `request_id`, and `analysis_id` to distinguish work across iterations; use `analysis_path`/`picture_paths` as evidence references and avoid copying full payloads into task state.
- 场景切换、Editor reload 或进入 Play Mode 可能取消活动生成；报告中必须保留该副作用。

## Evidence

- `animation_analyze` 的图片必须实际打开后才能用于视觉判断。
- 数值、文件名、标签和选帧数量只能辅助判断，不能替代视觉证据。
- 静态图片不能证明播放连续性、滑步、跳变、加速度或速度连续性。
- 必要证据缺失时返回 `not_verified` 或 `insufficient_evidence`，不得猜测为通过。

### Analysis handoff / 分析交接边界

`animation_analyze` 的证据交给下一个环节时，必须分成两类，不能互相替代：

- **图像识别**：`trajectory_shape`、`action_semantics`、
  `visual_motion_quality`、`contact_pattern`。每项必须实际打开对应 tile，
  并返回 `value`、`confidence`、`evidence`。
- **结构化分析**：`loop_endpoint`、`keyframe_heading`、
  `trajectory_turn`、`trajectory_length`。数值和布尔值只能读取返回的
  `motion_profile`（`phase_anchor_heading_consistent` 优先），不能从像素估算；字段缺失时返回 `UNKNOWN`。

没有时间序列或播放采样时，`playback_continuity`、`velocity_smoothness`、
`acceleration_quality` 固定为 `UNKNOWN`。`confidence` 是证据可靠度，不是
动作质量分数；任何必需项为 `UNKNOWN`、`CONFLICT` 或低置信度时，交接状态不得
报告为 `verified`。

### Phase track / 连续阶段轨道

Humanoid analysis 使用 `analysis_schema_version=5-command-60-single-clip-v1`，
并在 `clip.phase_track` 中返回 `phase_track_version=1-temporal-cluster-v2-command-60`。
图片的 `render_version` 以实时返回值为准；识别/比较必须记录它，版本不匹配时返回
`UNKNOWN` 并要求更新提示词。`animation_analyze` 的输入保留 `clips` 数组，但必须恰好
包含一个 `{character, clip}`；返回一个 `clip` 对象，不再使用 `clips[]` 输出。旧的均分关键帧列表不再是阶段证据；
`phase_track` 是唯一阶段来源。每项必须覆盖连续帧区间，区间首尾相接、无重叠、无空洞，
并使用全局 60 FPS 帧：`start_timeline_frame_60`、`end_timeline_frame_60_exclusive`、
`anchor_timeline_frame_60`、`kind` 和 `confidence`。`duration_frames` 可以作为区间长度，
但不要把秒值或局部 `start_frame`/`end_frame` 当作公开协议字段。`kind` 只能是 `phase` 或
`transition`；聚类器不得直接命名 `walk`、`wave` 等语义。Mesh analysis 对 `phase_track`
返回空数组，并通过 `analysis_mode=mesh` 表示阶段语义不适用。

比较多个目标语义时，按用户给出的优先级计算时长先验：第 `i` 个语义的权重为
`w_i = 1/(i*i)`，目标占比为 `p_i = w_i / sum(w_j)`。比较使用实际阶段覆盖
时长与该先验的偏差；不把权重相加做总质量分数。允许语义覆盖重叠（例如边走边挥手），
但 `idle`/站立背景阶段的总覆盖不得超过 50%，除非请求明确要求长时间 idle。

Evaluation has two separate outputs:

```text
constraint_compliance:
  loop, heading, path, distance, transition, pose, contact, ending
semantic_pose_coverage:
  required semantic poses present, expressed, and non-conflicting
```

For ordered semantic requirements, the duration prior remains `w_i = 1/(i*i)`.
Required semantic Pose coverage is evaluated separately: more verified required
semantic Poses expressed without conflict receives higher coverage. This is
evaluation evidence, not confidence, and never converts an analysis candidate
into a generation Pose. Idle/background is invalid when it contains a
conflicting action, such as sitting while the requested semantic is standing.

交接结果的最小外壳为：

```json
{
  "status": "verified|needs_review|not_verified",
  "analysis_handoff": {
    "<task>": {
      "value": "<fixed enum or number>",
      "confidence": "high|medium|low",
      "evidence": ["<tile id or structured field>"]
    }
  }
}
```

## Async tasks

- `kimodo_install_server` 与 `kimodo_generate_animation` 都返回 `request_id`；保存它并按固定间隔轮询 `kimodo_get_generation`。
- 安装和生成都使用 `accepted`、`running`、`completed`、`failed` 状态；生成任务还可能为 `canceled`。安装任务当前不能取消，生成任务可用 `kimodo_cancel_generation` 取消。
- 安装受理只返回 `request_id` 和 `status`；生成受理返回这两个字段以及非阻断 `check` 和可选 `warnings`，不会重复回显模型或约束参数。
- 每次状态查询以 `progress_current`、`progress_total`、`eta_seconds` 和 `message` 为进度依据；后端原始进度只在 `backend_progress` 中保留，不自行重新估算剩余时间。
- `kimodo_cancel_generation` 只返回取消状态、简短消息和必要的失败信息；要查看完整进度或生成诊断时，再按 `request_id` 调用 `kimodo_get_generation`。
- 命令失败使用 `ok=false` 和 `error={code,message,details?}`；异步任务失败仍是成功的状态查询响应（`ok=true`），任务状态为 `failed` 并携带同样的 `error` 结构。
- `kimodo_cancel_generation` 只适用于生成任务，因此不接受安装任务的 `install:` request_id。
- 超时、过期 request 或未知状态按未验证/失败报告，不无限重试。
- assembly reload、Editor 退出、切换场景和进入 Play Mode 导致的取消必须如实保留。
- 自动修正最多一次；修正结果是新的派生 Clip，原完成结果必须保留。

## Handles and paths

- 安全角色名和动画名是当前场景上下文的引用。
- `animation_analyze` 返回的 `clip.root_trajectory.path` 是可复用的 `{track,index}` Root Path
  引用；它只在生成约束的起始帧和结束帧生成 Root2D 约束。普通关键帧 Pose 仍必须来自
  `pose_get`，不能把分析结果自动当成生成约束。
- `kimodo_get_generation` 完成后返回一个 `clip` 对象；其中 `asset_path` 是 Unity 资产元数据，不是场景 Clip handle。若返回中没有派生资产路径，不自行推断。

## Report envelope

每个工具返回统一的最小外壳：

```text
{
  ok,
  error?,
  ...domain_fields
}
```

命令目录的 `outputSchema` 描述各命令的领域字段。命令失败时 `ok=false` 并返回
`error={code,message,details?}`；异步任务本身仍以 `ok=true` 返回状态，任务失败时状态为
`failed` 并带有同样的 `error` 对象。工具编排层可以在更外层增加 `result`、`evidence` 和
`unverified` 报告字段，但这些不是 Command 返回协议的一部分。

`animation_analyze` 的版本号只出现在响应顶层；Clip 时间范围统一使用
`start_timeline_frame_60` 和 `end_timeline_frame_60_exclusive`。图片请求仍通过输入参数
`picture` 提交，返回的渲染结果统一放在 `pictures`。
