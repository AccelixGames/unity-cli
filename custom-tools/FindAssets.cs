using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Name = "find_assets", Description = "Search and inspect project assets. Actions: search, get_info, get_labels, set_labels, get_dependencies.")]
    public static class FindAssets
    {
        public class Parameters
        {
            [ToolParameter("Action: search, get_info, get_labels, set_labels, get_dependencies", Required = true)]
            public string Action { get; set; }

            [ToolParameter("Search filter (e.g. 't:Material', 't:Prefab wood', 'PlayerController')")]
            public string Filter { get; set; }

            [ToolParameter("Folder to search in (e.g. 'Assets/Prefabs')")]
            public string Folder { get; set; }

            [ToolParameter("Asset path for get_info/get_labels/get_dependencies")]
            public string Path { get; set; }

            [ToolParameter("Comma-separated labels for set_labels")]
            public string Labels { get; set; }

            [ToolParameter("Max results (default: 30)")]
            public int MaxResults { get; set; }
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
                case "search": return Search(p);
                case "get_info": return GetInfo(p);
                case "get_labels": return GetLabels(p);
                case "set_labels": return SetLabels(p);
                case "get_dependencies": return GetDependencies(p);
                default: return new ErrorResponse($"Unknown action: {actionResult.Value}");
            }
        }

        private static object Search(ToolParams p)
        {
            var filter = p.Get("filter", "");
            var folder = p.Get("folder", "");
            var max = p.GetInt("max_results", 30) ?? 30;

            string[] guids;
            if (!string.IsNullOrEmpty(folder))
                guids = AssetDatabase.FindAssets(filter, new[] { folder });
            else
                guids = AssetDatabase.FindAssets(filter);

            var results = new List<object>();
            foreach (var guid in guids)
            {
                if (results.Count >= max) break;
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                results.Add(new
                {
                    path,
                    type = type != null ? type.Name : "Unknown",
                    name = System.IO.Path.GetFileNameWithoutExtension(path)
                });
            }

            return new SuccessResponse($"Found {guids.Length} assets (showing {results.Count}).", results);
        }

        private static object GetInfo(ToolParams p)
        {
            var path = p.Get("path", "");
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("'path' parameter required.");

            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null)
                return new ErrorResponse($"Asset not found: {path}");

            var guid = AssetDatabase.AssetPathToGUID(path);
            var type = asset.GetType();
            var labels = AssetDatabase.GetLabels(asset);
            var fileInfo = new FileInfo(path);

            var info = new
            {
                path,
                name = asset.name,
                type = type.FullName,
                guid,
                labels,
                fileSize = fileInfo.Exists ? fileInfo.Length : 0,
                lastModified = fileInfo.Exists ? fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") : "N/A"
            };

            // Add extra info for specific types
            if (asset is GameObject go)
            {
                var components = go.GetComponents<Component>()
                    .Where(c => c != null)
                    .Select(c => c.GetType().Name)
                    .ToArray();
                return new SuccessResponse($"Asset info: {path}",
                    new { info.path, info.name, info.type, info.guid, info.labels, info.fileSize, info.lastModified, components });
            }

            return new SuccessResponse($"Asset info: {path}", info);
        }

        private static object GetLabels(ToolParams p)
        {
            var path = p.Get("path", "");
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("'path' parameter required.");

            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null)
                return new ErrorResponse($"Asset not found: {path}");

            var labels = AssetDatabase.GetLabels(asset);
            return new SuccessResponse($"Labels for '{path}'", labels);
        }

        private static object SetLabels(ToolParams p)
        {
            var path = p.Get("path", "");
            var labelsStr = p.Get("labels", "");
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("'path' parameter required.");
            if (string.IsNullOrEmpty(labelsStr))
                return new ErrorResponse("'labels' parameter required.");

            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null)
                return new ErrorResponse($"Asset not found: {path}");

            var labels = labelsStr.Split(',').Select(l => l.Trim()).ToArray();
            AssetDatabase.SetLabels(asset, labels);
            return new SuccessResponse($"Set {labels.Length} labels on '{path}'", labels);
        }

        private static object GetDependencies(ToolParams p)
        {
            var path = p.Get("path", "");
            if (string.IsNullOrEmpty(path))
                return new ErrorResponse("'path' parameter required.");

            var deps = AssetDatabase.GetDependencies(path, false);
            var results = deps.Where(d => d != path).Select(d => new
            {
                path = d,
                type = AssetDatabase.GetMainAssetTypeAtPath(d)?.Name ?? "Unknown"
            }).ToArray();

            return new SuccessResponse($"Found {results.Length} dependencies.", results);
        }
    }
}
