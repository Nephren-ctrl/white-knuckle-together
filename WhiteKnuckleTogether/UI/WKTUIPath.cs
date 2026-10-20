using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace WhiteKnuckleTogether {
    /// <summary>
    /// Hierarchy lookups for game UI that report exactly where a path broke, so game updates that move UI around
    /// can be diagnosed straight from the log. Intended for initialization, not per-frame use.
    /// </summary>
    internal static class WKTUIPath {
        private const int c_max_listed_children = 16;
        private static readonly Stack<Transform> s_searchStack = new();
        private static readonly List<Component> s_componentCache = new();
        private static readonly StringBuilder s_sb = new();

        /// <summary>
        /// Finds <paramref name="path"/> relative to <paramref name="root"/>. When <paramref name="searchFallback"/>
        /// is set and the path is broken, the whole subtree is searched for the path's last segment instead.
        /// Failures are logged with the deepest resolved segment and its children.
        /// </summary>
        public static bool TryFind(Transform? root, string path, out Transform result, bool searchFallback = false) {
            result = null!;
            if (root == null) {
                WKT.Instance.Logger.LogError($"Cannot find '{path}'. Root is null.");
                return false;
            }
            var found = root.Find(path);
            if (found != null) {
                result = found;
                return true;
            }
            if (searchFallback && UIPathParser.TryGetLastSegment(path, out var start, out var length) &&
                TrySearchByName(root, path, start, length, out found)) {
                WKT.Instance.Logger.LogWarning(
                    $"Path '{root.name}/{path}' is outdated. Found it by name at '{GetPath(found, root)}' instead.");
                result = found;
                return true;
            }
            LogBrokenPath(root, path);
            return false;
        }

        public static bool TryFind<T>(Transform? root, string path, out T result, bool searchFallback = false) where T : Component {
            result = null!;
            if (!TryFind(root, path, out var transform, searchFallback)) return false;
            if (transform.TryGetComponent(out result)) return true;
            WKT.Instance.Logger.LogError($"Found '{GetPath(transform, root)}' but it has no {typeof(T).Name} component.");
            return false;
        }

        /// <summary>
        /// Depth-first search for the first object named <paramref name="name"/> under <paramref name="root"/>,
        /// including inactive objects. Does not include the root itself.
        /// </summary>
        public static bool TryFindByName(Transform root, string name, out Transform result) =>
            TrySearchByName(root, name, 0, name.Length, out result);

        /// <summary>
        /// Destroys the object at <paramref name="path"/>. Missing objects only warn, since the goal is already met.
        /// </summary>
        public static void DestroyIfFound(Transform root, string path) {
            var found = root.Find(path);
            if (found == null) {
                WKT.Instance.Logger.LogWarning($"Nothing to remove at '{root.name}/{path}'. It may have been removed by a game update.");
                return;
            }
            Object.Destroy(found.gameObject);
        }

        public static string GetPath(Transform transform, Transform? relativeTo = null) {
            s_sb.Clear();
            var current = transform;
            while (current != null && current != relativeTo) {
                s_sb.Insert(0, current.name);
                s_sb.Insert(0, UIPathParser.SEPARATOR);
                current = current.parent;
            }
            if (s_sb.Length > 0)
                s_sb.Remove(0, 1);
            var path = s_sb.ToString();
            s_sb.Clear();
            return path;
        }

        /// <summary>
        /// Appends an indented hierarchy of <paramref name="root"/> with active state and component types.
        /// </summary>
        public static void AppendHierarchy(Transform root, StringBuilder sb, int depth = 0) {
            sb.Append(' ', depth * 2);
            sb.Append(root.name);
            if (!root.gameObject.activeSelf)
                sb.Append(" [inactive]");
            sb.Append("  {");
            root.GetComponents(s_componentCache);
            var first = true;
            foreach (var component in s_componentCache) {
                if (component == null || component is Transform) continue;
                if (!first)
                    sb.Append(", ");
                sb.Append(component.GetType().Name);
                first = false;
            }
            s_componentCache.Clear();
            sb.Append('}');
            sb.AppendLine();
            for (var i = 0; i < root.childCount; i++)
                AppendHierarchy(root.GetChild(i), sb, depth + 1);
        }

        private static bool TrySearchByName(Transform root, string source, int start, int length, out Transform result) {
            s_searchStack.Clear();
            for (var i = root.childCount - 1; i >= 0; i--)
                s_searchStack.Push(root.GetChild(i));
            while (s_searchStack.Count > 0) {
                var current = s_searchStack.Pop();
                if (UIPathParser.SegmentEquals(source, start, length, current.name)) {
                    s_searchStack.Clear();
                    result = current;
                    return true;
                }
                for (var i = current.childCount - 1; i >= 0; i--)
                    s_searchStack.Push(current.GetChild(i));
            }
            result = null!;
            return false;
        }

        private static void LogBrokenPath(Transform root, string path) {
            var current = root;
            var index = 0;
            var resolvedLength = 0;
            var missingStart = 0;
            var missingLength = 0;
            while (UIPathParser.TryReadSegment(path, ref index, out var start, out var length)) {
                var child = FindDirectChild(current, path, start, length);
                if (child == null) {
                    missingStart = start;
                    missingLength = length;
                    break;
                }
                current = child;
                resolvedLength = start + length;
            }
            s_sb.Clear();
            s_sb.Append("Failed to find '").Append(root.name).Append(UIPathParser.SEPARATOR).Append(path).Append("'. ");
            s_sb.Append("Resolved up to '").Append(root.name);
            if (resolvedLength > 0)
                s_sb.Append(UIPathParser.SEPARATOR).Append(path, 0, resolvedLength);
            s_sb.Append("', missing '").Append(path, missingStart, missingLength).Append("'. Children: [");
            var childCount = current.childCount;
            var listed = Mathf.Min(childCount, c_max_listed_children);
            for (var i = 0; i < listed; i++) {
                if (i > 0)
                    s_sb.Append(", ");
                s_sb.Append(current.GetChild(i).name);
            }
            if (childCount > listed)
                s_sb.Append(", ... ").Append(childCount - listed).Append(" more");
            s_sb.Append(']');
            var message = s_sb.ToString();
            s_sb.Clear();
            WKT.Instance.Logger.LogError(message);
        }

        private static Transform? FindDirectChild(Transform parent, string path, int start, int length) {
            for (var i = 0; i < parent.childCount; i++) {
                var child = parent.GetChild(i);
                if (UIPathParser.SegmentEquals(path, start, length, child.name))
                    return child;
            }
            return null;
        }
    }
}
