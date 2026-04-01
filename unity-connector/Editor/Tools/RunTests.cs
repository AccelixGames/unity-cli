using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityCliConnector;

namespace UnityCliConnector.Tools
{
    [UnityCliTool(Name = "run_tests", Description = "Run NUnit tests from Assembly-CSharp. Discovers [Test] methods via reflection.")]
    public static class RunTestsTool
    {
        public class Parameters
        {
            [ToolParameter("Filter test class or method name (substring match)")]
            public string Filter { get; set; }
        }

        public static object HandleCommand(JObject @params)
        {
            var p = new ToolParams(@params);
            var filter = p.Get("filter", "");

            var testMethods = DiscoverTests(filter);

            if (testMethods.Count == 0)
                return new ErrorResponse($"No tests found matching filter: '{filter}'");

            var passed = new List<string>();
            var failed = new List<object>();
            var skipped = 0;

            foreach (var method in testMethods)
            {
                var fullName = $"{method.DeclaringType.FullName}.{method.Name}";

                try
                {
                    var instance = Activator.CreateInstance(method.DeclaringType);

                    // Run [SetUp] methods
                    foreach (var setup in GetMethodsWithAttribute<SetUpAttribute>(method.DeclaringType))
                        setup.Invoke(instance, null);

                    // Run test
                    method.Invoke(instance, null);

                    // Run [TearDown] methods
                    foreach (var teardown in GetMethodsWithAttribute<TearDownAttribute>(method.DeclaringType))
                        teardown.Invoke(instance, null);

                    passed.Add(fullName);
                }
                catch (TargetInvocationException tie)
                {
                    var inner = tie.InnerException;

                    if (inner is IgnoreException)
                    {
                        skipped++;
                        continue;
                    }

                    failed.Add(new
                    {
                        test = fullName,
                        error = inner?.Message ?? tie.Message
                    });
                }
                catch (Exception e)
                {
                    failed.Add(new
                    {
                        test = fullName,
                        error = e.Message
                    });
                }
            }

            var result = new
            {
                total = passed.Count + failed.Count + skipped,
                passed = passed.Count,
                failed = failed.Count,
                skipped,
                passes = passed,
                failures = failed
            };

            var message = failed.Count == 0
                ? $"All {passed.Count} tests passed."
                : $"{failed.Count} failed, {passed.Count} passed.";

            return new SuccessResponse(message, result);
        }

        private static List<MethodInfo> DiscoverTests(string filter)
        {
            var tests = new List<MethodInfo>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                var name = assembly.GetName().Name;
                if (name != "Assembly-CSharp" && name != "Assembly-CSharp-Editor")
                    continue;

                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if (method.GetCustomAttribute<TestAttribute>() == null &&
                                method.GetCustomAttribute<TestCaseAttribute>() == null)
                                continue;

                            if (!string.IsNullOrEmpty(filter))
                            {
                                var fullName = $"{type.FullName}.{method.Name}";
                                if (fullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                                    continue;
                            }

                            tests.Add(method);
                        }
                    }
                }
                catch
                {
                    // Skip assemblies that can't be reflected
                }
            }

            return tests;
        }

        private static IEnumerable<MethodInfo> GetMethodsWithAttribute<T>(Type type) where T : Attribute
        {
            return type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<T>() != null);
        }
    }
}
