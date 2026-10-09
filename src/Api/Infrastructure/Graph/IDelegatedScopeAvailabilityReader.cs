namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public enum ScopeAvailability
{
    Available,
    MissingConsent,
    Unknown
}

public sealed record DelegatedScopeResult(string Scope, ScopeAvailability Status, string? ProblemCategory);

public interface IDelegatedScopeAvailabilityReader
{
    Task<IReadOnlyCollection<DelegatedScopeResult>> ReadAsync(
        IReadOnlyCollection<string> scopes,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<DelegatedScopeResult>> ReadPartialAsync(
        IReadOnlyCollection<string> scopes,
        CancellationToken cancellationToken = default) =>
        ReadAsync(scopes, cancellationToken);
}
