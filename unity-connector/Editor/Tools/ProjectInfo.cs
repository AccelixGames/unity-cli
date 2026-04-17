using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Name = "project_info", Description = "Get project information. Actions: overview, packages, settings, scenes, quality.")]
    public static class ProjectInfo
    {
        private const int PackageListTimeoutMs = 30000;

        private static readonly HashSet<string> ValidActions = new HashSet<string>
        {
            "overview", "packages", "settings", "scenes", "quality"
        };

        public class Parameters
        {
            [ToolParameter("Action: overview, packages, settings, scenes, quality", Required = true)]
            public string Action { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
                return new ErrorResponse("Parameters cannot be null.");

            var p = new ToolParams(@params);
            var actionResult = p.GetRequired("action");
            if (!actionResult.IsSuccess)
                return new ErrorResponse(actionResult.ErrorMessage);

            string action = actionResult.Value.ToLowerInvariant();
            if (!ValidActions.Contains(action))
                return new ErrorResponse($"Unknown action: '{actionResult.Value}'. Valid actions: {string.Join(", ", ValidActions)}");

            switch (action)
            {
                case "overview": return GetOverview();
                case "packages": return GetPackages();
                case "settings": return GetSettings();
                case "scenes": return GetScenes();
                case "quality": return GetQuality();
                default: return new ErrorResponse($"Unknown action: '{actionResult.Value}'.");
            }
        }

        private static object GetOverview()
        {
            try
            {
                string renderPipeline;
                try
                {
                    renderPipeline = GraphicsSettings.currentRenderPipeline != null
                        ? GraphicsSettings.currentRenderPipeline.GetType().Name
                        : "Built-in";
                }
                catch
                {
                    renderPipeline = "Unknown";
                }

                var info = new
                {
                    unityVersion = Application.unityVersion,
                    productName = Application.productName,
                    companyName = Application.companyName,
                    dataPath = Application.dataPath,
                    platform = EditorUserBuildSettings.activeBuildTarget.ToString(),
                    colorSpace = PlayerSettings.colorSpace.ToString(),
                    scriptingBackend = PlayerSettings.GetScriptingBackend(EditorUserBuildSettings.selectedBuildTargetGroup).ToString(),
                    apiCompatibility = PlayerSettings.GetApiCompatibilityLevel(EditorUserBuildSettings.selectedBuildTargetGroup).ToString(),
                    renderPipeline
                };

                return new SuccessResponse("Project overview", info);
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to read project overview: {ex.Message}");
            }
        }

        private static object GetPackages()
        {
            var listRequest = Client.List(true);
            var startTime = DateTime.UtcNow;

            while (!listRequest.IsCompleted)
            {
                if ((DateTime.UtcNow - startTime).TotalMilliseconds > PackageListTimeoutMs)
                    return new ErrorResponse("Package list request timed out.");
            }

            if (listRequest.Status != StatusCode.Success)
            {
                var errorMsg = listRequest.Error != null ? listRequest.Error.message : "Unknown error";
                return new ErrorResponse($"Failed to list packages: {errorMsg}");
            }

            var packages = listRequest.Result.Select(pkg => new
            {
                name = pkg.name,
                version = pkg.version,
                source = pkg.source.ToString(),
                displayName = pkg.displayName
            }).OrderBy(pkg => pkg.name).ToArray();

            return new SuccessResponse($"Found {packages.Length} packages.", packages);
        }

        private static object GetSettings()
        {
            try
            {
                var settings = new
                {
                    // Player
                    productName = PlayerSettings.productName,
                    companyName = PlayerSettings.companyName,
                    bundleVersion = PlayerSettings.bundleVersion,
                    defaultScreenWidth = PlayerSettings.defaultScreenWidth,
                    defaultScreenHeight = PlayerSettings.defaultScreenHeight,
                    fullscreen = PlayerSettings.fullScreenMode.ToString(),
                    runInBackground = PlayerSettings.runInBackground,

                    // Build
                    buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                    buildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup.ToString(),
                    development = EditorUserBuildSettings.development,

                    // Physics
                    gravity = Physics.gravity.ToString(),
                    defaultSolverIterations = Physics.defaultSolverIterations,

                    // Time
                    fixedDeltaTime = Time.fixedDeltaTime,
                    timeScale = Time.timeScale
                };

                return new SuccessResponse("Project settings", settings);
            }
            catch (Exception ex)
            {
                return new ErrorResponse($"Failed to read project settings: {ex.Message}");
            }
        }

        private static object GetScenes()
        {
            var scenes = new List<object>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                scenes.Add(new
                {
                    path = scene.path,
                    enabled = scene.enabled,
                    name = System.IO.Path.GetFileNameWithoutExtension(scene.path)
                });
            }

            return new SuccessResponse($"Found {scenes.Count} scenes in build settings.", scenes);
        }

        private static object GetQuality()
        {
            var names = QualitySettings.names;
            var current = QualitySettings.GetQualityLevel();
            var levels = new List<object>();

            for (int i = 0; i < names.Length; i++)
            {
                levels.Add(new
                {
                    index = i,
                    name = names[i],
                    isCurrent = i == current
                });
            }

            string currentLevelName = (current >= 0 && current < names.Length)
                ? names[current]
                : $"Unknown ({current})";

            string renderPipeline;
            try
            {
                renderPipeline = GraphicsSettings.currentRenderPipeline != null
                    ? GraphicsSettings.currentRenderPipeline.name
                    : "Built-in";
            }
            catch
            {
                renderPipeline = "Unknown";
            }

            var info = new
            {
                currentLevel = currentLevelName,
                renderPipeline,
                vSyncCount = QualitySettings.vSyncCount,
                antiAliasing = QualitySettings.antiAliasing,
                shadowResolution = QualitySettings.shadowResolution.ToString(),
                levels
            };

            return new SuccessResponse("Quality settings", info);
        }
    }
}
