using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MainProjectNumoPart.Data;
using MainProjectNumoPart.Models;
using Microsoft.EntityFrameworkCore;

// Run on a schedule by .github/workflows/feature-review.yml, one hour before the cloud "New
// feature" routine. Two independent passes over the FeatureProposal table (see
// CLAUDE.md's "Feature proposals & review" section for the full state machine):
//
//   1. Every proposal AwaitingAiRevision gets one more round: Claude reads the history and either
//      writes a fuller proposal (still needs another human look) or decides it's fully specified
//      (ReadyForFinalApproval).
//   2. Every proposal that's Approved and not yet queued gets a line appended to docs/BACKLOG.md,
//      which the "New feature" routine already reads first -- this is the only bridge back to the
//      git-only cloud routines, since neither of them ever talks to this database directly.
//
// A failure on one proposal (a bad AI response, a transient API error) is logged and skipped
// rather than aborting the run -- it just tries again tomorrow, same spirit as the photo backup
// workflow being additive/idempotent.

var connectionString = Environment.GetEnvironmentVariable("FEATURE_REVIEW_SQL_CONNECTION_STRING")
    ?? throw new InvalidOperationException("FEATURE_REVIEW_SQL_CONNECTION_STRING is not set.");
var anthropicApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
    ?? throw new InvalidOperationException("ANTHROPIC_API_KEY is not set.");
var repoRoot = Environment.GetEnvironmentVariable("FEATURE_REVIEW_REPO_ROOT") ?? Directory.GetCurrentDirectory();

var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer(connectionString)
    .Options;

using var db = new AppDbContext(options);
using var http = new HttpClient { BaseAddress = new Uri("https://api.anthropic.com/") };
http.DefaultRequestHeaders.Add("x-api-key", anthropicApiKey);
http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

await RevisePendingProposalsAsync(db, http);
await QueueApprovedProposalsAsync(db, repoRoot);

static async Task RevisePendingProposalsAsync(AppDbContext db, HttpClient http)
{
    var pending = await db.FeatureProposals
        .Include(p => p.Rounds)
        .Where(p => p.Status == FeatureProposalStatus.AwaitingAiRevision)
        .ToListAsync();

    Console.WriteLine($"{pending.Count} proposal(s) awaiting AI revision.");

    foreach (var proposal in pending)
    {
        try
        {
            var revision = await DraftRevisionAsync(http, proposal);
            var nextRound = proposal.Rounds.Max(r => r.RoundNumber) + 1;

            proposal.Rounds.Add(new FeatureProposalRound
            {
                RoundNumber = nextRound,
                AiContent = revision.RevisedDescription,
                CreatedAtUtc = DateTime.UtcNow
            });
            proposal.Status = revision.ReadyForFinalApproval
                ? FeatureProposalStatus.ReadyForFinalApproval
                : FeatureProposalStatus.NeedsReview;

            await db.SaveChangesAsync();
            Console.WriteLine($"Proposal {proposal.Id} \"{proposal.Title}\": round {nextRound} drafted, ready-for-final={revision.ReadyForFinalApproval}.");
        }
        catch (Exception ex)
        {
            // Left in AwaitingAiRevision -- picked up again on tomorrow's run rather than losing
            // the human's "continue"/"revise" decision that got it here.
            Console.WriteLine($"Proposal {proposal.Id} \"{proposal.Title}\": revision failed, will retry tomorrow. {ex.Message}");
        }
    }
}

static async Task QueueApprovedProposalsAsync(AppDbContext db, string repoRoot)
{
    var approved = await db.FeatureProposals
        .Where(p => p.Status == FeatureProposalStatus.Approved && p.QueuedForBuildAtUtc == null)
        .ToListAsync();

    Console.WriteLine($"{approved.Count} approved proposal(s) not yet queued.");
    if (approved.Count == 0) return;

    var backlogPath = Path.Combine(repoRoot, "docs", "BACKLOG.md");
    var lines = approved.Select(p => $"Build: {p.Title} (proposal #{p.Id}): {SingleLine(p.Description)}");
    await File.AppendAllLinesAsync(backlogPath, lines);

    foreach (var proposal in approved)
    {
        proposal.QueuedForBuildAtUtc = DateTime.UtcNow;
    }
    await db.SaveChangesAsync();

    foreach (var proposal in approved)
    {
        Console.WriteLine($"Proposal {proposal.Id} \"{proposal.Title}\": queued in docs/BACKLOG.md.");
    }
}

static string SingleLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ').Trim();

static async Task<AiRevision> DraftRevisionAsync(HttpClient http, FeatureProposal proposal)
{
    var history = new StringBuilder();
    history.AppendLine($"Original submission: {proposal.Description}");
    foreach (var round in proposal.Rounds.OrderBy(r => r.RoundNumber))
    {
        if (round.AiContent is not null)
        {
            history.AppendLine($"Round {round.RoundNumber} (Claude's write-up): {round.AiContent}");
        }
        if (round.HumanDecision is not null)
        {
            history.AppendLine($"Round {round.RoundNumber} human decision: {round.HumanDecision}"
                + (string.IsNullOrEmpty(round.HumanComment) ? "" : $" -- \"{round.HumanComment}\""));
        }
    }

    // Split at the literal JSON braces rather than doubling them: $$""" would require doubling
    // every OTHER brace in this prompt too, which is easy to get wrong and hard to read.
    var prompt = $"""
        You are drafting a feature proposal for a small ASP.NET Core app that tracks vehicle
        photos for a car workshop. A staff member proposed the idea below; a human reviewer has
        been going back and forth with revisions. Write an improved, more detailed version of the
        proposal that addresses the reviewer's most recent comment (if any), staying realistic for
        a small workshop app. Then decide whether it is now fully specified enough to hand to a
        developer with no more open questions.

        Title: {proposal.Title}

        History:
        {history}

        Respond with ONLY a JSON object, no other text, in this exact shape:
        """ + """
        {"revisedDescription": "...", "readyForFinalApproval": true or false}
        """;

    var body = JsonSerializer.Serialize(new
    {
        model = "claude-sonnet-5",
        max_tokens = 1024,
        messages = new[] { new { role = "user", content = prompt } }
    });

    using var request = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };
    using var response = await http.SendAsync(request);
    response.EnsureSuccessStatusCode();

    var payload = await response.Content.ReadFromJsonAsync<AnthropicResponse>()
        ?? throw new InvalidOperationException("Empty response from the Anthropic API.");
    var text = payload.Content.FirstOrDefault(c => c.Type == "text")?.Text
        ?? throw new InvalidOperationException("No text block in the Anthropic API response.");

    return JsonSerializer.Deserialize<AiRevision>(text)
        ?? throw new InvalidOperationException("Could not parse the AI's revision as JSON.");
}

record AiRevision(
    [property: JsonPropertyName("revisedDescription")] string RevisedDescription,
    [property: JsonPropertyName("readyForFinalApproval")] bool ReadyForFinalApproval);

record AnthropicResponse([property: JsonPropertyName("content")] List<AnthropicContentBlock> Content);
record AnthropicContentBlock([property: JsonPropertyName("type")] string Type, [property: JsonPropertyName("text")] string? Text);
