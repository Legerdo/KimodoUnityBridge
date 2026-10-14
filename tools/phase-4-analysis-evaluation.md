---
name: kimodo-phase-4-analysis-evaluation
description: Evaluate a completed animation with structured analysis and opened image evidence, then decide whether to preserve or revise it.
---

# Phase 4: Analysis and evaluation

Generation completion means a Clip was produced. It does not establish that the requested action, path, contact, or visual quality is correct. Analyze the result and compare evidence with the user's requirements before reporting success or making a correction.

## Analyze one Clip

Call `animation_analyze` for exactly one `{character, clip}` per call. A generation-time `analysis_option` provides lightweight keyframe/contact information only; it is not the full analysis command. Open the returned image before making visual claims. Retain the returned `analysis_id`, `animation_id`, and `request_id` plus evidence paths to distinguish iterations. If you need the Session-wide index, call `scene.context` with `include_history=true` explicitly.

The current tested analysis contract is `analysis_schema_version=5-command-60-single-clip-v1`. Check the returned analysis and image `render_version` against the live response. Use `pictures.images` to map each tile ID, presentation, and frame before viewing it. If the analysis schema or render version is missing or incompatible with the reviewed procedure, leave the affected conclusion unverified.

## Tested visual recognition procedure

Use the acceptance criteria and any allowed semantic alternatives recorded in the phase-1 handoff. Never create new labels, thresholds, or quality scores while recognizing an animation. If no suitable criterion or alternatives were established, report that judgment as `UNKNOWN` or request the missing criterion; do not invent one.

For each candidate Clip:

1. Open the returned composite and use its image map to locate the relevant tiles.
2. Read visible motion from the image. Use structured analysis only for fields it actually returns.
3. Record one allowed value, confidence, and concrete evidence for each requested task.
4. Return `UNKNOWN` when evidence is absent, unreadable, ambiguous, or conflicting; do not resolve a conflict by guessing.

The retained visual tasks are:

- **trajectory shape:** closed loop, open path, near static, or unclear; inspect the `root2d_pelvis_projection` presentation.
- **action semantics:** compare against the caller's alternatives using phase anchors, contact transitions, and pose evidence; do not infer from the Clip name.
- **visual motion appearance:** unbroken visible drawing, visible break, or unclear. This is not a playback smoothness judgment.
- **contact pattern:** alternating, left-dominant, right-dominant, or unclear, using the `test_foot_transitions` presentation and its legend.
- **pose evidence:** when two action alternatives differ by body configuration, inspect the relevant `test_pose` tiles at the mapped frames.

Structured values such as endpoint loop candidacy, heading consistency, heading change, path length, and phase intervals must come from their named returned fields. Do not estimate them from pixels. Static pictures cannot prove velocity smoothness, acceleration quality, foot sliding, popping, or playback continuity; report these as unknown without playback evidence.

Use a constrained visual prompt with one task per judgment. Return a strict result containing only the task, allowed value, confidence, evidence references, and one short reason:

```text
You are a constrained animation-evidence recognizer.
Use the opened animation_analyze image, its tile map, the returned structured
analysis, and only the acceptance criteria and semantic alternatives from phase 1.
Locate the relevant
tile before judging. Report visible facts only. Filenames, Clip names, candidate
order, saliency, common-sense expectations, and unstated thresholds are not proof.
If evidence is missing, unreadable, or conflicts with the structured result, return
UNKNOWN and explain the conflict. Static images cannot establish playback
continuity, speed, acceleration, sliding, or popping. Return only the allowed
value, confidence (high/medium/low), evidence references, and one short reason.
```

## Compare candidates when useful

Comparison is optional and belongs inside this phase. Analyze each candidate separately under the same analysis and rendering conditions, open both images, and keep the mapping between evidence and candidate unambiguous. Compare only against the user's stated goal.

Judge the criteria independently: requested semantics, visible trajectory coherence, contact pattern, required phase presence/order/duration, confirmed required Pose coverage, expression strength, and transitions when a reference or neighboring Clip is part of the task. Record each criterion as candidate 1, candidate 2, tie, or unknown, with confidence, evidence, and one concise reason. A short accidental contact or one anchor frame does not prove a phase was expressed. Idle/background coverage is contextual; the phase-duration prior may guide evaluation, but it is not a universal pass threshold.

Do not compute an overall winner by adding scores, votes, magnitudes, displacement, contact counts, or selected-frame counts. Resolve conflicting evidence by relevance to the stated goal. Return a candidate preference only when the evidence is decisive; otherwise report no reliable difference or insufficient evidence. Never claim playback quality from static images.

## Decide whether to revise

Map each required criterion to evidence and label it verified, failed, or unverified. Warnings are evidence to inspect, not proof of failure or success. If a concrete defect is supported, use this revision loop:

1. Describe the defect in terms of the failed criterion and the evidence that supports it.
2. Return to phase 2 to rebuild only the affected constraints. Revisit phase 1 only if the defect changes the intent, action beats, timing, or acceptance criteria.
3. Submit and poll the derived generation through phase 3; do not bypass command validation or duplicate a running request.
4. Analyze the new Clip under the same criteria in this phase, while preserving the earlier candidate and its IDs.

Do not retry merely because evidence is incomplete; report what remains unverified or gather the missing evidence. Preserve completed candidates and use the returned IDs instead of inferring ancestry from names.

The final report should name the selected output, the criteria checked, the evidence used, any remaining unknowns, and whether a revision was made. Separate visual observations, structured measurements, and playback observations.
