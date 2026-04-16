using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Name = "manage_prefab", Description = "Manage prefabs. Actions: create, instantiate, apply, get_overrides.")]
    public static class ManagePrefab
    {
        public class Parameters
        {
            [ToolParameter("Action: create, instantiate, apply, get_overrides", Required = true)]
            public string Action { get; set; }

            [ToolParameter("Asset path for prefab (e.g. 'Assets/Prefabs/Enemy.prefab')")]
            public string Path { get; set; }

            [ToolParameter("GameObject name in scene (for create/apply)")]
            public string GameObject { get; set; }

            [ToolParameter("Parent GameObject path for instantiate")]
            public string Parent { get; set; }

            [ToolParameter("Name for the instantiated object")]
            public string Name { get; set; }

            [ToolParameter("Position as 'x,y,z'")]
            public string Position { get; set; }
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
                case "instantiate": return Instantiate(p);
                case "apply": return Apply(p);
                case "get_overrides": return GetOverrides(p);
                default: return new ErrorResponse($"Unknown action: {actionResult.Value}");
            }
        }

        private static object Create(ToolParams p)
        {
            var goName = p.Get("gameobject", "");
            var path = p.Get("path", "");

            if (string.IsNullOrEmpty(goName))
                return new ErrorResponse("'gameobject' parameter required for create.");
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("'path' parameter required for create.");

            var go = UnityEngine.GameObject.Find(goName)
                     ?? Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None)
                         .FirstOrDefault(g => g.name == goName);
            if (go == null)
                return new ErrorResponse($"GameObject not found: {goName}");

            var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.UserAction);
            if (prefab == null)
                return new ErrorResponse($"Failed to create prefab at {path}");

            return new SuccessResponse($"Created prefab at '{path}' from '{goName}'", new { path, name = prefab.name });
        }

        private static object Instantiate(ToolParams p)
        {
            var path = p.Get("path", "");
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("'path' parameter required for instantiate.");

            var prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(path);
            if (prefab == null)
                return new ErrorResponse($"Prefab not found at: {path}");

            var instance = (UnityEngine.GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null)
                return new ErrorResponse("Failed to instantiate prefab.");

            var name = p.Get("name", "");
            if (!string.IsNullOrEmpty(name))
                instance.name = name;

            var parentPath = p.Get("parent", "");
            if (!string.IsNullOrEmpty(parentPath))
            {
                var parent = UnityEngine.GameObject.Find(parentPath);
                if (parent != null)
                    instance.transform.SetParent(parent.transform, false);
            }

            var pos = p.Get("position", "");
            if (!string.IsNullOrEmpty(pos))
            {
                var parts = pos.Split(',');
                if (parts.Length == 3)
                    instance.transform.localPosition = new Vector3(
                        float.Parse(parts[0].Trim()),
                        float.Parse(parts[1].Trim()),
                        float.Parse(parts[2].Trim()));
            }

            Undo.RegisterCreatedObjectUndo(instance, $"Instantiate {prefab.name}");
            return new SuccessResponse($"Instantiated '{instance.name}' from '{path}'",
                new { name = instance.name, path, position = instance.transform.position.ToString() });
        }

        private static object Apply(ToolParams p)
        {
            var goName = p.Get("gameobject", "");
            if (string.IsNullOrEmpty(goName))
                return new ErrorResponse("'gameobject' parameter required for apply.");

            var go = UnityEngine.GameObject.Find(goName)
                     ?? Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None)
                         .FirstOrDefault(g => g.name == goName);
            if (go == null)
                return new ErrorResponse($"GameObject not found: {goName}");

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return new ErrorResponse($"'{goName}' is not a prefab instance.");

            PrefabUtility.ApplyPrefabInstance(go, InteractionMode.UserAction);
            return new SuccessResponse($"Applied overrides from '{goName}' to prefab.");
        }

        private static object GetOverrides(ToolParams p)
        {
            var goName = p.Get("gameobject", "");
            if (string.IsNullOrEmpty(goName))
                return new ErrorResponse("'gameobject' parameter required.");

            var go = UnityEngine.GameObject.Find(goName)
                     ?? Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None)
                         .FirstOrDefault(g => g.name == goName);
            if (go == null)
                return new ErrorResponse($"GameObject not found: {goName}");

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return new ErrorResponse($"'{goName}' is not a prefab instance.");

            var mods = PrefabUtility.GetPropertyModifications(go);
            if (mods == null || mods.Length == 0)
                return new SuccessResponse("No overrides found.", new object[0]);

            var overrides = mods.Select(m => new
            {
                target = m.target != null ? m.target.name : "null",
                property = m.propertyPath,
                value = m.value
            }).ToArray();

            return new SuccessResponse($"Found {overrides.Length} overrides.", overrides);
        }
    }
}
