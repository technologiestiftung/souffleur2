using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Souffleur.Infrastructure.Persistence.Configurations;

/// <summary>
/// Speichert Geldbetraege als ganze Cent.
/// </summary>
/// <remarks>
/// SQLite kennt keinen Dezimaltyp. EF Core legt <c>decimal</c> sonst als Text ab:
/// korrekt, aber in SQL nicht sortier- und summierbar, und als Gleitkomma waere es
/// schlicht falsch. Ganze Cent sind exakt, vergleichbar und addierbar.
///
/// Damit liegt die Obergrenze bei rund 92 Billionen Euro -- fuer ein Theater reicht das.
/// </remarks>
public class MoneyConverter() : ValueConverter<decimal, long>(
    amount => (long)Math.Round(amount * 100m, MidpointRounding.ToEven),
    cents => cents / 100m);

/// <summary>Dasselbe fuer Felder, die leer bleiben duerfen.</summary>
public class NullableMoneyConverter() : ValueConverter<decimal?, long?>(
    amount => amount == null ? null : (long)Math.Round(amount.Value * 100m, MidpointRounding.ToEven),
    cents => cents == null ? null : cents.Value / 100m);
