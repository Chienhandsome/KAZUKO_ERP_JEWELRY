using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Kazuko.Core.Updates;

namespace Kazuko.Infrastructure.Updates;

public sealed class ModulePackageInstaller : IModulePackageInstaller
{
    private readonly string _stagingRoot;

    public ModulePackageInstaller(string stagingRoot) => _stagingRoot = stagingRoot;

    public async Task<ModuleInstallResult> ValidateAndStageAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath))
            return new ModuleInstallResult(false, "Không tìm thấy gói cập nhật.");

        if (!string.Equals(Path.GetExtension(packagePath), ".kazuko-module", StringComparison.OrdinalIgnoreCase))
            return new ModuleInstallResult(false, "Gói cập nhật phải có đuôi .kazuko-module.");

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);
            var manifestEntry = archive.GetEntry("module.json");
            if (manifestEntry is null)
                return new ModuleInstallResult(false, "Gói cập nhật thiếu module.json.");

            await using var manifestStream = manifestEntry.Open();
            var manifest = await JsonSerializer.DeserializeAsync<ModulePackageManifest>(
                manifestStream,
                cancellationToken: cancellationToken);

            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id) || string.IsNullOrWhiteSpace(manifest.EntryAssembly))
                return new ModuleInstallResult(false, "Manifest không hợp lệ.");

            var assemblyEntry = archive.GetEntry(manifest.EntryAssembly);
            if (assemblyEntry is null)
                return new ModuleInstallResult(false, "Không tìm thấy entry assembly trong gói.");

            await using var assemblyStream = assemblyEntry.Open();
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(assemblyStream, cancellationToken));
            if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                return new ModuleInstallResult(false, "Checksum của module không khớp.");

            var safeId = string.Concat(manifest.Id.Where(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_'));
            if (string.IsNullOrWhiteSpace(safeId))
                return new ModuleInstallResult(false, "Module id không hợp lệ.");

            var stagingDirectory = Path.Combine(_stagingRoot, $"{safeId}-{manifest.Version}.kazuko-module.staging");
            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, true);
            Directory.CreateDirectory(stagingDirectory);
            archive.ExtractToDirectory(stagingDirectory);

            return new ModuleInstallResult(true, "Gói hợp lệ và đã được đưa vào vùng chờ.", stagingDirectory);
        }
        catch (InvalidDataException)
        {
            return new ModuleInstallResult(false, "Tệp không phải gói cập nhật hợp lệ.");
        }
        catch (JsonException)
        {
            return new ModuleInstallResult(false, "module.json không đúng định dạng.");
        }
    }
}
