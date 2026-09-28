using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

/// <summary>
/// EditMode テストを Unity 抜きで走らせる小さなランナー。
/// 引数はカンマ区切りのアセンブリ名(拡張子なし)。
/// </summary>
public static class Runner
{
    public static int Main(string[] args)
    {
        var names = args.Length > 0
            ? args[0].Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray()
            : new string[0];

        var skipFixtures = (Environment.GetEnvironmentVariable("SKIP_FIXTURES") ?? "")
            .Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();

        int passed = 0, failed = 0, skipped = 0;

        foreach (var name in names)
        {
            Assembly asm;
            try { asm = Assembly.LoadFrom(name + ".dll"); }
            catch (Exception e) { Console.WriteLine("LOAD FAIL " + name + ": " + e.Message); failed++; continue; }

            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

            foreach (var type in types.OrderBy(t => t.FullName))
            {
                if (type.IsAbstract || type.ContainsGenericParameters) continue;
                if (skipFixtures.Contains(type.Name)) continue;

                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                var tests = methods
                    .Where(m => m.GetCustomAttributes(typeof(TestAttribute), true).Any()
                             || m.GetCustomAttributes(typeof(TestCaseAttribute), true).Any())
                    .OrderBy(m => m.Name).ToArray();
                if (tests.Length == 0) continue;

                var setups = methods.Where(
                    m => m.GetCustomAttributes(typeof(SetUpAttribute), true).Any()).ToArray();
                var teardowns = methods.Where(
                    m => m.GetCustomAttributes(typeof(TearDownAttribute), true).Any()).ToArray();

                foreach (var test in tests)
                {
                    var cases = test.GetCustomAttributes(typeof(TestCaseAttribute), true)
                                    .Cast<TestCaseAttribute>().ToArray();
                    var invocations = cases.Length > 0
                        ? cases.Select(c => c.Args).ToArray()
                        : new object[][] { new object[0] };

                    foreach (var argv in invocations)
                    {
                        if (test.GetParameters().Length != argv.Length) { skipped++; continue; }

                        object instance = null;
                        try
                        {
                            instance = Activator.CreateInstance(type);
                            foreach (var s in setups) s.Invoke(instance, null);
                            var result = test.Invoke(instance, argv);
                            if (result is IEnumerator) { skipped++; }  // UnityTest は対象外
                            else passed++;
                            foreach (var t in teardowns) t.Invoke(instance, null);
                        }
                        catch (Exception e)
                        {
                            var inner = e is TargetInvocationException ? e.InnerException : e;
                            failed++;
                            Console.WriteLine("FAIL " + type.Name + "." + test.Name);
                            Console.WriteLine("     " + inner.GetType().Name + ": " + inner.Message);
                            try { foreach (var t in teardowns) t.Invoke(instance, null); }
                            catch { }
                        }
                    }
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine(passed + " passed, " + failed + " failed"
                          + (skipped > 0 ? ", " + skipped + " skipped" : ""));
        return failed == 0 ? 0 : 1;
    }
}
