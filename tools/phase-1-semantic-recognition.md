---
name: kimodo-phase-1-semantic-recognition
description: Infer an animation performance plan from the user's intent and confirmed Unity scene evidence.
---

# Phase 1: Understand the performance

Think as an animator blocking a performance, not as a parser translating each noun into a command field. Animate the change the viewer should understand: **intent → action → consequence → readable ending.** A request describes a small causal scene: what the character wants, what they do to achieve it, what changes through contact, and how the result reads at the end.

## Work examples

| Request | Animator's interpretation | Decision before constraints |
|---|---|---|
| “Wave hello while standing.” | The greeting is the point; establish a readable arm raise, repeated hand/forearm motion, then a relaxed finish. The body and root stay mostly planted unless the scene suggests otherwise. | No travel path is needed. A start/end body Pose is optional unless the user asks for exact posing. |
| “Walk to the door, open it, and look back.” | This is a cause-and-effect sequence: approach the door, decelerate and orient, reach and make contact, open it, then turn the head/body to look back. | Confirm which door and whether crossing the threshold is required. If exact travel matters, determine the destination/path; otherwise avoid inventing a metric route. |
| “Pick up the apple from the table and hold it out.” | Reach is not the whole action. Plan approach, hand contact, grasp/pickup, lift, settle, and presentation. The hand must meet the apple and the torso should support the reach. | Resolve the character and apple/table from the scene. Exact object contact may need a confirmed target/Pose; filename or prompt wording alone cannot create one. |
| “Continue this walk, then stop beside the chair.” | First inspect what the source Clip actually does and how it ends. Then plan a continuation that preserves its stride/facing before braking and settling beside the chair. | Treat the source's current trajectory and last pose as evidence. Reuse them only if they serve the requested continuation; do not infer continuity from the Clip name. |
| “Make this motion happier.” | The action should remain recognizable while timing, posture, gesture, and facial/body energy express a lighter quality. “Happy” does not specify a new route or a different action. | Inspect the source and identify what already works. Ask only if “happier” could mean materially different performance styles for this task. |
| “Use this crouching Pose for the jump.” | The Pose could be anticipation, landing, or a held crouch. Its role in the action matters more than the label “crouching.” | Clarify the role if context does not establish it. Inspect the sampled Pose before using it as a constraint; it does not define the jump by itself. |
| “Jump over the box and land facing the camera.” | The action has anticipation, takeoff, flight/clearance, landing absorption, and recovery. Facing the camera is an ending requirement, not necessarily the heading throughout the arc. | Identify the box and camera direction from current scene facts. Decide whether root travel and a landing Pose are needed; do not flatten the arc into two arbitrary endpoints. |
| “Make a seamless looping dance.” | Repeated choreography and loop-boundary continuity are related but separate: the dance needs a repeatable phrase, and the last pose/motion must connect to the first. | Enable loop behavior only through the structured field. Evaluate the seam with suitable evidence; a loop flag or still image is not proof of seamless playback. |
| “Make it more natural.” | “Natural” is a quality goal, not an action plan. It might mean less robotic timing, better weight transfer, cleaner contacts, or softer posing. | Inspect the current result and ask which visible defect matters when it is not inferable. Do not respond by changing every parameter. |

These examples illustrate reasoning, not fixed animation recipes or command defaults. The character, objects, timing, style, and constraints must come from the current request and scene evidence.

The recurring question is not “which command field matches this verb?” It is “what must change over time for the audience to read the intended event?” A wave needs a legible greeting; picking up an object needs contact and transfer of weight; continuing a Clip needs continuity from its actual ending. This keeps the plan expressive while preventing unsupported details from becoming constraints.

When a request specifies both travel distance and duration, check whether the action type, average speed, acceleration, braking, and turns can agree. If different interpretations would produce different performances, ask whether the user wants to preserve the action, distance, or duration. Do not silently replace a walk with a run, change the distance, or change the timing.

## The animator's reasoning sequence

1. **Find the performance's intent.** Identify the character's goal and the visible change the viewer should notice. Keep user-stated requirements separate from assumptions.
2. **Build the action grammar.** Order beats by cause and effect: preparation → action → contact/reaction → result → settle/ending. Include transitions; a list of verbs is not yet a performance.
3. **Place it in space.** Decide whether the character stays planted, translates, turns, interacts with a target, or repeats a cycle. Describe only the route/target/facing the user or scene actually establishes.
4. **Budget time.** Allocate time to anticipation, main action, contact, reaction, transition, and ending. Record whether the duration is user-specified or inferred, and whether it is strict or approximate. For travel, estimate average speed and allow for acceleration, braking, and turning. Check that the result matches the requested style and physical scale; phase 2 converts the accepted duration to generation frames.
5. **Choose what must be known.** Decide whether the plan needs an existing Clip inspection, a sampled Pose, an exact target, a path, or no additional evidence. Analysis candidates are evidence; they are not automatic constraints.
6. **Resolve consequential ambiguity.** Ask when two reasonable readings produce meaningfully different actions, targets, or timing. Otherwise proceed with a small, stated assumption.

## Turn the plan into a handoff

Before phase 2, write a compact performance brief:

```text
Intent: <what the character wants the viewer to understand>
Beats: <ordered actions and transitions>
Space: <planted / travel / turn / target interaction; only confirmed specifics>
Timing: <requested/inferred duration, strict/approximate, and pacing requirement>
Ending: <final action, pose, facing, or loop requirement>
Evidence needed: <source Clip, Pose, target, path, or none>
Acceptance: <observable criteria and allowed semantic alternatives for phase 4>
Uncertain: <only consequential unresolved facts>
```

This brief is temporary task reasoning, not a new JSON protocol or persisted state. Phase 2 turns only the confirmed parts into structured constraints, and phase 4 uses the acceptance criteria and allowed alternatives to evaluate the result. If no allowed alternatives or observable criterion can be established, phase 4 reports the relevant judgment as `UNKNOWN` instead of inventing a label or threshold.
