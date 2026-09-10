using System.Net.Http.Json;

namespace Dhole.Pricing.Api.Services;

public sealed record SellerDirectoryUser(
    Guid UserId,
    string? Email,
    string? DisplayName,
    string? UserName
);

public sealed class AuthSellerDirectoryService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration)
{
    public Task<IReadOnlyList<SellerDirectoryUser>> GetSellersAsync(
        CancellationToken cancellationToken)
    {
        return GetUsersAsync("/api/internal/auth/pricing-sellers", cancellationToken);
    }

    public Task<IReadOnlyList<SellerDirectoryUser>> GetSalesExecutivesAsync(
        CancellationToken cancellationToken)
    {
        return GetUsersAsync("/api/internal/auth/pricing-sales-executives", cancellationToken);
    }

    public async Task<SellerDirectoryUser?> GetSellerAsync(
        Guid sellerUserId,
        CancellationToken cancellationToken)
    {
        var sellers = await GetSellersAsync(cancellationToken);
        return sellers.FirstOrDefault(x => x.UserId == sellerUserId);
    }

    public async Task<SellerDirectoryUser?> GetSalesExecutiveAsync(
        Guid executiveUserId,
        CancellationToken cancellationToken)
    {
        var executives = await GetSalesExecutivesAsync(cancellationToken);
        return executives.FirstOrDefault(x => x.UserId == executiveUserId);
    }

    private async Task<IReadOnlyList<SellerDirectoryUser>> GetUsersAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var baseAddressText = configuration["Auth:Client:BaseAddress"] ?? "http://localhost:5201";
        if (!Uri.TryCreate(baseAddressText, UriKind.Absolute, out var baseAddress))
        {
            throw new InvalidOperationException(
                "Auth:Client:BaseAddress debe ser una URL absoluta válida."
            );
        }

        var headerName = configuration["Auth:Client:InternalServiceKeyHeader"]
            ?? "X-Dhole-Service-Key";
        var serviceKey = configuration["Auth:Client:InternalServiceKey"] ?? string.Empty;

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(baseAddress, path)
        );

        if (!string.IsNullOrWhiteSpace(serviceKey))
        {
            request.Headers.TryAddWithoutValidation(headerName, serviceKey);
        }

        var client = httpClientFactory.CreateClient();
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<SellerDirectoryUser[]>(
            cancellationToken: cancellationToken
        ) ?? [];
    }
}
