using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// Run on a schedule by .github/workflows/feature-review.yml, one hour before the cloud "New
// feature" routine. Two independent passes over the site's own /api/bot/feature-proposals
// routes (see CLAUDE.md's "Feature proposals & review" section for the full state machine):
//
//   1. Every proposal AwaitingAiRevision gets one more round: Claude reads the history and either
//      writes a fuller proposal (still needs another human look) or decides it's fully specified
//      (ReadyForFinalApproval).
//   2. Every proposal that's Approved and not yet queued gets a line appended to docs/BACKLOG.md,
//      which the "New feature" routine already reads first -- this is the only bridge back to the
//      git-only cloud routines, since neither of them ever talks to the site's database directly.
//
// This tool has no database access of its own -- it's a plain HTTP client authenticated with a
// shared API key, deliberately, so every read and write to FeatureProposal data flows through the
// same FeatureProposalService the web app itself uses rather than a second, parallel query path
// that could drift from it.
//
// A failure on one proposal (a bad AI response, a transient API error) is logged and skipped
// rather than aborting the run -- it just tries again tomorrow, same spirit as the photo backup
// workflow being additive/idempotent.

var siteBaseUrl = Environment.GetEnvironmentVariable("FEATURE_REVIEW_SITE_BASE_URL")
    ?? throw new InvalidOperationException("FEATURE_REVIEW_SITE_BASE_URL is not set.");
var siteApiKey = Environment.GetEnvironmentVariable("FEATURE_REVIEW_SITE_API_KEY")
    ?? throw new InvalidOperationException("FEATURE_REVIEW_SITE_API_KEY is not set.");
var anthropicApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
    ?? throw new InvalidOperationException("ANTHROPIC_API_KEY is not set.");
var repoRoot = Environment.GetEnvironmentVariable("FEATURE_REVIEW_REPO_ROOT") ?? Directory.GetCurrentDirectory();

using var site = new HttpClient { BaseAddress = new Uri(siteBaseUrl) };
site.DefaultRequestHeaders.Add("X-Api-Key", siteApiKey);

using var anthropic = new HttpClient { BaseAddress = new Uri("https://api.anthropic.com/") };
anthropic.DefaultRequestHeaders.Add("x-api-key", anthropicApiKey);
anthropic.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

await RevisePendingProposalsAsync(site, anthropic);
await QueueApprovedProposalsAsync(site, repoRoot);

static async Task RevisePendingProposalsAsync(HttpClient site, HttpClient anthropic)
{
    var pending = await site.GetFromJsonAsync<List<BotProposal>>("api/bot/feature-proposals/awaiting-ai-revision")
        ?? new List<BotProposal>();

    Console.WriteLine($"{pending.Count} proposal(s) awaiting AI revision.");

    foreach (var proposal in pending)
    {
        try
        {
            var revision = await DraftRevisionAsync(anthropic, proposal);

            var response = await site.PostAsJsonAsync(
                $"api/bot/feature-proposals/{proposal.Id}/revision",
                new { revisedDescription = revision.RevisedDescription, readyForFinalApproval = revision.ReadyForFinalApproval });
            response.EnsureSuccessStatusCode();

            Console.WriteLine($"Proposal {proposal.Id} \"{proposal.Title}\": revision drafted, ready-for-final={revision.ReadyForFinalApproval}.");
        }
        catch (Exception ex)
        {
            // Left AwaitingAiRevision on the site -- picked up again on tomorrow's run rather
            // than losing the human's "continue"/"revise" decision that got it here.
            Console.WriteLine($"Proposal {proposal.Id} \"{proposal.Title}\": revision failed, will retry tomorrow. {ex.Message}");
        }
    }
}

static async Task QueueApprovedProposalsAsync(HttpClient site, string repoRoot)
{
    var approved = await site.GetFromJsonAsync<List<BotApprovedProposal>>("api/bot/feature-proposals/approved-unqueued")
        ?? new List<BotApprovedProposal>();

    Console.WriteLine($"{approved.Count} approved proposal(s) not yet queued.");
    if (approved.Count == 0) return;

    var backlogPath = Path.Combine(repoRoot, "docs", "BACKLOG.md");
    var lines = approved.Select(p => $"Build: {p.Title} (proposal #{p.Id}): {SingleLine(p.Description)}");
    await File.AppendAllLinesAsync(backlogPath, lines);

    foreach (var proposal in approved)
    {
        var response = await site.PostAsync($"api/bot/feature-proposals/{proposal.Id}/mark-queued", content: null);
        response.EnsureSuccessStatusCode();
        Console.WriteLine($"Proposal {proposal.Id} \"{proposal.Title}\": queued in docs/BACKLOG.md.");
    }
}

static string SingleLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ').Trim();

static async Task<AiRevision> DraftRevisionAsync(HttpClient anthropic, BotProposal proposal)
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
    using var response = await anthropic.SendAsync(request);
    response.EnsureSuccessStatusCode();

    var payload = await response.Content.ReadFromJsonAsync<AnthropicResponse>()
        ?? throw new InvalidOperationException("Empty response from the Anthropic API.");
    var text = payload.Content.FirstOrDefault(c => c.Type == "text")?.Text
        ?? throw new InvalidOperationException("No text block in the Anthropic API response.");

    return JsonSerializer.Deserialize<AiRevision>(text)
        ?? throw new InvalidOperationException("Could not parse the AI's revision as JSON.");
}

record BotRound(
    [property: JsonPropertyName("roundNumber")] int RoundNumber,
    [property: JsonPropertyName("aiContent")] string? AiContent,
    [property: JsonPropertyName("humanDecision")] string? HumanDecision,
    [property: JsonPropertyName("humanComment")] string? HumanComment);

record BotProposal(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("rounds")] List<BotRound> Rounds);

record BotApprovedProposal(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description);

record AiRevision(
    [property: JsonPropertyName("revisedDescription")] string RevisedDescription,
    [property: JsonPropertyName("readyForFinalApproval")] bool ReadyForFinalApproval);

record AnthropicResponse([property: JsonPropertyName("content")] List<AnthropicContentBlock> Content);
record AnthropicContentBlock([property: JsonPropertyName("type")] string Type, [property: JsonPropertyName("text")] string? Text);
