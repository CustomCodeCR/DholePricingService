using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.Pricing.Application.Features.Imports.DeleteImportRate;

public sealed record DeleteImportRateCommand(IReadOnlyCollection<Guid> Ids, Guid? DeletedBy)
    : ICommand<Result>;
