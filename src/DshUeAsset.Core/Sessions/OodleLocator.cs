using DshUeAsset.Core.Config;

namespace DshUeAsset.Core.Sessions;

/// <summary>
/// Resolve the Oodle runtime without forcing the user to configure it. Search order:
/// explicit config → next to ueasset.exe → OODLE_DLL → game's Binaries/Win64 → FModel's data dir.
/// Oodle is proprietary, so it is never embedded in this repository; a local copy is used.
/// </summary>
public static class OodleLocator
{
    private static readonly string[] Names = ["oo2core_9_win64.dll", "oo2core_8_win64.dll", "oo2core_7_win64.dll"];

    public static string? Locate(GameProfile profile, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        var baseDirectory = AppContext.BaseDirectory;
        var nextToExe = FindIn(baseDirectory);
        if (nextToExe is not null) return nextToExe;

        var fromEnvironment = Environment.GetEnvironmentVariable("OODLE_DLL");
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment)) return fromEnvironment;

        // Walk up from the paks directory: <game>/Binaries/Win64 and the engine third-party folder.
        try
        {
            var directory = new DirectoryInfo(profile.Directory);
            for (var depth = 0; depth < 5 && directory is not null; depth++)
            {
                foreach (var relative in new[] { "Binaries/Win64", "Binaries/Win32", "Engine/Binaries/ThirdParty/Oodle" })
                {
                    var candidate = FindIn(Path.Combine(directory.FullName, relative));
                    if (candidate is not null) return candidate;
                }
                var direct = FindIn(directory.FullName);
                if (direct is not null) return direct;
                directory = directory.Parent;
            }
        }
        catch
        {
            // profile path may be inaccessible; fall through
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var fmodel = Path.Combine(local, "FModel");
        return Directory.Exists(fmodel) ? FindIn(fmodel) : null;
    }

    private static string? FindIn(string directory)
    {
        if (!Directory.Exists(directory)) return null;
        foreach (var name in Names)
        {
            var path = Path.Combine(directory, name);
            if (File.Exists(path)) return path;
        }
        try
        {
            return Directory.EnumerateFiles(directory, "oo2core*_win64.dll", SearchOption.TopDirectoryOnly).FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }
}
