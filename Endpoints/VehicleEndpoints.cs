using MainProjectNumoPart.Authorization;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.Mvc;

namespace MainProjectNumoPart.Endpoints
{
    public static class VehicleEndpoints
    {
        // The typed confirmation travels in a JSON body rather than the query string so VINs and
        // registrations don't end up in ingress access logs.
        public record DeleteVehicleRequest(string? Confirmation);

        public static void MapVehicleEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/vehicles").RequireAuthorization();

            // A minimal-API endpoint rather than a Razor Page handler specifically for this line:
            // Razor Pages has no per-handler [Authorize], so a delete handler on the (all-roles)
            // Details page would need a hand-written role check in the method body — exactly the
            // kind of manual security check that gets dropped in a later refactor.
            group.MapDelete("/{id:int}", async (
                int id,
                [FromBody] DeleteVehicleRequest? body,
                VehicleDeletionService deletion,
                CancellationToken ct) =>
            {
                // Nullable body: a request with no body binds Confirmation as null and falls into
                // our own ConfirmationMismatch message, rather than minimal APIs' generic 400.
                var result = await deletion.DeleteAsync(id, body?.Confirmation, ct);

                return result.Status switch
                {
                    VehicleDeleteStatus.NotFound => Results.NotFound(),
                    VehicleDeleteStatus.ConfirmationMismatch => Results.BadRequest(result.Message),
                    _ => Results.NoContent()
                };
            }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin));
        }
    }
}
