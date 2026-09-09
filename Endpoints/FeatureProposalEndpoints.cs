using MainProjectNumoPart.Authorization;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Microsoft.AspNetCore.Identity;

namespace MainProjectNumoPart.Endpoints
{
    public static class FeatureProposalEndpoints
    {
        public record SubmitProposalRequest(string? Title, string? Description);
        public record AnswerRequest(int QuestionId, int SelectedOptionId);
        public record DecideProposalRequest(FeatureReviewDecision Decision, string? Comment, List<AnswerRequest>? Answers = null);

        public record BotOptionView(int Id, string Label);
        public record BotQuestionView(int Id, string Prompt, List<BotOptionView> Options, string? SelectedOption);
        public record BotRoundView(int RoundNumber, string? AiContent, FeatureReviewDecision? HumanDecision, string? HumanComment, List<BotQuestionView> Questions);
        public record BotProposalView(int Id, string Title, string Description, List<BotRoundView> Rounds);
        public record BotApprovedProposalView(int Id, string Title, string Description);
        public record BotQuestionInput(string Prompt, List<string> Options);
        public record RecordAiRevisionRequest(string RevisedDescription, bool ReadyForFinalApproval, List<BotQuestionInput>? Questions = null);

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
                var answers = body.Answers?
                    .Select(a => new FeatureProposalAnswerInput(a.QuestionId, a.SelectedOptionId))
                    .ToList();
                var result = await proposals.DecideAsync(id, body.Decision, body.Comment, userId, answers, ct);

                return result.Status switch
                {
                    FeatureProposalDecisionStatus.Success => Results.Ok(),
                    FeatureProposalDecisionStatus.NotFound => Results.NotFound(),
                    _ => Results.BadRequest(result.Message)
                };
            });

            MapBotEndpoints(app);
        }

        // Called only by Tools/FeatureReviewRelay (.github/workflows/feature-review-prepare.yml
        // and feature-review-apply.yml) -- a script with no user to sign in as, so this group is
        // gated by a shared API key (ApiKeyEndpointFilter) instead of the cookie/role auth every
        // other route in this app uses. Deliberately its own DTOs rather than serializing
        // FeatureProposal/FeatureProposalRound directly: those have a circular Round->Proposal
        // reference and carry internal-only fields (SubmittedByUserId etc.) this caller has no
        // use for.
        private static void MapBotEndpoints(WebApplication app)
        {
            var group = app.MapGroup("/api/bot/feature-proposals")
                .AddEndpointFilter<ApiKeyEndpointFilter>();

            group.MapGet("/awaiting-ai-revision", async (FeatureProposalService proposals, CancellationToken ct) =>
            {
                var pending = await proposals.ListAwaitingAiRevisionAsync(ct);
                return Results.Ok(pending.Select(p => new BotProposalView(
                    p.Id, p.Title, p.Description,
                    p.Rounds.OrderBy(r => r.RoundNumber)
                        .Select(r => new BotRoundView(
                            r.RoundNumber, r.AiContent, r.HumanDecision, r.HumanComment,
                            r.Questions.OrderBy(q => q.QuestionNumber)
                                .Select(q => new BotQuestionView(
                                    q.Id, q.Prompt,
                                    q.Options.OrderBy(o => o.OptionNumber)
                                        .Select(o => new BotOptionView(o.Id, o.Label))
                                        .ToList(),
                                    q.Options.FirstOrDefault(o => o.Id == q.SelectedOptionId)?.Label))
                                .ToList()))
                        .ToList())));
            });

            group.MapPost("/{id:int}/revision", async (
                int id,
                RecordAiRevisionRequest? body,
                FeatureProposalService proposals,
                CancellationToken ct) =>
            {
                if (body is null) return Results.BadRequest("No revision supplied.");

                var questions = body.Questions?
                    .Select(q => new FeatureProposalQuestionInput(q.Prompt, q.Options))
                    .ToList();
                var result = await proposals.RecordAiRevisionAsync(id, body.RevisedDescription, body.ReadyForFinalApproval, questions, ct);

                return result.Status switch
                {
                    FeatureProposalDecisionStatus.Success => Results.Ok(),
                    FeatureProposalDecisionStatus.NotFound => Results.NotFound(),
                    _ => Results.BadRequest(result.Message)
                };
            });

            group.MapGet("/approved-unqueued", async (FeatureProposalService proposals, CancellationToken ct) =>
            {
                var approved = await proposals.ListApprovedUnqueuedAsync(ct);
                // The latest round's write-up, not the original one-line submission -- by the
                // time a proposal is Approved it's been through at least one round with
                // AiContent (ReadyForFinalApproval requires that), but the null-coalesce keeps
                // this safe if that invariant is ever wrong.
                return Results.Ok(approved.Select(p => new BotApprovedProposalView(
                    p.Id, p.Title,
                    p.Rounds.OrderByDescending(r => r.RoundNumber).First().AiContent ?? p.Description)));
            });

            group.MapPost("/{id:int}/mark-queued", async (int id, FeatureProposalService proposals, CancellationToken ct) =>
            {
                var result = await proposals.MarkQueuedForBuildAsync(id, ct);
                return result.Status == FeatureProposalDecisionStatus.Success ? Results.Ok() : Results.NotFound();
            });
        }
    }
}
