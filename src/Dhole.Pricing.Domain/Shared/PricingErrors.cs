using CustomCodeFramework.Core.Results;

namespace Dhole.Pricing.Domain.Shared;

public static class PricingErrors
{
    public static readonly Error CostNotFound = new("Pricing.CostNotFound", "El costo no existe.");

    public static readonly Error CostNameIsRequired = new(
        "Pricing.CostNameIsRequired",
        "El nombre del costo es obligatorio."
    );

    public static readonly Error CostCarrierIsRequired = new(
        "Pricing.CostCarrierIsRequired",
        "La naviera del costo es obligatoria."
    );

    public static readonly Error CostCarrierSnapshotIsRequired = new(
        "Pricing.CostCarrierSnapshotIsRequired",
        "Los datos de la naviera son obligatorios."
    );

    public static readonly Error CostPortIsRequired = new(
        "Pricing.CostPortIsRequired",
        "El puerto del costo es obligatorio."
    );

    public static readonly Error CostPortSnapshotIsRequired = new(
        "Pricing.CostPortSnapshotIsRequired",
        "Los datos del puerto son obligatorios."
    );

    public static readonly Error CostCurrencyIsRequired = new(
        "Pricing.CostCurrencyIsRequired",
        "La moneda del costo es obligatoria."
    );

    public static readonly Error CostCurrencySnapshotIsRequired = new(
        "Pricing.CostCurrencySnapshotIsRequired",
        "Los datos de la moneda son obligatorios."
    );

    public static readonly Error CostAmountIsRequired = new(
        "Pricing.CostAmountIsRequired",
        "El monto del costo es obligatorio."
    );

    public static readonly Error CostAmountMustBeGreaterOrEqualThanZero = new(
        "Pricing.CostAmountMustBeGreaterOrEqualThanZero",
        "El monto del costo no puede ser negativo."
    );

    public static readonly Error CostAlreadyExists = new(
        "Pricing.CostAlreadyExists",
        "Ya existe un costo registrado para la misma naviera, puerto, nombre y moneda."
    );

    public static readonly Error CostIsInactive = new(
        "Pricing.CostIsInactive",
        "El costo está inactivo."
    );

    public static readonly Error InvalidCost = new(
        "Pricing.InvalidCost",
        "El costo no cumple las reglas requeridas. Revise si es fijo, variable, manual y sus catálogos vinculados."
    );

    public static Error InvalidConfigCatalogReference(string field, string catalogGroupSlug) => new(
        "Pricing.InvalidConfigCatalogReference",
        $"{field} no existe, está inactivo o no pertenece al catálogo '{catalogGroupSlug}' de Config."
    );

    public static readonly Error ConfigServiceUnavailable = new(
        "Pricing.ConfigServiceUnavailable",
        "No fue posible validar los catálogos con Config. Intente nuevamente cuando el servicio de configuración esté disponible."
    );

    public static readonly Error RateHeaderHasNoFclDetails = new(
        "Pricing.RateHeaderHasNoFclDetails",
        "No se puede activar la tarifa porque no tiene detalles FCL."
    );

    public static readonly Error MarginApprovalRequired = new(
        "Pricing.MarginApprovalRequired",
        "La tarifa quedó pendiente de autorización porque el margen es menor al mínimo permitido."
    );

    public static readonly Error ImportFclRateNotFound = new(
        "Pricing.ImportFclRateNotFound",
        "La tarifa FCL importada no existe."
    );

    public static readonly Error ImportFclRatePolIsRequired = new(
        "Pricing.ImportFclRatePolIsRequired",
        "El puerto origen de la tarifa FCL importada es obligatorio."
    );

    public static readonly Error ImportFclRatePodIsRequired = new(
        "Pricing.ImportFclRatePodIsRequired",
        "El puerto destino de la tarifa FCL importada es obligatorio."
    );

    public static readonly Error ImportFclRateCarrierIsRequired = new(
        "Pricing.ImportFclRateCarrierIsRequired",
        "La naviera de la tarifa FCL importada es obligatoria."
    );

    public static readonly Error ImportFclRateContainerTypeIsRequired = new(
        "Pricing.ImportFclRateContainerTypeIsRequired",
        "El tipo de contenedor de la tarifa FCL importada es obligatorio."
    );

    public static readonly Error ImportFclRateCurrencyIsRequired = new(
        "Pricing.ImportFclRateCurrencyIsRequired",
        "La moneda de la tarifa FCL importada es obligatoria."
    );

    public static readonly Error ImportFclRateAmountIsRequired = new(
        "Pricing.ImportFclRateAmountIsRequired",
        "El monto de la tarifa FCL importada es obligatorio."
    );

    public static readonly Error ImportFclRateAmountMustBeGreaterOrEqualThanZero = new(
        "Pricing.ImportFclRateAmountMustBeGreaterOrEqualThanZero",
        "El monto de la tarifa FCL importada no puede ser negativo."
    );

    public static readonly Error ImportFclRateFreeDaysMustBeGreaterOrEqualThanZero = new(
        "Pricing.ImportFclRateFreeDaysMustBeGreaterOrEqualThanZero",
        "Los días libres de la tarifa FCL importada no pueden ser negativos."
    );

    public static readonly Error ImportFclRateInvalidValidityRange = new(
        "Pricing.ImportFclRateInvalidValidityRange",
        "La fecha final de vigencia no puede ser menor a la fecha inicial."
    );

    public static readonly Error ImportFclRateInvalidStatus = new(
        "Pricing.ImportFclRateInvalidStatus",
        "El estado de la tarifa FCL importada no es válido para esta operación."
    );

    public static readonly Error ImportFclRateOutsideValidity = new(
        "Pricing.ImportFclRateOutsideValidity",
        "La tarifa FCL importada no está vigente para la fecha actual."
    );

    public static readonly Error ImportFclRateCatalogConcordanceRequired = new(
        "Pricing.ImportFclRateCatalogConcordanceRequired",
        "Debe corregir y validar perfil, POL, POE, POD, naviera, agente, contenedor y moneda contra Config antes de aprobar."
    );

    public static readonly Error ImportFclRatePoeAssignmentRequired = new(
        "Pricing.ImportFclRatePoeAssignmentRequired",
        "El POE no ha sido asignado. Seleccione manualmente un POE válido de Config antes de aprobar."
    );

    public static readonly Error ImportFclRateRejectReasonIsRequired = new(
        "Pricing.ImportFclRateRejectReasonIsRequired",
        "Debe indicar el motivo de rechazo de la tarifa FCL importada."
    );

    public static readonly Error ImportFclRateAlreadyCreatedAsRate = new(
        "Pricing.ImportFclRateAlreadyCreatedAsRate",
        "La tarifa FCL importada ya fue convertida en tarifa oficial."
    );

    public static readonly Error RateHeaderNotFound = new(
        "Pricing.RateHeaderNotFound",
        "La tarifa no existe."
    );

    public static readonly Error RateTypeIsRequired = new(
        "Pricing.RateTypeIsRequired",
        "El tipo de tarifa es obligatorio."
    );

    public static readonly Error RateCurrencyIsRequired = new(
        "Pricing.RateCurrencyIsRequired",
        "La moneda de la tarifa es obligatoria."
    );

    public static readonly Error RateCurrencySnapshotIsRequired = new(
        "Pricing.RateCurrencySnapshotIsRequired",
        "Los datos de la moneda de la tarifa son obligatorios."
    );

    public static readonly Error ExchangeRateUnavailable = new(
        "Pricing.ExchangeRateUnavailable",
        "No fue posible obtener el tipo de cambio de Hacienda. Puede ingresar manualmente un tipo de cambio aplicado mayor que cero para continuar."
    );

    public static readonly Error RateInvalidValidityRange = new(
        "Pricing.RateInvalidValidityRange",
        "La fecha final de vigencia no puede ser menor a la fecha inicial."
    );

    public static readonly Error RateInvalidStatus = new(
        "Pricing.RateInvalidStatus",
        "El estado de la tarifa no es válido para esta operación."
    );

    public static readonly Error RateImportedStructureLocked = new(
        "Pricing.RateImportedStructureLocked",
        "Una tarifa creada desde una importación FCL debe conservar la modalidad y el tipo de contenedor de la tarifa importada."
    );

    public static readonly Error RateInvalidIncoterm = new(
        "Pricing.RateInvalidIncoterm",
        "El Incoterm seleccionado no existe, está inactivo o no pertenece al catálogo de Incoterms."
    );

    public static readonly Error RateInvalidFreightDistribution = new(
        "Pricing.RateInvalidFreightDistribution",
        "La distribución del flete no coincide con los contenedores de la tarifa. Revise tipo de contenedor y cantidad."
    );

    public static readonly Error RateInvalidDetailQuantity = new(
        "Pricing.RateInvalidDetailQuantity",
        "La cantidad de un detalle de tarifa no puede ser negativa."
    );

    public static Error RateUpdateValidationFailed(string? message) => new(
        "Pricing.RateUpdateValidationFailed",
        string.IsNullOrWhiteSpace(message)
            ? "No se pudo actualizar la tarifa porque uno de sus datos no es válido."
            : message.Trim()
    );

    public static readonly Error RateClosureReasonIsRequired = new(
        "Pricing.RateClosureReasonIsRequired",
        "Debe indicar el motivo por el que se cerró la tarifa."
    );

    public static readonly Error RateCannotBeActivatedWithoutFclDetails = new(
        "Pricing.RateCannotBeActivatedWithoutFclDetails",
        "No se puede activar la tarifa porque no tiene detalles FCL."
    );

    public static readonly Error RateTotalCostAmountMustBeGreaterOrEqualThanZero = new(
        "Pricing.RateTotalCostAmountMustBeGreaterOrEqualThanZero",
        "El costo total de la tarifa no puede ser negativo."
    );

    public static readonly Error RateSaleAmountMustBeGreaterOrEqualThanZero = new(
        "Pricing.RateSaleAmountMustBeGreaterOrEqualThanZero",
        "El monto de venta de la tarifa no puede ser negativo."
    );

    public static readonly Error RateLowMarginRequiresApproval = new(
        "Pricing.RateLowMarginRequiresApproval",
        "La tarifa requiere aprobación porque el margen es menor al mínimo permitido."
    );

    public static readonly Error MarginApprovalNotFound = new(
        "Pricing.MarginApprovalNotFound",
        "La aprobación de margen no existe."
    );

    public static readonly Error FclRateDetailNotFound = new(
        "Pricing.FclRateDetailNotFound",
        "El detalle FCL de la tarifa no existe."
    );

    public static readonly Error RateCostDetailFixedLocked = new(
        "Pricing.RateCostDetailFixedLocked",
        "Los costos fijos automáticos no se pueden modificar ni eliminar desde la tarifa."
    );

    public static readonly Error FclRateDetailCarrierIsRequired = new(
        "Pricing.FclRateDetailCarrierIsRequired",
        "La naviera del detalle FCL es obligatoria."
    );

    public static readonly Error FclRateDetailCarrierSnapshotIsRequired = new(
        "Pricing.FclRateDetailCarrierSnapshotIsRequired",
        "Los datos de la naviera del detalle FCL son obligatorios."
    );

    public static readonly Error FclRateDetailOriginPortIsRequired = new(
        "Pricing.FclRateDetailOriginPortIsRequired",
        "El puerto origen del detalle FCL es obligatorio."
    );

    public static readonly Error FclRateDetailOriginPortSnapshotIsRequired = new(
        "Pricing.FclRateDetailOriginPortSnapshotIsRequired",
        "Los datos del puerto origen del detalle FCL son obligatorios."
    );

    public static readonly Error FclRateDetailDestinationPortIsRequired = new(
        "Pricing.FclRateDetailDestinationPortIsRequired",
        "El puerto destino del detalle FCL es obligatorio."
    );

    public static readonly Error FclRateDetailDestinationPortSnapshotIsRequired = new(
        "Pricing.FclRateDetailDestinationPortSnapshotIsRequired",
        "Los datos del puerto destino del detalle FCL son obligatorios."
    );

    public static readonly Error FclRateDetailContainerTypeIsRequired = new(
        "Pricing.FclRateDetailContainerTypeIsRequired",
        "El tipo de contenedor del detalle FCL es obligatorio."
    );

    public static readonly Error FclRateDetailContainerTypeSnapshotIsRequired = new(
        "Pricing.FclRateDetailContainerTypeSnapshotIsRequired",
        "Los datos del tipo de contenedor del detalle FCL son obligatorios."
    );

    public static readonly Error FclRateDetailCurrencyIsRequired = new(
        "Pricing.FclRateDetailCurrencyIsRequired",
        "La moneda del detalle FCL es obligatoria."
    );

    public static readonly Error FclRateDetailCurrencySnapshotIsRequired = new(
        "Pricing.FclRateDetailCurrencySnapshotIsRequired",
        "Los datos de la moneda del detalle FCL son obligatorios."
    );

    public static readonly Error FclRateDetailAmountIsRequired = new(
        "Pricing.FclRateDetailAmountIsRequired",
        "El monto del detalle FCL es obligatorio."
    );

    public static readonly Error FclRateDetailAmountMustBeGreaterOrEqualThanZero = new(
        "Pricing.FclRateDetailAmountMustBeGreaterOrEqualThanZero",
        "El monto del detalle FCL no puede ser negativo."
    );

    public static readonly Error FclRateDetailFreeDaysMustBeGreaterOrEqualThanZero = new(
        "Pricing.FclRateDetailFreeDaysMustBeGreaterOrEqualThanZero",
        "Los días libres del detalle FCL no pueden ser negativos."
    );

    public static readonly Error FclRateDetailInvalidValidityRange = new(
        "Pricing.FclRateDetailInvalidValidityRange",
        "La fecha final de vigencia del detalle FCL no puede ser menor a la fecha inicial."
    );

    public static readonly Error RateCostDetailNotFound = new(
        "Pricing.RateCostDetailNotFound",
        "El detalle de costo de la tarifa no existe."
    );

    public static readonly Error RateCostDetailNameIsRequired = new(
        "Pricing.RateCostDetailNameIsRequired",
        "El nombre del detalle de costo es obligatorio."
    );

    public static readonly Error RateCostDetailTypeIsRequired = new(
        "Pricing.RateCostDetailTypeIsRequired",
        "El tipo de detalle de costo es obligatorio."
    );

    public static readonly Error RateCostDetailCurrencyIsRequired = new(
        "Pricing.RateCostDetailCurrencyIsRequired",
        "La moneda del detalle de costo es obligatoria."
    );

    public static readonly Error RateCostDetailCurrencySnapshotIsRequired = new(
        "Pricing.RateCostDetailCurrencySnapshotIsRequired",
        "Los datos de la moneda del detalle de costo son obligatorios."
    );

    public static readonly Error RateCostDetailAmountMustBeGreaterOrEqualThanZero = new(
        "Pricing.RateCostDetailAmountMustBeGreaterOrEqualThanZero",
        "El monto del detalle de costo no puede ser negativo."
    );

    public static readonly Error DataExtractionFailed = new(
        "Pricing.DataExtractionFailed",
        "No se pudo extraer la información del tarifario con Data Extraction."
    );

    public static readonly Error DataExtractionNoValidRows = new(
        "Pricing.DataExtractionNoValidRows",
        "Data Extraction no devolvió filas válidas para crear tarifas importadas."
    );

    public static readonly Error FclDecisionNotFound = new(
        "Pricing.FclDecisionNotFound",
        "La decisión tarifaria FCL no existe."
    );

    public static readonly Error FclDecisionCouldNotBeCreated = new(
        "Pricing.FclDecisionCouldNotBeCreated",
        "No se pudo crear la decisión tarifaria FCL."
    );

    public static readonly Error NoRatesForDecision = new(
        "Pricing.NoRatesForDecision",
        "No hay tarifas vigentes para los filtros indicados."
    );

    public static readonly Error InvalidImportFclRate = new(
        "Pricing.InvalidImportFclRate",
        "La importación no cumple con lo minimo requerido."
    );

    public static readonly Error ImportFclRateAlreadyUsed = new(
        "Pricing.ImportFclRateAlreadyUsed",
        "La tarifa importada ya esta utilizada."
    );

    public static readonly Error ImportFileIsEmpty = new(
        "Pricing.ImportFileIsEmpty",
        "El archivo de importación está vacío."
    );

    public static readonly Error ImportProfileRequired = new(
        "Pricing.ImportProfileRequired",
        "Debe seleccionar un perfil de importación de tarifas."
    );

    public static readonly Error UnsupportedReportFormat = new(
        "Pricing.Reports.UnsupportedFormat",
        "El formato solicitado no es soportado. Use pdf, xlsx o csv."
    );

    public static readonly Error ReportGenerationFailed = new(
        "Pricing.Reports.GenerationFailed",
        "Reports Service no pudo generar el documento de la tarifa."
    );

    public static readonly Error ReportGenerationTimedOut = new(
        "Pricing.Reports.GenerationTimedOut",
        "Reports Service superó el tiempo máximo para generar el documento."
    );
}
