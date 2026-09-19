using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.Pricing.Application.Features.Rates.DuplicateRate;

public sealed record DuplicateRateCommand(
    Guid Id,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    Guid? CreatedBy,
    bool ApplyTariff = false,
    string? ClientName = null,
    string? ExecutiveName = null,
    string? IdtraNumber = null
) : ICommand<Result<Guid>>;
