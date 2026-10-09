# Validation Windows réelle : arrêt des processus auxiliaires

## Règle de cycle de vie des nouveaux modules

Tous les processus auxiliaires **créés et contrôlés par DLSS NR Manager** doivent être démarrés avec `ExternalProcessTracker.Start` (`UseShellExecute=false`). Ils sont affectés à un Job Object Windows avec `KILL_ON_JOB_CLOSE`. Si ce Job Object n'est pas disponible, si l'affectation échoue ou si la fermeture a commencé, le lancement est refusé (fail-closed) et aucun nouveau processus auxiliaire non géré ne doit être conservé. Cela inclut les chemins futurs ComfyUI/Python, FFmpeg, Real-ESRGAN, VLC, ONNX externes et les outils de diagnostic, dès qu'ils démarrent effectivement un exécutable.

Les tests unitaires `ProcessLaunchPolicyTests` empêchent l'introduction de nouveaux `Process.Start` directs dans les services sans une revue explicite. Les tests Windows `NormalManagerShutdownTests` et `ForcedManagerTerminationTests` couvrent respectivement fermeture normale + refus des lancements tardifs et arrêt brutal d'un hôte indépendant. Ils ne remplacent pas l'acceptation sur PC réel avec traitements actifs.

**Exceptions intentionnelles :** les jeux lancés par l'utilisateur, les dossiers ouverts via Explorer, les installateurs interactifs ReShade, les sites ouverts par PC Update Center, les réparations élevées DISM/SFC, le processus de mise à jour autonome de l'application et l'effacement local demandé après fermeture ne sont pas des **auxiliaires gérés**. Ils peuvent continuer après la sortie du manager. Ne pas leur appliquer une terminaison aveugle par nom : cela peut interrompre des opérations système ou du travail utilisateur. Un processus autonome de mise à jour est nécessaire pour remplacer l'exécutable arrêté.

Ce test **n'est pas une preuve obtenue sur la machine utilisateur** tant qu'il n'est pas lancé et que son rapport n'est pas examiné. Il complète les tests xUnit GitHub Actions, sans les remplacer.

Depuis PowerShell sur le PC Windows qui exécute l'application, avec le binaire construit par la CI :

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\verify-v4-windows-process-cleanup.ps1 -ManagerExecutablePath "C:\Chemin\DlssNrManager.exe" -TimeoutSeconds 900 -GraceSeconds 10
```

Pour tester un **arrêt brutal volontaire de cette instance uniquement**, après avoir fermé les autres logiciels non sauvegardés et choisi un scénario reproductible :

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\verify-v4-windows-process-cleanup.ps1 -ManagerExecutablePath "C:\Chemin\DlssNrManager.exe" -ForceTerminateAfterSeconds 45 -TimeoutSeconds 900
```

**Attention :** cette seconde commande arrête volontairement l'application testée ; tout travail non enregistré à l'intérieur peut être perdu. Elle ne ferme pas l'arbre complet des processus et ne supprime aucun fichier utilisateur.

Le script :

1. photographie les PID des utilitaires courants (`video2dlssnr`, `ffmpeg`, `ffprobe`, `realesrgan-ncnn-vulkan`) présents **avant** l'exécution ;
2. lance une instance du manager puis observe sa sortie normale ou forcée ;
3. attend 10 secondes (modifiable), puis signale tout nouveau PID encore actif sous ces noms ;
4. écrit un JSON temporaire et renvoie le code `0` si aucun nouveau processus n'est observé, `2` en cas de résidu candidat, `3` si l'application ne quitte pas dans le délai.

Cette observation ne prouve **pas** qu'un processus résiduel appartient au manager : une autre application peut lancer un processus portant le même nom. Inversement, elle n'analyse pas les processus nommés autrement, le GPU, les pilotes, la VRAM, les modèles Python/ComfyUI ni la cohérence des sauvegardes sur disque. Il faut combiner ce test avec un scénario actif Real-ESRGAN, des mesures VRAM/RAM, des scénarios de téléchargement interrompu et une vérification manuelle des journaux et sauvegardes.

Conserver les résultats JSON et l'identifiant de Build GitHub dans l'issue [#95](https://github.com/grg914/dlss-nr-manager/issues/95) avant de conclure la préparation d'une release 4.0.


## Protocole de recette complémentaire

Sur la machine Windows/NVIDIA cible, exécuter une tâche longue de chaque composant effectivement installé (Real-ESRGAN, Media Neural FFmpeg, VLC, AI Origin et, une fois disponible, AI Studio Python/ComfyUI), puis fermer DLSS NR Manager et contrôler le rapport JSON, les PID et la disparition des sous-processus qu'il a lancés. Recommencer par une terminaison brutale contrôlée du manager. Les processus portant un même nom mais appartenant à d'autres applications ne doivent jamais être interrompus. Vérifier la stabilité du résultat sur plusieurs lancements, y compris pendant une annulation/téléchargement et après une mise à jour. La mémoire de processus est libérée par l'OS à la terminaison ; un cache VRAM pilote peut subsister temporairement sans qu'un processus applicatif reste actif.
