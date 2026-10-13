using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KimodoUnityBridge;
using KimodoBridge;
using KimodoBridge.Editor;
using TimelineInject;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

namespace KimodoUnityBridge.Command

{
    internal static partial class command_context
    {
        private static CommandCatalog BuildCommandCatalog()
        {
            JObject document = JObject.Parse(GetCommandDefinitionsJson());
            var handlers = new Dictionary<string, Func<string, string>>(StringComparer.Ordinal)
            {
                [HelpCommand] = GetCommandHelp,
                [SceneContextCommand] = SceneContextGet,
                [InstallServerCommand] = InstallServer,
                [MocapVideoAnalysisCommand] = AnalyzeMocapVideo,
                [MocapPictureAnalysisCommand] = AnalyzeMocapPicture,
                [AnimationAnalyzeCommand] = AnimationAnalyze,
                [TransformCaptureCommand] = TransformCapture,
                [RetargetAnimationCommand] = RetargetAnimation,
                [GenerateAnimationCommand] = GenerateAnimationAsset,
                [PoseGetCommand] = PoseGet,
                [PoseSetCommand] = PoseSet,
                [PoseSetRootTransformCommand] = PoseSetRootTransform,
                [PoseSetMuscleCommand] = PoseSetMuscle,
                [GetGenerationCommand] = GetGeneration,
                [CancelGenerationCommand] = CancelGeneration,
                [SessionClearCommand] = ClearSession,
            };

            var registrations = new List<CommandRegistration>();
            foreach (JObject definition in document["tools"]?.Values<JObject>() ?? Enumerable.Empty<JObject>())
            {
                string name = definition.Value<string>("name");
                if (string.IsNullOrWhiteSpace(name) || !handlers.TryGetValue(name, out Func<string, string> handler))
                {
                    throw new InvalidOperationException($"Command definition '{name ?? string.Empty}' has no handler.");
                }
                registrations.Add(new CommandRegistration(definition, handler));
            }

            if (registrations.Count != handlers.Count)
            {
                throw new InvalidOperationException("Command definitions and handlers are out of sync.");
            }
            return new CommandCatalog(registrations);
        }

        [MenuItem("Kimodo/Command/Export Help JSON")]
        public static void ExportCommandDefinitionsJson()
        {
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssetPath(HelpAssetPath);
            if (package == null || string.IsNullOrWhiteSpace(package.resolvedPath))
            {
                throw new InvalidOperationException($"Kimodo package was not found for '{HelpAssetPath}'.");
            }

            File.WriteAllText(
                Path.Combine(package.resolvedPath, "Command", "help.json"),
                JObject.Parse(BuildCommandDefinitionsJson()).ToString(Formatting.Indented));
            AssetDatabase.Refresh();
        }

        private static JObject CommandDefinition(string name, string description, JObject inputSchema)
        {
            return new JObject
            {
                ["name"] = name,
                ["description"] = description,
                ["inputSchema"] = inputSchema,
                ["outputSchema"] = BuildCommandOutputSchema(name),
                ["examples"] = BuildCommandExamples(name)
            };
        }

        private static JObject BuildCommandOutputSchema(string command)
        {
            var properties = new JObject
            {
                ["ok"] = new JObject { ["type"] = "boolean" },
                ["error"] = new JObject
                {
                    ["description"] = "Optional command error. Command errors use {code, message, details?}; asynchronous job failures include this object while the poll command itself succeeds.",
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["code"] = new JObject { ["type"] = "string" },
                        ["message"] = new JObject { ["type"] = "string" },
                        ["details"] = new JObject { ["type"] = "object" }
                    },
                    ["required"] = new JArray("code", "message"),
                    ["additionalProperties"] = true
                }
            };

            string[] fields = command switch
            {
                HelpCommand => new[] { "manual", "usage", "execution_model", "routing", "handles", "workflow", "commands", "constraints", "constraint_rules", "configs", "count" },
                SceneContextCommand => new[] { "scene", "scene_name", "director", "directors", "command_context_director", "playable_asset", "playable_asset_path", "duration_frames_60", "frame_rate", "current_frame_60", "characters" },
                InstallServerCommand => new[] { "request_id", "status" },
                MocapVideoAnalysisCommand or MocapPictureAnalysisCommand => new[] { "request_id", "status" },
                AnimationAnalyzeCommand => new[] { "analysis_schema_version", "clip", "pictures" },
                TransformCaptureCommand => new[] { "image_path", "width", "height", "tile_size", "tiles", "source", "targets", "evaluated_joints_world" },
                RetargetAnimationCommand => new[] { "retargeted", "source_character", "character", "animation" },
                GenerateAnimationCommand => new[] { "request_id", "status", "check", "warnings" },
                PoseGetCommand => new[] { "pose", "source", "data" },
                PoseSetCommand or PoseSetRootTransformCommand or PoseSetMuscleCommand => new[] { "pose", "data" },
                GetGenerationCommand => new[] { "request_id", "status", "stage", "message", "started_at_utc", "estimated_completion_utc", "eta_seconds", "progress_current", "progress_total", "progress_rate", "backend_progress", "runtime_root", "runtime_version", "install_mode", "server_connected", "clip", "seed", "analysis", "analysis_schema_version", "source_type", "skeleton", "person", "fps", "source_frame_count", "frame_count", "joint_names", "pose_parameterization", "coordinate_spaces", "betas", "rest_joints_m", "poses", "constraint_loss", "inout_sampling", "target_alive", "backend_request_json" },
                CancelGenerationCommand => new[] { "request_id", "status", "stage", "message", "canceled", "clip" },
                SessionClearCommand => new[] { "cleared", "name", "destroyed_context_count", "recreated_on_next_command" },
                _ => Array.Empty<string>()
            };

            foreach (string field in fields)
            {
                if (properties.Property(field) != null) continue;
                properties[field] = BuildCommandOutputFieldSchema(command, field);
            }

            return new JObject
            {
                ["type"] = "object",
                ["description"] = "Command responses keep domain fields at the top level. Success uses ok=true; command errors use ok=false and an error object. Async job failures are reported by status with an error object.",
                ["properties"] = properties,
                ["required"] = new JArray("ok"),
                ["additionalProperties"] = true
            };
        }

        private static JObject BuildCommandOutputFieldSchema(string command, string field)
        {
            switch (field)
            {
                case "status":
                    string[] statuses = command == InstallServerCommand || command == GenerateAnimationCommand
                        ? new[] { "accepted" }
                        : command == CancelGenerationCommand
                            ? new[] { "running", "completed", "failed", "canceled" }
                            : new[] { "accepted", "running", "completed", "failed", "canceled" };
                    return new JObject { ["type"] = "string", ["enum"] = new JArray(statuses) };
                case "request_id": case "message": case "usage": case "scene": case "scene_name": case "director":
                case "command_context_director": case "playable_asset": case "playable_asset_path": case "runtime_root":
                case "runtime_version": case "install_mode": case "image_path": case "source_character": case "character":
                case "name": case "asset_path": case "stage": case "started_at_utc": case "estimated_completion_utc":
                case "backend_progress": case "analysis_schema_version":
                case "source_type": case "skeleton": case "person":
                    return new JObject { ["type"] = "string" };
                case "width": case "height": case "tile_size": case "duration_frames_60": case "current_frame_60":
                case "destroyed_context_count": case "duration_frames": case "progress_current": case "progress_total": case "count": case "seed":
                case "source_frame_count": case "frame_count": case "source_frame":
                    return new JObject { ["type"] = "integer" };
                case "frame_rate": case "fps":
                    return new JObject { ["type"] = "number" };
                case "eta_seconds": case "progress_rate":
                    return NullableSchema(new JObject { ["type"] = "number" });
                case "ok": case "retargeted": case "cleared": case "server_connected": case "canceled": case "target_alive":
                    return new JObject { ["type"] = "boolean" };
                case "manual":
                    return new JObject { ["oneOf"] = new JArray(new JObject { ["type"] = "string" }, new JObject { ["type"] = "object" }) };
                case "animation":
                    return ObjectSchema("name", "source", "kind", "start_timeline_frame_60", "duration_frames", "segments", "transition");
                case "clip":
                    return command == AnimationAnalyzeCommand
                        ? ObjectSchema("character", "name", "start_timeline_frame_60", "end_timeline_frame_60_exclusive", "analysis_mode", "phase_track_version", "phase_track", "keyframes", "foot_contacts", "root_trajectory", "endpoint_pose_comparison", "motion_profile")
                        : ObjectSchema("name", "character", "asset_path", "start_timeline_frame_60", "end_timeline_frame_60_exclusive");
                case "picture": return ObjectSchema("tiles", "output", "environment");
                case "pictures":
                {
                    JObject schema = ObjectSchema("picture", "level", "aspect", "tile_count", "render_version", "image_path", "width", "height", "resolution", "supersample", "images", "tile_paths", "cached", "header_height", "screenshot_generated_at");
                    schema["properties"]["picture"] = ObjectSchema("tiles", "output", "environment");
                    return schema;
                }
                case "directors": return ArraySchema("object", "object_ref", "hierarchy_path", "name", "enabled", "playable_asset", "timeline_frame_60", "duration_frames_60", "frame_rate");
                case "characters": return ArraySchema("object", "name", "role", "scene_object", "object_ref", "animator", "timeline_preview", "avatar", "avatar_valid_humanoid", "animations");
                case "tiles": return ArraySchema("object", "tile", "view", "projection", "transforms", "bounds");
                case "targets": return ArraySchema("object", "key", "world_position");
                case "warnings": case "execution_model": case "routing": case "workflow": case "constraints": case "constraint_rules":
                    return new JObject { ["type"] = "array" };
                case "world_position": return NumberArraySchema();
                case "poses": return ArraySchema("object", "frame", "source_frame", "timestamp_seconds", "global_orient_axis_angle", "body_pose_axis_angle", "translation_m", "incam_global_orient_axis_angle", "incam_body_pose_axis_angle", "incam_translation_m", "static_confidence");
                case "joint_names": return new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } };
                case "betas": case "rest_joints_m": return new JObject { ["type"] = "array" };
                case "configs": return ArraySchema("object");
                case "commands": return ArraySchema("object", "name", "description", "required", "outputSchema", "examples");
                case "backend_request_json":
                    return new JObject { ["oneOf"] = new JArray(ObjectSchema(), new JObject { ["type"] = "string" }) };
                case "pose_parameterization": case "coordinate_spaces":
                    return ObjectSchema();
                case "pose": return ObjectSchema("track", "index");
                case "source":
                {
                    JObject source = ObjectSchema("type", "character", "character_path", "pose", "clip", "frame", "timeline_frame_60", "mode");
                    source["properties"]["clip"] = NullableSchema(new JObject { ["type"] = "string" });
                    source["properties"]["frame"] = NullableSchema(new JObject { ["type"] = "integer" });
                    return source;
                }
                case "data": case "check": case "analysis": case "constraint_loss": case "inout_sampling": case "evaluated_joints_world":
                    return ObjectSchema();
                default:
                    return new JObject { ["type"] = "object" };
            }
        }

        private static JObject ObjectSchema(params string[] propertyNames)
        {
            var properties = new JObject();
            foreach (string name in propertyNames)
            {
                properties[name] = name switch
                {
                    "name" or "character" or "source" or "kind" or "asset_path" or "analysis_schema_version" or
                    "analysis_mode" or "phase_track_version" or "tile" or "view" or "projection" or "key" or "role" or
                    "scene_object" or "object_ref" or "animator" or "avatar" or "hierarchy_path" or "playable_asset" or
                    "render_version" or "level" or "aspect" or "image_path" or "output" or "mode" or "path" or "type" or "clip" or "screenshot_generated_at" => new JObject { ["type"] = "string" },
                    "start_timeline_frame_60" or "end_timeline_frame_60_exclusive" or "duration_frames" or "timeline_frame_60" or
                    "duration_frames_60" or "tile_count" or "width" or "height" or "resolution" or "supersample" or "header_height" or "index" or "frame" or "source_frame" => new JObject { ["type"] = "integer" },
                    "enabled" or "avatar_valid_humanoid" or "cached" => new JObject { ["type"] = "boolean" },
                    "frame_rate" or "timestamp_seconds" => new JObject { ["type"] = "number" },
                    "segments" or "phase_track" or "keyframes" or "foot_contacts" or "images" or "tile_paths" or "transforms" or "animations" or "tiles" or "headers" or "global_orient_axis_angle" or "body_pose_axis_angle" or "translation_m" or "incam_global_orient_axis_angle" or "incam_body_pose_axis_angle" or "incam_translation_m" or "static_confidence" => new JObject { ["type"] = "array" },
                    "root_trajectory" or "endpoint_pose_comparison" or "motion_profile" or "environment" or "bounds" or "timeline_preview" or "outputSchema" or "title" or "header" => new JObject { ["type"] = "object" },
                    "world_position" => NumberArraySchema(),
                    "required" or "examples" => new JObject { ["type"] = "array" },
                    "description" => new JObject { ["type"] = "string" },
                    _ => new JObject { ["type"] = "object" }
                };
            }
            return new JObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["additionalProperties"] = true
            };
        }

        private static JObject ArraySchema(string itemType, params string[] itemProperties)
        {
            JObject items = itemType == "object" ? ObjectSchema(itemProperties) : new JObject { ["type"] = itemType };
            return new JObject { ["type"] = "array", ["items"] = items };
        }

        private static JObject NumberArraySchema()
        {
            return new JObject
            {
                ["type"] = "array",
                ["items"] = new JObject { ["type"] = "number" }
            };
        }

        private static JObject NullableSchema(JObject schema)
        {
            return new JObject
            {
                ["oneOf"] = new JArray(schema, new JObject { ["type"] = "null" })
            };
        }

        private static JObject Properties(params PropertyDefinition[] definitions)
        {
            var properties = new JObject();
            var required = new JArray();
            foreach (PropertyDefinition definition in definitions)
            {
                properties[definition.Name] = definition.Schema;
                if (definition.IsRequired)
                {
                    required.Add(definition.Name);
                }
            }
            return new JObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required,
                ["additionalProperties"] = false
            };
        }

        private static PropertyDefinition Required(string name, string type, string description)
        {
            return new PropertyDefinition(name, type, description, true);
        }

        private static PropertyDefinition Optional(string name, string type, string description)
        {
            return new PropertyDefinition(name, type, description, false);
        }

        private static PropertyDefinition OptionalLoop()
        {
            JObject axis = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["x"] = new JObject { ["type"] = "boolean" },
                    ["y"] = new JObject { ["type"] = "boolean" },
                    ["z"] = new JObject { ["type"] = "boolean" }
                }
            };
            JObject structured = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["enabled"] = new JObject
                    {
                        ["type"] = "boolean",
                        ["description"] = "Enable bounded loop preprocessing; defaults to true when the loop object is supplied."
                    },
                    ["lock_pos"] = axis.DeepClone(),
                    ["lock_rot"] = axis.DeepClone()
                }
            };
            return new PropertyDefinition("loop", new JObject
            {
                ["description"] = "Enable loop preprocessing, or provide per-axis endpoint locks with {enabled, lock_pos:{x,y,z}, lock_rot:{x,y,z}}.",
                ["oneOf"] = new JArray(
                    new JObject { ["type"] = "boolean" },
                    structured)
            }, false);
        }

        private static PropertyDefinition OptionalObject(string name, JObject properties, string description)
        {
            return new PropertyDefinition(name, new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = properties,
                ["description"] = description
            }, false);
        }

        private static PropertyDefinition OptionalPath()
        {
            return OptionalObject("path", new JObject
            {
                ["start_angle"] = new JObject { ["type"] = "number" },
                ["end_angle"] = new JObject { ["type"] = "number" },
                ["distance"] = new JObject { ["type"] = "number", ["minimum"] = 0 },
                ["heading"] = new JObject { ["type"] = "number" }
            }, "Optional Root2D path override. Any angle enables path override; distance replaces the measured path length; heading is applied after path composition.");
        }

        private static PropertyDefinition OptionalGeneration()
        {
            return OptionalObject("generation", new JObject
            {
                ["model"] = new JObject { ["type"] = "string" },
                ["text_encoder"] = new JObject { ["type"] = "string", ["enum"] = new JArray("high_performance", "high_precision") },
                ["seed"] = new JObject { ["type"] = "integer" },
                ["diffusion_steps"] = new JObject { ["type"] = "integer" }
            }, "Optional model and deterministic generation settings.");
        }

        private static PropertyDefinition OptionalOutput()
        {
            return OptionalObject("output", new JObject
            {
                ["mode"] = new JObject { ["type"] = "string", ["enum"] = new JArray("humanoid_muscle", "character_bone", "model_bone") },
                ["folder"] = new JObject { ["type"] = "string" },
                ["name"] = new JObject { ["type"] = "string" }
            }, "Optional animation output settings.");
        }


        private static PropertyDefinition OptionalArray(string name, string itemType, string description)
        {
            return new PropertyDefinition(name, new JObject
            {
                ["type"] = "array",
                ["items"] = new JObject { ["type"] = itemType },
                ["description"] = description
            }, false);
        }

        private static PropertyDefinition RequiredArray(string name, string itemType, string description)
        {
            return new PropertyDefinition(name, new JObject
            {
                ["type"] = "array",
                ["items"] = new JObject { ["type"] = itemType },
                ["description"] = description
            }, true);
        }

        private static PropertyDefinition OptionalEnumArray(string name, string description, params string[] values)
        {
            return new PropertyDefinition(name, new JObject
            {
                ["type"] = "array",
                ["items"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray(values)
                },
                ["description"] = description
            }, false);
        }

        private static PropertyDefinition RequiredEnumArray(string name, string description, params string[] values)
        {
            return new PropertyDefinition(name, new JObject
            {
                ["type"] = "array",
                ["items"] = new JObject
                {
                    ["type"] = "string",
                    ["enum"] = new JArray(values)
                },
                ["description"] = description
            }, true);
        }

        private static PropertyDefinition OptionalConstraints(string name, string description)
        {
            JObject poseReference = PoseReferenceSchema();
            JObject vector2 = new JObject
            {
                ["type"] = "array",
                ["items"] = new JObject { ["type"] = "number" },
                ["minItems"] = 2,
                ["maxItems"] = 2
            };
            JObject effectTarget = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["position"] = new JObject
                    {
                        ["type"] = "array", ["minItems"] = 3, ["maxItems"] = 3,
                        ["items"] = new JObject { ["type"] = "number" }
                    },
                    ["rotation"] = new JObject
                    {
                        ["type"] = "array", ["minItems"] = 4, ["maxItems"] = 4,
                        ["items"] = new JObject { ["type"] = "number" }
                    }
                },
                ["anyOf"] = new JArray(
                    new JObject { ["required"] = new JArray("position") },
                    new JObject { ["required"] = new JArray("rotation") })
            };
            JObject effectors = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["left_hand"] = effectTarget.DeepClone(),
                    ["right_hand"] = effectTarget.DeepClone(),
                    ["left_foot"] = effectTarget.DeepClone(),
                    ["right_foot"] = effectTarget.DeepClone()
                },
                ["anyOf"] = new JArray(
                    new JObject { ["required"] = new JArray("left_hand") },
                    new JObject { ["required"] = new JArray("right_hand") },
                    new JObject { ["required"] = new JArray("left_foot") },
                    new JObject { ["required"] = new JArray("right_foot") })
            };
            JObject root = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["position"] = new JObject { ["type"] = "array", ["minItems"] = 3, ["maxItems"] = 3, ["items"] = new JObject { ["type"] = "number" } },
                    ["rotation"] = new JObject { ["type"] = "array", ["minItems"] = 4, ["maxItems"] = 4, ["items"] = new JObject { ["type"] = "number" } }
                },
                ["minProperties"] = 1
            };
            JObject muscles = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = new JObject { ["type"] = "number" },
                ["minProperties"] = 1
            };
            var pointItem = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["frame"] = new JObject { ["type"] = "integer", ["minimum"] = 0 },
                    ["pose"] = poseReference.DeepClone(),
                    ["root2d"] = new JObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["properties"] = new JObject { ["position"] = vector2.DeepClone(), ["heading"] = vector2.DeepClone() },
                        ["required"] = new JArray("position", "heading")
                    },
                    ["effect"] = effectors.DeepClone()
                },
                ["required"] = new JArray("frame"),
                ["anyOf"] = new JArray(
                    new JObject { ["required"] = new JArray("pose") },
                    new JObject { ["required"] = new JArray("root2d") },
                    new JObject { ["required"] = new JArray("effect") })
            };
            var rootPathItem = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["frame"] = new JObject
                    {
                        ["type"] = "integer",
                        ["minimum"] = 0,
                        ["default"] = 0,
                        ["description"] = "First path frame; defaults to the clip start. root_path generates only start and end Root2D constraints."
                    },
                    ["root_path"] = new JObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["properties"] = new JObject
                        {
                            ["path"] = poseReference.DeepClone()
                        },
                        ["required"] = new JArray("path")
                    }
                },
                ["required"] = new JArray("root_path")
            };
            ((JObject)rootPathItem["properties"]?["root_path"]?["properties"]?["path"])["description"] =
                "Analyzed Root Path slot returned by animation_analyze at clip.root_trajectory.path.";
            return new PropertyDefinition(name, new JObject
            {
                ["type"] = "array",
                ["description"] = description,
                ["items"] = new JObject { ["oneOf"] = new JArray(pointItem, rootPathItem, InOutConstraintSchema()) }
            }, false);
        }

        private static JObject InOutConstraintSchema()
        {
            JObject source = Properties(
                Required("clip", "string", "Safe completed source Clip name in the current scene context."),
                Optional("character", "string", "Defaults to the generation character; cross-character sources are not supported."),
                new PropertyDefinition("frame", new JObject { ["type"] = "integer", ["minimum"] = 0,
                    ["description"] = "Optional boundary frame at 60 FPS, local to the Clip's played Timeline range. Defaults: outside In=last model frame / Out=first; inside In=first / Out=last." }, false));
            JObject side = Properties(
                new PropertyDefinition("source", source, true),
                new PropertyDefinition("window_frames", new JObject { ["type"] = "integer", ["minimum"] = 1, ["default"] = 1,
                    ["description"] = "Coverage at 60 FPS. Converted to model frames by ceil(window_frames * model_fps / 60), minimum 1; never silently shortened." }, false),
                new PropertyDefinition("sample_count", new JObject { ["type"] = "integer", ["minimum"] = 1, ["default"] = 1,
                    ["description"] = "Distinct samples within the converted window. One preserves the seam; multiple samples include both endpoints." }, false));
            JObject config = Properties(
                new PropertyDefinition("mode", new JObject { ["type"] = "string", ["enum"] = new JArray("outside", "inside"), ["default"] = "outside",
                    ["description"] = "Outside pads generation with source context then crops it off; inside constrains the returned clip's start/end windows. Output duration is preserved." }, false),
                new PropertyDefinition("in", (JObject)side.DeepClone(), false),
                new PropertyDefinition("out", (JObject)side.DeepClone(), false));
            config["anyOf"] = new JArray(new JObject { ["required"] = new JArray("in") }, new JObject { ["required"] = new JArray("out") });
            return Properties(new PropertyDefinition("inout", config, true));
        }

        private static PropertyDefinition RequiredPoseSource(string name)
        {
            JObject schema = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["character"] = new JObject { ["type"] = "string" },
                    ["mode"] = new JObject { ["type"] = "string", ["enum"] = new JArray("scene_pose", "skeleton_rest_pose"), ["description"] = "Sample the original scene Animator's currently evaluated pose, or the Avatar's stored skeleton rest pose." },
                    ["timeline_frame_60"] = new JObject
                    {
                        ["type"] = "integer",
                        ["minimum"] = 0,
                        ["description"] = "Absolute Timeline-global frame at 60 FPS. Use animation_analyze to find the selected Clip's start frame, then add the clip-local frame offset."
                    }
                },
                ["required"] = new JArray("character"),
                ["oneOf"] = new JArray(
                    new JObject
                    {
                        ["required"] = new JArray("character", "timeline_frame_60"),
                        ["not"] = new JObject { ["required"] = new JArray("mode") }
                    },
                    new JObject
                    {
                        ["required"] = new JArray("character", "mode"),
                        ["properties"] = new JObject { ["mode"] = new JObject { ["enum"] = new JArray("scene_pose") } },
                        ["not"] = new JObject { ["required"] = new JArray("timeline_frame_60") }
                    },
                    new JObject
                    {
                        ["required"] = new JArray("character", "mode"),
                        ["properties"] = new JObject { ["mode"] = new JObject { ["enum"] = new JArray("skeleton_rest_pose") } },
                        ["not"] = new JObject { ["required"] = new JArray("timeline_frame_60") }
                    })
            };
            return new PropertyDefinition(name, schema, true);
        }


        private static PropertyDefinition RequiredPoseReference(string name)
        {
            return new PropertyDefinition(name, PoseReferenceSchema(), true);
        }

        private static JObject PoseSetSchema()
        {
            JObject transform = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["position"] = new JObject { ["type"] = "array", ["minItems"] = 3, ["maxItems"] = 3, ["items"] = new JObject { ["type"] = "number" } },
                    ["rotation"] = new JObject { ["type"] = "array", ["minItems"] = 4, ["maxItems"] = 4, ["items"] = new JObject { ["type"] = "number" } }
                },
                ["anyOf"] = new JArray(
                    new JObject { ["required"] = new JArray("position") },
                    new JObject { ["required"] = new JArray("rotation") })
            };
            JObject effectors = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["left_hand"] = transform.DeepClone(), ["right_hand"] = transform.DeepClone(),
                    ["left_foot"] = transform.DeepClone(), ["right_foot"] = transform.DeepClone()
                },
                ["minProperties"] = 1
            };
            foreach (string key in new[] { "left_hand", "right_hand", "left_foot", "right_foot" })
            {
                JObject value = (JObject)effectors["properties"][key];
                value["description"] = "Effector target position and/or rotation.";
            }
            return new JObject
            {
                ["type"] = "object",
                ["properties"] = new JObject
                {
                    ["pose"] = PoseReferenceSchema(),
                    ["root"] = new JObject { ["type"] = "object", ["description"] = "Root position and rotation." },
                    ["muscles"] = new JObject { ["type"] = "object", ["description"] = "Map of muscle channel names to values." },
                    ["effector"] = effectors
                },
                ["required"] = new JArray("pose"),
                ["anyOf"] = new JArray(
                    new JObject { ["required"] = new JArray("root") },
                    new JObject { ["required"] = new JArray("muscles") },
                    new JObject { ["required"] = new JArray("effector") }),
                ["additionalProperties"] = false
            };
        }

        private static JObject PoseReferenceSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["description"] = "External Pose slot in the current scene context.",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["track"] = new JObject { ["type"] = "string" },
                    ["index"] = new JObject { ["type"] = "integer", ["minimum"] = 0 }
                },
                ["required"] = new JArray("track", "index")
            };
        }

        private static PropertyDefinition RequiredSamples(string name)
        {
            return new PropertyDefinition(name, new JObject
            {
                ["type"] = "array",
                ["minItems"] = 1,
                ["items"] = new JObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["properties"] = new JObject
                    {
                        ["character"] = new JObject { ["type"] = "string" },
                        ["time"] = new JObject { ["type"] = "number" }
                    },
                    ["required"] = new JArray("character", "time")
                }
            }, true);
        }

        private static PropertyDefinition RequiredAnalysisClips()
        {
            return new PropertyDefinition("clips", new JObject
            {
                ["type"] = "array",
                ["description"] = "Exactly one animation clip reference. Use a loaded Clip name or an Assets/Packages path; an external asset is appended to the character's Kimodo Timeline before analysis and is never modified.",
                ["minItems"] = 1,
                ["maxItems"] = 1,
                ["items"] = new JObject
                {
                    ["type"] = "object",
                    ["additionalProperties"] = false,
                    ["properties"] = new JObject
                    {
                        ["character"] = new JObject { ["type"] = "string" },
                        ["clip"] = new JObject { ["type"] = "string" }
                    },
                    ["required"] = new JArray("character", "clip")
                }
            }, true);
        }

        private static PropertyDefinition Enum(string name, params string[] values)
        {
            return new PropertyDefinition(name, new JObject
            {
                ["type"] = "string",
                ["enum"] = new JArray(values),
                ["default"] = values[0]
            }, false);
        }

        private static PropertyDefinition OptionalEnumWithDefault(string name, string defaultValue, params string[] values)
        {
            return new PropertyDefinition(name, new JObject
            {
                ["type"] = "string",
                ["enum"] = new JArray(values),
                ["default"] = defaultValue
            }, false);
        }

        private static PropertyDefinition RequiredEnum(string name, params string[] values)
        {
            return new PropertyDefinition(name, new JObject
            {
                ["type"] = "string",
                ["enum"] = new JArray(values)
            }, true);
        }

        private sealed class CommandCatalog
        {
            private readonly IReadOnlyList<CommandRegistration> registrations;
            private readonly Dictionary<string, CommandRegistration> byName;

            public CommandCatalog(IEnumerable<CommandRegistration> registrations)
            {
                this.registrations = registrations.ToList();
                byName = new Dictionary<string, CommandRegistration>(StringComparer.Ordinal);
                foreach (CommandRegistration registration in this.registrations)
                {
                    if (byName.ContainsKey(registration.Name))
                    {
                        throw new InvalidOperationException($"Duplicate Kimodo command '{registration.Name}'.");
                    }
                    byName.Add(registration.Name, registration);
                }
            }

            public bool TryGet(string name, out CommandRegistration registration)
            {
                registration = null;
                return name != null && byName.TryGetValue(name, out registration);
            }

            public string ToJson()
            {
                var tools = new JArray();
                foreach (CommandRegistration registration in registrations)
                {
                    tools.Add(registration.ToJson());
                }
                return new JObject { ["tools"] = tools }.ToString(Formatting.None);
            }
        }

        private sealed class CommandRegistration
        {
            private readonly JObject definition;

            public CommandRegistration(JObject definition, Func<string, string> handler)
            {
                this.definition = (JObject)definition.DeepClone();
                Handler = handler ?? throw new ArgumentNullException(nameof(handler));
                Name = this.definition.Value<string>("name");
            }

            public string Name { get; }
            public Func<string, string> Handler { get; }

            public JObject ToJson()
            {
                return (JObject)definition.DeepClone();
            }
        }

        private sealed class JobRecord
        {
            public JobRecord(
                UnityEngine.Object target,
                KimodoEditorGenerationJobSession session,
                TimelineGenerationTrace timelineGenerationTrace)
            {
                Target = target;
                Session = session;
                TimelineGenerationTrace = timelineGenerationTrace;
            }

            public UnityEngine.Object Target { get; }
            public KimodoEditorGenerationJobSession Session { get; }
            public TimelineGenerationTrace TimelineGenerationTrace { get; }
        }

        private sealed class CommandException : InvalidOperationException
        {
            public CommandException(string code, string message) : base(message)
            {
                Code = string.IsNullOrWhiteSpace(code) ? "invalid_argument" : code;
            }

            public string Code { get; }
        }

        private sealed class GenerationRangeLockedException : InvalidOperationException
        {
            public GenerationRangeLockedException(
                string command,
                Guid requestId,
                string character,
                string track,
                int lockedStartFrame,
                int lockedEndFrame,
                int requestedStartFrame,
                int requestedEndFrame)
                : base($"{command} cannot access [{requestedStartFrame},{requestedEndFrame}) on '{track}' while generation {requestId:D} locks [{lockedStartFrame},{lockedEndFrame}).")
            {
                Command = command;
                RequestId = requestId;
                Character = character;
                Track = track;
                LockedStartFrame = lockedStartFrame;
                LockedEndFrame = lockedEndFrame;
                RequestedStartFrame = requestedStartFrame;
                RequestedEndFrame = requestedEndFrame;
            }

            public string Command { get; }
            public Guid RequestId { get; }
            public string Character { get; }
            public string Track { get; }
            public int LockedStartFrame { get; }
            public int LockedEndFrame { get; }
            public int RequestedStartFrame { get; }
            public int RequestedEndFrame { get; }
        }

        private readonly struct ResolvedCharacter
        {
            public ResolvedCharacter(GameObject root, Animator animator, Avatar avatar, string name)
            {
                Root = root;
                Animator = animator;
                Avatar = avatar;
                Name = name;
            }

            public GameObject Root { get; }
            public Animator Animator { get; }
            public Avatar Avatar { get; }
            public UnityEngine.Object Target => Root;
            public string Name { get; }
        }

        private readonly struct PropertyDefinition
        {
            public PropertyDefinition(string name, string type, string description, bool required)
                : this(name, new JObject { ["type"] = type, ["description"] = description }, required)
            {
            }

            public PropertyDefinition(string name, JObject schema, bool required)
            {
                Name = name;
                Schema = schema;
                IsRequired = required;
            }

            public string Name { get; }
            public JObject Schema { get; }
            public bool IsRequired { get; }
        }
    }
}
