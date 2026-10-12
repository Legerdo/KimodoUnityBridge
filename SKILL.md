---
name: kimodo-unity-bridge
description: Install KimodoUnityBridge and route Unity animation work through its cooperating capability tools.
---

# KimodoUnityBridge

根 Skill 负责安装门槛、任务入口、命令调用方式、工具编排和统一结果；公共规则见 `tools/common.md`，具体命令参数以实时 schema 为准。

本文件及同目录 `tools/*.md` 是已导入 `com.unity.kimodo_unity_motion_tools` package 的唯一权威 Skill 文档。使用项目入口时，必须先定位该 package 根目录，再按相对路径读取这里的文档；不得使用外部复制的旧工具文档。

## Public command entry / 公共命令入口

CLI、`exec_editor_script` 和其他外部脚本只使用下面的公共 Editor 入口：

```csharp
using KimodoUnityBridge.Command;

string schema = command_dispatcher.GetCommandDefinitionsJson();
string result = command_dispatcher.Invoke(commandName, argumentsJson);
```

调用顺序固定为：

1. 读取 `GetCommandDefinitionsJson()`，或调用 `kimodo_help` 获取实时命令和 schema。
2. 使用 schema 中的协议命令名，例如 `kimodo_generate_animation`、`scene.context`。
3. 将 JSON 参数传给 `command_dispatcher.Invoke(commandName, argumentsJson)`。
4. 对 JSON 返回值检查 `ok`、`status`、`error` 和 `request_id`。

下面这些名称不是 CLI 的命令入口，不要用它们做命令索引或通过反射调用：

- `command_context` 的内部 Handler，例如 `GenerateAnimationAsset`；
- `command_kimodo` 的便利包装方法；
- `KimodoBridgeService.GenerateAsync`、`KimodoBridgeCommand.ExecuteAsync`；
- `KimodoEditorGeneratePipeline.ExecuteAsync` 及其他内部 Editor 管线方法。

`command_kimodo`、Bridge service 和 Editor pipeline 可以供包内 C# 代码使用，但不替代命令目录，也不保证覆盖全部协议命令。CLI 的稳定标识是 schema 中的命令名，不是 C# 方法名。

能力文档中的 `kimodo_generate_animation(args)`、`scene.context(args)` 等写法是协议命令的伪代码表示，不是可直接索引的 C# 方法。若当前环境没有原生能力工具，必须通过 `exec_editor_script` 创建 C# 脚本，并在脚本中调用 `command_dispatcher.Invoke`。

```pseudo
# Shared states / 公共状态
#define YES             1
#define NO              0
#define UNKNOWN         -1
#define NOT_APPLICABLE  -2

WORKFLOW_READY = UNKNOWN
```

## Installation gate / 安装门槛

```pseudo
PACKAGE_IMPORTED = project_contains_package()
UNITY_READY      = unity_import_and_compile_completed()
FIRST_USE        = project_runtime_has_never_been_initialized()
UPGRADE          = package_version_changed()
RECOVERY         = runtime_diagnostics_report_missing_or_broken_components()

if PACKAGE_IMPORTED != YES:
    add_package_to_target_unity_project()

wait_until(UNITY_READY == YES)

if FIRST_USE == YES or UPGRADE == YES or RECOVERY == YES:
    install = kimodo_install_server({})
    install_request_id = install.request_id
    poll kimodo_get_generation({request_id: install_request_id})
        until status in {"done", "error"}
    ASSERT status == "done"

# 普通编译、导入或 Editor 重启不重复安装。
WORKFLOW_READY = (
    PACKAGE_IMPORTED == YES
    and UNITY_READY == YES
    and required_project_components_available() == YES
)

ASSERT WORKFLOW_READY == YES before animation work
```

## Capability tools / 能力工具

工具按能力协作，不按单个命令拆分：

| Tool | 责任 | 入口文档 |
|---|---|---|
| common | 场景上下文、证据、状态和报告公共规则 | `tools/common.md` |
| scene-context | 记录场景解析与证据边界 | `tools/scene-context.md` |
| generation | 生成、轮询、取消，以及 Retarget 派生结果 | `tools/generation.md` |
| recognition | 根据明确语义识别动作 | `tools/recognition.md` |
| comparison | 在相同证据条件下比较两个候选 | `tools/comparison.md` |
| pose | 创建、编辑和复用 External Pose | `tools/pose.md` |
| constraints | 点约束格式、Pose 使用规则、同帧冲突和效应器预检 | `tools/constraints.md` |

命令目录只在调用边界使用；不要把完整参数结构复制到工具文档中。能力工具负责流程和证据，`Command/help.json` 与实时 `kimodo_help` 负责命令名、参数和返回结构。

## Orchestration loop / 总编排 Loop

动画任务固定经过“分析提示词 → 构造约束 → 生成动画 → 分析/比较 → 重新构造约束”的
闭环。第一轮没有参考 Clip 时，分析提示词阶段只解析用户明确写出的动作、阶段、方向、
接触和结束语义；不能凭经验补写未声明的动作。若请求涉及已有动作，先调用
`animation_analyze` 分析一个明确的 `character + clip`，再把结构化结果转成候选时序和约束。

分析提示词前必须先调用 `scene.context`。该命令返回的 context JSON 是本次 AI 分析的
唯一场景事实来源：角色、Clip、Timeline 区间、Animator 和对象引用只能使用 JSON 中
出现的值。不要读取内部 session/context 文件，也不要从保存路径猜测场景对象；若 JSON
没有足够事实，应报告需要补充场景信息。

```pseudo
function run_kimodo_task(request):
    intent = analyze_prompt_semantics(request)
    ensure_installation_gate()

    best = NONE
    no_improvement = 0
    repeat:
        # 1. 分析提示词：先确定动作阶段和物理可行的时间预算。
        # duration_frames 是唯一对外时间单位；没有用户时长时，依据阶段数量、位移
        # 和接触事件给出保守预算，并检查平均速度/加速度是否可实现。
        plan = infer_timing_and_feasibility(intent, request.reference_clip)
        # 2. 构造约束：只把用户明确要求或已确认的分析证据转换为约束。
        constraints, confirmed_poses = build_confirmed_constraints(plan, request)
        generated = generate_animation(request, duration_frames=plan.duration_frames,
                                       constraints=constraints, poses=confirmed_poses)
        if generated.status != "completed":
            return unified_report(generated)

        # 3. 生成后的首次检查自动执行一次 animation_analyze。
        analysis = analyze_generated_clip(generated)
        if best == NONE:
            best = generated
            comparison = NOT_APPLICABLE
        else:
            # 4. 有候选时在相同证据条件下逐个分析并比较。
            comparison = compare_candidates(generated, best, request)
            if comparison.improves_best == YES:
                best = generated
                no_improvement = 0
            else:
                no_improvement += 1

        if analysis_is_acceptable(analysis, request) or no_improvement >= request.max_no_improvement:
            return unified_report(best, analysis, comparison)
        # 5. 只根据分析/比较中确认的缺陷重构下一轮约束和时长；不把整份分析
        # 自动塞回 generation，也不把自由文本当成协议字段。
        intent, request = revise_plan_from_analysis(analysis, comparison, intent, request)
```

`animation_analyze` 输入使用恰好一个元素的 `clips` 数组，返回单个 `clip`，不是 `clips[]` 输出；时间字段使用全局 60 FPS 帧：
`start_timeline_frame_60`、`end_timeline_frame_60_exclusive`、
`anchor_timeline_frame_60` 和 `timeline_frame_60`。`pose_get` 只能接受全局
`timeline_frame_60`，或无时间的 `scene_pose`／`skeleton_rest_pose`；要采样 Clip 内某一帧，
先用 Analysis 的 Clip 起始帧加上局部偏移。`clip.root_trajectory.path` 只在生成时形成
起点和终点 Root2D 约束；关键帧 Pose 必须先经 Pose loop 明确确认。分析输出中的
`analysis_option` 是 generation-time 的轻量关键帧/脚接触分析开关，不是完整
`animation_analyze`，不参与最终语义判断。

路由至少覆盖 installation、scene context、generation、recognition、comparison、pose 和 retarget；未匹配到能力时才返回 `unsupported` 或 `needs_clarification`。

## Shared execution rules / 公共执行规则

- 只使用运行时返回的安全角色名、动画名和 `{track,index}` 引用；生成结果的 `path` 是资产元数据，不是场景 Clip handle。
- 不得把伪代码中的能力名称、C# Handler 名称或 Editor pipeline 类型当作 CLI 命令名；命令名必须来自实时 schema。
- 所有命令都以当前活动场景中用户打开/选中的 `Animator` 所属角色为权威来源；不要从保存的 prefab 路径猜测替代场景对象。
- 只要生成请求点名动作（例如 walk/run），生成前必须检查当前场景中是否已有语义匹配的角色/动画；修复、改进、替换、续作或变体请求绝不能跳过这一步。请求明确要求沿用、复刻或延续参考 Clip 的路线时，分析后必须传入 `clip.root_trajectory.path` 作为 `root_path`；仅发现同语义 Clip 不代表用户要求复用路径。明确距离/方向的移动任务应使用相应的 path 参数或已知点约束，不能只把它写进 prompt。
- 视觉结论必须建立在实际打开的返回图像上；静态证据不足时不得报告视觉通过。
- 已完成 Clip 不覆盖；修正、Retarget 和生成变体均追加派生 Clip。
- 安装和生成都返回 `request_id`，并通过 `kimodo_get_generation` 轮询。安装终态为 `done` 或 `error`；生成终态为 `completed`、`failed` 或 `canceled`。轮询必须有固定间隔和总超时；过期或未知请求按失败/未验证报告。
- assembly reload、Editor 退出、切换场景或进入 Play Mode 导致的取消必须如实报告，不声称自动恢复。
- 工具报告至少包含 `result`、`output`、`criteria`、`evidence`、`unverified`；工具可附加专属字段。
- 请求未声明的 source、target、range、path 或 constraint 不得自行补全；按约束格式决定 `pose` 是否需要提供。

需要 Unity 对象细节时，使用命令返回的路径、角色名、动画名或 `{track,index}` 引用；不读取内部上下文文件。
