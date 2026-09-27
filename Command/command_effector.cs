using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using KimodoBridge;
using KimodoUnityBridge;
using UnityEngine;

namespace KimodoUnityBridge.Command
{
    internal static partial class command_context
    {
        public static string EffectorValidate(string argumentsJson) => Execute(argumentsJson, arguments =>
        {
            TimelineSessionRecord session = RequireCurrentTimelineSession();
            string characterName = RequiredStringValue(arguments, "character");
            TimelineCharacterRecord character = ResolveSessionCharacterByReference(
                session, characterName, addIfMissing: false);
            string effectorName = RequiredStringValue(arguments, "effector").Trim().ToLowerInvariant();
            HumanBodyBones bone = ResolveEffectorBone(effectorName);

            JObject target = arguments["target"] as JObject
                ?? throw new InvalidOperationException("target must be an object.");
            Vector3 targetPosition = ReadVector3(
                target["position"] as JArray ?? throw new InvalidOperationException("target.position must be an array."),
                "target.position");
            bool hasRotation = target["rotation"] != null;
            Quaternion targetRotation = hasRotation
                ? ReadQuaternion(target["rotation"] as JArray ?? throw new InvalidOperationException("target.rotation must be an array."), "target.rotation")
                : Quaternion.identity;

            JObject options = arguments["options"] as JObject;
            float positionTolerance = ReadNonNegativeOption(
                options, "position_tolerance", 0.03f);
            float rotationTolerance = ReadNonNegativeOption(
                options, "rotation_tolerance_degrees", 15f);
            bool projectIfUnreachable = options?.Value<bool?>("project_if_unreachable") ?? false;

            if (!KimodoRetargetCoreUtility.IsValidHumanoid(character.Avatar))
            {
                throw new InvalidOperationException(
                    $"Character '{character.Name}' does not provide a valid humanoid Avatar.");
            }
            if (!KimodoRetargetAvatarUtility.TryBuildRetargetSkeleton(
                    character.Avatar,
                    "KimodoEffectorValidate",
                    out RetargetSkeleton cache,
                    out string cacheError))
            {
                throw new InvalidOperationException($"Build effector validation skeleton failed: {cacheError}");
            }

            try
            {
                bool poseDefaulted = arguments["pose"] == null;
                KimodoMarkerSampleResult source;
                string poseSource;
                if (poseDefaulted)
                {
                    if (!TryCreateAvatarRestPoseSample(
                            cache,
                            character.Root,
                            alignToSceneRoot: true,
                            out source,
                            out string restError))
                    {
                        throw new InvalidOperationException(
                            $"Avatar rest pose could not be created for effector validation: {restError}");
                    }
                    poseSource = "avatar_rest";
                }
                else
                {
                    source = ReadPoseSample(
                        RequirePoseReference(arguments["pose"] as JObject),
                        "effector_validate");
                    poseSource = "external_pose";
                }

                JObject root2D = arguments["root2d"] as JObject;
                if (root2D != null)
                {
                    ApplyOptionalRoot2D(source, root2D);
                }

                KimodoMarkerSampleResult candidate = source.Clone();
                candidate.constraintMode = root2D != null ? "mix" : NormalizeEffectorType(effectorName);
                candidate.enableMask = KimodoConstraintMask.ForType(candidate.constraintMode);
                if (root2D != null)
                {
                    candidate.enableMask.rootPosition = true;
                    candidate.enableMask.rootHeading = true;
                }
                SetEffectorValidity(candidate.enableMask, effectorName, true);
                candidate.validMask ??= new KimodoConstraintMask();
                candidate.effectors ??= new KimodoConstraintEffectors();
                KimodoRigidTransform value = GetEndEffector(candidate, effectorName);
                value.t = targetPosition;
                value.enableRotation = hasRotation;
                value.q = hasRotation ? targetRotation : Quaternion.identity;
                SetEndEffector(candidate, effectorName, value);
                SetEffectorValidity(candidate.validMask, effectorName, true);
                candidate.enabled = true;

                float[] restChain = MeasureEffectorChain(cache, bone, out Vector3 chainRoot);
                if (candidate.rootOverride != null &&
                    KimodoConstraintMask.IsActive(candidate, "rootposition"))
                {
                    // The measured segment lengths are Avatar-rest geometry; the
                    // envelope origin follows the authored pose/root target.
                    chainRoot = candidate.rootOverride.t;
                }

                if (!KimodoConstraintPosePipeline.TryApply(
                        candidate,
                        KimodoFrameTimeUtility.CommandFrameRate,
                        cache,
                        out _,
                        out _,
                        out string solveError))
                {
                    return Ok(new JObject
                    {
                        ["result"] = "invalid",
                        ["effector"] = effectorName,
                        ["pose_source"] = poseSource,
                        ["pose_defaulted"] = poseDefaulted,
                        ["error"] = solveError
                    });
                }

                if (!cache.GetBonePose(bone, out Vector3 solvedPosition, out Quaternion solvedRotation))
                {
                    throw new InvalidOperationException(
                        $"Avatar has no readable transform for effector '{effectorName}'.");
                }

                float positionError = Vector3.Distance(solvedPosition, targetPosition);
                float rotationError = hasRotation
                    ? Quaternion.Angle(solvedRotation, targetRotation)
                    : 0f;
                bool positionValid = positionError <= positionTolerance;
                bool rotationValid = !hasRotation || rotationError <= rotationTolerance;
                bool valid = positionValid && rotationValid;
                string result = valid ? "valid" : "unreachable";

                var response = new JObject
                {
                    ["result"] = result,
                    ["effector"] = effectorName,
                    ["pose_source"] = poseSource,
                    ["pose_defaulted"] = poseDefaulted,
                    ["target"] = new JObject
                    {
                        ["position"] = Vector3Json(targetPosition)
                    },
                    ["solved"] = new JObject
                    {
                        ["position"] = Vector3Json(solvedPosition),
                        ["rotation"] = QuaternionJson(solvedRotation)
                    },
                    ["position_error"] = positionError,
                    ["position_tolerance"] = positionTolerance,
                    ["rotation_error_degrees"] = rotationError,
                    ["rotation_tolerance_degrees"] = rotationTolerance,
                    ["reach"] = new JObject
                    {
                        ["chain_root"] = Vector3Json(chainRoot),
                        ["distance_from_chain_root"] = Vector3.Distance(chainRoot, targetPosition),
                        ["minimum"] = restChain[0],
                        ["maximum"] = restChain[1]
                    }
                };
                if (hasRotation) ((JObject)response["target"])["rotation"] = QuaternionJson(targetRotation);
                if (poseDefaulted)
                {
                    response["warnings"] = new JArray(
                        "No pose was supplied; validation used the Avatar skeleton rest pose.");
                }
                if (!valid && projectIfUnreachable)
                {
                    Vector3 projected = ProjectToReachEnvelope(chainRoot, targetPosition, restChain[1]);
                    response["projected_target"] = new JObject { ["position"] = Vector3Json(projected) };
                    JArray warnings = response["warnings"] as JArray ?? new JArray();
                    warnings.Add("Target was not applied automatically; projected_target is only a suggestion.");
                    response["warnings"] = warnings;
                }
                return Ok(response);
            }
            finally
            {
                cache.Dispose();
            }
        });

        private static HumanBodyBones ResolveEffectorBone(string name)
        {
            return name switch
            {
                "left_hand" => HumanBodyBones.LeftHand,
                "right_hand" => HumanBodyBones.RightHand,
                "left_foot" => HumanBodyBones.LeftFoot,
                "right_foot" => HumanBodyBones.RightFoot,
                _ => throw new InvalidOperationException(
                    "effector must be left_hand, right_hand, left_foot, or right_foot.")
            };
        }

        private static string NormalizeEffectorType(string name) => name.Replace('_', '-');

        private static float ReadNonNegativeOption(JObject options, string name, float defaultValue)
        {
            if (options == null || options[name] == null) return defaultValue;
            float value = ReadFiniteFloat(options[name], $"options.{name}");
            if (value < 0f) throw new InvalidOperationException($"options.{name} must be non-negative.");
            return value;
        }

        private static void ApplyOptionalRoot2D(KimodoMarkerSampleResult sample, JObject root2D)
        {
            bool hasPosition = root2D["position"] != null;
            bool hasHeading = root2D["heading"] != null;
            if (hasPosition != hasHeading)
            {
                throw new InvalidOperationException("root2d requires position and heading together.");
            }
            if (!hasPosition) return;
            Vector2 position = ReadVector2(root2D["position"] as JArray, "root2d.position");
            Vector2 heading = ReadVector2(root2D["heading"] as JArray, "root2d.heading");
            if (heading.sqrMagnitude <= 1e-8f)
            {
                throw new InvalidOperationException("root2d.heading must be non-zero.");
            }
            sample.rootOverride ??= KimodoRigidTransform.Identity;
            sample.rootOverride.t = KimodoMotionMath.ApplyPlanarPosition(
                sample.rootOverride.t,
                new Vector3(position.x, 0f, position.y));
            sample.rootOverride.q = KimodoMotionMath.ApplyPlanarHeading(
                sample.rootOverride.q,
                Quaternion.LookRotation(new Vector3(heading.x, 0f, heading.y), Vector3.up));
            sample.validMask ??= new KimodoConstraintMask();
            sample.validMask.rootPosition = true;
            sample.validMask.rootHeading = true;
        }

        private static float[] MeasureEffectorChain(
            RetargetSkeleton cache,
            HumanBodyBones effector,
            out Vector3 chainRoot)
        {
            HumanBodyBones[] chain = effector == HumanBodyBones.LeftHand || effector == HumanBodyBones.RightHand
                ? new[]
                {
                    effector == HumanBodyBones.LeftHand ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder,
                    effector == HumanBodyBones.LeftHand ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm,
                    effector == HumanBodyBones.LeftHand ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm,
                    effector
                }
                : new[]
                {
                    effector == HumanBodyBones.LeftFoot ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg,
                    effector == HumanBodyBones.LeftFoot ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg,
                    effector
                };
            var points = new List<Vector3>();
            for (int i = 0; i < chain.Length; i++)
            {
                if (cache.GetBonePose(chain[i], out Vector3 position, out _)) points.Add(position);
            }
            if (points.Count < 2)
            {
                chainRoot = cache.root != null ? cache.root.transform.position : Vector3.zero;
                return new[] { 0f, 0f };
            }
            chainRoot = points[0];
            float maximum = 0f;
            float largest = 0f;
            for (int i = 1; i < points.Count; i++)
            {
                float length = Vector3.Distance(points[i - 1], points[i]);
                maximum += length;
                largest = Mathf.Max(largest, length);
            }
            return new[] { Mathf.Max(0f, largest - (maximum - largest)), maximum };
        }

        private static Vector3 ProjectToReachEnvelope(Vector3 origin, Vector3 target, float maximum)
        {
            Vector3 delta = target - origin;
            if (maximum <= 0f || delta.sqrMagnitude <= maximum * maximum) return target;
            return origin + delta.normalized * maximum;
        }

        private static void SetEndEffector(
            KimodoMarkerSampleResult sample,
            string name,
            KimodoRigidTransform value)
        {
            switch (name)
            {
                case "left_hand": sample.effectors.leftHand = value; break;
                case "right_hand": sample.effectors.rightHand = value; break;
                case "left_foot": sample.effectors.leftFoot = value; break;
                case "right_foot": sample.effectors.rightFoot = value; break;
            }
        }

        private static void SetEffectorValidity(KimodoConstraintMask mask, string name, bool value)
        {
            switch (name)
            {
                case "left_hand": mask.leftHand = value; break;
                case "right_hand": mask.rightHand = value; break;
                case "left_foot": mask.leftFoot = value; break;
                case "right_foot": mask.rightFoot = value; break;
            }
        }

        private static JArray Vector3Json(Vector3 value) => new JArray(value.x, value.y, value.z);
        private static JArray QuaternionJson(Quaternion value) => new JArray(value.x, value.y, value.z, value.w);

        private static Vector2 ReadVector2(JArray value, string name)
        {
            if (value == null || value.Count != 2)
                throw new InvalidOperationException($"{name} must be [x,z].");
            return new Vector2(
                ReadFiniteFloat(value[0], name + "[0]"),
                ReadFiniteFloat(value[1], name + "[1]"));
        }
    }
}
