# Audit exhaustif des branches GitHub — 8 octobre 2026

> **Archive historique, non inventaire actuel (R056).** Les chiffres ci-dessous correspondent à l'état observé avant nettoyage. La PR #129 a été fusionnée le 8 octobre 2026 (commit `d5221deb14e774723e1cde408d182146597d1770`). Son nettoyage a supprimé **84 branches GitHub**, confirmé indépendamment par la liste des références. Il reste **45 branches**, dont `main`, les PR #125/#128 et les anciennes releases. La branche `refactor/phase4-zero-upstream-production` a été préservée car référencée par `nuget-seed.yml`. Voir l'issue #124 (R054 et R055) et `docs/PROJECT_PROGRESS_R056.md` pour l'état post-nettoyage. Ne pas interpréter les tableaux historiques de ce document comme la liste actuelle des branches.

**Dépôt :** `grg914/dlss-nr-manager` — **référence :** `main` commit `3e339572fba05f15fa89ba40161ad59b84e083dc` après fusion PR #122.

Cet inventaire a été généré depuis les réponses GitHub API `/branches` et `/pulls?state=all` paginées. Il examine **toutes les références de branche** visibles lors de la lecture, et non seulement les PR ouvertes. **Présence de branche ne signifie pas fonctionnalité non fusionnée.** Une PR fusionnée (souvent par squash) reste détectée par `merged_at`, même si sa tête originale n'est plus un ancêtre direct de `main`.

## Comptage

| Catégorie | Nombre |
|---|---:|
| Total branches | 125 |
| Référence protégée `main` | 1 |
| Branches avec au moins une PR fusionnée | 88 |
| Branches avec PR fermée sans fusion directe | 24 |
| Branches avec PR ouverte | 0 |
| Branches sans PR retrouvée | 12 |

## Décisions et risques

**Fusionné dans le code source :** la PR #122 a été fusionnée après Build et CodeQL verts, commit de fusion `3e339572fba05f15fa89ba40161ad59b84e083dc`. Elle contient les contributions précédemment ouvertes dans les PR #120 et #121, fermées comme remplacées. Checkpoints R039–R045 présents sur `main`. Le Build/CodeQL sur le `main` post-fusion doit être confirmé indépendamment.

**Non inclus dans le binaire public :** dernière release GitHub vérifiée `v3.2.0`. Le code `main` est en avance sur cette release; ne pas présenter la prochaine version comme publiée.

**Branches divergentes sans PR à ne PAS fusionner ni supprimer automatiquement :**
- `docs/live-ai-project-progress` : environ 199 commits en avance et 27 en retard par rapport au précédent `main` audité, avec divergence de configuration/gouvernance et journal; possible ancien historique parallèle.
- `refactor/video2dlssnr-dynamic-ngx` : environ 108 commits en avance et 60 en retard, plus de 300 chemins divergents; risque majeur d'écraser des sources vendored ou le runtime.
- `automation/upstream-sync-37588055056` et `automation/upstream-sync-37589917835` : chacune 1 commit en avance mais 46–47 en retard, différences importantes dans les sources FFmpeg vendored; requièrent validation des locks/licences plutôt qu'une fusion directe.
- `fix/phase3-bootstrap`, `fix/seed-consistency-and-continuity` : divergences des workflows de CI et des scripts de publication. Audit ciblé nécessaire pour s'assurer qu'une correction de fiabilité n'a pas été perdue.

**Branches sans PR déjà entièrement derrière `main`** (comparaison GitHub au 8 octobre) : `ci/protected-release-request`, `fix/reproducible-model-and-release`, `hardening/minecraft-native-ngx-policy`, `hardening-minecraft-native-ngx-policy`, `refactor/monorepo-caustica`. Ces références sont candidates au nettoyage après contrôle de l'absence de consommateur externe.

**PR fermées non fusionnées :** peuvent être supersédées (ex. #113 remplacée par #114, #115/#116 par #117, #118 par #119, #120/#121 par #122), mais d'autres PR anciennes nécessitent une comparaison sémantique des fichiers : ne pas les intégrer de force, ne pas affirmer que tout leur contenu exact est dans `main`.

**Règle de nettoyage proposée :** supprimer seulement les branches fusionnées et reconnues obsolètes, après conservation des références, vérification qu'aucun workflow/outil externe ne les utilise et qu'aucune modification unique n'est attendue. Ne jamais supprimer `main`, les branches de PR ouvertes, les branches de travail divergent non auditées, ni les branches de releases pertinentes. La connexion GitHub de cette session **ne fournit pas de commande d'effacement de branches** : aucun effacement n'est revendiqué. L'archivage conservateur évite de perdre du travail.

**Blocages release 4.0 :** issues #79 (licence OpenMP), #82 (CodeQL requis dans ruleset), #95 (tests Windows GPU réels), #111 (updates autres composants), #123 (publication d'asset seed non atomique); README/CHANGELOG et tests de release nécessaires. Les corrections doivent passer par des PR vers `main`, pas par fusion automatique de toutes les branches.

## Inventaire complet

| Branche | HEAD (10 caractères) | Catégorie GitHub | PR |
|---|---|---|---|
| `audit/caustica-scandi-assets` | `0756d7603a` | PR fermée sans fusion directe | [#60](https://github.com/grg914/dlss-nr-manager/pull/60) |
| `audit/caustica-scandi-assets-v2` | `f8d5ded11d` | PR fusionnée | [#61](https://github.com/grg914/dlss-nr-manager/pull/61) |
| `audit/optiscaler-minecraft-path` | `2aec4ca97b` | PR fusionnée | [#63](https://github.com/grg914/dlss-nr-manager/pull/63) |
| `automation/upstream-sync-37588055056` | `2e8bb92c5a` | Aucune PR retrouvée — audit requis | - |
| `automation/upstream-sync-37589917835` | `a72f03cb80` | Aucune PR retrouvée — audit requis | - |
| `automation/upstream-sync-37591538524` | `ddb4e72a6d` | PR fusionnée | [#49](https://github.com/grg914/dlss-nr-manager/pull/49) |
| `chore/bootstrap-github-release` | `3a20eab28c` | PR fusionnée | [#10](https://github.com/grg914/dlss-nr-manager/pull/10) |
| `chore/nvidia-dlss-310.9.1` | `4b95f00695` | PR fusionnée | [#74](https://github.com/grg914/dlss-nr-manager/pull/74) |
| `chore/repository-governance-20261008` | `0bf2b2ac57` | PR fermée sans fusion directe | [#78](https://github.com/grg914/dlss-nr-manager/pull/78) |
| `chore/repository-governance-hardening` | `42a37f15fc` | PR fermée sans fusion directe | [#80](https://github.com/grg914/dlss-nr-manager/pull/80) |
| `ci/document-release-only-refresh` | `240cef3eb2` | PR fusionnée | [#53](https://github.com/grg914/dlss-nr-manager/pull/53) |
| `ci/protected-release-request` | `a8fcf36c1c` | Aucune PR retrouvée — audit requis | - |
| `cleanup/stale-pr-consolidation` | `7a6ef3b64e` | PR fusionnée | [#86](https://github.com/grg914/dlss-nr-manager/pull/86) |
| `docs/ai-project-progress` | `f6a159067d` | PR fusionnée | [#39](https://github.com/grg914/dlss-nr-manager/pull/39) |
| `docs/close-caustica-audit` | `bca0e5b874` | PR fusionnée | [#62](https://github.com/grg914/dlss-nr-manager/pull/62) |
| `docs/close-v3.2.0-checkpoint` | `531b389ad5` | PR fusionnée | [#73](https://github.com/grg914/dlss-nr-manager/pull/73) |
| `docs/current-project-checkpoint-20261007` | `5ce91ff9d5` | PR fusionnée | [#54](https://github.com/grg914/dlss-nr-manager/pull/54) |
| `docs/final-v3-1-1-checkpoint` | `109088937f` | PR fusionnée | [#56](https://github.com/grg914/dlss-nr-manager/pull/56) |
| `docs/fix-readme-version-and-v4-checkpoint` | `0c0c464efc` | PR fermée sans fusion directe | [#107](https://github.com/grg914/dlss-nr-manager/pull/107) |
| `docs/live-ai-project-progress` | `6d2389af4e` | Aucune PR retrouvée — audit requis | - |
| `docs/progress-checkpoint-2026-10-07` | `2474208f2c` | PR fusionnée | [#58](https://github.com/grg914/dlss-nr-manager/pull/58) |
| `docs/recovery-checkpoint-20261008-pr102-103` | `5bf75221c0` | PR fermée sans fusion directe | [#104](https://github.com/grg914/dlss-nr-manager/pull/104) |
| `docs/v4-branch-audit-20261008` | `3e339572fb` | Aucune PR retrouvée — audit requis | - |
| `docs/v4-prompt-requirements-checkpoint` | `87bcdae73e` | PR fusionnée | [#106](https://github.com/grg914/dlss-nr-manager/pull/106) |
| `docs/v4-prompt-roadmap-20261008` | `099daecc4a` | PR fusionnée | [#92](https://github.com/grg914/dlss-nr-manager/pull/92) |
| `docs/v4-r019-reconciled` | `cd759a7cf5` | PR fusionnée | [#108](https://github.com/grg914/dlss-nr-manager/pull/108) |
| `docs/v4-release-changelog-baseline` | `f3b989a3bb` | PR fermée sans fusion directe | [#109](https://github.com/grg914/dlss-nr-manager/pull/109) |
| `feat/ai-origin-detection` | `9ec5cefd0f` | PR fusionnée | [#9](https://github.com/grg914/dlss-nr-manager/pull/9) |
| `feat/dlssnrmanager-menu-v1.5.0` | `e8e375a31e` | PR fusionnée | [#17](https://github.com/grg914/dlss-nr-manager/pull/17) |
| `feat/local-ai-studio` | `612c8e0974` | PR fusionnée | [#77](https://github.com/grg914/dlss-nr-manager/pull/77) |
| `feat/minecraft-scandi-packs` | `07976d0795` | PR fusionnée | [#8](https://github.com/grg914/dlss-nr-manager/pull/8) |
| `feat/nvidia-nr-check-v1.3` | `b6615289cc` | PR fusionnée | [#13](https://github.com/grg914/dlss-nr-manager/pull/13) |
| `feat/pc-clean-maintenance` | `6f9e656b02` | PR fusionnée | [#89](https://github.com/grg914/dlss-nr-manager/pull/89) |
| `feat/rtx-wide-compat-safety-v1.4` | `0303f769cb` | PR fusionnée | [#14](https://github.com/grg914/dlss-nr-manager/pull/14) |
| `feat/vlc-video-enhancement` | `6ab356fdf6` | PR fusionnée | [#75](https://github.com/grg914/dlss-nr-manager/pull/75) |
| `feature/ai-upscale` | `d37cf85102` | PR fusionnée | [#4](https://github.com/grg914/dlss-nr-manager/pull/4) |
| `feature/auto-runtime-winget-nvidia` | `d231197a61` | PR fusionnée | [#7](https://github.com/grg914/dlss-nr-manager/pull/7) |
| `feature/bundled-caustica-update-all` | `d73dcc1b96` | PR fusionnée | [#25](https://github.com/grg914/dlss-nr-manager/pull/25) |
| `feature/consolidate-local-updates` | `cd00f977d9` | PR fusionnée | [#1](https://github.com/grg914/dlss-nr-manager/pull/1) |
| `feature/manual-manager-update-check` | `c3261e5f88` | PR fusionnée | [#26](https://github.com/grg914/dlss-nr-manager/pull/26) |
| `feature/minecraft-rtx` | `5ffaa7b9a8` | PR fusionnée | [#5](https://github.com/grg914/dlss-nr-manager/pull/5) |
| `feature/pc-update-center` | `1ceb3c8039` | PR fusionnée | [#3](https://github.com/grg914/dlss-nr-manager/pull/3) |
| `feature/v2.1-swapper-hardening` | `3aa78bb359` | PR fusionnée | [#27](https://github.com/grg914/dlss-nr-manager/pull/27) |
| `feature/v4-download-center-media-update-detection` | `3fc1126d53` | PR fermée sans fusion directe | [#113](https://github.com/grg914/dlss-nr-manager/pull/113) |
| `feature/v4-download-center-media-update-reconciled` | `5eb6e958d2` | PR fusionnée | [#114](https://github.com/grg914/dlss-nr-manager/pull/114) |
| `feature/v4-hardware-profiles-20261008` | `2d90d0edd2` | PR fusionnée | [#93](https://github.com/grg914/dlss-nr-manager/pull/93) |
| `feature/v4-localized-option-tooltips` | `27b6082945` | PR fusionnée | [#94](https://github.com/grg914/dlss-nr-manager/pull/94) |
| `fix/ai-origin-ensemble-v1.4.1` | `ebb9293ad3` | PR fusionnée | [#15](https://github.com/grg914/dlss-nr-manager/pull/15) |
| `fix/artwork-icon-pc-updates` | `e790b7c7cf` | PR fusionnée | [#6](https://github.com/grg914/dlss-nr-manager/pull/6) |
| `fix/artwork-reload-battlefield6` | `704da63b6c` | PR fusionnée | [#2](https://github.com/grg914/dlss-nr-manager/pull/2) |
| `fix/caustica-texture-pack-hash` | `549713a794` | PR fusionnée | [#12](https://github.com/grg914/dlss-nr-manager/pull/12) |
| `fix/deterministic-fabric-profile` | `ecbd4e6cb8` | PR fusionnée | [#38](https://github.com/grg914/dlss-nr-manager/pull/38) |
| `fix/dynamic-fps-26-2-upstream` | `d851ce155b` | PR fusionnée | [#36](https://github.com/grg914/dlss-nr-manager/pull/36) |
| `fix/dynamic-fps-compatible-release-resolution` | `41a8e2b3ca` | PR fusionnée | [#37](https://github.com/grg914/dlss-nr-manager/pull/37) |
| `fix/fabric-installer-metadata-powershell` | `2680b94776` | PR fusionnée | [#44](https://github.com/grg914/dlss-nr-manager/pull/44) |
| `fix/fabric-loader-detail-metadata` | `185d60e644` | PR fusionnée | [#41](https://github.com/grg914/dlss-nr-manager/pull/41) |
| `fix/fabric-loader-response-enumeration` | `b67f672083` | PR fusionnée | [#43](https://github.com/grg914/dlss-nr-manager/pull/43) |
| `fix/fabric-maven-meta-release-status` | `15bdbc8280` | PR fusionnée | [#42](https://github.com/grg914/dlss-nr-manager/pull/42) |
| `fix/finalize-continuity-checkpoint` | `48dfc73646` | PR fermée sans fusion directe | [#55](https://github.com/grg914/dlss-nr-manager/pull/55) |
| `fix/finalize-v311-continuity` | `4b5e4eeeb1` | PR fermée sans fusion directe | [#57](https://github.com/grg914/dlss-nr-manager/pull/57) |
| `fix/full-audit-reliability-v1.4.2` | `f54a2263f0` | PR fusionnée | [#16](https://github.com/grg914/dlss-nr-manager/pull/16) |
| `fix/full-audit-v1.5.1-final` | `eabd5678aa` | PR fusionnée | [#19](https://github.com/grg914/dlss-nr-manager/pull/19) |
| `fix/full-audit-v1.5.1` | `b1c268b822` | PR fermée sans fusion directe | [#18](https://github.com/grg914/dlss-nr-manager/pull/18) |
| `fix/integrate-pr18-v1.5.1` | `7e3a104864` | PR fusionnée | [#20](https://github.com/grg914/dlss-nr-manager/pull/20) |
| `fix/minecraft-fabric-profile-determinism` | `7ff408db41` | PR fermée sans fusion directe | [#40](https://github.com/grg914/dlss-nr-manager/pull/40) |
| `fix/minecraft-nvidia-runtime-ux` | `2672e83428` | PR fermée sans fusion directe | [#22](https://github.com/grg914/dlss-nr-manager/pull/22) |
| `fix/modrinth-response-enumeration` | `c383fccf73` | PR fusionnée | [#45](https://github.com/grg914/dlss-nr-manager/pull/45) |
| `fix/notify-only-upstream-issues` | `5aa69a271d` | PR fusionnée | [#88](https://github.com/grg914/dlss-nr-manager/pull/88) |
| `fix/phase3-bootstrap` | `a41432be7d` | Aucune PR retrouvée — audit requis | - |
| `fix/phase4-post-merge-sync` | `d9dd76cfb6` | PR fusionnée | [#34](https://github.com/grg914/dlss-nr-manager/pull/34) |
| `fix/release-main-bootstrap-trigger` | `2d9a904e52` | PR fusionnée | [#11](https://github.com/grg914/dlss-nr-manager/pull/11) |
| `fix/replace-scanditexture-with-spbr-safe` | `9bd2644226` | PR fusionnée | [#23](https://github.com/grg914/dlss-nr-manager/pull/23) |
| `fix/reproducible-model-and-release` | `905c10af23` | Aucune PR retrouvée — audit requis | - |
| `fix/runtime-refresh-concurrency-v2` | `ba3123702c` | PR fusionnée | [#71](https://github.com/grg914/dlss-nr-manager/pull/71) |
| `fix/runtime-refresh-governance-scope` | `c4ddd472a6` | PR fusionnée | [#91](https://github.com/grg914/dlss-nr-manager/pull/91) |
| `fix/runtime-refresh-release-only` | `c3851553a3` | PR fusionnée | [#70](https://github.com/grg914/dlss-nr-manager/pull/70) |
| `fix/seed-consistency-and-continuity` | `e7a6be809a` | Aucune PR retrouvée — audit requis | - |
| `fix/upstream-pr-fallback-exitcode` | `47c66d73e9` | PR fusionnée | [#47](https://github.com/grg914/dlss-nr-manager/pull/47) |
| `fix/upstream-pr-permission-fallback` | `b74756166b` | PR fusionnée | [#46](https://github.com/grg914/dlss-nr-manager/pull/46) |
| `fix/upstream-protected-main-fallback` | `de2d315f94` | PR fusionnée | [#50](https://github.com/grg914/dlss-nr-manager/pull/50) |
| `fix/v3.2.0-i18n-complete` | `fd85fc6163` | PR fermée sans fusion directe | [#69](https://github.com/grg914/dlss-nr-manager/pull/69) |
| `fix/v3.2.0-release-streamline-exit` | `761c867faf` | PR fusionnée | [#72](https://github.com/grg914/dlss-nr-manager/pull/72) |
| `fix/v4-ai-studio-manual-import-safety` | `29cfac94b0` | PR fusionnée | [#102](https://github.com/grg914/dlss-nr-manager/pull/102) |
| `fix/v4-ai-studio-staging-cleanup` | `45f47855cc` | PR fermée sans fusion directe | [#103](https://github.com/grg914/dlss-nr-manager/pull/103) |
| `fix/v4-ai-studio-staging-cleanup-rebased` | `675f5e8455` | PR fusionnée | [#105](https://github.com/grg914/dlss-nr-manager/pull/105) |
| `fix/v4-download-center-transactional-redownload` | `b93a2fda50` | PR fusionnée | [#96](https://github.com/grg914/dlss-nr-manager/pull/96) |
| `fix/v4-media-update-recovery` | `84aa0ad830` | PR fusionnée | [#100](https://github.com/grg914/dlss-nr-manager/pull/100) |
| `fix/v4-recover-interrupted-component-reinstalls` | `5a8fbb1b8f` | PR fusionnée | [#98](https://github.com/grg914/dlss-nr-manager/pull/98) |
| `fix/v4-vlc-owned-process-lifecycle` | `d1f7d2e3f3` | PR fusionnée | [#97](https://github.com/grg914/dlss-nr-manager/pull/97) |
| `grg914-patch-1` | `3445bc8357` | PR fusionnée | [#59](https://github.com/grg914/dlss-nr-manager/pull/59) |
| `hardening/deterministic-nuget-seed` | `56e9d5784f` | PR fusionnée | [#83](https://github.com/grg914/dlss-nr-manager/pull/83) |
| `hardening/minecraft-native-ngx-policy` | `8b4ae3f2bc` | Aucune PR retrouvée — audit requis | - |
| `hardening/release-source-pinning` | `0bf7d66312` | PR fusionnée | [#85](https://github.com/grg914/dlss-nr-manager/pull/85) |
| `hardening/repository-governance` | `3d6365a8fa` | PR fusionnée | [#81](https://github.com/grg914/dlss-nr-manager/pull/81) |
| `hardening/runtime-refresh-scoping` | `e39886fe11` | PR fusionnée | [#84](https://github.com/grg914/dlss-nr-manager/pull/84) |
| `hardening-minecraft-native-ngx-policy` | `8b4ae3f2bc` | Aucune PR retrouvée — audit requis | - |
| `hardening-minecraft-native-ngx-policy-final` | `f549e9c12c` | PR fermée sans fusion directe | [#64](https://github.com/grg914/dlss-nr-manager/pull/64) |
| `hardening-minecraft-native-ngx-policy-v2` | `175f80c561` | PR fermée sans fusion directe | [#65](https://github.com/grg914/dlss-nr-manager/pull/65) |
| `hardening-minecraft-native-ngx-policy-v3` | `f0be1a446d` | PR fusionnée | [#66](https://github.com/grg914/dlss-nr-manager/pull/66) |
| `main` | `3e339572fb` | Branche protégée / intégration | - |
| `phase4-upstream-fix` | `43c21edadf` | PR fermée sans fusion directe | [#35](https://github.com/grg914/dlss-nr-manager/pull/35) |
| `refactor/ffmpeg-self-contained` | `c197f1e889` | PR fusionnée | [#30](https://github.com/grg914/dlss-nr-manager/pull/30) |
| `refactor/games-nvidia-runtime-manager` | `a22f77bf83` | PR fusionnée | [#67](https://github.com/grg914/dlss-nr-manager/pull/67) |
| `refactor/media-runtime-cutover` | `f2a087e250` | PR fermée sans fusion directe | [#31](https://github.com/grg914/dlss-nr-manager/pull/31) |
| `refactor/monorepo-caustica` | `7006960834` | Aucune PR retrouvée — audit requis | - |
| `refactor/phase3-upstream-sync` | `b61218ac12` | PR fusionnée | [#32](https://github.com/grg914/dlss-nr-manager/pull/32) |
| `refactor/phase4-zero-upstream-production` | `acc6bacd6b` | PR fusionnée | [#33](https://github.com/grg914/dlss-nr-manager/pull/33) |
| `refactor/self-contained-monorepo` | `befb8232e7` | PR fusionnée | [#29](https://github.com/grg914/dlss-nr-manager/pull/29) |
| `refactor/video2dlssnr-dynamic-ngx` | `c197f1e889` | Aucune PR retrouvée — audit requis | - |
| `release/v2.0.0-audit` | `c8bfde27f1` | PR fusionnée | [#21](https://github.com/grg914/dlss-nr-manager/pull/21) |
| `release/v2.0.1-spbrscandi` | `a0bfc6e91f` | PR fusionnée | [#24](https://github.com/grg914/dlss-nr-manager/pull/24) |
| `release/v3.0.0-full-audit` | `dfa6cac0f8` | PR fusionnée | [#28](https://github.com/grg914/dlss-nr-manager/pull/28) |
| `release/v3.2.0` | `0d8246acef` | PR fusionnée | [#68](https://github.com/grg914/dlss-nr-manager/pull/68) |
| `research/v4-realesrgan-openmp-free-build` | `c59d32c029` | PR fermée sans fusion directe | [#118](https://github.com/grg914/dlss-nr-manager/pull/118) |
| `research/v4-realesrgan-openmp-free-reconciled` | `4092e4fc24` | PR fusionnée | [#119](https://github.com/grg914/dlss-nr-manager/pull/119) |
| `security/v4-ai-studio-model-install-rollback` | `5679f61ccb` | PR fusionnée | [#101](https://github.com/grg914/dlss-nr-manager/pull/101) |
| `security/v4-cleanup-acceptance-reconciled` | `10f005c52e` | PR fusionnée | [#122](https://github.com/grg914/dlss-nr-manager/pull/122) |
| `security/v4-crash-and-safe-removal-reconciled` | `8c1abf61cf` | PR fusionnée | [#117](https://github.com/grg914/dlss-nr-manager/pull/117) |
| `security/v4-finalization-safety-docs-reconciled` | `30a32cedf5` | PR fusionnée | [#112](https://github.com/grg914/dlss-nr-manager/pull/112) |
| `security/v4-pc-clean-parent-reparse` | `5e5af82e79` | PR fermée sans fusion directe | [#121](https://github.com/grg914/dlss-nr-manager/pull/121) |
| `security/v4-recovery-parent-reparse-guard` | `b4e2434b13` | PR fermée sans fusion directe | [#110](https://github.com/grg914/dlss-nr-manager/pull/110) |
| `security/v4-safe-component-removal` | `5dd1fe222d` | PR fermée sans fusion directe | [#116](https://github.com/grg914/dlss-nr-manager/pull/116) |
| `security/v4-trusted-gpu-probing` | `05166b055e` | PR fusionnée | [#99](https://github.com/grg914/dlss-nr-manager/pull/99) |
| `test/v4-windows-acceptance-audit` | `474a455f1b` | PR fermée sans fusion directe | [#120](https://github.com/grg914/dlss-nr-manager/pull/120) |
| `test/v4-windows-jobobject-crash` | `87736db34a` | PR fermée sans fusion directe | [#115](https://github.com/grg914/dlss-nr-manager/pull/115) |

## Méthode de suivi
1. Fermer les écarts techniques avérés en ouvrant de nouvelles PR fondées sur `main`.
2. Marquer les anciennes branches « réconciliées » seulement après comparaison du diff et tests.
3. Attacher les preuves d'audit et la décision de nettoyage à une issue GitHub.
4. Vérifier le workflow Build et CodeQL sur `main` après chaque merge.
5. Ne pas taguer `v4.0` sans recette réelle Windows/NVIDIA, licence/provenance vérifiée et acceptation.
