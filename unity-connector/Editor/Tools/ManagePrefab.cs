using System;
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
            var goResult = p.GetRequired("gameobject", "'gameobject' parameter required for create.");
            if (!goResult.IsSuccess) return new ErrorResponse(goResult.ErrorMessage);
            var pathResult = p.GetRequired("path", "'path' parameter required for create.");
            if (!pathResult.IsSuccess) return new ErrorResponse(pathResult.ErrorMessage);

            var go = FindGameObject(goResult.Value);
            if (go == null)
                return new ErrorResponse($"GameObject not found: {goResult.Value}");

            try
            {
                var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, pathResult.Value, InteractionMode.AutomatedAction);
                if (prefab == null)
                    return new ErrorResponse($"Failed to create prefab at {pathResult.Value}");
                return new SuccessResponse($"Created prefab at '{pathResult.Value}' from '{go.name}'", new { path = pathResult.Value, name = prefab.name });
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to create prefab: {ex.Message}");
            }
        }

        private static object Instantiate(ToolParams p)
        {
            var pathResult = p.GetRequired("path", "'path' parameter required for instantiate.");
            if (!pathResult.IsSuccess) return new ErrorResponse(pathResult.ErrorMessage);

            var prefab = AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(pathResult.Value);
            if (prefab == null)
                return new ErrorResponse($"Prefab not found at: {pathResult.Value}");

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
                if (parts.Length != 3)
                    return new ErrorResponse("Position requires 3 comma-separated floats (e.g. '1.0,2.0,3.0').");
                try
                {
                    instance.transform.localPosition = new Vector3(
                        float.Parse(parts[0].Trim()),
                        float.Parse(parts[1].Trim()),
                        float.Parse(parts[2].Trim()));
                }
                catch (FormatException)
                {
                    return new ErrorResponse($"Failed to parse position '{pos}'. Expected 3 comma-separated floats.");
                }
            }

            Undo.RegisterCreatedObjectUndo(instance, $"Instantiate {prefab.name}");
            return new SuccessResponse($"Instantiated '{instance.name}' from '{pathResult.Value}'",
                new { name = instance.name, path = pathResult.Value, position = instance.transform.position.ToString() });
        }

        private static object Apply(ToolParams p)
        {
            var goResult = p.GetRequired("gameobject", "'gameobject' parameter required for apply.");
            if (!goResult.IsSuccess) return new ErrorResponse(goResult.ErrorMessage);

            var go = FindGameObject(goResult.Value);
            if (go == null)
                return new ErrorResponse($"GameObject not found: {goResult.Value}");

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return new ErrorResponse($"'{goResult.Value}' is not a prefab instance.");

            try
            {
                PrefabUtility.ApplyPrefabInstance(go, InteractionMode.AutomatedAction);
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to apply prefab overrides: {ex.Message}");
            }
            return new SuccessResponse($"Applied overrides from '{go.name}' to prefab.");
        }

        private static object GetOverrides(ToolParams p)
        {
            var goResult = p.GetRequired("gameobject", "'gameobject' parameter required for get_overrides.");
            if (!goResult.IsSuccess) return new ErrorResponse(goResult.ErrorMessage);

            var go = FindGameObject(goResult.Value);
            if (go == null)
                return new ErrorResponse($"GameObject not found: {goResult.Value}");

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return new ErrorResponse($"'{goResult.Value}' is not a prefab instance.");

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

        private static UnityEngine.GameObject FindGameObject(string name)
        {
            return UnityEngine.GameObject.Find(name)
                   ?? Object.FindObjectsByType<UnityEngine.GameObject>(FindObjectsSortMode.None)
                       .FirstOrDefault(g => g.name == name);
        }
    }
}
