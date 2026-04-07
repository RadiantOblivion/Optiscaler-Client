using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;

namespace OptiscalerClient.Services
{
    [SupportedOSPlatform("linux")]
    public class LinuxGpuDetectionService : IGpuDetectionService
    {
        public GpuInfo[] DetectGPUs()
        {
            var gpus = new List<GpuInfo>();
            try
            {
                // -mm gives machine-readable output, avoiding slow network name lookups
                var vgaLines = RunProcessAndGetOutput("lspci", "-mm");
                if (vgaLines != null)
                {
                    // -mm format: each device is a block of "Field:\tValue" lines separated by blank lines
                    // We parse it as key-value pairs per device block
                    gpus.AddRange(ParseLspciMachineReadable(vgaLines));
                }

                // Fallback to plain lspci if -mm produced nothing
                if (gpus.Count == 0)
                {
                    var plainLines = RunProcessAndGetOutput("lspci");
                    if (plainLines != null)
                    {
                        foreach (var line in plainLines)
                        {
                            if (line.Contains("VGA compatible controller", StringComparison.OrdinalIgnoreCase) ||
                                line.Contains("3D controller", StringComparison.OrdinalIgnoreCase) ||
                                line.Contains("Display controller", StringComparison.OrdinalIgnoreCase))
                            {
                                // lspci format: "01:00.0 VGA compatible controller: Vendor Device"
                                var parts = line.Split(' ', 2);
                                var slot = parts[0]; 
                                var sepIdx = line.LastIndexOf(": ");
                                var rawName = sepIdx >= 0 ? line.Substring(sepIdx + 2).Trim() : line.Trim();
                                var vendor = DetectVendorFromName(rawName);
                                var cleanName = CleanGpuName(rawName);
                                gpus.Add(new GpuInfo
                                {
                                    Name = cleanName,
                                    PciSlot = slot,
                                    Vendor = vendor,
                                    VideoMemoryBytes = GetEstimatedMemory(vendor),
                                    DriverVersion = GetDriverVersion(vendor)
                                });
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors
            }

            // Refinement pass: try to get the exact product name (e.g. distinguish RX 9070 vs RX 9070 XT)
            // lspci groups all variants of a PCI ID together, so we need a better source.
            RefineGpuNames(gpus);

            return gpus.ToArray();
        }

        /// <summary>
        /// Tries to replace lspci-derived names with more accurate names from:
        /// 1. /sys/class/drm/card*/device/product_name  (no deps, works on AMD/Intel)
        /// 2. glxinfo -B OpenGL renderer string (works on AMD/Intel/NVIDIA with Mesa/proprietary)
        /// 3. nvidia-smi --query-gpu=name (NVIDIA proprietary)
        /// </summary>
        private void RefineGpuNames(List<GpuInfo> gpus)
        {
            if (gpus.Count == 0) return;

            // --- Strategy 1: sysfs product_name (Mapped by PCI Slot) ---
            var sysfsMap = ReadSysfsProductNamesBySlot();

            // --- Strategy 2: glxinfo renderer string ---
            var glxNames = ReadGlxinfoNames();

            // --- Strategy 3: nvidia-smi ---
            var nvidiaNames = ReadNvidiaSmiNames();

            // For each GPU, try to find a better name
            for (int i = 0; i < gpus.Count; i++)
            {
                var gpu = gpus[i];

                // 1. Try Slot Match via sysfs (Highest Trust)
                if (gpu.PciSlot != null)
                {
                    var normalizedSlot = NormalizePciSlot(gpu.PciSlot);
                    if (sysfsMap.TryGetValue(normalizedSlot, out var sysfsName))
                    {
                        var potentialBrand = DetectVendorFromName(sysfsName);
                        // Clean and brand the specific product name from the host driver
                        var betterName = CleanGpuName(sysfsName);
                        if (potentialBrand != GpuVendor.Unknown || betterName.Length > gpu.Name.Length)
                        {
                            gpu.Name = betterName;
                            if (potentialBrand != GpuVendor.Unknown) gpu.Vendor = potentialBrand;
                            continue;
                        }
                    }
                }

                // 2. Try glxinfo renderer string (High trust for active dGPU)
                if (i < glxNames.Count && !string.IsNullOrEmpty(glxNames[i]))
                {
                    var glxName = CleanGpuName(glxNames[i]);
                    // Only overwrite if it looks substantive
                    if (glxName.Length > 10)
                    {
                        gpu.Name = glxName;
                        var potentialBrand = DetectVendorFromName(glxNames[i]);
                        if (potentialBrand != GpuVendor.Unknown) gpu.Vendor = potentialBrand;
                        continue;
                    }
                }

                // 3. Try nvidia-smi for NVIDIA GPUs (Positional fallback)
                if (gpu.Vendor == GpuVendor.NVIDIA && i < nvidiaNames.Count && !string.IsNullOrEmpty(nvidiaNames[i]))
                {
                    gpu.Name = nvidiaNames[i];
                }
            }
        }

        private string NormalizePciSlot(string slot)
        {
            // lspci -mm might return "01:00.0" while sysfs uevent has "0000:01:00.0"
            if (slot.Count(c => c == ':') == 1)
                return $"0000:{slot}";
            return slot;
        }

        private Dictionary<string, string> ReadSysfsProductNamesBySlot()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!System.IO.Directory.Exists("/sys/class/drm")) return map;

                var cards = System.IO.Directory.GetDirectories("/sys/class/drm", "card*")
                    .Where(d => !System.IO.Path.GetFileName(d).Contains('-'))
                    .ToArray();

                foreach (var card in cards)
                {
                    // Read slot from uevent: PCI_SLOT_NAME=0000:01:00.0
                    var ueventFile = System.IO.Path.Combine(card, "device", "uevent");
                    var productFile = System.IO.Path.Combine(card, "device", "product_name");
                    
                    if (System.IO.File.Exists(ueventFile))
                    {
                        var uevent = System.IO.File.ReadAllText(ueventFile);
                        var slotMatch = System.Text.RegularExpressions.Regex.Match(uevent, @"PCI_SLOT_NAME=([0-9a-fA-F:\.]+)");
                        if (slotMatch.Success)
                        {
                            var slot = slotMatch.Groups[1].Value.Trim();
                            var name = System.IO.File.Exists(productFile) 
                                ? System.IO.File.ReadAllText(productFile).Trim() 
                                : string.Empty;

                            if (!string.IsNullOrEmpty(name))
                                map[slot] = name;
                        }
                    }
                }
            }
            catch { }
            return map;
        }

        private List<string> ReadGlxinfoNames()
        {
            var names = new List<string>();
            try
            {
                var lines = RunProcessAndGetOutput("glxinfo", "-B");
                if (lines == null) return names;

                foreach (var line in lines)
                {
                    // "OpenGL renderer string: AMD Radeon RX 9070 XT (radeonsi, navi48, ...)"
                    if (line.Contains("OpenGL renderer string", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("OpenGL renderer", StringComparison.OrdinalIgnoreCase))
                    {
                        var colonIdx = line.IndexOf(':');
                        if (colonIdx < 0) continue;
                        var renderer = line.Substring(colonIdx + 1).Trim();

                        // Preserve "AMD" or "NVIDIA" if glxinfo provides it
                        // Strip internal driver detail in parentheses but keep everything else
                        var parenIdx = renderer.LastIndexOf('(');
                        if (parenIdx > 0)
                            renderer = renderer.Substring(0, parenIdx).Trim();

                        if (!string.IsNullOrEmpty(renderer))
                            names.Add(renderer);
                    }
                }
            }
            catch { }
            return names;
        }

        private List<string> ReadNvidiaSmiNames()
        {
            var names = new List<string>();
            try
            {
                var lines = RunProcessAndGetOutput("nvidia-smi", "--query-gpu=name --format=csv,noheader");
                if (lines == null) return names;
                foreach (var line in lines)
                {
                    var name = line.Trim();
                    if (!string.IsNullOrEmpty(name))
                        names.Add(name);
                }
            }
            catch { }
            return names;
        }


        public GpuInfo? GetPrimaryGPU()
        {
            var gpus = DetectGPUs();
            return gpus.Length > 0 ? gpus[0] : null;
        }

        public GpuInfo? GetDiscreteGPU()
        {
            var gpus = DetectGPUs();
            var nvidia = gpus.FirstOrDefault(g => g.Vendor == GpuVendor.NVIDIA);
            if (nvidia != null) return nvidia;

            var amd = gpus.FirstOrDefault(g => g.Vendor == GpuVendor.AMD);
            if (amd != null) return amd;
            
            return gpus.FirstOrDefault();
        }

        public bool HasGPU(GpuVendor vendor)
        {
            return DetectGPUs().Any(g => g.Vendor == vendor);
        }

        public string GetGPUDescription()
        {
            var gpus = DetectGPUs();
            if (gpus.Length == 0) return "No GPU detected";
            if (gpus.Length == 1) return $"{GetVendorIcon(gpus[0].Vendor)} {gpus[0].Name}";

            var discrete = GetDiscreteGPU();
            if (discrete != null)
                return $"{GetVendorIcon(discrete.Vendor)} {discrete.Name} (+{gpus.Length - 1} more)";

            return $"{gpus.Length} GPUs detected";
        }

        private string[]? RunProcessAndGetOutput(string command, string arguments = "")
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return null;

                // Read output async to avoid deadlock, with a hard 3-second timeout
                var outputTask = process.StandardOutput.ReadToEndAsync();
                bool exited = process.WaitForExit(3000);
                if (!exited)
                {
                    try { process.Kill(); } catch { }
                    return null;
                }
                var output = outputTask.GetAwaiter().GetResult();
                return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            }
            catch
            {
                return null;
            }
        }

        private GpuVendor DetectVendorFromName(string gpuName)
        {
            var nameLower = gpuName.ToLowerInvariant();
            if (nameLower.Contains("nvidia")) return GpuVendor.NVIDIA;
            if (nameLower.Contains("amd") || nameLower.Contains("radeon") || nameLower.Contains("advanced micro")) return GpuVendor.AMD;
            if (nameLower.Contains("intel")) return GpuVendor.Intel;
            return GpuVendor.Unknown;
        }

        /// <summary>
        /// Produces a clean, human-friendly GPU name from a raw lspci string.
        /// e.g. "Advanced Micro Devices, Inc. [AMD/ATI] Navi 48 [Radeon RX 9070/3070 XT] (rev c0)"
        ///   → "Radeon RX 9070"
        /// e.g. "NVIDIA Corporation AD102 [GeForce RTX 4090] (rev a1)"
        ///   → "GeForce RTX 4090"
        /// </summary>
        private string CleanGpuName(string rawName)
        {
            // 1. Strip trailing "(rev XX)"
            var name = System.Text.RegularExpressions.Regex.Replace(rawName, @"\s*\(rev\s+[0-9a-f]+\)\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

            // 2. Remove verbose vendor prefixes (AMD full name, NVIDIA Corporation, Intel Corporation)
            var vendorPrefixes = new[]
            {
                @"Advanced Micro Devices,?\s*Inc\.?\s*(\[AMD/ATI\])?\s*",
                @"NVIDIA Corporation\s*",
                @"Intel Corporation\s*",
            };
            foreach (var pattern in vendorPrefixes)
            {
                name = System.Text.RegularExpressions.Regex.Replace(name, "^" + pattern, "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            }

            // 3. If there's a bracketed model name e.g. "Navi 48 [Radeon RX 9070/3070 XT/3070 GRE]",
            //    prefer the content inside the LAST brackets as the friendly name.
            var bracketMatch = System.Text.RegularExpressions.Regex.Match(name, @"\[([^\]]+)\]$");
            if (bracketMatch.Success)
            {
                var inside = bracketMatch.Groups[1].Value.Trim();
                
                // Better slash handling (e.g. "Radeon RX 9070/3070 XT/GRE")
                if (inside.Contains('/'))
                {
                    var parts = inside.Split('/');
                    var firstPart = parts[0].Trim();
                    
                    // Look for any model suffix (like "XT", "Ti", "GRE") in any part of the group
                    foreach (var part in parts.Skip(1))
                    {
                        var lastPart = part.Trim();
                        var suffixMatch = System.Text.RegularExpressions.Regex.Match(lastPart, @"\s+([A-Z0-9\s]+)$");
                        if (suffixMatch.Success)
                        {
                            var suffix = suffixMatch.Groups[1].Value.Trim();
                            if (!firstPart.Contains(suffix, StringComparison.OrdinalIgnoreCase))
                                firstPart = $"{firstPart} {suffix}";
                            break;
                        }
                    }
                    
                    return EnsureVendorPrefix(firstPart, rawName);
                }

                if (!string.IsNullOrEmpty(inside))
                    return EnsureVendorPrefix(inside, rawName);
            }

            // 4. Ensure we have branding even if lspci name is simple
            return EnsureVendorPrefix(name, rawName);
        }

        private string EnsureVendorPrefix(string cleanName, string rawName)
        {
            var vendor = DetectVendorFromName(rawName);
            // Fallback: if name starts with Radeon/GeForce, we know the vendor
            if (vendor == GpuVendor.Unknown)
            {
                if (cleanName.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) vendor = GpuVendor.AMD;
                else if (cleanName.Contains("GeForce", StringComparison.OrdinalIgnoreCase)) vendor = GpuVendor.NVIDIA;
                else if (cleanName.Contains("Arc", StringComparison.OrdinalIgnoreCase)) vendor = GpuVendor.Intel;
            }

            var prefix = vendor switch
            {
                GpuVendor.NVIDIA => "NVIDIA",
                GpuVendor.AMD => "AMD",
                GpuVendor.Intel => "Intel",
                _ => ""
            };

            if (!string.IsNullOrEmpty(prefix) && !cleanName.Contains(prefix, StringComparison.OrdinalIgnoreCase))
            {
                // Special case for AMD Radeon
                if (prefix == "AMD" && cleanName.StartsWith("Radeon", StringComparison.OrdinalIgnoreCase))
                    return $"AMD {cleanName}";
                
                return $"{prefix} {cleanName}";
            }

            return cleanName;
        }
        
        private string GetVendorIcon(GpuVendor vendor)
        {
            return vendor switch
            {
                GpuVendor.NVIDIA => "",
                GpuVendor.AMD => "",
                GpuVendor.Intel => "",
                _ => ""
            };
        }

        private ulong GetEstimatedMemory(GpuVendor vendor)
        {
            // Fallback sizes since lspci doesn't always show VRAM size cleanly
            return vendor switch
            {
                GpuVendor.NVIDIA => 8L * 1024 * 1024 * 1024,
                GpuVendor.AMD => 8L * 1024 * 1024 * 1024,
                _ => 2L * 1024 * 1024 * 1024
            };
        }

        /// <summary>
        /// Parses `lspci -mm` machine-readable output (no network lookups, fast).
        /// Each device block is a series of "Field:\tValue" lines separated by blank lines.
        /// </summary>
        private List<GpuInfo> ParseLspciMachineReadable(string[] lines)
        {
            var results = new List<GpuInfo>();
            var gpuClasses = new[] { "VGA compatible controller", "3D controller", "Display controller" };

            // lspci -mm produces one device per line in the format:
            // "Slot" "Class" "Vendor" "Device" "SVendor" "SDevice" "Rev"
            // (quoted fields separated by spaces)
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Extract quoted fields
                var fields = new List<string>();
                int i = 0;
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        int end = line.IndexOf('"', i + 1);
                        if (end < 0) break;
                        fields.Add(line.Substring(i + 1, end - i - 1));
                        i = end + 2; // skip closing quote + space
                    }
                    else i++;
                }

                // fields: [0]=Slot [1]=Class [2]=Vendor [3]=Device ...
                if (fields.Count < 4) continue;

                var slot = fields[0];
                var devClass = fields[1];
                if (!gpuClasses.Any(c => devClass.Contains(c, StringComparison.OrdinalIgnoreCase))) continue;

                var vendorName = fields[2];
                var deviceName = fields[3];
                var fullName = $"{vendorName} {deviceName}".Trim();
                var vendor = DetectVendorFromName(fullName);
                var cleanName = CleanGpuName(fullName);

                results.Add(new GpuInfo
                {
                    Name = cleanName,
                    PciSlot = slot,
                    Vendor = vendor,
                    VideoMemoryBytes = GetEstimatedMemory(vendor),
                    DriverVersion = GetDriverVersion(vendor)
                });
            }

            return results;
        }

        /// <summary>
        /// Reads real driver version info from the kernel DRM subsystem or nvidia-smi.
        /// </summary>
        private string GetDriverVersion(GpuVendor vendor)
        {
            try
            {
                if (vendor == GpuVendor.NVIDIA)
                {
                    // Try nvidia-smi for the exact driver version
                    var output = RunProcessAndGetOutput("nvidia-smi", "--query-gpu=driver_version --format=csv,noheader");
                    if (output != null && output.Length > 0)
                        return $"NVIDIA {output[0].Trim()}";
                }

                // For AMD/Intel/unknown: read from /sys/class/drm
                if (System.IO.Directory.Exists("/sys/class/drm"))
                {
                    var driverLinks = System.IO.Directory.GetDirectories("/sys/class/drm", "card*");
                    foreach (var card in driverLinks)
                    {
                        var driverPath = System.IO.Path.Combine(card, "device", "driver");
                        if (System.IO.Directory.Exists(driverPath))
                        {
                            var driverName = System.IO.Path.GetFileName(
                                System.IO.Directory.ResolveLinkTarget(driverPath, true)?.FullName ?? driverPath);
                            if (!string.IsNullOrEmpty(driverName))
                                return driverName;
                        }
                    }
                }
            }
            catch { }

            return "Mesa/Linux";
        }
    }
}
