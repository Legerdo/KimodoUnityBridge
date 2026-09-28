---
name: kimodo-scene-context
description: Resolve the active scene context shared by Kimodo commands.
---

# Scene inputs / 场景输入

`scene.context` reports the active scene, its `PlayableDirector` objects, Timeline
assets and timing, plus resolved characters, Animators, Avatars and loaded animation
names. Scene context is working state, not a read-only contract: AI may adjust it as
the task requires using the available scene, Timeline, Pose and animation commands.

Kimodo commands resolve the active Unity scene directly. This page records only the input and evidence boundary used by the command layer.

- Scene characters are resolved from the active scene and the selected/open `Animator` when available.
- Constraint and effector coordinates follow the spaces documented by their command schemas.
- `effector_validate` currently uses the default generation model. If generation selects another
  model, the validation result does not represent reachability on that model.
- Existing clips remain immutable; generation, analysis, retargeting, and corrections append derived assets.
- Returned character and animation names, `{track,index}` pose references, and project-relative paths are the only handles passed between commands.
- Scene changes, Editor reloads, and Play Mode transitions can cancel active generation requests and must be reported.

`session.clear` resets the current runtime context. Persisted Timeline and animation assets
remain available.
