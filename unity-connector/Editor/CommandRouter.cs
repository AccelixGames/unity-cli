using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityCliConnector
{
    /// <summary>
    /// Routes incoming command requests to the appropriate tool handler.
    /// Handles both sync and async handlers, with main-thread marshaling.
    /// </summary>
    public static class CommandRouter
    {
        public static async Task<object> Dispatch(string command, JObject parameters)
        {
            if (command == "list_tools")
            {
                return new SuccessResponse("Available tools", ToolDiscovery.GetToolSchemas());
            }

            if (command == "tool_help")
            {
                var name = parameters?["name"]?.ToString();
                if (name == null) return new ErrorResponse("Missing 'name' parameter");
                if (ToolDiscovery.Tools.TryGetValue(name, out var info) == false)
                    return new ErrorResponse($"Unknown tool: {name}");
                return new SuccessResponse(info.Description, new
                {
                    name = info.Name,
                    description = info.Description,
                    group = info.Group,
                    parameters = ToolDiscovery.GetParameterSchema(info.ParametersType),
                });
            }

            if (ToolDiscovery.Tools.TryGetValue(command, out var tool) == false)
            {
                return new ErrorResponse($"Unknown command: {command}");
            }

            try
            {
                var result = tool.Handler.Invoke(null, new object[] { parameters ?? new JObject() });

                if (result is Task<object> asyncTask)
                {
                    return await asyncTask;
                }

                if (result is Task task)
                {
                    await task;
                    return new SuccessResponse($"{command} completed");
                }

                return result ?? new SuccessResponse($"{command} completed");
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                Debug.LogException(inner);
                return new ErrorResponse($"{command} failed: {inner.Message}");
            }
        }
    }
}
