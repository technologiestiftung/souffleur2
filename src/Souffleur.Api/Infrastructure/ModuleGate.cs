using Souffleur.Domain.Modules;
using Souffleur.Infrastructure.Modules;

namespace Souffleur.Api.Infrastructure;

/// <summary>
/// Sperrt die Endpunkte eines abgeschalteten Moduls.
/// </summary>
/// <remarks>
/// Ohne das waere das Abschalten reine Kosmetik: Die Oberflaeche zeigte den
/// Reiter nicht mehr, die Schnittstelle lieferte aber weiter Daten.
///
/// Geantwortet wird mit 404, nicht mit 403. Fuer dieses Haus gibt es das Modul
/// nicht -- das ist kein Rechteproblem, und ein 403 wuerde verraten, dass es
/// den Endpunkt gaebe.
/// </remarks>
public sealed class ModuleRequirement(SouffleurModule module)
{
    public SouffleurModule Module { get; } = module;
}

public static class ModuleGate
{
    /// <summary>
    /// Bindet eine Gruppe von Endpunkten an ein Modul.
    /// </summary>
    public static TBuilder RequireModule<TBuilder>(this TBuilder builder, SouffleurModule module)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new ModuleRequirement(module));
        builder.AddEndpointFilter(async (context, next) =>
        {
            ModuleRequirement? requirement = context.HttpContext
                .GetEndpoint()?.Metadata.GetMetadata<ModuleRequirement>();

            if (requirement is not null)
            {
                var registry = context.HttpContext.RequestServices.GetRequiredService<ModuleRegistry>();
                if (!registry.IsEnabled(requirement.Module))
                {
                    return TypedResults.Problem(
                        $"Das Modul {requirement.Module.Label()} ist in diesem Haus nicht eingeschaltet.",
                        statusCode: StatusCodes.Status404NotFound);
                }
            }

            return await next(context);
        });

        return builder;
    }
}
