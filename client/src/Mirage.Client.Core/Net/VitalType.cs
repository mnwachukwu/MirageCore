namespace Mirage.Client.Core.Net;

/// <summary>Which vital a change refers to, for floating combat text and bar updates.
/// <c>Exp</c> is included because experience gains use the same float-text path.</summary>
public enum VitalType { Hp, Mp, Sp, Exp }
