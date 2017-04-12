using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Xml.XPath;
using System.Xml.Serialization;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Xunit;
using Xunit.Abstractions;
using System.Xml;

namespace Tests
{
    public class VisualStudioGalleryTests
    {
        ITestOutputHelper output;

        public VisualStudioGalleryTests(ITestOutputHelper output) => this.output = output;

        [Fact]
        public void WhenDeserializingManifestThenSucceeds()
        {
            var file = new FileInfo(@"..\..\..\SampleVsix\source.extension.vsixmanifest").FullName;

            using (var stream = File.OpenRead(file))
            {
                var manifest = (PackageManifest)new XmlSerializer(typeof(PackageManifest)).Deserialize(stream);

                Assert.Equal("2.0.0", manifest.Version);
                Assert.NotNull(manifest.Metadata);
                Assert.Equal("SampleVsix", manifest.Metadata.Identity.Id);
                Assert.Equal("|SampleVsix;GetVersion|", manifest.Metadata.Identity.Version);
                Assert.Equal("kzu", manifest.Metadata.Identity.Publisher);
                Assert.Equal("SampleVsix", manifest.Metadata.DisplayName);
                Assert.Equal("A VSIX project.", manifest.Metadata.Description);
                Assert.Equal("Icon.png", manifest.Metadata.Icon);
            }
        }

        [InlineData("1.0.0")]
        [InlineData("2.0.0")]
        [InlineData("3.0.0")]
        [Theory]
        public void WhenBuildingVersionsThenSucceeds(string version)
        {
            var project = new FileInfo(@"..\..\..\SampleVsix\SampleVsix.csproj").FullName;

            if (File.Exists(@"..\..\..\SampleVsix\obj\Debug\SampleVsix.csproj.FileListAbsolute.txt"))
                File.Delete(@"..\..\..\SampleVsix\obj\Debug\SampleVsix.csproj.FileListAbsolute.txt");

            Build(project, version);
        }

        [Fact]
        public void WhenFeedIsNullThenCreatesFeed()
        {
            var project = new FileInfo(@"..\..\..\SampleVsix\SampleVsix.csproj").FullName;

            Stream currentFeed = null;

            Build(project, "1.0.0");

            var vsix = new MemoryStream(File.ReadAllBytes("SampleVsix.1.0.0.vsix"));
            var updatedFeed = new MemoryStream();

            var gallery = new VisualStudioGallery("https://devdiv.blob.core.windows.net/alpha/");

            gallery.UpdateFeed(vsix, "SampleVsix.1.0.0", currentFeed, updatedFeed, new MemoryStream());

            updatedFeed.Position = 0;

            var feed = XDocument.Load(updatedFeed);

            Assert.True(feed.Root.Elements(VisualStudioGallery.AtomNs + "entry").Count() == 1, feed.Root.ToString());
        }

        [Fact]
        public void WhenFeedIsEmptyThenCreatesFeed()
        {
            var project = new FileInfo(@"..\..\..\SampleVsix\SampleVsix.csproj").FullName;
            var gallery = new VisualStudioGallery("https://devdiv.blob.core.windows.net/alpha/");

            var currentFeed = new MemoryStream(new byte[0]);

            Build(project, "1.0.0");

            var vsix = new MemoryStream(File.ReadAllBytes("SampleVsix.1.0.0.vsix"));
            var updatedFeed = new MemoryStream();

            gallery.UpdateFeed(vsix, "SampleVsix.1.0.0", currentFeed, updatedFeed, new MemoryStream());

            updatedFeed.Position = 0;

            var feed = XDocument.Load(updatedFeed);

            Assert.True(feed.Root.Elements(VisualStudioGallery.AtomNs + "entry").Count() == 1, feed.Root.ToString());
        }

        [Fact]
        public void WhenUpdatingFeedThenUpdatesEntries()
        {
            var project = new FileInfo(@"..\..\..\SampleVsix\SampleVsix.csproj").FullName;
            var gallery = new VisualStudioGallery("https://devdiv.blob.core.windows.net/alpha/");
            var xmlns = new XmlNamespaceManager(new NameTable());
            xmlns.AddNamespace("a", VisualStudioGallery.AtomNs.NamespaceName);
            xmlns.AddNamespace("x", VisualStudioGallery.GalleryNs.NamespaceName);

            var currentFeed = new MemoryStream(Encoding.UTF8.GetBytes(
@"<feed xmlns='http://www.w3.org/2005/Atom'>
  <title type='text'></title>
  <id>gallery</id>
  <updated>2012-11-06T22:19:45Z</updated>
</feed>"));

            Build(project, "1.0.0");

            var vsix = new MemoryStream(File.ReadAllBytes("SampleVsix.1.0.0.vsix"));
            var updatedFeed = new MemoryStream();

            gallery.UpdateFeed(vsix, "SampleVsix.1.0.0", currentFeed, updatedFeed, new MemoryStream());

            updatedFeed.Position = 0;

            var feed = XDocument.Load(updatedFeed);
            Assert.Equal("1.0.0", (string)feed.XPathEvaluate("string(a:feed/a:entry/x:Vsix/x:Version/text())", xmlns));
            Assert.Equal(gallery.StorageBaseUrl + "SampleVsix.1.0.0.png", (string)feed.XPathEvaluate("string(a:feed/a:entry/a:link[@rel = 'icon']/@href)", xmlns));

            updatedFeed.Position = 0;
            Build(project, "2.0.0");

            currentFeed = updatedFeed;
            updatedFeed = new MemoryStream();

            vsix = new MemoryStream(File.ReadAllBytes("SampleVsix.2.0.0.vsix"));

            gallery.UpdateFeed(vsix, "SampleVsix.2.0.0", currentFeed, updatedFeed, new MemoryStream());

            updatedFeed.Position = 0;

            feed = XDocument.Load(updatedFeed);
            Assert.Equal("2.0.0", (string)feed.XPathEvaluate("string(a:feed/a:entry/x:Vsix/x:Version/text())", xmlns));
            Assert.Equal(gallery.StorageBaseUrl + "SampleVsix.2.0.0.png", (string)feed.XPathEvaluate("string(a:feed/a:entry/a:link[@rel = 'icon']/@href)", xmlns));
        }

        void Build(string project, string version)
        {
            var manager = BuildManager.DefaultBuildManager;
            var parameters = new BuildParameters
            {
                Loggers = new ILogger[] { new TestOutputLogger(output, LoggerVerbosity.Minimal) }
            };

            var request = new BuildRequestData(project,
                new Dictionary<string, string>
                {
                    { "VsixVersion", version },
                    { "OutputPath", new DirectoryInfo(".").FullName }
                },
                "15.0", new[] { "Build" }, null);

            var result = manager.Build(parameters, request);

            Assert.Equal(BuildResultCode.Success, result.OverallResult);
        }
    }

    [XmlRoot(Namespace = "http://schemas.microsoft.com/developer/vsx-schema/2011")]
    public class PackageManifest
    {
        [XmlAttribute]
        public string Version { get; set; }

        [XmlElement]
        public Metadata Metadata { get; set; }
    }

    [XmlRoot(Namespace = "http://schemas.microsoft.com/developer/vsx-schema/2011")]
    public class Metadata
    {
        [XmlElement]
        public Identity Identity { get; set; }

        [XmlElement]
        public string DisplayName { get; set; }

        [XmlElement]
        public string Description { get; set; }

        [XmlElement]
        public string Icon { get; set; }
    }

    [XmlRoot(Namespace = "http://schemas.microsoft.com/developer/vsx-schema/2011")]
    public class Identity
    {
        [XmlAttribute]
        public string Id { get; set; }

        [XmlAttribute]
        public string Version { get; set; }

        [XmlAttribute]
        public string Publisher { get; set; }
    }
}
