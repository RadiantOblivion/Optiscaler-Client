using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using OptiscalerClient.Views;

namespace OptiscalerClient.Helpers
{
    public static class ProcessHelper
    {
        public static void OpenFolder(string folderPath)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start("explorer.exe", $"\"{folderPath}\"");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", $"\"{folderPath}\"");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", $"\"{folderPath}\"");
                }
                else
                {
                    DebugWindow.Log($"[ProcessHelper] Unsupported platform for OpenFolder: {RuntimeInformation.OSDescription}");
                }
            }
            catch (Exception ex)
            {
                DebugWindow.Log($"[ProcessHelper] Error opening folder '{folderPath}': {ex.Message}");
            }
        }

        public static void OpenUrl(string url)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
                else
                {
                    DebugWindow.Log($"[ProcessHelper] Unsupported platform for OpenUrl: {RuntimeInformation.OSDescription}");
                }
            }
            catch (Exception ex)
            {
                DebugWindow.Log($"[ProcessHelper] Error opening URL '{url}': {ex.Message}");
            }
        }
    }
}
