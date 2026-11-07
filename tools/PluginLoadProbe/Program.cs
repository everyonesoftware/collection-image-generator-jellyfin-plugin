using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using SkiaSharp;

// A standalone host: no reference to either plugin project. Its loader mirrors
// Jellyfin v12.4's PluginLoadContext, to catch missing packaged dependencies.
if (args.Length != 1) throw new ArgumentException("Pass the packaged plugin ZIP path.");
GC.KeepAlive(typeof(ILibraryManager));
GC.KeepAlive(typeof(IScheduledTask));
GC.KeepAlive(typeof(SKBitmap));
var directory = Path.Combine(Path.GetTempPath(), "collection-image-generator-load-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    ZipFile.ExtractToDirectory(Path.GetFullPath(args[0]), directory);
    var pluginPath = Path.Combine(directory, "CollectionImageGenerator.Plugin.dll");
    var context = new HostPluginLoadContext(pluginPath);
    var plugin = context.LoadFromAssemblyPath(pluginPath);
    var types = plugin.GetTypes();
    if (!types.Any(t => typeof(IScheduledTask).IsAssignableFrom(t) && !t.IsAbstract))
        throw new InvalidOperationException("No discoverable scheduled task in package.");
    if (!types.Any(t => typeof(ILibraryPostScanTask).IsAssignableFrom(t) && !t.IsAbstract))
        throw new InvalidOperationException("No discoverable library post-scan task in package.");
    var core = context.LoadFromAssemblyName(new AssemblyName("CollectionImageGenerator.Core"));
    if (AssemblyLoadContext.GetLoadContext(core) != context)
        throw new InvalidOperationException("Core was accidentally resolved from the probe host instead of package.");
    if (core.GetType("CollectionImageGenerator.Core.CollageRenderer") is null)
        throw new InvalidOperationException("Packaged renderer type not found.");
    Console.WriteLine($"PASS: packaged plugin loads {types.Length} types, task exports, and isolated core using Jellyfin 12.4's resolver.");
    context.Unload();
}
catch (ReflectionTypeLoadException ex)
{
    foreach (var error in ex.LoaderExceptions) Console.Error.WriteLine(error);
    throw;
}
finally
{
    // Windows may retain loaded assembly handles until the collectible context is GC'd.
    GC.Collect();
    GC.WaitForPendingFinalizers();
    try { Directory.Delete(directory, true); }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
        Console.WriteLine("Temporary assembly cleanup deferred until the process releases its Windows file handles.");
    }
}

sealed class HostPluginLoadContext(string path) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver resolver = new(path);
    protected override Assembly? Load(AssemblyName name)
    {
        var path = resolver.ResolveAssemblyToPath(name);
        return path is null ? null : LoadFromAssemblyPath(path);
    }
}
