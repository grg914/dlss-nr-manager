using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace DlssNrManager.Services;

public static class UiLocalizationService
{
    private sealed record Entry(string English, string French);

    private static readonly Entry[] Entries =
    [
        new("Navigation", "Navigation"),
        new("Games & DLSS", "Jeux & DLSS"),
        new("Minecraft RTX", "Minecraft RTX"),
        new("Media Neural", "Média neuronal"),
        new("AI Detection", "Détection IA"),
        new("PC Update Center", "Centre de mises à jour PC"),
        new("PC Cleanup", "Nettoyage PC"),
        new("Diagnostics", "Diagnostics"),
        new("Advanced OptiScaler", "OptiScaler avancé"),
        new("OptiScaler log", "Journal OptiScaler"),
        new("Application", "Application"),
        new("Game library", "Bibliothèque de jeux"),
        new("Compatible installations detected on this PC", "Installations compatibles détectées sur ce PC"),
        new("Scanning installed games…", "Analyse des jeux installés…"),
        new("Selected target", "Cible sélectionnée"),
        new("Installation path, runtime and package state", "Chemin d’installation, runtime et état du package"),
        new("Executable folder", "Dossier de l’exécutable"),
        new("Installed package", "Package installé"),
        new("DLSSNR runtime", "Runtime DLSSNR"),
        new("NVIDIA DLSS / Streamline runtime", "Runtime NVIDIA DLSS / Streamline"),
        new("Central manager for NVIDIA runtime files used by Jeux & DLSS. Manager-owned packages come from DLSS NR Manager releases; Minecraft RTX remains isolated on its protected Caustica Vulkan/NGX path.", "Gestionnaire central des runtimes NVIDIA utilisés par Jeux & DLSS. Les packages gérés proviennent des releases DLSS NR Manager ; Minecraft RTX reste isolé sur son pipeline Caustica Vulkan/NGX protégé."),
        new("No local DLSS / Streamline package selected", "Aucun package DLSS / Streamline local sélectionné"),
        new("No local nvngx_dlssnr.dll selected", "Aucun nvngx_dlssnr.dll local sélectionné"),
        new("Manager-owned runtime", "Runtime géré par l’application"),
        new("Not checked yet. Select a game, then validate or stage the manager-owned NVIDIA runtime.", "Pas encore vérifié. Sélectionnez un jeu, puis validez ou préparez le runtime NVIDIA géré."),
        new("Install & configure", "Installer et configurer"),
        new("Choose the upstream channel and the DLSS features supported by your RTX GPU", "Choisissez le canal de release et les fonctions DLSS prises en charge par votre GPU RTX"),
        new("Release channel", "Canal de release"),
        new("OptiScaler build", "Build OptiScaler"),
        new("Neural Rendering preset", "Préréglage Neural Rendering"),
        new("1 pass • Natural style", "1 passe • Style naturel"),
        new("Offline / single-player use only", "Utilisation hors ligne / solo uniquement"),
        new("Do not install graphics injection/modification files into multiplayer or anti-cheat-protected games unless the game developer explicitly allows it. Anti-cheat systems can treat injected proxy DLLs as tampering and account sanctions, including bans, are possible.", "N’installez pas de fichiers d’injection/modification graphique dans des jeux multijoueurs ou protégés par anti-cheat sauf autorisation explicite du développeur. Les systèmes anti-cheat peuvent considérer les DLL proxy injectées comme une altération et des sanctions de compte, y compris un bannissement, sont possibles."),
        new("Select a game to run the anti-cheat risk check.", "Sélectionnez un jeu pour lancer la vérification du risque anti-cheat."),
        new("Minecraft Java RTX", "Minecraft Java RTX"),
        new("Minecraft instance", "Instance Minecraft"),
        new("RTX preflight", "Pré-vérification RTX"),
        new("Select an instance to run compatibility checks.", "Sélectionnez une instance pour lancer les vérifications de compatibilité."),
        new("Run preflight", "Lancer la pré-vérification"),
        new("Open-source renderer stack", "Pile de rendu open source"),
        new("Checking tested Caustica RTX build…", "Vérification du build Caustica RTX testé…"),
        new("Fabric API (required)", "Fabric API (requis)"),
        new("Latest tested Caustica RTX 26.2 build", "Dernier build Caustica RTX 26.2 testé"),
        new("SPBRScandi resource pack", "Pack de ressources SPBRScandi"),
        new("Install DLSS / RTX", "Installer DLSS / RTX"),
        new("Check & update managed files", "Vérifier et mettre à jour les fichiers gérés"),
        new("Restore original", "Restaurer l’original"),
        new("Open Launcher", "Ouvrir le launcher"),
        new("NVIDIA runtime", "Runtime NVIDIA"),
        new("Media Neural Rendering", "Rendu neuronal média"),
        new("Apply Neural Rendering, AI super-resolution, or both to an image or video file.", "Appliquez le Neural Rendering, la super-résolution IA, ou les deux, à une image ou une vidéo."),
        new("Processing mode", "Mode de traitement"),
        new("Source file", "Fichier source"),
        new("Output folder", "Dossier de sortie"),
        new("Output size", "Taille de sortie"),
        new("Neural style", "Style neuronal"),
        new("Intensity", "Intensité"),
        new("AI Upscale", "Upscale IA"),
        new("Real-ESRGAN NCNN Vulkan • local GPU processing", "Real-ESRGAN NCNN Vulkan • traitement GPU local"),
        new("AI scale", "Échelle IA"),
        new("AI model", "Modèle IA"),
        new("Tile size", "Taille des tuiles"),
        new("AI Upscale engine not installed yet.", "Le moteur d’upscale IA n’est pas encore installé."),
        new("Media engine not checked yet.", "Le moteur média n’a pas encore été vérifié."),
        new("AI origin detection (beta)", "Détection d’origine IA (bêta)"),
        new("Media to analyze", "Média à analyser"),
        new("Analysis mode", "Mode d’analyse"),
        new("Actions", "Actions"),
        new("Detector not checked yet.", "Détecteur pas encore vérifié."),
        new("Read-only detection for software, Windows, drivers, connected devices, firmware and BIOS", "Détection en lecture seule des logiciels, de Windows, des pilotes, périphériques connectés, firmwares et BIOS"),
        new("Not scanned yet.", "Pas encore analysé."),
        new("Analyze and clean safe Windows temporary caches and NVIDIA shader caches", "Analyser et nettoyer les caches temporaires Windows sûrs et les caches de shaders NVIDIA"),
        new("Select the cache groups to analyze or clean.", "Sélectionnez les groupes de caches à analyser ou nettoyer."),
        new("Analyzed total: 0 B", "Total analysé : 0 o"),
        new("Compatibility diagnostics", "Diagnostics de compatibilité"),
        new("Run Diagnose game to verify renderer signals, OptiScaler load state, DLSSNR runtime and loader conflicts.", "Lancez Diagnostiquer le jeu pour vérifier le rendu, l’état de chargement d’OptiScaler, le runtime DLSSNR et les conflits de loader."),
        new("Proxy, FPS overlay, process filtering and loader integration", "Proxy, overlay FPS, filtrage de processus et intégration du loader"),
        new("Proxy DLL", "DLL proxy"),
        new("Overlay detail", "Détail de l’overlay"),
        new("Overlay position", "Position de l’overlay"),
        new("Target process", "Processus cible"),
        new("Latest loader and Neural Rendering output", "Dernière sortie du loader et du Neural Rendering"),
        new("RTX / DLSS compatibility manager", "Gestionnaire de compatibilité RTX / DLSS"),
        new("Version", "Version"),
        new("Detecting GPU…", "Détection du GPU…"),
        new("Checking RTX feature compatibility…", "Vérification de la compatibilité des fonctions RTX…"),
        new("Choose folder manually", "Choisir le dossier manuellement"),
        new("Add scan folder", "Ajouter un dossier d’analyse"),
        new("Clear custom scan folders", "Effacer les dossiers d’analyse personnalisés"),
        new("Scan all fixed drives (slower)", "Analyser tous les disques fixes (plus lent)"),
        new("Launch game", "Lancer le jeu"),
        new("Open folder", "Ouvrir le dossier"),
        new("Choose executable", "Choisir l’exécutable"),
        new("History", "Historique"),
        new("Diagnose game", "Diagnostiquer le jeu"),
        new("Select DLSS ZIP", "Sélectionner le ZIP DLSS"),
        new("Select DLSSNR DLL", "Sélectionner la DLL DLSSNR"),
        new("Check & stage manager runtime", "Vérifier et préparer le runtime géré"),
        new("Automatically resolve Neural Rendering from the manager-owned Streamline bundle", "Résoudre automatiquement le Neural Rendering depuis le bundle Streamline géré"),
        new("DLSS Super Resolution", "DLSS Super Resolution"),
        new("Frame Generation", "Frame Generation"),
        new("Reflex", "Reflex"),
        new("Neural Rendering", "Neural Rendering"),
        new("Stage selected package", "Préparer le package sélectionné"),
        new("Clear managed runtime", "Nettoyer le runtime géré"),
        new("Stable", "Stable"),
        new("Prerelease", "Préversion"),
        new("Quality — 100%", "Qualité — 100 %"),
        new("Balanced — 75%", "Équilibré — 75 %"),
        new("Performance — 50%", "Performance — 50 %"),
        new("Add missing manager-owned NVIDIA Streamline/DLSS resources to the selected game", "Ajouter au jeu sélectionné les ressources NVIDIA Streamline/DLSS gérées manquantes"),
        new("Install", "Installer"),
        new("Update", "Mettre à jour"),
        new("Apply preset", "Appliquer le préréglage"),
        new("Restore backup", "Restaurer la sauvegarde"),
        new("Uninstall", "Désinstaller"),
        new("Refresh", "Actualiser"),
        new("Verify managed files", "Vérifier les fichiers gérés"),
        new("Scan", "Analyser"),
        new("Choose folder", "Choisir le dossier"),
        new("Neural Rendering + AI Upscale", "Neural Rendering + Upscale IA"),
        new("Choose image / video", "Choisir une image / vidéo"),
        new("Native", "Natif"),
        new("Default", "Par défaut"),
        new("Natural", "Naturel"),
        new("Cinematic", "Cinématique"),
        new("General / Photo", "Général / Photo"),
        new("General / Conservative", "Général / Conservateur"),
        new("Illustration / Anime", "Illustration / Anime"),
        new("Anime Video / Minecraft", "Vidéo anime / Minecraft"),
        new("Auto", "Auto"),
        new("TTA quality mode (slower)", "Mode qualité TTA (plus lent)"),
        new("Set up AI Upscale engine", "Configurer le moteur d’upscale IA"),
        new("Set up media engine", "Configurer le moteur média"),
        new("Process media", "Traiter le média"),
        new("Add media", "Ajouter un média"),
        new("Remove", "Retirer"),
        new("Quick — model-native view / short videos", "Rapide — vue native du modèle / vidéos courtes"),
        new("Balanced — full-frame + spatial crops", "Équilibré — image complète + recadrages spatiaux"),
        new("Thorough — multi-scale + scene-aware video", "Approfondi — multi-échelle + vidéo sensible aux scènes"),
        new("Verify / repair detector", "Vérifier / réparer le détecteur"),
        new("Analyze media", "Analyser le média"),
        new("Cancel", "Annuler"),
        new("Export analysis report", "Exporter le rapport d’analyse"),
        new("Scan PC updates", "Analyser les mises à jour PC"),
        new("WinGet update all", "Tout mettre à jour avec WinGet"),
        new("Clear scan cache", "Effacer le cache d’analyse"),
        new("Open official page", "Ouvrir la page officielle"),
        new("Analyze caches", "Analyser les caches"),
        new("Clean selected", "Nettoyer la sélection"),
        new("Save support bundle", "Enregistrer le bundle de support"),
        new("Enable OptiScaler FPS overlay", "Activer l’overlay FPS OptiScaler"),
        new("FPS only", "FPS uniquement"),
        new("Simple — avg FPS + upscaler", "Simple — FPS moyens + upscaler"),
        new("Detailed", "Détaillé"),
        new("Detailed + graph", "Détaillé + graphique"),
        new("Full", "Complet"),
        new("Full + graph", "Complet + graphique"),
        new("Reflex timings", "Timings Reflex"),
        new("Top left", "En haut à gauche"),
        new("Top right", "En haut à droite"),
        new("Bottom left", "En bas à gauche"),
        new("Bottom right", "En bas à droite"),
        new("Ask OptiScaler to load ReShade64.dll", "Demander à OptiScaler de charger ReShade64.dll"),
        new("Install latest ReShade with full add-on support after install/update", "Installer le dernier ReShade avec prise en charge complète des add-ons après installation/mise à jour"),
        new("Automatically keep GitHub components up to date", "Maintenir automatiquement les composants GitHub à jour"),
        new("Install ReShade add-on", "Installer l’add-on ReShade"),
        new("Check component updates", "Vérifier les mises à jour des composants"),
        new("Apply advanced settings", "Appliquer les paramètres avancés"),
        new("Reload log", "Recharger le journal"),
        new("Check for app updates", "Rechercher les mises à jour de l’application"),
        new("Update available", "Mise à jour disponible"),
        new("Use software UI rendering", "Utiliser le rendu logiciel de l’interface"),
        new("Use hardware UI rendering", "Utiliser le rendu matériel de l’interface"),
        new("Scan installed games", "Analyser les jeux installés"),
        new("Clear cover cache", "Effacer le cache des jaquettes"),
        new("Open diagnostic logs", "Ouvrir les journaux de diagnostic"),
        new("Delete local app data", "Supprimer les données locales de l’application"),
        new("Language", "Langue"),
        new("Open game folder", "Ouvrir le dossier du jeu"),
        new("Copy game path", "Copier le chemin du jeu"),
        new("Rescan", "Réanalyser"),
        new("View history", "Voir l’historique"),
        new("Restore originals", "Restaurer les originaux"),
        new("Minecraft 26.2 • one-click Vulkan path tracing • DLSS Ray Reconstruction • Frame Generation/MFG • Reflex", "Minecraft 26.2 • path tracing Vulkan en un clic • DLSS Ray Reconstruction • Frame Generation/MFG • Reflex"),
        new("Checks: NVIDIA RTX GPU, driver, Vulkan RT, Java 25, Minecraft 26.2, Fabric Loader 0.19.3+, renderer conflicts and write access.", "Vérifications : GPU NVIDIA RTX, pilote, Vulkan RT, Java 25, Minecraft 26.2, Fabric Loader 0.19.3+, conflits de rendu et droits d’écriture."),
        new("Downloads Fabric Installer from the official Fabric Maven and Fabric API/optional mods from Modrinth. The tested Minecraft 26.2 Caustica RTX JAR is bundled into DLSS NR Manager releases when available, with the configured manager-owned Caustica fallback used when needed.", "Télécharge Fabric Installer depuis le Maven officiel Fabric et Fabric API/mods optionnels depuis Modrinth. Le JAR Caustica RTX testé pour Minecraft 26.2 est intégré aux releases DLSS NR Manager lorsqu’il est disponible, avec la source de secours Caustica gérée par l’application lorsque nécessaire."),
        new("Sodium and Iris are not installed in RTX mode: they replace/modify the rendering pipeline and can conflict with Caustica, which owns the Vulkan/path-traced renderer. The optional performance pack avoids renderer replacement; Caustica is experimental, so mod compatibility is still validated at launch. The default SPBRScandi pack keeps the compatible SPBR LabPBR terrain/material base and adds the validated Scandi sky, End, GUI and visual assets without the terrain textures that caused RT artifacts.", "Sodium et Iris ne sont pas installés en mode RTX : ils remplacent/modifient le pipeline de rendu et peuvent entrer en conflit avec Caustica, qui contrôle le rendu Vulkan/path traced. Le pack de performances optionnel évite de remplacer le renderer ; Caustica reste expérimental, la compatibilité des mods est donc vérifiée au lancement. Le pack SPBRScandi par défaut conserve la base terrain/matériaux SPBR LabPBR compatible et ajoute le ciel Scandi, l’End, le GUI et les assets visuels validés sans les textures de terrain qui provoquaient des artefacts RT."),
        new("One-click install and managed update both run the RTX preflight and create a backup first. Managed update refreshes manager-owned Minecraft components to the newest compatible releases, including the bundled Caustica RTX JAR and SPBRScandi pack, while preserving the restore path. Unsupported preflight results block changes; warnings require explicit confirmation.", "L’installation en un clic et la mise à jour gérée exécutent toutes deux la pré-vérification RTX et créent d’abord une sauvegarde. La mise à jour gérée actualise les composants Minecraft contrôlés vers les dernières releases compatibles, y compris le JAR Caustica RTX et le pack SPBRScandi, tout en conservant le chemin de restauration. Les résultats non pris en charge bloquent les modifications ; les avertissements exigent une confirmation explicite."),
        new("Managed automatically by the protected Caustica Vulkan/NGX pipeline. Manual DLSS / Streamline staging is intentionally unavailable in Minecraft RTX so generic game actions cannot inject OptiScaler proxies or alter the Caustica render path.", "Géré automatiquement par le pipeline Caustica Vulkan/NGX protégé. Le staging manuel DLSS / Streamline est volontairement indisponible dans Minecraft RTX afin que les actions génériques pour les jeux ne puissent pas injecter de proxies OptiScaler ni modifier le chemin de rendu Caustica."),
        new("Scan for a Minecraft Java instance.", "Rechercher une instance Minecraft Java."),
        new("Caustica RTX currently provides path tracing, DLSS Ray Reconstruction, Frame Generation/MFG and Reflex. DLSS Super Resolution is not falsely enabled by copying DLLs: it requires renderer support. The one-click installer relies on Caustica's implemented NVIDIA path; generic NVIDIA runtime staging is kept exclusively in Jeux & DLSS.", "Caustica RTX fournit actuellement le path tracing, DLSS Ray Reconstruction, Frame Generation/MFG et Reflex. DLSS Super Resolution n’est pas activé artificiellement par simple copie de DLL : il nécessite la prise en charge du renderer. L’installation en un clic repose sur le chemin NVIDIA implémenté par Caustica ; le staging NVIDIA générique reste exclusivement dans Jeux & DLSS."),
        new("Local synthetic-media screening • 2-model ensemble • multi-view/multi-scale analysis • temporal consistency • structured provenance metadata", "Détection locale de médias synthétiques • ensemble de 2 modèles • analyse multi-vue/multi-échelle • cohérence temporelle • métadonnées de provenance structurées"),
        new("Advisory forensic signal only. Current open detectors do not reliably generalize to every new generator; metadata can also be removed or spoofed. Prefer Uncertain when evidence conflicts.", "Signal forensique indicatif uniquement. Les détecteurs ouverts actuels ne généralisent pas de façon fiable à chaque nouveau générateur ; les métadonnées peuvent aussi être supprimées ou falsifiées. Préférez Incertain lorsque les indices se contredisent."),
        new("Screening is probabilistic, not proof. Missing provenance does not prove human origin.", "La détection est probabiliste et ne constitue pas une preuve. L’absence de provenance ne prouve pas une origine humaine."),
        new("PC scans remain read-only. The only install action in this panel is WinGet update all, which runs only after explicit confirmation. Driver, Windows Update, firmware and BIOS actions still only open official pages/settings.", "Les analyses PC restent en lecture seule. La seule action d’installation de ce panneau est Tout mettre à jour avec WinGet, exécutée uniquement après confirmation explicite. Les actions pilotes, Windows Update, firmware et BIOS ouvrent uniquement les pages/réglages officiels."),
        new("Targets are intentionally limited to known cache/temp directories: current-user temp, Windows Temp, DirectX shader cache, NVIDIA DXCache, GLCache and NV_Cache. Locked files and inaccessible locations are skipped. Browser data, documents, downloads, registry entries, restore points and Windows Update storage are never touched.", "Les cibles sont volontairement limitées aux dossiers de cache/temp connus : temp de l’utilisateur courant, Windows Temp, cache de shaders DirectX, NVIDIA DXCache, GLCache et NV_Cache. Les fichiers verrouillés et emplacements inaccessibles sont ignorés. Les données de navigateur, documents, téléchargements, entrées de registre, points de restauration et stockage Windows Update ne sont jamais modifiés."),
        new("F10 opens the OptiScaler menu. Cyberpunk 2077 uses dbghelp.dll as its validated recommendation. Leave TargetProcessName empty unless a game requires explicit process filtering.", "F10 ouvre le menu OptiScaler. Cyberpunk 2077 utilise dbghelp.dll comme recommandation validée. Laissez TargetProcessName vide sauf si un jeu exige un filtrage explicite du processus."),
        new("Uses the validated manager-owned Streamline bundle from DLSS NR Manager releases and copies only missing SR/FG/Reflex/NR runtime DLLs. Existing game DLLs are never overwritten.", "Utilise le bundle Streamline géré et validé des releases DLSS NR Manager et copie uniquement les DLL runtime SR/FG/Reflex/NR manquantes. Les DLL existantes du jeu ne sont jamais écrasées."),
        new("Independent source used only by AI origin detection.", "Source indépendante utilisée uniquement par la détection d’origine IA."),
        new("Exports sanitized manager/game diagnostics to a ZIP you choose.", "Exporte les diagnostics assainis du manager/jeu dans un ZIP de votre choix."),
        new("Leave empty to keep TargetProcessName=auto", "Laisser vide pour conserver TargetProcessName=auto"),
        new("Checking GitHub…", "Vérification de GitHub…"),
        new("No compatible ZIP release found", "Aucune release ZIP compatible trouvée"),
        new("No compatible game detected", "Aucun jeu compatible détecté"),
        new("Game scan failed", "Échec de l’analyse des jeux"),
        new("Reading installation state…", "Lecture de l’état de l’installation…"),
        new("Unable to inspect selected game", "Impossible d’inspecter le jeu sélectionné"),
        new("Cover cache cleared. Reloading artwork…", "Cache des jaquettes effacé. Rechargement des visuels…"),
        new("Inspecting selected folder…", "Inspection du dossier sélectionné…"),
        new("Validating runtime…", "Validation du runtime…")
    ];

    private static readonly Dictionary<string, Entry> Lookup =
        BuildLookup();

    public static string NormalizeLanguage(string? language)
        => !string.IsNullOrWhiteSpace(language) &&
           language.StartsWith(
               "fr",
               StringComparison.OrdinalIgnoreCase)
            ? "fr"
            : "en";

    public static string Translate(
        string? value,
        string language)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;

        var normalized = NormalizeLanguage(language);

        if (Lookup.TryGetValue(value, out var entry))
            return normalized == "fr"
                ? entry.French
                : entry.English;

        return TranslateDynamic(value, normalized);
    }

    private static Dictionary<string, Entry> BuildLookup()
    {
        var result = new Dictionary<string, Entry>(
            StringComparer.Ordinal);

        foreach (var entry in Entries)
        {
            result[entry.English] = entry;
            result[entry.French] = entry;
        }

        return result;
    }

    private static string TranslateDynamic(
        string value,
        string language)
    {
        if (language == "fr")
        {
            if (value.StartsWith("Selected: ", StringComparison.Ordinal))
                return "Sélectionné : " +
                       value["Selected: ".Length..]
                           .Replace(" • prerelease", " • préversion", StringComparison.Ordinal)
                           .Replace(" • stable", " • stable", StringComparison.Ordinal);

            if (value.StartsWith("Installed OptiScaler package: ", StringComparison.Ordinal))
                return "Package OptiScaler installé : " +
                       value["Installed OptiScaler package: ".Length..];

            if (value.StartsWith("Version v", StringComparison.Ordinal) &&
                value.EndsWith(" • latest", StringComparison.Ordinal))
                return value[..^" • latest".Length] + " • à jour";

            if (value.StartsWith("Version v", StringComparison.Ordinal) &&
                value.EndsWith(" • update available", StringComparison.Ordinal))
                return value[..^" • update available".Length] + " • mise à jour disponible";
        }
        else
        {
            if (value.StartsWith("Sélectionné : ", StringComparison.Ordinal))
                return "Selected: " +
                       value["Sélectionné : ".Length..]
                           .Replace(" • préversion", " • prerelease", StringComparison.Ordinal);

            if (value.StartsWith("Package OptiScaler installé : ", StringComparison.Ordinal))
                return "Installed OptiScaler package: " +
                       value["Package OptiScaler installé : ".Length..];

            if (value.StartsWith("Version v", StringComparison.Ordinal) &&
                value.EndsWith(" • à jour", StringComparison.Ordinal))
                return value[..^" • à jour".Length] + " • latest";

            if (value.StartsWith("Version v", StringComparison.Ordinal) &&
                value.EndsWith(" • mise à jour disponible", StringComparison.Ordinal))
                return value[..^" • mise à jour disponible".Length] + " • update available";
        }

        return value;
    }
}

public sealed class UiLocalizationController
{
    private readonly DependencyObject _root;
    private readonly Func<string> _languageProvider;
    private readonly HashSet<DependencyObject> _observed = [];

    public UiLocalizationController(
        DependencyObject root,
        Func<string> languageProvider)
    {
        _root = root;
        _languageProvider = languageProvider;
    }

    public void Apply()
        => ApplyRecursive(_root);

    private void ApplyRecursive(DependencyObject node)
    {
        TranslateNode(node);
        Observe(node);

        foreach (var child in LogicalTreeHelper.GetChildren(node))
        {
            if (child is DependencyObject dependencyObject)
                ApplyRecursive(dependencyObject);
        }
    }

    private void Observe(DependencyObject node)
    {
        if (!_observed.Add(node))
            return;

        if (node is TextBlock)
        {
            DependencyPropertyDescriptor.FromProperty(
                    TextBlock.TextProperty,
                    typeof(TextBlock))
                ?.AddValueChanged(
                    node,
                    (_, _) => TranslateNode(node));
        }

        if (node is TextBox)
        {
            DependencyPropertyDescriptor.FromProperty(
                    TextBox.TextProperty,
                    typeof(TextBox))
                ?.AddValueChanged(
                    node,
                    (_, _) => TranslateNode(node));
        }

        if (node is ContentControl)
        {
            DependencyPropertyDescriptor.FromProperty(
                    ContentControl.ContentProperty,
                    typeof(ContentControl))
                ?.AddValueChanged(
                    node,
                    (_, _) => TranslateNode(node));
        }

        if (node is HeaderedContentControl)
        {
            DependencyPropertyDescriptor.FromProperty(
                    HeaderedContentControl.HeaderProperty,
                    typeof(HeaderedContentControl))
                ?.AddValueChanged(
                    node,
                    (_, _) => TranslateNode(node));
        }

        if (node is FrameworkElement)
        {
            DependencyPropertyDescriptor.FromProperty(
                    FrameworkElement.ToolTipProperty,
                    typeof(FrameworkElement))
                ?.AddValueChanged(
                    node,
                    (_, _) => TranslateNode(node));
        }
    }

    private void TranslateNode(DependencyObject node)
    {
        var language = _languageProvider();

        if (node is TextBlock textBlock)
        {
            var translated = UiLocalizationService.Translate(
                textBlock.Text,
                language);
            if (!string.Equals(
                    translated,
                    textBlock.Text,
                    StringComparison.Ordinal))
            {
                textBlock.Text = translated;
            }
        }

        if (node is TextBox textBox)
        {
            var translated = UiLocalizationService.Translate(
                textBox.Text,
                language);
            if (!string.Equals(
                    translated,
                    textBox.Text,
                    StringComparison.Ordinal))
            {
                textBox.Text = translated;
            }
        }

        if (node is ContentControl contentControl &&
            contentControl.Content is string content)
        {
            var translated = UiLocalizationService.Translate(
                content,
                language);
            if (!string.Equals(
                    translated,
                    content,
                    StringComparison.Ordinal))
            {
                contentControl.Content = translated;
            }
        }

        if (node is HeaderedContentControl headered &&
            headered.Header is string header)
        {
            var translated = UiLocalizationService.Translate(
                header,
                language);
            if (!string.Equals(
                    translated,
                    header,
                    StringComparison.Ordinal))
            {
                headered.Header = translated;
            }
        }

        if (node is FrameworkElement element &&
            element.ToolTip is string toolTip)
        {
            var translated = UiLocalizationService.Translate(
                toolTip,
                language);
            if (!string.Equals(
                    translated,
                    toolTip,
                    StringComparison.Ordinal))
            {
                element.ToolTip = translated;
            }
        }
    }
}
