using System.Runtime.CompilerServices;
using Palace.Helpers;

namespace Palace.Tests;

/// <summary>
/// Runs before any test: points CatalogStore at a throwaway folder so tests can never
/// read or write the real %LOCALAPPDATA%\Palace (pointer file or default catalog).
/// </summary>
internal static class TestDataDir
{
    public static string Path { get; private set; } = "";

    [ModuleInitializer]
    internal static void Init()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "palace-tests-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
        CatalogStore.DataDirOverride = Path;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best-effort */ }
        };
    }
}
