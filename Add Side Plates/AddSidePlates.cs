using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Cloners;
using Range = SemanticVersioning.Range;

namespace AddSidePlates;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.drollercandy.addsideplates";
    public string Name { get; init; } = "Add Side Plates";
    public string Author { get; init; } = "DrollerCandy";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("0.1.0");
    public Range SptVersion { get; init; } = new("~4.1.5");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, Range>? ModDependencies { get; init; } = new()
    {
        { "com.wtt.commonlib", new Range("~3.0.0") }
    };
    public string? Url { get; init; }
    public string License { get; init; } = "All Rights Reserved";
}

[Injectable(TypePriority = OnLoadOrder.GameCallbacks + 1)]
public class AddSidePlates(
    ISptLogger<AddSidePlates> logger,
    TemplateTable templateTable,
    ICloner cloner,
    ModHelper modHelper
) : IOnLoad
{
    private static readonly string[] SidePlateSlotNames = ["Left_side_plate", "Right_side_plate"];

    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var configPath = Path.Combine(
            modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()),
            "config.json"
        );

        if (!File.Exists(configPath))
        {
            logger.Warning($"Side plate config not found: {configPath}");
            return;
        }

        AddSidePlatesConfig? config;
        try
        {
            await using var configStream = File.OpenRead(configPath);
            config = await JsonSerializer.DeserializeAsync<AddSidePlatesConfig>(
                configStream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken: cancellationToken
            );
        }
        catch (Exception exception)
        {
            logger.Error($"Failed to read side plate config: {exception.Message}");
            return;
        }

        if (config is null || config.ItemIds.Count == 0)
        {
            logger.Warning("No item IDs are configured for side plate slots.");
            return;
        }

        if (!templateTable.Items.TryGetValue(config.SlotTemplateItemId, out var slotTemplate))
        {
            logger.Error($"Side plate slot template not found: {config.SlotTemplateItemId}");
            return;
        }

        var templateSlots = slotTemplate.Properties?.Slots;
        if (templateSlots is null)
        {
            logger.Error($"Side plate slot template has no slots: {config.SlotTemplateItemId}");
            return;
        }

        var sourceSlots = SidePlateSlotNames
            .Select(name => templateSlots.FirstOrDefault(slot =>
                string.Equals(slot.Name, name, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (sourceSlots.Any(slot => slot is null))
        {
            logger.Error($"Side plate slot template is missing one or more required slots: {config.SlotTemplateItemId}");
            return;
        }

        foreach (var configuredId in config.ItemIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var itemId = configuredId.Trim();

            if (itemId.Length == 0 || !templateTable.Items.TryGetValue(itemId, out var item))
            {
                logger.Warning($"Configured side plate item was not found: '{configuredId}'");
                continue;
            }

            if (item.Properties is null)
            {
                logger.Warning($"Configured side plate item has no properties: {itemId}");
                continue;
            }

            var itemSlots = item.Properties.Slots?.ToList() ?? [];
            foreach (var index in Enumerable.Range(0, SidePlateSlotNames.Length))
            {
                var slotName = SidePlateSlotNames[index];
                if (itemSlots.Any(slot =>
                    string.Equals(slot.Name, slotName, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var slot = cloner.Clone(sourceSlots[index]!);
                if (slot is null)
                {
                    logger.Warning($"Could not clone {slotName} for item {itemId}");
                    continue;
                }

                slot.Id = CreateSlotId(itemId, slotName);
                slot.Parent = itemId;
                itemSlots.Add(slot);
            }

            item.Properties.Slots = itemSlots;
        }
    }

    private static string CreateSlotId(string itemId, string slotName)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{itemId}:{slotName}"));
        return Convert.ToHexString(bytes.AsSpan(0, 12)).ToLowerInvariant();
    }
}

public sealed class AddSidePlatesConfig
{
    public string SlotTemplateItemId { get; init; } = "5c0e722886f7740458316a57";
    public List<string> ItemIds { get; init; } = [];
}