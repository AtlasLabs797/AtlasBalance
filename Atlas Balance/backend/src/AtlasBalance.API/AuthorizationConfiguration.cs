using Microsoft.AspNetCore.Authorization;

namespace AtlasBalance.API;

public static class AuthorizationConfiguration
{
    public static void Configure(AuthorizationOptions options)
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    }
}
