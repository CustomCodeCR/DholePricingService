using Dhole.Pricing.Application.Abstractions.Reports;
using System.Globalization;
using System.Text.Json.Nodes;
using Dhole.Pricing.Application.Abstractions.Repositories;
using System.Data;
using System.Security.Claims;
using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.Pricing.Api.Authorization;
using Dhole.Pricing.Api.Extensions;
using Dhole.Pricing.Api.Services;
using Dhole.Pricing.Application.Abstractions.Messaging;
using Dhole.Pricing.Application.Abstractions.Services;
using Dhole.Pricing.Application.Features.Rates.ApproveRateMargin;
using Dhole.Pricing.Application.Features.Rates.CreateRate;
using Dhole.Pricing.Application.Features.Rates.DeleteRate;
using Dhole.Pricing.Application.Features.Rates.DuplicateRate;
using Dhole.Pricing.Application.Features.Rates.GetRateById;
using Dhole.Pricing.Application.Features.Rates.GetRateRevisions;
using Dhole.Pricing.Application.Features.Rates.GetRateDashboard;
using Dhole.Pricing.Application.Features.Rates.GetRates;
using Dhole.Pricing.Application.Features.Rates.GenerateRateDocument;
using Dhole.Pricing.Application.Features.Rates.RejectRateMargin;
using Dhole.Pricing.Application.Features.Rates.SetRateStatus;
using Dhole.Pricing.Application.Features.Rates.UpdateRate;
using Dhole.Pricing.Contracts.Rates.Request;
using Dhole.Pricing.Contracts.Rates.Response;
using Dhole.Pricing.Domain.Costs.Enums;
using Dhole.Pricing.Domain.Rates.Entities;
using Dhole.Pricing.Domain.Rates;
using Dhole.Pricing.Domain.Rates.Enums;
using Dhole.Pricing.Domain.Shared;
using Dhole.Pricing.Persistence.DbContexts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Dhole.Pricing.Api.Endpoints;

public static class RateEndpoints
{
    private const string GroupDispatcherKey = "Dhole.Pricing.RateGroup.Dispatcher";
    private const string GroupCreatedIdKey = "Dhole.Pricing.RateGroup.CreatedRateId";
    public static IEndpointRouteBuilder MapRateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/pricing/rates").WithTags("Rates").RequireAuthorization();

        group.MapGet("/", GetRatesAsync).RequireScope(PricingConstants.Scopes.RateView);
        group.MapGet("/realtime-access", () => Results.Ok())
            .RequireScope(PricingConstants.Scopes.RateView);

        group.MapGet("/crm-account/{accountId:guid}", GetAccountQuotesAsync)
            .RequireScope(PricingConstants.Scopes.RateView);
        group.MapGet("/crm-account/{accountId:guid}/dashboard", GetAccountQuoteDashboardAsync)
            .RequireScope(PricingConstants.Scopes.RateView);
        group.MapPost("/crm-account/{accountId:guid}/sync-client", SyncCrmAccountQuotesAsync)
            .RequireScope(PricingConstants.Scopes.RateUpdate)
            .AddEndpointFilter(LegacyQuoteSuperAdminFilter);
        group.MapGet("/crm-lead/{leadId:guid}", GetLeadQuotesAsync)
            .RequireScope(PricingConstants.Scopes.RateView);
        group.MapGet("/crm-lead/{leadId:guid}/dashboard", GetLeadQuoteDashboardAsync)
            .RequireScope(PricingConstants.Scopes.RateView);
        group.MapPost("/crm-legacy-lead-reconcile", ReconcileLegacyLeadQuotesAsync)
            .RequireScope(PricingConstants.Scopes.RateUpdate)
            .AddEndpointFilter(LegacyQuoteSuperAdminFilter);
        group.MapGet("/crm-legacy-catalog", GetLegacyCatalogAsync)
            .RequireScope(PricingConstants.Scopes.RateView)
            .AddEndpointFilter(LegacyQuoteSuperAdminFilter);
        group.MapGet("/crm-legacy-candidates", GetLegacyCandidatesAsync)
            .RequireScope(PricingConstants.Scopes.RateView)
            .AddEndpointFilter(LegacyQuoteSuperAdminFilter);
        group.MapPost("/crm-legacy-reconcile", ReconcileLegacyQuotesAsync)
            .RequireScope(PricingConstants.Scopes.RateUpdate)
            .AddEndpointFilter(LegacyQuoteSuperAdminFilter);

        group
            .MapGet("/dashboard", GetRateDashboardAsync)
            .RequireScope(PricingConstants.Scopes.RateView);

        group
            .MapGet("/{rateId:guid}", GetRateByIdAsync)
            .RequireScope(PricingConstants.Scopes.RateView);

        group
            .MapGet("/{rateId:guid}/revisions", GetRateRevisionsAsync)
            .RequireScope(PricingConstants.Scopes.RateView);


        group
            .MapGet("/report-template-definition", GetRateReportTemplateDefinition)
            .RequireScope(PricingConstants.Scopes.RateView);

        group
            .MapPost("/{rateId:guid}/documents", GenerateRateDocumentAsync)
            .RequireScope(PricingConstants.Scopes.RateReportGenerate);

        group
            .MapGet("/exchange-rate/usd-crc", GetUsdCrcExchangeRateAsync)
            .RequireScope(PricingConstants.Scopes.RateView);

        group
            .MapGet("/exchange-rate/eur-usd", GetEurUsdExchangeRateAsync)
            .RequireScope(PricingConstants.Scopes.RateView);

        group.MapPost("/", CreateRateAsync).RequireIdempotency().RequireScope(PricingConstants.Scopes.RateCreate);
        group.MapPost("/groups", CreateRateGroupAsync).RequireIdempotency().RequireScope(PricingConstants.Scopes.RateCreate);
        group.MapGet("/groups/{rateId:guid}", GetRateGroupAsync).RequireScope(PricingConstants.Scopes.RateView);
        group.MapPost("/groups/{rateId:guid}/select", SelectRateGroupAsync).RequireIdempotency().RequireScope(PricingConstants.Scopes.RateUpdate);

        group
            .MapPut("/{rateId:guid}", UpdateRateAsync)
            .RequireScope(PricingConstants.Scopes.RateUpdate);

        group
            .MapPost("/{rateId:guid}/duplicate", DuplicateRateAsync)
            .RequireIdempotency()
            .RequireScope(PricingConstants.Scopes.RateCreate);

        group
            .MapPost("/{rateId:guid}/margin/approve", ApproveRateMarginAsync)
            .RequireScope(PricingConstants.Scopes.RateApproveLowMargin);

        group
            .MapPost("/{rateId:guid}/margin/reject", RejectRateMarginAsync)
            .RequireScope(PricingConstants.Scopes.RateApproveLowMargin);

        group
            .MapPatch("/{rateId:guid}/status", SetRateStatusAsync);

        // Reenvío explícito de la revisión ya enviada que espera reaprobación
        // del vendedor. No se manipula el estado comercial para forzar un correo.
        group
            .MapPost("/{rateId:guid}/resend", ResendRateRevisionAsync)
            .RequireIdempotency()
            .RequireScope(PricingConstants.Scopes.RateUpdate);

        // Reintento seguro para aceptaciones ya guardadas cuyo Outbox quedó pendiente.
        group
            .MapPost("/{rateId:guid}/acceptance-email/retry", RetryAcceptedRateEmailAsync)
            .RequireIdempotency()
            .RequireScope(PricingConstants.Scopes.RateUpdate);

        // Reenvío manual explícito (con DhModal) con correlación única por operación.
        group
            .MapPost("/{rateId:guid}/acceptance-email/resend", ResendAcceptedRateEmailAsync)
            .RequireIdempotency()
            .RequireScope(PricingConstants.Scopes.RateUpdate);

        group.MapDelete("/", DeleteRatesAsync).RequireScope(PricingConstants.Scopes.RateDelete);

        return app;
    }


    // Historic CRM ownership changes are a privileged administrative action,
    // independent of the ordinary Pricing view/update scopes. This filter runs
    // server-side and applies to legacy search, binding and repair endpoints.
    // Do not grant access to ordinary Admin/Administrador roles.
    private static ValueTask<object?> LegacyQuoteSuperAdminFilter(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
            return ValueTask.FromResult<object?>(Results.Unauthorized());

        return HasLegacyQuoteSuperAdminRole(user)
            ? next(context)
            : ValueTask.FromResult<object?>(Results.Forbid());
    }

    // The same authorization rule is used by the endpoint filter and regression tests.
    private static bool HasLegacyQuoteSuperAdminRole(ClaimsPrincipal user)
    {
        static bool IsAllowedRole(string role) =>
            role.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) ||
            role.Equals("SuperAdministrador", StringComparison.OrdinalIgnoreCase) ||
            role.Equals("SuperUsuario", StringComparison.OrdinalIgnoreCase) ||
            role.Equals("SuperUser", StringComparison.OrdinalIgnoreCase);

        var authorized = new[] { "SuperAdmin", "SuperAdministrador", "SuperUsuario", "SuperUser" }
            .Any(user.IsInRole);
        if (!authorized)
        {
            authorized = user.Claims
                .Where(claim =>
                    claim.Type == ClaimTypes.Role ||
                    claim.Type.Equals("role", StringComparison.OrdinalIgnoreCase) ||
                    claim.Type.Equals("roles", StringComparison.OrdinalIgnoreCase))
                .SelectMany(claim => claim.Value.Split(
                    new[] { ',', ';', ' ' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Any(IsAllowedRole);
        }
        return authorized;
    }

    public sealed record ReconcileLegacyQuotesRequest(
        Guid AccountId, string ClientName, Guid[] RateIds, bool ConfirmedForAccount = false,
        string? ExecutiveName = null);

    public sealed record SyncCrmAccountQuotesRequest(
        string ClientName, string? ExecutiveName, bool ConfirmedForAccount);

    public sealed record ReconcileLegacyLeadQuotesRequest(
        Guid LeadId, string ClientName, Guid[] RateIds,
        bool ConfirmedForLead = false, string? ExecutiveName = null);

    // Normalize only presentation differences (vowel accents/whitespace).
    // Keep Ñ, punctuation, words and business suffixes: broader fuzzy matches
    // could silently associate quotations belonging to different companies.
    private static string LegacyClientKey(string? clientName)
    {
        var key = (clientName ?? string.Empty).Trim().ToUpperInvariant()
            .Replace('Á', 'A').Replace('É', 'E').Replace('Í', 'I')
            .Replace('Ó', 'O').Replace('Ú', 'U').Replace('Ü', 'U')
            .Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        while (key.Contains("  ", StringComparison.Ordinal))
            key = key.Replace("  ", " ", StringComparison.Ordinal);
        return key;
    }

    // All historic QUO records, including already-linked and nameless records, are
    // discoverable through server-side pagination. Existing links are read-only.
    // Nothing is assigned by matching a name.
    // Filter at the database-query level, before Count/Skip/Take, so server totals,
    // pagination and mass selection never include historical TARIFARIO clients.
    // Do not remove linked CRM quotes or mutate their persisted names.
    private static IQueryable<RateHeader> ExcludeLegacyTariffClients(IQueryable<RateHeader> query) =>
        query.Where(x => x.ClientName == null || !x.ClientName.ToUpper().Contains("TARIFARIO"));

    private static async Task<IResult> GetLegacyCatalogAsync(
        string? q, string? linkStatus, int? page, int? pageSize,
        ServiceDbContext db, CancellationToken ct)
    {
        var search = (q ?? string.Empty).Trim();
        if (search.Length > 200) return Results.BadRequest(new { message = "Búsqueda demasiado larga." });
        var state = (linkStatus ?? "all").Trim().ToLowerInvariant();
        if (state is not ("all" or "unlinked" or "linked"))
            return Results.BadRequest(new { message = "Filtro de vinculación inválido." });

        var number = Math.Clamp(page ?? 1, 1, 100000);
        var size = Math.Clamp(pageSize ?? 10, 1, 100);
        var query = db.RateHeaders.AsNoTracking().Where(x => !x.IsDeleted &&
            (x.QuoNumber != null || x.RateCode.StartsWith("QUO-")));
        if (state == "unlinked")
            query = ExcludeLegacyTariffClients(query.Where(x =>
                x.ClientAccountId == null && x.ClientLeadId == null));
        else if (state == "linked")
            query = query.Where(x => x.ClientAccountId != null || x.ClientLeadId != null);
        if (search.Length > 0)
        {
            var term = search.ToUpperInvariant();
            query = query.Where(x =>
                x.RateCode.ToUpper().Contains(term) ||
                (x.QuoNumber != null && x.QuoNumber.ToUpper().Contains(term)) ||
                (x.ClientName != null && x.ClientName.ToUpper().Contains(term)) ||
                (x.PolName != null && x.PolName.ToUpper().Contains(term)) ||
                (x.PoeName != null && x.PoeName.ToUpper().Contains(term)) ||
                (x.PodName != null && x.PodName.ToUpper().Contains(term)));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id).Skip((number - 1) * size).Take(size)
            .Select(x => new {
                x.Id, x.QuoNumber, x.RateCode, x.ClientName, x.ClientAccountId,
                x.ClientLeadId, x.ShipmentMode, x.Status, x.PolName, x.PoeName,
                x.PodName, x.RevisionNumber, x.CreatedAtUtc
            }).ToArrayAsync(ct);
        return Results.Ok(new {
            items = rows.Select(x => new {
                pricingQuoteId = x.Id,
                quoteNumberSnapshot = string.IsNullOrWhiteSpace(x.QuoNumber) ? x.RateCode : x.QuoNumber,
                clientName = x.ClientName,
                clientAccountId = x.ClientAccountId,
                clientLeadId = x.ClientLeadId,
                shipmentModeSnapshot = x.ShipmentMode.ToString(),
                commercialStatusSnapshot = x.Status.ToString(),
                createdAtUtc = x.CreatedAtUtc,
                polName = x.PolName, poeName = x.PoeName, podName = x.PodName,
                revisionNumber = x.RevisionNumber
            }).ToArray(),
            total, page = number, pageSize = size
        });
    }

    // Show candidates, never silently attach historical quotes by matching names.
    // CRM first checks that the caller can see the requested account and supplies
    // its stored legal name; Pricing requires its own rate viewing permission.
    private static async Task<IResult> GetLegacyCandidatesAsync(
        string? clientName, int? page, int? pageSize,
        ServiceDbContext db, CancellationToken ct)
    {
        var expected = LegacyClientKey(clientName);
        if (expected.Length is < 3 or > 220) return Results.BadRequest();
        var number = Math.Clamp(page ?? 1, 1, 100000);
        var size = Math.Clamp(pageSize ?? 30, 1, 100);
        var query = db.RateHeaders.AsNoTracking().Where(x => !x.IsDeleted &&
            x.ClientAccountId == null && x.ClientLeadId == null &&
            x.ClientName != null &&
            x.ClientName.Trim().ToUpper()
                .Replace("Á", "A").Replace("É", "E").Replace("Í", "I")
                .Replace("Ó", "O").Replace("Ú", "U").Replace("Ü", "U")
                .Replace("\t", " ").Replace("\r", " ").Replace("\n", " ")
                .Replace("  ", " ").Replace("  ", " ").Replace("  ", " ") == expected &&
            (x.QuoNumber != null || x.RateCode.StartsWith("QUO-")));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id).Skip((number - 1) * size).Take(size)
            .Select(x => new {
                x.Id, x.QuoNumber, x.RateCode, x.ClientName,
                x.ShipmentMode, x.Status, x.PolName, x.PoeName,
                x.PodName, x.RevisionNumber, x.CreatedAtUtc
            }).ToArrayAsync(ct);
        return Results.Ok(new {
            items = rows.Select(x => new {
                pricingQuoteId = x.Id,
                quoteNumberSnapshot = string.IsNullOrWhiteSpace(x.QuoNumber) ? x.RateCode : x.QuoNumber,
                clientName = x.ClientName,
                shipmentModeSnapshot = x.ShipmentMode.ToString(),
                commercialStatusSnapshot = x.Status.ToString(),
                linkedAt = x.CreatedAtUtc,
                polName = x.PolName, poeName = x.PoeName, podName = x.PodName,
                revisionNumber = x.RevisionNumber,
                available = !string.IsNullOrWhiteSpace(x.PolName) &&
                    (!string.IsNullOrWhiteSpace(x.PodName) ||
                     !string.IsNullOrWhiteSpace(x.PoeName))
            }).ToArray(), total, page = number, pageSize = size
        });
    }

    // A human-confirmed reconciliation is atomic, conservative, and idempotent:
    // never steal an existing lead/account link or change commercial amounts.
    private static async Task<IResult> ReconcileLegacyQuotesAsync(
        ReconcileLegacyQuotesRequest request, ServiceDbContext db,
        ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var expected = LegacyClientKey(request.ClientName);
        var ids = request.RateIds?.Distinct().ToArray() ?? [];
        if (request.AccountId == Guid.Empty || expected.Length is < 1 or > 220 ||
            (!request.ConfirmedForAccount && expected.Length < 3) ||
            request.ExecutiveName?.Length > 220 ||
            ids.Length is < 1 or > 1000 || ids.Any(x => x == Guid.Empty))
            return Results.BadRequest(new { message = "Cuenta, razón social y cotizaciones requeridas." });
        var rates = await db.RateHeaders
            .Where(x => ids.Contains(x.Id) && !x.IsDeleted).ToListAsync(ct);
        if (rates.Count != ids.Length || rates.Any(x =>
            x.ClientLeadId != null ||
            (x.ClientAccountId != null && x.ClientAccountId != request.AccountId) ||
            (!request.ConfirmedForAccount && (string.IsNullOrWhiteSpace(x.ClientName) ||
                !string.Equals(LegacyClientKey(x.ClientName), expected,
                    StringComparison.Ordinal))) ||
            (x.QuoNumber == null && !x.RateCode.StartsWith("QUO-", StringComparison.OrdinalIgnoreCase))))
            return Results.Conflict(new {
                message = "Una o más cotizaciones no coinciden con la razón social o ya pertenecen a otra cuenta."
            });
        var modified = 0;
        foreach (var rate in rates)
        {
            if (request.ConfirmedForAccount)
            {
                // The CRM name and executive replace the current header display while
                // historical publication snapshots and all commercial amounts stay unchanged.
                if (rate.ReconcileLegacyClientAccount(
                    request.AccountId, request.ClientName, request.ExecutiveName))
                    modified++;
            }
            else if (rate.ClientAccountId != request.AccountId)
            {
                rate.ConfigureClientAccount(request.AccountId);
                modified++;
            }
        }
        // Keep all linked seller requests (including open modifications) consistent
        // with the confirmed CRM identity. This is also a repair for older links.
        var requestsUpdated = await SynchronizeLinkedRateRequestsAsync(rates, db, ct);
        if (modified > 0 || requestsUpdated > 0)
        {
            await db.SaveChangesAsync(ct);
            loggerFactory.CreateLogger("Dhole.Pricing.CrmLegacyQuotes").LogInformation(
                "Manually reconciled {Linked} historical QUO and {RequestsUpdated} linked requests to CRM account {AccountId}; rate ids: {RateIds}",
                modified, requestsUpdated, request.AccountId, string.Join(",", ids));
        }
        return Results.Ok(new { linked = modified, total = rates.Count, requestsUpdated });
    }

    // Manually confirmed lead linkage shares the same privilege and anti-stealing
    // rules as historical account linkage. A GET never writes the relationship.
    private static async Task<IResult> ReconcileLegacyLeadQuotesAsync(
        ReconcileLegacyLeadQuotesRequest request, ServiceDbContext db,
        ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var name = request.ClientName?.Trim();
        var ids = request.RateIds?.Distinct().ToArray() ?? [];
        if (request.LeadId == Guid.Empty || !request.ConfirmedForLead ||
            string.IsNullOrWhiteSpace(name) || name.Length > 220 ||
            request.ExecutiveName?.Length > 220 ||
            ids.Length is < 1 or > 1000 || ids.Any(x => x == Guid.Empty))
            return Results.BadRequest(new { message = "Confirme el prospecto y las QUO seleccionadas." });
        var rates = await db.RateHeaders.Where(x => ids.Contains(x.Id) && !x.IsDeleted)
            .ToListAsync(ct);
        if (rates.Count != ids.Length || rates.Any(x =>
            x.ClientAccountId != null ||
            (x.ClientLeadId != null && x.ClientLeadId != request.LeadId) ||
            (x.QuoNumber == null &&
             !x.RateCode.StartsWith("QUO-", StringComparison.OrdinalIgnoreCase))))
            return Results.Conflict(new {
                message = "Una o más QUO pertenecen a otra identidad CRM o no son cotizaciones."
            });
        var updated = 0;
        foreach (var rate in rates)
            if (rate.ReconcileLegacyClientLead(request.LeadId, name, request.ExecutiveName))
                updated++;
        var requestsUpdated = await SynchronizeLinkedRateRequestsAsync(rates, db, ct);
        if (updated > 0 || requestsUpdated > 0)
        {
            await db.SaveChangesAsync(ct);
            loggerFactory.CreateLogger("Dhole.Pricing.CrmLegacyQuotes").LogInformation(
                "Confirmed {Linked} QUO for CRM lead {LeadId}; updated {RequestsUpdated} requests",
                updated, request.LeadId, requestsUpdated);
        }
        return Results.Ok(new { linked = updated, total = rates.Count, requestsUpdated });
    }

    // Reconcile quotes linked before the CRM display-name synchronization was introduced.
    // Deliberately POST-only: GETs never write business data.
    private static async Task<IResult> SyncCrmAccountQuotesAsync(
        Guid accountId, SyncCrmAccountQuotesRequest request,
        ServiceDbContext db, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var name = request.ClientName?.Trim();
        if (accountId == Guid.Empty || !request.ConfirmedForAccount ||
            string.IsNullOrWhiteSpace(name) || name.Length > 220 ||
            request.ExecutiveName?.Length > 220)
            return Results.BadRequest(new { message = "Confirme la cuenta y su nombre CRM válido." });

        var rates = await db.RateHeaders
            .Where(x => !x.IsDeleted && x.ClientAccountId == accountId && x.ClientLeadId == null)
            .ToListAsync(ct);
        var updated = 0;
        foreach (var rate in rates)
            if (rate.ReconcileLegacyClientAccount(accountId, name, request.ExecutiveName))
                updated++;

        var requestsUpdated = await SynchronizeLinkedRateRequestsAsync(rates, db, ct);
        if (updated > 0 || requestsUpdated > 0)
        {
            await db.SaveChangesAsync(ct);
            loggerFactory.CreateLogger("Dhole.Pricing.CrmLegacyQuotes").LogInformation(
                "Synchronized CRM client display on {Updated} of {Total} linked QUO and {RequestsUpdated} requests for account {AccountId}",
                updated, rates.Count, requestsUpdated, accountId);
        }
        return Results.Ok(new { updated, total = rates.Count, requestsUpdated });
    }

    // Never locate requests by free-text client names: a RateId is the only
    // reliable link, and the write is committed with its RateHeader in one unit.
    private static async Task<int> SynchronizeLinkedRateRequestsAsync(
        IReadOnlyCollection<RateHeader> rates, ServiceDbContext db, CancellationToken ct)
    {
        if (rates.Count == 0) return 0;
        var ratesById = rates.ToDictionary(x => x.Id);
        var ids = ratesById.Keys.ToArray();
        var requests = await db.RateRequests
            .Where(x => x.RateId.HasValue && ids.Contains(x.RateId.Value))
            .ToListAsync(ct);
        var updated = 0;
        foreach (var request in requests)
        {
            if (request.RateId is Guid rateId &&
                request.SyncCrmClientIdentity(
                    ratesById[rateId].ClientName, ratesById[rateId].ExecutiveName))
                updated++;
        }
        return updated;
    }

    // Quotes retain the CRM lead that originated them. CRM supplies only conversion
    // mappings confirmed in its own database; never infer identity from a name.
    private static Task<IResult> GetLeadQuotesAsync(Guid leadId, ServiceDbContext db,
        int? page, int? pageSize, CancellationToken ct) =>
        ListCustomerQuotes(db, Guid.Empty, [leadId], page, pageSize, ct);

    private static Task<IResult> GetAccountQuotesAsync(Guid accountId, ServiceDbContext db,
        string? convertedLeadIds, int? page, int? pageSize, CancellationToken ct)
    {
        if (accountId == Guid.Empty) return Task.FromResult<IResult>(Results.BadRequest());
        Guid[] leads = [];
        if (!string.IsNullOrWhiteSpace(convertedLeadIds))
        {
            var values = convertedLeadIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (values.Length > 100 || values.Any(x => !Guid.TryParse(x, out var id) || id == Guid.Empty))
                return Task.FromResult<IResult>(Results.BadRequest());
            leads = values.Select(Guid.Parse).Distinct().ToArray();
        }
        return ListCustomerQuotes(db, accountId, leads, page, pageSize, ct);
    }

    // Official Pricing statuses drive CRM indicators, including QUO linked directly
    // to an account without a CRM opportunity. A single aggregation has no 100-row cap.
    private static async Task<IResult> GetLeadQuoteDashboardAsync(
        Guid leadId, ServiceDbContext db, CancellationToken ct)
    {
        if (leadId == Guid.Empty) return Results.BadRequest();
        var breakdown = await db.RateHeaders.AsNoTracking()
            .Where(x => !x.IsDeleted && x.ClientLeadId == leadId &&
                (x.QuoNumber != null || x.RateCode.StartsWith("QUO-")))
            .GroupBy(x => x.Status)
            .Select(x => new { Status = x.Key, Count = x.Count() })
            .ToListAsync(ct);
        int Count(params string[] statuses) => breakdown
            .Where(x => statuses.Contains(x.Status.ToString(), StringComparer.OrdinalIgnoreCase))
            .Sum(x => x.Count);
        var total = breakdown.Sum(x => x.Count);
        var sent = Count("Sent", "RequestedByClient", "Expired");
        var approved = Count("AcceptedByClient");
        var rejected = Count("RejectedByClient");
        var cancelled = Count("Cancelled");
        return Results.Ok(new {
            total, sent, approved, rejected, cancelled,
            other = total - sent - approved - rejected - cancelled,
            pendingSync = 0, lastSyncAt = (DateTime?)null,
            linkedOnly = true, truncated = false, source = "Pricing"
        });
    }

    private static async Task<IResult> GetAccountQuoteDashboardAsync(
        Guid accountId, ServiceDbContext db, CancellationToken ct)
    {
        if (accountId == Guid.Empty) return Results.BadRequest();
        var breakdown = await db.RateHeaders.AsNoTracking()
            .Where(x => !x.IsDeleted && x.ClientAccountId == accountId &&
                (x.QuoNumber != null || x.RateCode.StartsWith("QUO-")))
            .GroupBy(x => x.Status)
            .Select(x => new { Status = x.Key, Count = x.Count() })
            .ToListAsync(ct);
        int Count(params string[] statuses) => breakdown
            .Where(x => statuses.Contains(x.Status.ToString(), StringComparer.OrdinalIgnoreCase))
            .Sum(x => x.Count);
        var total = breakdown.Sum(x => x.Count);
        var sent = Count("Sent", "RequestedByClient", "Expired");
        var approved = Count("AcceptedByClient");
        var rejected = Count("RejectedByClient");
        var cancelled = Count("Cancelled");
        return Results.Ok(new {
            total, sent, approved, rejected, cancelled,
            other = total - sent - approved - rejected - cancelled,
            pendingSync = 0, lastSyncAt = (DateTime?)null,
            linkedOnly = false, truncated = false,
            source = "Pricing"
        });
    }

    private static async Task<IResult> ListCustomerQuotes(ServiceDbContext db,
        Guid accountId, Guid[] leadIds, int? page, int? pageSize, CancellationToken ct)
    {
        if (accountId == Guid.Empty && leadIds.Length == 0) return Results.BadRequest();
        var number = Math.Clamp(page ?? 1, 1, 100000);
        var size = Math.Clamp(pageSize ?? 30, 1, 100);
        var query = db.RateHeaders.AsNoTracking()
            .Where(x => !x.IsDeleted &&
                (x.QuoNumber != null || x.RateCode.StartsWith("QUO-")) &&
                (x.ClientAccountId == accountId ||
                 (x.ClientLeadId.HasValue && leadIds.Contains(x.ClientLeadId.Value))));
        var total = await query.CountAsync(ct);
        var snapshots = await query.OrderByDescending(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id).Skip((number - 1) * size).Take(size)
            .Select(x => new { x.Id, x.QuoNumber, x.RateCode, x.ShipmentMode,
                x.Status, x.PolName, x.PoeName, x.PodName, x.RevisionNumber,
                x.CreatedAtUtc, x.ClientLeadId, x.ClientAccountId, x.ClientName, x.ExecutiveName }).ToListAsync(ct);
        var items = snapshots.Select(x => new {
            pricingQuoteId = x.Id,
            quoteNumberSnapshot = string.IsNullOrWhiteSpace(x.QuoNumber) ? x.RateCode : x.QuoNumber,
            shipmentModeSnapshot = x.ShipmentMode.ToString(),
            commercialStatusSnapshot = x.Status.ToString(),
            linkedAt = x.CreatedAtUtc, polName = x.PolName,
            poeName = x.PoeName, podName = x.PodName,
            revisionNumber = x.RevisionNumber, clientLeadId = x.ClientLeadId,
            clientAccountId = x.ClientAccountId, clientName = x.ClientName,
            executiveName = x.ExecutiveName,
            available = !string.IsNullOrWhiteSpace(x.PolName) &&
                (!string.IsNullOrWhiteSpace(x.PodName) || !string.IsNullOrWhiteSpace(x.PoeName))
        }).ToArray();
        return Results.Ok(new { items, total, page = number, pageSize = size });
    }

    private static async Task<IResult> GetUsdCrcExchangeRateAsync(
        IPricingExchangeRateProvider exchangeRateProvider,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var snapshot = await exchangeRateProvider.GetUsdCrcAsync(cancellationToken);
        if (snapshot is null)
        {
            return Results.Problem(
                title: "Tipo de cambio no disponible",
                detail: "No fue posible consultar el tipo de cambio del dólar en Hacienda en este momento.",
                statusCode: StatusCodes.Status503ServiceUnavailable
            );
        }

        return EndpointResults.Ok(new
        {
            purchase = snapshot.Purchase,
            sale = snapshot.Sale,
            rateDate = snapshot.RateDate,
            capturedAtUtc = snapshot.CapturedAtUtc,
            source = snapshot.Source
        });
    }

    private static async Task<IResult> GetEurUsdExchangeRateAsync(
        IPricingExchangeRateProvider exchangeRateProvider,
        CancellationToken cancellationToken
    )
    {
        var snapshot = await exchangeRateProvider.GetEurUsdAsync(cancellationToken);
        if (snapshot is null)
        {
            return Results.Problem(
                title: "Tipo de cambio no disponible",
                detail: "No fue posible consultar el tipo de cambio EUR a USD en Hacienda en este momento.",
                statusCode: StatusCodes.Status503ServiceUnavailable
            );
        }

        return EndpointResults.Ok(new
        {
            dollars = snapshot.DollarsPerEuro,
            colones = snapshot.ColonesPerEuro,
            rateDate = snapshot.RateDate,
            capturedAtUtc = snapshot.CapturedAtUtc,
            source = snapshot.Source
        });
    }

    private static IResult GetRateReportTemplateDefinition()
    {
        var sampleData = new
        {
            company = new
            {
                name = "Grupo Castro Fallas",
                legalName = "Grupo Castro Fallas",
                phone = "+506 0000-0000",
                email = "pricing@empresa.com",
                website = "https://logisticacastrofallas.com",
                logoDataUri = ""
            },
            generated = new { date = "06/08/2026", time = "11:00" },
            rate = new
            {
                rateCode = "RATE-ABCDE-123456",
                quoteNumber = "QUO-ABCDE-123456",
                idtraNumber = "IDTRA-2026-00125",
                clientName = "Cliente de ejemplo",
                agent = "Agente de ejemplo",
                carrier = "MSC",
                pol = "Shanghai",
                poe = "Moín",
                pod = "Puerto Caldera",
                route = "Shanghai → Puerto Caldera vía Moín",
                rateType = "TARIFARIO",
                shipmentMode = "Fcl",
                totalPackages = 0,
                totalPallets = 0,
                totalWeightKg = 0m,
                totalVolumeCbm = 0m,
                kgPerCbm = 500m,
                chargeableQuantity = 2m,
                containerType = "1 x 40 HC + 1 x 20 DV",
                containerQuantity = 2,
                containerSummary = "1 x 40 HC + 1 x 20 DV",
                currency = "USD",
                freeDays = 21,
                transitTime = "28 días",
                transitDays = 28,
                validFrom = "08/08/2026",
                validTo = "14/08/2026",
                total = "USD 12,730.00",
                totalAmount = 12730.00m,
                includes = "Flete marítimo y días libres indicados.",
                subjectTo = "Espacio y equipo disponibles.",
                excludes = "Impuestos y gastos no indicados.",
                status = "Open"
            },
            containers = new[]
            {
                new { containerTypeId = Guid.Empty, containerType = "40 HC", containerTypeName = "40 HC", containerTypeCode = "40HC", quantity = 1, label = "1 x 40 HC" },
                new { containerTypeId = Guid.Empty, containerType = "20 DV", containerTypeName = "20 DV", containerTypeCode = "20DV", quantity = 1, label = "1 x 20 DV" }
            },
            items = new[]
            {
                new { description = "Flete marítimo", quantity = 2, unitSale = "USD 6,300.00", unitSaleAmount = 6300m, lineTotal = "USD 12,600.00", lineTotalAmount = 12600m, notes = "" },
                new { description = "ISPS", quantity = 2, unitSale = "USD 15.00", unitSaleAmount = 15m, lineTotal = "USD 30.00", lineTotalAmount = 30m, notes = "Por contenedor" },
                new { description = "P/S", quantity = 2, unitSale = "USD 50.00", unitSaleAmount = 50m, lineTotal = "USD 100.00", lineTotalAmount = 100m, notes = "Por contenedor" }
            },
            rows = new[]
            {
                new Dictionary<string, object?> { ["Concepto"] = "Flete marítimo", ["Cantidad"] = 2, ["Moneda"] = "USD", ["Precio unitario"] = 6300m, ["Total"] = 12600m, ["Notas"] = "" }
            }
        };

        var variables = new[]
        {
            "company.name", "company.legalName", "company.phone", "company.email", "company.website", "company.logoDataUri",
            "generated.date", "generated.time",
            "rate.rateCode", "rate.quoteNumber", "rate.idtraNumber", "rate.clientName", "rate.agent", "rate.carrier",
            "rate.pol", "rate.poe", "rate.pod", "rate.route", "rate.rateType", "rate.shipmentMode", "rate.totalPackages", "rate.totalPallets",
            "rate.totalWeightKg", "rate.totalVolumeCbm", "rate.kgPerCbm", "rate.chargeableQuantity",
            "rate.containerType", "rate.containerQuantity", "rate.containerSummary", "rate.currency",
            "containers[].containerType", "containers[].containerTypeName", "containers[].containerTypeCode", "containers[].quantity", "containers[].label",
            "rate.freeDays", "rate.transitTime", "rate.transitDays", "rate.validFrom", "rate.validTo", "rate.total", "rate.totalAmount",
            "rate.includes", "rate.subjectTo", "rate.excludes", "rate.status",
            "items[].description", "items[].quantity", "items[].unitSale", "items[].unitSaleAmount", "items[].lineTotal", "items[].lineTotalAmount", "items[].notes"
        };

        return EndpointResults.Ok(new
        {
            templateCode = "pricing-fcl-client-quote",
            name = "Cotización FCL para cliente",
            pageSize = "A4",
            orientation = "Portrait",
            variables,
            sampleData
        });
    }

    private static async Task<IResult> GenerateRateDocumentAsync(
        Guid rateId,
        GenerateRateDocumentRequest request,
        ICommandDispatcher dispatcher,
        ServiceDbContext db,
        IRateHeaderRepository rateHeaders,
        IRateCommentStore rateComments,
        IRateReportDataFactory dataFactory,
        IPricingReportsClient reportsClient,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Before a decision the PDF shows the group; after selection only the individual quote is valid.
        // Look up siblings by the immutable group table, never the currently active POE.
        if (string.IsNullOrWhiteSpace(request.Format) ||
            string.Equals(request.Format, "pdf", StringComparison.OrdinalIgnoreCase))
        {
            var groupedIds = new List<Guid>();
            Guid? finalizedRateId = null;
            var parentQuote = string.Empty;
            var connection = db.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose) await connection.OpenAsync(cancellationToken);
            try
            {
                await using var lookup = connection.CreateCommand();
                lookup.CommandText = """
                    SELECT a.rate_id, a.base_quote, a.status FROM pricing.rate_quote_alternatives a
                    WHERE a.parent_rate_id = (
                        SELECT parent_rate_id FROM pricing.rate_quote_alternatives
                        WHERE rate_id = @rate_id LIMIT 1
                    ) ORDER BY CASE WHEN a.status = 'Selected' THEN 0 ELSE 1 END,
                               length(a.suffix), a.suffix
                    """;
                var parameter = lookup.CreateParameter();
                parameter.ParameterName = "@rate_id";
                parameter.Value = rateId;
                lookup.Parameters.Add(parameter);
                await using var reader = await lookup.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    groupedIds.Add(reader.GetGuid(0));
                    parentQuote = reader.GetString(1);
                    if (reader.GetString(2) == "Selected") finalizedRateId = reader.GetGuid(0);
                }
            }
            finally
            {
                if (shouldClose) await connection.CloseAsync();
            }

            if (finalizedRateId.HasValue)
            {
                // Any historic sub-QUO URL generates the one definitive quote.
                rateId = finalizedRateId.Value;
            }
            else if (groupedIds.Count > 1)
            {
                var options = new List<PricingQuoteGroupDocumentItem>(groupedIds.Count);
                foreach (var childId in groupedIds)
                {
                    var rate = await rateHeaders.GetByIdWithDetailsAsync(childId, cancellationToken);
                    if (rate is null || rate.IsDeleted)
                        return Results.NotFound(new { code = "Pricing.QuoteAlternativeMissing" });
                    if (rate.RequiredApproval || rate.Status is not (
                        RateStatus.ApprovedByManagement or RateStatus.Open or RateStatus.Sent
                        or RateStatus.AcceptedByClient or RateStatus.RejectedByClient
                        or RateStatus.Closed or RateStatus.Expired))
                        return Results.Conflict(new { code = "Pricing.QuoteGroupRequiresApproval" });

                    var comments = await rateComments.GetAsync(childId, cancellationToken);
                    var json = JsonNode.Parse(dataFactory.CreateDataJson(rate)) as JsonObject;
                    if (json?["rate"] is JsonObject rateData)
                        rateData["comments"] = comments?.Trim() ?? string.Empty;

                    var freight = rate.RateDetails.Where(detail => detail.CostDetailType == CostDetailType.Freight).ToArray();
                    var oceanFreight = freight.Sum(detail => detail.SaleAmount * detail.Quantity);
                    var freightCurrency = QuoteCurrencyValue.Resolve(freight.FirstOrDefault()?.CurrencyName, rate.CurrencyName);
                    var route = rate.PolName + " → " + rate.PoeName
                        + (string.IsNullOrWhiteSpace(rate.PodName) ? "" : " → " + rate.PodName);
                    if (!string.IsNullOrWhiteSpace(rate.CarrierRoute))
                        route += " · Ruta naviera: " + rate.CarrierRoute.Trim();
                    options.Add(new PricingQuoteGroupDocumentItem(
                        rate.QuoNumber ?? rate.RateCode,
                        route,
                        rate.CarrierName,
                        oceanFreight.ToString("N2", CultureInfo.GetCultureInfo("en-US")),
                        freightCurrency,
                        json?.ToJsonString() ?? dataFactory.CreateDataJson(rate)));
                }
                try
                {
                    var document = await reportsClient.GenerateGroupedPricingQuoteAsync(
                        parentQuote, options, cancellationToken);
                    return Results.File(document.Content, document.ContentType, document.FileName);
                }
                catch (HttpRequestException)
                {
                    return Results.Problem(
                        title: "No se pudo generar el PDF completo de multiQUO.",
                        statusCode: StatusCodes.Status502BadGateway);
                }
            }
        }

        var result = await dispatcher.DispatchAsync(
            new GenerateRateDocumentCommand(rateId, request.TemplateCode, request.Format),
            cancellationToken);

        return result.IsSuccess
            ? Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName)
            : EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> GetRateDashboardAsync(
        DateTime? createdFrom,
        DateTime? createdTo,
        DateTime? modifiedFrom,
        DateTime? modifiedTo,
        DateTime? validityFrom,
        DateTime? validityTo,
        IQueryDispatcher dispatcher,
        RateCreatorIdentityService creatorIdentityService,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (createdFrom.HasValue && createdTo.HasValue && createdFrom > createdTo)
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidCreatedDateRange",
                "La fecha inicial de creación no puede ser posterior a la fecha final.",
                httpContext
            );
        }

        if (modifiedFrom.HasValue && modifiedTo.HasValue && modifiedFrom > modifiedTo)
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidModifiedDateRange",
                "La fecha inicial de modificación no puede ser posterior a la fecha final.",
                httpContext
            );
        }

        if (validityFrom.HasValue && validityTo.HasValue && validityFrom > validityTo)
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidValidityDateRange",
                "La fecha inicial de vigencia no puede ser posterior a la fecha final.",
                httpContext
            );
        }

        var result = await dispatcher.DispatchAsync(
            new GetRateDashboardQuery(
                createdFrom,
                createdTo,
                modifiedFrom,
                modifiedTo,
                validityFrom,
                validityTo
            ),
            cancellationToken
        );

        if (result.IsFailure)
            return EndpointResults.FromResult(result, httpContext);

        var dashboard = await creatorIdentityService.EnrichAsync(
            result.Value,
            cancellationToken
        );

        return EndpointResults.Ok(dashboard);
    }

    private static Guid[] ReadGuidOptions(HttpContext context, string key)
    {
        var ids = new HashSet<Guid>();
        foreach (var raw in context.Request.Query[key])
        {
            if (!Guid.TryParse(raw, out var id) || id == Guid.Empty)
                throw new BadHttpRequestException($"Invalid {key} value.", 400);
            ids.Add(id);
        }

        return ids.ToArray();
    }

    private static RateStatus[] ReadStatusOptions(HttpContext context)
    {
        var statuses = new HashSet<RateStatus>();
        foreach (var raw in context.Request.Query["statuses"])
        {
            // Categoría virtual: no representa un estado del dominio RateStatus.
            if (string.Equals(raw, "PendingReapproval", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!Enum.TryParse<RateStatus>(raw, true, out var status) || !Enum.IsDefined(status))
                throw new BadHttpRequestException("Invalid statuses value.", 400);
            statuses.Add(status);
        }

        return statuses.ToArray();
    }

    private static async Task<IResult> GetRatesAsync(
        int? pageNumber,
        int? pageSize,
        string? search,
        string? idtraNumber,
        string? quoNumber,
        Guid? sourceImportFclRateId,
        Guid? sourceTariffRateId,
        RateType? rateType,
        bool? tariffMasterOnly,
        bool? excludeTariffMasters,
        bool? groupQuotes,
        Guid? agentId,
        Guid? carrierId,
        Guid? polId,
        Guid? poeId,
        Guid? podId,
        Guid? containerTypeId,
        Guid? currencyId,
        RateStatus? status,
        bool? requiredApproval,
        DateTime? quoteDate,
        DateTime? validFrom,
        DateTime? validTo,
        IQueryDispatcher dispatcher,
        RateCreatorIdentityService creatorIdentityService,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetRatesQuery(
                PageRequest.Create(pageNumber ?? 1, pageSize ?? 20),
                search,
                idtraNumber,
                quoNumber,
                sourceImportFclRateId,
                sourceTariffRateId,
                rateType,
                tariffMasterOnly,
                excludeTariffMasters,
                agentId,
                carrierId,
                polId,
                poeId,
                podId,
                containerTypeId,
                currencyId,
                status,
                requiredApproval,
                quoteDate,
                validFrom,
                validTo,
                httpContext.Request.Query["executiveNames"].ToArray(),
                new RateMultiSelectFilters(
                    ReadStatusOptions(httpContext),
                    ReadGuidOptions(httpContext, "agentIds"),
                    ReadGuidOptions(httpContext, "carrierIds"),
                    ReadGuidOptions(httpContext, "polIds"),
                    ReadGuidOptions(httpContext, "poeIds"),
                    ReadGuidOptions(httpContext, "podIds"),
                    ReadGuidOptions(httpContext, "containerTypeIds"),
                    ReadGuidOptions(httpContext, "currencyIds"),
                    httpContext.Request.Query["statuses"].Any(value =>
                        string.Equals(value, "PendingReapproval", StringComparison.OrdinalIgnoreCase))
                ),
                groupQuotes == true
            ),
            cancellationToken
        );

        if (result.IsFailure)
            return EndpointResults.FromPaged(result, httpContext);

        var items = await creatorIdentityService.EnrichAsync(
            result.Value.Items,
            cancellationToken
        );
        var page = PagedResult<RateDto>.Create(
            items,
            result.Value.PageNumber,
            result.Value.PageSize,
            result.Value.TotalCount
        );

        return EndpointResults.FromPaged(page);
    }

    private static async Task<IResult> GetRateByIdAsync(
        Guid rateId,
        IQueryDispatcher dispatcher,
        RateCreatorIdentityService creatorIdentityService,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new GetRateByIdQuery(rateId),
            cancellationToken
        );

        if (result.IsFailure)
            return EndpointResults.FromResult(result, httpContext);

        var items = await creatorIdentityService.EnrichAsync(
            [result.Value],
            cancellationToken
        );

        return EndpointResults.Ok(items[0]);
    }

    private static async Task<IResult> GetRateRevisionsAsync(
        Guid rateId, IQueryDispatcher dispatcher, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var result = await dispatcher.DispatchAsync(new GetRateRevisionsQuery(rateId), cancellationToken);
        return EndpointResults.FromResult(result, httpContext);
    }

    // El generador QUO usa una transacción explícita: con EnableRetryOnFailure
    // la reserva, el guardado y el commit deben ejecutarse dentro de la estrategia EF.
    // Cada reintento recibe un DbContext nuevo para evitar entidades rastreadas
    // o eventos de outbox pendientes de un intento que se revirtió.
    private static async Task<TResult> DispatchWithTransactionalRateCodeAsync<TResult>(
        ServiceDbContext db,
        HttpContext httpContext,
        Func<ICommandDispatcher, Task<TResult>> dispatch
    )
    {
        // The group endpoint owns one scoped dispatcher and one outer EF transaction.
        // Creating a new scope for every child would commit partial QUOs.
        if (httpContext.Items.TryGetValue(GroupDispatcherKey, out var shared)
            && shared is ICommandDispatcher groupDispatcher)
            return await dispatch(groupDispatcher);

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var retryScope = httpContext.RequestServices
                .GetRequiredService<IServiceScopeFactory>()
                .CreateAsyncScope();
            var retryDispatcher = retryScope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            return await dispatch(retryDispatcher);
        });
    }

    private static async Task<IResult> CreateRateAsync(
        CreateRateRequest request,
        ServiceDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (request.ClientAccountId.HasValue && request.ClientLeadId.HasValue)
            return Results.BadRequest(new { message = "Seleccione una cuenta o un prospecto, no ambos." });
        var termValidation = ValidateExclusiveRateTerms(request.Includes, request.SubjectTo, request.Excludes, httpContext);
        if (termValidation is not null) return termValidation;

        if (
            request.SourceImportFclRateId.HasValue
            && !HasScope(httpContext.User, PricingConstants.Scopes.ImportFclRateCreateAsRate)
        )
        {
            return Results.Forbid();
        }

        var details = new List<CreateRateDetailCommandItem>();

        foreach (var detail in request.Details)
        {
            if (!TryParseDefinedEnum(detail.CostDetailType, out CostDetailType costDetailType))
            {
                return EndpointResults.BadRequest(
                    "Pricing.InvalidCostDetailType",
                    $"El rubro '{detail.CostDetailType}' no es válido.",
                    httpContext
                );
            }

            if (!TryParseDefinedEnum(detail.CostType, out CostType costType))
            {
                return EndpointResults.BadRequest(
                    "Pricing.InvalidCostType",
                    $"El tipo '{detail.CostType}' no es válido.",
                    httpContext
                );
            }

            ChargeBasis? chargeBasis = null;
            if (!string.IsNullOrWhiteSpace(detail.ChargeBasis))
            {
                if (!TryParseDefinedEnum(detail.ChargeBasis, out ChargeBasis parsedChargeBasis))
                {
                    return EndpointResults.BadRequest(
                        "Pricing.InvalidChargeBasis",
                        $"La base de cobro '{detail.ChargeBasis}' no es válida.",
                        httpContext
                    );
                }
                chargeBasis = parsedChargeBasis;
            }

            details.Add(
                new CreateRateDetailCommandItem(
                    detail.CostId,
                    detail.Name,
                    costDetailType,
                    costType,
                    detail.CurrencyId,
                    detail.CurrencyName,
                    detail.CurrencyCode,
                    detail.CostAmount,
                    detail.SaleAmount,
                    detail.Notes,
                    detail.Quantity,
                    chargeBasis,
                    detail.ApplyDestinationTax,
                    detail.DestinationTaxRate,
                    detail.BillToClient
                )
            );
        }

        if (!TryParseDefinedEnum(request.ShipmentMode, out ShipmentMode shipmentMode))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidShipmentMode",
                $"La modalidad '{request.ShipmentMode}' no es válida.",
                httpContext
            );
        }

        if (!TryParseDefinedEnum(request.OperationType, out RateOperationType operationType))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidOperationType",
                $"El tipo de operación '{request.OperationType}' no es válido.",
                httpContext
            );
        }

        if (!TryParseDefinedEnum(request.RateType, out RateType rateType))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidRateType",
                $"El tipo de tarifa '{request.RateType}' no es válido.",
                httpContext
            );
        }

        var cargoLines = (request.CargoLines ?? Array.Empty<RateCargoLineRequest>())
            .Select(x => new Dhole.Pricing.Application.Features.Rates.RateCargoLineCommandItem(
                x.Description, x.Packages, x.Pallets, x.WeightKg, x.LengthCm, x.WidthCm, x.HeightCm, x.IsStackable ?? true, x.UnitType))
            .ToArray();

        var containers = (request.Containers ?? Array.Empty<RateContainerRequest>())
            .Select(x => new RateContainerCommandItem(
                x.ContainerTypeId,
                x.ContainerTypeName,
                x.ContainerTypeCode,
                x.Quantity,
                x.UnitWeightsKg
            ))
            .ToList();

        if (containers.Count == 0)
        {
            var synthetic = SyntheticEquipment(shipmentMode, request.ContainerTypeId, request.ContainerTypeName, request.ContainerTypeCode);
            containers.Add(new RateContainerCommandItem(
                synthetic.Id, synthetic.Name, synthetic.Code, Math.Max(request.ContainerQuantity, 1)));
        }

        /*
         * Este valor se calcula desde los claims.
         * Nunca debe recibirse desde el frontend.
         */
        var canApproveImportedRate = HasScope(
            httpContext.User,
            PricingConstants.Scopes.ImportFclRateApprove
        );
        var canApproveLowMargin = HasScope(
            httpContext.User,
            PricingConstants.Scopes.RateApproveLowMargin
        );
        var freeDays = await ResolveConfiguredFreeDaysAsync(
            db,
            request.CarrierId,
            request.FreeDays,
            cancellationToken
        );

        var creationCommand = new CreateRateCommand(
                request.SourceImportFclRateId,
                request.AgentId,
                request.AgentName,
                request.AgentCode,
                request.CarrierId,
                request.CarrierName,
                request.CarrierCode,
                request.PolId,
                request.PolName,
                request.PolCode,
                request.PoeId,
                request.PoeName,
                request.PoeCode,
                request.PodId,
                request.PodName,
                request.PodCode,
                request.ContainerTypeId,
                request.ContainerTypeName,
                request.ContainerTypeCode,
                request.IncotermId,
                request.IncotermName,
                request.IncotermCode,
                request.CurrencyId,
                request.CurrencyName,
                request.CurrencyCode,
                freeDays,
                request.ValidFrom,
                request.ValidTo,
                request.ContainerQuantity,
                request.ClientName,
                request.ExecutiveName,
                request.IdtraNumber,
                request.QuoNumber,
                request.Includes,
                request.SubjectTo,
                request.Excludes,
                request.TransitTime,
                details,
                containers,
                rateType,
                shipmentMode,
                request.KgPerCbm,
                request.TotalPackages,
                request.TotalPallets,
                request.TotalWeightKg,
                request.TotalVolumeCbm,
                cargoLines,
                request.WarehouseId,
                request.PickupAddress,
                request.PickupLatitude,
                request.PickupLongitude,
                request.ExchangeRatePurchase,
                request.ExchangeRateSale,
                request.ExchangeRateApplied,
                canApproveImportedRate,
                operationType,
                (request.Services ?? [])
                    .Select(x => new RateServiceSelection(x.Id, x.Name, x.Code))
                    .ToArray(),
                request.UseAllInPresentation,
                canApproveLowMargin,
                httpContext.GetCurrentUserId()
            )
            {
                FinalBackupStorageIds = request.FinalBackupStorageIds ?? [],
                ClientAccountId = request.ClientAccountId,
                ClientLeadId = request.ClientLeadId,
                OtherReferences = request.OtherReferences,
                LclGroupAgentId = request.LclGroupAgentId,
                PickupLocationsJson = SerializePickupLocations(request.PickupLocations),
                InlandServiceLocationsJson = request.InlandPickupLocation is null && request.InlandDeliveryLocation is null
                    ? null
                    : System.Text.Json.JsonSerializer.Serialize(new
                    {
                        pickup = request.InlandPickupLocation,
                        delivery = request.InlandDeliveryLocation
                    }),
                StorageDays = request.StorageDays,
                CargoValue = request.CargoValue,
                TransitDays = request.TransitDays,
                CarrierRoute = request.CarrierRoute,
                CarrierComments = request.CarrierComments,
            };

        var result = await DispatchWithTransactionalRateCodeAsync(
            db,
            httpContext,
            retryDispatcher => retryDispatcher.DispatchAsync(creationCommand, cancellationToken)
        );

        if (result.IsSuccess && httpContext.Items.ContainsKey(GroupDispatcherKey))
            httpContext.Items[GroupCreatedIdKey] = result.Value;
        return EndpointResults.FromResult(result, httpContext);
    }


    /// <summary>
    /// Creates all FCL routes in the same PostgreSQL transaction. Individual
    /// CreateRate commands keep their existing validation and audit semantics;
    /// no RateHeader, sequence reservation or outbox event is committed unless
    /// every POE/carrier alternative succeeds.
    /// </summary>
    private static async Task<IResult> CreateRateGroupAsync(
        CreateRateGroupRequest request,
        ServiceDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var alternatives = request.Alternatives?.ToArray() ?? [];
        if (alternatives.Length < 2 || alternatives.Length > 52)
            return EndpointResults.BadRequest("Pricing.InvalidQuoteGroup",
                "Una multiQUO requiere de 2 a 52 alternativas FCL.", httpContext);

        var candidates = new List<QuoteAlternativeCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var first = alternatives[0].Rate;
        foreach (var alternative in alternatives)
        {
            var rate = alternative.Rate;
            if (!string.Equals(rate.ShipmentMode, "Fcl", StringComparison.OrdinalIgnoreCase)
                || rate.PoeId == Guid.Empty || !rate.CarrierId.HasValue || rate.CarrierId.Value == Guid.Empty)
                return EndpointResults.BadRequest("Pricing.InvalidQuoteGroupRoute",
                    "Las alternativas deben tener POE y naviera válidos y modalidad FCL.", httpContext);
            if (rate.PolId != first.PolId || rate.PodId != first.PodId
                || rate.ContainerTypeId != first.ContainerTypeId
                || rate.ClientAccountId != first.ClientAccountId || rate.ClientLeadId != first.ClientLeadId
                || !string.Equals(rate.ClientName?.Trim(), first.ClientName?.Trim(), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(rate.OperationType, first.OperationType, StringComparison.OrdinalIgnoreCase))
                return EndpointResults.BadRequest("Pricing.InconsistentQuoteGroup",
                    "Todas las alternativas deben compartir cliente, origen, POD, equipo y operación.", httpContext);
            if (!Enum.TryParse<QuoteAlternativeHaulage>(alternative.HaulageMode, true, out var haulage)
                || !Enum.IsDefined(haulage))
                return EndpointResults.BadRequest("Pricing.InvalidHaulageMode",
                    "La alternativa requiere Merchant o Carrier.", httpContext);
            if (rate.Details is null || rate.Details.Count == 0)
                return EndpointResults.BadRequest("Pricing.EmptyQuoteAlternative",
                    "Cada alternativa debe incluir sus cargos y flete individuales.", httpContext);

            var candidate = new QuoteAlternativeCandidate(rate.PoeId, rate.CarrierId!.Value, haulage,
                alternative.RouteKey);
            if (!seen.Add(QuoteAlternativeNumbering.Key(candidate)))
                return EndpointResults.BadRequest("Pricing.DuplicateQuoteAlternative",
                    "El mismo POE, naviera, transporte y ruta están duplicados.", httpContext);
            candidates.Add(candidate);
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var scope = httpContext.RequestServices
                .GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
            var scopedDb = scope.ServiceProvider.GetRequiredService<ServiceDbContext>();
            var scopedDispatcher = scope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
            await using var transaction = await scopedDb.Database.BeginTransactionAsync(cancellationToken);
            var created = new List<Guid>(alternatives.Length);
            httpContext.Items[GroupDispatcherKey] = scopedDispatcher;
            try
            {
                foreach (var alternative in alternatives)
                {
                    httpContext.Items.Remove(GroupCreatedIdKey);
                    // The final commercial number is assigned after the root QUO
                    // has been reserved, never accepted from a browser payload.
                    var childRequest = alternative.Rate with
                    {
                        QuoNumber = null,
                        // The active UI option must not propagate its presentation
                        // to a different POE. Always enforce this per child route.
                        UseAllInPresentation = PanamaCostaRicaAllInPolicy.Applies(
                            alternative.Rate.ShipmentMode,
                            alternative.Rate.PoeCode,
                            alternative.Rate.PoeName,
                            alternative.Rate.PodCode,
                            alternative.Rate.PodName)
                    };
                    var outcome = await CreateRateAsync(childRequest, scopedDb, httpContext, cancellationToken);
                    if (httpContext.Items[GroupCreatedIdKey] is not Guid createdId)
                        return outcome; // transaction rolls back on dispose
                    created.Add(createdId);
                }

                var parentId = created[0];
                var baseQuote = await scopedDb.RateHeaders.AsNoTracking()
                    .Where(rate => rate.Id == parentId)
                    .Select(rate => rate.RateCode)
                    .SingleAsync(cancellationToken);
                var responseAlternatives = new List<object>(created.Count);
                for (var index = 0; index < created.Count; index++)
                {
                    var id = created[index];
                    var candidate = candidates[index];
                    var suffix = QuoteAlternativeNumbering.SuffixAt(index);
                    var quote = QuoteAlternativeNumbering.SubQuoteNumber(baseQuote, suffix);
                    var routeKey = candidate.RouteKey?.Trim() ?? string.Empty;
                    var haulage = candidate.HaulageMode.ToString();
                    await scopedDb.Database.ExecuteSqlInterpolatedAsync(
                        $@"UPDATE pricing.""RateHeaders"" SET quo_number = {quote} WHERE id = {id}",
                        cancellationToken);
                    await scopedDb.Database.ExecuteSqlInterpolatedAsync(
                        $@"INSERT INTO pricing.rate_quote_alternatives
                            (rate_id, parent_rate_id, base_quote, alternative_quote, suffix,
                             poe_id, pod_id, carrier_id, haulage_mode, route_key, status)
                           VALUES ({id}, {parentId}, {baseQuote}, {quote}, {suffix},
                                   {candidate.PoeId}, {alternatives[index].Rate.PodId},
                                   {candidate.CarrierId}, {haulage}, {routeKey}, 'Draft')",
                        cancellationToken);
                    responseAlternatives.Add(new
                    {
                        rateId = id, quoteNumber = quote, suffix,
                        poeId = candidate.PoeId, podId = alternatives[index].Rate.PodId,
                        carrierId = candidate.CarrierId, haulageMode = haulage
                    });
                }
                await transaction.CommitAsync(cancellationToken);
                return Results.Ok(new { parentRateId = parentId, parentQuoteNumber = baseQuote,
                    alternatives = responseAlternatives });
            }
            finally
            {
                httpContext.Items.Remove(GroupDispatcherKey);
                httpContext.Items.Remove(GroupCreatedIdKey);
            }
        });
    }

    private static async Task<IResult> GetRateGroupAsync(
        Guid rateId,
        ServiceDbContext db,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT a.parent_rate_id, a.base_quote, a.rate_id, a.alternative_quote,
                       a.suffix, a.poe_id, a.pod_id, a.carrier_id,
                       a.haulage_mode, a.status, h.poe_name, h.pod_name, h.carrier_name
                FROM pricing.rate_quote_alternatives a
                INNER JOIN pricing."RateHeaders" h ON h.id = a.rate_id
                WHERE a.parent_rate_id = (
                    SELECT parent_rate_id FROM pricing.rate_quote_alternatives
                    WHERE rate_id = @rate_id LIMIT 1
                )
                ORDER BY length(a.suffix), a.suffix;
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@rate_id";
            parameter.Value = rateId;
            command.Parameters.Add(parameter);
            var items = new List<object>();
            Guid parentId = Guid.Empty;
            Guid? selectedRateId = null;
            string parentQuote = string.Empty;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                parentId = reader.GetGuid(0);
                parentQuote = reader.GetString(1);
                if (reader.GetString(9) == "Selected")
                    selectedRateId = reader.GetGuid(2);
                items.Add(new
                {
                    rateId = reader.GetGuid(2),
                    quoteNumber = reader.GetString(3),
                    suffix = reader.GetString(4),
                    poeId = reader.GetGuid(5),
                    podId = reader.IsDBNull(6) ? (Guid?)null : reader.GetGuid(6),
                    carrierId = reader.GetGuid(7),
                    haulageMode = reader.GetString(8),
                    status = reader.GetString(9),
                    poeName = reader.GetString(10),
                    podName = reader.IsDBNull(11) ? null : reader.GetString(11),
                    carrierName = reader.GetString(12)
                });
            }
            return items.Count > 0
                ? Results.Ok(new { parentRateId = parentId, parentQuoteNumber = parentQuote,
                    selectedRateId, alternatives = items })
                : Results.NotFound();
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }


    /// <summary>
    /// Selects exactly one commercial alternative. The parent row lock serializes
    /// concurrent decisions; the unique partial index is the final DB safeguard.
    /// </summary>
    private static async Task<IResult> SelectRateGroupAsync(
        Guid rateId,
        ServiceDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var candidate = await db.RateHeaders.AsNoTracking()
            .FirstOrDefaultAsync(rate => rate.Id == rateId && !rate.IsDeleted, cancellationToken);
        if (candidate is null)
            return Results.NotFound();

        // Resolve the parent using a parameterized query; no user-supplied
        // quotation numbers are allowed to select an unrelated group.
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        Guid? parentId;
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT parent_rate_id FROM pricing.rate_quote_alternatives
                WHERE rate_id = @rate_id LIMIT 1;
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@rate_id";
            parameter.Value = rateId;
            command.Parameters.Add(parameter);
            var raw = await command.ExecuteScalarAsync(cancellationToken);
            parentId = raw is null or DBNull ? null : (Guid)raw;
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
        if (!parentId.HasValue)
            return Results.NotFound();

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, cancellationToken);
            // Lock the common parent before checking other accepted alternatives.
            await using (var lockCommand = db.Database.GetDbConnection().CreateCommand())
            {
                lockCommand.Transaction = transaction.GetDbTransaction();
                lockCommand.CommandText = """
                    SELECT id FROM pricing."RateHeaders" WHERE id = @parent FOR UPDATE;
                    """;
                var parameter = lockCommand.CreateParameter();
                parameter.ParameterName = "@parent";
                parameter.Value = parentId.Value;
                lockCommand.Parameters.Add(parameter);
                await lockCommand.ExecuteScalarAsync(cancellationToken);
            }
            Guid? existingSelectedId;
            await using (var check = db.Database.GetDbConnection().CreateCommand())
            {
                check.Transaction = transaction.GetDbTransaction();
                check.CommandText = """
                    SELECT rate_id FROM pricing.rate_quote_alternatives
                    WHERE parent_rate_id = @parent AND status = 'Selected' LIMIT 1;
                    """;
                var parameter = check.CreateParameter();
                parameter.ParameterName = "@parent";
                parameter.Value = parentId.Value;
                check.Parameters.Add(parameter);
                var raw = await check.ExecuteScalarAsync(cancellationToken);
                existingSelectedId = raw is null or DBNull ? null : (Guid)raw;
            }
            if (existingSelectedId.HasValue && existingSelectedId.Value != rateId)
                return Results.Conflict(new { code = "Pricing.QuoteGroupSelectionFinal",
                    message = "La sub-QUO definitiva ya fue elegida; no puede cambiarse." });

            // Never replace a prior customer acceptance, even for old groups.
            await using (var acceptance = db.Database.GetDbConnection().CreateCommand())
            {
                acceptance.Transaction = transaction.GetDbTransaction();
                acceptance.CommandText = """
                    SELECT a.rate_id FROM pricing.rate_quote_alternatives a
                    JOIN pricing."RateHeaders" h ON h.id = a.rate_id
                    WHERE a.parent_rate_id = @parent AND h.status = 'AcceptedByClient'
                    LIMIT 1;
                    """;
                var parameter = acceptance.CreateParameter();
                parameter.ParameterName = "@parent";
                parameter.Value = parentId.Value;
                acceptance.Parameters.Add(parameter);
                if (await acceptance.ExecuteScalarAsync(cancellationToken) is Guid acceptedId && acceptedId != rateId)
                    return Results.Conflict(new { code = "Pricing.QuoteGroupAlreadyAccepted",
                        message = "Otra alternativa del grupo ya fue aceptada." });
            }
            if (!existingSelectedId.HasValue)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $@"UPDATE pricing.rate_quote_alternatives SET status = 'NotSelected'
                       WHERE parent_rate_id = {parentId.Value};", cancellationToken);
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $@"UPDATE pricing.rate_quote_alternatives SET status = 'Selected'
                       WHERE rate_id = {rateId} AND parent_rate_id = {parentId.Value};",
                    cancellationToken);
            }

            // Keep the immutable parent_rate_id and internal RateCode intact.
            // Only the chosen rate takes the unsuffixed commercial QUO number.
            var baseQuote = await db.Database.SqlQueryRaw<string>(
                    "SELECT base_quote AS \"Value\" FROM pricing.rate_quote_alternatives WHERE rate_id = {0}", rateId)
                .SingleAsync(cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"UPDATE pricing.""RateHeaders"" SET quo_number = {baseQuote} WHERE id = {rateId};",
                cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $@"UPDATE pricing.rate_quote_alternatives SET alternative_quote = {baseQuote}
                   WHERE rate_id = {rateId} AND status = 'Selected';", cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Ok(new { parentRateId = parentId.Value, selectedRateId = rateId, finalized = true });
        });
    }

    private static async Task<IResult> UpdateRateAsync(
        Guid rateId,
        UpdateRateRequest request,
        ICommandDispatcher dispatcher,
        ServiceDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        // Do not edit a discarded sub-QUO after final selection.
        var selectedPrincipal = await db.Database.SqlQueryRaw<Guid>(
                """
                SELECT chosen.rate_id AS "Value"
                FROM pricing.rate_quote_alternatives child
                JOIN pricing.rate_quote_alternatives chosen
                  ON chosen.parent_rate_id = child.parent_rate_id AND chosen.status = 'Selected'
                WHERE child.rate_id = {0}
                """, rateId).ToListAsync(cancellationToken);
        if (selectedPrincipal.Count > 0 && selectedPrincipal[0] != rateId)
            return Results.Conflict(new { code = "Pricing.QuoteAlternativeDiscarded",
                message = "Esta sub-QUO se descartó; solo la principal puede editarse." });

        if (request.ClientAccountId.HasValue && request.ClientLeadId.HasValue)
            return Results.BadRequest(new { message = "Seleccione una cuenta o un prospecto, no ambos." });
        var termValidation = ValidateExclusiveRateTerms(request.Includes, request.SubjectTo, request.Excludes, httpContext);
        if (termValidation is not null) return termValidation;

        // A seller amendment is bound to one open work item. A later retry of
        // the same completed request must not create another quote revision.
        if (request.ModificationRequestId is { } modificationRequestId)
        {
            var modification = await db.RateRequests.AsNoTracking()
                .Where(x => x.Id == modificationRequestId)
                .Select(x => new { x.RateId, x.Status, x.PayloadJson })
                .FirstOrDefaultAsync(cancellationToken);
            if (modification is null || modification.RateId != rateId
                || !RateModificationRequestPayload.IsModification(modification.PayloadJson)
                || modification.Status != RateRequestStatus.Open)
                return Results.Conflict(new {
                    code = "Pricing.ModificationAlreadyCompletedOrInvalid",
                    message = "Esta solicitud de modificación ya se completó o no pertenece a la cotización. Recargue la tarifa; no cree otra revisión."
                });
            if (request.ExpectedRevisionNumber is null)
                return Results.BadRequest(new {
                    code = "Pricing.ModificationExpectedRevisionRequired",
                    message = "La modificación requiere la revisión original para evitar guardados duplicados."
                });
        }

        var extraDetails = new List<UpsertRateExtraDetailCommandItem>();

        foreach (var detail in request.ExtraDetails)
        {
            if (!TryParseDefinedEnum(detail.CostDetailType, out CostDetailType costDetailType))
            {
                return EndpointResults.BadRequest(
                    "Pricing.InvalidCostDetailType",
                    $"El rubro '{detail.CostDetailType}' no es válido.",
                    httpContext
                );
            }

            if (!TryParseDefinedEnum(detail.CostType, out CostType costType))
            {
                return EndpointResults.BadRequest(
                    "Pricing.InvalidCostType",
                    $"El tipo '{detail.CostType}' no es válido.",
                    httpContext
                );
            }

            ChargeBasis? chargeBasis = null;
            if (!string.IsNullOrWhiteSpace(detail.ChargeBasis))
            {
                if (!TryParseDefinedEnum(detail.ChargeBasis, out ChargeBasis parsedChargeBasis))
                {
                    return EndpointResults.BadRequest(
                        "Pricing.InvalidChargeBasis",
                        $"La base de cobro '{detail.ChargeBasis}' no es válida.",
                        httpContext
                    );
                }
                chargeBasis = parsedChargeBasis;
            }

            extraDetails.Add(
                new UpsertRateExtraDetailCommandItem(
                    detail.Id,
                    detail.CostId,
                    detail.Name,
                    costDetailType,
                    costType,
                    detail.CurrencyId,
                    detail.CurrencyName,
                    detail.CurrencyCode,
                    detail.CostAmount,
                    detail.SaleAmount,
                    detail.Notes,
                    detail.Quantity,
                    chargeBasis,
                    detail.ApplyDestinationTax,
                    detail.DestinationTaxRate,
                    detail.BillToClient
                )
            );
        }

        if (!TryParseDefinedEnum(request.ShipmentMode, out ShipmentMode shipmentMode))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidShipmentMode",
                $"La modalidad '{request.ShipmentMode}' no es válida.",
                httpContext
            );
        }

        if (!TryParseDefinedEnum(request.OperationType, out RateOperationType operationType))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidOperationType",
                $"El tipo de operación '{request.OperationType}' no es válido.",
                httpContext
            );
        }

        if (!TryParseDefinedEnum(request.RateType, out RateType rateType))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidRateType",
                $"El tipo de tarifa '{request.RateType}' no es válido.",
                httpContext
            );
        }

        var cargoLines = (request.CargoLines ?? Array.Empty<RateCargoLineRequest>())
            .Select(x => new Dhole.Pricing.Application.Features.Rates.RateCargoLineCommandItem(
                x.Description, x.Packages, x.Pallets, x.WeightKg, x.LengthCm, x.WidthCm, x.HeightCm, x.IsStackable ?? true, x.UnitType))
            .ToArray();

        var containers = (request.Containers ?? Array.Empty<RateContainerRequest>())
            .Select(x => new UpdateRateContainerCommandItem(
                x.ContainerTypeId,
                x.ContainerTypeName,
                x.ContainerTypeCode,
                x.Quantity,
                x.UnitWeightsKg
            ))
            .ToList();

        if (containers.Count == 0)
        {
            var synthetic = SyntheticEquipment(shipmentMode, request.ContainerTypeId, request.ContainerTypeName, request.ContainerTypeCode);
            containers.Add(new UpdateRateContainerCommandItem(
                synthetic.Id, synthetic.Name, synthetic.Code, Math.Max(request.ContainerQuantity, 1)));
        }

        var canApproveLowMargin = HasScope(
            httpContext.User,
            PricingConstants.Scopes.RateApproveLowMargin
        );
        var freeDays = await ResolveConfiguredFreeDaysAsync(
            db,
            request.CarrierId,
            request.FreeDays,
            cancellationToken
        );

        var result = await dispatcher.DispatchAsync(
            new UpdateRateCommand(
                rateId,
                request.AgentId ?? Guid.Empty,
                request.AgentName ?? string.Empty,
                request.AgentCode ?? string.Empty,
                request.CarrierId ?? Guid.Empty,
                request.CarrierName ?? string.Empty,
                request.CarrierCode ?? string.Empty,
                request.PolId,
                request.PolName,
                request.PolCode,
                request.PoeId,
                request.PoeName,
                request.PoeCode,
                request.PodId,
                request.PodName,
                request.PodCode,
                request.ContainerTypeId,
                request.ContainerTypeName,
                request.ContainerTypeCode,
                request.IncotermId,
                request.IncotermName,
                request.IncotermCode,
                request.CurrencyId,
                request.CurrencyName,
                request.CurrencyCode,
                freeDays,
                request.ValidFrom,
                request.ValidTo,
                request.ContainerQuantity,
                request.ClientName,
                request.ExecutiveName,
                request.IdtraNumber,
                request.QuoNumber,
                request.Includes,
                request.SubjectTo,
                request.Excludes,
                request.TransitTime,
                extraDetails,
                request.RemovedExtraDetailIds,
                containers,
                rateType,
                shipmentMode,
                request.KgPerCbm,
                request.TotalPackages,
                request.TotalPallets,
                request.TotalWeightKg,
                request.TotalVolumeCbm,
                cargoLines,
                request.WarehouseId,
                request.PickupAddress,
                request.PickupLatitude,
                request.PickupLongitude,
                canApproveLowMargin,
                operationType,
                (request.Services ?? [])
                    .Select(x => new RateServiceSelection(x.Id, x.Name, x.Code))
                    .ToArray(),
                request.ExchangeRatePurchase,
                request.ExchangeRateSale,
                request.ExchangeRateApplied,
                request.UseAllInPresentation,
                httpContext.GetCurrentUserId()
            )
            {
                FinalBackupStorageIds = request.FinalBackupStorageIds,
                ClientAccountId = request.ClientAccountId,
                ClientLeadId = request.ClientLeadId,
                OtherReferences = request.OtherReferences,
                LclGroupAgentId = request.LclGroupAgentId,
                SourceImportFclRateId = request.SourceImportFclRateId,
                UpdateReason = request.UpdateReason,
                ReplaceExtraDetails = request.ReplaceExtraDetails,
                ExpectedRevisionNumber = request.ExpectedRevisionNumber,
                ModificationRequestId = request.ModificationRequestId,
                PickupLocationsJson = SerializePickupLocations(request.PickupLocations),
                InlandServiceLocationsJson = request.InlandPickupLocation is null && request.InlandDeliveryLocation is null
                    ? null
                    : System.Text.Json.JsonSerializer.Serialize(new
                    {
                        pickup = request.InlandPickupLocation,
                        delivery = request.InlandDeliveryLocation
                    }),
                StorageDays = request.StorageDays,
                CargoValue = request.CargoValue,
                TransitDays = request.TransitDays,
                CarrierRoute = request.CarrierRoute,
                CarrierComments = request.CarrierComments,
            },
            cancellationToken
        );

        // A successful amendment save is the authoritative completion of Pricing's
        // work item. PDF / email delivery is handled separately by the durable
        // retry worker, so a failed delivery never leaves completed work on /open.
        if (result.IsSuccess && request.ModificationRequestId is { } savedModificationId)
        {
            var savedRevision = await db.RateHeaders.AsNoTracking()
                .Where(x => x.Id == rateId && !x.IsDeleted)
                .Select(x => (int?)x.RevisionNumber)
                .FirstOrDefaultAsync(cancellationToken);
            var sellerRequest = await db.RateRequests
                .FirstOrDefaultAsync(x => x.Id == savedModificationId, cancellationToken);
            var sourceRevision = RateModificationRequestPayload.SourceRevisionNumber(sellerRequest?.PayloadJson);

            if (sellerRequest is null || sellerRequest.RateId != rateId
                || !RateModificationRequestPayload.IsModification(sellerRequest.PayloadJson)
                || savedRevision is null
                || savedRevision != request.ExpectedRevisionNumber.GetValueOrDefault() + 1
                || savedRevision <= sourceRevision)
                return Results.Conflict(new
                {
                    code = "Pricing.RateModificationCompletionReconciliationRequired",
                    message = "La QUO fue guardada, pero no se pudo verificar la revisión para cerrar la solicitud. No vuelva a guardar la tarifa."
                });

            if (sellerRequest.Status == RateRequestStatus.Open)
            {
                sellerRequest.PrepareModification(savedRevision.Value);
                sellerRequest.MarkSellerModificationReadyForDelivery(DateTime.UtcNow);
                await db.SaveChangesAsync(cancellationToken);
            }
            else if (sellerRequest.Status != RateRequestStatus.Completed
                || sellerRequest.PreparedRevisionNumber != savedRevision)
                return Results.Conflict(new
                {
                    code = "Pricing.RateModificationCompletionConflict",
                    message = "La revisión fue guardada, pero la solicitud cambió de estado. Compruebe el historial antes de continuar."
                });
        }

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> DuplicateRateAsync(
        Guid rateId,
        DuplicateRateRequest request,
        ServiceDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var duplicateCommand = new DuplicateRateCommand(
                rateId,
                request.ValidFrom,
                request.ValidTo,
                httpContext.GetCurrentUserId(),
                request.ApplyTariff,
                request.ClientName,
                request.ExecutiveName,
                request.IdtraNumber,
                request.IdenticalCopy
            );

        var result = await DispatchWithTransactionalRateCodeAsync(
            db,
            httpContext,
            retryDispatcher => retryDispatcher.DispatchAsync(duplicateCommand, cancellationToken)
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> ApproveRateMarginAsync(
        Guid rateId,
        ICommandDispatcher dispatcher,
        LowMarginApprovedNotificationService lowMarginApprovedNotificationService,
        ServiceDbContext db,
        IIntegrationEventOutboxWriter outbox,
        ILoggerFactory loggerFactory,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var approvedBy = httpContext.GetCurrentUserId();
        var result = await dispatcher.DispatchAsync(
            new ApproveRateMarginCommand(rateId, approvedBy),
            cancellationToken
        );

        if (result.IsSuccess)
        {
            await lowMarginApprovedNotificationService.QueueAsync(
                rateId,
                approvedBy,
                cancellationToken
            );

            // Creation already linked the pending-approval quote to its seller request.
            // Once approved, send the PDF and close the request without needing an
            // operator to reclaim the lease or manually mark the request completed.
            await RateRequestCompletionEndpoints.TryCompleteApprovedRateAsync(
                rateId, db, dispatcher, outbox, httpContext, loggerFactory, cancellationToken
            );
        }

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> RejectRateMarginAsync(
        Guid rateId,
        RejectRateMarginRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new RejectRateMarginCommand(rateId, request.Reason, httpContext.GetCurrentUserId()),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> ResendRateRevisionAsync(
        Guid rateId,
        ResendRateRevisionRequest request,
        SentRateEmailNotificationService sentRateEmailNotificationService,
        ServiceDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var rate = await db.RateHeaders.AsNoTracking()
            .Where(x => x.Id == rateId && !x.IsDeleted)
            .Select(x => new
            {
                x.RevisionNumber,
                x.Status,
                x.PendingReapproval,
                x.RequiredApproval,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (rate is null)
            return Results.NotFound();

        if (request.RevisionNumber != rate.RevisionNumber)
            return Results.Conflict(new
            {
                code = "Pricing.RateRevisionChanged",
                message = "La tarifa cambió de revisión. Recargue antes de reenviarla.",
            });

        if (rate.Status != RateStatus.Sent || !rate.PendingReapproval || rate.RequiredApproval)
            return EndpointResults.BadRequest(
                "Pricing.RateNotEligibleForResend",
                "Solo se puede reenviar una revisión ya enviada que esté pendiente de reaprobación y no tenga aprobación de margen pendiente.",
                httpContext);

        // Cada clic explícito representa un envío nuevo. La clave de idempotencia
        // HTTP evita duplicarlo ante reintentos del mismo POST.
        try
        {
            var error = await sentRateEmailNotificationService.QueueBeforeStatusTransitionAsync(
                rateId,
                cancellationToken,
                resendCorrelationId: $"pricing-rate-resend:{rateId:N}:rev:{rate.RevisionNumber}:attempt:{Guid.NewGuid():N}",
                deliveryTarget: request.DeliveryTarget,
                contactId: request.ContactId,
                authorization: httpContext.Request.Headers["Authorization"].ToString(),
                requireAccountVerification: request.DeliveryTarget is not null
            );

            if (error is not null)
                return EndpointResults.BadRequest(
                    "Pricing.RateSentEmailNotQueued", error, httpContext);

            // El reenvío no cambia el estado; persistir el Outbox explícitamente.
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Results.Problem(
                title: "No se pudo preparar el reenvío al vendedor",
                detail: "No se encoló un nuevo correo. Intente nuevamente.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<IResult> SetRateStatusAsync(
        Guid rateId,
        SetRateStatusRequest request,
        ICommandDispatcher dispatcher,
        AcceptedRateOpeningsNotificationService openingsNotificationService,
        SentRateEmailNotificationService sentRateEmailNotificationService,
        ServiceDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (!TryParseDefinedEnum(request.Status, out RateStatus status)
            || status is not (
                RateStatus.Open
                or RateStatus.Sent
                or RateStatus.RequestedByClient
                or RateStatus.AcceptedByClient
                or RateStatus.RejectedByClient
                or RateStatus.Closed
                or RateStatus.Cancelled
                or RateStatus.QUOVinculada
            ))
        {
            return EndpointResults.BadRequest(
                "Pricing.InvalidRateStatus",
                "El estado comercial de la tarifa no es válido.",
                httpContext
            );
        }

        var canUpdateRateStatus = HasScope(
            httpContext.User,
            PricingConstants.Scopes.RateUpdate
        );
        var canApproveLowMargin = HasScope(
            httpContext.User,
            PricingConstants.Scopes.RateApproveLowMargin
        );
        var isClientDecision = status is
            RateStatus.AcceptedByClient or RateStatus.RejectedByClient or RateStatus.QUOVinculada;
        // A commercial account manager may send a linked quote without unrestricted
        // permission to edit pricing. CRM verifies account visibility at dispatch.
        var canSendFromAccount = status == RateStatus.Sent
            && HasScope(httpContext.User, "crm.account.update")
            && HasScope(httpContext.User, "crm.quote.read");

        // El permiso de aprobación de margen bajo también habilita registrar la
        // decisión del cliente directamente desde una tarifa Abierta. El resto
        // de transiciones comerciales sigue requiriendo pricing.rate.update.
        if (!canUpdateRateStatus && !canSendFromAccount && !(isClientDecision && canApproveLowMargin))
        {
            return Results.Forbid();
        }


        // Discarded alternatives cannot be sent, accepted, rejected or reopened.
        var groupedSelection = await db.Database.SqlQueryRaw<Guid>(
                """
                SELECT chosen.rate_id AS "Value"
                FROM pricing.rate_quote_alternatives child
                JOIN pricing.rate_quote_alternatives chosen
                  ON chosen.parent_rate_id = child.parent_rate_id AND chosen.status = 'Selected'
                WHERE child.rate_id = {0}
                """, rateId).ToListAsync(cancellationToken);
        if (groupedSelection.Count > 0 && groupedSelection[0] != rateId)
            return Results.Conflict(new { code = "Pricing.QuoteAlternativeDiscarded",
                message = "Esta sub-QUO se descartó; solo la principal puede utilizarse." });

        // A grouped alternative cannot reach Dynamics or commercial acceptance
        // until the user has explicitly selected it in the quotation group.
        if (status == RateStatus.AcceptedByClient)
        {
            var connection = db.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;
            if (shouldClose) await connection.OpenAsync(cancellationToken);
            try
            {
                await using var check = connection.CreateCommand();
                check.CommandText = """
                    SELECT status FROM pricing.rate_quote_alternatives
                    WHERE rate_id = @rate_id LIMIT 1;
                    """;
                var parameter = check.CreateParameter();
                parameter.ParameterName = "@rate_id";
                parameter.Value = rateId;
                check.Parameters.Add(parameter);
                var selected = await check.ExecuteScalarAsync(cancellationToken);
                if (selected is string groupStatus &&
                    !string.Equals(groupStatus, "Selected", StringComparison.Ordinal))
                    return Results.Conflict(new { code = "Pricing.QuoteAlternativeNotSelected",
                        message = "Seleccione esta alternativa dentro del grupo antes de aceptarla." });
            }
            finally
            {
                if (shouldClose) await connection.CloseAsync();
            }
        }

        if (status == RateStatus.AcceptedByClient && request.Observations?.Trim().Length > 2000)
            return EndpointResults.BadRequest(
                "Pricing.AcceptanceObservationsTooLong",
                "Las observaciones de apertura no pueden superar 2000 caracteres.",
                httpContext);

        // Preparar el PDF y el correo en Outbox antes de persistir el cambio
        // de estado. El handler confirma ambos en la misma transacción.
        if (status == RateStatus.Sent)
        {
            var previousStatus = await db.RateHeaders
                .AsNoTracking()
                .Where(x => x.Id == rateId && !x.IsDeleted)
                .Select(x => (RateStatus?)x.Status)
                .FirstOrDefaultAsync(cancellationToken);

            // A seller who is authorised only for CRM account delivery must
            // not use an idempotent Sent->Sent status call as a hidden mutation.
            if (canSendFromAccount && !canUpdateRateStatus &&
                (!previousStatus.HasValue || previousStatus.Value == RateStatus.Sent))
                return Results.Conflict(new
                {
                    code = "Pricing.QuoteDeliveryAlreadySent",
                    message = "La cotización ya fue enviada o no está disponible para este envío.",
                });

            if (previousStatus.HasValue && previousStatus.Value != RateStatus.Sent)
            {
                string? emailError;
                try
                {
                    emailError = await sentRateEmailNotificationService
                        .QueueBeforeStatusTransitionAsync(
                            rateId, cancellationToken,
                            deliveryTarget: request.DeliveryTarget,
                            contactId: request.ContactId,
                            authorization: httpContext.Request.Headers["Authorization"].ToString(),
                            requireAccountVerification: canSendFromAccount || request.DeliveryTarget is not null);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    return Results.Problem(
                        title: "No se pudo preparar el correo al vendedor",
                        detail: "No se cambió el estado de la tarifa. Intente nuevamente.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                if (emailError is not null)
                    return EndpointResults.BadRequest(
                        "Pricing.RateSentEmailNotQueued", emailError, httpContext);
            }
        }

        var acceptanceObservations = status == RateStatus.AcceptedByClient
            ? request.Observations?.Trim() : null;

        var result = await dispatcher.DispatchAsync(
            new SetRateStatusCommand(
                rateId,
                status,
                request.Reason,
                request.IdtraNumber,
                httpContext.GetCurrentUserId(),
                canApproveLowMargin,
                request.OtherReferences,
                acceptanceObservations,
                request.LinkedAcceptedRateId
            ),
            cancellationToken
        );

        if (result.IsSuccess && status == RateStatus.AcceptedByClient)
        {
            var emailQueued = await openingsNotificationService.QueueAsync(
                rateId, RateAcceptanceActor.Pricing, cancellationToken, acceptanceObservations);
            if (!emailQueued)
            {
                // La aceptación ya quedó persistida: responder 202 y no fingir que falló.
                return Results.Json(new
                {
                    code = "Pricing.AcceptanceSavedEmailPending",
                    message = "La tarifa quedó aceptada, pero el correo de Apertura no se pudo preparar. Reintente la notificación.",
                    accepted = true,
                    notificationQueued = false,
                    rateId
                }, statusCode: StatusCodes.Status202Accepted);
            }
        }

        return EndpointResults.FromResult(result, httpContext);
    }

    private static async Task<IResult> RetryAcceptedRateEmailAsync(
        Guid rateId,
        ServiceDbContext db,
        AcceptedRateOpeningsNotificationService openingsNotificationService,
        CancellationToken cancellationToken)
    {
        var rate = await db.RateHeaders
            .AsNoTracking()
            .Where(x => x.Id == rateId && !x.IsDeleted)
            .Select(x => new { x.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (rate is null)
            return Results.NotFound();

        if (rate.Status != RateStatus.AcceptedByClient)
            return Results.Conflict(new
            {
                code = "Pricing.AcceptanceEmailRequiresAcceptedQuote",
                message = "Solamente se puede reintentar el correo de una cotización aceptada."
            });

        // No cambia la aceptación, revisión ni IDTRA. El Outbox deduplica por
        // correlación de tarifa/revisión y el mismo actor de la ruta Pricing.
        var queued = await openingsNotificationService.QueueDetailedAsync(
            rateId, RateAcceptanceActor.Pricing, cancellationToken);
        if (!queued.Queued)
            return Results.Problem(
                title: "No se pudo preparar el correo de aceptación",
                detail: queued.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?> { ["code"] = queued.Code });

        return Results.Accepted(value: new
        {
            code = "Pricing.AcceptanceEmailQueued",
            message = "Correo de aceptación preparado en Outbox. Notifications gestionará la entrega.",
            rateId
        });
    }

    private sealed record ResendAcceptedRateEmailRequest(Guid RequestId, string? NotificationType = null);

    // Every resend uses the same RequestId for HTTP/outbox idempotency. The
    // selected notification is validated server-side; clients cannot override
    // arbitrary recipients or addresses.
    private static async Task<IResult> ResendAcceptedRateEmailAsync(
        Guid rateId,
        ResendAcceptedRateEmailRequest request,
        ServiceDbContext db,
        AcceptedRateOpeningsNotificationService openingsNotificationService,
        SentRateEmailNotificationService sentRateEmailNotificationService,
        HttpContext httpContext,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (request.RequestId == Guid.Empty)
            return Results.BadRequest(new
            {
                code = "Pricing.AcceptanceEmailResendIdRequired",
                message = "La operación de reenvío necesita un identificador válido."
            });

        var selectedType = AcceptedRateResendPolicy.NormalizeNotificationType(request.NotificationType);
        if (!AcceptedRateResendPolicy.IsSupportedNotificationType(selectedType))
            return Results.BadRequest(new
            {
                code = "Pricing.AcceptanceEmailNotificationTypeInvalid",
                message = "Elija una notificación válida: apertura, cotización enviada o aviso sin PDF."
            });

        var rate = await db.RateHeaders.AsNoTracking()
            .Where(x => x.Id == rateId && !x.IsDeleted)
            .Select(x => new { x.Status, x.RevisionNumber, x.QuoNumber, x.RateCode })
            .FirstOrDefaultAsync(cancellationToken);
        if (rate is null) return Results.NotFound();

        if (!AcceptedRateResendPolicy.CanResend(rate.Status))
            return Results.Conflict(new
            {
                code = "Pricing.AcceptanceEmailRequiresAcceptedQuote",
                message = "Solo se puede utilizar este reenvío con una cotización aceptada."
            });

        var correlationId =
            $"pricing-manual-notification:{selectedType}:{rateId:N}:rev:{rate.RevisionNumber}:request:{request.RequestId:N}";

        if (selectedType == "quote")
        {
            try
            {
                // Reusar el mismo sistema de preferencias de destinatarios CRM
                // que utiliza el envío normal, sin aceptar correos arbitrarios.
                var error = await sentRateEmailNotificationService.QueueBeforeStatusTransitionAsync(
                    rateId, cancellationToken,
                    resendCorrelationId: correlationId,
                    authorization: httpContext.Request.Headers["Authorization"].ToString());

                if (error is not null)
                    return Results.UnprocessableEntity(new
                    {
                        code = "Pricing.QuoteResendNotQueued",
                        message = error
                    });

                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                loggerFactory.CreateLogger(nameof(RateEndpoints)).LogError(exception,
                    "Falló reenvío de cotización aceptada. RateId={RateId} RequestId={RequestId}",
                    rateId, request.RequestId);
                return Results.Problem(
                    title: "Fallo al registrar el correo de cotización",
                    detail: "La QUO sigue aceptada. No se confirmó el guardado en Outbox; verifique la cola antes de un nuevo reenvío.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    extensions: new Dictionary<string, object?> {
                        ["code"] = "Pricing.QuoteResendOutboxUnavailable",
                        ["traceId"] = httpContext.TraceIdentifier,
                    });
            }

            return Results.Accepted(value: new
            {
                code = "Pricing.QuoteResendQueued",
                message = "Cotización encolada conforme a los destinatarios comerciales configurados en CRM.",
                notificationType = selectedType,
                rateId,
                requestId = request.RequestId
            });
        }

        var queued = await openingsNotificationService.QueueDetailedAsync(
            rateId, RateAcceptanceActor.Pricing, cancellationToken,
            manualResendCorrelationId: correlationId,
            includePdf: selectedType == "opening");

        if (!queued.Queued)
            return Results.Problem(
                title: "No se pudo preparar el correo",
                detail: queued.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?> {
                    ["code"] = queued.Code,
                    ["traceId"] = httpContext.TraceIdentifier,
                });

        return Results.Accepted(value: new
        {
            code = queued.Code,
            message = queued.Message,
            notificationType = selectedType,
            rateId,
            requestId = request.RequestId
        });
    }

    private static async Task<IResult> DeleteRatesAsync(
        [FromBody] DeleteRateBatchRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var result = await dispatcher.DispatchAsync(
            new DeleteRateCommand(request.Ids, httpContext.GetCurrentUserId()),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    private static string? SerializePickupLocations(
        IReadOnlyCollection<RatePickupLocationRequest>? pickupLocations
    )
    {
        if (pickupLocations is null) return null;

        var normalized = pickupLocations
            .Where(x => !string.IsNullOrWhiteSpace(x.Address))
            .Take(50)
            .Select(x => new RatePickupLocationRequest(
                x.Address.Trim(),
                x.Latitude,
                x.Longitude,
                x.CargoCondition is "FiscalCargo" or "NationalizedCargo"
                    ? x.CargoCondition
                    : null
            ))
            .ToArray();

        return normalized.Length == 0
            ? null
            : System.Text.Json.JsonSerializer.Serialize(normalized);
    }

    private static bool HasScope(ClaimsPrincipal user, string requiredScope)
    {
        return user
            .Claims.Where(claim => claim.Type is "scope" or "scp")
            .SelectMany(claim =>
                claim.Value.Split(
                    [' ', ','],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            )
            .Any(scope => string.Equals(scope, requiredScope, StringComparison.OrdinalIgnoreCase));
    }

    private static (Guid Id, string Name, string Code) SyntheticEquipment(
        ShipmentMode mode, Guid requestedId, string requestedName, string requestedCode)
    {
        if (requestedId != Guid.Empty && !string.IsNullOrWhiteSpace(requestedName) && !string.IsNullOrWhiteSpace(requestedCode))
            return (requestedId, requestedName, requestedCode);

        return mode switch
        {
            ShipmentMode.Lcl => (Guid.Parse("00000000-0000-4000-8000-0000000000C1"), "Carga consolidada LCL", "LCL"),
            ShipmentMode.Ftl => (Guid.Parse("00000000-0000-4000-8000-0000000000F1"), "Camión completo FTL", "FTL"),
            ShipmentMode.Ltl => (Guid.Parse("00000000-0000-4000-8000-0000000000D1"), "Carga consolidada LTL", "LTL"),
            _ => (requestedId, requestedName, requestedCode),
        };
    }

    private static bool TryParseDefinedEnum<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        return Enum.TryParse(value, ignoreCase: true, out result) && Enum.IsDefined(result);
    }
    private static IResult? ValidateExclusiveRateTerms(
        string? includes,
        string? subjectTo,
        string? excludes,
        HttpContext httpContext
    )
    {
        static HashSet<string> Lines(string? value) =>
            (value ?? string.Empty)
                .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x =>
                {
                    var normalized = System.Text.RegularExpressions.Regex.Replace(
                        x.ToUpperInvariant(), @"[^\p{L}\p{N}]+", " "
                    ).Trim();
                    var qualifier = System.Text.RegularExpressions.Regex.Match(
                        normalized, @"\s(?:USD|EUR|CRC|IVI|IVA|ITBMS|\d)"
                    );
                    return qualifier.Success && qualifier.Index > 0
                        ? normalized[..qualifier.Index].Trim()
                        : normalized;
                })
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Repetir accidentalmente una línea dentro de la misma categoría no viola
        // exclusividad. El error aplica únicamente si el mismo ítem aparece en
        // categorías distintas: Incluye / Sujeto a / No incluye.
        var categories = new[] { Lines(includes), Lines(subjectTo), Lines(excludes) };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in categories)
        {
            foreach (var item in category)
            {
                if (seen.Contains(item))
                {
                    return EndpointResults.BadRequest(
                        "Pricing.RateTermItemDuplicated",
                        "Un ítem de tarifa solo puede pertenecer a una categoría de la cotización.",
                        httpContext
                    );
                }
            }

            seen.UnionWith(category);
        }
        return null;
    }

    private static async Task<int> ResolveConfiguredFreeDaysAsync(
        ServiceDbContext db,
        Guid? carrierId,
        int fallback,
        CancellationToken cancellationToken
    )
    {
        if (!carrierId.HasValue || carrierId.Value == Guid.Empty) return Math.Max(0, fallback);

        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT free_days
                FROM pricing."CarrierFreeDayRules"
                WHERE carrier_id = @carrier_id AND is_active = TRUE
                LIMIT 1;
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@carrier_id";
            parameter.Value = carrierId.Value;
            command.Parameters.Add(parameter);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is null or DBNull ? Math.Max(0, fallback) : Math.Max(0, Convert.ToInt32(value));
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

}
