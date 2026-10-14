---
name: kimodo-animation-generation
description: Generate and evaluate Unity animation Clips from an explicit performance plan.
---

# Animation generation

Generation is phase 3 of the four-phase workflow in the root Skill. Intent and feasibility belong to semantic recognition; structured constraints belong to constraint construction; this file covers command submission, execution, and generated output behavior. The full post-generation evaluation belongs to phase 4 and is an AI-orchestrated `animation_analyze` call.

## Before submission

Use `scene.context` to resolve the scene character and any source Clip. For a repair, continuation, improvement, or variant, analyze the selected source first. Build the prompt around the planned action beats, transitions, orientation, contacts, and ending. Pass only confirmed path/Pose/effector constraints and the plausible `duration_frames` selected in phase 1. The public time base for `duration_frames`, constraint frames, `window_frames`, and `pose_get.source.timeline_frame_60` is 60 FPS; note that Timeline UI In/Out frame controls use the selected model's FPS (Kimodo: 30 FPS).

Do not assume prompt wording will set structured fields such as `loop`, `path`, `root_path`, or Clip In/Out constraints. Read live `kimodo_help(command="kimodo_generate_animation")` for the exact current schema.

Point constraints do not accept a `type` property. The command infers channels from `pose`, `root2d`, and `effect`; same-frame values compose by channel internally. Explicit duplicate point frames and root-path collisions are rejected. See [`constraints.md`](constraints.md) for the complete contract.

`root_path` uses a returned `{track,index}` reference and creates only start and end Root2D constraints. It does not constrain the intermediate trajectory. `root2d` requires both planar `position` and `heading`. A Pose used as a FullBody constraint must first be sampled, inspected, and confirmed through the Pose workflow.

## Timeline In/Out sampling

Timeline clips expose `In Window Frames`, `In Sample Count`, `Out Window Frames`, and `Out Sample Count` when enabled. C# samples evaluated Timeline poses, including clip speed, blending, and offsets, then exports them through the shared FullBody protocol. Existing clips default to one frame and one sample. ARDY keeps its existing single-frame boundary/history path; the expanded controls apply to Kimodo.

The command accepts a standalone `constraints[].inout` entry. Each enabled `in` or `out` explicitly names `source.clip`; `source.character` defaults to the generation character and must match it. Sources must be completed Clips on the same character; do not infer a neighbor from scene layout. `source.frame` is relative to the Clip's played range, including speed and trim.

`outside` (default) samples the In source tail and Out source head as extra context and crops that context from the output. `inside` samples the source head/tail into the output boundary windows. For a 60-frame output with 7-frame In and 4-frame Out windows, outside mode requests 71 frames and keeps `[7, 67)`. Samples are evenly spaced on integer model frames. Short source ranges limit the effective window and sample count. Insufficient ranges, duplicate sampling frames, overlapping inside windows, cross-character sources, loop generation, ARDY, and requests over the model frame limit are rejected rather than silently shortened. The completed poll response's `inout_sampling` reports source/local/global sample frames and crop metadata.

## Submit and poll

1. Call `kimodo_generate_animation` with the live-schema arguments.
2. Save its `request_id`; inspect the non-blocking `check` and optional warnings.
3. Poll `kimodo_get_generation` at a fixed interval until `completed`, `failed`, or `canceled`, subject to a bounded timeout. Do not resubmit while the original request is running.
4. On completion, retain the returned Clip handle, `animation_id` from refreshed `scene.context`, request ID, and asset path as distinct references. `asset_path` is metadata, not a scene Clip handle.
5. Refresh `scene.context` when you need the persisted session revision/history index. The generation command does not automatically run full `animation_analyze`.

The accepted generation response's `check` is a deterministic, non-blocking Editor-side warning check over root trajectory, constraint overlap, and effectors. The current speed threshold is 7 m/s and acceleration threshold is 5 m/s². A warning does not block generation; evaluate it against user intent. These checks do not decide whether an action is semantically correct or whether an unusual duration is artistically plausible.

`analysis_option` enables lightweight generation-time salient-keyframe/foot-contact analysis. It is not a full `animation_analyze` run, a visual judgment, or semantic evaluation. After a completed output, phase 4 calls full `animation_analyze` as a separate AI workflow step.

## Derived outputs and evaluation

Generation, retarget, and corrections append derived outputs; completed Clips are not overwritten. A revision should target a defect supported by phase-4 evidence, such as a missing action beat, incorrect travel, heading mismatch, contact problem, or weak ending. Preserve the previous candidate in history and explain why the next constraint or prompt changed.

Generation uses the actual `KimodoPlayableClip` Timeline. Completion alone does not establish visual quality or playback continuity. Open the analysis pictures for static visual judgments. If seam quality, foot sliding, speed continuity, or acceleration matters, inspect playback evidence; still images cannot establish those properties.
