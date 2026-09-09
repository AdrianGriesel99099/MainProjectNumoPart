using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

// Two independent modes, run as separate steps around the "Feature review" cloud routine (see
// CLAUDE.md's "Feature proposals & review" section) -- neither one does any reasoning, and neither
// needs an AI API key, since the routine itself already IS Claude. This tool exists only because a
// cloud routine's sandbox has no network path to the production site at all (confirmed via a
// direct test: outbound HTTPS to it is rejected by the sandbox's own egress policy), so getting
// data to and from the routine has to go through the one channel it DOES have: its git checkout.
//
//   prepare (runs shortly before the routine): fetches proposals awaiting a revision from the
//     site's API and writes one JSON file per proposal under FeatureReviewQueue/pending/ -- pure
//     data the routine reads directly out of its own checkout, no network call from the routine
//     required. Also relays already-approved proposals into docs/BACKLOG.md directly (no
//     reasoning needed for that step, so it doesn't need to wait for the routine at all).
//
//   apply (runs shortly after the routine): reads whatever the routine wrote under
//     FeatureReviewQueue/drafted/ and POSTs each one back to the site, deleting the file once
//     relayed. A file left behind (a POST failure, or the routine never got to it) is retried
//     automatically next cycle -- prepare/the routine/apply all just re-derive state from what's
//     actually there each time, nothing here assumes yesterday's run succeeded.
if (args.Length != 1 || (args[0] != "prepare" && args[0] != "apply"))
{
    Console.Error.WriteLine("Usage: FeatureReviewRelay <prepare|apply>");
    Environment.Exit(1);
    return;
}

var siteBaseUrl = Environment.GetEnvironmentVariable("FEATURE_REVIEW_SITE_BASE_URL")
    ?? throw new InvalidOperationException("FEATURE_REVIEW_SITE_BASE_URL is not set.");
var siteApiKey = Environment.GetEnvironmentVariable("FEATURE_REVIEW_SITE_API_KEY")
    ?? throw new InvalidOperationException("FEATURE_REVIEW_SITE_API_KEY is not set.");
var repoRoot = Environment.GetEnvironmentVariable("FEATURE_REVIEW_REPO_ROOT") ?? Directory.GetCurrentDirectory();

using var site = new HttpClient { BaseAddress = new Uri(siteBaseUrl) };
site.DefaultRequestHeaders.Add("X-Api-Key", siteApiKey);

var queueRoot = Path.Combine(repoRoot, "FeatureReviewQueue");
var pendingDir = Path.Combine(queueRoot, "pending");
var draftedDir = Path.Combine(queueRoot, "drafted");
Directory.CreateDirectory(pendingDir);
Directory.CreateDirectory(draftedDir);

if (args[0] == "prepare")
{
    await PrepareAsync(site, repoRoot, pendingDir);
}
else
{
    await ApplyAsync(site, draftedDir);
}

static async Task PrepareAsync(HttpClient site, string repoRoot, string pendingDir)
{
    var pending = await site.GetFromJsonAsync<List<BotProposal>>("api/bot/feature-proposals/awaiting-ai-revision")
        ?? new List<BotProposal>();
    Console.WriteLine($"{pending.Count} proposal(s) awaiting revision -- writing to FeatureReviewQueue/pending/.");

    // A full refresh, not an accumulate-forever pattern: a proposal that left AwaitingAiRevision
    // since the last run (already revised, or the apply step already relayed it) has nothing
    // pending any more, so its old file here would otherwise linger and get redrafted for no
    // reason -- the routine's next run has to see exactly today's true state, not yesterday's.
    foreach (var stale in Directory.GetFiles(pendingDir, "*.json"))
    {
        File.Delete(stale);
    }

    foreach (var proposal in pending)
    {
        var path = Path.Combine(pendingDir, $"{proposal.Id}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(proposal));
    }

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

static async Task ApplyAsync(HttpClient site, string draftedDir)
{
    var files = Directory.GetFiles(draftedDir, "*.json");
    Console.WriteLine($"{files.Length} drafted revision(s) to relay.");

    foreach (var file in files)
    {
        var id = Path.GetFileNameWithoutExtension(file);
        try
        {
            var draft = JsonSerializer.Deserialize<DraftedRevision>(await File.ReadAllTextAsync(file))
                ?? throw new InvalidOperationException("Empty or invalid draft file.");

            var response = await site.PostAsJsonAsync(
                $"api/bot/feature-proposals/{id}/revision",
                new
                {
                    revisedDescription = draft.RevisedDescription,
                    readyForFinalApproval = draft.ReadyForFinalApproval,
                    questions = draft.Questions?.Select(q => new { prompt = q.Prompt, options = q.Options })
                });
            response.EnsureSuccessStatusCode();

            File.Delete(file);
            Console.WriteLine($"Proposal {id}: revision relayed, ready-for-final={draft.ReadyForFinalApproval}.");
        }
        catch (Exception ex)
        {
            // Left in place -- retried on tomorrow's cycle rather than losing the drafted text.
            Console.WriteLine($"Proposal {id}: relay failed, will retry tomorrow. {ex.Message}");
        }
    }
}

static string SingleLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ').Trim();

record BotOptionView(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("label")] string Label);

record BotQuestionView(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("prompt")] string Prompt,
    [property: JsonPropertyName("options")] List<BotOptionView> Options,
    [property: JsonPropertyName("selectedOption")] string? SelectedOption);

record BotRound(
    [property: JsonPropertyName("roundNumber")] int RoundNumber,
    [property: JsonPropertyName("aiContent")] string? AiContent,
    [property: JsonPropertyName("humanDecision")] string? HumanDecision,
    [property: JsonPropertyName("humanComment")] string? HumanComment,
    [property: JsonPropertyName("questions")] List<BotQuestionView> Questions);

record BotProposal(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("rounds")] List<BotRound> Rounds);

record BotApprovedProposal(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description);

// The shape the routine itself writes under FeatureReviewQueue/drafted/{id}.json. Questions is
// optional -- most rounds won't have any (see CLAUDE.md: only drafted when there's a genuine
// fork worth putting to the reviewer directly).
record DraftedQuestion(
    [property: JsonPropertyName("prompt")] string Prompt,
    [property: JsonPropertyName("options")] List<string> Options);

record DraftedRevision(
    [property: JsonPropertyName("revisedDescription")] string RevisedDescription,
    [property: JsonPropertyName("readyForFinalApproval")] bool ReadyForFinalApproval,
    [property: JsonPropertyName("questions")] List<DraftedQuestion>? Questions = null);
