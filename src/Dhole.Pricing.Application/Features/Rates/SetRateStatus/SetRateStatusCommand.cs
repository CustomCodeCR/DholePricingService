using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.Pricing.Domain.Rates.Enums;

namespace Dhole.Pricing.Application.Features.Rates.SetRateStatus;

public sealed record SetRateStatusCommand(Guid Id, RateStatus Status, Guid? UpdatedBy)
    : ICommand<Result>;
