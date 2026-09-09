using System.Linq;
using System.Threading.Tasks;
using MainProjectNumoPart.Models;
using MainProjectNumoPart.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MainProjectNumoPart.Tests
{
    public class FeatureProposalServiceTests
    {
        private static FeatureProposalService Build(Data.AppDbContext db)
            => new(db, NullLogger<FeatureProposalService>.Instance);

        [Fact]
        public async Task SubmitAsync_CreatesAProposalWithARound0()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var result = await Build(db).SubmitAsync("Bulk photo export", "Let staff export a vehicle's photos as a zip.", "u1", "staff@w.local");

            Assert.Equal(FeatureProposalSubmitStatus.Success, result.Status);
            var saved = db.FeatureProposals.Include(p => p.Rounds).Single();
            Assert.Equal(FeatureProposalStatus.NeedsReview, saved.Status);
            Assert.Equal("staff@w.local", saved.SubmittedByEmail);
            var round0 = Assert.Single(saved.Rounds);
            Assert.Equal(0, round0.RoundNumber);
            Assert.Null(round0.AiContent);
            Assert.Null(round0.HumanDecision);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public async Task SubmitAsync_RejectsEmptyTitle(string? title)
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var result = await Build(db).SubmitAsync(title, "A description", "u1", "staff@w.local");

            Assert.Equal(FeatureProposalSubmitStatus.EmptyTitle, result.Status);
            Assert.Empty(db.FeatureProposals);
        }

        [Fact]
        public async Task SubmitAsync_RejectsEmptyDescription()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var result = await Build(db).SubmitAsync("A title", "  ", "u1", "staff@w.local");

            Assert.Equal(FeatureProposalSubmitStatus.EmptyDescription, result.Status);
            Assert.Empty(db.FeatureProposals);
        }

        [Fact]
        public async Task SubmitAsync_RejectsTitleOverTheLengthCap()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var tooLong = new string('x', FeatureProposalService.MaxTitleLength + 1);

            var result = await Build(db).SubmitAsync(tooLong, "A description", "u1", "staff@w.local");

            Assert.Equal(FeatureProposalSubmitStatus.TooLong, result.Status);
            Assert.Empty(db.FeatureProposals);
        }

        [Fact]
        public async Task DecideAsync_AcceptingARawSubmission_QueuesItForAiRevisionNotFinalApproval()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var submit = await Build(db).SubmitAsync("Title", "Description", "submitter", "submitter@w.local");

            var result = await Build(db).DecideAsync(submit.ProposalId!.Value, FeatureReviewDecision.Accepted, null, "reviewer");

            Assert.Equal(FeatureProposalDecisionStatus.Success, result.Status);
            Assert.Equal(FeatureProposalStatus.AwaitingAiRevision, db.FeatureProposals.Single().Status);
        }

        [Fact]
        public async Task DecideAsync_AcceptingWhenReadyForFinalApproval_ApprovesIt()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var submit = await Build(db).SubmitAsync("Title", "Description", "submitter", "submitter@w.local");
            var proposal = db.FeatureProposals.Single();
            proposal.Status = FeatureProposalStatus.ReadyForFinalApproval;
            proposal.Rounds.Add(new FeatureProposalRound { RoundNumber = 1, AiContent = "Fleshed out.", CreatedAtUtc = proposal.CreatedAtUtc });
            db.SaveChanges();

            var result = await Build(db).DecideAsync(proposal.Id, FeatureReviewDecision.Accepted, null, "reviewer");

            Assert.Equal(FeatureProposalDecisionStatus.Success, result.Status);
            Assert.Equal(FeatureProposalStatus.Approved, db.FeatureProposals.Single().Status);
        }

        [Fact]
        public async Task DecideAsync_Revised_RequiresAComment()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var submit = await Build(db).SubmitAsync("Title", "Description", "submitter", "submitter@w.local");

            var result = await Build(db).DecideAsync(submit.ProposalId!.Value, FeatureReviewDecision.Revised, "  ", "reviewer");

            Assert.Equal(FeatureProposalDecisionStatus.CommentRequired, result.Status);
            Assert.Equal(FeatureProposalStatus.NeedsReview, db.FeatureProposals.Single().Status);
        }

        [Fact]
        public async Task DecideAsync_Revised_WithACommentSendsItToAiRevision()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var submit = await Build(db).SubmitAsync("Title", "Description", "submitter", "submitter@w.local");

            var result = await Build(db).DecideAsync(submit.ProposalId!.Value, FeatureReviewDecision.Revised, "Make it CSV instead of zip.", "reviewer");

            Assert.Equal(FeatureProposalDecisionStatus.Success, result.Status);
            var proposal = db.FeatureProposals.Include(p => p.Rounds).Single();
            Assert.Equal(FeatureProposalStatus.AwaitingAiRevision, proposal.Status);
            Assert.Equal("Make it CSV instead of zip.", proposal.Rounds.Single().HumanComment);
        }

        [Fact]
        public async Task DecideAsync_Denied_IsTerminal()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var submit = await Build(db).SubmitAsync("Title", "Description", "submitter", "submitter@w.local");
            await Build(db).DecideAsync(submit.ProposalId!.Value, FeatureReviewDecision.Denied, null, "reviewer");

            var result = await Build(db).DecideAsync(submit.ProposalId!.Value, FeatureReviewDecision.Accepted, null, "reviewer");

            Assert.Equal(FeatureProposalDecisionStatus.InvalidState, result.Status);
            Assert.Equal(FeatureProposalStatus.Denied, db.FeatureProposals.Single().Status);
        }

        [Fact]
        public async Task DecideAsync_ReturnsNotFoundForUnknownProposal()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var result = await Build(db).DecideAsync(999, FeatureReviewDecision.Accepted, null, "reviewer");

            Assert.Equal(FeatureProposalDecisionStatus.NotFound, result.Status);
        }

        [Fact]
        public async Task DecideAsync_RejectsADecisionWhileAwaitingAiRevision()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var submit = await Build(db).SubmitAsync("Title", "Description", "submitter", "submitter@w.local");
            await Build(db).DecideAsync(submit.ProposalId!.Value, FeatureReviewDecision.Accepted, null, "reviewer");

            var result = await Build(db).DecideAsync(submit.ProposalId!.Value, FeatureReviewDecision.Accepted, null, "reviewer");

            Assert.Equal(FeatureProposalDecisionStatus.InvalidState, result.Status);
        }

        [Fact]
        public async Task DecideAsync_DenyingWhileAwaitingAiRevision_IsAlwaysAllowed()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var submit = await Build(db).SubmitAsync("Title", "Description", "submitter", "submitter@w.local");
            await Build(db).DecideAsync(submit.ProposalId!.Value, FeatureReviewDecision.Accepted, null, "reviewer");

            var result = await Build(db).DecideAsync(submit.ProposalId!.Value, FeatureReviewDecision.Denied, null, "reviewer");

            Assert.Equal(FeatureProposalDecisionStatus.Success, result.Status);
            Assert.Equal(FeatureProposalStatus.Denied, db.FeatureProposals.Single().Status);
        }

        [Fact]
        public async Task DecideAsync_DenyingWhileAwaitingAiRevision_DoesNotOverwriteTheRoundThatGotItThere()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var submit = await Build(db).SubmitAsync("Title", "Description", "submitter", "submitter@w.local");
            await Build(db).DecideAsync(submit.ProposalId!.Value, FeatureReviewDecision.Revised, "Change the flow.", "reviewer");

            await Build(db).DecideAsync(submit.ProposalId!.Value, FeatureReviewDecision.Denied, null, "someone-else");

            var round0 = db.FeatureProposals.Include(p => p.Rounds).Single().Rounds.Single();
            Assert.Equal(FeatureReviewDecision.Revised, round0.HumanDecision);
            Assert.Equal("Change the flow.", round0.HumanComment);
        }

        [Fact]
        public async Task DecideAsync_RevisingWhileAwaitingAiRevision_IsStillRejected()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var submit = await Build(db).SubmitAsync("Title", "Description", "submitter", "submitter@w.local");
            await Build(db).DecideAsync(submit.ProposalId!.Value, FeatureReviewDecision.Accepted, null, "reviewer");

            var result = await Build(db).DecideAsync(submit.ProposalId!.Value, FeatureReviewDecision.Revised, "More detail please.", "reviewer");

            Assert.Equal(FeatureProposalDecisionStatus.InvalidState, result.Status);
        }

        [Fact]
        public async Task ListAwaitingAiRevisionAsync_OnlyReturnsThatStatus()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var submit = await Build(db).SubmitAsync("Title", "Description", "submitter", "submitter@w.local");
            await Build(db).SubmitAsync("Other", "Untouched", "submitter", "submitter@w.local");
            await Build(db).DecideAsync(submit.ProposalId!.Value, FeatureReviewDecision.Accepted, null, "reviewer");

            var pending = await Build(db).ListAwaitingAiRevisionAsync();

            var pendingProposal = Assert.Single(pending);
            Assert.Equal(submit.ProposalId, pendingProposal.Id);
        }

        [Fact]
        public async Task RecordAiRevisionAsync_NotReadyForFinal_LandsOnNeedsReview()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var submit = await Build(db).SubmitAsync("Title", "Description", "submitter", "submitter@w.local");
            await Build(db).DecideAsync(submit.ProposalId!.Value, FeatureReviewDecision.Accepted, null, "reviewer");

            var result = await Build(db).RecordAiRevisionAsync(submit.ProposalId!.Value, "A fuller write-up.", readyForFinalApproval: false);

            Assert.Equal(FeatureProposalDecisionStatus.Success, result.Status);
            var proposal = db.FeatureProposals.Include(p => p.Rounds).Single();
            Assert.Equal(FeatureProposalStatus.NeedsReview, proposal.Status);
            var newRound = proposal.Rounds.Single(r => r.RoundNumber == 1);
            Assert.Equal("A fuller write-up.", newRound.AiContent);
        }

        [Fact]
        public async Task RecordAiRevisionAsync_ReadyForFinal_LandsOnReadyForFinalApproval()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var submit = await Build(db).SubmitAsync("Title", "Description", "submitter", "submitter@w.local");
            await Build(db).DecideAsync(submit.ProposalId!.Value, FeatureReviewDecision.Accepted, null, "reviewer");

            var result = await Build(db).RecordAiRevisionAsync(submit.ProposalId!.Value, "Fully specified.", readyForFinalApproval: true);

            Assert.Equal(FeatureProposalDecisionStatus.Success, result.Status);
            Assert.Equal(FeatureProposalStatus.ReadyForFinalApproval, db.FeatureProposals.Single().Status);
        }

        [Fact]
        public async Task RecordAiRevisionAsync_RejectsAProposalNotAwaitingRevision()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var submit = await Build(db).SubmitAsync("Title", "Description", "submitter", "submitter@w.local");

            var result = await Build(db).RecordAiRevisionAsync(submit.ProposalId!.Value, "Too early.", readyForFinalApproval: false);

            Assert.Equal(FeatureProposalDecisionStatus.InvalidState, result.Status);
        }

        [Fact]
        public async Task ListApprovedUnqueuedAsync_OnlyReturnsApprovedAndUnqueued()
        {
            using var db = TestDbContextFactory.CreateInMemory();
            var submit = await Build(db).SubmitAsync("Title", "Description", "submitter", "submitter@w.local");
            var proposal = db.FeatureProposals.Single();
            proposal.Status = FeatureProposalStatus.Approved;
            db.SaveChanges();

            var approved = await Build(db).ListApprovedUnqueuedAsync();
            Assert.Single(approved);

            await Build(db).MarkQueuedForBuildAsync(proposal.Id);

            Assert.Empty(await Build(db).ListApprovedUnqueuedAsync());
        }

        [Fact]
        public async Task MarkQueuedForBuildAsync_ReturnsNotFoundForUnknownProposal()
        {
            using var db = TestDbContextFactory.CreateInMemory();

            var result = await Build(db).MarkQueuedForBuildAsync(999);

            Assert.Equal(FeatureProposalDecisionStatus.NotFound, result.Status);
        }
    }
}
