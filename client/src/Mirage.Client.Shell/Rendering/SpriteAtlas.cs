using Microsoft.Xna.Framework;
using Mirage.Shared;

namespace Mirage.Client.Shell.Rendering;

/// <summary>
/// Maps (spriteNum, dir, animFrame) to a source rectangle in its sprite sheet.
/// Layout: sprite number maps directly to atlas row (sprite 0 = row 0, sprite 1 = row 1, etc.),
/// counted within one sheet - which sheet is the record's to name.
/// animFrame: 0 = idle, 1 = walk, 2 = attack.
/// </summary>
public static class SpriteAtlas
{
    public static Rectangle GetSourceRect(int spriteNum, Direction dir, int animFrame)
        => GetSourceRect(spriteNum, dir, animFrame, Constants.PicX);

    /// <summary>Size-aware overload for variable-size NPC sheets: <paramref name="cell"/> is the square
    /// cell size in px (32/64/96 for size class 1/2/3). Same row/column math, just a larger cell so the
    /// atlas layout is identical at every size.</summary>
    public static Rectangle GetSourceRect(int spriteNum, Direction dir, int animFrame, int cell)
    {
        int frame = (int)dir * 3 + animFrame;
        return new Rectangle(frame * cell, spriteNum * cell, cell, cell);
    }
}
