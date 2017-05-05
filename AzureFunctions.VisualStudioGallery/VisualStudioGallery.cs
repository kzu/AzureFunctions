using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

/// <summary>
/// Provides functionality to update a stream-based Visual Studio gallery 
/// feed with blob streams.
/// </summary>
public class VisualStudioGallery
{
    /// <summary>
    /// Atom 1.0 XML.
    /// </summary>
    public static XNamespace AtomNs => XNamespace.Get("http://www.w3.org/2005/Atom");

    /// <summary>
    /// XML namespace of the custom Visual Studio gallery elements.
    /// </summary>
    public static XNamespace GalleryNs => XNamespace.Get("http://schemas.microsoft.com/developer/vsx-syndication-schema/2010");

    /// <summary>
    /// XML namespace of a VSIX extension manifest.
    /// </summary>
    public static XNamespace VsixNs => XNamespace.Get("http://schemas.microsoft.com/developer/vsx-schema/2011");

    string feedTitle;
    string feedId;

    /// <summary>
    /// Initializes the Visual Studio gallery basic information.
    /// </summary>
    /// <param name="storageBaseUrl">The base URL where blobs are stored. Used to build the VSIX payload URLs for the feed.</param>
    /// <param name="feedId">Optional feed identifier used in the Atom XML. Defaults to 'Gallery'.</param>
    /// <param name="feedTitle">Optional feed title used in the Atom XML. Defaults to 'Gallery'.</param>
    public VisualStudioGallery(string storageBaseUrl, string feedId = "Gallery", string feedTitle = "Gallery")
    {
        StorageBaseUrl = storageBaseUrl ?? throw new ArgumentNullException(nameof(storageBaseUrl));

        if (!StorageBaseUrl.EndsWith("/"))
            StorageBaseUrl += "/";

        this.feedId = feedId;
        this.feedTitle = feedTitle;
    }

    public string StorageBaseUrl { get; private set; }

    /// <summary>
    /// Updates the gallery feed with the updated information from the given VSIX stream.
    /// </summary>
    /// <param name="vsixBlob">The new VSIX payload to process.</param>
    /// <param name="vsixBlobName">The name of the blob being uploaded, without the file extension. Used as the base name for the corresponding VSIX Icon image, if found.</param>
    /// <param name="currentFeed">Current feed, or <see langword="null"/> if not initialized yet. Will be created on first access.</param>
    /// <param name="updatedFeed">Output stream with the updated Atom feed for the gallery.</param>
    /// <param name="updatedIcon">Output stream with the optional icon extracted from the <paramref name="vsixBlob"/> VSIX.</param>
    public void UpdateFeed(Stream vsixBlob, string vsixBlobName, Stream currentFeed, Stream updatedFeed, Stream updatedIcon)
    {
        if (vsixBlob == null) throw new ArgumentNullException(nameof(vsixBlob));
        if (vsixBlobName == null) throw new ArgumentNullException(nameof(vsixBlobName));

        XElement atom;
        if (currentFeed == null)
        {
            atom = new XElement(AtomNs + "feed",
                new XElement(AtomNs + "title", new XAttribute("type", "text"), feedTitle),
                new XElement(AtomNs + "id", feedId)
            );
        }
        else
        {
            try
            {
                atom = XDocument.Load(currentFeed).Root;
                atom.Element(AtomNs + "title")?.SetValue(feedTitle);
                atom.Element(AtomNs + "id")?.SetValue(feedId);
            }
            catch (XmlException)
            {
                atom = new XElement(AtomNs + "feed",
                    new XElement(AtomNs + "title", new XAttribute("type", "text"), feedTitle),
                    new XElement(AtomNs + "id", feedId)
                );
            }
        }

        var updated = atom.Element(AtomNs + "updated");
        if (updated == null)
        {
            updated = new XElement(AtomNs + "updated");
            atom.Add(updated);
        }

        updated.Value = XmlConvert.ToString(DateTimeOffset.UtcNow);

        using (var archive = new ZipArchive(vsixBlob, ZipArchiveMode.Read))
        {
            var zipEntry = archive.GetEntry("extension.vsixmanifest");
            if (zipEntry != null)
            {
                using (var stream = zipEntry.Open())
                {
                    var manifest = XDocument.Load(stream).Root;
                    var metadata = manifest.Element(VsixNs + "Metadata");
                    var identity = metadata.Element(VsixNs + "Identity");
                    var id = identity.Attribute("Id").Value;
                    var version = identity.Attribute("Version").Value;

                    var entry = atom.Elements(AtomNs + "entry").FirstOrDefault(x => x.Element(AtomNs + "id")?.Value == id);
                    if (entry != null)
                        entry.Remove();

                    entry = new XElement(AtomNs + "entry",
                        new XElement(AtomNs + "id", id),
                        new XElement(AtomNs + "title", new XAttribute("type", "text"), metadata.Element(VsixNs + "DisplayName").Value),
                        new XElement(AtomNs + "link",
                            new XAttribute("rel", "alternate"),
                            new XAttribute("href", $"{StorageBaseUrl}{vsixBlobName}.vsix")),
                        new XElement(AtomNs + "summary", new XAttribute("type", "text"), metadata.Element(VsixNs + "Description").Value),
                        new XElement(AtomNs + "published", XmlConvert.ToString(DateTimeOffset.UtcNow)),
                        new XElement(AtomNs + "updated", XmlConvert.ToString(DateTimeOffset.UtcNow)),
                        new XElement(AtomNs + "author",
                            new XElement(AtomNs + "name", identity.Attribute("Publisher").Value)),
                        new XElement(AtomNs + "content",
                            new XAttribute("type", "application/octet-stream"),
                            new XAttribute("src", $"{StorageBaseUrl}{vsixBlobName}.vsix"))
                    );

                    var icon = metadata.Element(VsixNs + "Icon");
                    if (icon != null)
                    {
                        try
                        {
                            var iconEntry = archive.GetEntry(icon.Value);
                            if (iconEntry != null)
                            {
                                using (var iconStream = iconEntry.Open())
                                {
                                    iconStream.CopyTo(updatedIcon);
                                }

                                entry.Add(new XElement(AtomNs + "link",
                                    new XAttribute("rel", "icon"),
                                    new XAttribute("href", $"{StorageBaseUrl}{vsixBlobName}.png")));
                            }
                        }
                        catch { }
                    }

                    var vsix = new XElement(GalleryNs + "Vsix",
                        new XElement(GalleryNs + "Id", id),
                        new XElement(GalleryNs + "Version", version),
                        new XElement(GalleryNs + "References")
                    );

                    entry.Add(vsix);
                    atom.AddFirst(entry);

                    using (var writer = XmlWriter.Create(updatedFeed, new XmlWriterSettings { Indent = true }))
                    {
                        atom.WriteTo(writer);
                        writer.Flush();
                    }
                }
            }
        }
    }
}
