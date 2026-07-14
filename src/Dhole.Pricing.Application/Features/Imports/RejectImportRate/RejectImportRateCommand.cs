using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.Pricing.Application.Features.Imports.RejectImportRate;

public sealed record RejectImportRateCommand(
    IReadOnlyCollection<Guid> Ids,
    string Reason,
    Guid? RejectedBy
) : ICommand<Result>;
