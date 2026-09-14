namespace Dhole.Pricing.Application.Abstractions.Repositories;

public interface IRateCommentStore
{
    Task<string?> GetAsync(Guid rateId, CancellationToken cancellationToken = default);

    Task SetAsync(
        Guid rateId,
        string? comments,
        Guid? updatedBy,
        CancellationToken cancellationToken = default
    );
}
