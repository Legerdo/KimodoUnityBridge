---
name: kimodo-scene-context
description: Resolve the active scene facts and current Session identity for Kimodo commands.
---

# Scene context

`scene.context` is the public command for the active Unity scene and current Kimodo Session. Use its character, Clip, Timeline, Animator, and object references as the authoritative handles for this task. The response also provides Session identity/version and a compact artifact-history index; see [`session.md`](session.md). Resolving the first context may initialize and persist a Session; reading an already active context does not itself change its revision.

The command layer manages one transient working Session for Timeline evaluation, generation, analysis, and Pose sampling. Timeline and animation assets plus their existing history records are persisted separately. `session.clear` clears transient context objects; it retains saved Timeline and animation assets. A new context is created lazily when another command needs it.

The original scene Animator is the bound character. Timeline preview references are diagnostic and are not alternate character handles. Existing clips remain unchanged; generation, analysis, retargeting, and corrections append derived assets.

If the scene changes, Editor reloads, or Play Mode transitions cancel a running request, report the returned terminal status. Session revision is a persisted revision counter, not a rollback point. Do not inspect internal session files to resolve scene objects; use the public command response.
