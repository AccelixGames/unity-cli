using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Name = "multi_screenshot", Description = "Capture scene view from multiple angles (Front, Right, Top, Isometric). Returns PNG file paths.")]
    public static class MultiScreenshot
    {
        public class Parameters
        {
            [ToolParameter("Target GameObject to focus on (optional)")]
            public string Target { get; set; }

            [ToolParameter("Distance from target (default: 10)")]
            public float Distance { get; set; }

            [ToolParameter("Image width (default: 512)")]
            public int Width { get; set; }

            [ToolParameter("Image height (default: 512)")]
            public int Height { get; set; }

            [ToolParameter("Output directory (default: Temp/Screenshots)")]
            public string OutputDir { get; set; }
        }

        private struct ViewAngle
        {
            public string Name;
            public Quaternion Rotation;
            public bool Orthographic;
        }

        public static object HandleCommand(JObject @params)
        {
            var p = new ToolParams(@params ?? new JObject());
            var targetName = p.Get("target", "");
            var distance = p.GetFloat("distance", 10f) ?? 10f;
            var width = p.GetInt("width", 512) ?? 512;
            var height = p.GetInt("height", 512) ?? 512;
            var outputDir = p.Get("output_dir", "Temp/Screenshots");

            Vector3 focusPoint = Vector3.zero;
            if (!string.IsNullOrEmpty(targetName))
            {
                var target = GameObject.Find(targetName);
                if (target == null)
                    return new ErrorResponse($"Target not found: {targetName}");
                focusPoint = target.transform.position;
            }

            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            var angles = new ViewAngle[]
            {
                new ViewAngle { Name = "Front", Rotation = Quaternion.Euler(0, 0, 0), Orthographic = true },
                new ViewAngle { Name = "Right", Rotation = Quaternion.Euler(0, 90, 0), Orthographic = true },
                new ViewAngle { Name = "Top", Rotation = Quaternion.Euler(90, 0, 0), Orthographic = true },
                new ViewAngle { Name = "Isometric", Rotation = Quaternion.Euler(30, 45, 0), Orthographic = false }
            };

            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                return new ErrorResponse("No active Scene View found.");

            // Save original state
            var origPivot = sceneView.pivot;
            var origRotation = sceneView.rotation;
            var origSize = sceneView.size;
            var origOrtho = sceneView.orthographic;

            var savedPaths = new List<string>();

            try
            {
                foreach (var angle in angles)
                {
                    sceneView.pivot = focusPoint;
                    sceneView.rotation = angle.Rotation;
                    sceneView.size = distance;
                    sceneView.orthographic = angle.Orthographic;
                    sceneView.Repaint();

                    var camera = sceneView.camera;
                    if (camera == null) continue;

                    var rt = new RenderTexture(width, height, 24);
                    camera.targetTexture = rt;
                    camera.Render();

                    RenderTexture.active = rt;
                    var tex = new Texture2D(width, height, TextureFormat.RGB24, false, false);
                    tex.ReadPixels(new Rect(0, 0, (float)width, (float)height), 0, 0);
                    tex.Apply();

                    var bytes = tex.EncodeToPNG();
                    var filePath = Path.Combine(outputDir, $"SceneView_{angle.Name}.png");
                    File.WriteAllBytes(filePath, bytes);
                    savedPaths.Add(filePath);

                    camera.targetTexture = null;
                    RenderTexture.active = null;
                    Object.DestroyImmediate(tex);
                    Object.DestroyImmediate(rt);
                }
            }
            finally
            {
                // Restore original state
                sceneView.pivot = origPivot;
                sceneView.rotation = origRotation;
                sceneView.size = origSize;
                sceneView.orthographic = origOrtho;
                sceneView.Repaint();
            }

            return new SuccessResponse($"Captured {savedPaths.Count} screenshots.", savedPaths);
        }
    }
}
