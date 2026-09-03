from pathlib import Path
import re


def read(path: str) -> str:
    return Path(path).read_text(encoding='utf-8')


def write(path: str, text: str) -> None:
    Path(path).write_text(text, encoding='utf-8')


def replace_one(text: str, before: str, after: str, label: str) -> str:
    count = text.count(before)
    if count != 1:
        raise RuntimeError(f'{label}: expected one occurrence, found {count}')
    return text.replace(before, after, 1)

# 1) Own-LCL create authorization and country-specific historical sales.
path = 'src/Dhole.Pricing.Api/Endpoints/OwnLclConsolidationEndpoints.cs'
text = read(path)
text = replace_one(
    text,
    'group.MapPost("/", CreateAsync).RequireScope(PricingConstants.Scopes.RateCreate);',
    'group.MapPost("/", CreateAsync).RequireScope(PricingConstants.Scopes.OwnLclConsolidationCreate);',
    'own-lcl create scope',
)
text = replace_one(
    text,
    'var historicalDestination = destination == "PA" ? "PA" : "CR";',
    'var historicalDestination = destination;',
    'country-specific historical destination',
)
write(path, text)

# 2) Automatic own-LCL creation uses the dedicated scope and advertises editable costs.
path = 'src/Dhole.Pricing.Api/Endpoints/OwnLclDestinationAutomationEndpoints.cs'
text = read(path)
text = replace_one(
    text,
    'group.MapPost("/consolidations", CreateAsync)\n            .RequireScope(PricingConstants.Scopes.RateCreate);',
    'group.MapPost("/consolidations", CreateAsync)\n            .RequireScope(PricingConstants.Scopes.OwnLclConsolidationCreate);',
    'automatic own-lcl create scope',
)
# There are two profile constructors, matrix-backed and empty-matrix fallback.
text = text.replace(
    '            false,\n            "Pricing: Matriz de costos',
    '            true,\n            "Pricing: Matriz de costos',
)
write(path, text)

# 3) Imported rates are automatically pre-authorized. Manual approve remains the
#    human pre-approval step and continues to emit the existing approval audit event.
path = 'src/Dhole.Pricing.Domain/Imports/Enums/ImportStatus.cs'
text = read(path)
text = replace_one(
    text,
    '    Expired = 4,\n',
    '    Expired = 4,\n    PreAuthorized = 5,\n',
    'pre-authorized enum',
)
write(path, text)

path = 'src/Dhole.Pricing.Domain/Imports/Entities/ImportFclRate.cs'
text = read(path)
text = replace_one(
    text,
    '        Status = ImportStatus.Pending;',
    '        // Imported rows enter a machine pre-authorized state. An authorized\n        // reviewer can still perform the manual pre-approval through Approve().\n        Status = ImportStatus.PreAuthorized;',
    'automatic pre-authorization',
)
text = text.replace(
    'if (Status != ImportStatus.Pending)',
    'if (Status is not (ImportStatus.Pending or ImportStatus.PreAuthorized))',
)
write(path, text)

path = 'src/Dhole.Pricing.Application/Features/Imports/ApproveImportRate/ApproveImportRateCommandHandler.cs'
text = read(path)
text = replace_one(
    text,
    'if (importRate.Status is not (ImportStatus.Pending or ImportStatus.Approved))',
    'if (importRate.Status is not (ImportStatus.Pending or ImportStatus.PreAuthorized or ImportStatus.Approved))',
    'approve accepted statuses',
)
text = replace_one(
    text,
    '.Where(importRate => importRate.Status == ImportStatus.Pending)',
    '.Where(importRate => importRate.Status is ImportStatus.Pending or ImportStatus.PreAuthorized)',
    'manual preapproval candidates',
)
write(path, text)

# 4) Keep automatic fixed details if the UI accidentally reports them as removed.
#    They remain editable in-place (cost/sale amounts), but their CostId identity stays locked.
path = 'src/Dhole.Pricing.Application/Features/Rates/UpdateRate/UpdateRateCommandHandler.cs'
text = read(path)
anchor = '''        var removedIds = (command.RemovedExtraDetailIds ?? Array.Empty<Guid>())
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToHashSet();
'''
replacement = anchor + '''
        // Automatic fixed details belong to the rate snapshot. The wizard is allowed to
        // edit their amounts, but an omitted/re-keyed UI row must never turn into a hard
        // delete error for the whole LCL rate. Preserve those rows instead.
        removedIds.RemoveWhere(id =>
            existingDetails.TryGetValue(id, out var existing) && IsAutomaticFixed(existing));
'''
text = replace_one(text, anchor, replacement, 'preserve automatic fixed details')
write(path, text)

# 5) Replace freight-only variation notifications with complete-cost comparisons
#    against commercial rates already Sent and AcceptedByClient (accepted >= 7 days).
path = 'src/Dhole.Pricing.Persistence/Services/ImportedRateChangeNotificationService.cs'
text = read(path)
if 'using Dhole.Pricing.Domain.Rates.Enums;' not in text:
    text = text.replace(
        'using Dhole.Pricing.Domain.Imports.Enums;\n',
        'using Dhole.Pricing.Domain.Imports.Enums;\nusing Dhole.Pricing.Domain.Rates.Enums;\n',
        1,
    )
text = text.replace(
    'if (currentRate.SourceType != ImportSourceType.Email || currentRate.Status != ImportStatus.Pending)',
    'if (currentRate.SourceType != ImportSourceType.Email || currentRate.Status is not (ImportStatus.Pending or ImportStatus.PreAuthorized))',
    1,
)
text = text.replace(
    'var subject = "Nuevas tarifas de correo pendientes de aprobación";',
    'var subject = "Nuevas tarifas preautorizadas pendientes de preaprobación";',
    1,
)
text = text.replace(
    '$"Hay nuevas tarifas recibidas por correo pendientes de revisión. "',
    '$"Hay nuevas tarifas recibidas por correo que ya pasaron la preautorización automática y están pendientes de preaprobación. "',
    1,
)

start = text.index('    public async Task QueueVariationNotificationsAsync(')
end = text.index('    private Task QueueAsync(', start)
new_method = r'''    public async Task QueueVariationNotificationsAsync(
        ImportFclRates currentRate,
        CancellationToken cancellationToken = default
    )
    {
        if (!configuration.GetValue("Pricing:RateChangeNotifications:Enabled", true))
            return;

        var completeCost = ResolveCompleteImportedCost(currentRate);
        if (completeCost <= 0m) return;

        var comparable = dbContext.RateHeaders
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted
                && x.PolId == currentRate.PolId
                && x.PoeId == currentRate.PoeId
                && x.ContainerTypeId == currentRate.ContainerTypeId
                && x.TotalCostAmount > 0m);

        var sent = await comparable
            .Where(x => x.Status == RateStatus.Sent)
            .OrderBy(x => x.TotalCostAmount)
            .FirstOrDefaultAsync(cancellationToken);

        var acceptedCutoff = DateTime.UtcNow.AddDays(-7);
        var accepted = await comparable
            .Where(x =>
                x.Status == RateStatus.AcceptedByClient
                && (x.UpdatedAtUtc ?? x.CreatedAtUtc) <= acceptedCutoff)
            .OrderBy(x => x.TotalCostAmount)
            .FirstOrDefaultAsync(cancellationToken);

        IReadOnlyCollection<PricingNotificationRecipient> recipients;
        try
        {
            recipients = await recipientProvider.GetPricingRecipientsAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "No se pudieron resolver usuarios de Pricing para comparar la tarifa completa {ImportRateId}.",
                currentRate.Id
            );
            return;
        }

        if (recipients.Count == 0) return;

        if (sent is not null && completeCost < sent.TotalCostAmount)
        {
            await QueueLowerCompleteRateAsync(
                currentRate,
                completeCost,
                sent.Id,
                sent.RateCode,
                sent.Status.ToString(),
                sent.TotalCostAmount,
                recipients,
                "sent",
                cancellationToken);
        }

        if (accepted is not null && completeCost < accepted.TotalCostAmount)
        {
            await QueueLowerCompleteRateAsync(
                currentRate,
                completeCost,
                accepted.Id,
                accepted.RateCode,
                accepted.Status.ToString(),
                accepted.TotalCostAmount,
                recipients,
                "accepted-7d",
                cancellationToken);
        }
    }

    private async Task QueueLowerCompleteRateAsync(
        ImportFclRates currentRate,
        decimal completeCost,
        Guid comparedRateId,
        string comparedRateCode,
        string comparedStatus,
        decimal comparedCost,
        IReadOnlyCollection<PricingNotificationRecipient> recipients,
        string comparisonKind,
        CancellationToken cancellationToken)
    {
        var savings = comparedCost - completeCost;
        var percentage = comparedCost <= 0m ? 0m : decimal.Round((savings / comparedCost) * 100m, 2);
        var statusLabel = comparisonKind == "sent" ? "enviada" : "aceptada hace al menos 7 días";
        var subject = $"Tarifa completa más baja: {currentRate.PolName} → {currentRate.PoeName} · {currentRate.ContainerTypeName}";
        var body =
            $"Se registró una tarifa cuyo costo completo es menor que una tarifa {statusLabel}. "
            + $"Ruta: {currentRate.PolName} → {currentRate.PoeName} → {currentRate.PodName}; equipo: {currentRate.ContainerTypeName}. "
            + $"Tarifa comparada {comparedRateCode}: {currentRate.CurrencyCode} {comparedCost:N2}; "
            + $"nueva tarifa completa: {currentRate.CurrencyCode} {completeCost:N2}; ahorro: {savings:N2} ({percentage:N2}%). "
            + "La comparación considera el costo completo (flete, origen, destino y recargos), no solamente el flete internacional.";

        var payload = new
        {
            type = "pricing.imported-rate.lower-complete-rate",
            comparisonKind,
            currentImportRateId = currentRate.Id,
            currentRate.ImportBatchId,
            comparedRateId,
            comparedRateCode,
            comparedStatus,
            currentRate.PolName,
            currentRate.PoeName,
            currentRate.PodName,
            currentRate.ContainerTypeName,
            currencyCode = currentRate.CurrencyCode,
            comparedCompleteCost = comparedCost,
            newCompleteCost = completeCost,
            savings,
            percentage,
            action = "review-lower-complete-rate",
            route = "/pricing/imports",
        };

        foreach (var recipient in recipients)
        {
            if (recipient.UserId != Guid.Empty)
            {
                await QueueAsync(
                    "System",
                    "pricing.imported-rate.lower-complete-rate",
                    recipient,
                    subject,
                    body,
                    payload,
                    currentRate.Id,
                    $"pricing-lower-complete:{currentRate.Id:N}:{comparisonKind}:system:{recipient.UserId:N}",
                    cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(recipient.Email))
            {
                await QueueAsync(
                    "Email",
                    "pricing.imported-rate.lower-complete-rate",
                    recipient,
                    subject,
                    body,
                    payload,
                    currentRate.Id,
                    $"pricing-lower-complete:{currentRate.Id:N}:{comparisonKind}:email:{recipient.Email}",
                    cancellationToken);
            }
        }
    }

    private static decimal ResolveCompleteImportedCost(ImportFclRates rate)
    {
        if (rate.TotalCost is > 0m) return rate.TotalCost.Value;

        return Math.Max(0m, rate.OceanFreight ?? rate.Freight)
            + Math.Max(0m, rate.OriginCharges ?? 0m)
            + Math.Max(0m, rate.DestinationCharges ?? 0m)
            + Math.Max(0m, rate.Surcharges ?? 0m);
    }

'''
text = text[:start] + new_method + text[end:]
write(path, text)

print('Pricing Sep-02 patch applied successfully.')
