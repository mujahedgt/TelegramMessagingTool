using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TelegramMessagingTool.Services;

public static class LocalDeviceInfoService
{
    public static string RenderSystemInfo()
    {
        var builder = new StringBuilder();
        builder.AppendLine("System information");
        builder.AppendLine();
        builder.AppendLine($"Operating system: {RuntimeInformation.OSDescription.Trim()}");
        builder.AppendLine($"OS architecture: {RuntimeInformation.OSArchitecture}");
        builder.AppendLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine($"Machine name: {Environment.MachineName}");
        builder.AppendLine($"Processor count: {Environment.ProcessorCount}");
        builder.AppendLine($".NET runtime: {RuntimeInformation.FrameworkDescription}");
        builder.AppendLine($"Current directory: {Environment.CurrentDirectory}");
        builder.AppendLine($"Uptime: {FormatDuration(TimeSpan.FromMilliseconds(Environment.TickCount64))}");
        builder.AppendLine($"Process memory: {FormatBytes(Environment.WorkingSet)}");
        return builder.ToString().Trim();
    }

    public static string RenderDiskStatus()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Disk status");
        builder.AppendLine();

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception ex)
        {
            return $"Disk status\n\nUnable to read drive information: {ex.Message}";
        }

        foreach (DriveInfo drive in drives.OrderBy(x => x.Name))
        {
            if (!drive.IsReady)
            {
                builder.AppendLine($"{drive.Name} not ready");
                continue;
            }

            double usedRatio = drive.TotalSize == 0
                ? 0
                : 1.0 - (drive.AvailableFreeSpace / (double)drive.TotalSize);
            builder.AppendLine($"{drive.Name} {drive.DriveType} {drive.DriveFormat}");
            builder.AppendLine($"  Free: {FormatBytes(drive.AvailableFreeSpace)} / {FormatBytes(drive.TotalSize)} ({usedRatio:P0} used)");
        }

        return builder.ToString().Trim();
    }

    public static string RenderTopProcesses(int limit = 10)
    {
        limit = Math.Clamp(limit, 1, 25);
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex)
        {
            return $"Running processes\n\nUnable to read process information: {ex.Message}";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Running processes: {processes.Length}");
        builder.AppendLine($"Top {limit} by memory");
        builder.AppendLine();

        foreach (Process process in processes
            .OrderByDescending(SafeWorkingSet)
            .ThenBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Take(limit))
        {
            long memory = SafeWorkingSet(process);
            builder.AppendLine($"PID {SafeId(process),6}  {FormatBytes(memory),9}  {SafeName(process)}");
        }

        return builder.ToString().Trim();
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{value:0} {units[unit]}"
            : $"{value:0.#} {units[unit]}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
        {
            return $"{(int)duration.TotalDays}d {duration.Hours}h {duration.Minutes}m";
        }

        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        return $"{duration.Minutes}m {duration.Seconds}s";
    }

    private static long SafeWorkingSet(Process process)
    {
        try
        {
            return process.WorkingSet64;
        }
        catch
        {
            return 0;
        }
    }

    private static int SafeId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch
        {
            return 0;
        }
    }

    private static string SafeName(Process process)
    {
        try
        {
            return string.IsNullOrWhiteSpace(process.ProcessName) ? "unknown" : process.ProcessName;
        }
        catch
        {
            return "unknown";
        }
    }
}
