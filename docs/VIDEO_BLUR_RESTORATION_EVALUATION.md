# Évaluation — restauration de vidéos floues

Cette évaluation complète le module **VSR-HDR Vidéo**. L'objectif n'est pas de maximiser artificiellement la résolution, mais d'améliorer une vidéo floue, compressée ou peu détaillée tout en gardant une image stable, lisible et cohérente d'une frame à l'autre.

## Pipeline actuel disponible

Le mode permanent dispose déjà de trois étapes indépendantes :

1. **Neural Rendering** : nettoyage/reconstruction à résolution native ou pendant un changement d'échelle.
2. **Réduction des artefacts** : prétraitement conservateur, utile sur compression, blocking et bruit.
3. **Real-ESRGAN** : upscale x2 ou x4.

Le mode VLC temps réel utilise NVIDIA RTX VSR pour l'upscale/cleanup d'affichage et RTX Video HDR pour SDR -> HDR. Ce chemin améliore le rendu affiché mais ne recrée pas de manière fiable les détails réellement absents de la source.

## Jusqu'où pousser l'upscale

### Flou léger / compression légère

**Cible recommandée : x2.**

- Neural Rendering : ON.
- Réduction des artefacts : ON si la source est compressée.
- Real-ESRGAN : modèle **General / Conservative** ou **General / Photo**.
- x4 peut être testé seulement si les contours, visages, textes et textures restent stables.

C'est le cas où le gain visuel a le meilleur rapport amélioration / risque d'invention de détails.

### Flou moyen

**Cible recommandée : restauration d'abord, x2 ensuite.**

Un modèle image frame-par-frame peut rendre chaque frame plus nette tout en produisant du scintillement ou des détails différents d'une frame à l'autre. Pour ce niveau de dégradation, un moteur vidéo temporel est préférable.

Candidats à évaluer :

- **RealBasicVSR** : conçu pour les dégradations vidéo réelles et utilise un nettoyage en amont avant propagation temporelle.
- **BasicVSR++** : propagation/alignement temporels, avec configurations et checkpoints de deblur et denoise.

Les deux dépôts sont sous **Apache-2.0**, donc leur code peut être étudié, forké et intégré en respectant leurs notices et les licences de leurs dépendances/modèles.

### Flou sévère / mise au point ratée / motion blur important

**Ne pas considérer x4 comme une récupération réelle de détails.**

Quand l'information optique n'existe plus dans la source, un modèle génératif peut produire un détail plausible mais faux. Les risques principaux sont :

- visages qui changent légèrement entre les frames ;
- texte faux ou instable ;
- textures qui scintillent ;
- contours doubles ;
- oversharpening / halos ;
- détails inventés sur cheveux, peau, végétation ou objets fins.

Pour ce niveau, la priorité doit être :

1. deblur temporel ;
2. débruitage / suppression des artefacts ;
3. stabilisation temporelle ;
4. upscale x2 ;
5. x4 seulement après validation visuelle.

## Politique qualité proposée

Le module ne doit pas appliquer automatiquement le niveau le plus agressif. La stratégie recommandée est :

| Niveau | Traitement recommandé | x4 |
| --- | --- | --- |
| Source propre / légèrement douce | NR + upscale x2 | optionnel |
| Compression / flou léger | nettoyage + NR + x2 | test manuel |
| Flou moyen | moteur vidéo temporel + x2 | déconseillé par défaut |
| Flou sévère | deblur temporel + restauration conservatrice | désactivé par défaut |

## Garde-fous avant production

Avant d'ajouter un moteur de restauration floue à la distribution, le benchmark doit inclure :

- comparaison frame par frame avant/après ;
- cohérence temporelle sur mouvement, visages, texte et détails fins ;
- recherche de scintillement/flicker ;
- recherche de halos et contours doubles ;
- comparaison x2 vs x4 ;
- échantillons faible lumière, compression forte, motion blur et defocus blur ;
- mesure du coût GPU/VRAM et du temps par frame ;
- prévisualisation courte avant encodage complet.

Les métriques classiques PSNR/SSIM ne suffisent pas seules pour une vidéo réelle sans référence. L'acceptation finale doit inclure un contrôle visuel et temporel.

## Décision d'architecture

Pour la version actuelle :

- **x2 reste le choix qualité recommandé pour une vidéo floue** ;
- **x4 reste disponible mais n'est pas présenté comme une récupération fidèle de détails** ;
- le modèle **General / Conservative** est le choix de départ conseillé pour éviter l'oversharpening ;
- la prochaine vraie amélioration pour les vidéos floues est un moteur **temporel** de type RealBasicVSR / BasicVSR++, pas davantage de sharpen frame-par-frame ;
- aucune dépendance supplémentaire ne passe en production avant validation des modèles, poids, licences transitives, VRAM et stabilité temporelle.
