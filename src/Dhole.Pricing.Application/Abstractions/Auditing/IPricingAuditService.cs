namespace Dhole.Pricing.Application.Abstractions.Auditing;

public interface IPricingAuditService
{
    Task PublishAsync(PricingAuditEvent auditEvent, CancellationToken cancellationToken = default);
}
