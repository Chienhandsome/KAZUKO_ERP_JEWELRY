using Kazuko.Core.Authentication;

namespace Kazuko.Core.PriceBoard;

public sealed record PriceBoardHostStatus(PriceBoardTvSettings Settings, IReadOnlyList<GoldPriceRecord> Prices,
    string LocalUrl, IReadOnlyList<string> LanUrls, bool DisplayOpen, bool Paused, int ProcessId, string Error = "");

public interface IPriceBoardTvHostClient
{
    Task<PriceBoardHostStatus> StatusAsync(CancellationToken cancellationToken = default);
    Task<PriceBoardHostStatus> ConfigureAsync(PriceBoardTvSettings settings, CancellationToken cancellationToken = default);
    Task<PriceBoardHostStatus> OpenDisplayAsync(string monitorDevice, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface IPriceBoardTvHostClientFactory
{
    Task<IPriceBoardTvHostClient> ConnectAsync(AuthenticatedSession session, long companyId, CancellationToken cancellationToken = default);
}

public interface IPriceBoardTvPriceSource
{
    Task<IReadOnlyList<GoldPriceRecord>> LoadAsync(long companyId, CancellationToken cancellationToken = default);
}
