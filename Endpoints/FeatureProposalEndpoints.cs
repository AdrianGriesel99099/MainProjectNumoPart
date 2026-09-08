using MainProjectNumoPart.Authorization;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.Identity;

namespace MainProjectNumoPart.Endpoints
{
    public static class FeatureProposalEndpoints
    {
        public record SubmitProposalRequest(string? Title, string? Description);
        public record DecideProposalRequest(FeatureReviewDecision Decision, string? Comment);

        // Both routes here need the same role bar (submitting or reviewing an idea is treated the
        // same as uploading -- see User.CanUpload() in _Layout.cshtml), so the group-level policy
        // covers both rather than repeating it per route the way VehicleEndpoints does for its
        // mixed public/Admin group.
        public static void MapFeatureProposalEndpoints(this WebApplication app)
        {
            var group = app.MapGroup("/api/feature-proposals")
                .RequireAuthorization(policy => policy.RequireRole(Roles.Staff, Roles.Admin));

            group.MapPost("", async (
                SubmitProposalRequest? body,
                FeatureProposalService proposals,
                UserManager<IdentityUser> userManager,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = userManager.GetUserId(http.User)!;
                var email = (await userManager.FindByIdAsync(userId))?.Email ?? userId;

                var result = await proposals.SubmitAsync(body?.Title, body?.Description, userId, email, ct);

                return result.Status == FeatureProposalSubmitStatus.Success
                    ? Results.Ok(new { id = result.ProposalId })
                    : Results.BadRequest(result.Message);
            });

            group.MapPost("/{id:int}/decide", async (
                int id,
                DecideProposalRequest? body,
                FeatureProposalService proposals,
                UserManager<IdentityUser> userManager,
                HttpContext http,
                CancellationToken ct) =>
            {
                if (body is null) return Results.BadRequest("No decision supplied.");

                var userId = userManager.GetUserId(http.User)!;
                var result = await proposals.DecideAsync(id, body.Decision, body.Comment, userId, ct);

                return result.Status switch
                {
                    FeatureProposalDecisionStatus.Success => Results.Ok(),
                    FeatureProposalDecisionStatus.NotFound => Results.NotFound(),
                    _ => Results.BadRequest(result.Message)
                };
            });
        }
    }
}
