# Quiz Party — Spécification projet

## 1. Contexte & Objectif

Groupe d'amis sur Discord organisant régulièrement des quiz (culture générale, jeux vidéo, thèmes divers) actuellement **faits à la main** (montage manuel des images/zooms, calcul des scores à l'oral, etc.).

**Objectif** : créer une plateforme web permettant à un "Game Master" (GM) de créer, gérer et animer des quiz interactifs avec ses amis, en remplaçant le travail manuel par des outils dédiés, tout en gardant l'esprit "projet fun entre potes" (pas de sur-ingénierie sécurité).

### Non-objectifs (hors scope V1)
- Pas de système de sécurité avancé (pas de RGPD, pas de 2FA, pas de rate limiting poussé).
- Pas de montée en charge massive (quelques dizaines de joueurs simultanés max).
- Pas de modération de contenu.
- Pas de version mobile native (juste responsive web).

---

## 2. Stack technique

| Couche | Techno |
|---|---|
| Frontend | Angular (dernière version stable), TypeScript |
| Backend | ASP.NET Core (C#), Web API + SignalR |
| Base de données | SQLite (via EF Core) |
| Temps réel | SignalR (WebSocket) |
| Auth GM | Discord OAuth2 |
| Auth joueurs | Aucune — accès par lien de session à usage unique + pseudo |
| Hébergement cible | Home server — actuellement Windows 11, potentiellement Linux plus tard — **tout doit rester cross-platform** (.NET + SQLite le sont nativement) |
| Déploiement | Prévoir un `docker-compose.yml` optionnel pour faciliter la portabilité Windows → Linux, sans que ce soit obligatoire pour tourner en natif |

**Contraintes d'hébergement** : DNS + ports ouverts existants côté utilisateur — l'app doit fonctionner correctement derrière ce type d'exposition (attention aux cookies OAuth, à l'URL de redirection Discord, aux origines CORS, et au fait que SignalR doit accepter les WebSockets à travers l'exposition réseau).

---

## 3. Architecture générale

Trois "profils" de client, tous servis par la même app Angular mais avec des routes/vues différentes :

1. **GM (authentifié)** : Login → Librairie de quiz → Création/édition de quiz → Lancement de session → Page d'administration live.
2. **Joueur (anonyme)** : arrive via un lien de session → saisit un pseudo → écran de jeu personnel (chaque joueur voit sa propre vue, y compris l'image zoomée).
3. *(Pas de vue spectateur/écran commun en V1 — chaque joueur a son propre écran complet.)*

### Flux temps réel (SignalR)
Un **Hub** central par session de jeu, avec des groupes SignalR :
- Groupe `session-{sessionId}` : tous les joueurs + le GM.
- Le GM envoie des commandes (démarrer manche, question suivante, valider/invalider une réponse, ajuster un score, annuler une question).
- Le serveur pousse l'état du jeu (question courante, niveau de zoom courant, temps restant, scores) à tous les clients connectés à la session.
- Reconnexion : si un joueur perd la connexion/rafraîchit, il doit pouvoir se "ré-attacher" à la session via un token stocké en `localStorage` (playerSessionToken), sans perdre son pseudo ni son score.

---

## 4. Architecture "Feature Plugin" (important — pensé pour l'extensibilité)

Un **Quiz** est composé d'une ou plusieurs **Manches (Rounds)**. Chaque manche a un **type de feature** (ex: `zoom-image`, et plus tard `blind-test`, `qcm`, `buzzer`, etc.).

### Principe
- Chaque feature définit :
  - Un **schéma de configuration** (JSON) propre à la manche (ex: paliers de zoom, durées).
  - Une **liste de questions** typées pour cette feature (ex: pour `zoom-image` : image + réponse(s) acceptée(s) + coordonnées du point de zoom).
  - Un **comportement d'exécution** côté backend (cycle de vie de la manche : start / next / submit-answer / validate / score / end).
  - Un **rendu** côté frontend, à la fois pour le joueur, pour l'admin (contrôle live), et pour la **prévisualisation** (preview) dans l'éditeur.

### Modélisation technique proposée

**Backend (C#)**
```
IQuizFeature
├── string TypeKey  // "zoom-image"
├── Type ConfigType
├── Type QuestionType
├── StartRound(...)
├── NextQuestion(...)
├── SubmitAnswer(...)
├── ComputeAutoScore(...)   // si mode auto activé
└── GetPreviewState(...)    // pour la preview GM
```
Chaque feature est enregistrée dans un registre (`FeatureRegistry`) au démarrage de l'app — permet d'ajouter une nouvelle feature sans toucher au cœur du moteur de jeu.

**Frontend (Angular)**
- Un `FeatureComponentRegistry` qui mappe `typeKey` → composants Angular (`PlayerComponent`, `AdminControlComponent`, `EditorComponent`, `PreviewComponent`) chargés dynamiquement (standalone components + `NgComponentOutlet` ou lazy loading par route).

**Stockage config/questions** : colonnes `ConfigJson` et table `Questions` avec une colonne `PayloadJson` (schéma libre par type), pour éviter une table par feature.

---

## 5. Modèle de données (SQLite / EF Core)

```
User (GameMaster)
- Id, DiscordId, Username, AvatarUrl, CreatedAt

Quiz
- Id, OwnerId (User), Title, Description, CreatedAt, UpdatedAt

Round
- Id, QuizId, Order, FeatureTypeKey, Title, ConfigJson

Question
- Id, RoundId, Order, PayloadJson  // structure dépend de FeatureTypeKey

GameSession
- Id, QuizId, InviteToken (unique, généré à chaque lancement), Status (Lobby/Running/Paused/Finished), CreatedAt, ExpiresAt

Player
- Id, SessionId, Pseudo, ConnectionToken (pour reconnexion), JoinedAt

Answer
- Id, SessionId, PlayerId, QuestionId, RawAnswer, IsCorrect (nullable -> null tant que non jugé), PointsAwarded, ValidationMode (Auto/Manual), ValidatedByGmAt

ScoreAdjustment  // historique des corrections manuelles du GM (ajout/retrait/annulation)
- Id, SessionId, PlayerId, QuestionId (nullable), Delta, Reason, CreatedAt
```

**Note validation des réponses** : configurable **par manche** (`ConfigJson.ValidationMode = "Auto" | "Manual"`).
- **Auto** : comparaison texte tolérante aux fautes (normalisation : minuscule, accents, trim, + distance de Levenshtein/similarité configurable) contre une liste de réponses acceptées par question.
- **Manual** : le GM voit toutes les réponses en live dans l'admin et valide/invalide chacune.
- **Dans tous les cas** : le GM peut à tout moment ajuster manuellement les points (ajouter/retirer/annuler une question pour un joueur) via `ScoreAdjustment`, y compris a posteriori sur une réponse déjà auto-validée.

---

## 6. Feature V1 : Zoom progressif (`zoom-image`)

### Configuration de la manche (`ConfigJson`)
```json
{
  "validationMode": "Auto",
  "autoAdvance": true,
  "answerTimeSeconds": 30,
  "zoomSteps": [
    { "level": 5, "durationSeconds": 10, "points": 100 },
    { "level": 3, "durationSeconds": 10, "points": 60 },
    { "level": 1.5, "durationSeconds": 10, "points": 30 }
  ],
  "finalLevel": 1
}
```
- `zoomSteps` : liste ordonnée et **réglable** (niveau de zoom, durée du palier, points attribués si trouvé pendant ce palier). Le nombre de paliers n'est pas fixé en dur.
- `autoAdvance` : passage automatique à la question suivante en fin de cycle, ou attente d'une action du GM.
- `answerTimeSeconds` : temps max pour répondre (peut différer de la durée totale du zoom si besoin).

### Question type (`PayloadJson`)
```json
{
  "imageUrl": "/media/xxx.jpg",
  "acceptedAnswers": ["The Legend of Zelda", "Zelda"],
  "zoomFocusPoint": { "x": 0.42, "y": 0.65 }  // en % de l'image, réglé via un sélecteur visuel dans l'éditeur
}
```
- `zoomFocusPoint` : défini par le GM en cliquant sur l'image dans l'éditeur (pas forcément le centre).
- Upload de l'image géré côté backend (stocké sur disque, chemin en DB).

### Déroulé côté joueur
- L'image s'affiche zoomée sur le point défini, au niveau du premier palier.
- Un timer visuel indique le temps restant sur le palier courant.
- Le joueur peut soumettre sa réponse à tout moment ; le niveau de zoom au moment de la soumission détermine les points (via le palier en cours).
- Dézoom progressif entre paliers (transition animée, pas un cut brutal) selon la config.

### Côté admin (contrôle live)
- Vue d'ensemble : image, palier courant, réponses reçues en temps réel (pseudo + réponse + statut auto/à valider).
- Actions : passer à la question suivante manuellement (si `autoAdvance = false`), forcer la validation/invalidation d'une réponse, ajuster un score, annuler la question en cours pour tout le monde.

### Preview (éditeur)
- Le GM peut lancer une preview solo de la manche/question directement depuis l'éditeur, avec les mêmes paliers de zoom, sans créer de session réelle ni impacter de scores.

---

## 7. Parcours utilisateurs (flows)

### GM
1. **Login** via Discord OAuth → redirection vers la Librairie.
2. **Librairie** : liste des quiz créés par ce GM (titre, nb de manches, date de modif) → actions : Éditer / Lancer / Dupliquer / Supprimer / + Nouveau quiz.
3. **Éditeur de quiz** : titre/description, liste ordonnée de manches (ajouter une manche → choisir un type de feature → configurer les paramètres → ajouter les questions une à une ou en lot) + bouton "Preview" par manche.
4. **Lancement d'une session** : depuis la librairie, "Lancer" génère une `GameSession` avec un `InviteToken` unique → redirige le GM vers la **page d'administration live** de cette session, qui affiche le lien à partager (Discord) et une salle d'attente listant les joueurs qui rejoignent.
5. **Page d'administration live** : contrôle du déroulé (start/pause/next), suivi des réponses/scores, validations manuelles, ajustements de points, vue globale du classement.

### Joueur
1. Clique sur le lien d'invitation → page "Entrer un pseudo" (pas de compte).
2. Salle d'attente jusqu'à ce que le GM démarre.
3. Écran de jeu personnel, synchronisé en temps réel avec l'état de la session.
4. Écran de classement final en fin de session.

---

## 8. Découpage en phases (pour guider Claude Code)

### Phase 0 — Setup
- Structure repo : `/backend` (ASP.NET Core), `/frontend` (Angular), `docker-compose.yml` (optionnel).
- Config EF Core + migrations SQLite.
- Auth Discord OAuth2 fonctionnelle (login GM, session/JWT ou cookie).

### Phase 1 — Cœur : Librairie & Éditeur de quiz (sans feature de jeu encore)
- CRUD Quiz / Round / Question générique.
- UI Librairie + Éditeur (sans encore la logique métier des features).
- Architecture `FeatureRegistry` (backend) + `FeatureComponentRegistry` (frontend), même avec une seule feature pour l'instant.

### Phase 2 — Feature "Zoom progressif" complète
- Config + éditeur visuel (upload image, sélection du point de zoom, réglage des paliers).
- Moteur d'exécution backend (cycle de vie manche/question, timers serveur-autoritaires).
- Hub SignalR (events, groupes de session).
- Écran joueur (zoom animé, soumission réponse, reconnexion).
- Écran admin live (contrôle, validation, scores).
- Preview dans l'éditeur.

### Phase 3 — Polish
- Salle d'attente + gestion pseudo joueur (unicité dans la session).
- Classement final animé.
- Historique des sessions passées par quiz.
- Thème (dark mode par défaut), responsive mobile pour les joueurs.

### Phase 4 (backlog, hors implémentation immédiate)
- Nouvelles features (QCM, buzzer, blind-test...).
- Banque de questions réutilisables entre quiz.
- Export/Import JSON d'un quiz.

---

## 9. Points à valider / prérequis avant de lancer Claude Code

- [ ] Créer une **Discord Application** (portail développeur Discord) pour obtenir Client ID/Secret OAuth, avec la redirect URI qui correspond au DNS de ton home server.
- [ ] Définir la **redirect URI exacte** et le domaine utilisé (ex: `https://quiz.mondomaine.fr/auth/callback`) pour éviter les soucis CORS/cookies.
- [ ] Décider si l'app tourne directement en process natif (Kestrel) sur le home server, ou via Docker dès la V1.
- [ ] Confirmer le format de tolérance aux fautes souhaité pour le mode "Auto" (ex: distance de Levenshtein ≤ 2, insensible à la casse/accents) — un réglage par défaut sera implémenté et ajustable.

---

## 10. Prompt de démarrage suggéré pour Claude Code

> Utilise ce document (`quiz-party-spec.md`) comme spécification de référence. Commence par la **Phase 0** puis la **Phase 1**, en respectant l'architecture "Feature Plugin" décrite en section 4 même s'il n'y a qu'une seule feature implémentée pour l'instant. Demande confirmation avant de passer à la phase suivante.

---

## Journal des décisions (session Claude Code)

Ces points ont été tranchés en dehors du texte original de la spec, au fil de l'implémentation :

- **Branche de travail** : `V2`, créée depuis `main` (pas de branche `master` dans ce repo).
- **Auth GM** : JWT applicatif émis par le backend après l'échange OAuth Discord (via `AspNet.Security.OAuth.Discord`), plutôt qu'un cookie de session — évite les soucis `SameSite`/`Secure` en dev HTTP cross-origin (`localhost:4200` ↔ `localhost:5100`), et réutilise le pattern JWT + intercepteur déjà validé sur une itération précédente du projet.
- **Hébergement Phase 0** : process natif Kestrel (pas de `docker-compose.yml` pour l'instant), test en local uniquement, déploiement repoussé à plus tard.
- **Secrets Discord** : stockés via `dotnet user-secrets` (jamais commités), placeholders non-vides dans `appsettings.json` pour éviter un crash au démarrage (le middleware d'auth ASP.NET Core valide les options OAuth sur chaque requête, y compris avec un `ClientId` vide).
- **Périmètre session** : Phase 0 uniquement (setup + EF Core + auth Discord de bout en bout), point d'étape avant la Phase 1.
