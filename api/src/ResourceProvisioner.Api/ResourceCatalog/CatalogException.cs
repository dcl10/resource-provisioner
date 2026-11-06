namespace ResourceProvisioner.Api.ResourceCatalog;

/// <summary>The catalog couldn't be loaded: a missing folder, a missing file or an invalid manifest.</summary>
/// <param name="message">What's wrong, naming the file and each problem.</param>
public class CatalogException(string message) : Exception(message);
