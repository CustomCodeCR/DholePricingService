namespace Dhole.Pricing.Application.Abstractions.Services;

public interface IRateCodeGenerator
{
    Task<string> GenerateAsync(CancellationToken cancellationToken = default);
}
