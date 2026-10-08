using System.Text.Json;

namespace DlssNrManager.Services;

public sealed record AiStudioLicenseInfo(
    string ModelId,
    string ModelName,
    string LicenseName,
    string OfficialModelUrl,
    string OfficialLicenseUrl,
    string SummaryEnglish,
    string SummaryFrench);

public sealed record AiStudioLicenseAcceptance(
    string ModelId,
    string LicenseName,
    string OfficialLicenseUrl,
    DateTimeOffset AcceptedAtUtc);

public sealed class AiStudioLicenseAcceptanceService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = true };

    private readonly LocalAiStudioService _studio;

    public AiStudioLicenseAcceptanceService(
        LocalAiStudioService studio)
    {
        _studio = studio;
    }

    public string AcceptancePath =>
        Path.Combine(
            _studio.Root,
            "licenses",
            "accepted.json");

    public static AiStudioLicenseInfo? GetInfo(
        AiStudioModelDescriptor model)
        => model.Id switch
        {
            "flux2-dev" => new(
                model.Id,
                model.DisplayName,
                "FLUX [dev] Non-Commercial License v2.0",
                "https://huggingface.co/black-forest-labs/FLUX.2-dev",
                "https://huggingface.co/black-forest-labs/FLUX.2-dev/blob/main/LICENSE.md",
                "Gated model. Use is subject to the FLUX [dev] license and Acceptable Use Policy. The weight license is non-commercial/non-production unless a separate commercial license applies.",
                "Modèle gated. Usage du modèle soumis à la licence FLUX [dev] et à l'Acceptable Use Policy. La licence des poids est non-commerciale/non-production sauf licence commerciale séparée."),

            "qwen-image-2.1" => new(
                model.Id,
                model.DisplayName,
                "Qwen RESEARCH LICENSE AGREEMENT",
                "https://huggingface.co/Qwen/Qwen-Image-2.1",
                "https://huggingface.co/Qwen/Qwen-Image-2.1/blob/main/LICENSE",
                "Research license. Commercial use requires a separate commercial license. Attribution and the license must be preserved when redistribution is permitted.",
                "Licence de recherche. L'utilisation commerciale nécessite une licence commerciale séparée. L'attribution et la licence doivent être conservées lors d'une redistribution autorisée."),

            "ltx-2.5" => new(
                model.Id,
                model.DisplayName,
                "LTX-2.x Community License",
                "https://huggingface.co/Lightricks/LTX-2.5-Pre-Trained",
                "https://huggingface.co/Lightricks/LTX-2.5-Pre-Trained",
                "LTX-2.x community license. Commercial rights depend on the license terms and revenue thresholds. Official access may be gated and require acceptance.",
                "Licence communautaire LTX-2.x. Les droits commerciaux dépendent notamment des conditions et seuils de revenus de la licence. L'accès officiel peut être gated et demander une acceptation."),

            _ => null
        };

    public bool IsAccepted(
        AiStudioModelDescriptor model)
    {
        var info = GetInfo(model);
        if (info == null)
            return true;

        return Load()
            .Any(x =>
                x.ModelId.Equals(
                    info.ModelId,
                    StringComparison.OrdinalIgnoreCase) &&
                x.LicenseName.Equals(
                    info.LicenseName,
                    StringComparison.Ordinal) &&
                x.OfficialLicenseUrl.Equals(
                    info.OfficialLicenseUrl,
                    StringComparison.OrdinalIgnoreCase));
    }

    public void Accept(
        AiStudioModelDescriptor model)
    {
        var info = GetInfo(model)
            ?? throw new InvalidOperationException(
                $"No manual license metadata is registered for {model.DisplayName}.");

        _studio.EnsureWorkspace();

        var directory =
            Path.GetDirectoryName(
                AcceptancePath)!;
        Directory.CreateDirectory(directory);

        var items =
            Load()
                .Where(x =>
                    !x.ModelId.Equals(
                        info.ModelId,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

        items.Add(
            new AiStudioLicenseAcceptance(
                info.ModelId,
                info.LicenseName,
                info.OfficialLicenseUrl,
                DateTimeOffset.UtcNow));

        File.WriteAllText(
            AcceptancePath,
            JsonSerializer.Serialize(
                items
                    .OrderBy(x => x.ModelId)
                    .ToArray(),
                JsonOptions));
    }

    public void Revoke(
        AiStudioModelDescriptor model)
    {
        if (!File.Exists(AcceptancePath))
            return;

        var items =
            Load()
                .Where(x =>
                    !x.ModelId.Equals(
                        model.Id,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

        File.WriteAllText(
            AcceptancePath,
            JsonSerializer.Serialize(
                items,
                JsonOptions));
    }

    private IReadOnlyList<AiStudioLicenseAcceptance>
        Load()
    {
        if (!File.Exists(AcceptancePath))
            return [];

        try
        {
            return JsonSerializer
                .Deserialize<
                    AiStudioLicenseAcceptance[]>(
                    File.ReadAllText(
                        AcceptancePath))
                ?? [];
        }
        catch
        {
            return [];
        }
    }
}
