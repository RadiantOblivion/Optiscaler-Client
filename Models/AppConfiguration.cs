using System.Collections.Generic;

namespace OptiscalerClient.Models
{
    /// <summary>
    /// Configuration for GitHub repositories
    /// </summary>
    public class RepositoryConfig
    {
        public string RepoOwner { get; set; } = string.Empty;
        public string RepoName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Configuration for scan sources
    /// </summary>
    public class ScanSourcesConfig
    {
        public bool ScanSteam { get; set; } = true;
        public bool ScanEpic { get; set; } = true;
        public bool ScanGOG { get; set; } = true;
        public bool ScanXbox { get; set; } = true;
        public bool ScanEA { get; set; } = true;
        public bool ScanUbisoft { get; set; } = true;
        public List<string> CustomFolders { get; set; } = new();
    }

    /// <summary>
    /// Root configuration containing all repository configurations
    /// </summary>
    public class AppConfiguration
    {
        public RepositoryConfig App { get; set; } = new() { RepoOwner = "Agustinm28", RepoName = "Optiscaler-Switcher" };
        public RepositoryConfig OptiScaler { get; set; } = new();
        public RepositoryConfig Fakenvapi { get; set; } = new();
        public RepositoryConfig NukemFG { get; set; } = new();
        public string Language { get; set; } = "en";
        public bool Debug { get; set; } = false;
        public bool AutoScan { get; set; } = true;
        public bool AnimationsEnabled { get; set; } = true;
        public ScanSourcesConfig ScanSources { get; set; } = new();
    }

    /// <summary>
    /// Version information for all components
    /// </summary>
    public class ComponentVersions
    {
        public string? OptiScalerVersion { get; set; }
        public string? FakenvapiVersion { get; set; }
        public string? NukemFGVersion { get; set; }
    }
}
