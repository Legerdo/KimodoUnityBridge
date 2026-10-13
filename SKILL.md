---
name: kimodo-unity-bridge
description: Install KimodoUnityBridge and guide Unity animation work through an animation-engineer workflow.
---

# KimodoUnityBridge

Use this Skill for animation work in a Unity project with the KimodoUnityBridge package. The root Skill owns the task workflow and shared rules; linked `tools/*.md` files provide detailed procedures. The live command schema and `kimodo_help` own current command parameters and return shapes.

When using a project-installed package, locate its package root and read these documents from that package. Do not rely on stale copied manuals.

## Command entry

External scripts and CLI integrations use the public Editor dispatcher:

```csharp
using KimodoUnityBridge.Command;

string schema = command_dispatcher.GetCommandDefinitionsJson();
string result = command_dispatcher.Invoke(commandName, argumentsJson);
```

Discover the live schema or call `kimodo_help` before choosing a command. Invoke protocol command names from that schema. Internal handlers, convenience facades, bridge services, and Editor pipelines are not CLI command names. If native capability tools are unavailable, call the dispatcher through `exec_editor_script`.

## Environment readiness

Before animation work, confirm the package is imported and Unity has completed compilation. Use runtime diagnostics and live model availability to decide whether `kimodo_install_server` is needed; install on first use, upgrade, or a diagnosed broken/missing runtime. Poll its returned request ID to a terminal state. Do not reinstall on every ordinary Editor restart.

## The four animation-engineer phases

Every animation task is organized into these four phases. Recognition and candidate comparison are supporting methods inside analysis/evaluation, not separate top-level phases.

### 1. Semantic recognition

Understand the requested performance before choosing parameters. Identify the action's start state, ordered action beats, transitions, direction, contact or interaction, and ending. Separate explicit requirements from assumptions. Decide whether the task needs travel, a loop, source-Clip continuity, particular body poses, or a target in the environment.

Resolve the active scene with `scene.context`. Use its returned character, Clip, Timeline, and object handles as facts. For a named action, or a request to repair, improve, replace, continue, or vary an animation, inspect semantically relevant existing Clips before generation. A matching Clip is evidence, not automatic permission to reuse its path or poses.

Choose a plausible duration from the number and complexity of beats, travel distance, contacts, and requested pacing. Convert duration to `duration_frames` at the public 60 FPS time base. Check average travel speed (`distance / duration`) and whether starts, stops, turns, and contacts have enough time. If a request such as “walk 10 m in 3 seconds” appears implausible, explain the conflict and ask for a decision when it changes the intended result; do not disguise it by silently changing the request. C# performs deterministic range/schema checks and available mechanical warnings, but semantic plausibility remains the AI's responsibility.

Input combinations are handled without inventing data:

- Prompt only: infer the performance plan from the prompt and confirmed scene facts.
- Prompt plus Pose source: inspect the Pose and use it only for the requested role.
- Pose only: clarify the action to animate unless context and the request make it unambiguous.
- Existing animation only: analyze it and report what it does or what quality question can be answered.
- Existing animation plus prompt: analyze the source first, then identify the requested change.
- Existing animation plus Pose and prompt: analyze the source, inspect/confirm the Pose, and use each only for its stated purpose.

For an existing animation, `animation_analyze` accepts exactly one Clip per call. Use `tools/recognition.md` when identifying semantics from evidence; never infer an action from a filename alone.

### 2. Constraint construction

Translate only explicit requirements and confirmed evidence into constraints. Decide whether the performance needs no constraints, start/end root placement, a path, one or more confirmed Poses, hand/foot effectors, or Clip In/Out context. Do not turn every observation into a constraint.

For existing animations, use analysis to select candidate phase frames. Call `pose_get` at the corresponding absolute Timeline-global 60 FPS frame, inspect the returned Pose, and only then pass its `{track,index}` reference if the user requested that Pose as a generation constraint. A root trajectory is a separate path reference: generation `root_path` creates only start and end root constraints. It does not force the intermediate route. Use [`tools/pose.md`](tools/pose.md) and [`tools/constraints.md`](tools/constraints.md) for exact rules.

Before submission, cross-check that the prompt, duration, path, Pose references, and target character agree. Never rely on a model to preserve an omitted structured path or Clip constraint. Never invent a distance, path, source Clip, or Pose.

### 3. Animation generation

Submit through `kimodo_generate_animation` using live-schema fields. The normal request needs `character` and `prompt`; provide `duration_frames` and structured constraints when the plan requires them. Save the returned `request_id`, poll `kimodo_get_generation` at a fixed interval, and stop at a terminal state or a bounded timeout. Do not resubmit just because a task is still running. Follow [`tools/generation.md`](tools/generation.md) for constraint and Timeline sampling details.

The Command executes the request, records its job state, and performs deterministic validation and non-blocking checks that are implemented. It does not make the full semantic plan on the AI's behalf.

### 4. Analysis and evaluation

After a generated Clip completes, run the full `animation_analyze` as an AI-orchestrated evaluation step. The command does not automatically run that full analysis. `analysis_option` is only lightweight generation-time keyframe/contact analysis; it is not a substitute. Open returned images before making visual claims, and compare structured evidence against the user's actual requirements.

Evaluate action beats, timing, travel, path/heading, contact, pose, ending, and any requested visual quality. Static images cannot prove playback continuity or smooth acceleration; use playback evidence if those criteria matter. Report criteria that cannot be verified. If a specific defect is supported by evidence, make a targeted revision as a new derived Clip, then analyze it again. Preserve completed candidates and use comparison only when choosing among candidates is useful; see [`tools/comparison.md`](tools/comparison.md).

## Session identity and history

`scene.context` returns the current Session's `session_id`, persisted `session_revision`, and `schema_version`, plus a compact history index of animations, generation requests, and analysis records. Resolving a missing context may initialize and persist a Session; reading an active context does not itself advance the revision. Use these stable IDs and references to distinguish outputs across iterations. `session_revision` is a persisted write counter, not an immutable snapshot, branch, or undo point. Detailed analysis, motion, and image evidence remain in their referenced files; do not copy those payloads into task notes or create another state store.

Keep only the small working facts needed to coordinate the current task: selected character, source/output Clip IDs, request ID, confirmed Pose/path references, planned duration, and evaluation outcome. These belong to the current AI task orchestration, not to new public command parameters or CLI persistence.

## Shared rules

- Trust live schema for names, parameters, and response structure. Check `ok`, `error`, and asynchronous `status`; retain request IDs for polling.
- Use only handles returned by the current scene context or a command. `clip.asset_path` is asset metadata, not a scene Clip handle.
- Existing completed Clips are preserved. Corrections, retargets, and variants create derived outputs.
- Respect command errors and warnings. A warning is evidence to evaluate, not an automatic failure or proof of safety.
- Be explicit about what was checked and what remains unverified. Do not claim visual or playback success without the corresponding evidence.
- Read [`tools/common.md`](tools/common.md) for shared evidence and status rules.
