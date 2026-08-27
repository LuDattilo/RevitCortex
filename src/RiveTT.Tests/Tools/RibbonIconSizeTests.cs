using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace RiveTT.Tests.Tools;

/// <summary>
/// Every ribbon icon must be exactly the size its filename claims.
///
/// lock-32.png shipped at 128x128 and lock-16.png at 64x64 — four times nominal —
/// while status-*.png, the only pair the generator actually produced, was correct.
/// The buttons then showed no icon in Revit and nothing failed: CortexRibbon.LoadIcon
/// finds the resource, decodes it, assigns it, and returns without complaint. A
/// wrong-sized icon is invisible, not broken, which is why it survived review.
///
/// The dimensions are read straight from the PNG header rather than through
/// System.Drawing: IHDR is the first chunk of every PNG, at a fixed offset, so this
/// needs no imaging dependency and runs anywhere the rest of the suite runs.
/// </summary>
public class RibbonIconSizeTests
{
    private static string ResourcesDir() => Path.GetFullPath(
        Path.Combine("..", "..", "..", "..", "RiveTT.Plugin", "Resources"));

    /// <summary>Width and height from the PNG IHDR chunk.</summary>
    private static (int Width, int Height) ReadPngSize(string path)
    {
        var header = new byte[24];
        using (var stream = File.OpenRead(path))
        {
            if (stream.Read(header, 0, header.Length) != header.Length)
                throw new InvalidDataException($"{Path.GetFileName(path)} is too short to be a PNG.");
        }

        // 8-byte signature, then the IHDR chunk: length, type, width, height.
        ReadOnlySpan<byte> signature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Assert.True(header.AsSpan(0, 8).SequenceEqual(signature),
            $"{Path.GetFileName(path)} is not a PNG.");

        var width = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4));
        return (width, height);
    }

    [Fact]
    public void EveryRibbonIconMatchesTheSizeInItsName()
    {
        var dir = ResourcesDir();
        Assert.True(Directory.Exists(dir), $"Resources directory not found: {dir}");

        // Top level only: Resources\source\ holds the high-resolution originals the
        // generator resamples from, and the .csproj embeds Resources\*.png without
        // recursion, so those are deliberately not ribbon icons.
        var icons = Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(icons);

        var offenders = new List<string>();
        foreach (var icon in icons)
        {
            var name = Path.GetFileName(icon);
            var match = Regex.Match(name, @"-(\d+)\.png$");
            Assert.True(match.Success,
                $"{name} does not carry its size in its name; the ribbon expects <role>-16.png and <role>-32.png.");

            var expected = int.Parse(match.Groups[1].Value);
            var (width, height) = ReadPngSize(icon);
            if (width != expected || height != expected)
                offenders.Add($"{name}: {width}x{height}, expected {expected}x{expected}");
        }

        Assert.True(offenders.Count == 0,
            "Revit sizes ribbon images by their slot, not by the file. An icon larger than "
            + "its slot shows as blank rather than scaled:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void EveryRibbonButtonHasBothSizes()
    {
        // CortexRibbon.Button asks for "<role>-32.png" and "<role>-16.png" for every
        // button. A missing one is silently skipped by the `if (large != null)` guard.
        var dir = ResourcesDir();
        var roles = Directory.GetFiles(dir, "*.png", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Select(n => Regex.Replace(n!, @"-\d+\.png$", ""))
            .Distinct()
            .ToList();

        Assert.NotEmpty(roles);
        foreach (var role in roles)
        {
            Assert.True(File.Exists(Path.Combine(dir, $"{role}-32.png")),
                $"{role} has no 32x32 icon (LargeImage).");
            Assert.True(File.Exists(Path.Combine(dir, $"{role}-16.png")),
                $"{role} has no 16x16 icon (Image).");
        }
    }

    [Fact]
    public void HandAuthoredArtworkKeepsItsHighResolutionSource()
    {
        // The lock/unlock artwork is resampled by tools\make-ribbon-icons.ps1. Losing
        // the originals would mean the nominal-size PNGs become the only copy, and the
        // next regeneration would upscale 32x32 art.
        var source = Path.Combine(ResourcesDir(), "source");
        Assert.True(Directory.Exists(source), "Resources\\source\\ is missing.");

        foreach (var art in new[] { "lock", "unlock" })
        {
            var path = Path.Combine(source, $"{art}.png");
            Assert.True(File.Exists(path), $"High-resolution source missing: {art}.png");
            var (width, height) = ReadPngSize(path);
            Assert.True(width >= 32 && height >= 32,
                $"{art}.png is {width}x{height}; it must be at least the largest ribbon size.");
        }
    }
}
