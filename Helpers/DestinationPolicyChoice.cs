using Palace.Models;

namespace Palace.Helpers;

/// <summary>
/// Settings "New-file destination" picker. The ComboBox shows friendly labels by index;
/// the source folder still stores the <see cref="DestinationPolicy"/> name.
/// </summary>
public static class DestinationPolicyChoice
{
    /// <summary>Picker index for a stored policy name. Unknown or empty means InSource (0).</summary>
    public static int IndexOf(string? policy) =>
        string.Equals(policy?.Trim(), nameof(DestinationPolicy.Destination), StringComparison.OrdinalIgnoreCase) ? 1 : 0;

    /// <summary>Stored policy name for a picker index; null for -1 (cleared ComboBox) or out of range.</summary>
    public static string? NameAt(int index) => index switch
    {
        0 => nameof(DestinationPolicy.InSource),
        1 => nameof(DestinationPolicy.Destination),
        _ => null
    };
}