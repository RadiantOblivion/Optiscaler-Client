namespace OptiscalerClient.Services
{
    public enum GpuVendor
    {
        Unknown,
        NVIDIA,
        AMD,
        Intel
    }

    public class GpuInfo
    {
        public string Name { get; set; } = string.Empty;
        public GpuVendor Vendor { get; set; } = GpuVendor.Unknown;
        public string DriverVersion { get; set; } = string.Empty;
        public ulong VideoMemoryBytes { get; set; }

        public string VideoMemoryGB => $"{VideoMemoryBytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
    }

    public interface IGpuDetectionService
    {
        GpuInfo[] DetectGPUs();
        GpuInfo? GetPrimaryGPU();
        GpuInfo? GetDiscreteGPU();
        bool HasGPU(GpuVendor vendor);
        string GetGPUDescription();
    }
}
