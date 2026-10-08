# Checkpoints R046–R050 — complément append-only du journal

Ce fichier complète `AI_PROJECT_PROGRESS.txt` : le fichier principal dépasse 250 000 caractères, et son écriture intégrale par le connecteur GitHub a été rejetée. **Ne pas prétendre que R046–R050 figurent déjà dans le journal principal.** Les checkpoints R039–R045 sont vérifiés dans `AI_PROJECT_PROGRESS.txt` sur `main`. Le document exhaustif `docs/BRANCH_AUDIT_2026-10-08.md` contient les preuves de l'inventaire.

## R046 — Publication transitoire de l'asset Streamline (8 octobre 2026)

Le Build de #122 a échoué vers 20:53:20Z sur `gh release download runtime-seed-v1 --pattern streamline-runtime-v2.14.1-win-x64.zip` (« no assets match »). L'API GitHub a confirmé le retour du même asset, avec `updated_at=20:53:23Z`, taille 171 509 289 octets et digest SHA-256 `28491d3610650ab08cde1420668123bb5299601e27eef53c2715b1464eb04660`. Le workflow de publication emploie `--clobber` sur un tag mutable; cela est compatible avec une courte fenêtre de non-disponibilité, sans démontrer l'origine exacte. Issue #123 ouverte, aucune vérification d'intégrité désactivée.

## R047 — Rerun de l'audit Streamline

Le rerun des tâches échouées du Build `37842287560` a réussi sur le même commit `10f005c52e5daa89f7b0f1c7fa113ee138e13b48` avec l'asset rétabli. Le Build et CodeQL de la PR #122 ont finalement réussi, sans changement de digest ni suppression de contrôle. Issue #123 ouverte pour correction durable (publication atomique ou retry borné et vérifié).

## R048 — Inventaire release stable et seed

Dernière release stable vérifiée : `v3.2.0`, publiée le 7 octobre 2026; 29 assets avec digests GitHub SHA-256 présents. VLC `vlc-3.0.24-win64.zip` et `nuget-offline.zip` résident dans `runtime-seed-v1`, et non dans les 29 assets de la release stable. `VlcRuntimeService.ResolveAssetAsync` teste la dernière release, puis le seed et valide le SHA-256. Ce constat est **une inspection de code et de métadonnées**, pas un test d'installation VLC sur Windows.

## R049 — Inventaire des branches

Audit GitHub API paginé : 125 branches recensées après création de `docs/v4-branch-audit-20261008`, dont 88 liées à une PR fusionnée, 24 liées à une PR fermée sans fusion directe, 12 sans PR connue (incluant la branche documentaire), et `main`. Les 6 divergences significatives sans PR et les 24 branches à PR fermée nécessitent comparaison sémantique et décision individuelle avant merge ou suppression. **Aucune branche n'a été supprimée** : l'outil de suppression n'est pas disponible. Détails individuels et SHAs dans `docs/BRANCH_AUDIT_2026-10-08.md`; issue #124.

## R050 — Consolidation sur main

PR #122 fusionnée dans `main` SHA `3e339572fba05f15fa89ba40161ad59b84e083dc`, avec Build `37842287560` et CodeQL `37842287567` verts et **126 tests réussis, aucun échec ni ignoré** sur son commit testé. PR #120 et #121 fermées comme reprises. `AI_PROJECT_PROGRESS.txt` vérifié sur `main` pour R039–R045; script Windows d'acceptation et correctif PC Clean également vérifiés sur `main`. GitHub a déclenché Build `37845241716` et CodeQL `37845241728` sur le `main` post-fusion; **en cours au dernier contrôle**. La release téléchargeable reste v3.2.0, les nouveautés de main ne sont pas distribuées dans ce binaire. La v4.0 **n'est pas publiée**.

## Prochaines actions

- Vérifier les deux workflows sur `main` (SHA exact), y compris packaging Windows x64.
- Auditer les forks historiques/branches divergentes sans écraser `main` et conserver les corrections utiles par PR.
- Réduire la dépendance à un `runtime-seed-v1` mutable pour la fiabilité de la CI (#123).
- Ajouter CodeQL aux checks requis de Protect main (#82) via accès administrateur autorisé.
- Valider licences OpenMP (#79), mise à jour de tous les composants (#111), fermeture/rollback Windows réel (#95) avant une release v4.
