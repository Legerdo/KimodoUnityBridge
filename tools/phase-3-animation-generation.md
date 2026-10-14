---
name: kimodo-phase-3-animation-generation
description: Submit, monitor, and preserve a Kimodo animation generation request.
---

# Phase 3: Animation generation

Submit only the performance plan and constraints prepared by phases 1 and 2. The Command executes the request and records its lifecycle; the Skill remains responsible for the semantic plan and the decision to revise.

## Prepare the request

Read the live schema or `kimodo_help(command="kimodo_generate_animation")`. The ordinary request needs the confirmed scene character and a motion prompt. Add `duration_frames` and structured constraints only when the plan requires them. Pass confirmed Pose references, root/path data, loop settings, and Clip In/Out sources through their documented fields; do not expect prose to populate structured parameters.

### Write the motion prompt

Use concise natural language to describe the visible performance in action order: what the character does, how the action transitions, what it contacts or holds, and how it ends. Include direction, pace, and body attitude when they change the motion. For example: “Walk to the apple, stop beside the table, reach with the right hand, grasp and lift the apple, then hold it at chest height.”

The prompt describes motion semantics. Set exact duration, path/root trajectory, Pose anchors, loop behavior, and Clip windows through their explicit structured fields; prompt wording never enables those options.

Command-created Timeline Clips enable AutoBegin automatically. When the first second has no effective anchor, C# captures the current scene character pose as an implicit FullBody constraint at generated frame 0; no separate `pose_get` call or Pose constraint is needed for this fixed start. Explicit sampled Pose constraints remain available when the task requires a particular inspected pose.

Keep the public time bases distinct:

- `duration_frames`, point-constraint frames, and generation windows use 60 FPS frames relative to the generated Clip.
- `pose_get.source.timeline_frame_60` uses an absolute Timeline-global 60 FPS frame.
- Timeline UI In/Out controls expose the selected model's frame rate; use the live schema and returned sampling metadata rather than converting by guesswork.

The command rejects invalid schemas, duplicate point frames, root-path collisions, cross-character In/Out sources, unsupported ranges, and other implemented boundary errors. Its non-blocking `check` reports available mechanical warnings such as root trajectory, constraint overlap, and effector reachability. These checks do not decide semantic correctness or artistic plausibility.

## Submit and poll

1. Call `kimodo_generate_animation` and save the returned `request_id`.
2. Inspect `check` and `warnings`; treat warnings as evidence to evaluate.
3. Poll `kimodo_get_generation` at a fixed interval using the exact request ID until `completed`, `failed`, or `canceled`, with a bounded timeout.
4. Do not resubmit while the original request is running. Use `kimodo_cancel_generation` only for a generation request that should stop; installation requests cannot be canceled.
5. On completion, retain the returned Clip metadata and `animation_id`, the request ID, and the asset path as separate references. Refresh the default `scene.context` only when current scene handles need resolving; `asset_path` is metadata, not a scene Clip handle.

Installation and generation use the asynchronous status envelope. A command-level failure is `ok=false` with `error={code,message,details?}`. A successful poll can still report task status `failed` and include the task error. Scene changes, assembly reloads, Editor exit, and Play Mode transitions can cancel a request; report the returned terminal state.

## Lightweight and full analysis

`analysis_option` is optional generation-time keyframe/contact analysis. It does not run the full `animation_analyze` command and does not prove visual quality. After a completed generation, phase 4 explicitly runs `animation_analyze` and opens its returned pictures.

Generation, retargets, and corrections append derived Timeline outputs. Existing completed Clips remain available. Preserve a completed candidate when revising; use the history IDs from `scene.context` to distinguish the new request, output animation, and analysis instead of inferring ancestry from display names.

The generation loop ends when the job reaches a terminal state. Evaluation and any targeted retry belong to phase 4, so a slow job is polled rather than duplicated and an unsupported correction is reported rather than guessed.
