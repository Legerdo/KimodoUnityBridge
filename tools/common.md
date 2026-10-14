---
name: kimodo-common
description: Shared command, evidence, asynchronous status, and history rules for all animation phases.
---

# Common rules

The root `SKILL.md` is the task entry point. The four phase documents describe animation work from intent through evaluation. This page contains only rules shared by those phases.

## Command entry and schema

The public Editor entry is `KimodoUnityBridge.Command.command_dispatcher`:

```csharp
string schema = command_dispatcher.GetCommandDefinitionsJson();
string result = command_dispatcher.Invoke(commandName, argumentsJson);
```

Read the live schema or `kimodo_help` before choosing a command or parameter. Use protocol command names from the schema; internal C# handler names are not public commands. Route calls through the current host's tools, or through `exec_editor_script` when needed.

## Responses and asynchronous jobs

- Check the top-level `ok` before reading domain fields.
- A command error uses `ok=false` and `error={code,message,details?}`.
- An asynchronous task can return a successful poll response (`ok=true`) whose task `status` is `failed` or `canceled`; report the task status and returned error.
- Save the exact returned `request_id`. Poll at a fixed interval until a terminal state or bounded timeout. Do not duplicate a request because it is still running.
- Respect warnings and errors as returned evidence. A warning is neither proof of failure nor proof that the output is safe.

## Scene handles and history

Use character, Clip, Pose, and object references returned by `scene.context` or the relevant command. Treat `clip.asset_path` as asset metadata, not as a scene Clip handle. Resolve stale or ambiguous references again through the public commands; do not infer a handle from display names or file paths.

`scene.context` exposes the current `session_id`, persisted `session_revision`, schema version, characters, and their Timeline Clips by default. Pass `include_history=true` only when the Session-wide generation and analysis index is needed. Each generation and analysis also has its own persisted JSON record, so that index should not be fetched on every scene lookup. Keep `animation_id`, `request_id`, and `analysis_id` distinct when coordinating iterations; use `analysis_path` and `picture_paths` as evidence references. `session_revision` is a persisted write counter, not a snapshot, branch, undo point, or rollback command.

Keep only the small facts needed during the current task in working context. Do not create another public state format, persistent CLI state, or copied scene/analysis payload.

## Evidence and reporting

- Open returned images before making visual claims.
- Separate visible observations, structured measurements, and playback observations.
- Static images do not establish playback continuity, acceleration, velocity smoothness, foot sliding, or popping.
- Missing or conflicting evidence remains unknown; state what could not be checked.
- Preserve completed Clips and make supported corrections as derived outputs.
