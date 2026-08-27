using System;
using System.Linq;
using RiveTT.Tools.Utilities;
using Xunit;

namespace RiveTT.Tests.Tools;

/// <summary>
/// The text-matching half of the localization fix. It lives in its own
/// Revit-free class precisely so this can be tested without a Revit host — the
/// alias table itself references BuiltInParameter and cannot be.
/// </summary>
public class NameMatchingTests
{
    [Theory]
    [InlineData("Repère", "repere")]
    [InlineData("REPÈRE", "repere")]
    [InlineData("Hauteur d'allège", "hauteur d allege")]
    [InlineData("hauteur_d_allege", "hauteur d allege")]
    [InlineData("Famille et type", "famille et type")]
    [InlineData("  Type Name  ", "type name")]
    [InlineData("Décalage supérieur", "decalage superieur")]
    public void Normalize_StripsCaseAccentsAndPunctuation(string input, string expected)
    {
        Assert.Equal(expected, NameMatching.Normalize(input));
    }

    [Fact]
    public void Normalize_HandlesNullAndEmpty()
    {
        Assert.Equal("", NameMatching.Normalize(null));
        Assert.Equal("", NameMatching.Normalize("   "));
    }

    [Fact]
    public void Normalize_MakesLocalizedAndApiSpellingsComparable()
    {
        // The point of the whole exercise: two spellings of the same parameter
        // must collapse to one key.
        Assert.Equal(NameMatching.Normalize("Décalage inférieur"), NameMatching.Normalize("decalage inferieur"));
        Assert.NotEqual(NameMatching.Normalize("Largeur"), NameMatching.Normalize("Longueur"));
    }

    [Fact]
    public void Suggest_RanksAContainingNameFirst()
    {
        // Real case: the project's only "Repère"-like parameter for Windows was the
        // project parameter ARC_PAR_Repère. Edit distance alone buries it.
        var suggestions = NameMatching.Suggest(
            "Repère",
            new[] { "Commentaires", "ARC_PAR_Repère", "Niveau", "Repérage du type" });

        Assert.Equal("ARC_PAR_Repère", suggestions.First());
    }

    [Fact]
    public void Suggest_FindsATypoWithinDistance()
    {
        var suggestions = NameMatching.Suggest("Nivau", new[] { "Niveau", "Volume", "Surface" });

        Assert.Contains("Niveau", suggestions);
    }

    [Fact]
    public void Suggest_ReturnsNothingForAnUnrelatedName()
    {
        var suggestions = NameMatching.Suggest("Mark", new[] { "Volume", "Périmètre", "Département" });

        Assert.Empty(suggestions);
    }

    [Fact]
    public void Suggest_IsCapped()
    {
        var candidates = Enumerable.Range(0, 40).Select(i => $"Niveau {i}").ToArray();

        Assert.Equal(3, NameMatching.Suggest("Niveau", candidates, max: 3).Count);
    }

    [Fact]
    public void Suggest_IgnoresEmptyRequest()
    {
        Assert.Empty(NameMatching.Suggest("", new[] { "Niveau" }));
        Assert.Empty(NameMatching.Suggest("   ", new[] { "Niveau" }));
    }

    [Fact]
    public void Distance_IsSymmetricAndZeroOnEquality()
    {
        Assert.Equal(0, NameMatching.Distance("niveau", "niveau"));
        Assert.Equal(NameMatching.Distance("niveau", "nivau"), NameMatching.Distance("nivau", "niveau"));
        Assert.Equal(6, NameMatching.Distance("", "niveau"));
    }
}
