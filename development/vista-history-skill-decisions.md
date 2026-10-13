# VISTA History and Skill Refresh Decisions

This record is the running design summary for the VISTA-inspired version/history and animation-engineer Skill update.

## Decisions

1. Reuse Kimodo's existing Session identity, persisted `session_revision`, schema version, generation request records, analysis records, Timeline animation IDs, and asset references. Do not add a Python state service, CLI persistence layer, duplicate `SemanticContext`, or copied scene/analysis payload.
2. Expose a compact version identity and read-only artifact history through the existing `scene.context` response. Treat the revision as the persisted Session revision counter, not a Git branch, immutable snapshot, or undo point.
3. Preserve evidence as references and compact metadata: animation IDs, generation request IDs/status, analysis IDs/signatures/timestamps, and paths to existing files. Do not embed dense motion or full analysis JSON in the history index.
4. Keep semantic choices in the Skill: intent, action phases, duration plausibility, whether displacement/Pose/path evidence is needed, interpretation of analysis, and whether to revise. Keep deterministic input checks, execution state, and any reliable mechanical checks in C# Commands.
5. Organize the Skill around the four user-facing phases: semantic recognition, constraint construction, animation generation, analysis/evaluation. Existing recognition and comparison capabilities are supporting procedures inside analysis/evaluation, not top-level phases.
6. Retain old files in place if their content is superseded; repository instructions prohibit removal. Update their contents or mark their current role clearly.
7. Do not add a command-schema hash or parent-Clip field in this pass. `schema_version` is the persisted Session format version, while callers already query the live command schema; parentage cannot be inferred reliably for every retarget/pose/analysis path. Link only relations the runtime already knows: generation `request_id` to output `animation_id`, and analysis ID to animation/session IDs.

## Verified current behavior

- Session metadata already has `sessionId`, `sessionRevision`, `schemaVersion`; `session.json` persists `session_id`, `session_revision`, and `schema_version` under `Library/KimodoData/Sessions`.
- Generation state is persisted under each Session's `Generations/generation_<request_id>.json` and summarized in Session metadata.
- Analysis files contain `analysis_id`, `session_id`, `input_signature`, `created_at_utc`, animation references, and picture references; Session JSON indexes them without duplicating dense motion.
- `scene.context` is already a public command, despite `tools/session.md` saying otherwise.
- Generation's `analysis_option` is lightweight generation-time analysis. It does not replace the full `animation_analyze` command.
- Full `animation_analyze` is not automatic inside generation; the previous Skill described it as an AI follow-up step. `PersistPictureSummary` already stores picture metadata/paths with the analysis record.

## Implementation notes

- `scene.context` now exposes Session identity/revision/schema version and compact animation, generation, and analysis indexes. Generation status includes the output `animation_id`; analysis results expose `analysis_id` and `session_id`. Analysis picture paths stay references to files already persisted by the existing renderer.
- Rewrote the root Skill and generation/context/session guides around the four phases, with analysis and comparison nested under evaluation. Added explicit animation-engineer decisions for phases, duration feasibility, travel, contacts, Pose/path use, and evidence-based revision.
- Updated `Command/help.json`, the live output schema, command contract coverage, and an Editor integration assertion for Session/history linkage.
- Static validation: `Command/help.json` parses; Session/history schema assertions pass; documentation command references are being checked against its command list; `git diff --check` passes.
- Build/test boundary: the running Unity Editor log shows a successful script compilation before the final schema/test assertion edit, so it is not current evidence for the complete working tree. A standalone `dotnet build` of Unity-generated projects is not a valid substitute here: project references fail on the Unity Test Framework's missing NUnit dependency / framework mismatch. No Unity Test Runner interface or live `kimodo_help` invocation is available in this session; Editor tests and final live-schema comparison remain unrun.
