// A real (asserting) NUnit shim, so pure-C# EditMode tests can actually be RUN
// outside Unity. Only the subset this project uses.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace NUnit.Framework
{
    [AttributeUsage(AttributeTargets.Method)] public sealed class TestAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class SetUpAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class TearDownAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class OneTimeSetUpAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class OneTimeTearDownAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class TestFixtureAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class TestCaseAttribute : Attribute { public TestCaseAttribute(params object[] args) { Args = args; } public object[] Args; }

    public sealed class AssertionException : Exception { public AssertionException(string m) : base(m) { } }

    public delegate void TestDelegate();

    public static class Assert
    {
        private static string Msg(string message, object[] args)
        {
            if (string.IsNullOrEmpty(message)) return "";
            return "  — " + (args != null && args.Length > 0 ? string.Format(message, args) : message);
        }

        private static void Fail0(string text) { throw new AssertionException(text); }

        private static bool Eq(object a, object b)
        {
            if (a == null || b == null) return ReferenceEquals(a, b);
            if (a is IConvertible && b is IConvertible && !(a is string) && !(b is string))
            {
                try { return Convert.ToDouble(a) == Convert.ToDouble(b); } catch { }
            }
            return a.Equals(b);
        }

        public static void AreEqual(object e, object a) { AreEqual(e, a, null); }
        public static void AreEqual(object e, object a, string m, params object[] args)
        { if (!Eq(e, a)) Fail0($"Expected: {e ?? "null"}  But was: {a ?? "null"}{Msg(m, args)}"); }

        public static void AreEqual(double e, double a, double delta) { AreEqual(e, a, delta, null); }
        public static void AreEqual(double e, double a, double delta, string m, params object[] args)
        { if (double.IsNaN(a) || Math.Abs(e - a) > delta) Fail0($"Expected: {e} +/- {delta}  But was: {a}{Msg(m, args)}"); }

        public static void AreNotEqual(object e, object a) { AreNotEqual(e, a, null); }
        public static void AreNotEqual(object e, object a, string m, params object[] args)
        { if (Eq(e, a)) Fail0($"Expected: not {e ?? "null"}  But was: {a ?? "null"}{Msg(m, args)}"); }

        public static void AreSame(object e, object a) { AreSame(e, a, null); }
        public static void AreSame(object e, object a, string m, params object[] args)
        { if (!ReferenceEquals(e, a)) Fail0($"Expected same instance{Msg(m, args)}"); }

        public static void AreNotSame(object e, object a) { AreNotSame(e, a, null); }
        public static void AreNotSame(object e, object a, string m, params object[] args)
        { if (ReferenceEquals(e, a)) Fail0($"Expected different instances{Msg(m, args)}"); }

        public static void IsTrue(bool c) { IsTrue(c, null); }
        public static void IsTrue(bool c, string m, params object[] args)
        { if (!c) Fail0($"Expected: True  But was: False{Msg(m, args)}"); }

        public static void IsFalse(bool c) { IsFalse(c, null); }
        public static void IsFalse(bool c, string m, params object[] args)
        { if (c) Fail0($"Expected: False  But was: True{Msg(m, args)}"); }

        public static void IsNull(object o) { IsNull(o, null); }
        public static void IsNull(object o, string m, params object[] args)
        { if (o != null) Fail0($"Expected: null  But was: {o}{Msg(m, args)}"); }

        public static void IsNotNull(object o) { IsNotNull(o, null); }
        public static void IsNotNull(object o, string m, params object[] args)
        { if (o == null) Fail0($"Expected: not null{Msg(m, args)}"); }

        public static void IsInstanceOf<T>(object a) { IsInstanceOf<T>(a, null); }
        public static void IsInstanceOf<T>(object a, string m, params object[] args)
        { if (!(a is T)) Fail0($"Expected instance of {typeof(T).Name}  But was: {(a == null ? "null" : a.GetType().Name)}{Msg(m, args)}"); }

        public static void IsNotInstanceOf<T>(object a) { IsNotInstanceOf<T>(a, null); }
        public static void IsNotInstanceOf<T>(object a, string m, params object[] args)
        { if (a is T) Fail0($"Expected NOT instance of {typeof(T).Name}{Msg(m, args)}"); }

        public static void Greater(double a, double b) { Greater(a, b, null); }
        public static void Greater(double a, double b, string m, params object[] args)
        { if (!(a > b)) Fail0($"Expected: greater than {b}  But was: {a}{Msg(m, args)}"); }

        public static void GreaterOrEqual(double a, double b) { GreaterOrEqual(a, b, null); }
        public static void GreaterOrEqual(double a, double b, string m, params object[] args)
        { if (!(a >= b)) Fail0($"Expected: >= {b}  But was: {a}{Msg(m, args)}"); }

        public static void Less(double a, double b) { Less(a, b, null); }
        public static void Less(double a, double b, string m, params object[] args)
        { if (!(a < b)) Fail0($"Expected: less than {b}  But was: {a}{Msg(m, args)}"); }

        public static void LessOrEqual(double a, double b) { LessOrEqual(a, b, null); }
        public static void LessOrEqual(double a, double b, string m, params object[] args)
        { if (!(a <= b)) Fail0($"Expected: <= {b}  But was: {a}{Msg(m, args)}"); }

        public static void IsEmpty(IEnumerable c) { IsEmpty(c, null); }
        public static void IsEmpty(IEnumerable c, string m, params object[] args)
        { if (c.Cast<object>().Any()) Fail0($"Expected empty{Msg(m, args)}"); }

        public static void IsNotEmpty(IEnumerable c) { IsNotEmpty(c, null); }
        public static void IsNotEmpty(IEnumerable c, string m, params object[] args)
        { if (!c.Cast<object>().Any()) Fail0($"Expected not empty{Msg(m, args)}"); }

        public static void Fail() { Fail0("Assert.Fail"); }
        public static void Fail(string m, params object[] args) { Fail0("Assert.Fail" + Msg(m, args)); }
        public static void Pass() { }
        public static void Pass(string m, params object[] args) { }

        public static void Throws<T>(TestDelegate code) where T : Exception { Throws<T>(code, null); }
        public static T Throws<T>(TestDelegate code, string m) where T : Exception
        {
            try { code(); }
            catch (T e) { return e; }
            catch (Exception e) { Fail0($"Expected {typeof(T).Name} but got {e.GetType().Name}{Msg(m, null)}"); }
            Fail0($"Expected {typeof(T).Name} but nothing was thrown{Msg(m, null)}");
            return null;
        }

        public static void DoesNotThrow(TestDelegate code)
        {
            try { code(); }
            catch (Exception e) { Fail0($"Expected no exception but got {e.GetType().Name}: {e.Message}"); }
        }
    }

    public static class CollectionAssert
    {
        private static List<object> L(IEnumerable c) { return c == null ? new List<object>() : c.Cast<object>().ToList(); }

        public static void AreEqual(IEnumerable e, IEnumerable a) { AreEqual(e, a, null); }
        public static void AreEqual(IEnumerable e, IEnumerable a, string m, params object[] args)
        { if (!L(e).SequenceEqual(L(a))) throw new AssertionException($"Collections differ: [{string.Join(", ", L(e))}] vs [{string.Join(", ", L(a))}] {m}"); }

        public static void AreNotEqual(IEnumerable e, IEnumerable a) { AreNotEqual(e, a, null); }
        public static void AreNotEqual(IEnumerable e, IEnumerable a, string m, params object[] args)
        { if (L(e).SequenceEqual(L(a))) throw new AssertionException($"Collections are equal but should not be {m}"); }

        public static void AreEquivalent(IEnumerable e, IEnumerable a) { AreEquivalent(e, a, null); }
        public static void AreEquivalent(IEnumerable e, IEnumerable a, string m, params object[] args)
        { if (!L(e).OrderBy(x => x?.ToString()).SequenceEqual(L(a).OrderBy(x => x?.ToString()))) throw new AssertionException($"Collections not equivalent {m}"); }

        public static void Contains(IEnumerable c, object item) { Contains(c, item, null); }
        public static void Contains(IEnumerable c, object item, string m, params object[] args)
        { if (!L(c).Contains(item)) throw new AssertionException($"Expected collection to contain {item} {m}"); }

        public static void DoesNotContain(IEnumerable c, object item) { DoesNotContain(c, item, null); }
        public static void DoesNotContain(IEnumerable c, object item, string m, params object[] args)
        { if (L(c).Contains(item)) throw new AssertionException($"Expected collection NOT to contain {item} {m}"); }

        public static void IsEmpty(IEnumerable c) { IsEmpty(c, null); }
        public static void IsEmpty(IEnumerable c, string m, params object[] args)
        { if (L(c).Any()) throw new AssertionException($"Expected empty {m}"); }

        public static void IsNotEmpty(IEnumerable c) { IsNotEmpty(c, null); }
        public static void IsNotEmpty(IEnumerable c, string m, params object[] args)
        { if (!L(c).Any()) throw new AssertionException($"Expected not empty {m}"); }

        public static void AllItemsAreUnique(IEnumerable c) { AllItemsAreUnique(c, null); }
        public static void AllItemsAreUnique(IEnumerable c, string m, params object[] args)
        { var l = L(c); if (l.Distinct().Count() != l.Count) throw new AssertionException($"Duplicate items {m}"); }

        public static void AllItemsAreNotNull(IEnumerable c)
        { if (L(c).Any(x => x == null)) throw new AssertionException("Null item"); }

        public static void IsSubsetOf(IEnumerable sub, IEnumerable sup) { IsSubsetOf(sub, sup, null); }
        public static void IsSubsetOf(IEnumerable sub, IEnumerable sup, string m, params object[] args)
        { var s = L(sup); if (L(sub).Any(x => !s.Contains(x))) throw new AssertionException($"Not a subset {m}"); }

        public static void IsNotSubsetOf(IEnumerable sub, IEnumerable sup) { IsNotSubsetOf(sub, sup, null); }
        public static void IsNotSubsetOf(IEnumerable sub, IEnumerable sup, string m, params object[] args)
        { var s = L(sup); if (!L(sub).Any(x => !s.Contains(x))) throw new AssertionException($"Is a subset but should not be {m}"); }

        public static void IsOrdered(IEnumerable c) { IsOrdered(c, null); }
        public static void IsOrdered(IEnumerable c, string m, params object[] args) { }
    }

    public static class StringAssert
    {
        public static void Contains(string e, string a) { Contains(e, a, null); }
        public static void Contains(string e, string a, string m, params object[] args)
        { if (a == null || !a.Contains(e)) throw new AssertionException($"Expected \"{a}\" to contain \"{e}\" {m}"); }

        public static void DoesNotContain(string e, string a) { DoesNotContain(e, a, null); }
        public static void DoesNotContain(string e, string a, string m, params object[] args)
        { if (a != null && a.Contains(e)) throw new AssertionException($"Expected \"{a}\" NOT to contain \"{e}\" {m}"); }

        public static void StartsWith(string e, string a)
        { if (a == null || !a.StartsWith(e)) throw new AssertionException($"Expected \"{a}\" to start with \"{e}\""); }

        public static void EndsWith(string e, string a)
        { if (a == null || !a.EndsWith(e)) throw new AssertionException($"Expected \"{a}\" to end with \"{e}\""); }
    }
}
