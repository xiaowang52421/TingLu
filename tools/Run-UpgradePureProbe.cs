using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

// Isolates cached pure NUnit methods for hang diagnosis; this is not a Unity test run.
internal static class UpgradePureProbe
{
    private static string[] searchPaths;

    private static int Main(string[] args)
    {
        if (args.Length != 5) return 2;
        searchPaths = new[] { Path.GetFullPath(args[0]), Path.GetFullPath(args[1]) };
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        try
        {
            Assembly assembly = Assembly.LoadFrom(Path.Combine(searchPaths[0], args[2]));
            Type type = assembly.GetType(args[3], true);
            object fixture = Activator.CreateInstance(type);
            MethodInfo setup = type.GetMethod("SetUp", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (setup != null) setup.Invoke(fixture, null);
            Console.WriteLine("START pure diagnostic assembly: " + args[3] + "." + args[4]);
            var watch = Stopwatch.StartNew();
            type.GetMethod(args[4], BindingFlags.Instance | BindingFlags.Public).Invoke(fixture, null);
            Console.WriteLine("PASS pure diagnostic probe; elapsed_ms=" + watch.ElapsedMilliseconds);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error is TargetInvocationException ? error.InnerException.ToString() : error.ToString());
            return 1;
        }
    }

    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name + ".dll";
        foreach (string folder in searchPaths)
        {
            string path = Path.Combine(folder, name);
            if (File.Exists(path)) return Assembly.LoadFrom(path);
        }
        return null;
    }
}
