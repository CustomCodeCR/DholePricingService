using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.Pricing.Application.Features.Rates.DeleteRate;

public sealed record DeleteRateCommand(IReadOnlyCollection<Guid> Ids, Guid? DeletedBy)
    : ICommand<Result>;
