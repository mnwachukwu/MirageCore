using Mirage.Shared.Records;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Serialization;

/// <summary>
/// (De)serializes <see cref="TileRecord"/>.
///
/// <para>Writes the new compact shape: per-layer-type arrays of packed <see cref="LayerCell"/> values
/// (trailing empties trimmed, and the property omitted entirely when a layer type is all-empty), and
/// omits Type/Data fields at their defaults — so a blank tile serializes as just <c>{}</c>.</para>
///
/// <para>Reads that shape back.  Property names are matched case-insensitively and <c>type</c> is
/// accepted as either an enum name or a number, so the converter does not depend on the caller's
/// options (the server uses camelCase + string enums; the editor uses PascalCase + numeric).</para>
/// </summary>
internal sealed class TileRecordConverter : JsonConverter<TileRecord>
{
    public override TileRecord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
        var tile = new TileRecord();
        Span<int> art = stackalloc int[Math.Max(Constants.MaxGroundLayers,
                                       Math.Max(Constants.MaxFringeLayers, Constants.MaxCanopyLayers))];

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException();
            string name = reader.GetString()!.ToLowerInvariant();
            reader.Read(); // advance to the value

            switch (name)
            {
                case "ground":
                    tile = tile.WithArt(LayerType.Ground, art[..ReadPackedArray(ref reader, art, Constants.MaxGroundLayers)]);
                    break;
                case "fringe":
                    tile = tile.WithArt(LayerType.Fringe, art[..ReadPackedArray(ref reader, art, Constants.MaxFringeLayers)]);
                    break;
                case "canopy":
                    tile = tile.WithArt(LayerType.Canopy, art[..ReadPackedArray(ref reader, art, Constants.MaxCanopyLayers)]);
                    break;
                case "fringeattr":
                    tile = tile with { FringeAttr = ReadFringeAttr(ref reader) };
                    break;
                case "type":
                    tile = tile with { Type = ReadTileType(ref reader) };
                    break;
                case "warpmap": tile = tile with { WarpMap = reader.GetInt16() }; break;
                case "warpx": tile = tile with { WarpX = reader.GetUInt16() }; break;
                case "warpy": tile = tile with { WarpY = reader.GetUInt16() }; break;
                case "warplayer": tile = tile with { WarpLayer = ReadLayer(ref reader) }; break;
                case "itemnum": tile = tile with { ItemNum = reader.GetInt16() }; break;
                // "itemvalue" is the older spelling, still accepted so an existing map loads.
                case "itemquantity" or "itemvalue": tile = tile with { ItemQuantity = reader.GetInt16() }; break;
                case "itemrespawnsecs": tile = tile with { ItemRespawnSecs = reader.GetInt16() }; break;
                case "keyitemnum": tile = tile with { KeyItemNum = reader.GetInt16() }; break;
                case "keyisconsumed": tile = tile with { KeyIsConsumed = reader.GetBoolean() }; break;
                case "blockslight": tile = tile with { BlocksLight = reader.GetBoolean() }; break;
                case "blockssight": tile = tile with { BlocksSight = reader.GetBoolean() }; break;
                case "doorx": tile = tile with { DoorX = reader.GetUInt16() }; break;
                case "doory": tile = tile with { DoorY = reader.GetUInt16() }; break;
                case "doorlayer": tile = tile with { DoorLayer = ReadLayer(ref reader) }; break;
                case "rampgroundside": tile = tile with { RampGroundSide = ReadDirection(ref reader) }; break;
                default:
                    reader.Skip();
                    break;
            }
        }
        return tile;
    }

    public override void Write(Utf8JsonWriter writer, TileRecord value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        WritePackedArray(writer, "ground", value.Ground);
        WritePackedArray(writer, "fringe", value.Fringe);
        WritePackedArray(writer, "canopy", value.Canopy);
        if (value.Type != TileType.Walkable) writer.WriteString("type", value.Type.ToString());
        WriteAttrFields(writer, value.Type,
            value.WarpMap, value.WarpX, value.WarpY, value.WarpLayer,
            value.ItemNum, value.ItemQuantity, value.ItemRespawnSecs,
            value.KeyItemNum, value.KeyIsConsumed,
            value.BlocksLight, value.BlocksSight,
            value.DoorX, value.DoorY, value.DoorLayer,
            value.RampGroundSide);
        if (value.FringeAttr is { } fa) WriteFringeAttr(writer, fa);
        writer.WriteEndObject();
    }

    // Reads a JSON array of packed layer ints (reader positioned at StartArray) into the caller's scratch
    // buffer, and returns how many were written. A file carrying more layers than this build has is read to
    // the end and the surplus dropped, so a map authored against a deeper stack still loads.
    private static int ReadPackedArray(ref Utf8JsonReader reader, scoped Span<int> scratch, int depth)
    {
        int i = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            int v = reader.GetInt32();
            if (i < depth) scratch[i] = v;
            i++;
        }
        return Math.Min(i, depth);
    }

    // Writes the layer stack up to its last non-empty slot; omits the property when fully empty.
    private static void WritePackedArray(Utf8JsonWriter writer, string name, ReadOnlySpan<int> layers)
    {
        int last = -1;
        for (int i = 0; i < layers.Length; i++)
            if (!LayerCell.IsEmpty(layers[i])) last = i;
        if (last < 0) return;
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        for (int i = 0; i <= last; i++) writer.WriteNumberValue(layers[i]);
        writer.WriteEndArray();
    }

    private static TileType ReadTileType(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Number) return (TileType)reader.GetInt32();
        if (reader.TokenType == JsonTokenType.String)
        {
            string s = reader.GetString()!;
            if (Enum.TryParse<TileType>(s, ignoreCase: true, out var t)) return t;
            if (int.TryParse(s, out int n)) return (TileType)n;
        }
        return TileType.Walkable;
    }

    private static FringeAttr ReadFringeAttr(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
        var fa = new FringeAttr();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) throw new JsonException();
            string name = reader.GetString()!.ToLowerInvariant();
            reader.Read(); // advance to the value
            switch (name)
            {
                case "type":
                    fa = fa with { Type = ReadTileType(ref reader) };
                    break;
                case "warpmap": fa = fa with { WarpMap = reader.GetInt16() }; break;
                case "warpx": fa = fa with { WarpX = reader.GetUInt16() }; break;
                case "warpy": fa = fa with { WarpY = reader.GetUInt16() }; break;
                case "warplayer": fa = fa with { WarpLayer = ReadLayer(ref reader) }; break;
                case "itemnum": fa = fa with { ItemNum = reader.GetInt16() }; break;
                case "itemquantity" or "itemvalue": fa = fa with { ItemQuantity = reader.GetInt16() }; break;
                case "itemrespawnsecs": fa = fa with { ItemRespawnSecs = reader.GetInt16() }; break;
                case "keyitemnum": fa = fa with { KeyItemNum = reader.GetInt16() }; break;
                case "keyisconsumed": fa = fa with { KeyIsConsumed = reader.GetBoolean() }; break;
                case "blockslight": fa = fa with { BlocksLight = reader.GetBoolean() }; break;
                case "blockssight": fa = fa with { BlocksSight = reader.GetBoolean() }; break;
                case "doorx": fa = fa with { DoorX = reader.GetUInt16() }; break;
                case "doory": fa = fa with { DoorY = reader.GetUInt16() }; break;
                case "doorlayer": fa = fa with { DoorLayer = ReadLayer(ref reader) }; break;
                case "rampgroundside": fa = fa with { RampGroundSide = ReadDirection(ref reader) }; break;
                default:
                    reader.Skip();
                    break;
            }
        }
        return fa;
    }

    // A non-null FringeAttr emits the object; its mere PRESENCE marks "a walkable fringe layer exists
    // here" (so a plain walkable fringe is the compact {}).  Default fields inside are omitted, matching
    // the tile's own Type/Data handling.
    private static void WriteFringeAttr(Utf8JsonWriter writer, FringeAttr fa)
    {
        writer.WritePropertyName("fringeAttr");
        writer.WriteStartObject();
        if (fa.Type != TileType.Walkable) writer.WriteString("type", fa.Type.ToString());
        WriteAttrFields(writer, fa.Type,
            fa.WarpMap, fa.WarpX, fa.WarpY, fa.WarpLayer,
            fa.ItemNum, fa.ItemQuantity, fa.ItemRespawnSecs,
            fa.KeyItemNum, fa.KeyIsConsumed,
            fa.BlocksLight, fa.BlocksSight,
            fa.DoorX, fa.DoorY, fa.DoorLayer,
            fa.RampGroundSide);
        writer.WriteEndObject();
    }

    // One writer for both planes, since their field sets are identical.
    //
    // Gated on TYPE rather than on "is it non-zero", which is what makes a tile file readable: a Warp
    // writes its destination even when that destination is (0,0) — a real coordinate — while a Blocked
    // tile writes nothing at all no matter what happens to be sitting in its unused fields. The old
    // format could not tell those apart, because a zero and an absent slot looked the same.
    //
    // Enums go out as NAMES: "warpLayer": "Fringe" still means something to a reader that has never seen
    // the enum, where a bare number does not.
    private static void WriteAttrFields(
        Utf8JsonWriter writer, TileType type,
        short warpMap, ushort warpX, ushort warpY, WorldLayer warpLayer,
        short itemNum, short itemQuantity, short itemRespawnSecs,
        short keyItemNum, bool keyIsConsumed,
        bool blocksLight, bool blocksSight,
        ushort doorX, ushort doorY, WorldLayer doorLayer,
        Direction rampGroundSide)
    {
        if (TileAttrRules.UsesWarp(type))
        {
            writer.WriteNumber("warpMap", warpMap);
            writer.WriteNumber("warpX", warpX);
            writer.WriteNumber("warpY", warpY);
            if (warpLayer != WorldLayer.Ground) writer.WriteString("warpLayer", warpLayer.ToString());
        }
        if (TileAttrRules.UsesItem(type))
        {
            writer.WriteNumber("itemNum", itemNum);
            // Always written as "itemQuantity"; the readers above still accept the older "itemValue"
            // spelling, so a map authored before the rename loads untouched.
            writer.WriteNumber("itemQuantity", itemQuantity);
            if (itemRespawnSecs != 0) writer.WriteNumber("itemRespawnSecs", itemRespawnSecs);
        }
        if (TileAttrRules.UsesKey(type))
        {
            writer.WriteNumber("keyItemNum", keyItemNum);
            if (keyIsConsumed) writer.WriteBoolean("keyIsConsumed", true);
        }
        if (TileAttrRules.UsesDoor(type))
        {
            writer.WriteNumber("doorX", doorX);
            writer.WriteNumber("doorY", doorY);
            if (doorLayer != WorldLayer.Ground) writer.WriteString("doorLayer", doorLayer.ToString());
        }
        if (TileAttrRules.UsesRamp(type))
            writer.WriteString("rampGroundSide", rampGroundSide.ToString());
        // Only what the wall lets THROUGH is written. A wall stops everything, so the ordinary case costs
        // nothing on disk and a file with neither key reads back as solid.
        if (TileAttrRules.UsesBlocked(type))
        {
            if (!blocksLight) writer.WriteBoolean("blocksLight", false);
            if (!blocksSight) writer.WriteBoolean("blocksSight", false);
        }
    }

    // Enum readers that take a name or a number, matching ReadTileType — the server writes names, and a
    // hand-edited or converter-written file may well carry numbers.
    private static WorldLayer ReadLayer(ref Utf8JsonReader reader) =>
        ReadEnum(ref reader, WorldLayer.Ground);

    private static Direction ReadDirection(ref Utf8JsonReader reader) =>
        ReadEnum(ref reader, Direction.Up);

    private static TEnum ReadEnum<TEnum>(ref Utf8JsonReader reader, TEnum fallback) where TEnum : struct, Enum
    {
        if (reader.TokenType == JsonTokenType.Number) return (TEnum)Enum.ToObject(typeof(TEnum), reader.GetInt32());
        if (reader.TokenType == JsonTokenType.String)
        {
            string s = reader.GetString()!;
            if (Enum.TryParse<TEnum>(s, ignoreCase: true, out var v)) return v;
            if (int.TryParse(s, out int n)) return (TEnum)Enum.ToObject(typeof(TEnum), n);
        }
        return fallback;
    }
}
