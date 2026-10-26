namespace Dhole.Pricing.Application.Abstractions.Services;

public interface IRateCodeGenerator
{
    // La reserva mantiene la transacción abierta hasta que se guarde la cotización.
    Task<IRateCodeReservation> ReserveAsync(CancellationToken cancellationToken = default);
}

public interface IRateCodeReservation : IAsyncDisposable
{
    string Code { get; }

    Task CommitAsync(CancellationToken cancellationToken = default);
}
