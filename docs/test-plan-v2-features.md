# Plan de test — lot de features (audio, image, scoreboard, équipes, participants restreints, manche à thèmes)

À exécuter en local (`dotnet run` + `ng serve`, ou sur le serveur de prod) avec au moins **2 fenêtres joueur + 1 fenêtre host** ouvertes en parallèle (idéal : navigateur normal pour le host, navigation privée ou navigateur différent pour chaque joueur, pour ne pas mélanger les `localStorage`).

Coche au fur et à mesure. Si un point échoue, note le comportement observé — utile pour me le signaler précisément.

---

## 0. Pré-requis

- [ ] `dotnet ef database update` appliqué (déjà fait en local pendant le dev, à refaire sur le serveur de prod avant de tester là-bas)
- [ ] Un quiz de test existant avec au moins une manche `qa-text` classique (pour les tests de non-régression)

---

## 1. Non-régression — rien de cassé sur l'existant

Le moteur des manches (`qa-text`, `zoom-image`, `blind-test`) a été touché indirectement (nouveaux champs sur `FeatureRuntimeState`, renommage de statuts) — à vérifier avant tout le reste :

- [ ] Créer/ouvrir un quiz existant → toutes les manches et questions s'affichent correctement dans l'éditeur
- [ ] Lancer une session, rejoindre avec 2 joueurs
- [ ] Manche `qa-text` classique (sans buzzer) : question s'affiche, réponse texte fonctionne, score s'incrémente
- [ ] Manche `zoom-image` : le dézoom progressif fonctionne, les paliers de points diminuent normalement
- [ ] Mode buzzer (sur `qa-text` ou `blind-test`) : buzz, résolution correct/incorrect par le host, le minuteur se fige bien pendant la résolution
- [ ] Validation manuelle : réponse en attente, le host valide/invalide, le score se met à jour
- [ ] Ajustement manuel du score d'un joueur (bouton ✎) fonctionne toujours
- [ ] `Dupliquer` un quiz existant fonctionne (vérifie que la copie récursive manches/questions n'a rien cassé)
- [ ] Fin de partie normale (dernière manche, dernière question) → statut `Finished`

---

## 2. Manche "Image à deviner"

- [ ] Dans l'éditeur, `+ Ajouter une manche` → "Image à deviner" apparaît dans la liste des types
- [ ] Créer une question : upload d'image fonctionne, aperçu s'affiche, réponses acceptées enregistrées
- [ ] Cette manche partage bien la config Question/Réponse (buzzer, validation manuelle, retry, scoring au rang — tous les réglages qa-round-config s'appliquent)
- [ ] Côté joueur (`/play`) : l'image s'affiche **intégralement dès le début** (pas de zoom progressif, contrairement à zoom-image)
- [ ] Côté host (`host-live`) : l'image s'affiche dans "Question en cours" avec les réponses acceptées visibles

---

## 3. Scoreboard en panneau latéral

- [ ] Host clique "Afficher les scores aux joueurs"
- [ ] Côté joueur : le classement apparaît **à côté** (écran large) ou **en dessous** (mobile/fenêtre étroite) de la question — la question reste visible et jouable en même temps
- [ ] Réduire la largeur de la fenêtre navigateur sous ~700px → le classement passe en dessous (pas de scroll horizontal, pas de superposition)
- [ ] Host clique "Masquer les scores" → le panneau disparaît, la mise en page revient à un seul panneau centré

---

## 4. Lecteur audio (Blind Test)

- [ ] Lancer une question `blind-test` → le son démarre **automatiquement** côté joueur (pas besoin de cliquer play)
- [ ] Les 3 boutons ▶/⏸/⏹ sont présents (pas de curseur de progression) côté joueur
- [ ] ⏸ met en pause localement, ▶ reprend, ⏹ remet à 0 et coupe
- [ ] **Synchronisation buzzer** (test à 2 fenêtres joueur) : un joueur buzz → le son se met en pause **chez tous les joueurs simultanément** ; le host résout le buzz (correct/incorrect) → le son reprend **chez tous les joueurs au même endroit** (pas de retour à 0)
- [ ] Côté host, la prévisu (avant le lancement de la manche ciblée, et pendant la question en cours) affiche en plus une **timeline/curseur manuel**, absente côté joueur
- [ ] Le curseur host permet de scruber manuellement dans le son sans affecter la lecture des joueurs

---

## 5. Équipes

- [ ] Host-live → "Créer des équipes" : formulaire d'ajout d'équipes (nom + sélection de joueurs), un joueur ne peut pas être coché dans 2 équipes en même temps
- [ ] "Enregistrer les équipes" → les équipes apparaissent dans la carte "Équipes" avec leur nombre de joueurs et leur score (0 au départ)
- [ ] Bouton "Activer le mode équipe" (visible seulement si des équipes existent) sur une manche en cours **hors** manche à thèmes
- [ ] Avec le mode équipe actif : un joueur répond correctement → les points vont dans le **pot de l'équipe**, pas dans son score perso (vérifier `X perso + Y équipe` affiché sous son pseudo côté host)
- [ ] Score total affiché = perso + équipe, aussi bien côté host que côté joueur (`play__score` en haut, classement)
- [ ] Édition manuelle du score d'une équipe (bouton ✎ sur la ligne équipe) fonctionne comme pour un joueur
- [ ] Désactiver le mode équipe → les points suivants retournent au score perso

---

## 6. Manche à participants restreints (remplace l'ancien ciblage à un seul joueur)

- [ ] Dans l'éditeur, cocher "Manche à participants restreints" sur une manche classique
- [ ] Lancer la session jusqu'à cette manche → statut `AwaitingParticipants`, le sélecteur de participants s'affiche côté host
- [ ] Sélectionner 2-3 joueurs précis (pas tous) parmi ceux inscrits → "Démarrer la manche"
- [ ] Côté joueurs non sélectionnés : écran "👀 Vous êtes spectateur pour cette manche — c'est au tour de X, Y."
- [ ] Côté joueurs sélectionnés : la question est jouable normalement
- [ ] Un joueur spectateur qui essaie de répondre/buzzer via l'API est bien rejeté (403) — pas testable facilement sans devtools, sinon vérifier juste que l'UI ne montre pas de formulaire de réponse pour les spectateurs
- [ ] Refaire le test en sélectionnant une **équipe** au lieu de joueurs individuels (si des équipes existent) : tous les membres de l'équipe participent, les autres sont spectateurs, et le mode équipe s'active automatiquement pour cette manche
- [ ] Bouton "Tout le monde" du sélecteur : sélectionne tous les joueurs (ou toutes les équipes) d'un coup

---

## 7. Manche à thèmes

### Création (éditeur)

- [ ] `+ Manche à thèmes` crée une nouvelle manche de ce type dans la liste
- [ ] `+ Ajouter un thème` → le sélecteur de feature s'affiche → choisir un type (ex: qa-text) crée un thème
- [ ] Créer au moins 3 thèmes de types différents (ex: un zoom-image, un qa-text, un blind-test), avec des questions dans chacun
- [ ] Cliquer sur un thème dans la liste ouvre son éditeur imbriqué (titre, config, questions) — édition indépendante des autres thèmes
- [ ] Supprimer un thème fonctionne
- [ ] Enregistrer le quiz, recharger la page → la structure (manche à thèmes + tous ses thèmes + leurs questions) est bien conservée

### En jeu

- [ ] Lancer la session jusqu'à cette manche → statut `ChoosingTheme`, plateau affiché
- [ ] Par défaut, **tous les thèmes sont cachés** côté joueur (affichage "???")
- [ ] Côté host : bouton "Révéler tous les thèmes" (visible seulement si au moins un thème caché) + bouton "Révéler" individuel par thème
- [ ] Révéler un seul thème → seul celui-là affiche son vrai titre côté joueur, les autres restent "???"
- [ ] "Révéler tous les thèmes" → tous les titres apparaissent côté joueur
- [ ] Host clique "Choisir ce thème" sur un thème → le sélecteur de participants s'ouvre inline
- [ ] Sélectionner un ou plusieurs joueurs (ou une/des équipe(s)) et valider → la sous-manche démarre immédiatement (statut `Running`), les questions du thème s'enchaînent normalement
- [ ] Les joueurs non sélectionnés sont spectateurs pour ce thème (comme au point 6)
- [ ] Une fois toutes les questions du thème épuisées → retour automatique au plateau (`ChoosingTheme`), ce thème est marqué "✓ Joué" et grisé
- [ ] Sur un thème encore en attente, bouton "Skip" → marqué "⤼ Skippé", grisé, plus cliquable
- [ ] Choisir/jouer un deuxième thème avec une **sélection de participants différente** du premier (ex: joueurs différents, ou équipe au lieu de joueurs) — vérifie que la restriction est bien spécifique à chaque thème
- [ ] Une fois tous les thèmes résolus (joués ou skippés), le host clique "Question suivante" pour sortir de la manche à thèmes → passe à la manche suivante (ou fin de partie)
- [ ] Sortir prématurément d'une manche à thèmes (bouton "Question suivante" alors qu'il reste des thèmes en attente) fonctionne aussi — le host doit pouvoir forcer la sortie

---

## 8. Lien de join (contexte du bug résolu plus tôt)

- [ ] `https://quizz.eynwa.fr/join/<token>` en navigation privée (ou après vidage de cache) mène bien directement au choix du pseudo, connecté ou non en tant que host

---

## Environnements à couvrir

- [ ] Un passage complet en **local** (`dotnet run` + `ng serve`) avant de considérer que c'est bon
- [ ] Un passage sur le **serveur de prod** (`quizz.eynwa.fr`) après déploiement — n'oublie pas `dotnet ef database update` sur le serveur avant de démarrer le service, sinon les nouvelles tables n'existeront pas
