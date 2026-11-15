namespace CLMS.Application.Identity.Services;

/// <summary>Credentials are correct, but ordinary access tokens must not be issued yet.</summary>
public sealed class PasswordChangeRequiredException() : Exception("Change your initial password before using Labster.");
