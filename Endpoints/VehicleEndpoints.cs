using MainProjectNumoPart.Authorization;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace MainProjectNumoPart.Endpoints
{
    public static class VehicleEndpoints
    {
        // The typed confirmation travels in a JSON body rather than the query string so VINs and
        // registrations don't end up in ingress access logs.
        public record DeleteVehicleRequest(string? Confirmation);

        public record AddUpdateRequest(string? Body);

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

            group.MapPost("/{id:int}/updates", async (
                int id,
                AddUpdateRequest? body,
                VehicleUpdateService updates,
                UserManager<IdentityUser> userManager,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = userManager.GetUserId(http.User)!;
                var email = (await userManager.FindByIdAsync(userId))?.Email ?? userId;

                var result = await updates.AddAsync(id, body?.Body, userId, email, ct);

                return result.Status switch
                {
                    NoteStatus.Success => Results.Ok(),
                    NoteStatus.NotFound => Results.NotFound(),
                    _ => Results.BadRequest(result.Message)
                };
            })
            // Two arguments, never the comma-joined Roles.StaffOrAdmin constant — see the
            // tagging endpoint in PhotoEndpoints.cs for why that silently denies everyone.
            .RequireAuthorization(policy => policy.RequireRole(Roles.Staff, Roles.Admin));

            group.MapDelete("/updates/{updateId:int}", async (
                int updateId,
                VehicleUpdateService updates,
                CancellationToken ct) =>
            {
                var result = await updates.DeleteAsync(updateId, ct);
                return result.Status == NoteStatus.Success ? Results.NoContent() : Results.NotFound();
            }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin));
        }
    }
}
