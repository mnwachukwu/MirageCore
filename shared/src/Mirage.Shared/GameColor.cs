namespace Mirage.Shared;

public static class GameColor
{
    // ── Original 16-color CGA/EGA palette — QBColor() indices, PRESERVED verbatim ──
    // These 16 keep their exact QBColor RGB (see Rgb[] below) even where no longer referenced, so the
    // authentic palette stays intact. New chat-overhaul colors are APPENDED (16+), never overwritten here.
    public const int Black = 0;
    public const int Blue = 1;
    public const int Green = 2;
    public const int Cyan = 3;
    public const int Red = 4;
    public const int Magenta = 5;
    public const int Brown = 6;
    public const int Gray = 7;
    public const int DarkGray = 8;
    public const int BrightBlue = 9;
    public const int BrightGreen = 10;
    public const int BrightCyan = 11;
    public const int BrightRed = 12;
    public const int Pink = 13;
    public const int Yellow = 14;
    public const int White = 15;

    // ── Extended palette (16+) — added for the chat overhaul, each NAMED FOR ITS COLOR ──
    // Constants identify colors; the semantic aliases below map a role/channel onto one of these so all
    // of a role's sites share (and re-color from) a single place.
    public const int Cornflower = 16;
    public const int Rose = 17;
    public const int OliveGold = 18;
    public const int Orange = 19;
    public const int Amethyst = 20;
    public const int Emerald = 21;
    public const int Mint = 22;
    public const int Crimson = 23;
    public const int Brick = 24;
    public const int RoyalBlue = 25;
    public const int Turquoise = 26;
    public const int Coral = 27;
    public const int Periwinkle = 28;
    public const int Tan = 29;

    // ── Semantic aliases — a role/channel points at a color so every one of its sites changes together ──
    public const int Say = Gray;
    public const int Emote = Gray;              // shares Say's gray on purpose
    public const int Tell = BrightGreen;
    public const int Who = Pink;
    public const int JoinLeft = DarkGray;
    public const int Roll = Cornflower;
    public const int AdminChat = Rose;
    public const int Notice = Periwinkle;       // the admin /notice broadcast
    public const int Warning = Coral;           // client errors, weather/ToD warnings, urgent guild-war lines
    public const int Npc = OliveGold;           // NPC dialogue — its own slot, distinct from player names
    public const int Guild = Emerald;           // guild member chat + guild-wide social notices
    public const int GuildOfficer = Mint;       // guild officer chat + officer-only nudges
    public const int War = Crimson;             // public war announcements
    public const int GuildWar = Brick;          // private guild-war feed
    // (Overhead-name rank colors live in PlayerNameColor.For, which maps each rank to a color constant.)

    /// <summary>
    /// Canonical packed <c>0xRRGGBB</c> RGB for each palette index — the SINGLE source of truth for what
    /// these colors actually are. Indices 0-15 are the untouched QBColor palette; 16-29 are the chat
    /// overhaul's appended colors. Kept as ints so this stays free of any MonoGame dependency (Mirage.Shared
    /// has none). The client's render table (<c>TextArea.GameColors</c>) maps these to XNA <c>Color</c>, and
    /// <see cref="GuildColorPolicy"/> reserves them — both DERIVE from this array so the rendered palette and
    /// the reserved set can never drift apart. Order matches the index constants above.
    /// </summary>
    public static readonly int[] Rgb =
    {
        0x000000, // 0  Black
        0x000080, // 1  Blue        (Navy)
        0x008000, // 2  Green
        0x008080, // 3  Cyan        (Teal)
        0x800000, // 4  Red         (Maroon)
        0x800080, // 5  Magenta     (Purple)
        0x808000, // 6  Brown       (Olive)
        0xC0C0C0, // 7  Gray        (Silver)
        0x808080, // 8  DarkGray
        0x0000FF, // 9  BrightBlue
        0x00FF00, // 10 BrightGreen (Lime)
        0x00FFFF, // 11 BrightCyan
        0xFF0000, // 12 BrightRed
        0xFF00FF, // 13 Pink        (Magenta)
        0xFFFF00, // 14 Yellow
        0xFFFFFF, // 15 White
        0x6495ED, // 16 Cornflower  — Roll
        0xFF5C9E, // 17 Rose        — AdminChat
        0xB5A03C, // 18 OliveGold   — NPC dialogue
        0xE8843C, // 19 Orange      — Monitor name
        0xC74DE0, // 20 Amethyst    — Creator name
        0x43C46A, // 21 Emerald     — Guild chat
        0x86E3B0, // 22 Mint        — GuildOfficer chat
        0xE5484D, // 23 Crimson     — War (public)
        0xB5352F, // 24 Brick       — GuildWar (private)
        0x3B6FE6, // 25 RoyalBlue   — Developer name
        0x1BA89C, // 26 Turquoise   — Mapper name
        0xFF6B6B, // 27 Coral       — Warning / error
        0xB39DFF, // 28 Periwinkle  — Notice
        0xC2AE86, // 29 Tan         — Player name
    };

    // ── Packed-RGB helpers (0xRRGGBB) — the shared, MonoGame-free color representation ──
    public static int Pack(int r, int g, int b) => ((r & 0xFF) << 16) | ((g & 0xFF) << 8) | (b & 0xFF);
    public static int RedOf(int rgb) => (rgb >> 16) & 0xFF;
    public static int GreenOf(int rgb) => (rgb >> 8) & 0xFF;
    public static int BlueOf(int rgb) => rgb & 0xFF;
}
