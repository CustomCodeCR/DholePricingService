using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.Pricing.Application.Features.Rates.RejectRateMargin;

public sealed record RejectRateMarginCommand(Guid Id, string? Reason, Guid? RejectedBy)
    : ICommand<Result>;
