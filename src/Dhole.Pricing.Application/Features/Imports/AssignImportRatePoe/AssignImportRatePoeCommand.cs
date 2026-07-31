using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.Pricing.Application.Features.Imports.AssignImportRatePoe;

public sealed record AssignImportRatePoeCommand(
    Guid ImportRateId,
    Guid PoeId,
    Guid? UpdatedBy
) : ICommand<Result>;
