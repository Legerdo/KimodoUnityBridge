---
name: kimodo-scene-context
description: Resolve and inspect the automatic context shared by Kimodo commands.
---

# Scene context / 场景上下文

Kimodo commands do not expose Session creation or switching. Each command resolves the active Unity scene and reuses one automatic context for Timeline evaluation, generation, analysis, and pose sampling. `scene.context` is the read-only discovery command for the real scene, Director, Timeline asset, characters, Animators, Avatars, and loaded clips.

- Scene characters are resolved from the active scene and the selected/open `Animator` when available.
- Existing clips remain immutable; generation, analysis, retargeting, and corrections append derived assets.
- Returned character and animation names, `{track,index}` pose references, and project-relative paths are the only handles passed between commands.
- Use `scene.context` before selecting handles when the active Director or character identity is unknown. The command does not mutate the scene.
- Scene changes, Editor reloads, and Play Mode transitions can cancel active generation requests and must be reported.

The hidden context may persist internal metadata for reliable polling and analysis caching. That metadata is an implementation detail and is not a public query API.
