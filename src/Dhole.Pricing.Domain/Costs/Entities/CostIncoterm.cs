namespace Dhole.Pricing.Domain.Costs.Entities;

public sealed class CostIncoterm
{
    private CostIncoterm() { }

    internal CostIncoterm(Guid costId, Guid incotermId, string incotermName, string incotermCode)
    {
        if (costId == Guid.Empty || incotermId == Guid.Empty)
            throw new InvalidOperationException("El costo y el Incoterm son obligatorios.");
        if (string.IsNullOrWhiteSpace(incotermName) || string.IsNullOrWhiteSpace(incotermCode))
            throw new InvalidOperationException("El Incoterm debe incluir nombre y código.");

        CostId = costId;
        IncotermId = incotermId;
        IncotermName = incotermName.Trim();
        IncotermCode = incotermCode.Trim();
    }


    internal void UpdateSnapshot(string incotermName, string incotermCode)
    {
        if (string.IsNullOrWhiteSpace(incotermName) || string.IsNullOrWhiteSpace(incotermCode))
            throw new InvalidOperationException("El Incoterm debe incluir nombre y código.");

        IncotermName = incotermName.Trim();
        IncotermCode = incotermCode.Trim();
    }

    public Guid CostId { get; private set; }
    public Guid IncotermId { get; private set; }
    public string IncotermName { get; private set; } = string.Empty;
    public string IncotermCode { get; private set; } = string.Empty;
}

public sealed record CostIncotermSelection(Guid Id, string Name, string Code);
