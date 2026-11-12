using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace EnglishCenter.Modules.MediaNotification.Application.Files;

internal static class FileContentValidator
{
    public static bool IsValid(string name, byte[] bytes)
    {
        ReadOnlySpan<byte> data = bytes;
        return Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".pdf" => data.StartsWith("%PDF-"u8) && data.Length >= 12 &&
                data[Math.Max(0, data.Length - 1024)..].IndexOf("%%EOF"u8) >= 0,
            ".png" => data.Length >= 33 && data.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
                data.Slice(12, 4).SequenceEqual("IHDR"u8),
            ".jpg" => data.Length >= 4 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff &&
                data[^2] == 0xff && data[^1] == 0xd9,
            ".mp3" => IsMp3(data),
            ".m4a" => IsM4a(data),
            ".docx" => IsDocx(bytes),
            _ => false
        };
    }

    private static bool IsMp3(ReadOnlySpan<byte> data)
    {
        var offset = 0;
        if (data.StartsWith("ID3"u8))
        {
            if (data.Length < 10 || data[3] is < 2 or > 4 || (data[6] | data[7] | data[8] | data[9]) >= 128) return false;
            offset = 10 + (data[6] << 21 | data[7] << 14 | data[8] << 7 | data[9]);
        }
        if (data.Length - offset < 4) return false;
        var frame = data[offset..];
        return frame[0] == 0xff && (frame[1] & 0xe0) == 0xe0 && (frame[1] & 0x18) != 0x08 &&
            (frame[1] & 0x06) != 0 && (frame[2] & 0xf0) is not (0 or 0xf0) && (frame[2] & 0x0c) != 0x0c;
    }

    private static bool IsM4a(ReadOnlySpan<byte> data)
    {
        if (data.Length < 16 || !data.Slice(4, 4).SequenceEqual("ftyp"u8)) return false;
        var boxSize = BinaryPrimitives.ReadUInt32BigEndian(data);
        if (boxSize < 16 || boxSize > data.Length || boxSize > 4096 || boxSize % 4 != 0) return false;
        var brand = Encoding.ASCII.GetString(data.Slice(8, 4));
        // Generic MP4/video brands alone do not establish an audio submission.
        return brand is "M4A " or "M4B ";
    }

    private static bool IsDocx(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            if (zip.Entries.Count is 0 or > 2048) return false;
            long expanded = 0;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in zip.Entries)
            {
                if (!names.Add(entry.FullName) || entry.FullName.Contains("..", StringComparison.Ordinal) ||
                    entry.FullName.Contains('\\') || entry.FullName.StartsWith('/') ||
                    entry.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase)) return false;
                expanded += entry.Length;
                if (expanded > 64 * 1024 * 1024 || entry.Length > 20 * 1024 * 1024) return false;
            }
            var types = ReadXml(zip.GetEntry("[Content_Types].xml"));
            var document = ReadXml(zip.GetEntry("word/document.xml"));
            var relationships = ReadXml(zip.GetEntry("_rels/.rels"));
            XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
            XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            return types?.Root?.Name == ct + "Types" &&
                types.Root.Elements(ct + "Override").Any(x => (string?)x.Attribute("PartName") == "/word/document.xml" &&
                    (string?)x.Attribute("ContentType") == "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml") &&
                document?.Root?.Name == word + "document" && document.Root.Element(word + "body") is not null &&
                relationships?.Root?.Name == rel + "Relationships" &&
                relationships.Root.Elements(rel + "Relationship").Any(x =>
                    (string?)x.Attribute("Type") == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" &&
                    ((string?)x.Attribute("Target") is "word/document.xml" or "/word/document.xml") &&
                    (string?)x.Attribute("TargetMode") != "External");
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    private static XDocument? ReadXml(ZipArchiveEntry? entry)
    {
        const int maxXmlBytes = 2 * 1024 * 1024;
        if (entry is null || entry.Length > maxXmlBytes) return null;
        using var input = entry.Open();
        using var bounded = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (bounded.Length + count > maxXmlBytes) return null;
            bounded.Write(buffer, 0, count);
        }
        bounded.Position = 0;
        using var reader = XmlReader.Create(bounded, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = maxXmlBytes
        });
        return XDocument.Load(reader);
    }
}
