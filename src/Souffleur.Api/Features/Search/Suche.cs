using System.Linq.Expressions;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Modules;
using Souffleur.Infrastructure.Identity;
using Souffleur.Infrastructure.Modules;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Api.Features.Search;

/// <summary>Ein Fund: wohin er fuehrt, steht im <see cref="Link"/>.</summary>
public record Treffer(Guid Id, string Titel, string? Untertitel, string Link);

/// <summary>Die Funde eines Bereichs, so wie die Oberflaeche sie gruppiert.</summary>
public record Bereich(string Key, string Label, string Icon, IReadOnlyList<Treffer> Treffer);

/// <summary>
/// Was jemand ins Suchfeld getippt hat, zerlegt in Woerter.
/// </summary>
/// <remarks>
/// Jedes Wort muss vorkommen, in beliebiger Reihenfolge und an beliebiger
/// Stelle: „hamlet probe" findet „Probe Hamlet". Die Quellen wenden dafuer
/// <see cref="Eingrenzen{T}"/> an -- einmal je Wort, auf einen Heuhaufen aus
/// den Feldern, die zu einem Gegenstand gehoeren.
///
/// Gesucht wird mit LIKE, nicht mit einem Volltextindex: Ein Haus hat ein
/// paar tausend Zeilen, keine Millionen, und SQLite liest sie schneller, als
/// ein Index gepflegt waere. SQLites LIKE ist fuer ASCII schreibungsblind,
/// fuer Umlaute nicht -- „über" findet „Übergabe" deshalb nicht, „Über" schon.
/// </remarks>
public sealed class Suchbegriff
{
    public const int MindestLaenge = 2;

    /// <summary>
    /// Das Maskierungszeichen der Muster. Wer ein Muster in einem eigenen
    /// Ausdruck anwendet, gibt es an EF.Functions.Like mit -- sonst gilt ein
    /// getipptes Prozentzeichen wieder als Platzhalter.
    /// </summary>
    public const string Escape = "\\";

    private Suchbegriff(IReadOnlyList<string> woerter)
    {
        Woerter = woerter;
        Muster = [.. woerter.Select(w => "%" + Maskiert(w) + "%")];
    }

    public IReadOnlyList<string> Woerter { get; }

    /// <summary>Je Wort ein LIKE-Muster, Platzhalter aus der Eingabe entschaerft.</summary>
    public IReadOnlyList<string> Muster { get; }

    public static Suchbegriff? Aus(string? eingabe)
    {
        string[] woerter = (eingabe ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();

        return woerter.Length == 0 || woerter.Sum(w => w.Length) < MindestLaenge
            ? null
            : new Suchbegriff(woerter);
    }

    /// <summary>
    /// Behaelt nur Zeilen, deren Heuhaufen jedes Wort enthaelt.
    /// </summary>
    /// <remarks>
    /// Die Schleife statt eines einzigen Ausdrucks, weil EF <c>woerter.All(…)</c>
    /// nicht nach SQL uebersetzen kann -- je Wort eine eigene WHERE-Bedingung
    /// kann es. Der Heuhaufen wird dabei je Wort wiederholt; bei den Mengen
    /// eines Hauses faellt das nicht ins Gewicht.
    ///
    /// <paramref name="ausserdem"/> liefert je Wort eine zweite Bedingung, die
    /// das Wort ebenfalls gelten laesst -- fuer Felder, die nicht in einen
    /// String passen, etwa die Schlagworte eines Dokuments.
    /// </remarks>
    public IQueryable<T> Eingrenzen<T>(
        IQueryable<T> quelle,
        Expression<Func<T, string>> heuhaufen,
        Func<string, Expression<Func<T, bool>>>? ausserdem = null)
    {
        ParameterExpression zeile = heuhaufen.Parameters[0];

        foreach (string muster in Muster)
        {
            Expression bedingung = Expression.Call(
                LikeMethode,
                Expression.Constant(EF.Functions),
                heuhaufen.Body,
                Expression.Constant(muster),
                Expression.Constant(Escape));

            if (ausserdem is not null)
            {
                Expression<Func<T, bool>> zusatz = ausserdem(muster);
                bedingung = Expression.OrElse(
                    bedingung,
                    new ParameterTausch(zusatz.Parameters[0], zeile).Visit(zusatz.Body));
            }

            quelle = quelle.Where(Expression.Lambda<Func<T, bool>>(bedingung, zeile));
        }

        return quelle;
    }

    private static readonly System.Reflection.MethodInfo LikeMethode = typeof(DbFunctionsExtensions).GetMethod(
        nameof(DbFunctionsExtensions.Like),
        [typeof(DbFunctions), typeof(string), typeof(string), typeof(string)])!;

    /// <summary>Setzt den Parameter eines fremden Lambdas auf den eigenen um, damit beide in einem Ausdruck stehen koennen.</summary>
    private sealed class ParameterTausch(ParameterExpression von, ParameterExpression nach) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == von ? nach : base.VisitParameter(node);
    }

    private static string Maskiert(string wort) => wort
        .Replace(Escape, Escape + Escape)
        .Replace("%", Escape + "%")
        .Replace("_", Escape + "_");
}

/// <summary>
/// Alles, was eine Quelle braucht, um zu suchen: die Datenbank, wer fragt,
/// und was das Haus eingeschaltet hat.
/// </summary>
public sealed class Suchlauf(
    IServiceProvider dienste,
    ClaimsPrincipal user,
    Suchbegriff begriff)
{
    /// <summary>Mehr als eine Handvoll je Bereich lenkt nur ab -- wer mehr will, tippt weiter.</summary>
    public const int HoechstensJeBereich = 8;

    public SouffleurDbContext Context { get; } = dienste.GetRequiredService<SouffleurDbContext>();
    public Suchbegriff Begriff { get; } = begriff;
    public Guid? UserId { get; } = dienste.GetRequiredService<ICurrentUser>().Id;

    /// <summary>Ein Dienst aus dem Scope der Anfrage -- fuer Quellen, die ihre Sichtbarkeitsregel dort haben.</summary>
    public T Dienst<T>() where T : notnull => dienste.GetRequiredService<T>();

    public bool Darf(Permission recht) =>
        user.HasClaim(PermissionClaimsFactory.ClaimType, ((int)recht).ToString());

    public bool ModulAn(SouffleurModule modul) => dienste.GetRequiredService<ModuleRegistry>().IsEnabled(modul);
}

/// <summary>
/// Ein Bereich, der durchsucht werden kann. Jedes Modul bringt seine eigene
/// Quelle mit; wer ein Modul hinzufuegt, haengt hier eine an.
/// </summary>
public interface ISuchquelle
{
    string Key { get; }
    string Label { get; }

    /// <summary>Der Name aus <c>IconName</c> der Oberflaeche.</summary>
    string Icon { get; }

    /// <summary>Ob diese Person in diesem Haus hier ueberhaupt suchen darf.</summary>
    bool Offen(Suchlauf lauf);

    Task<IReadOnlyList<Treffer>> SuchenAsync(Suchlauf lauf, CancellationToken token);
}
