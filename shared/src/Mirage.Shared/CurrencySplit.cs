namespace Mirage.Shared;

/// <summary>
/// Dividing an amount of currency between several recipients.
///
/// <para>Returns one amount per recipient, in the order they were given. This decides the SHAPE of the
/// split and nothing about who stands where — the caller orders the recipients, and keeping the
/// arithmetic separate from that draw is what makes the arithmetic testable.</para>
///
/// <para><b>The remainder rule:</b> an even share each, then the leftover one apiece to whoever comes
/// first. Currency is integral, so three recipients splitting ten must either lose a unit or invent
/// one; the odd one goes to whoever the caller put first, which is why the caller decides that order
/// deliberately rather than by arrival.</para>
///
/// <para>It degrades correctly when the purse is smaller than the group: three among four pays three of
/// them one each and the fourth nothing, rather than rounding everybody to zero. A zero share is the
/// caller's to skip.</para>
///
/// <para><b>It conserves the total exactly</b> — nothing is created and nothing is destroyed. That is
/// the property worth pinning, because this is the only place in the engine that divides currency.</para>
/// </summary>
public static class CurrencySplit
{
    /// <summary>Divides <paramref name="total"/> between <paramref name="recipients"/>.</summary>
    public static int[] Divide(int total, int recipients)
    {
        if (recipients <= 0) return [];
        if (total <= 0) return new int[recipients];

        int share = total / recipients;
        int leftover = total % recipients;

        var shares = new int[recipients];
        for (int i = 0; i < recipients; i++) shares[i] = share + (i < leftover ? 1 : 0);
        return shares;
    }
}
