using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MainProjectNumoPart.Services
{
    public enum FeatureProposalSubmitStatus
    {
        Success,
        EmptyTitle,
        EmptyDescription,
        TooLong
    }

    public record FeatureProposalSubmitResult(FeatureProposalSubmitStatus Status, int? ProposalId = null, string? Message = null);

    public enum FeatureProposalDecisionStatus
    {
        Success,
        NotFound,
        InvalidState,
        CommentRequired,
        InvalidAnswer
    }

    public record FeatureProposalDecisionResult(FeatureProposalDecisionStatus Status, string? Message = null);

    // A question drafted alongside a round's revision, with the reviewer's chosen option (if
    // they answered it). Used both for submitting an answer (DecideAsync) and for drafting a new
    // one (RecordAiRevisionAsync) -- Options carries plain label strings on the way in.
    public record FeatureProposalQuestionInput(string Prompt, List<string> Options);
    public record FeatureProposalAnswerInput(int QuestionId, int SelectedOptionId);

    // The review/approval state machine described in CLAUDE.md's "Feature proposals & review"
    // section. Every round's HumanDecision is set here, never trusted from the client beyond which
    // button was pressed -- the proposal's current Status, loaded fresh from the DB, is what
    // actually gates which transition is legal. Same "don't trust the button being hidden"
    // philosophy as UserAdminService.
    public class FeatureProposalService
    {
        public const int MaxTitleLength = 150;
        public const int MaxDescriptionLength = 4000;
        public const int MaxCommentLength = 2000;

        private readonly AppDbContext _db;
        private readonly ILogger<FeatureProposalService> _logger;

        public FeatureProposalService(AppDbContext db, ILogger<FeatureProposalService> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<FeatureProposalSubmitResult> SubmitAsync(
            string? title, string? description, string userId, string userEmail, CancellationToken ct = default)
        {
            var trimmedTitle = title?.Trim();
            var trimmedDescription = description?.Trim();

            if (string.IsNullOrEmpty(trimmedTitle))
            {
                return new FeatureProposalSubmitResult(FeatureProposalSubmitStatus.EmptyTitle, Message: "Give the idea a title.");
            }
            if (string.IsNullOrEmpty(trimmedDescription))
            {
                return new FeatureProposalSubmitResult(FeatureProposalSubmitStatus.EmptyDescription, Message: "Describe what the feature should do.");
            }
            if (trimmedTitle.Length > MaxTitleLength || trimmedDescription.Length > MaxDescriptionLength)
            {
                return new FeatureProposalSubmitResult(FeatureProposalSubmitStatus.TooLong,
                    Message: $"Title is limited to {MaxTitleLength} characters, description to {MaxDescriptionLength}.");
            }

            var proposal = new FeatureProposal
            {
                Title = trimmedTitle,
                Description = trimmedDescription,
                SubmittedByUserId = userId,
                SubmittedByEmail = userEmail,
                Status = FeatureProposalStatus.NeedsReview,
                CreatedAtUtc = DateTime.UtcNow
            };
            proposal.Rounds.Add(new FeatureProposalRound
            {
                RoundNumber = 0,
                AiContent = null,
                CreatedAtUtc = proposal.CreatedAtUtc
            });

            _db.FeatureProposals.Add(proposal);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Feature proposal {ProposalId} submitted by {UserId}: {Title}", proposal.Id, userId, proposal.Title);
            return new FeatureProposalSubmitResult(FeatureProposalSubmitStatus.Success, proposal.Id);
        }

        public async Task<FeatureProposalDecisionResult> DecideAsync(
            int proposalId, FeatureReviewDecision decision, string? comment, string userId,
            IReadOnlyList<FeatureProposalAnswerInput>? answers = null, CancellationToken ct = default)
        {
            var proposal = await _db.FeatureProposals
                .Include(p => p.Rounds).ThenInclude(r => r.Questions).ThenInclude(q => q.Options)
                .FirstOrDefaultAsync(p => p.Id == proposalId, ct);

            if (proposal is null)
            {
                return new FeatureProposalDecisionResult(FeatureProposalDecisionStatus.NotFound);
            }

            if (proposal.Status == FeatureProposalStatus.Denied || proposal.Status == FeatureProposalStatus.Approved)
            {
                return new FeatureProposalDecisionResult(FeatureProposalDecisionStatus.InvalidState,
                    "This proposal has already been decided.");
            }

            // Denying is always available, even mid-revision -- a human can stop a proposal any
            // time, regardless of whether a round is currently being drafted for it. Accepting or
            // asking for changes only makes sense once there's an actual round to react to.
            var awaitingDecision = proposal.Status == FeatureProposalStatus.NeedsReview
                || proposal.Status == FeatureProposalStatus.ReadyForFinalApproval;
            if (decision != FeatureReviewDecision.Denied && !awaitingDecision)
            {
                return new FeatureProposalDecisionResult(FeatureProposalDecisionStatus.InvalidState,
                    "This proposal isn't currently awaiting a decision.");
            }

            var trimmedComment = comment?.Trim();
            if (decision == FeatureReviewDecision.Revised && string.IsNullOrEmpty(trimmedComment))
            {
                return new FeatureProposalDecisionResult(FeatureProposalDecisionStatus.CommentRequired,
                    "Say what should change before sending this back for another round.");
            }
            if (trimmedComment is { Length: > MaxCommentLength })
            {
                return new FeatureProposalDecisionResult(FeatureProposalDecisionStatus.CommentRequired,
                    $"Comments are limited to {MaxCommentLength} characters.");
            }

            // Only record the decision (and any question answers) on the latest round when that
            // round is actually the one awaiting it. Denying while AwaitingAiRevision means the
            // latest round's own Accept/Revise decision already happened -- overwriting it would
            // erase that history for no benefit; the status change to Denied is the record then.
            if (awaitingDecision)
            {
                var latestRound = proposal.Rounds.OrderByDescending(r => r.RoundNumber).First();

                // Answering is optional, same as the comment -- but an answer that doesn't
                // actually belong to this round's own questions is rejected outright rather than
                // silently ignored, since that can only mean a stale or tampered request.
                if (answers is { Count: > 0 })
                {
                    foreach (var answer in answers)
                    {
                        var question = latestRound.Questions.FirstOrDefault(q => q.Id == answer.QuestionId);
                        var option = question?.Options.FirstOrDefault(o => o.Id == answer.SelectedOptionId);
                        if (question is null || option is null)
                        {
                            return new FeatureProposalDecisionResult(FeatureProposalDecisionStatus.InvalidAnswer,
                                "That answer doesn't match a question on this round.");
                        }
                        question.SelectedOptionId = option.Id;
                    }
                }

                latestRound.HumanDecision = decision;
                latestRound.HumanComment = string.IsNullOrEmpty(trimmedComment) ? null : trimmedComment;
                latestRound.DecidedByUserId = userId;
                latestRound.DecidedAtUtc = DateTime.UtcNow;
            }

            proposal.Status = decision switch
            {
                FeatureReviewDecision.Denied => FeatureProposalStatus.Denied,
                FeatureReviewDecision.Revised => FeatureProposalStatus.AwaitingAiRevision,
                FeatureReviewDecision.TooComplex => FeatureProposalStatus.AwaitingAiRevision,
                FeatureReviewDecision.Accepted when proposal.Status == FeatureProposalStatus.ReadyForFinalApproval
                    => FeatureProposalStatus.Approved,
                FeatureReviewDecision.Accepted => FeatureProposalStatus.AwaitingAiRevision,
                _ => proposal.Status
            };

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Feature proposal {ProposalId} decided {Decision} by {UserId} -> {Status}",
                proposal.Id, decision, userId, proposal.Status);

            return new FeatureProposalDecisionResult(FeatureProposalDecisionStatus.Success);
        }

        public Task<List<FeatureProposal>> ListAsync(CancellationToken ct = default) =>
            _db.FeatureProposals.OrderByDescending(p => p.CreatedAtUtc).ToListAsync(ct);

        public Task<FeatureProposal?> GetWithRoundsAsync(int id, CancellationToken ct = default) =>
            _db.FeatureProposals
                .Include(p => p.Rounds).ThenInclude(r => r.Questions).ThenInclude(q => q.Options)
                .FirstOrDefaultAsync(p => p.Id == id, ct);

        // The operations below are called by Tools/FeatureReviewRelay (via
        // Endpoints/FeatureProposalEndpoints.cs's API-key-gated /api/bot/... routes), not by the
        // browser -- see CLAUDE.md's "Feature proposals & review" section for the full nightly
        // workflow these support.

        public Task<List<FeatureProposal>> ListAwaitingAiRevisionAsync(CancellationToken ct = default) =>
            _db.FeatureProposals
                .Include(p => p.Rounds).ThenInclude(r => r.Questions).ThenInclude(q => q.Options)
                .Where(p => p.Status == FeatureProposalStatus.AwaitingAiRevision)
                .ToListAsync(ct);

        public async Task<FeatureProposalDecisionResult> RecordAiRevisionAsync(
            int proposalId, string revisedDescription, bool readyForFinalApproval,
            IReadOnlyList<FeatureProposalQuestionInput>? questions = null, CancellationToken ct = default)
        {
            var proposal = await _db.FeatureProposals
                .Include(p => p.Rounds)
                .FirstOrDefaultAsync(p => p.Id == proposalId, ct);

            if (proposal is null)
            {
                return new FeatureProposalDecisionResult(FeatureProposalDecisionStatus.NotFound);
            }
            if (proposal.Status != FeatureProposalStatus.AwaitingAiRevision)
            {
                return new FeatureProposalDecisionResult(FeatureProposalDecisionStatus.InvalidState,
                    "This proposal isn't currently awaiting an AI revision.");
            }

            var nextRound = proposal.Rounds.Max(r => r.RoundNumber) + 1;
            var round = new FeatureProposalRound
            {
                RoundNumber = nextRound,
                AiContent = revisedDescription,
                CreatedAtUtc = DateTime.UtcNow
            };

            var questionNumber = 0;
            foreach (var question in questions ?? Array.Empty<FeatureProposalQuestionInput>())
            {
                var questionEntity = new FeatureProposalQuestion
                {
                    QuestionNumber = questionNumber++,
                    Prompt = question.Prompt
                };
                var optionNumber = 0;
                foreach (var optionLabel in question.Options)
                {
                    questionEntity.Options.Add(new FeatureProposalQuestionOption
                    {
                        OptionNumber = optionNumber++,
                        Label = optionLabel
                    });
                }
                round.Questions.Add(questionEntity);
            }

            proposal.Rounds.Add(round);
            proposal.Status = readyForFinalApproval
                ? FeatureProposalStatus.ReadyForFinalApproval
                : FeatureProposalStatus.NeedsReview;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Feature proposal {ProposalId}: AI drafted round {Round} with {QuestionCount} question(s), ready-for-final={Ready}",
                proposal.Id, nextRound, round.Questions.Count, readyForFinalApproval);

            return new FeatureProposalDecisionResult(FeatureProposalDecisionStatus.Success);
        }

        // Rounds included so callers can hand the build step the latest, human-reviewed write-up
        // rather than the original one-line submission -- see the bot endpoint that consumes this.
        public Task<List<FeatureProposal>> ListApprovedUnqueuedAsync(CancellationToken ct = default) =>
            _db.FeatureProposals
                .Include(p => p.Rounds)
                .Where(p => p.Status == FeatureProposalStatus.Approved && p.QueuedForBuildAtUtc == null)
                .ToListAsync(ct);

        public async Task<FeatureProposalDecisionResult> MarkQueuedForBuildAsync(int proposalId, CancellationToken ct = default)
        {
            var proposal = await _db.FeatureProposals.FirstOrDefaultAsync(p => p.Id == proposalId, ct);
            if (proposal is null)
            {
                return new FeatureProposalDecisionResult(FeatureProposalDecisionStatus.NotFound);
            }

            proposal.QueuedForBuildAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return new FeatureProposalDecisionResult(FeatureProposalDecisionStatus.Success);
        }
    }
}
