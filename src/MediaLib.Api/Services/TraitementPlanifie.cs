using MediaLib.Api.Configuration;
using MediaLib.Shared;
using Microsoft.Extensions.Options;

namespace MediaLib.Api.Services;

/// <summary>Un passage du traitement planifié : détection des échéances, envoi/réessai des relances, expiration des réservations.</summary>
public class TraitementPlanifie(RelanceService relances, ReservationService reservations, ILogger<TraitementPlanifie> logger)
{
    public async Task<TraitementRelancesDto> ExecuterAsync(CancellationToken ct = default)
    {
        var detectees = await relances.DetecterAsync(ct);
        var (envoyees, echecs) = await relances.TraiterAsync(ct);
        var expirees = await reservations.ExpirerReservationsAsync(ct);
        var total = detectees.Values.Sum();
        logger.LogInformation("Traitement planifié : {Detectees} relance(s) détectée(s), {Envoyees} envoyée(s), {Echecs} en échec, {Expirees} réservation(s) expirée(s).",
            total, envoyees, echecs, expirees);
        return new TraitementRelancesDto(total, envoyees, echecs, expirees);
    }
}

/// <summary>Service d'arrière-plan : exécute le traitement planifié à intervalle régulier.</summary>
public class PlanificateurRelances(IServiceScopeFactory scopes, IOptions<PlanificateurOptions> options, ILogger<PlanificateurRelances> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Actif)
        {
            logger.LogInformation("Planificateur des relances désactivé (Planificateur:Actif=false).");
            return;
        }
        using var minuterie = new PeriodicTimer(TimeSpan.FromMinutes(options.Value.IntervalleMinutes));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<TraitementPlanifie>().ExecuterAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Une erreur ponctuelle n'arrête pas le planificateur : le passage suivant réessaiera.
                logger.LogError(e, "Échec du traitement planifié des relances.");
            }
        }
        while (await minuterie.WaitForNextTickAsync(stoppingToken));
    }
}
