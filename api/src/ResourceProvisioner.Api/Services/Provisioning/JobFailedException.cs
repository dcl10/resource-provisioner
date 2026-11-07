namespace ResourceProvisioner.Api.Services.Provisioning;

/// <summary>A job step failed in a way the user should read about in the log; the request moves to <c>Failed</c>.</summary>
/// <param name="message">What went wrong, in words the user can act on. Never includes a secret.</param>
public class JobFailedException(string message) : Exception(message);
