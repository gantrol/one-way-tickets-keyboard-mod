using System.IO.Compression;
using System.Security.Cryptography;
using Installer = OneWayTickets.KeyboardMod.Installer.Program;
using Loader = OneWayTickets.KeyboardMod.Installer.LoaderInstallation;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows7.0")]

namespace InstallerRegression;

internal static class Program
{
    private static void Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("Installation leaves original game hashes unchanged", RoundTrip),
            ("Reinstallation retains complete loader ownership", RepeatedInstall),
            ("A Steam update keeps the independent mod installed", GameUpdate),
            ("Existing compatible loaders and settings are reused", SharedLoader),
            ("Other plugins keep their shared loader", () => SharedDependency("BepInEx/plugins/OtherMod/Other.dll")),
            ("Other patchers keep their shared loader", () => SharedDependency("BepInEx/patchers/Other.dll")),
            ("Changed loader configuration is preserved", ChangedLoader),
            ("Unknown native proxies are never overwritten", () => Conflict("winhttp.dll", "foreign proxy")),
            ("Unknown loader configuration is never overwritten", () => Conflict("doorstop_config.ini", "[General]\nenabled=true\ntarget_assembly=Other.dll")),
            ("A known partial loader is completed without claiming old files", PartialLoader),
            ("Old mod files and backups are completely untouched", LegacyFilesUntouched),
            ("Tampered ownership paths cannot trigger external deletion", TamperedOwnership),
            ("Failed plugin replacement rolls back installation writes", LockedPlugin),
            ("Unrelated core extensions prevent destructive loader cleanup", () => SharedDependency("BepInEx/core/OtherExtension.dll")),
            ("Linked installation directories cannot redirect writes or deletion", LinkedInstallDirectory),
            ("Linked third-party plugins retain their shared loader without traversal", LinkedOtherPlugin),
        };
        foreach (var test in tests) { test.Run(); Console.WriteLine("PASS " + test.Name); }
        Console.WriteLine($"{tests.Length} independent-loader regression tests passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static void RoundTrip()
    {
        using var game = new Game();
        var original = game.AllHashes();
        Installer.PerformInstall(game.Root);
        Check(Installer.IsModInstalled(game.Root), "The loader and plugin must be detected.");
        game.AssertHashes(original);
        Check(File.Exists(game.Path("BepInEx/core/UnityDoorstop.Source.zip")), "Redistribute loader source.");
        Check(File.Exists(game.Path("BepInEx/core/OneWayTickets.ThirdPartyNotices.md")), "Redistribute notices.");
        Check(!File.Exists(game.World + ".bak"), "Never create a game backup.");
        Installer.PerformUninstall(game.Root);
        game.AssertAllHashes(original);
        Check(!File.Exists(game.Path("winhttp.dll")), "Remove exclusively owned unchanged loader files.");
        Check(!Installer.HasModFiles(game.Root), "Remove this mod's files and ownership record.");
    }

    private static void RepeatedInstall()
    {
        using var game = new Game();
        var original = game.AllHashes();
        Installer.PerformInstall(game.Root);
        string ownership = File.ReadAllText(game.Path(Loader.OwnershipPath));
        Installer.PerformInstall(game.Root);
        game.AssertHashes(original);
        Check(File.ReadAllText(game.Path(Loader.OwnershipPath)) == ownership, "Retain complete ownership.");
        Installer.PerformUninstall(game.Root);
        game.AssertAllHashes(original);
    }

    private static void GameUpdate()
    {
        using var game = new Game();
        Installer.PerformInstall(game.Root);
        File.WriteAllBytes(game.World, RandomNumberGenerator.GetBytes(5000));
        string updated = Hash(game.World);
        Check(Installer.IsModInstalled(game.Root), "Replacing World.dll must not remove the independent entry.");
        Installer.PerformInstall(game.Root);
        Check(Hash(game.World) == updated, "Reinstall preserves the new game.");
        Installer.PerformUninstall(game.Root);
        Check(Hash(game.World) == updated, "Uninstall preserves the new game.");
    }

    private static void SharedLoader()
    {
        using var game = new Game();
        game.ExtractExistingLoader();
        File.AppendAllText(game.Path("doorstop_config.ini"), "\n# existing setup\n");
        var existing = game.AllHashes();
        Installer.PerformInstall(game.Root);
        game.AssertHashes(existing);
        Installer.PerformUninstall(game.Root);
        game.AssertAllHashes(existing);
    }

    private static void SharedDependency(string relative)
    {
        using var game = new Game();
        Installer.PerformInstall(game.Root);
        game.Write(relative, "another component");
        string proxy = Hash(game.Path("winhttp.dll"));
        Installer.PerformUninstall(game.Root);
        Check(Hash(game.Path("winhttp.dll")) == proxy, "Keep the loader while others need it.");
        Check(File.ReadAllText(game.Path(relative)) == "another component", "Preserve the other component.");
        Check(!File.Exists(game.Path(Loader.PluginPath)), "Remove our plugin.");
    }

    private static void ChangedLoader()
    {
        using var game = new Game();
        Installer.PerformInstall(game.Root);
        File.AppendAllText(game.Path("doorstop_config.ini"), "\n# customization\n");
        string customized = Hash(game.Path("doorstop_config.ini"));
        Installer.PerformUninstall(game.Root);
        Check(Hash(game.Path("doorstop_config.ini")) == customized, "Preserve changed loader files.");
        Check(File.Exists(game.Path("winhttp.dll")), "Do not partly remove a changed loader.");
    }

    private static void Conflict(string relative, string contents)
    {
        using var game = new Game();
        game.Write(relative, contents);
        var before = game.AllHashes();
        Reject(() => Installer.PerformInstall(game.Root));
        game.AssertAllHashes(before);
    }

    private static void PartialLoader()
    {
        using var game = new Game();
        using var archive = new ZipArchive(new MemoryStream(Installer.ReadEmbeddedResource("BepInEx.Loader.zip")));
        using (var output = File.Create(game.Path("winhttp.dll")))
        using (var input = archive.GetEntry("winhttp.dll").Open()) input.CopyTo(output);
        string existing = Hash(game.Path("winhttp.dll"));
        Installer.PerformInstall(game.Root);
        Check(Installer.IsModInstalled(game.Root), "Complete the known partial loader.");
        Installer.PerformUninstall(game.Root);
        Check(Hash(game.Path("winhttp.dll")) == existing, "Do not claim pre-existing files.");
        Check(File.Exists(game.Path("BepInEx/core/BepInEx.dll")), "Keep the shared loader together.");
    }

    private static void LegacyFilesUntouched()
    {
        using var game = new Game();
        game.Write("One Way Tickets_Data/Managed/World.dll.bak", "stale backup");
        game.Write("One Way Tickets_Data/Managed/OneWayTickets.KeyboardMod.dll", "old mod");
        var before = game.AllHashes();
        Installer.PerformInstall(game.Root);
        game.AssertHashes(before);
        Installer.PerformUninstall(game.Root);
        game.AssertAllHashes(before);
    }

    private static void TamperedOwnership()
    {
        using var game = new Game();
        Installer.PerformInstall(game.Root);
        File.AppendAllText(game.Path(Loader.OwnershipPath), new string('A', 64) + "\t../outside.txt\n");
        var before = game.AllHashes();
        Reject(() => Installer.PerformUninstall(game.Root));
        game.AssertAllHashes(before);
    }

    private static void LockedPlugin()
    {
        using var game = new Game();
        Installer.PerformInstall(game.Root);
        var before = game.AllHashes();
        using (var locked = new FileStream(game.Path(Loader.PluginPath), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bool failed = false;
            try { Installer.PerformInstall(game.Root); } catch (IOException) { failed = true; }
            Check(failed, "A locked plugin must reject replacement.");
        }
        game.AssertAllHashes(before);
        Check(Directory.GetFiles(game.Root, "*.tmp", SearchOption.AllDirectories).Length == 0, "Clean up temporary files.");
    }

    private static void Reject(Action action)
    {
        bool rejected = false;
        try { action(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Conflicting setups must fail before writes.");
    }

    private static void LinkedInstallDirectory()
    {
        using var game = new Game();
        using var external = new Game();
        var before = external.AllHashes();
        string junction = game.Path("BepInEx");
        CreateJunction(junction, external.Root);
        try
        {
            Reject(() => Installer.PerformInstall(game.Root));
            Reject(() => Installer.PerformUninstall(game.Root));
            external.AssertAllHashes(before);
        }
        finally { Directory.Delete(junction); }
    }

    private static void LinkedOtherPlugin()
    {
        using var game = new Game();
        using var external = new Game();
        var before = external.AllHashes();
        Installer.PerformInstall(game.Root);
        string junction = game.Path("BepInEx/plugins/OtherLink");
        CreateJunction(junction, external.Root);
        try
        {
            Installer.PerformUninstall(game.Root);
            Check(File.Exists(game.Path("winhttp.dll")), "A linked third-party mod still needs the shared loader.");
            external.AssertAllHashes(before);
        }
        finally { Directory.Delete(junction); }
    }

    private static void CreateJunction(string path, string target)
    {
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in new[] { "/c", "mklink", "/J", path, target }) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start);
        process.WaitForExit();
        Check(process.ExitCode == 0, "Cannot create test junction: " + process.StandardError.ReadToEnd());
    }

    private sealed class Game : IDisposable
    {
        internal string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OneWayTickets.LoaderRegression", Guid.NewGuid().ToString("N"));
        internal string World => Path("One Way Tickets_Data/Managed/World.dll");
        internal string Path(string relative) => System.IO.Path.Combine(Root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

        internal Game()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(World));
            // Arbitrary bytes prove the installer never inspects or disassembles game assemblies.
            File.WriteAllBytes(World, RandomNumberGenerator.GetBytes(4096));
            File.WriteAllBytes(Path("One Way Tickets.exe"), RandomNumberGenerator.GetBytes(1024));
            File.WriteAllBytes(Path("UnityPlayer.dll"), RandomNumberGenerator.GetBytes(2048));
            File.WriteAllText(Path("OneWayTickets.KeyboardMod.ini"), "custom config");
        }
        internal void Write(string relative, string contents)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path(relative)));
            File.WriteAllText(Path(relative), contents);
        }
        internal Dictionary<string, string> AllHashes() => Directory.GetFiles(Root, "*", SearchOption.AllDirectories).ToDictionary(file => file, Hash);
        internal void AssertHashes(Dictionary<string, string> expected)
        {
            foreach (var item in expected) Check(File.Exists(item.Key) && Hash(item.Key) == item.Value, "File changed: " + item.Key);
        }
        internal void AssertAllHashes(Dictionary<string, string> expected)
        {
            AssertHashes(expected);
            Check(AllHashes().Count == expected.Count, "Unexpected files left behind.");
        }
        internal void ExtractExistingLoader()
        {
            using var archive = new ZipArchive(new MemoryStream(Installer.ReadEmbeddedResource("BepInEx.Loader.zip")));
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName == "changelog.txt") continue;
                string destination = Path(entry.FullName);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination));
                using var input = entry.Open();
                using var output = File.Create(destination);
                input.CopyTo(output);
            }
        }
        public void Dispose()
        {
            string expectedParent = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OneWayTickets.LoaderRegression")) + System.IO.Path.DirectorySeparatorChar;
            if (!System.IO.Path.GetFullPath(Root).StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid cleanup path.");
            Directory.Delete(Root, true);
        }
    }
}
