using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.Pricing.Application.Features.Costs.Delete;

public sealed record DeleteCostCommand(Guid Id, Guid? DeletedBy) : ICommand<Result>;
