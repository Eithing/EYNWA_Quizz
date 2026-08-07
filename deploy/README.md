# Déploiement prod

`deploy.ps1` automatise la séquence de déploiement manuelle vers `quizz.eynwa.fr` :
git pull → build frontend → build backend → (coupure du service ici) → backup DB → migrations
EF Core → copie des binaires → redémarrage du service.

## Utilisation

Sur le serveur de prod (RDP), en PowerShell **administrateur** (nécessaire pour
`Stop-Service`/`Start-Service`), depuis n'importe quel dossier :

```powershell
C:\Users\Eithing\Documents\Prod-app-AutoDeploy\quizparty\src\deploy\deploy.ps1
```

Le script se repère par rapport à sa propre position dans le repo (pas besoin d'être dans un
dossier précis pour le lancer). Il faut juste que le repo ait déjà été `git pull`é une première
fois pour récupérer ce script.

Un log complet de chaque exécution est écrit dans `deploy/logs/` (ignoré par git).

## Paramètres

Tous optionnels, valeurs par défaut = config actuelle du serveur eynwa.fr :

```powershell
.\deploy.ps1 -ServiceName QuizPartyApi -AppDir C:\quizparty\app -DbPath C:\quizparty\data\quizparty.db
```

- `-SkipGitPull` : ne pas faire `git pull` (si le checkout est déjà à jour).

## Sécurité intégrée

- **Build avant coupure** : frontend et backend sont buildés pendant que le service tourne
  encore. Le service n'est arrêté qu'une fois le build validé — une erreur de build ne coupe
  jamais le site.
- **Sauvegarde automatique** de la base avant toute migration (`quizparty.db.bak-<horodatage>`).
- **Refus de pull silencieux** : si le checkout du serveur a des modifications locales
  inattendues (autre chose que le bruit connu `frontend/angular.json`, cf. `docs/`), le script
  s'arrête avant de risquer d'écraser quoi que ce soit — à traiter à la main.
- **`appsettings.Production.json`** (secrets + connection string réels) n'est jamais dans le
  build publié donc jamais écrasé par la copie des binaires.
- **En cas d'échec** après l'arrêt du service, le script tente de le redémarrer quand même
  (avec les binaires disponibles à ce stade) plutôt que de laisser le site down.

## Ce que le script ne fait PAS (encore)

- Pas de rollback automatique des binaires en cas d'échec en pleine copie (rare, mais possible).
  En cas de pépin : les anciens binaires restent dans un état inconnu, revérifier à la main via
  `C:\quizparty\app`.
- Pas de vérification post-déploiement que le site répond réellement (juste que le service
  Windows a démarré) — un coup d'œil sur `https://quizz.eynwa.fr` après coup reste recommandé.
