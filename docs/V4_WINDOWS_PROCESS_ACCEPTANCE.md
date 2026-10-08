# Validation Windows réelle : arrêt des processus auxiliaires

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
