using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Souffleur.Api.Features.Search;

/// <summary>
/// Das eine Suchfeld oben: Es fragt jeden Bereich, den diese Person sehen
/// darf, und liefert die Funde nach Bereichen gebuendelt zurueck.
/// </summary>
/// <remarks>
/// Die Rechtepruefung liegt bei den Quellen, nicht hier: Finanzen antwortet
/// nur, wer Buchungen lesen darf; Dokumente nur, was <c>DocumentService.Visible</c>
/// durchlaesst. Ein abgeschaltetes Modul kommt gar nicht vor -- als gaebe es
/// den Bereich nicht, genau wie bei seinen eigenen Endpunkten.
///
/// Die Bereiche stehen in der Reihenfolge der Reiter. Leere Bereiche werden
/// weggelassen, damit die Oberflaeche nichts ausblenden muss.
/// </remarks>
public static class SearchEndpoints
{
    /// <summary>
    /// Wer ein Modul hinzufuegt, traegt seine Quelle hier ein. Die Reihenfolge
    /// ist die der Antwort.
    /// </summary>
    private static readonly ISuchquelle[] Quellen =
    [
        new KontakteQuelle(),
        new ProduktionenQuelle(),
        new AufgabenQuelle(),
        new KalenderQuelle(),
        new DokumenteQuelle(),
        new FinanzenQuelle(),
        new ChatQuelle(),
        new HausQuelle(),
    ];

    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/search", SearchAsync)
            .WithTags("Suche")
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> SearchAsync(
        [FromQuery] string? q,
        ClaimsPrincipal user,
        HttpContext http,
        CancellationToken token)
    {
        Suchbegriff? begriff = Suchbegriff.Aus(q);
        if (begriff is null)
        {
            // Zu wenig getippt: kein Fehler, nur noch nichts zu zeigen.
            return TypedResults.Ok(Array.Empty<Bereich>());
        }

        var lauf = new Suchlauf(http.RequestServices, user, begriff);
        var bereiche = new List<Bereich>();

        // Nacheinander, nicht parallel: Ein DbContext vertraegt nur eine
        // Abfrage zugleich, und die Antwort kommt bei SQLite ohnehin aus dem
        // Dateipuffer.
        foreach (ISuchquelle quelle in Quellen)
        {
            if (!quelle.Offen(lauf))
            {
                continue;
            }

            IReadOnlyList<Treffer> treffer = await quelle.SuchenAsync(lauf, token);
            if (treffer.Count > 0)
            {
                bereiche.Add(new Bereich(quelle.Key, quelle.Label, quelle.Icon, treffer));
            }
        }

        return TypedResults.Ok(bereiche);
    }
}
