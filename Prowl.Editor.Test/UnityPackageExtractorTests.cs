// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Formats.Tar;
using System.IO.Compression;
using System.Text;

using Xunit;

namespace Prowl.Editor.Test;

public class UnityPackageExtractorTests : IDisposable
{
    private readonly string _root;
    private readonly string _assets;

    public UnityPackageExtractorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ProwlUnityPackageTest", Guid.NewGuid().ToString("N"));
        _assets = Path.Combine(_root, "Assets");
        Directory.CreateDirectory(_assets);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
        GC.SuppressFinalize(this);
    }

    /// <summary>Writes a .unitypackage holding one GUID folder per (pathname, content); null content makes a folder entry.</summary>
    private string MakePackage(params (string Pathname, string? Content)[] assets)
    {
        string path = Path.Combine(_root, "test.unitypackage");
        using FileStream file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
        using var tar = new TarWriter(gzip);

        void Add(string name, string text)
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text)) };
            tar.WriteEntry(entry);
        }

        foreach ((string pathname, string? content) in assets)
        {
            string guid = Guid.NewGuid().ToString("N");
            Add($"{guid}/pathname", pathname);
            Add($"{guid}/asset.meta", "guid: " + guid);
            if (content != null) Add($"{guid}/asset", content);
        }
        return path;
    }

    [Fact]
    public void AllowedAssets_KeepTheirFolders_WithoutAssetsPrefix()
    {
        string package = MakePackage(("Assets/Models/Building/Wall.fbx\n00", "fbx"), ("Assets/Textures/Wall.png", "png"));

        UnityPackageExtractor.Result result = UnityPackageExtractor.Extract(package, _assets);

        Assert.Equal(2, result.Imported);
        Assert.Empty(result.Errors);
        Assert.Equal("fbx", File.ReadAllText(Path.Combine(_assets, "Models", "Building", "Wall.fbx")));
        Assert.Equal("png", File.ReadAllText(Path.Combine(_assets, "Textures", "Wall.png")));
    }

    [Fact]
    public void DataAndSourceFormats_AreImported()
    {
        string package = MakePackage(("Assets/Data/Items.json", "{}"), ("Assets/Data/Table.csv", "a,b"),
            ("Assets/Art/Logo.bmp", "bmp"), ("Assets/Art/Rock.blend", "blend"));

        UnityPackageExtractor.Result result = UnityPackageExtractor.Extract(package, _assets);

        Assert.Equal(4, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.True(File.Exists(Path.Combine(_assets, "Art", "Rock.blend")));
    }

    [Fact]
    public void UnityOnlyFilesAndFolders_AreSkipped()
    {
        string package = MakePackage(("Assets/Mats/Wall.mat", "m"), ("Assets/Wall.prefab", "p"), ("Assets/Mats", null));

        UnityPackageExtractor.Result result = UnityPackageExtractor.Extract(package, _assets);

        Assert.Equal(0, result.Imported);
        Assert.Equal(2, result.Skipped);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_assets));
    }

    [Fact]
    public void ExistingFile_IsNotOverwritten()
    {
        File.WriteAllText(Path.Combine(_assets, "Hit.wav"), "mine");
        string package = MakePackage(("Assets/Hit.wav", "theirs"));

        UnityPackageExtractor.Result result = UnityPackageExtractor.Extract(package, _assets);

        Assert.Equal(1, result.Skipped);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_assets, "Hit.wav")));
    }

    [Fact]
    public void PathnameLeavingAssets_IsRejected()
    {
        string package = MakePackage(("Assets/../../Evil.cs", "x"));

        UnityPackageExtractor.Result result = UnityPackageExtractor.Extract(package, _assets);

        Assert.Equal(0, result.Imported);
        Assert.Single(result.Errors);
        Assert.False(File.Exists(Path.Combine(_root, "Evil.cs")));
    }
}
