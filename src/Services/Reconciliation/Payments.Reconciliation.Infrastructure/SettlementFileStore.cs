using System.Buffers;
using System.Security.Cryptography;

namespace Payments.Reconciliation.Infrastructure;

public sealed record StagedSettlementFile(string TemporaryPath, string Hash, long Length);

public interface ISettlementFileStore
{
    Task<StagedSettlementFile> StageAsync(Stream source, long maxBytes, CancellationToken cancellationToken);
    Task CommitAsync(StagedSettlementFile staged, string provider, CancellationToken cancellationToken);
    Stream OpenRead(string provider, string hash);
    void Discard(StagedSettlementFile staged);
}

public sealed class LocalSettlementFileStore : ISettlementFileStore
{
    private readonly string _root;
    public LocalSettlementFileStore(string root) { _root = Path.GetFullPath(root); Directory.CreateDirectory(_root); }

    public async Task<StagedSettlementFile> StageAsync(Stream source, long maxBytes, CancellationToken ct)
    {
        var path = Path.Combine(_root, $"stage-{Guid.NewGuid():N}.tmp");
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        long length = 0;
        try
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
            {
                length += read;
                if (length > maxBytes) throw new InvalidDataException("Settlement file exceeds configured maximum size.");
                hasher.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }
            await output.FlushAsync(ct).ConfigureAwait(false);
            return new StagedSettlementFile(path, Convert.ToHexString(hasher.GetHashAndReset()), length);
        }
        catch { File.Delete(path); throw; }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    public Task CommitAsync(StagedSettlementFile staged, string provider, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = PathFor(provider, staged.Hash);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try { File.Move(staged.TemporaryPath, target); }
        catch (IOException) when (File.Exists(target)) { Discard(staged); }
        return Task.CompletedTask;
    }

    public Stream OpenRead(string provider, string hash) => new FileStream(PathFor(provider, hash), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
    public void Discard(StagedSettlementFile staged) { if (File.Exists(staged.TemporaryPath)) File.Delete(staged.TemporaryPath); }

    private string PathFor(string provider, string hash)
    {
        if (provider.Length is < 1 or > 64 || provider.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new InvalidDataException("Invalid provider name.");
        if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Invalid settlement hash.");
        return Path.Combine(_root, provider, hash + ".csv");
    }
}
