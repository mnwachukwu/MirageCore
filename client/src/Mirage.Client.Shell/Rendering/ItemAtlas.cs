using Microsoft.Xna.Framework;
using Mirage.Shared;

namespace Mirage.Client.Shell.Rendering;

/// <summary>
/// Maps a 0-based item pic number to a source rectangle in its item sheet.
/// Layout: a vertical strip, each item 32 pixels tall. Pic=0 is the first graphic of whichever
/// sheet the item names, so a pic number is a row within one sheet rather than across all of them.
/// </summary>
public static class ItemAtlas
{
    public static Rectangle GetSourceRect(short pic)
    {
        if (pic < 0) return Rectangle.Empty;
        return new Rectangle(0, pic * Constants.PicY, Constants.PicX, Constants.PicY);
    }
}
