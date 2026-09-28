---
name: kimodo-scene-context
description: Resolve the active scene context shared by Kimodo commands.
---

# Scene inputs / 场景输入

`scene.context` is the public read-only discovery path. It returns the active scene,
all real authored `PlayableDirector` objects and the selected Director hierarchy path,
their Timeline assets and timing, then the
resolved character object, Animator, Avatar validity, and loaded animation names.
The response contains handles for discovery; it does not switch or edit the context.

Kimodo commands resolve the active Unity scene directly. This page records only the input and evidence boundary used by the command layer.

- Scene characters are resolved from the active scene and the selected/open `Animator` when available.
- Generation constraints use the generation model's retarget `Avatar`; omitted constraint Poses
  are materialized from that skeleton's bind/rest pose and reported as
  `avatar_rest` warnings.
- `effector_validate` uses the same generation model Avatar, pose reference reader, RootEffect
  shape validation, and Humanoid IK pipeline as generation preflight.
- Existing clips remain immutable; generation, analysis, retargeting, and corrections append derived assets.
- Returned character and animation names, `{track,index}` pose references, and project-relative paths are the only handles passed between commands.
- Scene changes, Editor reloads, and Play Mode transitions can cancel active generation requests and must be reported.

Internal metadata may be persisted for polling and analysis caching. It is not a mutable
command surface. `session.clear` clears the runtime session and the next command recreates
the automatic context.
