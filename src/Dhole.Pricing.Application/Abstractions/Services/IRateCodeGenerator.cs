namespace Dhole.Pricing.Application.Abstractions.Services;

public interface IRateCodeGenerator
{
    Task<long> GetNextAsync(CancellationToken cancellationToken = default);
}
