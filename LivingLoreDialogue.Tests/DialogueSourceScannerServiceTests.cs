using LivingLoreDialogue.Services;
using Xunit;

namespace LivingLoreDialogue.Tests;

public sealed class DialogueSourceScannerServiceTests
{
    [Fact]
    public void ExtractsFlatDialogueJson()
    {
        string json = """
        {
          "spring_1": "Nice weather for patrol.",
          "Rainy_Day_0": "Rain keeps the dust down."
        }
        """;

        DialogueJsonExtractionPreview preview = DialogueSourceScannerService.PreviewJsonExtractionForTests(
            json,
            @"Mods\TestMod\assets\CharacterFiles\Dialogue\Lance\Dialogue.json");

        Assert.Equal("strict JSON", preview.ParserUsed);
        Assert.Equal("HasDialogue", preview.Classification);
        Assert.Contains(preview.Pairs, pair => pair.Key == "spring_1" && pair.Value.Contains("patrol"));
        Assert.Contains(preview.Pairs, pair => pair.Key == "Rainy_Day_0" && pair.Value.Contains("dust"));
    }

    [Fact]
    public void ExtractsContentPatcherEntriesJson()
    {
        string json = """
        {
          "Changes": [
            {
              "Action": "EditData",
              "Target": "Characters/Dialogue/Lance",
              "Entries": {
                "summer_5": "The highlands are calm today."
              }
            }
          ]
        }
        """;

        DialogueJsonExtractionPreview preview = DialogueSourceScannerService.PreviewJsonExtractionForTests(
            json,
            @"Mods\TestMod\content.json");

        Assert.Equal("HasDialogue", preview.Classification);
        Assert.Contains(preview.Pairs, pair => pair.Key.EndsWith("summer_5") && pair.Value.Contains("highlands"));
    }

    [Fact]
    public void ExtractsNestedChangesJson()
    {
        string json = """
        {
          "Changes": [
            {
              "Target": "Characters/Dialogue/Claire",
              "Changes": {
                "Claire": {
                  "Marriage": {
                    "Indoor_Day_0": "I made coffee before work."
                  }
                }
              }
            }
          ]
        }
        """;

        DialogueJsonExtractionPreview preview = DialogueSourceScannerService.PreviewJsonExtractionForTests(
            json,
            @"Mods\TestMod\content.json");

        Assert.Equal("HasDialogue", preview.Classification);
        Assert.Contains(preview.Pairs, pair => pair.Key.Contains("Indoor_Day_0") && pair.Value.Contains("coffee"));
    }

    [Fact]
    public void ExtractsArraysContainingDialogueObjects()
    {
        string json = """
        {
          "Entries": {
            "Scarlett": [
              { "key": "fall_12", "text": "I should get back before sundown." },
              { "Dialogue": "This valley still surprises me." }
            ]
          }
        }
        """;

        DialogueJsonExtractionPreview preview = DialogueSourceScannerService.PreviewJsonExtractionForTests(
            json,
            @"Mods\TestMod\assets\CharacterFiles\Dialogue\Scarlett\Dialogue.json");

        Assert.Equal("HasDialogue", preview.Classification);
        Assert.Contains(preview.Pairs, pair => pair.Value.Contains("sundown"));
        Assert.Contains(preview.Pairs, pair => pair.Value.Contains("surprises"));
    }

    [Fact]
    public void RecoversMalformedJsonWithRawCarriageReturnInsideString()
    {
        string json = "{ \"spring_1\": \"First line\rsecond line\" }";

        DialogueJsonExtractionPreview preview = DialogueSourceScannerService.PreviewJsonExtractionForTests(
            json,
            @"Mods\TestMod\assets\CharacterFiles\Dialogue\Lance\MarriageDialogue.json");

        Assert.Equal("lenient JSON", preview.ParserUsed);
        Assert.Equal("LenientJsonRecovered", preview.Classification);
        Assert.Contains(preview.Pairs, pair => pair.Value.Contains("First line"));
    }

    [Fact]
    public void ClassifiesValidEmptyDialogueJsonAsExpectedEmpty()
    {
        DialogueJsonExtractionPreview preview = DialogueSourceScannerService.PreviewJsonExtractionForTests(
            "{ }",
            @"Mods\TestMod\assets\CharacterFiles\Dialogue\Scarlett\FakeDialogue.json");

        Assert.Equal("strict JSON", preview.ParserUsed);
        Assert.Equal("ExpectedEmptyDialogueVariant", preview.Classification);
        Assert.Empty(preview.Pairs);
    }
}
