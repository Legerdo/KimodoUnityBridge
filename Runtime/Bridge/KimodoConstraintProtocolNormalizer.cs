using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace KimodoBridge
{
    /// <summary>
    /// Checks frame compatibility at the serialized payload boundary. Same-frame
    /// Root2D and root-context composition has no defined execution semantics.
    /// </summary>
    public static class KimodoConstraintProtocolNormalizer
    {
        /// <summary>
        /// Retains the legacy entry point name while validating that Root2D rows
        /// do not overlap FullBody or effector-family rows. No rows are merged
        /// or rewritten.
        /// </summary>
        public static JArray NormalizeRoot2DIntoFullBody(JArray constraints)
        {
            if (constraints == null || constraints.Count == 0)
            {
                return constraints ?? new JArray();
            }

            var rootFrames = new HashSet<int>();
            for (int i = 0; i < constraints.Count; i++)
            {
                if (constraints[i] is not JObject record || !IsType(record, "root2d"))
                {
                    continue;
                }

                JArray frames = RequireArray(record, "frame_indices", "Root2D");
                JArray positions = RequireAlignedArray(record, "smooth_root_2d", frames.Count, "Root2D");
                JArray headings = record["global_root_heading"] as JArray;
                if (headings != null && headings.Count != frames.Count)
                {
                    throw new InvalidOperationException("Root2D global_root_heading must align with frame_indices.");
                }

                for (int row = 0; row < frames.Count; row++)
                {
                    ValidateVector2(positions[row], "Root2D smooth_root_2d");
                    if (headings != null) ValidateVector2(headings[row], "Root2D global_root_heading");
                    int frame = ReadFrame(frames[row], "Root2D");
                    if (!rootFrames.Add(frame))
                    {
                        throw new InvalidOperationException(
                            $"Undefined behavior: multiple constraints share frame {frame}; Root2D rows cannot be combined.");
                    }
                }
            }

            for (int i = 0; i < constraints.Count; i++)
            {
                if (constraints[i] is not JObject record || !IsRootContextType(record))
                {
                    continue;
                }

                string type = record.Value<string>("type") ?? "constraint";
                JArray frames = RequireArray(record, "frame_indices", type);
                for (int row = 0; row < frames.Count; row++)
                {
                    int frame = ReadFrame(frames[row], type);
                    if (rootFrames.Contains(frame))
                    {
                        throw new InvalidOperationException(
                            $"Undefined behavior: multiple constraints share frame {frame}; Root2D cannot be combined with another constraint family.");
                    }
                }
            }

            return constraints;
        }

        private static JArray RequireArray(JObject record, string propertyName, string type)
        {
            if (record[propertyName] is not JArray values)
            {
                throw new InvalidOperationException($"{type} requires {propertyName}.");
            }
            return values;
        }

        private static JArray RequireAlignedArray(JObject record, string propertyName, int expectedCount, string type)
        {
            JArray values = RequireArray(record, propertyName, type);
            if (values.Count != expectedCount)
            {
                throw new InvalidOperationException($"{type} {propertyName} must align with frame_indices.");
            }
            return values;
        }

        private static void ValidateVector2(JToken value, string name)
        {
            if (value is not JArray values || values.Count < 2)
            {
                throw new InvalidOperationException($"{name} entries must have two values.");
            }
            _ = ReadFiniteFloat(values[0], name);
            _ = ReadFiniteFloat(values[1], name);
        }

        private static int ReadFrame(JToken value, string type)
        {
            float frame = ReadFiniteFloat(value, $"{type} frame index");
            if (Mathf.Abs(frame - Mathf.Round(frame)) > 1e-4f)
            {
                throw new InvalidOperationException($"{type} frame_indices must contain integers.");
            }
            return Mathf.RoundToInt(frame);
        }

        private static float ReadFiniteFloat(JToken value, string name)
        {
            if (value == null || value.Type == JTokenType.Null)
            {
                throw new InvalidOperationException($"{name} is missing.");
            }
            float result = value.Value<float>();
            if (float.IsNaN(result) || float.IsInfinity(result))
            {
                throw new InvalidOperationException($"{name} must be finite.");
            }
            return result;
        }

        private static bool IsType(JObject record, string type) =>
            string.Equals(record.Value<string>("type"), type, StringComparison.OrdinalIgnoreCase);

        private static bool IsRootContextType(JObject record)
        {
            string type = (record?.Value<string>("type") ?? string.Empty).Trim().ToLowerInvariant();
            return type == "fullbody" || type == "end-effector" || type == "left-hand" ||
                type == "right-hand" || type == "left-foot" || type == "right-foot";
        }
    }
}
