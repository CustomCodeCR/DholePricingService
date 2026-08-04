using System.Security.Cryptography;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.Pricing.Persistence.Services;

public sealed class RateCodeGenerator(ServiceDbContext dbContext) : IRateCodeGenerator
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const int FirstBlockLength = 5;
    private const int SecondBlockLength = 6;
    private const int MaximumGenerationAttempts = 32;

    public async Task<string> GenerateAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < MaximumGenerationAttempts; attempt++)
        {
            var candidate = $"QUO-{GenerateBlock(FirstBlockLength)}-{GenerateBlock(SecondBlockLength)}";

            var alreadyExists = await dbContext
                .Set<RateHeader>()
                .AsNoTracking()
                .AnyAsync(rate => rate.RateCode == candidate, cancellationToken);

            if (!alreadyExists)
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "No se pudo generar un identificador QUO único después de varios intentos."
        );
    }

    private static string GenerateBlock(int length)
    {
        Span<char> block = stackalloc char[length];

        for (var index = 0; index < block.Length; index++)
        {
            block[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(block);
    }
}
