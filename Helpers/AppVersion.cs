using System.Reflection;
using Windows.ApplicationModel;

namespace Palace.Helpers;

public static class AppVersion
{
    /// <summary>Current 0.x slice name. Keep in lockstep with the Version table in AGENTS.md.</summary>
    public const string Milestone = "Library core · initial public preview";

    /// <summary>Honest status for Settings → About. Rooms and Organization are not finished.</summary>
    public const string InfancyNote =
        "Rooms and Organization features are in their infancy — preview only, not finished.";

    public static string Configuration =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    public static string Numeric
    {
        get
        {
            try
            {
                var v = Package.Current.Id.Version;
                return $"{v.Major}.{v.Minor}.{v.Build}";
            }
            catch (InvalidOperationException)
            {
                return Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion
                    ?.Split('+')[0]
                    ?? "0.0.0";
            }
        }
    }

    public static string Display => $"Palace {Numeric} ({Configuration}) · {Milestone}";

    public static string TitleBar => $"{Numeric} {Configuration} · preview";
}
