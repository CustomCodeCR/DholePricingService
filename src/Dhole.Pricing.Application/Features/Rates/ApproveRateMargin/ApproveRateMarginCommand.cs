using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.Pricing.Application.Features.Rates.ApproveRateMargin;

public sealed record ApproveRateMarginCommand(Guid Id, Guid? ApprovedBy) : ICommand<Result>;
