using System.Security.Cryptography;
using Kazuko.Core.Updates;

namespace Kazuko.Infrastructure.Updates;

public sealed class RsaPssSha256PackageSignatureVerifier : IModulePackageSignatureVerifier
{
    public bool IsAlgorithmAllowed(string algorithm) =>
        string.Equals(
            algorithm?.Trim(),
            ModulePackageFormat.AllowedSignatureAlgorithm,
            StringComparison.Ordinal);

    public bool Verify(
        ReadOnlySpan<byte> manifestBytes,
        ReadOnlySpan<byte> signatureBytes,
        ReadOnlySpan<byte> subjectPublicKeyInfo,
        string algorithm)
    {
        if (!IsAlgorithmAllowed(algorithm))
            return false;
        if (manifestBytes.IsEmpty || signatureBytes.IsEmpty || subjectPublicKeyInfo.IsEmpty)
            return false;

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
            return rsa.VerifyData(
                manifestBytes,
                signatureBytes,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
