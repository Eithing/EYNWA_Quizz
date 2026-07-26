# Quiz Party

Plateforme web permettant à un Game Master (GM) de créer, gérer et animer des quiz interactifs
avec ses amis sur Discord. Voir `quiz-party-spec.md` pour la spécification complète.

## Structure

- `backend/` — ASP.NET Core (.NET 10) Web API : auth Discord OAuth2 + JWT, EF Core / SQLite, SignalR (à partir de la Phase 2)
- `frontend/` — Angular (standalone components, signals)

## Prérequis : créer une Discord Application

L'authentification GM passe par Discord OAuth2. Avant de pouvoir te connecter :

1. Va sur https://discord.com/developers/applications
2. **New Application** → donne-lui un nom (ex: "Quiz Party Dev")
3. Onglet **OAuth2** → note le **Client ID**, clique **Reset Secret** pour générer et copier le **Client Secret**
4. Toujours dans OAuth2 → **Redirects** → ajoute exactement :
   ```
   http://localhost:5100/auth/discord/callback
   ```
5. Renseigne les identifiants en local (jamais commités) :
   ```
   cd backend
   dotnet user-secrets set "Discord:ClientId" "TON_CLIENT_ID"
   dotnet user-secrets set "Discord:ClientSecret" "TON_CLIENT_SECRET"
   ```

Sans ces user-secrets, l'app démarre et tout le reste fonctionne (les placeholders dans
`appsettings.json` empêchent juste un crash au démarrage), mais le bouton "Se connecter avec
Discord" échouera côté Discord tant que le Client ID n'est pas valide.

## Lancer en local

### Backend

```
cd backend
dotnet ef database update   # crée quizparty.db à partir des migrations
dotnet run
```

API sur `http://localhost:5100`.

### Frontend

```
cd frontend
npm start
```

App sur `http://localhost:4200`.

## Notes d'architecture

- **Auth GM** : Discord OAuth2 (via `AspNet.Security.OAuth.Discord`) → le backend échange le code,
  upsert le `GameMaster`, émet un **JWT applicatif** et redirige le navigateur vers
  `frontend/auth/callback?token=...`. Pas de cookie de session cross-origin : le JWT est stocké
  côté client et envoyé en `Authorization: Bearer` sur chaque appel API — plus simple à tester en
  local (pas de souci `SameSite`/`Secure` entre `localhost:4200` et `localhost:5100`).
- **Auth joueurs** : aucune — accès par lien de session à usage unique + pseudo (Phase 2+).
- **Stockage** : SQLite via EF Core. Le schéma complet de la section 5 de la spec est déjà en place
  (`GameMaster`, `Quiz`, `Round`, `Question`, `GameSession`, `Player`, `Answer`, `ScoreAdjustment`),
  même si seul `GameMaster` est réellement utilisé pour l'instant (Phase 0).
