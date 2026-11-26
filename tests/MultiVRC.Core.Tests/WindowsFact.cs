namespace MultiVRC.Core.Tests;

/// <summary>A test of something only Windows has (the registry, junctions, locked files): skipped elsewhere.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Windows only.";
    }
}
