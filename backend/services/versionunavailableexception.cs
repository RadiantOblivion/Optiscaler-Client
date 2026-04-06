using System;

namespace OptiscalerClient.Services;

/// <summary>
/// Thrown when an OptiScaler version cannot be installed because it is not 
/// available for download (e.g. GitHub is rate-limited or unreachable, and 
/// no cached download URL exists for that version).
/// </summary>
public class VersionUnavailableException : Exception
{
    public string Version { get; }

    public VersionUnavailableException(string version, string reason)
        : base($"Cannot install OptiScaler v{version}: {reason}")
    {
        Version = version;
    }
}
