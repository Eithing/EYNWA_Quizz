# EYNWA_Quizz

Plateforme web de quiz gaming multi-épreuves (voir cahier des charges).

## Structure

- `client/` — Angular 19 (standalone components, signals, SCSS séparé du HTML)
- `server/` — ASP.NET Core (.NET 10) Web API + SignalR + EF Core / SQLite

## Lancer en local

### Backend

```
cd server
dotnet ef database update   # crée quiz.db à partir des migrations
dotnet run
```

API disponible sur `https://localhost:<port>` (voir `Properties/launchSettings.json`).
Hub SignalR : `/hubs/quiz`.

### Frontend

```
cd client
npm start
```

App disponible sur `http://localhost:4200`. CORS déjà ouvert côté API pour cette origine.

## Stockage

- Données relationnelles (sessions, équipes, scores, config des épreuves) : SQLite via EF Core.
- Fichiers média (images, sons, vidéos) : système de fichiers du serveur (dossier `server/media/`, non versionné), la base ne stocke que les métadonnées/chemins.
