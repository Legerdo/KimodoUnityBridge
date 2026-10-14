---
name: kimodo-phase-2-constraint-construction
description: Convert the confirmed performance plan into the smallest valid set of Kimodo generation constraints.
---

# Phase 2: Constraint construction

Turn the phase-1 performance plan into only the structured constraints the task needs. A prompt describes action semantics; structured fields carry exact spatial, pose, contact, loop, and Clip-window requirements.

## Choose constraints from evidence

Use the live schema from `kimodo_help(command="kimodo_generate_animation")` before constructing the request. Select only what is supported by the user's request or confirmed evidence:

- No constraints for a performance that does not need exact spatial or pose anchors.
- `root2d` samples when exact planar position and heading are required at a generated-Clip frame.
- `root_path` when a returned Root Path reference is explicitly appropriate. It creates only the beginning and ending root constraints; it does not force the intermediate route.
- An explicit FullBody or effector Pose only after `pose_get` has materialized and the caller has inspected and confirmed that Pose for the requested role. In the Timeline Inspector workflow, enabled AutoBegin is a fixed exception: when the first second has no effective anchor, C# captures the current scene character pose and inserts it as an implicit FullBody frame-0 constraint; the AI does not need a separate Pose slot for this fixed step.
- `constraints[].inout` only when explicit same-character source Clip context at the start or end is requested.
- Loop settings only when a loop is an explicit requirement; prose in the prompt does not set the structured loop field.

Do not convert every analysis observation into a constraint. In particular, an analyzed path or phase anchor is evidence, not an automatic generation input.

For exact travel, use a reference whose source and coordinate meaning are confirmed. A `root_path` must be the `{track,index}` returned by `animation_analyze` at `clip.root_trajectory.path`; it supplies only the generated Clip's beginning and ending root constraints. A `root2d` point must use the live schema's planar `[x,z]` position and `[x,z]` heading convention. A hand/foot target or scene object must come from the current scene or command response with a resolvable reference and confirmed coordinate meaning. If the source, coordinate convention, or target cannot be confirmed, leave the requirement unresolved and do not submit a guessed constraint.

## Resolve and sample Pose references

Command-created Timeline Clips enable AutoBegin automatically. When the first second has no effective anchor, C# captures the current scene character pose as an implicit FullBody constraint at generated frame 0; the AI does not need to call `pose_get` or pass a separate Pose for this fixed start. Use `pose_get` when a specific sampled Pose is itself required, such as an inspected Pose from an existing animation. `character_root` is the bound scene GameObject Transform in world space; `data.root` is the sampled Pose root channel. Keep those references distinct when comparing positions.

For a Pose from an existing animation, use the relevant analysis interval and candidate anchor to choose an absolute Timeline-global frame. Call `pose_get` with `source.timeline_frame_60` at 60 FPS, inspect the returned Pose, and confirm its role before passing its returned `{track,index}` reference.

The public time bases differ by operation: `pose_get.source.timeline_frame_60` is Timeline-global; `constraints[].frame` is relative to the generated Clip. Convert deliberately. Do not pass an analysis candidate directly as a generation Pose.

## Validate the assembled request

Before submission, cross-check:

1. The character, source Clip, target, and Pose references resolve in the current scene context.
2. Convert an approximate duration in seconds to the nearest `duration_frames` at 60 FPS (`round(seconds × 60)`) and record the represented duration (`duration_frames / 60`). Preserve an explicitly requested frame count. If the user requires an exact duration that is not representable as a whole 60 FPS frame, ask whether the nearest frame duration is acceptable before submitting.
3. Every exact distance, direction, start/end point, loop, contact, or Clip boundary requested by the user is represented in a structured field when one exists.
4. No additional path, Pose, or range has been inferred from a filename or scene layout.
5. Point constraints use the live schema and avoid explicit duplicate frame entries or root-path endpoint collisions.

The model can misread prose or omit a path. Never depend on a prompt alone to enforce an exact structured route or Pose anchor. C# command validation checks schema/ranges and implemented deterministic conditions; it does not decide whether the chosen action or duration is semantically sensible.

Keep only compact working facts needed to submit and evaluate the task: selected character/source, confirmed Pose and path references, duration, and required criteria. These remain in current task orchestration and are not a new public Command parameter or persistent state format.
