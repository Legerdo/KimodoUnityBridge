---
name: kimodo-session-history
description: Read the current Kimodo Session identity and its compact artifact history.
---

# Session identity and history

`scene.context` is the public entry for current scene facts and Session history. It returns `session.session_id`, `session.session_revision`, and `session.schema_version`. Resolving a missing context can initialize and persist a Session; later reads of an existing context do not themselves advance its revision. The revision advances when persisted Session metadata is written. It identifies the current persisted write revision; it does not identify an immutable snapshot or support checkout/rollback.

The `history` object is a compact index:

- `animations` identifies Timeline outputs with `animation_id`, character, name, source, and timing.
- `generations` records request IDs, status, stage, timestamps, and the output `animation_id` when available.
- `analyses` identifies analysis runs by `analysis_id`, input signature, creation time, animation reference, analysis path, and stored picture paths. The full `animation_analyze` result returns its `analysis_id` and `session_id` in `clip`.

Use the IDs to distinguish candidates and retrieve evidence through existing handles. The index does not copy full analysis payloads, motion data, or image bytes. The persisted `session.json` remains an internal navigation/index artifact under the existing Session storage; it is not a new CLI state layer or a public command argument.

Generation status is also written to the existing per-request JSON file under `Generations`. Analysis records and picture evidence are retained by the existing analysis persistence. A context refresh is read-only; it does not create a revision.

`session.clear` destroys transient context GameObjects while retaining saved Timeline and animation assets and their history. When more than one active `KimodoSession_*` exists, commands reject the ambiguous context; use the public help/schema and clear operation according to the current command contract.

## Using history during an animation task

1. Read `scene.context` and record the session identity and current revision for task coordination.
2. Before revising a result, identify its `animation_id` and relevant generation/analysis IDs. Do not infer parentage from a display name.
3. After generation and evaluation, read the updated context and compare the new IDs/status with the earlier task facts.
4. Report the revision and evidence references when useful. Do not treat a higher revision as proof that the animation improved.
