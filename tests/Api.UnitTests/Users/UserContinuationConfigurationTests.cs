using Atea.UnifiedWorkplace.Api.Features.Users;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Users;

public sealed class UserContinuationConfigurationTests
{
    [Fact]
    public void Production_requires_a_configured_continuation_signing_key()
    {
        var configuration = new ConfigurationBuilder().Build();

        var act = () => UserContinuationConfiguration.ResolveSigningKey(configuration, isDevelopment: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Users:ContinuationSigningKey must be configured outside Development.");
    }

    [Fact]
    public void Development_may_use_the_ephemeral_key_fallback()
    {
        var configuration = new ConfigurationBuilder().Build();

        UserContinuationConfiguration.ResolveSigningKey(configuration, isDevelopment: true).Should().BeNull();
    }
}
