---
name: kimodo-unity-bridge
description: Install KimodoUnityBridge and route supported Unity animation tasks through its scene, generation, Pose, and analysis capabilities.
---

# KimodoUnityBridge

## Use this Skill when

The user wants to create, revise, continue, retarget, inspect, or compare animation inside a Unity project that has KimodoUnityBridge installed. It is for operating the package's Unity animation tools, not for general animation advice, other engines, or authoring assets outside Unity.

## What it provides

KimodoUnityBridge connects the active Unity scene and character to animation generation, Pose constraints, Clip analysis, visual evidence, and Timeline outputs. Completed Clips are preserved and revisions produce derived outputs.

## How to use it

1. Locate the installed package and read its current Skill and phase documents.
2. Confirm Unity has finished compiling and check whether its local runtime/model is ready; install only when needed.
3. Follow the four workflow guides: [semantic recognition](tools/phase-1-semantic-recognition.md), [constraint construction](tools/phase-2-constraint-construction.md), [generation](tools/phase-3-animation-generation.md), and [analysis/evaluation](tools/phase-4-analysis-evaluation.md).
4. Use the host's Kimodo capability tools. If they are unavailable, discover the live schema and call the public `command_dispatcher` through `exec_editor_script`.

The live schema and `kimodo_help` define command names, parameters, and responses. [`tools/common.md`](tools/common.md) contains shared calling and evidence rules.
