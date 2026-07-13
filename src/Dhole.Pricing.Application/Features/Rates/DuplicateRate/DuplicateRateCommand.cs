using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.Pricing.Application.Features.Rates.DuplicateRate;

public sealed record DuplicateRateCommand(
    Guid Id,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    Guid? CreatedBy
) : ICommand<Result<Guid>>;
