from pathlib import Path

path = Path('src/Dhole.Pricing.Application/Features/Rates/CreateRate/CreateRateCommandHandler.cs')
text = path.read_text()
old = '''            if (importedRate.Status == ImportStatus.Pending)
            {
                if (!command.CanApproveImportedRate)
                {
                    return Result.Failure<Guid>(PricingErrors.ImportFclRateInvalidStatus);
                }

                importedRate.Approve(command.CreatedBy);
                automaticallyApprovedImport = true;
            }

            if (importedRate.Status != ImportStatus.Approved)
'''
new = '''            // PreAuthorized is a machine-approved state created specifically so an active
            // imported rate can be selected by Pricing without requiring the manual
            // approve-import scope. Promote it to Approved when it is actually used.
            if (importedRate.Status == ImportStatus.PreAuthorized)
            {
                importedRate.Approve(command.CreatedBy);
                automaticallyApprovedImport = true;
            }
            else if (importedRate.Status == ImportStatus.Pending)
            {
                if (!command.CanApproveImportedRate)
                {
                    return Result.Failure<Guid>(PricingErrors.ImportFclRateInvalidStatus);
                }

                importedRate.Approve(command.CreatedBy);
                automaticallyApprovedImport = true;
            }

            if (importedRate.Status != ImportStatus.Approved)
'''
count = text.count(old)
if count != 1:
    raise SystemExit(f'Expected one import-status block, found {count}')
path.write_text(text.replace(old, new))
