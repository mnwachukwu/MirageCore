using Mirage.Client.Shell.Localization;
using NUnit.Framework;

namespace Mirage.Client.Shell.Tests;

/// <summary>Assembly-wide one-time setup: loads <see cref="ClientStrings"/> ONCE, before any fixture in this
/// namespace runs, so a test whose code-under-test resolves a localized string works regardless of run order
/// or test filtering.
///
/// <para>In DEBUG, <c>ClientStrings.Get</c>/<c>.Format</c> THROW on a missing/unloaded key (RELEASE returns a
/// bracketed fallback and never throws). So before this fixture existed, a test that reached that path without
/// its own fixture having loaded strings failed <b>order-dependently</b> — only when it ran before any
/// string-loading fixture, e.g. run in isolation or filtered. This SetUpFixture removes that whole class of
/// failure: individual fixtures need not — and should not — load strings themselves.</para>
///
/// <para>A namespace-scoped <c>[SetUpFixture]</c> runs its OneTimeSetUp once before every fixture in the
/// namespace it is declared in (and descendants); all shell tests live in <c>Mirage.Client.Shell.Tests</c>.</para></summary>
[SetUpFixture]
public sealed class StringsSetUpFixture
{
    [OneTimeSetUp]
    public void LoadStrings() => ClientStrings.Load(Path.Combine(AppContext.BaseDirectory, "lang"));
}
