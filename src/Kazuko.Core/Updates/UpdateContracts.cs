namespace Kazuko.Core.Updates;

public sealed record ModulePackageManifest(
    string Id,
    string Name,
    string Version,
    string EntryAssembly,
    string Sha256,
    int MinimumHostVersion = 1);

public sealed record ModuleInstallResult(bool IsSuccess, string Message, string? StagingDirectory = null);

public interface IModulePackageInstaller
{
    Task<ModuleInstallResult> ValidateAndStageAsync(string packagePath, CancellationToken cancellationToken = default);
}
