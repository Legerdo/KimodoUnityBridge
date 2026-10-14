---
name: kimodo-unity-bridge
description: Run reusable Unity animation tasks through Kimodo scene context, pose constraints, generation, analysis, comparison, and targeted revision.
---

# KimodoUnityBridge

Use this Skill when the user wants to create, continue, retarget, inspect, compare, or improve animation in a Unity project with KimodoUnityBridge installed. Read [`tools/common.md`](tools/common.md) before executing a task. Read the live command schema or `kimodo_help` before choosing parameters; [`Command/help.json`](Command/help.json) is the checked-in reference, not a substitute for the connected Editor schema.

## Scope

The reusable workflow covers Humanoid character animation and simple Transform-driven scene objects such as doors, balls, weapons, and props. It can generate derived character Clips, sample and edit generation Poses, apply root and end-effector constraints, retarget Humanoid Clips, inspect evidence, and compare candidates. Complex rigs, quadrupeds, non-humanoid creatures, vehicles, physics authoring, and final frame-by-frame acting polish require a different tool or an animator.

Do not claim that an object interaction is complete when the available commands cannot author or bind the object's Transform curve. Record the missing capability and hand it off after the supported character work and evidence collection.

## One execution loop

Treat every request as a bounded loop, not as a fixed list of phases:

```text
understand intent and scene
→ split the performance into independently testable Clips
→ define observable acceptance criteria
→ gather only the evidence needed for constraints
→ generate one or more candidates
→ analyze and recognize relevant evidence
→ compare candidates when comparison is useful
→ revise only the failed or uncertain criterion
→ preserve the best derived output, or hand off to an animator
```

The loop may revisit any earlier decision when new evidence changes the action, timing, target, or acceptance criteria. Do not regenerate unrelated Clips that already pass. A completed Clip is preserved; revisions produce derived outputs.

### Intent and planning

Identify the participating characters and objects, the visible result, action order, contact or interaction points, duration, and ending state. Separate facts supplied by the user from assumptions. Split actions when they have different immediate goals or target interactions; keep one continuous performance together. Ask only when two reasonable interpretations would produce materially different motion, target, timing, or Clip boundaries.

Choose criteria that can be checked for this task. Typical criteria are action semantics, required phase order, root position and heading, contact or effector reach, endpoint continuity, trajectory shape, foot contacts, loop closure, and object-relative motion. Do not invent a universal score or threshold when the task has not supplied one.

### Constraints and generation

Keep the motion prompt short and semantic. Put exact positions, headings, Poses, hand or foot targets, loop settings, Clip windows, and confirmed continuation data in structured command fields. Use only scene evidence or explicit requirements for exact constraints. Resolve and inspect a `pose_get` result before using its `{track,index}` reference. Distinguish Timeline-global frames from generated-Clip-relative frames.

Generate dependent Clips in order. When a later Clip must continue from an earlier one, finish and evaluate the earlier Clip first, then use its confirmed ending as the next input. For unstable generation, vary the Seed or a targeted constraint and retain every candidate's `request_id`, `animation_id`, Seed, prompt, constraints, and analysis IDs.

### Evidence, recognition, and comparison

Run `animation_analyze` for each candidate that needs evaluation and open its returned picture before making a visual claim. Use the fixed recognition tasks in `tools/common.md`; only run tasks that answer the current acceptance criteria. Compare candidates under identical analysis and rendering conditions. Decide criterion by criterion; never add scores, votes, frame counts, displacement, or contact counts into an invented total.

Separate visual observations, structured measurements, and playback observations. Static analysis cannot prove playback continuity, acceleration, speed smoothness, foot sliding, popping, or a final acting judgment. Mark missing or conflicting evidence as unknown.

### Revision and handoff

When evidence supports a defect, state the failed criterion, change only the relevant prompt, Pose, root, effector, continuation, or object curve input, then generate and evaluate a derived candidate under the same criterion. Stop when the required criteria pass, when further candidates show no meaningful improvement, or when the remaining issue requires frame-by-frame acting, complex binding, physics, or an unsupported command. The final report names the selected output, criteria and evidence, remaining unknowns, attempts, and the exact human follow-up.

## Command route

Use the host's Unity tools first. If they cannot perform the operation, use the Unity CLI connected to the Editor and invoke the public `KimodoUnityBridge.Command.command_dispatcher`. `kimodo_install_server` and `kimodo_generate_animation` are asynchronous: save the exact `request_id` and poll with `kimodo_get_generation` until a terminal state. Check top-level `ok`, then task `status`; warnings are evidence to inspect, not proof of success or failure.

Use `scene.context` for authoritative character, Clip, Timeline, and Session references. Use `pose_get`, `pose_set`, `kimodo_retarget_animation`, `animation_analyze`, and `transform_capture` only with handles returned by the active context or the relevant command. Treat an asset path as metadata, not a scene Clip handle. Do not infer references from display names when a command has returned an unambiguous handle.

The current command surface does not itself author arbitrary prop Transform curves or parent a prop to a hand. When those commands are added, connect them to the same loop through shared contact frames, offsets, Timeline ranges, and interaction analysis; until then, report that part as unsupported rather than pretending the character Clip moved the prop.
