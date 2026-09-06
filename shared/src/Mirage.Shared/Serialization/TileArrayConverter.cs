using Mirage.Shared.Records;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mirage.Shared.Serialization;

/// <summary>
/// Serializes <see cref="TileRecord"/>[,] as a JSON array-of-arrays because
/// System.Text.Json has no built-in support for multi-dimensional arrays.
/// Outer array = X (column), inner array = Y (row).
/// </summary>
internal sealed class TileArrayConverter : JsonConverter<TileRecord[,]>
{
    /// <summary>Reads the grid at whatever size the file wrote it. The map's dimensions ARE the shape of
    /// this array — nothing else records them — so the columns are buffered before the array is allocated,
    /// and a short or ragged row is filled out with empty tiles rather than truncating the map to fit.</summary>
    public override TileRecord[,] Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException();
        reader.Read();

        var columns = new List<List<TileRecord>>();
        while (reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException();
            reader.Read();

            var column = new List<TileRecord>();
            while (reader.TokenType != JsonTokenType.EndArray)
            {
                column.Add(JsonSerializer.Deserialize<TileRecord>(ref reader, options));
                reader.Read();
            }
            columns.Add(column);
            reader.Read(); // past inner EndArray
        }
        // Reader is now at outer EndArray; framework advances past it.

        int cols = columns.Count;
        int rows = 0;
        foreach (var column in columns)
            if (column.Count > rows) rows = column.Count;
        if (cols == 0 || rows == 0) return TileGrid.Empty(Constants.MaxMapX + 1, Constants.MaxMapY + 1);

        var arr = new TileRecord[cols, rows];
        for (int x = 0; x < cols; x++)
        {
            var column = columns[x];
            for (int y = 0; y < rows; y++)
                arr[x, y] = y < column.Count ? column[y] : new TileRecord();
        }
        return arr;
    }

    public override void Write(
        Utf8JsonWriter writer, TileRecord[,] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        int cols = value.GetLength(0);
        int rows = value.GetLength(1);
        for (int x = 0; x < cols; x++)
        {
            writer.WriteStartArray();
            for (int y = 0; y < rows; y++)
                JsonSerializer.Serialize(writer, value[x, y], options);
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
    }
}
