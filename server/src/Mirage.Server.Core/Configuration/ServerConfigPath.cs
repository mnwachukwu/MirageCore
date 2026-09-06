namespace Mirage.Server.Core.Configuration;

/// <summary>Which serverconfig.json this server read.
///
/// <para>🔴 NOT <see cref="ServerConfigStore.DefaultPath"/>. A server started with <c>--config</c> reads a
/// different file — the load benchmark runs a second server that way, and so does any scratch instance —
/// so a setting written back to the default lands in the real installation's config instead. The symptom
/// is a scratch run's ports turning up in a server nobody touched.</para>
///
/// <para>A named record rather than a bare string, so the container cannot hand it to something expecting
/// a different one.</para></summary>
public sealed record ServerConfigPath(string Path);
