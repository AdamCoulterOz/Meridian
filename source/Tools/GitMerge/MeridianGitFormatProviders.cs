using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using Meridian.Core.Formats;
using MeridianGit.Abstractions;
using MeridianGit.Formats.Binary;
using MeridianGit.Formats.Images;
using MeridianGit.Formats.Markup;
using MeridianGit.Formats.PowerPlatform;
using MeridianGit.Formats.Web;

namespace Meridian.Tools.GitMerge;

internal static class MeridianGitFormatProviders
{
    private static readonly MeridianGitProviderRegistry Registry = CreateBundledRegistry();
    private static readonly TrustedProviderPackage[] TrustedPackages =
    [
        new("MeridianGit.Formats.Markup", "0.1.0", "MeridianGit.Formats.Markup", "MeridianGit.Formats.Markup.MarkupMeridianGitProvider", [".xml", ".json", ".json5", ".yaml", ".yml"]),
        new("MeridianGit.Formats.Web", "0.1.0", "MeridianGit.Formats.Web", "MeridianGit.Formats.Web.WebMeridianGitProvider", [".html", ".htm", ".css", ".js"]),
        new("MeridianGit.Formats.Images", "0.1.0", "MeridianGit.Formats.Images", "MeridianGit.Formats.Images.ImagesMeridianGitProvider", [".png", ".jpg", ".jpeg", ".gif", ".ico"]),
        new("MeridianGit.Formats.PowerPlatform", "0.1.0", "MeridianGit.Formats.PowerPlatform", "MeridianGit.Formats.PowerPlatform.PowerPlatformMeridianGitProvider", [".xap", ".liquid"]),
        new("MeridianGit.Formats.Binary", "0.1.0", "MeridianGit.Formats.Binary", "MeridianGit.Formats.Binary.BinaryMeridianGitProvider", [".bin"])
    ];

    public static async Task<IFormatAdapter?> CreateAdapterAsync(string repoPath, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(repoPath).ToLowerInvariant();
        if (Registry.TryCreateAdapter(extension, out var adapter))
            return adapter;

        var providerPackage = TrustedPackages.FirstOrDefault(package => package.Supports(extension));
        if (providerPackage is null || ProviderDownloadsDisabled())
            return null;

        var provider = await TrustedProviderLoader.LoadAsync(providerPackage, cancellationToken);
        Registry.AddProvider(provider);

        return Registry.TryCreateAdapter(extension, out adapter)
            ? adapter
            : null;
    }

    private static bool ProviderDownloadsDisabled() =>
        string.Equals(
            Environment.GetEnvironmentVariable("MERIDIANGIT_DISABLE_PROVIDER_DOWNLOAD"),
            "1",
            StringComparison.Ordinal);

    private static MeridianGitProviderRegistry CreateBundledRegistry()
    {
        var registry = new MeridianGitProviderRegistry();
        registry.AddProvider(new MarkupMeridianGitProvider());
        registry.AddProvider(new WebMeridianGitProvider());
        registry.AddProvider(new ImagesMeridianGitProvider());
        registry.AddProvider(new PowerPlatformMeridianGitProvider());
        registry.AddProvider(new BinaryMeridianGitProvider());
        return registry;
    }

    private sealed class MeridianGitProviderRegistry
    {
        private readonly Dictionary<string, Func<IFormatAdapter>> _factories = new(StringComparer.OrdinalIgnoreCase);

        public void AddProvider(IMeridianGitProvider provider)
        {
            foreach (var registration in provider.GetFormatRegistrations())
                _factories[registration.Extension] = registration.CreateAdapter;
        }

        public bool TryCreateAdapter(string extension, out IFormatAdapter adapter)
        {
            if (_factories.TryGetValue(extension, out var factory))
            {
                adapter = factory();
                return true;
            }

            adapter = null!;
            return false;
        }
    }

    private sealed record TrustedProviderPackage(
        string PackageId,
        string Version,
        string AssemblyName,
        string ProviderTypeName,
        string[] Extensions)
    {
        public bool Supports(string extension) => Extensions.Contains(extension);
    }

    private static class TrustedProviderLoader
    {
        private const string NuGetSource = "https://api.nuget.org/v3/index.json";

        public static async Task<IMeridianGitProvider> LoadAsync(
            TrustedProviderPackage package,
            CancellationToken cancellationToken)
        {
            var loadedAssembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, package.AssemblyName, StringComparison.Ordinal));

            if (loadedAssembly is null)
            {
                loadedAssembly = await RestoreAndLoadAsync(package, cancellationToken);
            }

            var providerType = loadedAssembly.GetType(package.ProviderTypeName, throwOnError: true)!;
            if (Activator.CreateInstance(providerType) is not IMeridianGitProvider provider)
                throw new InvalidOperationException($"Trusted provider type '{package.ProviderTypeName}' does not implement '{nameof(IMeridianGitProvider)}'.");

            return provider;
        }

        private static async Task<Assembly> RestoreAndLoadAsync(
            TrustedProviderPackage package,
            CancellationToken cancellationToken)
        {
            var cacheRoot = GetProviderCacheRoot();
            var packagesRoot = Path.Combine(cacheRoot, "packages");
            var restoreProject = WriteRestoreProject(cacheRoot, package);

            Console.Error.WriteLine($"MeridianGit trusted provider restore: {package.PackageId} {package.Version}.");
            await RunDotNetRestoreAsync(restoreProject, packagesRoot, cancellationToken);

            ProviderDependencyResolver.AddSearchRoot(packagesRoot);
            var assemblyPath = Path.Combine(
                packagesRoot,
                package.PackageId.ToLowerInvariant(),
                package.Version,
                "lib",
                "net11.0",
                package.AssemblyName + ".dll");

            if (!File.Exists(assemblyPath))
                throw new FileNotFoundException($"Trusted provider assembly '{package.AssemblyName}' was not found after restore.", assemblyPath);

            return AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        }

        private static string WriteRestoreProject(string cacheRoot, TrustedProviderPackage package)
        {
            var projectDirectory = Path.Combine(
                cacheRoot,
                "restore",
                package.PackageId.ToLowerInvariant(),
                package.Version);

            Directory.CreateDirectory(projectDirectory);
            var projectPath = Path.Combine(projectDirectory, "provider-restore.csproj");
            File.WriteAllText(
                projectPath,
                $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                    <RestoreSources>{{string.Join(";", GetRestoreSources())}}</RestoreSources>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="{{package.PackageId}}" Version="{{package.Version}}" />
                  </ItemGroup>
                </Project>
                """);

            return projectPath;
        }

        private static async Task RunDotNetRestoreAsync(
            string projectPath,
            string packagesRoot,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(packagesRoot);
            using var process = Process.Start(new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                ArgumentList =
                {
                    "restore",
                    projectPath,
                    "--packages",
                    packagesRoot,
                    "--nologo"
                }
            }) ?? throw new InvalidOperationException("Failed to start dotnet restore for trusted provider package.");

            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "Failed to restore trusted MeridianGit provider package." +
                    Environment.NewLine +
                    await standardOutput +
                    await standardError);
            }
        }

        private static string[] GetRestoreSources()
        {
            var configuredSources = Environment.GetEnvironmentVariable("MERIDIANGIT_PROVIDER_SOURCES");
            return string.IsNullOrWhiteSpace(configuredSources)
                ? [NuGetSource]
                : configuredSources.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static string GetProviderCacheRoot()
        {
            var configured = Environment.GetEnvironmentVariable("MERIDIANGIT_PROVIDER_CACHE");
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".cache", "meridiangit", "providers");
        }
    }

    private static class ProviderDependencyResolver
    {
        private static readonly object SyncRoot = new();
        private static readonly HashSet<string> SearchDirectories = new(StringComparer.Ordinal);
        private static bool _registered;

        public static void AddSearchRoot(string packageRoot)
        {
            lock (SyncRoot)
            {
                if (!_registered)
                {
                    AssemblyLoadContext.Default.Resolving += ResolveFromProviderPackages;
                    _registered = true;
                }

                foreach (var directory in Directory.EnumerateDirectories(packageRoot, "net11.0", SearchOption.AllDirectories))
                    SearchDirectories.Add(directory);
            }
        }

        private static Assembly? ResolveFromProviderPackages(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            lock (SyncRoot)
            {
                foreach (var directory in SearchDirectories)
                {
                    var candidate = Path.Combine(directory, assemblyName.Name + ".dll");
                    if (File.Exists(candidate))
                        return context.LoadFromAssemblyPath(candidate);
                }
            }

            return null;
        }
    }
}
