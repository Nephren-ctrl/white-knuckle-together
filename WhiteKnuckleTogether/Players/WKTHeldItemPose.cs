using System;
using System.Collections.Generic;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// How another player holds an item. Items have no held model, only the one that lies in the world, turned whichever way
    /// it was built, so the hold comes from the model's extent (its bounds in its own space) and what the item does:
    /// <list type="bullet">
    /// <item>A tool: its longest side runs up out of the fist, leaning forward <see cref="TILT"/> degrees like a raised hammer,
    /// its second longest side facing forward, gripped <see cref="GRIP_FRACTION"/> of the way up from the bottom end.</item>
    /// <item>Something fired or thrown (a gun, a rebar; its hand item shoots): its longest side points where the player looks,
    /// gripped <see cref="AIM_GRIP_FRACTION"/> of the way from the back.</item>
    /// <item>Anything near round (longest side under <see cref="COMPACT_RATIO"/> times the next): held by its middle.</item>
    /// </list>
    /// The model's own positive end goes on top or in front, except for the items in <see cref="s_reversed"/>. Checked against
    /// every item in the game with <see cref="WKTAvatarPreview"/>.
    /// </summary>
    internal static class WKTHeldItemPose {
        /// <summary>How far the item leans forward from straight up, in degrees.</summary>
        public const float TILT = 25f;
        /// <summary>Where the hand grips, as a fraction of the item's length from its bottom end.</summary>
        public const float GRIP_FRACTION = 0.2f;
        /// <summary>Where the hand grips something aimed, as a fraction of its length from its back end.</summary>
        public const float AIM_GRIP_FRACTION = 0.3f;
        /// <summary>Below this ratio of its two longest sides an item counts as round, and is held by its middle.</summary>
        public const float COMPACT_RATIO = 1.4f;
        /// <summary>The game's scanner marks, hidden sprites on many items that are no part of their shape.</summary>
        private const string c_scan_marker = "Scan_Target";
        private const string c_target_circle = "Target_Circle";
        /// <summary>
        /// Items held the other way around: their negative end in front. A roped rebar's rope is tied at its model's negative
        /// end, and the rope goes out ahead of the player, as it shows in their own hand.
        /// </summary>
        private static readonly HashSet<string> s_reversed = new(StringComparer.OrdinalIgnoreCase) { "Item_RebarRope", "Item_RebarRope_Holiday" };
        private static readonly Dictionary<string, Bounds?> s_shapes = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<Renderer> s_renderers = new();

        /// <summary>Turns and places <paramref name="model"/> (a copy of <paramref name="prefab"/>'s world model) in its holder, the hand.</summary>
        public static void Apply(string prefabName, Item_Object prefab, Transform model) {
            if (!s_shapes.TryGetValue(prefabName, out var shape)) {
                shape = TryMeasure(prefab.gameObject, out var measured) ? measured : null;
                s_shapes[prefabName] = shape;
            }
            if (shape == null) return;
            var aimed = prefab.itemData != null && prefab.itemData.handItemAsset is HandItem_Shoot;
            GetHold(shape.Value, aimed, IsReversed(prefabName), out var rotation, out var position);
            model.localRotation = rotation;
            model.localPosition = position;
        }

        /// <summary>
        /// The rotation and position in the hand's space (forward is where the player looks, up is up) of a model with
        /// <paramref name="shape"/>, so that its grip point sits on the hand.
        /// </summary>
        public static void GetHold(Bounds shape, bool aimed, out Quaternion rotation, out Vector3 position) =>
            GetHold(shape, aimed, false, out rotation, out position);

        /// <summary>Whether <paramref name="prefabName"/> is held with its model's negative end in front (see <see cref="s_reversed"/>).</summary>
        public static bool IsReversed(string prefabName) => s_reversed.Contains(prefabName);

        /// <summary>
        /// As <see cref="GetHold(Bounds, bool, out Quaternion, out Vector3)"/>; <paramref name="reversed"/> puts the model's negative
        /// end in front or on top instead, turning it around its second side.
        /// </summary>
        public static void GetHold(Bounds shape, bool aimed, bool reversed, out Quaternion rotation, out Vector3 position) {
            var size = shape.size;
            var center = shape.center;
            var longAxis = LargestAxis(size, -1);
            var secondAxis = LargestAxis(size, longAxis);
            var length = size[longAxis];
            var grip = aimed ? AIM_GRIP_FRACTION : size[secondAxis] > 0f && length < size[secondAxis] * COMPACT_RATIO ? 0.5f : GRIP_FRACTION;
            var sourceLong = reversed ? -Axis(longAxis) : Axis(longAxis);
            var sourceSecond = Axis(secondAxis);
            if (aimed) {
                // Pointing where the player looks, its second side standing up.
                rotation = FromBases(sourceSecond, sourceLong, Vector3.up, Vector3.forward);
            }
            else {
                var radians = TILT * Mathf.Deg2Rad;
                var targetUp = new Vector3(0f, Mathf.Cos(radians), Mathf.Sin(radians));
                var targetForward = new Vector3(0f, -Mathf.Sin(radians), Mathf.Cos(radians));
                rotation = FromBases(sourceLong, sourceSecond, targetUp, targetForward);
            }
            var gripPoint = center;
            // Measured from the end that goes behind or below.
            gripPoint[longAxis] = center[longAxis] + (reversed ? 1f : -1f) * length * (0.5f - grip);
            position = -(rotation * gripPoint);
        }

        /// <summary>Measures a prefab's renderers in its own space without instantiating it. False if it has none.</summary>
        public static bool TryMeasure(GameObject prefab, out Bounds shape) {
            shape = default;
            var root = prefab.transform;
            var toRoot = root.worldToLocalMatrix;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            var any = false;
            prefab.GetComponentsInChildren(true, s_renderers);
            try {
                foreach (var renderer in s_renderers) {
                    if (renderer == null || renderer is ParticleSystemRenderer or TrailRenderer or LineRenderer ||
                        renderer.name.StartsWith(c_scan_marker, StringComparison.Ordinal) || renderer.name.Contains(c_target_circle)) continue;
                    Bounds bounds;
                    var frame = renderer.transform;
                    switch (renderer) {
                        case MeshRenderer when renderer.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null:
                            bounds = filter.sharedMesh.bounds;
                            break;
                        case SkinnedMeshRenderer skinned when skinned.sharedMesh != null:
                            // Drawn around its root bone, posed: its own bounds in that bone's space say where it shows.
                            bounds = skinned.localBounds;
                            if (skinned.rootBone != null)
                                frame = skinned.rootBone;
                            break;
                        case SpriteRenderer sprite when sprite.sprite != null:
                            bounds = sprite.sprite.bounds;
                            break;
                        default:
                            continue;
                    }
                    var toModel = toRoot * frame.localToWorldMatrix;
                    Encapsulate(toModel, bounds, ref min, ref max);
                    any = true;
                }
            }
            finally {
                s_renderers.Clear();
            }
            if (!any) return false;
            shape = new Bounds((min + max) * 0.5f, max - min);
            return true;
        }

        private static void Encapsulate(Matrix4x4 toModel, Bounds bounds, ref Vector3 min, ref Vector3 max) {
            var c = bounds.center;
            var e = bounds.extents;
            for (var i = 0; i < 8; i++) {
                var corner = new Vector3(c.x + ((i & 1) == 0 ? -e.x : e.x), c.y + ((i & 2) == 0 ? -e.y : e.y), c.z + ((i & 4) == 0 ? -e.z : e.z));
                var point = toModel.MultiplyPoint3x4(corner);
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }
        }

        /// <summary>The index of the largest component, skipping <paramref name="except"/>. Ties go to the earlier axis.</summary>
        private static int LargestAxis(Vector3 size, int except) {
            var best = -1;
            for (var i = 0; i < 3; i++) {
                if (i == except) continue;
                if (best < 0 || size[i] > size[best])
                    best = i;
            }
            return best;
        }

        private static Vector3 Axis(int index) => index switch {
            0 => Vector3.right,
            1 => Vector3.up,
            _ => Vector3.forward
        };

        /// <summary>The rotation taking one up/forward pair of unit vectors onto another. Pure math, so it runs outside Unity too.</summary>
        public static Quaternion FromBases(Vector3 sourceUp, Vector3 sourceForward, Vector3 targetUp, Vector3 targetForward) {
            var sourceRight = Vector3.Cross(sourceUp, sourceForward);
            var targetRight = Vector3.Cross(targetUp, targetForward);
            // R = T * S^T, with the bases as columns (right, up, forward).
            float M(int row, int column) =>
                Component(targetRight, row) * Component(sourceRight, column) + Component(targetUp, row) * Component(sourceUp, column) +
                Component(targetForward, row) * Component(sourceForward, column);
            return FromMatrix(M(0, 0), M(0, 1), M(0, 2), M(1, 0), M(1, 1), M(1, 2), M(2, 0), M(2, 1), M(2, 2));
        }

        private static float Component(Vector3 v, int i) => i == 0 ? v.x : i == 1 ? v.y : v.z;

        private static Quaternion FromMatrix(float m00, float m01, float m02, float m10, float m11, float m12, float m20, float m21, float m22) {
            var trace = m00 + m11 + m22;
            float x, y, z, w;
            if (trace > 0f) {
                var s = Mathf.Sqrt(trace + 1f) * 2f;
                w = 0.25f * s;
                x = (m21 - m12) / s;
                y = (m02 - m20) / s;
                z = (m10 - m01) / s;
            }
            else if (m00 > m11 && m00 > m22) {
                var s = Mathf.Sqrt(1f + m00 - m11 - m22) * 2f;
                w = (m21 - m12) / s;
                x = 0.25f * s;
                y = (m01 + m10) / s;
                z = (m02 + m20) / s;
            }
            else if (m11 > m22) {
                var s = Mathf.Sqrt(1f + m11 - m00 - m22) * 2f;
                w = (m02 - m20) / s;
                x = (m01 + m10) / s;
                y = 0.25f * s;
                z = (m12 + m21) / s;
            }
            else {
                var s = Mathf.Sqrt(1f + m22 - m00 - m11) * 2f;
                w = (m10 - m01) / s;
                x = (m02 + m20) / s;
                y = (m12 + m21) / s;
                z = 0.25f * s;
            }
            return new Quaternion(x, y, z, w);
        }
    }
}
