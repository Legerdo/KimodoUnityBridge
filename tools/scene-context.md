---
name: kimodo-scene-context
description: Resolve the active scene context shared by Kimodo commands.
---

# Scene inputs / 场景输入

Kimodo commands resolve the active Unity scene directly. This page records only the input and evidence boundary used by the command layer.

- Scene characters are resolved from the active scene and the selected/open `Animator` when available.
- Constraint commands use the resolved character's real humanoid `Avatar` and `Animator`; omitted
  constraint Poses are materialized from that Avatar skeleton's bind/rest pose and reported as
  `avatar_rest` warnings.
- `effector_validate` resolves the same character context and runs the shared Humanoid IK pipeline
  before a hand/foot target is accepted for a RootEffect or generation request.
- Existing clips remain immutable; generation, analysis, retargeting, and corrections append derived assets.
- Returned character and animation names, `{track,index}` pose references, and project-relative paths are the only handles passed between commands.
- Scene changes, Editor reloads, and Play Mode transitions can cancel active generation requests and must be reported.

Internal metadata may be persisted for polling and analysis caching. It is not a public handle or query surface.
