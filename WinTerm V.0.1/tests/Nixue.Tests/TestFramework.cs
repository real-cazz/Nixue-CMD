using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Nixue.Tests
{
    /// <summary>
    /// A deliberately tiny test harness. The environment has no test SDK (no dotnet,
    /// no VSTest), so tests are plain methods registered here, compiled against the
    /// built Core.dll and run as a console program that returns a non-zero exit code
    /// on any failure. This keeps "actual test execution" possible fully offline.
    /// </summary>
    public sealed class TestRunner
    {
        private readonly List<KeyValuePair<string, Action>> _tests = new List<KeyValuePair<string, Action>>();
        public int Passed;
        public int Failed;
        public List<string> Failures = new List<string>();

        public void Add(string name, Action test)
        {
            _tests.Add(new KeyValuePair<string, Action>(name, test));
        }

        public int Run()
        {
            Console.WriteLine("NIXUE CMD - Core test suite");
            Console.WriteLine("Running " + _tests.Count + " tests...");
            Console.WriteLine(new string('-', 60));
            Stopwatch sw = Stopwatch.StartNew();
            foreach (KeyValuePair<string, Action> t in _tests)
            {
                try
                {
                    t.Value();
                    Passed++;
                    Console.WriteLine("  PASS  " + t.Key);
                }
                catch (Exception ex)
                {
                    Failed++;
                    string msg = "  FAIL  " + t.Key + "  ->  " + ex.Message;
                    Console.WriteLine(msg);
                    Failures.Add(t.Key + ": " + ex.Message);
                }
            }
            sw.Stop();
            Console.WriteLine(new string('-', 60));
            Console.WriteLine("Total: " + _tests.Count + "   Passed: " + Passed + "   Failed: " + Failed +
                              "   (" + sw.ElapsedMilliseconds + " ms)");
            return Failed == 0 ? 0 : 1;
        }
    }

    public static class Assert
    {
        public static void True(bool condition, string message)
        {
            if (!condition) throw new Exception("Expected true: " + message);
        }

        public static void False(bool condition, string message)
        {
            if (condition) throw new Exception("Expected false: " + message);
        }

        public static void Equal(object expected, object actual, string message)
        {
            bool eq = (expected == null && actual == null) ||
                      (expected != null && expected.Equals(actual));
            if (!eq)
                throw new Exception(message + " (expected <" + Fmt(expected) + "> but was <" + Fmt(actual) + ">)");
        }

        public static void EqualInt(int expected, int actual, string message)
        {
            if (expected != actual)
                throw new Exception(message + " (expected " + expected + " but was " + actual + ")");
        }

        public static void EqualStr(string expected, string actual, string message)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
                throw new Exception(message + " (expected \"" + expected + "\" but was \"" + actual + "\")");
        }

        public static void NotNull(object o, string message)
        {
            if (o == null) throw new Exception("Expected non-null: " + message);
        }

        private static string Fmt(object o) { return o == null ? "null" : o.ToString(); }
    }
}
