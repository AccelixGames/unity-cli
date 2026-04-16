using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityCliConnector.Tools
{
    /// <summary>
    /// Compare two prefabs: hierarchy structure, components, and serialized field values.
    /// </summary>
    [UnityCliTool(Name = "diff_prefab", Description = "Compare two prefabs: hierarchy, components, and serialized fields.")]
    public static class DiffPrefab
    {
        public class Parameters
        {
            [ToolParameter("First prefab asset path (positional arg 0)", Required = true)]
            public string PathA { get; set; }

            [ToolParameter("Second prefab asset path (positional arg 1)", Required = true)]
            public string PathB { get; set; }

            [ToolParameter("Navigate to this child transform name in both prefabs before comparing")]
            public string Subtree { get; set; }

            [ToolParameter("Max SerializedProperty recursion depth (default: 3)")]
            public int Depth { get; set; }

            [ToolParameter("Show only differences, omit matching fields (default: true)")]
            public bool DiffOnly { get; set; }

            [ToolParameter("Explicit name mappings for fuzzy match: 'NameInA=NameInB,Other=Other2'")]
            public string Mapping { get; set; }
        }

        private static readonly HashSet<string> SkipProperties = new HashSet<string>
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance",
            "m_PrefabAsset", "m_GameObject", "m_Script", "m_Father", "m_Children",
            "m_LocalEulerAnglesHint"
        };

        private static readonly Regex PrefixPattern = new Regex(@"^\([^)]+\)\s*", RegexOptions.Compiled);

        private const int MaxEntries = 500;

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);

            // Parse positional args or named params
            var argsToken = p.GetRaw("args") as JArray;
            string pathA, pathB;
            if (argsToken != null && argsToken.Count >= 2)
            {
                pathA = argsToken[0].ToString();
                pathB = argsToken[1].ToString();
            }
            else
            {
                pathA = p.Get("path_a", "");
                pathB = p.Get("path_b", "");
            }

            if (string.IsNullOrEmpty(pathA) || string.IsNullOrEmpty(pathB))
                return new ErrorResponse("Two prefab paths required. Usage: diff_prefab <pathA> <pathB>");

            var subtree = p.Get("subtree", "");
            int depth = p.GetInt("depth", 3) ?? 3;
            if (depth < 0) depth = 3;
            bool diffOnly = p.GetBool("diff_only", true);
            var mappingStr = p.Get("mapping", "");

            // Parse explicit mappings
            var mappings = ParseMappings(mappingStr);

            // Load prefabs
            var prefabA = AssetDatabase.LoadAssetAtPath<GameObject>(pathA);
            if (prefabA == null)
                return new ErrorResponse($"Prefab not found: {pathA}");

            var prefabB = AssetDatabase.LoadAssetAtPath<GameObject>(pathB);
            if (prefabB == null)
                return new ErrorResponse($"Prefab not found: {pathB}");

            // Navigate to subtree if specified
            Transform rootA = prefabA.transform;
            Transform rootB = prefabB.transform;

            if (!string.IsNullOrEmpty(subtree))
            {
                rootA = FindTransformByName(prefabA.transform, subtree);
                if (rootA == null)
                    return new ErrorResponse($"Subtree '{subtree}' not found in A ({pathA})");

                rootB = FindTransformByName(prefabB.transform, subtree);
                if (rootB == null)
                    return new ErrorResponse($"Subtree '{subtree}' not found in B ({pathB})");
            }

            // Walk and diff
            var result = new DiffResult();
            WalkHierarchy(rootA, rootB, "", mappings, depth, diffOnly, result);

            return new SuccessResponse(
                $"Diff complete: {result.HierarchyDiffs.Count} hierarchy, {result.ComponentDiffs.Count} component, {result.FieldDiffs.Count} field diffs",
                new
                {
                    path_a = pathA,
                    path_b = pathB,
                    subtree = string.IsNullOrEmpty(subtree) ? null : subtree,
                    summary = new
                    {
                        hierarchy_diffs = result.HierarchyDiffs.Count,
                        component_diffs = result.ComponentDiffs.Count,
                        field_diffs = result.FieldDiffs.Count,
                        truncated = result.Truncated
                    },
                    hierarchy = result.HierarchyDiffs,
                    components = result.ComponentDiffs,
                    fields = result.FieldDiffs
                }
            );
        }

        private static void WalkHierarchy(
            Transform a, Transform b, string path,
            Dictionary<string, string> mappings, int depth, bool diffOnly,
            DiffResult result)
        {
            if (result.TotalCount >= MaxEntries)
            {
                result.Truncated = true;
                return;
            }

            string currentPath = string.IsNullOrEmpty(path) ? a.name : $"{path}/{a.name}";

            // Report root name difference
            if (a.name != b.name)
            {
                result.HierarchyDiffs.Add(new
                {
                    path = currentPath,
                    type = "name_diff",
                    name_a = a.name,
                    name_b = b.name
                });
            }

            // Compare GameObject properties
            CompareGameObject(a.gameObject, b.gameObject, currentPath, diffOnly, result);

            // Compare components
            CompareComponents(a.gameObject, b.gameObject, currentPath, depth, diffOnly, result);

            // Match and recurse children
            var childrenA = GetChildren(a);
            var childrenB = GetChildren(b);
            var matched = MatchChildren(childrenA, childrenB, mappings);

            foreach (var m in matched.Matched)
            {
                WalkHierarchy(m.A, m.B, currentPath, mappings, depth, diffOnly, result);
            }

            foreach (var onlyA in matched.OnlyInA)
            {
                result.HierarchyDiffs.Add(new
                {
                    path = $"{currentPath}/{onlyA.name}",
                    type = "only_in_a",
                    children = CountDescendants(onlyA),
                    components = GetComponentTypeNames(onlyA.gameObject)
                });
            }

            foreach (var onlyB in matched.OnlyInB)
            {
                result.HierarchyDiffs.Add(new
                {
                    path = $"{currentPath}/{onlyB.name}",
                    type = "only_in_b",
                    children = CountDescendants(onlyB),
                    components = GetComponentTypeNames(onlyB.gameObject)
                });
            }

            // Report explicit mapping matches
            foreach (var m in matched.MappingMatched)
            {
                result.HierarchyDiffs.Add(new
                {
                    path = currentPath,
                    type = "explicit_mapping",
                    name_a = m.A.name,
                    name_b = m.B.name
                });
            }

            // Report fuzzy matches for transparency
            foreach (var m in matched.FuzzyMatched)
            {
                result.HierarchyDiffs.Add(new
                {
                    path = currentPath,
                    type = "fuzzy_match",
                    name_a = m.A.name,
                    name_b = m.B.name
                });
            }
        }

        private static void CompareGameObject(
            GameObject a, GameObject b, string path, bool diffOnly, DiffResult result)
        {
            var diffs = new List<object>();

            string tagA, tagB;
            try { tagA = a.tag; } catch { tagA = "(invalid)"; }
            try { tagB = b.tag; } catch { tagB = "(invalid)"; }

            if (a.layer != b.layer)
                diffs.Add(new { field = "layer", value_a = LayerMask.LayerToName(a.layer), value_b = LayerMask.LayerToName(b.layer) });
            else if (!diffOnly)
                diffs.Add(new { field = "layer", value_a = LayerMask.LayerToName(a.layer), value_b = LayerMask.LayerToName(b.layer), match = true });

            if (tagA != tagB)
                diffs.Add(new { field = "tag", value_a = tagA, value_b = tagB });
            else if (!diffOnly)
                diffs.Add(new { field = "tag", value_a = tagA, value_b = tagB, match = true });

            if (a.activeSelf != b.activeSelf)
                diffs.Add(new { field = "activeSelf", value_a = a.activeSelf, value_b = b.activeSelf });
            else if (!diffOnly)
                diffs.Add(new { field = "activeSelf", value_a = a.activeSelf, value_b = b.activeSelf, match = true });

            if (diffs.Count > 0)
            {
                result.ComponentDiffs.Add(new
                {
                    path,
                    component = "GameObject",
                    diffs
                });
            }
        }

        private static void CompareComponents(
            GameObject a, GameObject b, string path, int depth, bool diffOnly, DiffResult result)
        {
            var compsA = a.GetComponents<Component>().Where(c => c != null).ToList();
            var compsB = b.GetComponents<Component>().Where(c => c != null).ToList();

            // Match components by type name
            var matchedComps = new List<(Component A, Component B)>();
            var unmatchedB = new List<Component>(compsB);
            var onlyInA = new List<string>();

            foreach (var ca in compsA)
            {
                var typeName = ca.GetType().Name;

                var match = unmatchedB.FirstOrDefault(cb => cb.GetType().Name == typeName);
                if (match != null)
                {
                    unmatchedB.Remove(match);
                    matchedComps.Add((ca, match));
                }
                else
                {
                    onlyInA.Add(typeName);
                }
            }

            var onlyInB = unmatchedB
                .Select(c => c.GetType().Name)
                .ToList();

            // Report component presence diff
            if (onlyInA.Count > 0 || onlyInB.Count > 0)
            {
                result.ComponentDiffs.Add(new
                {
                    path,
                    component = "presence",
                    only_in_a = onlyInA,
                    only_in_b = onlyInB
                });
            }

            // Compare matched components' fields
            foreach (var (ca, cb) in matchedComps)
            {
                CompareSerializedFields(ca, cb, path, depth, diffOnly, result);
            }
        }

        private static void CompareSerializedFields(
            Component a, Component b, string path, int maxDepth, bool diffOnly, DiffResult result)
        {
            var typeName = a.GetType().Name;
            using var soA = new SerializedObject(a);
            using var soB = new SerializedObject(b);

            var fieldDiffs = new List<object>();

            // Check m_Enabled explicitly (not visible to NextVisible iterator)
            var enabledA = soA.FindProperty("m_Enabled");
            var enabledB = soB.FindProperty("m_Enabled");
            if (enabledA != null && enabledB != null)
            {
                if (enabledA.boolValue != enabledB.boolValue)
                {
                    fieldDiffs.Add(new
                    {
                        property = "m_Enabled",
                        value_a = (object)enabledA.boolValue,
                        value_b = (object)enabledB.boolValue
                    });
                }
                else if (!diffOnly)
                {
                    fieldDiffs.Add(new
                    {
                        property = "m_Enabled",
                        value_a = (object)enabledA.boolValue,
                        value_b = (object)enabledB.boolValue,
                        match = true
                    });
                }
            }

            // Build property map from A
            var propsA = CollectProperties(soA, maxDepth);
            var propsB = CollectProperties(soB, maxDepth);

            // Compare all properties in A
            foreach (var kvp in propsA)
            {
                if (SkipProperties.Contains(kvp.Key.Split('.')[0]))
                    continue;

                if (propsB.TryGetValue(kvp.Key, out var valB))
                {
                    if (!ValuesEqual(kvp.Value, valB))
                    {
                        fieldDiffs.Add(new
                        {
                            property = kvp.Key,
                            value_a = FormatValue(kvp.Value),
                            value_b = FormatValue(valB)
                        });
                    }
                    else if (!diffOnly)
                    {
                        fieldDiffs.Add(new
                        {
                            property = kvp.Key,
                            value_a = FormatValue(kvp.Value),
                            value_b = FormatValue(valB),
                            match = true
                        });
                    }
                }
                else
                {
                    fieldDiffs.Add(new
                    {
                        property = kvp.Key,
                        value_a = FormatValue(kvp.Value),
                        value_b = (object)"MISSING",
                        match = false
                    });
                }
            }

            // Properties only in B
            foreach (var kvp in propsB)
            {
                if (SkipProperties.Contains(kvp.Key.Split('.')[0]))
                    continue;
                if (propsA.ContainsKey(kvp.Key))
                    continue;

                fieldDiffs.Add(new
                {
                    property = kvp.Key,
                    value_a = (object)"MISSING",
                    value_b = FormatValue(kvp.Value),
                    match = false
                });
            }

            if (fieldDiffs.Count > 0)
            {
                result.FieldDiffs.Add(new
                {
                    path,
                    component = typeName,
                    fields = fieldDiffs
                });
            }
        }

        #region Property Collection

        private static Dictionary<string, PropertyValue> CollectProperties(SerializedObject so, int maxDepth)
        {
            var props = new Dictionary<string, PropertyValue>();
            var iter = so.GetIterator();
            if (!iter.NextVisible(true)) return props;

            do
            {
                if (iter.depth > maxDepth) continue;

                var val = ReadPropertyValue(iter);
                if (val != null)
                    props[iter.propertyPath] = val;
            } while (iter.NextVisible(false));

            return props;
        }

        private static PropertyValue ReadPropertyValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return new PropertyValue(PropertyKind.Primitive, prop.intValue);
                case SerializedPropertyType.Boolean:
                    return new PropertyValue(PropertyKind.Primitive, prop.boolValue);
                case SerializedPropertyType.Float:
                    return new PropertyValue(PropertyKind.Primitive, prop.floatValue);
                case SerializedPropertyType.String:
                    return new PropertyValue(PropertyKind.Primitive, prop.stringValue ?? "");
                case SerializedPropertyType.Enum:
                    var idx = prop.enumValueIndex;
                    var name = (idx >= 0 && idx < prop.enumNames.Length)
                        ? prop.enumNames[idx]
                        : idx.ToString();
                    return new PropertyValue(PropertyKind.Primitive, name);
                case SerializedPropertyType.ObjectReference:
                    var obj = prop.objectReferenceValue;
                    if (obj == null)
                        return new PropertyValue(PropertyKind.ObjectRef, null, null);
                    return new PropertyValue(PropertyKind.ObjectRef, obj.name, obj.GetType().Name);
                case SerializedPropertyType.LayerMask:
                    return new PropertyValue(PropertyKind.Primitive, prop.intValue);
                case SerializedPropertyType.Vector2:
                    var v2 = prop.vector2Value;
                    return new PropertyValue(PropertyKind.FloatArray, new float[] { v2.x, v2.y });
                case SerializedPropertyType.Vector3:
                    var v3 = prop.vector3Value;
                    return new PropertyValue(PropertyKind.FloatArray, new float[] { v3.x, v3.y, v3.z });
                case SerializedPropertyType.Vector4:
                    var v4 = prop.vector4Value;
                    return new PropertyValue(PropertyKind.FloatArray, new float[] { v4.x, v4.y, v4.z, v4.w });
                case SerializedPropertyType.Rect:
                    var r = prop.rectValue;
                    return new PropertyValue(PropertyKind.FloatArray, new float[] { r.x, r.y, r.width, r.height });
                case SerializedPropertyType.Color:
                    var c = prop.colorValue;
                    return new PropertyValue(PropertyKind.FloatArray, new float[] { c.r, c.g, c.b, c.a });
                case SerializedPropertyType.AnimationCurve:
                    var curve = prop.animationCurveValue;
                    return new PropertyValue(PropertyKind.Primitive, $"AnimationCurve({curve?.length ?? 0} keys)");
                case SerializedPropertyType.Bounds:
                    var bounds = prop.boundsValue;
                    return new PropertyValue(PropertyKind.FloatArray, new float[]
                    {
                        bounds.center.x, bounds.center.y, bounds.center.z,
                        bounds.size.x, bounds.size.y, bounds.size.z
                    });
                case SerializedPropertyType.ArraySize:
                    return new PropertyValue(PropertyKind.Primitive, prop.intValue);
                case SerializedPropertyType.Quaternion:
                    var q = prop.quaternionValue;
                    return new PropertyValue(PropertyKind.FloatArray, new float[] { q.x, q.y, q.z, q.w });
                case SerializedPropertyType.Vector2Int:
                    return new PropertyValue(PropertyKind.Primitive, $"({prop.vector2IntValue.x}, {prop.vector2IntValue.y})");
                case SerializedPropertyType.Vector3Int:
                    var v3i = prop.vector3IntValue;
                    return new PropertyValue(PropertyKind.Primitive, $"({v3i.x}, {v3i.y}, {v3i.z})");
                case SerializedPropertyType.RectInt:
                    var ri = prop.rectIntValue;
                    return new PropertyValue(PropertyKind.Primitive, $"({ri.x}, {ri.y}, {ri.width}, {ri.height})");
                case SerializedPropertyType.BoundsInt:
                    var bi = prop.boundsIntValue;
                    return new PropertyValue(PropertyKind.Primitive, $"BoundsInt({bi.position}, {bi.size})");
                case SerializedPropertyType.Generic:
                    // Generic/complex types — skip leaf value, children are iterated separately
                    return null;
                default:
                    return new PropertyValue(PropertyKind.Primitive, $"({prop.propertyType})");
            }
        }

        #endregion

        #region Value Comparison

        private static bool ValuesEqual(PropertyValue a, PropertyValue b)
        {
            if (a.Kind != b.Kind) return false;

            if (a.Kind == PropertyKind.ObjectRef)
            {
                return a.TypeName == b.TypeName && a.Value?.ToString() == b.Value?.ToString();
            }

            if (a.Kind == PropertyKind.FloatArray)
            {
                var arrA = a.Value as float[];
                var arrB = b.Value as float[];
                if (arrA == null && arrB == null) return true;
                if (arrA == null || arrB == null) return false;
                if (arrA.Length != arrB.Length) return false;
                for (int i = 0; i < arrA.Length; i++)
                {
                    if (!Mathf.Approximately(arrA[i], arrB[i])) return false;
                }
                return true;
            }

            if (a.Value == null && b.Value == null) return true;
            if (a.Value == null || b.Value == null) return false;

            // Float comparison with tolerance
            if (a.Value is float fa && b.Value is float fb)
                return Mathf.Approximately(fa, fb);

            return a.Value.ToString() == b.Value.ToString();
        }

        private static object FormatValue(PropertyValue val)
        {
            if (val == null) return "NULL";

            if (val.Kind == PropertyKind.ObjectRef)
            {
                if (val.Value == null) return "NULL";
                return $"({val.TypeName}) {val.Value}";
            }

            if (val.Kind == PropertyKind.FloatArray)
            {
                var arr = val.Value as float[];
                if (arr == null) return "NULL";
                return "(" + string.Join(", ", arr.Select(f => f.ToString())) + ")";
            }

            return val.Value ?? "NULL";
        }

        #endregion

        #region Hierarchy Matching

        private static MatchResult MatchChildren(
            List<Transform> childrenA, List<Transform> childrenB,
            Dictionary<string, string> mappings)
        {
            var result = new MatchResult();
            var usedA = new HashSet<int>();
            var usedB = new HashSet<int>();

            // Pass 1: Explicit mapping (user intent has highest priority)
            if (mappings.Count > 0)
            {
                for (int i = 0; i < childrenA.Count; i++)
                {
                    if (usedA.Contains(i)) continue;
                    if (!mappings.TryGetValue(childrenA[i].name, out var targetName)) continue;

                    for (int j = 0; j < childrenB.Count; j++)
                    {
                        if (usedB.Contains(j)) continue;
                        if (childrenB[j].name == targetName)
                        {
                            result.Matched.Add((childrenA[i], childrenB[j]));
                            result.MappingMatched.Add((childrenA[i], childrenB[j]));
                            usedA.Add(i);
                            usedB.Add(j);
                            break;
                        }
                    }
                }
            }

            // Pass 2: Exact name match (forward order — preserves sibling index for duplicates)
            for (int i = 0; i < childrenA.Count; i++)
            {
                if (usedA.Contains(i)) continue;
                for (int j = 0; j < childrenB.Count; j++)
                {
                    if (usedB.Contains(j)) continue;
                    if (childrenB[j].name == childrenA[i].name)
                    {
                        result.Matched.Add((childrenA[i], childrenB[j]));
                        usedA.Add(i);
                        usedB.Add(j);
                        break;
                    }
                }
            }

            // Pass 3: Fuzzy match — strip "(Prefix) " pattern
            for (int i = 0; i < childrenA.Count; i++)
            {
                if (usedA.Contains(i)) continue;
                var strippedA = PrefixPattern.Replace(childrenA[i].name, "");

                for (int j = 0; j < childrenB.Count; j++)
                {
                    if (usedB.Contains(j)) continue;
                    if (PrefixPattern.Replace(childrenB[j].name, "") == strippedA)
                    {
                        result.Matched.Add((childrenA[i], childrenB[j]));
                        result.FuzzyMatched.Add((childrenA[i], childrenB[j]));
                        usedA.Add(i);
                        usedB.Add(j);
                        break;
                    }
                }
            }

            result.OnlyInA = childrenA.Where((_, i) => !usedA.Contains(i)).ToList();
            result.OnlyInB = childrenB.Where((_, j) => !usedB.Contains(j)).ToList();
            return result;
        }

        private static Dictionary<string, string> ParseMappings(string mappingStr)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(mappingStr)) return result;

            foreach (var pair in mappingStr.Split(','))
            {
                var parts = pair.Split('=');
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                        result[key] = value;
                }
            }

            return result;
        }

        #endregion

        #region Utility

        private static Transform FindTransformByName(Transform root, string name)
        {
            if (root.name == name) return root;
            return FindChildRecursive(root, name);
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                var found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static List<Transform> GetChildren(Transform t)
        {
            var list = new List<Transform>();
            foreach (Transform child in t)
                list.Add(child);
            return list;
        }

        private static int CountDescendants(Transform t)
        {
            int count = 0;
            foreach (Transform child in t)
            {
                count += 1 + CountDescendants(child);
            }
            return count;
        }

        private static string[] GetComponentTypeNames(GameObject go)
        {
            return go.GetComponents<Component>()
                .Where(c => c != null)
                .Select(c => c.GetType().Name)
                .ToArray();
        }

        #endregion

        #region Data Types

        private class DiffResult
        {
            public List<object> HierarchyDiffs = new List<object>();
            public List<object> ComponentDiffs = new List<object>();
            public List<object> FieldDiffs = new List<object>();
            public bool Truncated;

            public int TotalCount => HierarchyDiffs.Count + ComponentDiffs.Count + FieldDiffs.Count;
        }

        private class MatchResult
        {
            public List<(Transform A, Transform B)> Matched = new List<(Transform, Transform)>();
            public List<(Transform A, Transform B)> MappingMatched = new List<(Transform, Transform)>();
            public List<(Transform A, Transform B)> FuzzyMatched = new List<(Transform, Transform)>();
            public List<Transform> OnlyInA = new List<Transform>();
            public List<Transform> OnlyInB = new List<Transform>();
        }

        private enum PropertyKind { Primitive, ObjectRef, FloatArray }

        private class PropertyValue
        {
            public PropertyKind Kind;
            public object Value;
            public string TypeName;

            public PropertyValue(PropertyKind kind, object value, string typeName = null)
            {
                Kind = kind;
                Value = value;
                TypeName = typeName;
            }
        }

        #endregion
    }
}
