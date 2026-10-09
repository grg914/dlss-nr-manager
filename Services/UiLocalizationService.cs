using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace DlssNrManager.Services;

public static class UiLocalizationService
{
    private sealed record Entry(string English, string French);

    private static readonly Entry[] Entries =
    [
        // v4.0: French-first WPF literals must also render correctly in English.
        new("Never output HDR", "Jamais sortir en HDR"),
        new("Official tool versions", "Versions officielles des outils"),
        new("Compare official repositories against tracked sources (without automatic installation).", "Comparer les dépôts officiels aux sources suivies (sans installation automatique)."),
        new("Check official versions", "Vérifier versions officielles"),
        new("View official repository", "Voir le dépôt officiel"),
        new("On-demand check only.", "Contrôle à la demande uniquement."),
        new("Download confirmation is required above 1 GB.", "Confirmation obligatoire au-delà de 1 Go."),
        new("GAME", "JEU"),
        new("0 % • 0.0 MB/s • calculating…", "0 % • 0,0 Mo/s • calcul…"),
        new("Navigation", "Navigation"),
        new("Search every fixed local drive for supported game installations. This can take longer.", "Recherche les jeux sur tous les disques locaux fixes. L'analyse peut être plus longue."),
        new("Automatically locate the validated manager-owned Neural Rendering runtime when compatible.", "Recherche automatiquement le runtime Neural Rendering validé et géré par l'application lorsqu'il est compatible."),
        new("Enable DLSS Super Resolution for the selected game when the integration supports it.", "Active DLSS Super Resolution pour le jeu sélectionné si son intégration le permet."),
        new("Request Frame Generation only on compatible RTX hardware and supported games.", "Demande Frame Generation uniquement sur les cartes RTX et les jeux compatibles."),
        new("Enable NVIDIA Reflex latency reduction where supported by the selected game.", "Active la réduction de latence NVIDIA Reflex si le jeu la prend en charge."),
        new("Request Neural Rendering only when the GPU, runtime and game integration support it.", "Demande Neural Rendering uniquement si le GPU, le runtime et le jeu sont compatibles."),
        new("Install Fabric API, required by the selected Minecraft RTX mod set.", "Installe Fabric API, nécessaire aux mods Minecraft RTX sélectionnés."),
        new("Select the latest tested Caustica RTX build supported by the Minecraft integration.", "Sélectionne la dernière version testée de Caustica RTX prise en charge par l'intégration Minecraft."),
        new("Include compatible Minecraft performance mods without replacing the Caustica renderer.", "Ajoute des mods de performance compatibles sans remplacer le moteur de rendu Caustica."),
        new("Install the SPBRScandi resource pack with the managed Minecraft RTX profile.", "Installe le pack de ressources SPBRScandi avec le profil Minecraft RTX géré."),
        new("Test-time augmentation can improve some AI upscales but increases processing time.", "L'augmentation à l'inférence peut améliorer certains agrandissements IA, mais ralentit le traitement."),
        new("Apply NVIDIA Video Super Resolution during VLC playback without changing the source video.", "Applique NVIDIA Video Super Resolution dans VLC sans modifier la vidéo source."),
        new("Reduce visible compression artifacts during supported VLC playback.", "Réduit les artefacts de compression visibles pendant la lecture VLC compatible."),
        new("Enable a selected playback scaling factor in VLC windowed mode.", "Active le facteur d'agrandissement choisi pour la lecture VLC en fenêtre."),
        new("Play the video in fullscreen; scaling follows the monitor resolution.", "Lit la vidéo en plein écran avec une mise à l'échelle adaptée à l'écran."),
        new("Show a small VSR and HDR status indicator during VLC playback.", "Affiche un petit indicateur d'état VSR et HDR pendant la lecture VLC."),
        new("Request NVIDIA RTX Video HDR only if the system and display support HDR.", "Demande NVIDIA RTX Video HDR seulement si le système et l'écran sont compatibles HDR."),
        new("Use local Real-ESRGAN to upscale the output video; processing takes time.", "Utilise Real-ESRGAN localement pour agrandir la vidéo exportée ; le traitement prend du temps."),
        new("Enable manual output resolution scaling for the processed video.", "Active le réglage manuel de la résolution de la vidéo traitée."),
        new("Apply the available Neural Rendering processing stage to the exported video.", "Applique l'étape Neural Rendering disponible à la vidéo exportée."),
        new("Run conservative artifact cleanup before writing the processed output.", "Effectue un nettoyage modéré des artefacts avant l'exportation."),
        new("Show OptiScaler's in-game FPS overlay when its hook is active.", "Affiche les FPS dans le jeu lorsque le module OptiScaler est actif."),
        new("Ask the OptiScaler loader to start ReShade64.dll if it is installed.", "Demande au chargeur OptiScaler de lancer ReShade64.dll si disponible."),
        new("Offer ReShade installation with add-on support after the game update.", "Propose l'installation de ReShade avec prise en charge des extensions après la mise à jour du jeu."),
        new("Allow supported manager-owned components to be refreshed on app startup.", "Autorise l'actualisation des composants gérés compatibles au démarrage de l'application."),
        new("Choose whether to install stable or prerelease builds from the selected channel.", "Choisit les versions stables ou de préversion du canal sélectionné."),
        new("Select the OptiScaler build to install in the selected game.", "Sélectionne la version d'OptiScaler à installer dans le jeu."),
        new("Choose the Neural Rendering internal rendering scale; lower values can affect image quality.", "Choisit l'échelle de rendu interne de Neural Rendering ; les valeurs basses peuvent réduire la qualité."),
        new("Choose the detected Minecraft installation that will receive the RTX components.", "Choisit l'installation Minecraft détectée qui recevra les composants RTX."),
        new("Choose Neural Rendering, AI upscale or combined media processing.", "Choisit Neural Rendering, l'agrandissement IA ou un traitement combiné."),
        new("Set the desired output size for the image or video processing task.", "Définit la taille de sortie du traitement d'image ou de vidéo."),
        new("Select a visual processing style; results depend on the input.", "Sélectionne un style de traitement visuel ; le résultat dépend de la source."),
        new("Choose the enlargement multiplier for Real-ESRGAN.", "Choisit le facteur d'agrandissement de Real-ESRGAN."),
        new("Choose an AI model suited to photographs, illustrations or video frames.", "Choisit un modèle IA adapté aux photos, illustrations ou images vidéo."),
        new("Choose AI tile size based on available GPU memory; AUTO is recommended.", "Choisit la taille des blocs IA selon la mémoire GPU ; AUTO est recommandé."),
        new("Choose the VLC windowed playback scaling multiplier.", "Choisit le facteur d'agrandissement pendant la lecture VLC en fenêtre."),
        new("Choose whether to preserve, request or disable HDR during VLC playback.", "Choisit de conserver, demander ou désactiver le HDR pendant la lecture VLC."),
        new("Choose the video export scaling factor; higher values take longer.", "Choisit le facteur d'agrandissement vidéo à l'exportation ; les valeurs élevées sont plus lentes."),
        new("Choose the local AI model used for permanently exported video enhancement.", "Choisit le modèle IA local utilisé pour améliorer la vidéo exportée."),
        new("Choose the duration and thoroughness of AI-origin analysis.", "Choisit la durée et la profondeur de l'analyse de l'origine IA."),
        new("Choose which image or video generation task AI Studio should run.", "Choisit la tâche de génération d'image ou de vidéo dans AI Studio."),
        new("Choose the local processing backend or leave automatic selection enabled.", "Choisit le moteur de traitement local ou conserve la sélection automatique."),
        new("Choose the installed AI Studio model suitable for the current task.", "Choisit le modèle AI Studio installé adapté à la tâche."),
        new("Choose the loader DLL name appropriate for the selected game's graphics API.", "Choisit le nom de DLL du chargeur adapté à l'API graphique du jeu."),
        new("Choose which performance metrics appear in the OptiScaler overlay.", "Choisit les mesures de performance affichées dans l'overlay OptiScaler."),
        new("Choose the on-screen location of the OptiScaler performance overlay.", "Choisit la position de l'overlay de performances OptiScaler."),
        new("Change the application interface language between French and English.", "Change la langue de l'interface entre le français et l'anglais."),
        new("Adjust the strength of the media enhancement effect.", "Règle l'intensité de l'effet d'amélioration du média."),
        new("Hardware & Profiles", "Matériels et Profils"),
        new("Read-only hardware detection and capability-safe preferences", "Détection matérielle en lecture seule et préférences compatibles"),
        new("Hardware profile", "Profil matériel"),
        new("Compatible mode", "Mode Compatible"),
        new("Uncheck Compatible mode to use Normal mode.", "Décochez Mode Compatible pour utiliser le mode Normal."),
        new("Refresh hardware detection", "Actualiser la détection matérielle"),
        new("Hardware detection pending.", "Détection matérielle en attente."),
        new("No hardware scan completed yet.", "Aucune analyse matérielle effectuée."),
        new("DirectX and Vulkan loader detection does not guarantee feature support. Game, driver and runtime compatibility are checked separately.", "La détection des chargeurs DirectX et Vulkan ne garantit pas les fonctions disponibles. La compatibilité du jeu, du pilote et du runtime est vérifiée séparément."),
        new("AUTO detects your actual GPU and CPU. A manual preset never unlocks unsupported hardware features.", "AUTO détecte le GPU et le CPU réels. Un profil manuel ne débloque jamais de fonctions matérielles incompatibles."),
        new("Compatible mode conservatively disables optional Frame Generation and Neural Rendering while retaining supported Super Resolution.", "Le mode Compatible désactive prudemment Frame Generation et Neural Rendering tout en conservant Super Resolution si compatible."),
        new("Recheck your GPU, CPU, memory, NVIDIA driver and graphics API loaders without modifying drivers.", "Revérifie GPU, CPU, mémoire, pilote NVIDIA et chargeurs graphiques sans modifier les pilotes."),
        new("Games & DLSS", "Jeux & DLSS"),
        new("Minecraft RTX", "Minecraft RTX"),
        new("Media Neural", "Média neuronal"),
        new("VSR-HDR Vidéo", "VSR-HDR Vidéo"),
        new("VSR-HDR VLC (Direct)", "VSR-HDR VLC (Direct)"),
        new("Restore HD Vidéo", "Restore HD Vidéo"),
        new("Indicateur VSR/HDR en haut à droite", "Indicateur VSR/HDR en haut à droite"),
        new("Real-time NVIDIA VSR / RTX Video HDR for local playback through VLC Direct3D11.", "NVIDIA VSR / RTX Video HDR en temps réel pour la lecture locale via VLC Direct3D11."),
        new("Local video", "Vidéo locale"),
        new("Choose video", "Choisir une vidéo"),
        new("VLC runtime", "Runtime VLC"),
        new("Set up portable VLC", "Configurer VLC portable"),
        new("Refresh VLC detection", "Actualiser la détection VLC"),
        new("Real-time NVIDIA / D3D11", "NVIDIA / D3D11 en temps réel"),
        new("No transcoding: VLC enhances frames during playback and the original file remains unchanged.", "Sans transcodage : VLC améliore les images pendant la lecture et le fichier original reste inchangé."),
        new("Super Resolution / NVIDIA VSR", "Super Resolution / NVIDIA VSR"),
        new("Video artifact reduction", "Réduction des artefacts vidéo"),
        new("RTX VSR cleanup for compression/blocking artifacts. VLC exposes this through the same NVIDIA D3D11 VSR extension, so this option uses the VSR path.", "Nettoyage RTX VSR des artefacts de compression/blocs. VLC expose ce traitement via la même extension NVIDIA D3D11 VSR."),
        new("HDR / RTX Video HDR", "HDR / RTX Video HDR"),
        new("Window scale", "Échelle de fenêtre"),
        new("Display mode", "Mode d’affichage"),
        new("Fullscreen", "Plein écran"),
        new("x1 — native window", "x1 — fenêtre native"),
        new("x2 — 2× window", "x2 — fenêtre 2×"),
        new("x4 — 4× window", "x4 — fenêtre 4×"),
        new("HDR output", "Sortie HDR"),
        new("Auto — preserve source", "Auto — préserver la source"),
        new("Generate HDR from SDR — RTX Video HDR / TrueHDR", "Générer du HDR depuis SDR — RTX Video HDR / TrueHDR"),
        new("Always output HDR", "Toujours sortir en HDR"),
        new("Never output HDR", "Ne jamais sortir en HDR"),
        new("Play in VLC with enhancement", "Lire dans VLC avec amélioration"),
        new("Amélioration permanente : restaure, nettoie et upscale la vidéo puis enregistre une nouvelle copie dans le dossier choisi.", "Amélioration permanente : restaure, nettoie et upscale la vidéo puis enregistre une nouvelle copie dans le dossier choisi."),
        new("Source video", "Vidéo source"),
        new("Permanent scale", "Échelle permanente"),
        new("x1 — enhance at native resolution", "x1 — améliorer à la résolution native"),
        new("x2 — AI upscale 2×", "x2 — upscale IA 2×"),
        new("x4 — AI upscale 4×", "x4 — upscale IA 4×"),
        new("Neural Rendering pre-pass", "Pré-passe Neural Rendering"),
        new("Artifact reduction / cleanup", "Réduction des artefacts / nettoyage"),
        new("Neural Rendering, artifact cleanup and AI upscale can be enabled independently. Artifact cleanup uses a conservative local neural pre-pass; x2/x4 AI upscale uses Real-ESRGAN. Processing is local and may be slow.", "Neural Rendering, nettoyage des artefacts et upscale IA peuvent être activés indépendamment. Le nettoyage utilise une pré-passe neuronale locale conservatrice ; l’upscale x2/x4 utilise Real-ESRGAN. Le traitement est local et peut être lent."),
        new("Enhance & save video", "Améliorer et enregistrer la vidéo"),
        new("AI Detection", "Détection IA"),
        new("VSR-HDR Video", "VSR-HDR Vidéo"),
        new("Render scale", "Échelle de rendu"),
        new("Auto — keep source", "Auto — conserver la source"),
        new("Output scale", "Échelle de sortie"),
        new("x1 — native resolution", "x1 — résolution native"),
        new("Choose a video and an output folder.", "Choisissez une vidéo et un dossier de sortie."),
        new("Uses VLC Direct3D11 Super Resolution for real-time upscaling when supported by the NVIDIA driver.", "Utilise VLC Direct3D11 Super Resolution pour l’upscale en temps réel lorsque le pilote NVIDIA le prend en charge."),
        new("x1/x2/x4 applies to windowed playback. Fullscreen targets the monitor resolution directly so the scale selector is disabled to avoid crop/zoom.", "x1/x2/x4 s’applique à la lecture fenêtrée. Le plein écran cible directement la résolution du moniteur ; le sélecteur d’échelle est donc désactivé pour éviter le recadrage/zoom."),
        new("SDR → HDR requires a compatible NVIDIA GPU/driver, an HDR display and Windows HDR enabled.", "SDR → HDR nécessite un GPU/pilote NVIDIA compatible, un écran HDR et le HDR Windows activé."),
        new("Checking manager-owned VLC runtime…", "Vérification du runtime VLC géré…"),
        new("Manager-owned VLC 3.0.24 already installed • offline-ready.", "VLC 3.0.24 géré déjà installé • prêt hors ligne."),
        new("Using bundled manager-owned VLC 3.0.24 • no network required.", "Utilisation du VLC 3.0.24 géré inclus • aucun réseau requis."),
        new("Downloading manager-owned VLC 3.0.24…", "Téléchargement du VLC 3.0.24 géré…"),
        new("Manager-owned VLC 3.0.24 ready.", "VLC 3.0.24 géré prêt."),
        new("VLC was not detected. Install VLC 3.0.24 or newer.", "VLC n’a pas été détecté. Installez VLC 3.0.24 ou une version plus récente."),
        new("VLC detection not checked yet.", "Détection VLC pas encore vérifiée."),
        new("VSR/HDR indicator at top right", "Indicateur VSR/HDR en haut à droite"),
        new("Select a local video, then launch VLC.", "Sélectionnez une vidéo locale, puis lancez VLC."),
        new("Restore HD Video", "Restore HD Vidéo"),
        new("Permanent enhancement: restore, clean and upscale the video, then save a new copy in the selected folder.", "Amélioration permanente : restaure, nettoie et upscale la vidéo puis enregistre une nouvelle copie dans le dossier choisi."),
        new("For a blurry video: start with Neural Rendering + artifact reduction + x2 using General / Conservative. x4 can invent or flicker details absent from the source; keep it as a manual choice.", "Pour une vidéo floue : commence par Neural Rendering + réduction des artefacts + x2 avec General / Conservative. x4 peut inventer ou faire scintiller des détails absents de la source ; il doit rester un choix manuel."),
        new("Permanent export currently targets a standard encoded video. NVIDIA VSR / RTX Video HDR are display-time effects and are not falsely baked into the file.", "L’export permanent produit actuellement une vidéo encodée standard. NVIDIA VSR / RTX Video HDR sont des effets d’affichage et ne sont pas faussement intégrés au fichier."),
        new("AI Detection", "Détection IA"),
        new("Local AI Studio", "AI Studio local"),
        new("Downloads", "Téléchargements"),
        new("Manage in Downloads", "Gérer dans Téléchargements"),
        new("Selected component", "Composant sélectionné"),
        new("Install", "Installer"),
        new("Redownload", "Retélécharger"),
        new("Remove", "Supprimer"),
        new("Cancel download", "Annuler le téléchargement"),
        new("Refresh", "Actualiser"),
        new("Create", "Créer"),
        new("Model Manager", "Model Manager"),
        new("Jobs", "Jobs"),
        new("Runtime", "Runtime"),
        new("Task", "Tâche"),
        new("Backend", "Backend"),
        new("Model", "Modèle"),
        new("Prompt", "Prompt"),
        new("Source", "Source"),
        new("Mask", "Masque"),
        new("Output folder", "Dossier de sortie"),
        new("Add to generation queue", "Ajouter à la file de génération"),
        new("Quality selection", "Sélection qualité"),
        new("Recommended image", "Image recommandé"),
        new("Recommended video", "Vidéo recommandé"),
        new("Maximum quality", "Qualité maximale"),
        new("Offline / isolation", "Offline / isolation"),
        new("Model details", "Détails du modèle"),
        new("Refresh models", "Actualiser les modèles"),
        new("Open models folder", "Ouvrir le dossier des modèles"),
        new("Job Manager", "Job Manager"),
        new("Refresh queue", "Actualiser la file"),
        new("Isolated runtime", "Runtime isolé"),
        new("Prepare isolated workspace", "Préparer le workspace isolé"),
        new("Open AI Studio", "Ouvrir AI Studio"),
        new("Backends", "Backends"),
        new("Auto — best available backend", "Auto — meilleur backend disponible"),
        new("ComfyUI — workflows / nodes", "ComfyUI — workflows / nodes"),
        new("Diffusers — direct pipeline", "Diffusers — pipeline direct"),
        new("Image or video source for image-to-image, image-to-video or video-to-video.", "Source image ou vidéo pour image-to-image, image-to-video ou video-to-video."),
        new("Optional mask for inpainting/outpainting.", "Masque optionnel pour inpainting/outpainting."),
        new("Runtime not prepared.", "Runtime non préparé."),
        new("Status not checked.", "État non vérifié."),
        new("Download", "Téléchargement"),
        new("Category", "Catégorie"),
        new("Status", "État"),
        new("License", "Licence"),
        new("Size", "Taille"),
        new("License accepted locally", "Licence acceptée localement"),
        new("Distribution from your Releases", "Distribution depuis tes Releases"),
        new("Installation", "Installation"),
        new("Import files obtained from the official source after accepting the license.", "Import des fichiers obtenus depuis la source officielle après acceptation de la licence."),
        new("Official source", "Source officielle"),
        new("YES", "OUI"),
        new("NO", "NON"),
        new("Manager-owned package not published", "Package manager-owned non publié"),
        new("Package version", "Version package"),
        new("Download/reconstruction size", "Taille téléchargement/reconstruction"),
        new("video2dlssnr + FFmpeg. Used by Media Neural, video processing and several local pipelines.", "video2dlssnr + FFmpeg. Utilisé par Media Neural, traitement vidéo et plusieurs pipelines locaux."),
        new("Local x2/x3/x4 upscale engine and associated models.", "Moteur local d’upscale x2/x3/x4 et modèles associés."),
        new("Portable VLC runtime used by VSR-HDR Video. Matching source and provenance are published with manager-owned assets.", "Runtime VLC portable utilisé par VSR-HDR Vidéo. Le source correspondant et la provenance sont publiés avec les assets gérés."),
        new("Local two-model ONNX ensemble used by the AI Detection page.", "Ensemble local de deux modèles ONNX utilisé par la page Détection IA."),
        new("Local image and video generation with model selection, isolated backend and offline output.", "Génération locale image et vidéo avec choix du modèle, backend isolé et sortie hors ligne."),
        new("Choose a task to see the best compatible models.", "Choisissez une tâche pour voir les meilleurs modèles compatibles."),
        new("AI Studio local ready to configure.", "AI Studio local prêt à configurer."),
        new("The default model prioritizes quality + reliability. Maximum Quality models remain available when VRAM and storage allow it.", "Le modèle par défaut privilégie qualité + fiabilité. Les modèles Maximum Quality restent disponibles si la VRAM et le stockage le permettent."),
        new("Python, PyTorch, CUDA userspace, ComfyUI, Diffusers, models, workflows and jobs remain inside the private DLSS NR Manager runtime. No system Python is modified.", "Python, PyTorch, CUDA userspace, ComfyUI, Diffusers, modèles, workflows et jobs restent dans le runtime privé DLSS NR Manager. Aucun Python système n'est modifié."),
        new("Models available by quality, task, license and offline redistribution capability.", "Modèles disponibles selon qualité, tâche, licence et possibilité de redistribution offline."),
        new("Select a model.", "Sélectionnez un modèle."),
        new("Local persistent image/video generation queue.", "File locale persistante des générations image/vidéo."),
        new("Private workspace for Python / PyTorch / CUDA userspace / ComfyUI / Diffusers.", "Workspace privé pour Python / PyTorch / CUDA userspace / ComfyUI / Diffusers."),
        new("No AI Studio runtime prepared.", "Aucun runtime AI Studio préparé."),
        new("Primary orchestrator: workflows, nodes and local API. External GPL-3.0 process.", "Orchestrateur principal : workflows, nodes et API locale. Processus externe GPL-3.0."),
        new("Direct/fallback Apache-2.0 backend for reproducible Python pipelines.", "Backend direct/fallback Apache-2.0 pour pipelines Python reproductibles."),
        new("Manage all local components: install, remove or redownload whenever you want.", "Gérez tous les composants locaux : installer, supprimer ou retélécharger quand vous voulez."),
        new("Select a download.", "Sélectionnez un téléchargement."),
        new("No operation in progress.", "Aucune opération en cours."),
        new("Rules", "Règles"),
        new("• Over 1 GB: confirmation required before download.\n• Large models: unlimited total size in the app, transported as GitHub chunks.\n• SHA-256 verified before installation.\n• Local removal possible at any time.", "• Plus de 1 Go : confirmation obligatoire avant téléchargement.\n• Gros modèles : taille totale illimitée côté app, transport en chunks GitHub.\n• SHA-256 vérifié avant installation.\n• Suppression locale possible à tout moment."),
        new("Installed", "Installé"),
        new("Not installed", "Non installé"),
        new("Installed • verification required", "Installé • vérification requise"),
        new("Available if package is published", "Disponible si package publié"),
        new("Manual installation required", "Installation manuelle requise"),
        new("Size determined by release assets", "Taille déterminée par les assets release"),
        new("AI origin detector", "Détecteur d’origine IA"),
        new("AI Studio • Models", "AI Studio • Modèles"),
        new("Recommended • high quality / reliable", "Recommandé • haute qualité / fiable"),
        new("Consumer NVIDIA GPU • ~13 GB VRAM class", "GPU NVIDIA grand public • classe ~13 Go de VRAM"),
        new("Primary image model. Unified text generation and image editing; preferred default for local production.", "Modèle image principal. Génération de texte vers image et édition unifiées ; choix par défaut privilégié pour la production locale."),
        new("Maximum FLUX image quality", "Qualité image FLUX maximale"),
        new("Very high VRAM / 32B model / quantized local paths recommended", "VRAM très élevée / modèle 32B / variantes locales quantifiées recommandées"),
        new("Quality-first FLUX option. Gated model; manual license acceptance is required and the model itself is restricted to non-commercial/non-production use.", "Option FLUX orientée qualité. Modèle gated ; acceptation manuelle de la licence requise et usage du modèle limité au non-commercial/non-production."),
        new("Maximum image quality / editing", "Qualité image / édition maximale"),
        new("High VRAM / large model", "VRAM élevée / gros modèle"),
        new("Premium image option. Manual installation/acceptance only because the model weights use the Qwen Research License.", "Option image premium. Installation/acceptation manuelle uniquement car les poids utilisent la Qwen Research License."),
        new("Mature / highly compatible", "Mature / très compatible"),
        new("Moderate VRAM", "VRAM modérée"),
        new("Compatibility fallback with a large ecosystem of workflows and control tools.", "Solution de compatibilité avec un vaste écosystème de workflows et d’outils de contrôle."),
        new("Reliable dedicated inpainting", "Inpainting dédié fiable"),
        new("Dedicated mask-based inpainting model; preferred compatibility fallback for precise masked repairs.", "Modèle d’inpainting dédié basé sur masque ; fallback de compatibilité privilégié pour les réparations masquées précises."),
        new("Recommended local video", "Vidéo locale recommandée"),
        new("Large GPU workload • optimized local profile", "Charge GPU élevée • profil local optimisé"),
        new("Primary practical local video model for text/image-to-video.", "Modèle vidéo local principal et pratique pour text/image-to-video."),
        new("Maximum text-to-video quality", "Qualité text-to-video maximale"),
        new("Very large model / high VRAM + disk", "Très gros modèle / VRAM + disque élevés"),
        new("Quality-first text-to-video option. Keep optional because the full checkpoint is very large.", "Option text-to-video orientée qualité. À garder optionnelle car le checkpoint complet est très volumineux."),
        new("Maximum image-to-video quality", "Qualité image-to-video maximale"),
        new("Quality-first image-to-video option with stronger identity/detail preservation than the practical 5B profile.", "Option image-to-video orientée qualité avec meilleure conservation de l’identité et des détails que le profil pratique 5B."),
        new("High-fidelity video transformation", "Transformation vidéo haute fidélité"),
        new("Advanced video-to-video/character animation model; optional due to size.", "Modèle avancé de video-to-video/animation de personnage ; optionnel en raison de sa taille."),
        new("Advanced production / audio-video", "Production avancée / audio-vidéo"),
        new("Optional advanced engine. Manual license acceptance required before model weights can be installed.", "Moteur avancé optionnel. Acceptation manuelle de la licence requise avant installation des poids."),
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
        new("Performance pack without renderer replacement (Lithium + FerriteCore + Krypton + C2ME + BadOptimizations + Dynamic FPS)", "Pack de performances sans remplacement du renderer (Lithium + FerriteCore + Krypton + C2ME + BadOptimizations + Dynamic FPS)"),
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
        new("Update", "Mettre à jour"),
        new("Apply preset", "Appliquer le préréglage"),
        new("Restore backup", "Restaurer la sauvegarde"),
        new("Uninstall", "Désinstaller"),
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
        new("Run DISM + SFC", "Lancer DISM + SFC"),
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
        new("Analyze and clean safe Windows temporary files, shell caches and GPU shader caches", "Analyser et nettoyer les fichiers temporaires Windows, caches du shell et caches shaders GPU sûrs"),
        new("Targets are intentionally limited to known rebuildable cache/temp locations: current-user temp, Windows Temp, Windows thumbnail/icon cache, DirectX shader cache and NVIDIA/AMD/Intel GPU caches. Locked files and inaccessible locations are skipped. Browser data, documents, downloads, registry entries, restore points, Prefetch, WinSxS and Windows Update storage are never touched.", "Les cibles sont volontairement limitées à des emplacements de cache/temp reconstruisibles connus : temp de l’utilisateur courant, Windows Temp, cache des miniatures/icônes Windows, cache de shaders DirectX et caches GPU NVIDIA/AMD/Intel. Les fichiers verrouillés et emplacements inaccessibles sont ignorés. Les données de navigateur, documents, téléchargements, entrées de registre, points de restauration, Prefetch, WinSxS et stockage Windows Update ne sont jamais modifiés."),
        new("Windows system repair", "Réparation système Windows"),
        new("Run DISM RestoreHealth, then SFC /scannow with administrator rights", "Exécuter DISM RestoreHealth puis SFC /scannow avec les droits administrateur"),
        new("No repair has been run in this session.", "Aucune réparation n’a été lancée pendant cette session."),
        new("This repair action does not delete personal files. It requests UAC, runs DISM /Online /Cleanup-Image /RestoreHealth first, then SFC /scannow in a visible elevated PowerShell window.", "Cette réparation ne supprime aucun fichier personnel. Elle demande l’UAC, exécute d’abord DISM /Online /Cleanup-Image /RestoreHealth, puis SFC /scannow dans une fenêtre PowerShell élevée visible."),
        new("User temporary files", "Fichiers temporaires utilisateur"),
        new("Temporary files for the current Windows account", "Fichiers temporaires du compte Windows actuel"),
        new("Windows temporary files", "Fichiers temporaires Windows"),
        new("System temporary files; locked/in-use files are skipped", "Fichiers temporaires système ; les fichiers verrouillés/en cours d’utilisation sont ignorés"),
        new("DirectX shader cache", "Cache de shaders DirectX"),
        new("Windows Direct3D shader cache; games rebuild it as needed", "Cache de shaders Direct3D Windows ; les jeux le reconstruisent si nécessaire"),
        new("Windows thumbnail and icon cache", "Cache des miniatures et icônes Windows"),
        new("Explorer thumbnail/icon databases; Windows rebuilds them automatically", "Bases de miniatures/icônes de l’Explorateur ; Windows les reconstruit automatiquement"),
        new("NVIDIA DirectX shader cache", "Cache de shaders DirectX NVIDIA"),
        new("NVIDIA DXCache variants; shaders are rebuilt after cleanup", "Variantes NVIDIA DXCache ; les shaders sont reconstruits après nettoyage"),
        new("NVIDIA OpenGL/Vulkan cache", "Cache OpenGL/Vulkan NVIDIA"),
        new("NVIDIA GLCache variants; shaders are rebuilt after cleanup", "Variantes NVIDIA GLCache ; les shaders sont reconstruits après nettoyage"),
        new("NVIDIA compute cache", "Cache de calcul NVIDIA"),
        new("CUDA/NVIDIA compute kernels; applications rebuild them as needed", "Kernels de calcul CUDA/NVIDIA ; les applications les reconstruisent si nécessaire"),
        new("AMD shader caches", "Caches de shaders AMD"),
        new("AMD DirectX/OpenGL/Vulkan shader caches; drivers rebuild them as needed", "Caches de shaders DirectX/OpenGL/Vulkan AMD ; les pilotes les reconstruisent si nécessaire"),
        new("Intel shader cache", "Cache de shaders Intel"),
        new("Intel graphics shader cache; drivers rebuild it as needed", "Cache de shaders graphiques Intel ; les pilotes le reconstruisent si nécessaire"),
        new("NVIDIA legacy shader cache", "Ancien cache de shaders NVIDIA"),
        new("Known NVIDIA NV_Cache locations", "Emplacements NVIDIA NV_Cache connus"),
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
        new("Validating runtime…", "Validation du runtime…"),
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
