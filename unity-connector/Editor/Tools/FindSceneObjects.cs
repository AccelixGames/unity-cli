using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Name = "find_scene_objects", Description = "Search GameObjects in loaded scenes by name, tag, layer, component, or active state.")]
    public static class FindSceneObjects
    {
        public class Parameters
        {
            [ToolParameter("Name filter (substring match)")]
            public string Name { get; set; }

            [ToolParameter("Tag filter (exact match)")]
            public string Tag { get; set; }

            [ToolParameter("Layer name filter")]
            public string Layer { get; set; }

            [ToolParameter("Component type filter (e.g. 'Camera', 'Rigidbody')")]
            public string Component { get; set; }

            [ToolParameter("Filter by active state: true, false, or all (default: all)")]
            public string Active { get; set; }

            [ToolParameter("Include component list per object (default: false)")]
            public bool IncludeComponents { get; set; }

            [ToolParameter("Max results (default: 50)")]
            public int MaxResults { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            var p = new ToolParams(@params ?? new JObject());
            var name = p.Get("name", "");
            var tag = p.Get("tag", "");
            var layer = p.Get("layer", "");
            var component = p.Get("component", "");
            var activeFilter = p.Get("active", "all").ToLowerInvariant();
            var includeComponents = p.GetBool("include_components");
            var max = p.GetInt("max_results", 50) ?? 50;

            Type componentType = null;
            if (!string.IsNullOrEmpty(component))
            {
                componentType = FindComponentType(component);
                if (componentType == null)
                    return new ErrorResponse($"Component type not found: {component}");
            }

            int layerIndex = -1;
            if (!string.IsNullOrEmpty(layer))
            {
                layerIndex = LayerMask.NameToLayer(layer);
                if (layerIndex < 0)
                    return new ErrorResponse($"Layer not found: {layer}");
            }

            var allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var results = new List<object>();

            foreach (var go in allObjects)
            {
                if (!string.IsNullOrEmpty(name) &&
                    go.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (!string.IsNullOrEmpty(tag) && !go.CompareTag(tag))
                    continue;

                if (layerIndex >= 0 && go.layer != layerIndex)
                    continue;

                if (componentType != null && go.GetComponent(componentType) == null)
                    continue;

                if (activeFilter == "true" && !go.activeInHierarchy)
                    continue;
                if (activeFilter == "false" && go.activeInHierarchy)
                    continue;

                var info = new Dictionary<string, object>
                {
                    ["name"] = go.name,
                    ["path"] = GetPath(go),
                    ["active"] = go.activeInHierarchy,
                    ["tag"] = go.tag,
                    ["layer"] = LayerMask.LayerToName(go.layer)
                };

                if (includeComponents)
                {
                    info["components"] = go.GetComponents<UnityEngine.Component>()
                        .Where(c => c != null)
                        .Select(c => c.GetType().Name)
                        .ToArray();
                }

                results.Add(info);
                if (results.Count >= max) break;
            }

            return new SuccessResponse($"Found {results.Count} objects.", results);
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

        private static Type FindComponentType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                try
                {
                    var type = assembly.GetTypes().FirstOrDefault(t =>
                        t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) &&
                        typeof(UnityEngine.Component).IsAssignableFrom(t));
                    if (type != null) return type;
                }
                catch { }
            }
            return null;
        }
    }
}
