namespace ClinicalAgent.Core.Interfaces;

/// <summary>Abstracts Azure Blob Storage operations. Implementations live in ClinicalAgent.Plugins.</summary>
public interface IBlobStorageService
{
    /// <summary>Uploads a stream to the given container and blob path, returning the resolved blob path.</summary>
    Task<Result<string>> UploadAsync(
        Stream content,
        string containerName,
        string blobPath,
        string contentType,
        CancellationToken ct = default);

    /// <summary>Downloads a blob as a stream.</summary>
    Task<Result<Stream>> DownloadAsync(
        string containerName,
        string blobPath,
        CancellationToken ct = default);

    /// <summary>Generates a time-limited SAS URL for a blob.</summary>
    Task<Result<Uri>> GenerateSasUrlAsync(
        string containerName,
        string blobPath,
        TimeSpan expiry,
        CancellationToken ct = default);

    /// <summary>Lists blob paths in a container under an optional prefix.</summary>
    Task<IReadOnlyList<string>> ListBlobsAsync(
        string containerName,
        string prefix,
        CancellationToken ct = default);
}
