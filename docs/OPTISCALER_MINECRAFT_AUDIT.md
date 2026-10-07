# Audit OptiScaler / Minecraft RTX / DLSS 5

Date: 2026-10-07

## Portee

Audit en lecture seule du point 2 du cahier des charges.

Aucun fichier OptiScaler, aucune DLL et aucune configuration de jeu n'ont ete modifies pendant cet audit.

Objectif utilisateur:
- PERFORMANCE > LATENCE > STABILITE > QUALITE D'IMAGE;
- reduire au minimum la perte de FPS;
- eviter les doubles traitements;
- verifier le role reel d'OptiScaler dans Minecraft 26.2 + Caustica RTX + ScandiShader + SPBRScandi;
- proposer A / B / C avant toute modification.

## 1. Version OptiScaler reellement vendoree

Source verrouillee:
- repository: wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass
- commit: 1bd39091337cc07ba961c8e59ded57e21dc95b18
- tree: 2e71bd96f977cf432120f6650579478a463cf6dd
- version source: 0.7.7
- ce commit est aussi le head le plus recent observe du fork source lors de l'audit.

Asset manager stable v3.1.1:
- OptiScaler-NR-v0.7.7-pre0-vendored-win-x64.zip
- SHA-256: 9a8e1eb945cf9438b6f78350db1f0c8e3e4383bf3e72db7415212dbfd6234ae6

Remarque:
- le script tools/build-optiscaler.ps1 construit le snapshot vendorise et produit le nom de package "-pre0";
- le header source declare 0.7.7.0 et ne definit pas VER_PRE_RELEASE.
- c'est une incoherence de nommage du package, pas une preuve de binaire incorrect.

## 2. Conclusion principale: OptiScaler n'est PAS dans le chemin Minecraft manager-owned actuel

Verification des services Minecraft:
- Services/MinecraftIntegrationService.cs: 0 reference "OptiScaler", 0 reference dxgi.dll, 0 reference d3d12.dll.
- Services/MinecraftOneClickService.cs: 0 reference "OptiScaler", 0 reference dxgi.dll, 0 reference d3d12.dll.
- Services/MinecraftDlssPackageService.cs: 0 reference "OptiScaler".
- Services/MinecraftPreflightService.cs: 0 reference "OptiScaler".

MinecraftOneClickService force le backend graphique prefere:
- preferredGraphicsBackend = "vulkan".

Le meme service indique que Caustica RTX fournit directement:
- path tracing;
- DLSS Ray Reconstruction;
- Frame Generation / MFG;
- NVIDIA Reflex;
- DLSS Neural Rendering lorsque disponible;
- RTX Performance Mode;
- ScandiShader RTX Look natif.

MinecraftDlssPackageService ne copie pas OptiScaler dans Minecraft.
Il stage uniquement les runtimes NVIDIA selectionnes sous:
- <instance>/.dlss-nr-manager-runtime/

Caustica NgxRuntime:
- charge directement son ngxshim natif;
- utilise l'instance Vulkan Minecraft/Caustica;
- importe les feature runtimes NVIDIA manager-staged depuis .dlss-nr-manager-runtime;
- appelle ensuite NGX via NgxLibrary.

Caustica RtDlssRr:
- execute directement DLSS Ray Reconstruction sur les buffers Vulkan Caustica;
- fournit les guides path tracing (depth, motion, diffuse/specular albedo, normals, reflection guides);
- fait RR + upscale render resolution -> display resolution en une passe NGX.

Caustica RtDlssFg:
- partage le meme NgxRuntime;
- appelle directement DLSSG.

Caustica RtDlssNr:
- appelle directement l'ABI DLSS-NR exposee par ngxshim;
- n'utilise pas OptiScaler;
- le code documente NR apres le display mapping SDR et avant UI / Frame Generation.

## 3. Chaine de rendu constatee

La chaine du cahier des charges:

Minecraft RTX -> Caustica -> ScandiShader -> SPBRScandi -> OptiScaler -> DLSS

n'est pas la chaine implementee actuellement.

La chaine manager-owned constatee est conceptuellement:

Minecraft Java 26.2 / Fabric
-> backend Vulkan
-> Caustica RTX path tracer
-> SPBRScandi comme ressources LabPBR / textures d'entree
-> pipeline Caustica
-> NGX direct via ngxshim
   -> DLSS RR / SR
   -> DLSS NR optionnel
   -> DLSS FG optionnel
-> post/display Caustica, dont ScandiShader RTX Look natif
-> presentation

Le placement exact des passes Caustica reste gere par le renderer Caustica.
OptiScaler n'est pas un maillon de cette chaine.

## 4. Consequence performance

Sur le chemin Minecraft actuel:
- cout OptiScaler: aucun, puisqu'il n'est pas charge;
- pas de proxy dxgi.dll / d3d12.dll ajoute par le flux Minecraft;
- pas de conversion DX11 -> DX12;
- pas de TextureSyncMethod / CopyBackSyncMethod / SyncAfterDx12;
- pas d'Output Scaling OptiScaler;
- pas de RCAS/CAS OptiScaler;
- pas de Frame Generation OptiScaler;
- pas de ratio override OptiScaler;
- pas de DRS OptiScaler;
- pas de double upscaling OptiScaler + Caustica.

Les valeurs FPS exactes restent a mesurer sur la machine utilisateur.
Aucun chiffre FPS n'est invente dans cet audit.

## 5. Ce qui arriverait si OptiScaler etait injecte manuellement dans Minecraft

### VulkanUpscaler

Dans le snapshot actuel:
- VulkanUpscaler=auto
- le commentaire du projet indique que "auto" vaut FSR 2.2 pour Vulkan.

Donc une injection OptiScaler non configuree dans ce chemin pourrait aller a l'encontre de l'objectif DLSS natif et remplacer/intercepter un chemin deja gere directement par Caustica.

### Synchronisation DX11withDX12

TextureSyncMethod, CopyBackSyncMethod et SyncAfterDx12 concernent le chemin DX11 avec backend DX12.

Ils ne sont pas pertinents pour Minecraft/Caustica en Vulkan natif.

Les optimiser pour Minecraft ne donnerait pas de gain puisque ce chemin n'est pas utilise.

### Sharpening

Defaults actuels:
- OverrideSharpness=false;
- CAS Enabled=false;
- MotionSharpnessEnabled=false;
- ContrastEnabled=false.

C'est coherent avec l'objectif d'eviter un double sharpening.

Caustica possede deja son propre post-FX / sharpen.
Activer RCAS/CAS OptiScaler en plus introduirait une passe supplementaire et un risque de sur-accentuation.

### Output Scaling

Default:
- OutputScaling Enabled=false.

Le laisser desactive est le seul choix coherent si Caustica gere deja la resolution et DLSS.
L'activer ajouterait une seconde mise a l'echelle/downscale.

### Upscale ratios / DRS

Defaults:
- UpscaleRatioOverrideEnabled=false;
- QualityRatioOverrideEnabled=false;
- DrsMinOverrideEnabled=false;
- DrsMaxOverrideEnabled=false.

Caustica RR demande directement a NGX la resolution optimale pour le quality mode.
Forcer une seconde politique de ratio dans OptiScaler n'apporte aucun benefice dans le chemin natif et risque de desynchroniser les dimensions attendues par Caustica/NGX.

### Frame Generation

Defaults OptiScaler:
- Enabled=false;
- FGInput=nofg;
- FGOutput=nofg.

Caustica possede son propre DLSSG via NgxRuntime.
Activer OptiScaler FG en plus creerait une seconde couche FG/interposition avec risque de conflit de swapchain, HUD, pacing et latence.

### Logging / debug

Defaults principaux:
- LogToFile=false;
- LogToConsole=false;
- LogToNGX=false;
- LogToDebug=false;
- debug views off.

Ces defaults sont adaptes a la performance normale.

### DLSS Neural Rendering OptiScaler

Default OptiScaler:
- Enabled=false.

Le manager generique, hors chemin Minecraft, peut appliquer:
- Enabled=true;
- RunBeforeSR=true;
- Passes=1;
- WorkingScale=<valeur utilisateur>;
- Style=1;
- overlay + compteur FPS actives.

Le snapshot OptiScaler documente:
- Passes=2 ou 3 coute environ 2x ou 3x le temps du modele;
- WorkingScale reduit le cout approximativement avec le carre de l'echelle;
- WorkingScale > 1 fait du supersampling du modele;
- AutoCapture est active par defaut et ecrit quelques captures lors du premier passage.

Ces fonctions peuvent etre pertinentes pour les jeux generiques geres par InstallerService.
Elles ne justifient pas l'ajout d'OptiScaler dans Minecraft, puisque Caustica a deja son integration NR native.

## 6. Solutions

### Solution A - Performance maximale

Architecture:
- ne pas injecter OptiScaler dans Minecraft;
- conserver Caustica Vulkan + NGX direct;
- laisser Caustica gerer RR/SR, NR, FG et Reflex;
- SPBRScandi reste un resource pack;
- ScandiShader reste le post-FX natif Caustica;
- aucune seconde couche de sharpening/scaling/FG.

FPS relativement a Minecraft sans OptiScaler:
- structurellement, c'est le meme chemin actuellement;
- perte OptiScaler attendue: aucune, car OptiScaler n'est pas charge;
- chiffres exacts: A MESURER SUR TA CONFIGURATION.

Avantages:
- minimum absolu d'interposition;
- aucun proxy DXGI/D3D12 inutile;
- aucun double upscaling;
- aucun double FG;
- aucun double sharpening;
- guides RR Caustica conserves nativement;
- meilleure previsibilite du frame pacing et de la latence;
- surface de bugs minimale.

Inconvenients:
- pas d'overlay OptiScaler dans Minecraft;
- pas des fonctions experimentales OptiScaler propres a son fork;
- les reglages doivent etre exposes par Caustica ou DLSS NR Manager.

Risques:
- faibles;
- depend des bugs propres a Caustica/NGX, pas d'une seconde couche d'interception.

Qualite:
- conserve le chemin RR guide par le renderer, le meilleur point d'integration disponible dans ce projet.

### Solution B - Performance / qualite equilibree

Architecture possible:
- garder Caustica comme proprietaire du renderer, RR/SR, FG et Reflex;
- n'utiliser OptiScaler que comme couche experimentale tres limitee, par exemple NR-only/diagnostic;
- ne jamais lui laisser remplacer l'upscaler, FG, Output Scaling, CAS, ratios ou DRS;
- overlay/logs desactives hors diagnostic;
- AutoCapture desactive apres validation.

Etat actuel:
- cette architecture N'EST PAS implementee par DLSS NR Manager pour Minecraft;
- aucun loader OptiScaler Minecraft n'existe dans le chemin one-click;
- il faudrait concevoir explicitement une injection Vulkan compatible et prouver qu'elle n'interfere pas avec ngxshim/Caustica.

FPS:
- A MESURER SUR TA CONFIGURATION;
- cout attendu non nul si NR OptiScaler execute une passe modele supplementaire.

Avantages:
- experimentation OptiScaler possible sans lui confier toute la chaine;
- possibilite de comparer son NR a l'integration Caustica.

Inconvenients:
- duplication fonctionnelle avec Caustica NR;
- ajout d'un interposer;
- complexite de support;
- gain non demontre.

Risques:
- ordre de passes incorrect;
- conflits NGX;
- captures/synchronisations/copies supplementaires;
- probleme d'injection dans un processus Java/Vulkan.

Qualite:
- potentiellement interessante uniquement si un benchmark visuel montre un gain reel.

### Solution C - Qualite maximale / experimentation OptiScaler complete

Architecture possible:
- OptiScaler devient actif pour upscaling, NR, eventuellement FG, sharpening ou Output Scaling.

FPS:
- A MESURER SUR TA CONFIGURATION;
- c'est la solution avec le plus grand risque de cout GPU/CPU supplementaire.

Avantages:
- acces maximal aux fonctions experimentales OptiScaler;
- possibilite de supersampling modele, sharpening et autres filtres.

Inconvenients:
- double emploi avec Caustica;
- plusieurs passes supplementaires possibles;
- Output Scaling et supersampling peuvent couter fortement;
- Passes NR > 1 multiplie directement le temps modele;
- perte de la simplicite du pipeline Vulkan direct.

Risques:
- tres eleves dans ce projet;
- remplacement involontaire du DLSS natif par FSR2.2 avec VulkanUpscaler=auto;
- conflit FG/swapchain;
- double sharpen;
- double scaling;
- perte de coherence des guides RR;
- bugs HUD, pacing et stabilite.

Qualite:
- peut etre superieure sur certains criteres visuels, mais rien dans le depot ne prouve un meilleur resultat que le chemin Caustica natif.

## 7. Recommandation

Solution A.

Pour Minecraft 26.2 + Caustica RTX, OptiScaler est actuellement une dependance utile au produit general DLSS NR Manager, mais pas au pipeline Minecraft.

L'ajouter au chemin Minecraft pour "optimiser" les FPS ferait l'inverse de l'objectif prioritaire tant qu'un benefice precis n'est pas mesure.

La voie la plus performante et la plus propre est de laisser:
- Caustica gerer Vulkan;
- Caustica appeler NGX directement;
- RR utiliser les guides natifs du path tracer;
- Caustica gerer FG / Reflex / NR;
- ScandiShader rester natif Caustica;
- SPBRScandi rester uniquement un resource pack.

## 8. Benchmark a effectuer apres validation utilisateur

Comparer sur la meme scene, meme camera, meme resolution et meme preset:
1. Caustica natif, OptiScaler absent.
2. Eventuelle solution B prototype si l'utilisateur l'autorise.
3. Eventuelle solution C seulement si B apporte deja une preuve d'interet.

Mesures:
- FPS moyen;
- 1% low;
- 0.1% low;
- frametime moyen;
- variance / spikes frametime;
- GPU usage;
- GPU power;
- VRAM;
- CPU main-thread;
- latence si mesure disponible;
- artefacts visuels.

Ne pas utiliser le compteur de frames generees comme substitut au FPS rendu de base.

## 9. Decision requise avant implementation

Aucune modification OptiScaler / DLL / config ne doit etre faite avant validation explicite de l'utilisateur.

Choix propose:
- A: conserver OptiScaler hors de Minecraft et durcir l'UI/diagnostic pour rendre ce fait explicite;
- B: prototyper une integration NR-only experimentale;
- C: prototyper une chaine OptiScaler active complete.

Recommandation: A.
