using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Name = "manage_component", Description = "Manage components on GameObjects. Actions: add, remove, get, set, list_types.")]
    public static class ManageComponent
    {
        public class Parameters
        {
            [ToolParameter("Action: add, remove, get, set, list_types", Required = true)]
            public string Action { get; set; }

            [ToolParameter("Target GameObject name or path", Required = true)]
            public string GameObject { get; set; }

            [ToolParameter("Component type name (e.g. 'BoxCollider', 'Rigidbody')")]
            public string ComponentType { get; set; }

            [ToolParameter("Property name to get/set")]
            public string Property { get; set; }

            [ToolParameter("Value to set (string, will be parsed to target type)")]
            public string Value { get; set; }

            [ToolParameter("Search keyword for list_types")]
            public string Search { get; set; }
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
                case "add": return Add(p);
                case "remove": return Remove(p);
                case "get": return Get(p);
                case "set": return Set(p);
                case "list_types": return ListTypes(p);
                default: return new ErrorResponse($"Unknown action: {actionResult.Value}");
            }
        }

        private static object Add(ToolParams p)
        {
            var go = FindGameObject(p);
            if (go == null) return GameObjectNotFound(p);

            var typeResult = p.GetRequired("component_type");
            if (!typeResult.IsSuccess) return new ErrorResponse(typeResult.ErrorMessage);

            var type = FindType(typeResult.Value);
            if (type == null) return new ErrorResponse($"Component type not found: {typeResult.Value}");

            Undo.AddComponent(go, type);
            return new SuccessResponse($"Added {type.Name} to '{go.name}'");
        }

        private static object Remove(ToolParams p)
        {
            var go = FindGameObject(p);
            if (go == null) return GameObjectNotFound(p);

            var typeResult = p.GetRequired("component_type");
            if (!typeResult.IsSuccess) return new ErrorResponse(typeResult.ErrorMessage);

            var type = FindType(typeResult.Value);
            if (type == null) return new ErrorResponse($"Component type not found: {typeResult.Value}");

            var comp = go.GetComponent(type);
            if (comp == null) return new ErrorResponse($"Component {type.Name} not found on '{go.name}'");

            Undo.DestroyObjectImmediate(comp);
            return new SuccessResponse($"Removed {type.Name} from '{go.name}'");
        }

        private static object Get(ToolParams p)
        {
            var go = FindGameObject(p);
            if (go == null) return GameObjectNotFound(p);

            var typeName = p.Get("component_type", "");
            var propertyName = p.Get("property", "");

            if (string.IsNullOrEmpty(typeName))
            {
                // List all components
                var components = go.GetComponents<Component>()
                    .Where(c => c != null)
                    .Select(c => new { type = c.GetType().Name, properties = GetSerializedProperties(c) })
                    .ToList();
                return new SuccessResponse($"Components on '{go.name}'", components);
            }

            var type = FindType(typeName);
            if (type == null) return new ErrorResponse($"Component type not found: {typeName}");

            var component = go.GetComponent(type);
            if (component == null) return new ErrorResponse($"Component {type.Name} not found on '{go.name}'");

            if (!string.IsNullOrEmpty(propertyName))
            {
                var so = new SerializedObject(component);
                var prop = so.FindProperty(propertyName);
                if (prop == null) return new ErrorResponse($"Property '{propertyName}' not found on {type.Name}");
                return new SuccessResponse($"{type.Name}.{propertyName}", GetPropertyValue(prop));
            }

            return new SuccessResponse($"{type.Name} on '{go.name}'", GetSerializedProperties(component));
        }

        private static object Set(ToolParams p)
        {
            var go = FindGameObject(p);
            if (go == null) return GameObjectNotFound(p);

            var typeResult = p.GetRequired("component_type");
            if (!typeResult.IsSuccess) return new ErrorResponse(typeResult.ErrorMessage);
            var propResult = p.GetRequired("property");
            if (!propResult.IsSuccess) return new ErrorResponse(propResult.ErrorMessage);
            var valueResult = p.GetRequired("value");
            if (!valueResult.IsSuccess) return new ErrorResponse(valueResult.ErrorMessage);

            var type = FindType(typeResult.Value);
            if (type == null) return new ErrorResponse($"Component type not found: {typeResult.Value}");

            var component = go.GetComponent(type);
            if (component == null) return new ErrorResponse($"Component {type.Name} not found on '{go.name}'");

            var so = new SerializedObject(component);
            var prop = so.FindProperty(propResult.Value);
            if (prop == null) return new ErrorResponse($"Property '{propResult.Value}' not found");

            if (!SetPropertyValue(prop, valueResult.Value))
                return new ErrorResponse($"Failed to set '{propResult.Value}' to '{valueResult.Value}'");

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(component);
            return new SuccessResponse($"Set {type.Name}.{propResult.Value} = {valueResult.Value}");
        }

        private static object ListTypes(ToolParams p)
        {
            var search = p.Get("search", "");
            var types = new List<string>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (!typeof(Component).IsAssignableFrom(type)) continue;
                        if (type.IsAbstract) continue;
                        if (!string.IsNullOrEmpty(search) &&
                            type.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        types.Add(type.FullName);
                        if (types.Count >= 50) break;
                    }
                }
                catch { }
                if (types.Count >= 50) break;
            }

            types.Sort();
            return new SuccessResponse($"Found {types.Count} component types.", types);
        }

        private static GameObject FindGameObject(ToolParams p)
        {
            var name = p.Get("gameobject", "");
            if (string.IsNullOrEmpty(name)) return null;
            var go = UnityEngine.GameObject.Find(name);
            if (go != null) return go;
            return UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None)
                .FirstOrDefault(g => g.name == name);
        }

        private static ErrorResponse GameObjectNotFound(ToolParams p)
        {
            return new ErrorResponse($"GameObject not found: {p.Get("gameobject", "")}");
        }

        private static Type FindType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                try
                {
                    var type = assembly.GetType(typeName);
                    if (type != null && typeof(Component).IsAssignableFrom(type)) return type;

                    type = assembly.GetTypes().FirstOrDefault(t =>
                        t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) &&
                        typeof(Component).IsAssignableFrom(t));
                    if (type != null) return type;
                }
                catch { }
            }
            return null;
        }

        private static object GetSerializedProperties(Component comp)
        {
            var so = new SerializedObject(comp);
            var props = new Dictionary<string, object>();
            var iter = so.GetIterator();
            iter.NextVisible(true);
            int count = 0;
            do
            {
                props[iter.propertyPath] = GetPropertyValue(iter);
                if (++count > 30) break;
            } while (iter.NextVisible(false));
            return props;
        }

        private static object GetPropertyValue(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: return prop.intValue;
                case SerializedPropertyType.Boolean: return prop.boolValue;
                case SerializedPropertyType.Float: return prop.floatValue;
                case SerializedPropertyType.String: return prop.stringValue;
                case SerializedPropertyType.Enum: return prop.enumNames[prop.enumValueIndex];
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue != null ? prop.objectReferenceValue.name : null;
                case SerializedPropertyType.Vector2:
                    return $"{prop.vector2Value.x},{prop.vector2Value.y}";
                case SerializedPropertyType.Vector3:
                    return $"{prop.vector3Value.x},{prop.vector3Value.y},{prop.vector3Value.z}";
                case SerializedPropertyType.Color:
                    var c = prop.colorValue;
                    return $"{c.r},{c.g},{c.b},{c.a}";
                default:
                    return $"({prop.propertyType})";
            }
        }

        private static bool SetPropertyValue(SerializedProperty prop, string value)
        {
            try
            {
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        prop.intValue = int.Parse(value);
                        return true;
                    case SerializedPropertyType.Boolean:
                        prop.boolValue = bool.Parse(value);
                        return true;
                    case SerializedPropertyType.Float:
                        prop.floatValue = float.Parse(value);
                        return true;
                    case SerializedPropertyType.String:
                        prop.stringValue = value;
                        return true;
                    case SerializedPropertyType.Enum:
                        var idx = Array.IndexOf(prop.enumNames, value);
                        if (idx >= 0) { prop.enumValueIndex = idx; return true; }
                        if (int.TryParse(value, out var enumInt)) { prop.enumValueIndex = enumInt; return true; }
                        return false;
                    case SerializedPropertyType.Vector3:
                        var parts = value.Split(',');
                        if (parts.Length == 3)
                        {
                            prop.vector3Value = new Vector3(
                                float.Parse(parts[0].Trim()),
                                float.Parse(parts[1].Trim()),
                                float.Parse(parts[2].Trim()));
                            return true;
                        }
                        return false;
                    default:
                        return false;
                }
            }
            catch { return false; }
        }
    }
}
