---
name: kimodo-scene-context
description: Resolve and inspect the automatic context shared by Kimodo commands.
---

# Scene context / 场景上下文

Kimodo commands resolve the active Unity scene and share context for Timeline evaluation, generation, analysis, and Pose sampling. `scene.context` reports the current scene, Directors, Timeline assets, characters, Animators, Avatars, and loaded clips. The AI may adjust the working context as the task requires.

- Scene characters are resolved from the active scene and the selected/open `Animator` when available.
- Existing clips remain immutable; generation, analysis, retargeting, and corrections append derived assets.
- Returned character and animation names, `{track,index}` pose references, and project-relative paths are the only handles passed between commands.
- Use `scene.context` when the active Director or character identity is unknown. Use returned names and references when making subsequent changes.
- Scene changes, Editor reloads, and Play Mode transitions can cancel active generation requests and must be reported.

`session.clear` resets the current runtime context; saved Timeline and animation assets are retained.
