using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.Pricing.Application.Features.Imports.InactivateImportRate;

public sealed record InactivateImportRateCommand(
    Guid ImportRateId,
    Guid? InactivatedBy
) : ICommand<Result>;
