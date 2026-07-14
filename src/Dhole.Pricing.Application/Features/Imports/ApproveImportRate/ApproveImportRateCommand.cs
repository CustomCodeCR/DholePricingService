using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;

namespace Dhole.Pricing.Application.Features.Imports.ApproveImportRate;

public sealed record ApproveImportRateCommand(IReadOnlyCollection<Guid> Ids, Guid? ApprovedBy)
    : ICommand<Result>;
