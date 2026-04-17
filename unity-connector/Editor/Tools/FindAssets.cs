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
            var pathResult = p.GetRequired("path", "'path' parameter required.");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            var path = pathResult.Value;
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null)
                return new ErrorResponse($"Asset not found: {path}");

            var guid = AssetDatabase.AssetPathToGUID(path);
            var type = asset.GetType();
            var labels = AssetDatabase.GetLabels(asset);

            long fileSize = 0;
            string lastModified = "N/A";
            try
            {
                var fileInfo = new FileInfo(path);
                if (fileInfo.Exists)
                {
                    fileSize = fileInfo.Length;
                    lastModified = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
                }
            }
            catch (System.Exception) { }

            var info = new
            {
                path,
                name = asset.name,
                type = type.FullName,
                guid,
                labels,
                fileSize,
                lastModified
            };

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
            var pathResult = p.GetRequired("path", "'path' parameter required.");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            var asset = AssetDatabase.LoadMainAssetAtPath(pathResult.Value);
            if (asset == null)
                return new ErrorResponse($"Asset not found: {pathResult.Value}");

            var labels = AssetDatabase.GetLabels(asset);
            return new SuccessResponse($"Labels for '{pathResult.Value}'", labels);
        }

        private static object SetLabels(ToolParams p)
        {
            var pathResult = p.GetRequired("path", "'path' parameter required.");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            var labelsResult = p.GetRequired("labels", "'labels' parameter required.");
            if (!labelsResult.IsSuccess)
                return new ErrorResponse(labelsResult.ErrorMessage);

            var asset = AssetDatabase.LoadMainAssetAtPath(pathResult.Value);
            if (asset == null)
                return new ErrorResponse($"Asset not found: {pathResult.Value}");

            var labels = labelsResult.Value.Split(',').Select(l => l.Trim()).ToArray();
            AssetDatabase.SetLabels(asset, labels);
            return new SuccessResponse($"Set {labels.Length} labels on '{pathResult.Value}'", labels);
        }

        private static object GetDependencies(ToolParams p)
        {
            var pathResult = p.GetRequired("path", "'path' parameter required.");
            if (!pathResult.IsSuccess)
                return new ErrorResponse(pathResult.ErrorMessage);

            var max = p.GetInt("max_results", 30) ?? 30;
            var path = pathResult.Value;

            string[] deps;
            try
            {
                deps = AssetDatabase.GetDependencies(path, false);
            }
            catch (System.Exception ex)
            {
                return new ErrorResponse($"Failed to get dependencies for '{path}': {ex.Message}");
            }

            var filtered = deps.Where(d => d != path).ToArray();
            var results = filtered.Take(max).Select(d => new
            {
                path = d,
                type = AssetDatabase.GetMainAssetTypeAtPath(d)?.Name ?? "Unknown"
            }).ToArray();

            var message = filtered.Length > max
                ? $"Found {filtered.Length} dependencies (showing {results.Length})."
                : $"Found {results.Length} dependencies.";

            return new SuccessResponse(message, results);
        }
    }
}
