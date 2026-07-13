using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.Pricing.Application.Features.Imports.RejectImportRate;

public sealed record RejectImportRateCommand(Guid Id, string Reason, Guid? RejectedBy)
    : ICommand<Result>;
