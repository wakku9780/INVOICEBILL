namespace SareeBilling.Infrastructure.Updates;

/// <summary>
/// Where releases are published. The repository must be public and should contain only
/// installers and release notes (no source code), so the app needs no access token.
/// </summary>
/// <param name="Owner">GitHub user or organisation, e.g. "wakku9780".</param>
/// <param name="Repository">Public releases repository, e.g. "SareeBilling-Releases".</param>
/// <param name="CurrentVersion">Version of the running app.</param>
public sealed record UpdateSettings(string Owner, string Repository, Version CurrentVersion);
