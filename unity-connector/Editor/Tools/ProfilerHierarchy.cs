using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Name = "profiler_hierarchy",
        Description = "[ReadOnly] Hierarchical profiler drill-down with recursive depth support.")]
    public static class ProfilerHierarchy
    {
        public class Parameters
        {
            [ToolParameter("Frame index to inspect. -1 or omit = last captured frame.")]
            public int Frame { get; set; }

            [ToolParameter("Thread index. 0 = main thread.")]
            public int ThreadIndex { get; set; }

            [ToolParameter("Parent item ID to drill into. Omit for root level.")]
            public int ParentId { get; set; }

            [ToolParameter("Minimum total time (ms) filter.")]
            public float MinTime { get; set; }

            [ToolParameter("Sort column: 'total', 'self', or 'calls'. Default 'total'.")]
            public string SortBy { get; set; }

            [ToolParameter("Max children per level. Default 30.")]
            public int MaxItems { get; set; }

            [ToolParameter("Recursive depth. 1 = one level (default), 0 = unlimited.")]
            public int Depth { get; set; }
        }

        public static object HandleCommand(JObject parameters)
        {
            if (ProfilerDriver.enabled == false && ProfilerDriver.lastFrameIndex < 0)
                return new ErrorResponse("Profiler has no captured data. Enable profiler and capture frames first.");

            var p = new ToolParams(parameters);

            var frameIndex = p.GetInt("frame", -1).Value;
            if (frameIndex < 0) frameIndex = ProfilerDriver.lastFrameIndex;
            if (frameIndex < ProfilerDriver.firstFrameIndex || frameIndex > ProfilerDriver.lastFrameIndex)
                return new ErrorResponse(
                    $"Frame {frameIndex} out of range [{ProfilerDriver.firstFrameIndex}..{ProfilerDriver.lastFrameIndex}]");

            var threadIndex = p.GetInt("threadIndex", 0).Value;
            var parentIdToken = p.GetRaw("parentId");
            var minTime = p.GetFloat("minTime", 0f).Value;
            var sortBy = (p.Get("sortBy", "total")).ToLowerInvariant();
            var maxItems = p.GetInt("maxItems", 30).Value;
            if (maxItems <= 0) maxItems = 30;
            var depth = p.GetInt("depth", 1).Value;
            if (depth <= 0) depth = 999;

            int sortColumn;
            switch (sortBy)
            {
                case "self": sortColumn = HierarchyFrameDataView.columnSelfTime; break;
                case "calls": sortColumn = HierarchyFrameDataView.columnCalls; break;
                default: sortColumn = HierarchyFrameDataView.columnTotalTime; break;
            }

            using var frameData = ProfilerDriver.GetHierarchyFrameDataView(
                frameIndex, threadIndex,
                HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                sortColumn, false);

            if (frameData == null || frameData.valid == false)
                return new ErrorResponse($"No profiler data for frame {frameIndex}, thread {threadIndex}.");

            int parentId;
            if (parentIdToken == null || parentIdToken.Type == JTokenType.Null)
                parentId = frameData.GetRootItemID();
            else
                parentId = parentIdToken.Value<int>();

            var items = BuildChildren(frameData, parentId, minTime, maxItems, depth);

            var parentName = parentIdToken != null && parentIdToken.Type != JTokenType.Null
                ? frameData.GetItemName(parentId)
                : "(root)";

            var result = new JObject
            {
                ["frame"] = frameIndex,
                ["threadIndex"] = threadIndex,
                ["parentId"] = parentId,
                ["parentName"] = parentName,
                ["depth"] = depth >= 999 ? 0 : depth,
                ["children"] = items,
            };

            return new SuccessResponse($"Hierarchy of '{parentName}' (frame {frameIndex})", result);
        }

        static JArray BuildChildren(HierarchyFrameDataView frameData, int parentId, float minTime, int maxItems, int remainingDepth)
        {
            var childIds = new List<int>();
            frameData.GetItemChildren(parentId, childIds);

            var items = new JArray();
            int shown = 0;
            foreach (var childId in childIds)
            {
                var totalTime = frameData.GetItemColumnDataAsFloat(childId, HierarchyFrameDataView.columnTotalTime);
                if (totalTime < minTime) continue;
                if (shown >= maxItems) break;
                shown++;

                var selfTime = frameData.GetItemColumnDataAsFloat(childId, HierarchyFrameDataView.columnSelfTime);
                var calls = (int)frameData.GetItemColumnDataAsFloat(childId, HierarchyFrameDataView.columnCalls);

                var item = new JObject
                {
                    ["itemId"] = childId,
                    ["name"] = frameData.GetItemName(childId),
                    ["totalMs"] = System.Math.Round(totalTime, 3),
                    ["selfMs"] = System.Math.Round(selfTime, 3),
                    ["calls"] = calls,
                };

                if (remainingDepth > 1)
                {
                    var subChildren = BuildChildren(frameData, childId, minTime, maxItems, remainingDepth - 1);
                    if (subChildren.Count > 0)
                        item["children"] = subChildren;
                }

                items.Add(item);
            }

            return items;
        }
    }
}
