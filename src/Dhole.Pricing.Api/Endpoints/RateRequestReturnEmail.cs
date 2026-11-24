using System.Net;
using Dhole.Pricing.Domain.Rates.Entities;

namespace Dhole.Pricing.Api.Endpoints;

public sealed record RateRequestReturnEmailContent(string Subject, string SystemBody, string HtmlBody);

/// <summary>
/// Builds the seller-facing notification without displaying the internal rate-request UUID.
/// The UUID is used only in the destination URL and the outbox correlation metadata.
/// </summary>
public static class RateRequestReturnEmail
{
    public static RateRequestReturnEmailContent Compose(
        RateRequest request, string reason, string? sellerPortalBaseUrl)
    {
        var client = string.IsNullOrWhiteSpace(request.ClientName) ? "Cliente sin definir" : request.ClientName.Trim();
        var seller = string.IsNullOrWhiteSpace(request.SellerName)
            ? request.ExecutiveName?.Trim() ?? "equipo comercial"
            : request.SellerName.Trim();
        var route = string.Join(" → ", new[]
        {
            request.OriginName,
            request.PoeName,
            request.PodName,
            request.DestinationName,
        }.Where(value => !string.IsNullOrWhiteSpace(value))
         .Select(value => value!.Trim())
         .Distinct(StringComparer.OrdinalIgnoreCase));
        if (route.Length == 0) route = "Ruta por revisar";
        var mode = string.IsNullOrWhiteSpace(request.ShipmentMode)
            ? "Por revisar"
            : request.ShipmentMode.Trim().ToUpperInvariant();

        var subject = $"Acción requerida: tarifa devuelta por Pricing · {client}";
        var systemBody =
            $"Pricing devolvió la solicitud de tarifa de {client}. Ruta: {route}. "
            + $"Motivo: {reason}. Corregí los datos y reenviá la misma solicitud desde Ventas.";

        var safeSeller = WebUtility.HtmlEncode(seller);
        var safeClient = WebUtility.HtmlEncode(client);
        var safeRoute = WebUtility.HtmlEncode(route);
        var safeMode = WebUtility.HtmlEncode(mode);
        var safeReason = WebUtility.HtmlEncode(reason)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", "<br />", StringComparison.Ordinal);

        var editUrl = BuildEditUrl(sellerPortalBaseUrl, request.Id);
        var action = editUrl is null
            ? """
              <p style="margin:20px 0 0;font-size:14px;line-height:22px;color:#343a40;">
                Ingresá al módulo <strong>Ventas</strong>, buscá la solicitud devuelta y seleccioná <strong>Corregir y reenviar</strong>.
              </p>
              """
            : $"""
              <table role="presentation" cellspacing="0" cellpadding="0" border="0" style="margin:24px 0 8px;">
                <tr>
                  <td bgcolor="#a80e18" style="border-radius:9px;text-align:center;">
                    <a href="{WebUtility.HtmlEncode(editUrl)}" style="background-color:#a80e18;border:1px solid #a80e18;border-radius:9px;color:#ffffff;display:inline-block;font-size:14px;font-weight:bold;line-height:20px;padding:13px 22px;text-align:center;text-decoration:none;">
                      Corregir y reenviar solicitud
                    </a>
                  </td>
                </tr>
              </table>
              """;

        var html = $"""
            <!DOCTYPE html>
            <html lang="es">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <title>Solicitud de tarifa devuelta</title>
            </head>
            <body style="margin:0;padding:0;background-color:#f7f7f8;font-family:Arial,Helvetica,sans-serif;color:#30343b;">
              <table role="presentation" cellspacing="0" cellpadding="0" border="0" width="100%" style="background-color:#f7f7f8;">
                <tr>
                  <td align="center" style="padding:32px 12px;">
                    <table role="presentation" cellspacing="0" cellpadding="0" border="0" width="600" style="width:100%;max-width:600px;background-color:#ffffff;border:1px solid #e5e7eb;border-radius:14px;">
                      <tr>
                        <td style="padding:23px 30px;background-color:#a80e18;border-radius:13px 13px 0 0;">
                          <table role="presentation" cellpadding="0" cellspacing="0" border="0" width="100%" style="border-collapse:collapse;">
                            <tr>
                              <td width="125" valign="middle" style="width:125px;padding:0 20px 0 0;">
                                <img src="cid:dhole-castro-fallas-logo" alt="Castro Fallas" width="120" style="display:block;width:120px;height:auto;border:0;outline:0;text-decoration:none;" />
                              </td>
                              <td valign="middle" style="vertical-align:middle;">
                                <p style="margin:0;font-size:12px;font-weight:bold;letter-spacing:1px;color:#ffe1e4;">GRUPO CASTRO FALLAS</p>
                                <p style="margin:5px 0 0;font-size:15px;font-weight:bold;color:#ffffff;">Departamento de Pricing</p>
                              </td>
                            </tr>
                          </table>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:30px;">
                          <p style="margin:0 0 12px;color:#9a661c;font-size:12px;letter-spacing:.8px;font-weight:bold;">ACCIÓN REQUERIDA · CORRECCIÓN PENDIENTE</p>
                          <h1 style="font-size:25px;line-height:32px;margin:0 0 20px;color:#a80e18;">Tu solicitud de tarifa fue devuelta</h1>
                          <p style="margin:0 0 16px;font-size:15px;line-height:24px;color:#343a40;">Hola, {safeSeller}:</p>
                          <p style="margin:0 0 22px;font-size:15px;line-height:24px;color:#343a40;">
                            El equipo de Pricing revisó tu solicitud y necesita que corrijás algunos datos antes de continuar.
                          </p>
                          <table role="presentation" cellspacing="0" cellpadding="0" border="0" width="100%" style="background-color:#f8f9fa;border:1px solid #e5e7eb;border-radius:10px;">
                            <tr>
                              <td style="padding:18px 20px;">
                                <p style="margin:0 0 12px;font-size:13px;color:#61666d;"><strong style="color:#30343b;">Cliente:</strong> {safeClient}</p>
                                <p style="margin:0 0 12px;font-size:13px;line-height:21px;color:#61666d;"><strong style="color:#30343b;">Ruta:</strong> {safeRoute}</p>
                                <p style="margin:0;font-size:13px;color:#61666d;"><strong style="color:#30343b;">Modalidad:</strong> {safeMode}</p>
                              </td>
                            </tr>
                          </table>
                          <table role="presentation" cellspacing="0" cellpadding="0" border="0" width="100%" style="margin-top:22px;background-color:#fff9ed;border-left:4px solid #d69a37;border-radius:6px;">
                            <tr>
                              <td style="padding:16px 18px;">
                                <p style="margin:0 0 8px;font-size:13px;font-weight:bold;color:#795017;">MOTIVO DE DEVOLUCIÓN</p>
                                <p style="margin:0;font-size:15px;line-height:23px;color:#3b352a;overflow-wrap:break-word;">{safeReason}</p>
                              </td>
                            </tr>
                          </table>
                          {action}
                          <p style="margin:18px 0 0;font-size:13px;line-height:21px;color:#61666d;">
                            Una vez corregida, podés reenviar <strong>la misma solicitud</strong>; no hace falta crear otra y se conserva su historial.
                          </p>
                        </td>
                      </tr>
                      <tr>
                        <td style="border-top:1px solid #e5e7eb;padding:18px 30px;">
                          <p style="margin:0;font-size:12px;line-height:18px;color:#6b7280;">Notificación automática del sistema Dhole · Grupo Castro Fallas</p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        return new RateRequestReturnEmailContent(subject, systemBody, html);
    }

    private static string? BuildEditUrl(string? baseUrl, Guid requestId)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo))
            return null;

        return new Uri(uri, $"/pricing/request-rate/{requestId:D}/edit").AbsoluteUri;
    }
}
