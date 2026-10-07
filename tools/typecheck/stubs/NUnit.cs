// Minimal NUnit + Unity TestRunner stubs — API shape only.
using System;
using System.Collections;
using System.Collections.Generic;

namespace NUnit.Framework
{
    [AttributeUsage(AttributeTargets.Method)] public sealed class TestAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class SetUpAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class TearDownAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class OneTimeSetUpAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class OneTimeTearDownAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class TestFixtureAttribute : Attribute { public TestFixtureAttribute() { } public TestFixtureAttribute(params object[] args) { } }
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)] public sealed class TestCaseAttribute : Attribute { public TestCaseAttribute(params object[] args) { } public string TestName { get; set; } public object ExpectedResult { get; set; } }
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)] public sealed class IgnoreAttribute : Attribute { public IgnoreAttribute(string reason) { } }
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)] public sealed class CategoryAttribute : Attribute { public CategoryAttribute(string name) { } }
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)] public sealed class DescriptionAttribute : Attribute { public DescriptionAttribute(string description) { } }
    [AttributeUsage(AttributeTargets.Method)] public sealed class TimeoutAttribute : Attribute { public TimeoutAttribute(int ms) { } }
    [AttributeUsage(AttributeTargets.Method)] public sealed class RepeatAttribute : Attribute { public RepeatAttribute(int count) { } }
    [AttributeUsage(AttributeTargets.Parameter)] public sealed class ValuesAttribute : Attribute { public ValuesAttribute(params object[] args) { } }

    public class Constraint { }
    public static class Is
    {
        public static Constraint EqualTo(object expected) { return null; }
        public static Constraint Not { get { return null; } }
        public static Constraint True { get { return null; } }
        public static Constraint False { get { return null; } }
        public static Constraint Null { get { return null; } }
        public static Constraint GreaterThan(object v) { return null; }
        public static Constraint LessThan(object v) { return null; }
    }

    public static class Assert
    {
        public static void AreEqual(object expected, object actual) { }
        public static void AreEqual(object expected, object actual, string message, params object[] args) { }
        public static void AreEqual(double expected, double actual, double delta) { }
        public static void AreEqual(double expected, double actual, double delta, string message, params object[] args) { }
        public static void AreNotEqual(object expected, object actual) { }
        public static void AreNotEqual(object expected, object actual, string message, params object[] args) { }
        public static void AreSame(object expected, object actual) { }
        public static void AreSame(object expected, object actual, string message, params object[] args) { }
        public static void AreNotSame(object expected, object actual) { }
        public static void AreNotSame(object expected, object actual, string message, params object[] args) { }
        public static void IsTrue(bool condition) { }
        public static void IsTrue(bool condition, string message, params object[] args) { }
        public static void IsFalse(bool condition) { }
        public static void IsFalse(bool condition, string message, params object[] args) { }
        public static void IsNull(object o) { }
        public static void IsNull(object o, string message, params object[] args) { }
        public static void IsNotNull(object o) { }
        public static void IsNotNull(object o, string message, params object[] args) { }
        public static void IsEmpty(IEnumerable collection) { }
        public static void IsEmpty(IEnumerable collection, string message, params object[] args) { }
        public static void IsNotEmpty(IEnumerable collection) { }
        public static void IsNotEmpty(IEnumerable collection, string message, params object[] args) { }
        public static void IsInstanceOf<T>(object actual) { }
        public static void IsInstanceOf<T>(object actual, string message, params object[] args) { }
        public static void IsNotInstanceOf<T>(object actual) { }
        public static void IsNotInstanceOf<T>(object actual, string message, params object[] args) { }
        public static void Greater(int a, int b) { }
        public static void Greater(int a, int b, string message, params object[] args) { }
        public static void Greater(double a, double b) { }
        public static void Greater(double a, double b, string message, params object[] args) { }
        public static void GreaterOrEqual(int a, int b) { }
        public static void GreaterOrEqual(int a, int b, string message, params object[] args) { }
        public static void GreaterOrEqual(double a, double b) { }
        public static void GreaterOrEqual(double a, double b, string message, params object[] args) { }
        public static void Less(int a, int b) { }
        public static void Less(int a, int b, string message, params object[] args) { }
        public static void Less(double a, double b) { }
        public static void Less(double a, double b, string message, params object[] args) { }
        public static void LessOrEqual(int a, int b) { }
        public static void LessOrEqual(int a, int b, string message, params object[] args) { }
        public static void LessOrEqual(double a, double b) { }
        public static void LessOrEqual(double a, double b, string message, params object[] args) { }
        public static void Fail() { }
        public static void Fail(string message, params object[] args) { }
        public static void Pass() { }
        public static void Pass(string message, params object[] args) { }
        public static void Inconclusive(string message) { }
        public static void That(bool condition) { }
        public static void That(bool condition, string message) { }
        public static void That(object actual, Constraint constraint) { }
        public static void That(object actual, Constraint constraint, string message) { }
        public static void Throws<T>(TestDelegate code) where T : Exception { }
        public static void DoesNotThrow(TestDelegate code) { }
        public static T Throws<T>(TestDelegate code, string message) where T : Exception { return null; }
    }

    public delegate void TestDelegate();

    public static class CollectionAssert
    {
        public static void AreEqual(IEnumerable expected, IEnumerable actual) { }
        public static void AreEqual(IEnumerable expected, IEnumerable actual, string message, params object[] args) { }
        public static void AreEquivalent(IEnumerable expected, IEnumerable actual) { }
        public static void AreEquivalent(IEnumerable expected, IEnumerable actual, string message, params object[] args) { }
        public static void Contains(IEnumerable collection, object item) { }
        public static void Contains(IEnumerable collection, object item, string message, params object[] args) { }
        public static void DoesNotContain(IEnumerable collection, object item) { }
        public static void DoesNotContain(IEnumerable collection, object item, string message, params object[] args) { }
        public static void IsEmpty(IEnumerable collection) { }
        public static void IsEmpty(IEnumerable collection, string message, params object[] args) { }
        public static void IsNotEmpty(IEnumerable collection) { }
        public static void IsNotEmpty(IEnumerable collection, string message, params object[] args) { }
        public static void IsOrdered(IEnumerable collection) { }
        public static void IsOrdered(IEnumerable collection, string message, params object[] args) { }
        public static void AllItemsAreNotNull(IEnumerable collection) { }
        public static void AllItemsAreUnique(IEnumerable collection) { }
        public static void AllItemsAreUnique(IEnumerable collection, string message, params object[] args) { }
        public static void AreNotEqual(IEnumerable expected, IEnumerable actual) { }
        public static void AreNotEqual(IEnumerable expected, IEnumerable actual, string message, params object[] args) { }
        public static void AreNotEquivalent(IEnumerable expected, IEnumerable actual) { }
        public static void AreNotEquivalent(IEnumerable expected, IEnumerable actual, string message, params object[] args) { }
        public static void IsSubsetOf(IEnumerable subset, IEnumerable superset) { }
        public static void IsSubsetOf(IEnumerable subset, IEnumerable superset, string message, params object[] args) { }
        public static void IsNotSubsetOf(IEnumerable subset, IEnumerable superset) { }
        public static void IsNotSubsetOf(IEnumerable subset, IEnumerable superset, string message, params object[] args) { }
        public static void AllItemsAreInstancesOfType(IEnumerable collection, Type expectedType) { }
    }

    public static class StringAssert
    {
        public static void Contains(string expected, string actual) { }
        public static void Contains(string expected, string actual, string message, params object[] args) { }
        public static void StartsWith(string expected, string actual) { }
        public static void EndsWith(string expected, string actual) { }
        public static void AreEqualIgnoringCase(string expected, string actual) { }
        public static void DoesNotContain(string expected, string actual) { }
        public static void DoesNotContain(string expected, string actual, string message, params object[] args) { }
        public static void DoesNotStartWith(string expected, string actual) { }
        public static void DoesNotEndWith(string expected, string actual) { }
        public static void IsMatch(string pattern, string actual) { }
    }
}

namespace UnityEngine.TestTools
{
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class UnityTestAttribute : System.Attribute { }

    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class UnitySetUpAttribute : System.Attribute { }

    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class UnityTearDownAttribute : System.Attribute { }

    public static class LogAssert
    {
        public static bool ignoreFailingMessages { get; set; }
        public static void Expect(UnityEngine.LogType type, string message) { }
        public static void NoUnexpectedReceived() { }
    }
}

namespace UnityEngine
{
    public enum LogType { Error, Assert, Warning, Log, Exception }
}
