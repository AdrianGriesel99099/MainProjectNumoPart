namespace MainProjectNumoPart.Authorization
{
    // Gates the bot-only /api/bot/feature-proposals/* routes (Endpoints/FeatureProposalEndpoints.cs).
    // Deliberately NOT the cookie/role-based auth the rest of the app uses -- Tools/FeatureReviewBot
    // is a script with no user to sign in as, so it authenticates with a single shared secret
    // instead (FeatureReviewBot:ApiKey, supplied via a Container App secret in production; see
    // CLAUDE.md's "Feature proposals & review" section). An unconfigured key (local dev, by
    // default) rejects every request rather than silently allowing them.
    public class ApiKeyEndpointFilter : IEndpointFilter
    {
        private readonly IConfiguration _config;

        public ApiKeyEndpointFilter(IConfiguration config)
        {
            _config = config;
        }

        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var expected = _config["FeatureReviewBot:ApiKey"];
            var provided = context.HttpContext.Request.Headers["X-Api-Key"].FirstOrDefault();

            if (string.IsNullOrEmpty(expected) || provided != expected)
            {
                return Results.Unauthorized();
            }

            return await next(context);
        }
    }
}
