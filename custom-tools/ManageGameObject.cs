using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Name = "manage_gameobject", Description = "Manage GameObjects. Actions: create, delete, modify, find, get_hierarchy.")]
    public static class ManageGameObject
    {
        public class Parameters
        {
            [ToolParameter("Action: create, delete, modify, find, get_hierarchy", Required = true)]
            public string Action { get; set; }

            [ToolParameter("GameObject name")]
            public string Name { get; set; }

            [ToolParameter("Parent GameObject path (e.g. 'Canvas/Panel')")]
            public string Parent { get; set; }

            [ToolParameter("Tag to assign")]
            public string Tag { get; set; }

            [ToolParameter("Layer name to assign")]
            public string Layer { get; set; }

            [ToolParameter("Set active state (true/false)")]
            public bool Active { get; set; }

            [ToolParameter("Position as 'x,y,z'")]
            public string Position { get; set; }

            [ToolParameter("Rotation as 'x,y,z'")]
            public string Rotation { get; set; }

            [ToolParameter("Scale as 'x,y,z'")]
            public string Scale { get; set; }

            [ToolParameter("Primitive type for create: Cube, Sphere, Capsule, Cylinder, Plane, Quad")]
            public string Primitive { get; set; }

            [ToolParameter("Max hierarchy depth (default: 3)")]
            public int Depth { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess)
                return new ErrorResponse(actionResult.ErrorMessage);

            switch (actionResult.Value.ToLowerInvariant())
            {
                case "create": return Create(p);
                case "delete": return Delete(p);
                case "modify": return Modify(p);
                case "find": return Find(p);
                case "get_hierarchy": return GetHierarchy(p);
                default: return new ErrorResponse($"Unknown action: {actionResult.Value}. Use: create, delete, modify, find, get_hierarchy");
            }
        }

        private static object Create(ToolParams p)
        {
            var name = p.Get("name", "GameObject");
            var primitiveStr = p.Get("primitive", "");

            GameObject go;
            if (!string.IsNullOrEmpty(primitiveStr) && System.Enum.TryParse<PrimitiveType>(primitiveStr, true, out var primitiveType))
            {
                go = GameObject.CreatePrimitive(primitiveType);
                go.name = name;
            }
            else
            {
                go = new GameObject(name);
            }

            SetParent(go, p);
            ApplyTransform(go, p);
            ApplyTagLayer(go, p);

            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            return new SuccessResponse($"Created '{go.name}'", GetGameObjectInfo(go));
        }

        private static object Delete(ToolParams p)
        {
            var nameResult = p.GetRequired("name");
            if (!nameResult.IsSuccess)
                return new ErrorResponse(nameResult.ErrorMessage);

            var go = FindByPath(nameResult.Value);
            if (go == null)
                return new ErrorResponse($"GameObject not found: {nameResult.Value}");

            Undo.DestroyObjectImmediate(go);
            return new SuccessResponse($"Deleted '{nameResult.Value}'");
        }

        private static object Modify(ToolParams p)
        {
            var nameResult = p.GetRequired("name");
            if (!nameResult.IsSuccess)
                return new ErrorResponse(nameResult.ErrorMessage);

            var go = FindByPath(nameResult.Value);
            if (go == null)
                return new ErrorResponse($"GameObject not found: {nameResult.Value}");

            Undo.RecordObject(go.transform, $"Modify {go.name}");
            Undo.RecordObject(go, $"Modify {go.name}");

            ApplyTransform(go, p);
            ApplyTagLayer(go, p);

            var activeStr = p.Get("active", "");
            if (!string.IsNullOrEmpty(activeStr))
                go.SetActive(activeStr.ToLowerInvariant() == "true");

            var newParent = p.Get("parent", "");
            if (!string.IsNullOrEmpty(newParent))
                SetParent(go, p);

            EditorUtility.SetDirty(go);
            return new SuccessResponse($"Modified '{go.name}'", GetGameObjectInfo(go));
        }

        private static object Find(ToolParams p)
        {
            var name = p.Get("name", "");
            var tag = p.Get("tag", "");
            var layer = p.Get("layer", "");
            var results = new List<object>();

            var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);

            foreach (var go in allObjects)
            {
                if (!string.IsNullOrEmpty(name) && !go.name.Contains(name))
                    continue;
                if (!string.IsNullOrEmpty(tag) && !go.CompareTag(tag))
                    continue;
                if (!string.IsNullOrEmpty(layer) && go.layer != LayerMask.NameToLayer(layer))
                    continue;

                results.Add(GetGameObjectInfo(go));
                if (results.Count >= 50) break;
            }

            return new SuccessResponse($"Found {results.Count} objects.", results);
        }

        private static object GetHierarchy(ToolParams p)
        {
            var depth = p.GetInt("depth", 3) ?? 3;
            var name = p.Get("name", "");
            var hierarchy = new List<object>();

            if (!string.IsNullOrEmpty(name))
            {
                var root = FindByPath(name);
                if (root == null)
                    return new ErrorResponse($"GameObject not found: {name}");
                hierarchy.Add(BuildHierarchy(root, 0, depth));
            }
            else
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (!scene.isLoaded) continue;
                    foreach (var root in scene.GetRootGameObjects())
                        hierarchy.Add(BuildHierarchy(root, 0, depth));
                }
            }

            return new SuccessResponse("Hierarchy retrieved.", hierarchy);
        }

        private static object BuildHierarchy(GameObject go, int currentDepth, int maxDepth)
        {
            var info = new Dictionary<string, object>
            {
                ["name"] = go.name,
                ["active"] = go.activeSelf
            };

            if (currentDepth < maxDepth && go.transform.childCount > 0)
            {
                var children = new List<object>();
                foreach (Transform child in go.transform)
                    children.Add(BuildHierarchy(child.gameObject, currentDepth + 1, maxDepth));
                info["children"] = children;
            }
            else if (go.transform.childCount > 0)
            {
                info["childCount"] = go.transform.childCount;
            }

            return info;
        }

        private static GameObject FindByPath(string path)
        {
            // Try direct path first
            var go = GameObject.Find(path);
            if (go != null) return go;

            // Try searching all objects
            var all = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            return all.FirstOrDefault(g => g.name == path);
        }

        private static void SetParent(GameObject go, ToolParams p)
        {
            var parentPath = p.Get("parent", "");
            if (string.IsNullOrEmpty(parentPath)) return;
            var parent = GameObject.Find(parentPath);
            if (parent != null)
                go.transform.SetParent(parent.transform, false);
        }

        private static void ApplyTransform(GameObject go, ToolParams p)
        {
            var pos = p.Get("position", "");
            if (!string.IsNullOrEmpty(pos) && TryParseVector3(pos, out var posVec))
                go.transform.localPosition = posVec;

            var rot = p.Get("rotation", "");
            if (!string.IsNullOrEmpty(rot) && TryParseVector3(rot, out var rotVec))
                go.transform.localEulerAngles = rotVec;

            var scale = p.Get("scale", "");
            if (!string.IsNullOrEmpty(scale) && TryParseVector3(scale, out var scaleVec))
                go.transform.localScale = scaleVec;
        }

        private static void ApplyTagLayer(GameObject go, ToolParams p)
        {
            var tag = p.Get("tag", "");
            if (!string.IsNullOrEmpty(tag))
                go.tag = tag;

            var layer = p.Get("layer", "");
            if (!string.IsNullOrEmpty(layer))
            {
                int layerIndex = LayerMask.NameToLayer(layer);
                if (layerIndex >= 0) go.layer = layerIndex;
            }
        }

        private static object GetGameObjectInfo(GameObject go)
        {
            var components = go.GetComponents<Component>()
                .Where(c => c != null)
                .Select(c => c.GetType().Name)
                .ToArray();

            return new
            {
                name = go.name,
                path = GetPath(go),
                active = go.activeSelf,
                tag = go.tag,
                layer = LayerMask.LayerToName(go.layer),
                position = Vec3Str(go.transform.localPosition),
                rotation = Vec3Str(go.transform.localEulerAngles),
                scale = Vec3Str(go.transform.localScale),
                components
            };
        }

        private static string GetPath(GameObject go)
        {
            var path = go.name;
            var parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        private static string Vec3Str(Vector3 v) => $"{v.x},{v.y},{v.z}";

        private static bool TryParseVector3(string str, out Vector3 result)
        {
            result = Vector3.zero;
            var parts = str.Split(',');
            if (parts.Length != 3) return false;
            if (!float.TryParse(parts[0].Trim(), out var x)) return false;
            if (!float.TryParse(parts[1].Trim(), out var y)) return false;
            if (!float.TryParse(parts[2].Trim(), out var z)) return false;
            result = new Vector3(x, y, z);
            return true;
        }
    }
}
