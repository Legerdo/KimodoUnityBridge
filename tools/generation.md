---
name: kimodo-animation-generation
description: Generate, verify, and derive Unity animation Clips from explicit motion requests.
---

# Generation tool / Generation 工具

## Timeline In/Out sampling / Timeline 衔接采样

Kimodo Timeline clips expose `In Window Frames`, `In Sample Count`, `Out Window Frames`, and `Out Sample Count` when the corresponding In/Out toggle is enabled. These frame counts use the selected model's FPS (Kimodo: 30 FPS), not the command time base of 60 FPS. Existing clips default to one frame and one sample. C# samples the evaluated Timeline, including clip speed, blending and offsets, and exports the poses through the existing FullBody protocol. ARDY retains its existing single-frame boundary and history path; these new controls apply to Kimodo.

Inside samples the current clip's opening/closing windows. Outside samples the previous clip's tail and the next clip's beginning. Outside context extends the backend request and is trimmed from the result, keeping the requested output duration and avoiding replay of the previous tail. For example, a 60-frame output with a 7-frame In window and a 4-frame Out window requests 71 frames and keeps frames `[7, 67)`. With three samples per window, constraints land at `[0, 3, 6]` and `[67, 69, 70]`. In connected generation, internal In windows constrain the preceding portion of the aggregate; only the group's outer context extends its duration.

Samples are evenly spaced on integer model frames; two or more samples include both endpoints, while one sample keeps the pose nearest the seam. Short source ranges limit the effective window and sample count. Overlapping Inside windows are rejected rather than silently overriding poses. `Show Constraint` displays the actual samples on a selected clip (In: blue; Out: orange); `Refresh` resamples them. Export applies one shared world-to-track transform to all samples, preserving relative motion.

Generation uses the actual `KimodoPlayableClip` Timeline; it is not the final playback result. Generation completion alone does not establish visual seam quality: verify consecutive playback of the resulting clips, including root motion and foot contacts.

## Decision program / 决策程序

Command generation also accepts one standalone `constraints[].inout` entry. Read `kimodo_help(command="kimodo_generate_animation")` for its closed schema and examples. Each enabled `in`/`out` explicitly names `source.clip`; `source.character` defaults to the generation character and must match it. Sources are completed scene Clips; never infer a neighbor from scene layout. `source.frame` is optional and relative to the Clip's played range, including its speed and trim.

Command `window_frames` and `source.frame` use **60 FPS**, unlike the Timeline UI controls above. C# converts a window with `ceil(window_frames * model_fps / 60)` (minimum one model frame), samples complete poses including Timeline transforms, then uses the shared Character → Track → Muscle → Kimodo export. `outside` (default) samples the In source tail and Out source head as extra context and crops it off; `inside` samples the source head/tail into the output's first/last windows. Sources may be explicitly chosen from any completed Clip on the same character; they do not need to neighbor the output in the scene context.

### RootEffect constraints

The complete point-constraint contract is defined in [`constraints.md`](constraints.md).
The sections below summarize the generation-specific behavior.

Point constraints do not accept a `type` field. The command parser infers the concrete
family from the supplied `pose`, `root2d`, and `effect` fields: a Pose selects FullBody,
Root2D supplies planar root channels, and effect supplies named end-effector targets;
multiple fields at one frame are composed by channel. Duplicate constraint frames and
root_path collisions are rejected at the command boundary.

The three point types are a discriminated union and are never nested. See `constraints.md`
for the complete shape and validation rules.

This lets a pickup contact frame express "the character is here and the hand is here" as one constraint sample.

### Root2D point constraints and support Pose

The optional Reference Pose model, per-type schema, command-boundary duplicate checks, and
runtime/raw composition rules are defined in [`constraints.md`](constraints.md).
需要指定支撑姿态时，在目标时间调用 `pose_get`，检查后把 Pose 引用放入 Root2D 约束。
### Generation warning check

每次生成接受后都会返回非阻断的 `check`。检查由 Editor 层共享服务执行，因此 Timeline、命令和其他 generator 使用同一套规则：`root_trajectory` 检查 Root2D 的 XZ/heading 与 FullBody 的 root（含可用 Y）连续性，`constraint_overlap` 检查同帧 root 描述冲突，`effectors` 检查 RootEffect 以及 FullBody 中有效的手脚目标。速度超过 7 m/s、加速度超过 5 m/s² 或约束之间出现跳变时，状态为 `warning`，任务仍会继续。`blocking` 仅用于协议级阻断，warning 不会设置为 true。

Command In/Out rejects insufficient source ranges, too many samples for distinct model frames, overlapping inside windows, same-frame explicit constraints, cross-character sources, loop generation, ARDY, and requests exceeding the model frame limit including context. These checks do not silently shorten or downgrade the request. Acceptance and `kimodo_get_generation` return `inout_sampling`: source-local and Timeline-global 60 FPS sample frames, output/runtime model frames, padding counts, and the exclusive model-frame crop range. The output retains the requested duration. This metadata verifies sampling and transport, not visual seam quality. Loop is enabled only by the explicit `loop` argument; prompt wording is never parsed as a loop flag.

Every command's help entry includes `examples`. Replace illustrative names and request IDs with actual returned handles before executing them.

```pseudo
#define YES             1
#define NO              0
#define UNKNOWN        -1
#define NOT_APPLICABLE -2

#define RESULT_PASSED       "passed"
#define RESULT_NOT_VERIFIED "not_verified"
#define RESULT_NEEDS_REVISION "needs_revision"
#define RESULT_FAILED       "failed"

// Fill these macros only from the request and established project state.
// 这些宏只能根据请求和已确认的项目状态填写。
#define REQUEST_IS_RANGE_OPERATION      <YES|NO>
#define REQUEST_IS_RETARGET_ONLY        <YES|NO>
#define HAS_SOURCE_ANIMATION            <YES|NO>
#define SHOULD_LOOP                     <YES|NO>
#define SHOULD_REUSE_ANALYZED_PATH       <YES|NO>
#define SHOULD_OVERRIDE_PATH_DIRECTIONS <YES|NO>
#define SHOULD_OVERRIDE_FIXED_HEADING   <YES|NO>
#define NEED_FULLBODY_POSE              <YES|NO>
#define NEED_ROOT2D_POSE                <YES|NO>
#define NEED_LEFT_HAND_POSE             <YES|NO>
#define NEED_RIGHT_HAND_POSE            <YES|NO>
#define NEED_LEFT_FOOT_POSE             <YES|NO>
#define NEED_RIGHT_FOOT_POSE            <YES|NO>
#define SHOULD_RETARGET_AFTER_GENERATION <YES|NO>
#define ALLOW_DIRECT_POSE_EDIT           NO

CHARACTER       = REQUIRED("<safe character / 安全角色名>")
SOURCE_CLIP     = REQUIRED_IF(
    HAS_SOURCE_ANIMATION or REQUEST_IS_RETARGET_ONLY,
    "<safe clip / 安全动画名>"
)
TARGET_CHARACTER = REQUIRED_IF(
    REQUEST_IS_RETARGET_ONLY or SHOULD_RETARGET_AFTER_GENERATION,
    "<safe target character / 安全目标角色名>"
)

#define ACTION_REQUIRED not (
    REQUEST_IS_RANGE_OPERATION or REQUEST_IS_RETARGET_ONLY
)

ACTION                 = REQUIRED_IF(ACTION_REQUIRED, "<main action / 主动作>")
START_STATE            = OPTIONAL("<start state / 起始状态>")
PHASE                  = OPTIONAL("<phase / 阶段>")
DIRECTION_OR_PATH      = OPTIONAL("<direction or path / 方向或路径>")
SPEED_OR_ENERGY        = OPTIONAL("<speed or energy / 速度或能量>")
BODY_OR_CONTACT        = OPTIONAL("<body or contact / 身体或接触>")
ENDING_OR_LOOP         = OPTIONAL("<ending or loop / 结束或循环>")
STYLE                  = OPTIONAL("<relevant style / 相关风格>")

DURATION_FRAMES        = REQUIRED("<planned total duration in 60 FPS frames / 计划总时长>")
PATH_BEGIN_YAW_DEGREES = REQUIRED_IF(SHOULD_OVERRIDE_PATH_DIRECTIONS, "<absolute Unity yaw>")
PATH_END_YAW_DEGREES   = REQUIRED_IF(SHOULD_OVERRIDE_PATH_DIRECTIONS, "<absolute Unity yaw>")
FIXED_HEADING_DEGREES  = REQUIRED_IF(SHOULD_OVERRIDE_FIXED_HEADING, "<absolute Unity yaw>")
EXPLICIT_CONSTRAINT_FRAMES = OPTIONAL("<frame list / 约束帧列表>")

#define MAX_AUTOMATIC_CORRECTIONS 1
CORRECTION_ATTEMPTS = 0

#define GENERATION_COMPLETED UNKNOWN
#define VISUAL_OPENED        UNKNOWN
#define ACTION_MATCH         UNKNOWN
#define LOOP_MATCH           UNKNOWN
#define PATH_MATCH           UNKNOWN
#define HEADING_MATCH        UNKNOWN
#define POSE_MATCH           UNKNOWN
#define CONTACT_MATCH        UNKNOWN
#define ENDING_MATCH         UNKNOWN
LAST_COMPLETED_SEED = UNKNOWN

GENERATION_PROMPT = JOIN_EXPLICIT_FIELDS(
    start_state=START_STATE,
    action=ACTION,
    phase=PHASE,
    direction_or_path=DIRECTION_OR_PATH,
    speed_or_energy=SPEED_OR_ENERGY,
    body_or_contact=BODY_OR_CONTACT,
    ending_or_loop=ENDING_OR_LOOP,
    style=STYLE,
)

# JOIN_EXPLICIT_FIELDS preserves the supplied token text and order, joining
# only non-empty fields with natural punctuation; omitted fields are absent.
# Only fields explicitly supplied by the caller are emitted. Do not infer
# preparation/recovery structure, breathing, contacts, root displacement, or
# other unstated semantics; preserve unknown abbreviations verbatim.

## Prompt analysis and physical timing / 提示词语义与物理时序

生成前先把用户提示词拆成已明确要求的动作阶段、先后顺序、接触/目标、方向、位移、
速度意图和结束状态。可以为这些已声明阶段推导时间预算，但不得增添用户没说的动作。
随后把明确的路径、姿态或接触转成命令约束，生成 Clip；生成完成后必须调用一次完整
`animation_analyze`；有参考 Clip 或已选最佳候选时，再用 Comparison 做同条件比较。
根据分析中确认的缺陷调整提示词、时长或约束，进入下一轮。

- 对外时间单位统一为 60 FPS 的 `duration_frames`。用户给出秒数时按
  `round(seconds * 60)` 转帧；用户未给时长时，根据动作阶段数、起止状态、位移和接触
  事件规划总时长，并把计划值传给 `kimodo_generate_animation`，不要无意中依赖默认
  `300` 帧（5 秒）。
- 对带位移的动作先计算 `distance / duration`。例如“3 秒走 10 米”意味着平均速度约
  `3.33 m/s`；这不应被当作普通步行的默认节奏。若用户明确说“走”，应调整时长或确认
  用户实际想表达的是快走/跑；若用户明确说跑，则仍要检查起步、转向和停止所需时间。
- `kimodo_generate_animation` 返回的 `check` 中速度超过 `7 m/s` 或加速度超过
  `5 m/s²` 是非阻断 warning。它只能作为需要复核的信号；没有 warning 不能证明动作
  符合日常动作语义，也不能代替 Analysis/Compare。
- 不要只在 prompt 文本里写“沿参考路径”或“在 3 秒内走 10 米”。请求明确要求复用
  已有 Clip 的路线时，先分析该 Clip，再将返回的 `clip.root_trajectory.path` 作为
  `root_path` 约束传入。`root_path` 只在起点和终点生成 Root2D 约束，不保证逐帧复刻
  中间曲线；若中间转向/停靠位置是硬要求，必须在计划中显式处理，并在结果中核验，
  不得宣称端点约束已经锁定整条曲线。
- `analysis_option` 只控制生成阶段的轻量关键帧/脚接触分析。生成后仍必须运行完整的
  `animation_analyze`；不要把 `analysis_option` 当作完整分析或语义评分。

## Animation source discovery / 动画来源发现

当请求涉及修复、替换、参考或查找某个动作时，必须先检查场景角色的
`Animator.runtimeAnimatorController` 及其 BlendTree 实际引用的 AnimationClip，
不能先在 `KimodoGeneratedClips` 目录中按名称搜索。

```pseudo
controller_clips = clips_referenced_by_current_character_controller(CHARACTER)
source_clip = first_semantic_match(
    controller_clips,
    prefer_assets_outside = "Assets/KimodoGeneratedClips"
)

if source_clip is empty:
    source_clip = first_semantic_match(
        clips_referenced_by_other_scene_controllers(CHARACTER),
        prefer_assets_outside = "Assets/KimodoGeneratedClips"
    )

if source_clip is empty:
    source_clip = first_semantic_match(clips_under = "Assets/KimodoGeneratedClips")
```

`source: "added"` 只表示 Clip 被解析到场景上下文，不能证明它是原始动画。
Controller 中的实际 AnimationClip 引用优先；生成目录中的 Clip 只能作为后备来源。

## Context-first generation / 上下文优先生成

```pseudo
if request_names_one_or_more_actions():
    related_clips = find_semantically_matching_controller_clips_first(
        character=CHARACTER,
        actions=actions_named_by_request,
        prefer_assets_outside="Assets/KimodoGeneratedClips"
    )
    if related_clips is empty:
        related_clips = find_semantically_matching_clips_in_scene_context(
            character=CHARACTER,
            actions=actions_named_by_request,
            prefer_assets_outside="Assets/KimodoGeneratedClips"
        )
    record_context_evidence(related_clips)
    if request_semantics_include_fix_or_variant_of_named_actions() and related_clips is empty:
        record_context_evidence("No matching action clip found; this is a new baseline generation.")
    if related_clips is not empty:
        related_analysis = animation_analyze(
            clips=[{character: CHARACTER, clip: related_clips[0]}],
            picture={"output": "composite"},
            resolution=1920
        )
        source_profile = related_analysis.clip.motion_profile
        source_keyframes = related_analysis.clip.phase_track[*].anchor_timeline_frame_60
        if request_is_repair_or_variant_of_related_clip():
            derive_loop_path_and_heading_decisions_from(
                source_profile,
                selected_semantic,
                explicit_user_intent
            )
        if request_explicitly_requires_reference_or_constraints():
            build_constraints_from_analysis_trajectory_and_pathangle(related_analysis)
```

匹配到已有动画后，必须先使用 analysis 返回的 `motion_profile` 判断循环、路径和
heading，再决定是否覆盖。关键帧列表用于阶段计划、Pose 采样帧和验证证据。请求明确
要求某个参考姿态时，用 Analysis 的 Clip 起始全局帧加 Clip 内偏移，调用 `pose_get`；
检查并确认返回的 Pose 后，才把其 `{track,index}` 放入 generation 的 point constraint。
`pose_set` 只用于用户明确要求编辑 Pose 的任务。轨迹通过 `root_path` 复用，明确方向
使用 PathAngle 起止角度。若没有匹配动画，
报告未找到上下文后再按动作语义推断：向前为 PathAngle 起止 `0°`，左转终点
`-90°`，右转终点 `+90°`；仅在语义明确时启用对应覆盖。

```pseudo
analysis_frame_anchors = source_analysis.clip.phase_track[*].anchor_timeline_frame_60
verification_frames = choose_phase_and_endpoint_frames(analysis_frame_anchors)
path_constraint = source_analysis.clip.root_trajectory.path
path_angle = derive_path_angle_from_motion_profile_and_request(source_profile, request)
// Analysis anchors are metadata only. They may enter the independent Pose loop,
// but never become generation inputs without explicit conversion and
// confirmation. Path/path_angle remain generation inputs only when requested.
```

function execute_generate_skill(request):
    ASSERT not (
        REQUEST_IS_RANGE_OPERATION == YES and
        REQUEST_IS_RETARGET_ONLY == YES
    )

    context = resolve_scene_context()
    character = resolve_scene_character(CHARACTER)

    if REQUEST_IS_RANGE_OPERATION == YES:
        return unsupported("Range recording is no longer a public command; provide a source clip or generate a derived clip.")

    if REQUEST_IS_RETARGET_ONLY == YES:
        ASSERT request_explicitly_supplies_source_animation_and_target_character()
        source_clip = resolve_scene_clip(character, SOURCE_CLIP)
        target_character = resolve_scene_character(TARGET_CHARACTER)
        final_output = kimodo_retarget_animation({
            source_character: character,
            animation: source_clip,
            target_character: target_character,
            name: request.name if supplied,
            output_folder: request.output.folder if supplied
        })
        final_ref = {
            character: final_output.character,
            clip: final_output.animation.name
        }
        GENERATION_COMPLETED = YES
        return verify_final_output(
            final_ref,
            runtime_evidence = final_output
        )

    source_analysis = NOT_APPLICABLE
    if HAS_SOURCE_ANIMATION == YES:
        source_clip = resolve_scene_clip(character, SOURCE_CLIP)
        source_analysis = animation_analyze({
            clips: [{character: character, clip: source_clip}],
            picture: {"output": "composite"},
            resolution: 1920
        })
        source_image_path = source_analysis.pictures.image_path
        source_picture_map = source_analysis.pictures.images
        ASSERT OPEN_WITH_AVAILABLE_VISUAL_TOOL(source_image_path) == YES
        source_profile = source_analysis.clip.motion_profile
        source_keyframes = source_analysis.clip.phase_track[*].anchor_timeline_frame_60
        if request_is_repair_or_variant_of_source_clip():
            derive_loop_path_and_heading_decisions_from(
                source_profile,
                selected_semantic,
                explicit_user_intent
            )

    constraints = []

    if SHOULD_REUSE_ANALYZED_PATH == YES:
        ASSERT HAS_SOURCE_ANIMATION == YES
        path_ref = source_analysis.clip.root_trajectory.path
        ASSERT path_ref has exactly {track, index} returned by animation_analyze
        constraints.append({
            frame: request.path_start_frame if supplied else 0,
            root_path: {path: path_ref}
        })

    if any_pose_constraint_is_needed() == YES:
        ASSERT HAS_SOURCE_ANIMATION == YES
        source_global_frame = source_analysis.clip.start_timeline_frame_60 + source_local_frame
        pose_result = pose_get({
            source: {character: character, timeline_frame_60: source_global_frame}
        })
        inspect_and_confirm_pose(pose_result.pose)
        constraints.append({
            frame: planned_output_frame,
            pose: pose_result.pose
        })

    args = {
        character: character,
        prompt: GENERATION_PROMPT
    }

    args.duration_frames = DURATION_FRAMES

    if SHOULD_LOOP == YES:
        args.loop = {
            enabled: true,
            lock_pos: { x: false, y: true, z: false },
            lock_rot: { x: true, y: false, z: true }
        }

    if request_semantics_contain_explicit_facing_or_path_direction():
        SHOULD_OVERRIDE_PATH_DIRECTIONS = YES
        PATH_BEGIN_YAW_DEGREES = resolve_absolute_unity_yaw_from_scene_context(
            CHARACTER,
            DIRECTION_OR_PATH
        )
        PATH_END_YAW_DEGREES = PATH_BEGIN_YAW_DEGREES

    if SHOULD_OVERRIDE_PATH_DIRECTIONS == YES:
        // Supply both values deliberately; do not rely on the omitted-peer default.
        // 明确提供起点和终点，不能依赖缺省另一端为零。
        args.path = {
            start_angle: PATH_BEGIN_YAW_DEGREES,
            end_angle: PATH_END_YAW_DEGREES,
            distance: OVERRIDE_DISTANCE,
            heading: FIXED_HEADING_DEGREES,
        }

    if SHOULD_OVERRIDE_FIXED_HEADING == YES:

    if constraints is not empty:
        args.constraints = constraints

    copy_only_user_supplied_optional_fields(
        request,
        args,
        fields = [
            "generation", "output"
        ]
    )

    generation = kimodo_generate_animation(args)
    request_id = generation.request_id

    poll_interval_seconds = 2
    poll_deadline = now() + configured_generation_timeout()
    do:
        wait(poll_interval_seconds)
        if user_requests_cancellation() == YES:
            kimodo_cancel_generation({
                request_id: request_id,
                reason: request.cancellation_reason if supplied
            })
        state = kimodo_get_generation({request_id: request_id})
    until state.status in {"completed", "failed", "canceled"} or now() >= poll_deadline

    if now() >= poll_deadline and state.status not in {"completed", "failed", "canceled"}:
        return {
            result: RESULT_NOT_VERIFIED,
            output: {request_id: request_id},
            criteria: [],
            evidence: [],
            unverified: ["generation_completion"],
            runtime_warnings: {accepted: generation, last: state, reason: "poll_timeout"}
        }

    if state.status != "completed":
        GENERATION_COMPLETED = NO
        return {
            result: RESULT_FAILED,
            status: state.status,
            runtime_payload: {
                accepted: generation,
                terminal: state
            }
        }

    GENERATION_COMPLETED = YES
    LAST_COMPLETED_SEED = state.seed
    generated_ref = {
        character: character,
        clip: state.animation,
        path: state.path
    }

    runtime_payload = {
        accepted: generation,
        terminal: state
    }

    if SHOULD_RETARGET_AFTER_GENERATION == YES:
        target_character = resolve_scene_character(TARGET_CHARACTER)
        retargeted_output = kimodo_retarget_animation({
            source_character: character,
            animation: generated_ref.clip,
            target_character: target_character,
            name: request.retarget_name if supplied,
            output_folder: request.output.folder if supplied
        })
        generated_ref = {
            character: retargeted_output.character,
            clip: retargeted_output.animation.name
        }
        runtime_payload.retargeted = retargeted_output

    return verify_final_output(
        generated_ref,
        runtime_evidence = runtime_payload
    )

function verify_final_output(final_ref, runtime_evidence):
    final_analysis = animation_analyze({
        clips: [{character: final_ref.character, clip: final_ref.clip}],
        picture: {"output": "composite"},
        resolution: 1920
    })

    final_image_path = final_analysis.pictures.image_path
    final_picture_map = final_analysis.pictures.images
    VISUAL_OPENED = OPEN_WITH_AVAILABLE_VISUAL_TOOL(final_image_path)

    VERIFICATION_PROMPT = """
    Compare the generated result with the declared request intent.
    Fill ACTION_MATCH, LOOP_MATCH, PATH_MATCH, HEADING_MATCH, POSE_MATCH,
    CONTACT_MATCH, and ENDING_MATCH with YES, NO, UNKNOWN, or NOT_APPLICABLE.
    Only required intent fields participate in the final decision.
    A requested loop is not proof of a seamless boundary. The fixed default
    lock set is pos.y plus rot.x/rot.z; change an axis only when the user
    explicitly requests that axis. Verify the resulting endpoint after generation.

    将生成结果与已声明请求意图对照，只填写 YES、NO、UNKNOWN 或
    NOT_APPLICABLE。只有请求要求的项目参与最终决策；请求循环不等于
    已证明边界无缝。
    """

    verification_observations = fill_verification_macros_from(
        prompt = VERIFICATION_PROMPT,
        visual = final_image_path,
        picture_map = final_picture_map,
        structured_support = final_analysis
    )

    required_matches = [
        required(ACTION_REQUIRED, ACTION_MATCH),
        required(SHOULD_LOOP, LOOP_MATCH),
        required(
            SHOULD_REUSE_ANALYZED_PATH or SHOULD_OVERRIDE_PATH_DIRECTIONS,
            PATH_MATCH
        ),
        required(SHOULD_OVERRIDE_FIXED_HEADING, HEADING_MATCH),
        required(NEED_FULLBODY_POSE or NEED_ROOT2D_POSE, POSE_MATCH),
        required(
            NEED_LEFT_HAND_POSE or NEED_RIGHT_HAND_POSE or
            NEED_LEFT_FOOT_POSE or NEED_RIGHT_FOOT_POSE,
            CONTACT_MATCH
        ),
        required(is_present(ENDING_OR_LOOP), ENDING_MATCH)
    ]

    if GENERATION_COMPLETED != YES or VISUAL_OPENED != YES:
        return generation_report(
            RESULT_NOT_VERIFIED,
            final_ref,
            verification_observations,
            runtime_evidence
        )

    if runtime_reported_fallback_for_requested_behavior(runtime_evidence):
        return generation_report(
            RESULT_NEEDS_REVISION,
            final_ref,
            verification_observations,
            runtime_evidence
        )

    if required_matches contains UNKNOWN:
        return generation_report(
            RESULT_NOT_VERIFIED,
            final_ref,
            verification_observations,
            runtime_evidence
        )

    if required_matches contains NO:
        if CORRECTION_ATTEMPTS < MAX_AUTOMATIC_CORRECTIONS:
            correction = derive_supported_correction_from_failed_macros()
            if correction exists:
                CORRECTION_ATTEMPTS += 1
                preserve_completed_output()
                return execute_generate_skill(correction)
        return generation_report(
            RESULT_NEEDS_REVISION,
            final_ref,
            verification_observations,
            runtime_evidence
        )

    return generation_report(
        RESULT_PASSED,
        final_ref,
        verification_observations,
        runtime_evidence
    )

function generation_report(result, final_ref, observations, runtime_evidence):
    return {
        result: result,
        output: final_ref,
        criteria: required_generation_matches_only(),
        evidence: concise_observations_mapped_to_criteria(observations),
        unverified: criteria_with_UNKNOWN_evidence(),
        runtime_warnings_and_fallbacks:
            copy_only_returned_warnings_and_fallbacks(runtime_evidence)
    }

function resolve_scene_character(character_ref):
    return the unique matching character in the active scene context

function resolve_scene_clip(character_ref, clip_ref):
    return the unique matching clip already available for that scene character

function derive_supported_correction_from_failed_macros():
    if PATH_MATCH == NO and explicit_path_directions_exist():
        return same_seed_request_with(
            seed = LAST_COMPLETED_SEED,
            path = { start_angle: PATH_BEGIN_YAW_DEGREES, end_angle: PATH_END_YAW_DEGREES }
        )

    if HEADING_MATCH == NO and explicit_fixed_heading_exists():
        return same_seed_request_with(
            seed = LAST_COMPLETED_SEED,
            path.heading = FIXED_HEADING_DEGREES
        )

    if POSE_MATCH == NO or CONTACT_MATCH == NO:
        if ALLOW_DIRECT_POSE_EDIT == YES and request_explicitly_confirms_rig_side_effects():
            return request_with_explicit_pose_workflow(source_analysis)
        return no_supported_correction

    if LOOP_MATCH == NO and SHOULD_LOOP == YES:
        return request_with_loop_enabled_after_explicit_review()

    return no_supported_correction

function required(required_flag, evidence):
    return evidence if required_flag == YES else NOT_APPLICABLE

    ASSERT analysis_keyframes_are_used_as_evidence_and_timing_anchors()
ASSERT default_generation_does_not_materialize_or_edit_pose_assets()
ASSERT analysis_candidates_do_not_enter_generation_directly()
ASSERT only_confirmed_pose_set_entries_enter_generation()
ASSERT loop_flag_is_explicit_and_prompt_text_never_enables_it()
ASSERT direct_pose_edit_requires_explicit_user_confirmation_and_opt_in()
ASSERT analyzed_path_is_a_constraint_only_when_root_trajectory_path_is_explicitly_reused()
ASSERT path_override_and_loop_are_independent_explicit_options()
ASSERT default_loop_locks_are_pos_y_and_rot_xz()
ASSERT loop_lock_axes_change_only_on_explicit_user_request()
ASSERT fixed_heading_overrides_path_tangent_heading_but_not_path_positions()
ASSERT root_path_and_point_frame_collisions_are_rejected()
ASSERT duplicate_point_frames_are_rejected()
ASSERT no_source_target_range_pose_path_contact_or_constraint_is_invented()
ASSERT completed_outputs_are_preserved_and_corrections_are_derived_outputs()
ASSERT failed_canceled_or_fallback_results_are_reported_as_returned()

## Root path and path-angle constraints / Root 路径统一采样

`root_path` 与 `path.start_angle/path.end_angle` 使用同一条
稀疏 Root2D 约束管线。`root_path` 在生成约束中只产生起点和终点两个样本；
中间路径形状由生成器连续解算，不展开为每帧或自适应的中间约束。两者的区别仅在路径采样对象：

```pseudo
sample_frames = [clip_start_frame, clip_end_frame]

if ROOT_PATH:
    path_samples = sample_animationclip_root_trajectory(root_path.source_clip)
if PATH_ANGLE_OVERRIDE:
    first_pass_clip = materialize_first_pass_motion_as_transient_animationclip()
    path_samples = sample_animationclip_root_trajectory_with_angles(
        first_pass_clip,
        path.start_angle,
        path.end_angle
    )

emit_two_endpoint_root2d_constraints(path_samples, sample_frames)
```

路径约束默认只在起点和终点采样；固定 heading
覆盖是独立选项，只有显式启用时才可按其采样间隔增加帧。不得把路径默认展开为
每一帧一个 Root2D 约束。路径和姿态均应通过实际 `AnimationClip` 采样。PathAngle
的第一遍生成结果在最终 Clip 写回前，应先物化为临时 `AnimationClip`（或使用等价
的 Clip 采样器），然后与 `root_path` 走同一套采样和稀疏约束导出逻辑；不能仅凭
prompt 推断轨迹数据。

Root2D 只覆盖根节点的 XZ 和平面 heading；原动画/支撑 Pose 中的根 Y、高低、
Pitch 与 Roll 均是运动语义，必须保留，不能由路径约束静默归零或替换。

if evidence_is_static_only():
    LOOP_SEAMLESSNESS       = UNKNOWN
    PLAYBACK_CONTINUITY_MATCH = UNKNOWN
    SLIDING_MATCH           = UNKNOWN
    POPPING_MATCH           = UNKNOWN
    ACCELERATION_MATCH      = UNKNOWN
    VELOCITY_CONTINUITY_MATCH = UNKNOWN
```
