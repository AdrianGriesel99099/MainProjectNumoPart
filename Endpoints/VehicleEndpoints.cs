using MainProjectNumoPart.Authorization;
using MainProjectNumoPart.Models;
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

        // PhotoId is nullable — null means the part had no tagged photo when the mark was made,
        // so it's anchored at a fixed position rather than a specific photo.
        public record AddDamageMarkRequest(Part Part, int? PhotoId, double XPercent, double YPercent, string? Note);

        public record VehicleSuggestion(int Id, string? Vin, string? Reg, string? MakeModel);

        // Fewer than VehicleLookupService's own MaxResults (25) -- that list is a "pick the right
        // one" page, this is a dropdown under a text box, and needs to fit on screen as you type.
        private const int SuggestionLimit = 8;

        // A search term shorter than this matches too much of the table to be a useful dropdown
        // (a single digit hits a large fraction of every VIN) -- the home page's full search
        // still works normally for a short term, only the live suggestions wait for more input.
        private const int MinSuggestionTermLength = 2;

        public static void MapVehicleEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/vehicles").RequireAuthorization();

            group.MapGet("/search-suggestions", async (
                string? term,
                VehicleLookupService vehicles,
                CancellationToken ct) =>
            {
                if (term is null || term.Trim().Length < MinSuggestionTermLength)
                {
                    return Results.Ok(Array.Empty<VehicleSuggestion>());
                }

                var matches = await vehicles.FindManyBySearchTermAsync(term, SuggestionLimit, ct);

                return Results.Ok(matches.Select(v =>
                    new VehicleSuggestion(v.Id, v.Vin, v.Reg, v.MakeModel)));
            });

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

            group.MapPost("/{id:int}/damage-marks", async (
                int id,
                AddDamageMarkRequest? body,
                DamageMarkService marks,
                UserManager<IdentityUser> userManager,
                HttpContext http,
                CancellationToken ct) =>
            {
                if (body is null) return Results.BadRequest("No mark data supplied.");

                var userId = userManager.GetUserId(http.User)!;
                var email = (await userManager.FindByIdAsync(userId))?.Email ?? userId;

                var result = await marks.AddAsync(
                    id, body.Part, body.PhotoId, body.XPercent, body.YPercent, body.Note, userId, email, ct);

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

            group.MapDelete("/damage-marks/{markId:int}", async (
                int markId,
                DamageMarkService marks,
                CancellationToken ct) =>
            {
                var result = await marks.DeleteAsync(markId, ct);
                return result.Status == NoteStatus.Success ? Results.NoContent() : Results.NotFound();
            }).RequireAuthorization(policy => policy.RequireRole(Roles.Admin));
        }
    }
}
