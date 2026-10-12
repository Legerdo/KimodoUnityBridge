---
name: kimodo-scene-context
description: Resolve the active scene context shared by Kimodo commands.
---

# Scene inputs / 场景输入

The command layer uses one automatically managed session for the active scene. Scene
characters, Timeline assets, Animators, Avatars and loaded animation names are resolved
through that same session by each command. There is no separate public context command.

Kimodo commands resolve the active Unity scene directly. This page records only the input and evidence boundary used by the command layer.

- Scene characters are resolved from the active scene and the selected/open `Animator` when available.
- Every returned character has `role: "scene"` and is the only valid character handle for generation, analysis, Pose, retarget, and constraints.
- Timeline preview tracks bind directly to the original scene Animator. The returned `timeline_preview` object identifies that same scene object; it is diagnostic and does not introduce another character handle.
- Constraint and effector coordinates follow the spaces documented by their command schemas.
- RootEffect reachability is reported inside the non-blocking generation check and uses the selected generation model.
- Existing clips remain immutable; generation, analysis, retargeting, and corrections append derived assets.
- Returned character and animation names, `{track,index}` pose references, and project-relative paths are the only handles passed between commands.
- Scene changes, Editor reloads, and Play Mode transitions can cancel active generation requests and must be reported.

`session.clear` resets the active scene context by destroying its temporary context GameObjects. Persisted Timeline and animation assets
remain available, and a new session is created lazily by the next command.



The runtime session is temporary and is not saved with the scene. Historical analysis and
generation assets remain on disk, while transient session objects use `DontSaveInEditor |
DontSaveInBuild`; clearing a session destroys its temporary objects.
