using System;

namespace OptiscalerClient.Services
{
    public static class GpuDetectionServiceFactory
    {
        private static IGpuDetectionService? _cached;
        private static GpuInfo? _cachedGpu;
        private static bool _gpuDetected = false;

        public static IGpuDetectionService Create()
        {
            if (_cached != null) return _cached;

            if (OperatingSystem.IsWindows())
            {
                _cached = new WindowsGpuDetectionService();
            }
            else if (OperatingSystem.IsLinux())
            {
                _cached = new LinuxGpuDetectionService();
            }
            else
            {
                _cached = null!;
            }

            return _cached;
        }

        /// <summary>
        /// Returns a cached GpuInfo — detection only runs once per app session.
        /// Safe to call from the UI thread after the first background detection completes.
        /// </summary>
        public static GpuInfo? GetCachedGpu()
        {
            if (_gpuDetected) return _cachedGpu;
            _gpuDetected = true;
            var svc = Create();
            if (svc == null) return null;
            try { _cachedGpu = svc.GetDiscreteGPU() ?? svc.GetPrimaryGPU(); }
            catch { _cachedGpu = null; }
            return _cachedGpu;
        }

        /// <summary>Pre-warms the GPU cache on a background thread so UI calls are instant.</summary>
        public static void WarmUpAsync()
        {
            if (_gpuDetected) return;
            System.Threading.Tasks.Task.Run(() => GetCachedGpu());
        }
    }
}
