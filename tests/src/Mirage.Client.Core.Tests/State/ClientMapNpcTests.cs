using Mirage.Client.Core.State;
using Mirage.Shared;
using NUnit.Framework;

namespace Mirage.Client.Core.Tests.State;

/// <summary>ClientMapNpc.ApplySnapshot: a same-NPC-same-tile snapshot is a mid-step re-sync (returns true, and
/// must NOT reset the in-flight walk/attack interpolation, so a seam-crossing re-sync doesn't snap the slide);
/// any position/identity change is a real update that resets the interpolation.</summary>
[TestFixture]
public class ClientMapNpcTests
{
}
