using System.Reflection;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Identity.Web;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Graph;

public sealed class MicrosoftIdentityGraphTokenProviderTests
{
    [Fact]
    public async Task Forwards_cancellation_to_Microsoft_Identity_token_acquisition()
    {
        var acquisition = DispatchProxy.Create<ITokenAcquisition, RecordingTokenAcquisition>();
        var recorder = (RecordingTokenAcquisition)(object)acquisition;
        var provider = new MicrosoftIdentityGraphTokenProvider(acquisition);
        using var cancellation = new CancellationTokenSource();

        var acquisitionTask = provider.GetAccessTokenForCurrentUserAsync(["User.Read"], cancellation.Token);

        recorder.Arguments!.OfType<TokenAcquisitionOptions>()
            .Single()
            .CancellationToken
            .Should()
            .Be(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acquisitionTask);
    }

    public class RecordingTokenAcquisition : DispatchProxy
    {
        public object?[]? Arguments { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Arguments = args;
            var options = args?.OfType<TokenAcquisitionOptions>().SingleOrDefault();
            return options is null ? Task.FromResult("access-token") : WaitForCancellationAsync(options.CancellationToken);
        }

        private static async Task<string> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return "access-token";
        }
    }
}
