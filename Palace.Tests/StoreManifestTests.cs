using System.Xml.Linq;
using Palace.Helpers;
using Xunit;

namespace Palace.Tests;

/// <summary>Microsoft Store readiness checks on Package.appxmanifest.</summary>
public class StoreManifestTests
{
    private static readonly XNamespace F = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";

    private static XDocument Manifest()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Package.appxmanifest")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return XDocument.Load(Path.Combine(dir!.FullName, "Package.appxmanifest"));
    }

    [Fact]
    public void ManifestIdentityIsARelease()
    {
        // When the reserved Partner Center identity lands in the manifest, it must also be
        // in CatalogStore.ReleasePackageNames, or the Store build would isolate itself
        // away from the durable catalog like a side-by-side test package.
        var name = Manifest().Root!.Element(F + "Identity")!.Attribute("Name")!.Value;
        Assert.Contains(name, CatalogStore.ReleasePackageNames, StringComparer.OrdinalIgnoreCase);
        Assert.Null(CatalogStore.IsolatedDataDirFor(name, Path.GetTempPath()));
    }

    [Fact]
    public void DesktopOnlyNoPhoneIdentityNoUnusedCapabilities()
    {
        var doc = Manifest();
        var root = doc.Root!;
        Assert.DoesNotContain(root.Elements(), e => e.Name.LocalName == "PhoneIdentity");
        var families = root.Element(F + "Dependencies")!.Elements(F + "TargetDeviceFamily")
            .Select(e => e.Attribute("Name")!.Value).ToList();
        Assert.Equal(["Windows.Desktop"], families);
        var caps = root.Element(F + "Capabilities")!.Elements().Select(e => e.Attribute("Name")!.Value).ToList();
        Assert.DoesNotContain("systemAIModels", caps);
        Assert.Contains("broadFileSystemAccess", caps);
        Assert.Contains("runFullTrust", caps);
    }

    [Fact]
    public void VersionFourthPartIsZero()
    {
        var v = Version.Parse(Manifest().Root!.Element(F + "Identity")!.Attribute("Version")!.Value);
        Assert.Equal(0, v.Revision);
    }
}
