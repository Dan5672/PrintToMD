using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace Print2Md.Core;

// OPC logical parts may be stored as name/[0].piece ... name/[N].last.piece.
// Reassemble bytes before parsing XML: a boundary can split a UTF-8 character.
internal sealed class OxpsPart
{
    private static readonly Regex PieceName = new Regex(
        @"^(?<part>.+)/\[(?<index>0|[1-9][0-9]*)\](?<last>\.last)?\.piece$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly ZipArchiveEntry[] pieces;

    private OxpsPart(ZipArchiveEntry[] pieces) => this.pieces = pieces;

    public static IReadOnlyDictionary<string, OxpsPart> Index(ZipArchive archive)
    {
        var parts = new Dictionary<string, OxpsPart>(StringComparer.OrdinalIgnoreCase);
        var groups = new Dictionary<string, List<Piece>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (name.EndsWith("/", StringComparison.Ordinal)) continue;
            var match = PieceName.Match(name);
            if (!match.Success)
            {
                if (parts.ContainsKey(name)) throw InvalidPieces();
                parts.Add(name, new OxpsPart(new[] { entry }));
                continue;
            }

            if (!int.TryParse(match.Groups["index"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                throw InvalidPieces();
            var part = match.Groups["part"].Value;
            if (!groups.TryGetValue(part, out var group))
            {
                group = new List<Piece>();
                groups.Add(part, group);
            }
            group.Add(new Piece(entry, index, match.Groups["last"].Success));
        }

        foreach (var pair in groups)
        {
            if (parts.ContainsKey(pair.Key)) throw InvalidPieces();
            var ordered = pair.Value.OrderBy(piece => piece.Index).ToArray();
            for (var index = 0; index < ordered.Length; index++)
            {
                if (ordered[index].Index != index || ordered[index].Last != (index == ordered.Length - 1))
                    throw InvalidPieces();
            }
            parts.Add(pair.Key, new OxpsPart(ordered.Select(piece => piece.Entry).ToArray()));
        }
        return parts;
    }

    public Stream Open()
    {
        if (pieces.Length == 1) return pieces[0].Open();
        var joined = new MemoryStream();
        try
        {
            foreach (var piece in pieces)
            {
                using (var input = piece.Open()) input.CopyTo(joined);
            }
            joined.Position = 0;
            return joined;
        }
        catch
        {
            joined.Dispose();
            throw;
        }
    }

    private static ConversionException InvalidPieces() => new ConversionException(
        ConversionFailure.InvalidPackage, "The OXPS package contains duplicate parts or an invalid piece sequence.");

    private sealed class Piece
    {
        public Piece(ZipArchiveEntry entry, int index, bool last)
        {
            Entry = entry;
            Index = index;
            Last = last;
        }
        public ZipArchiveEntry Entry { get; }
        public int Index { get; }
        public bool Last { get; }
    }
}
