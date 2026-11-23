using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Platform.Storage;
using UglyToad.PdfPig;

namespace StudyHelper.Services;

public static class DocumentReader
{
    public static readonly string[] AllowedExtensions = [".txt", ".md", ".pdf", ".docx"];

    public static async Task<string> ReadTextAsync(IStorageFile file, string extension)
    {
        await using var stream = await file.OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        ms.Position = 0;

        return extension switch
        {
            ".pdf" => ReadPdf(ms),
            ".docx" => ReadDocx(ms),
            _ => Encoding.UTF8.GetString(ms.ToArray())
        };
    }

    private static string ReadPdf(Stream stream)
    {
        using var pdf = PdfDocument.Open(stream);
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            sb.AppendLine($"[Page {page.Number}]");
            // Join words with spaces; page.Text often glues words together.
            sb.AppendLine(string.Join(" ", page.GetWords().Select(w => w.Text)));
        }
        return sb.ToString();
    }

    // A .docx is a zip file; the text lives in word/document.xml.
    private static string ReadDocx(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/document.xml");
        if (entry is null) return string.Empty;

        using var entryStream = entry.Open();
        var xml = XDocument.Load(entryStream);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        var sb = new StringBuilder();
        foreach (var paragraph in xml.Descendants(w + "p"))
        {
            sb.AppendLine(string.Concat(paragraph.Descendants(w + "t").Select(t => t.Value)));
        }
        return sb.ToString();
    }
}
