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

- [ ] **Régression du bug corrigé** : côté host, les titres des thèmes sont **toujours visibles** (jamais "???" pour le GM), avec un badge 👁/🙈 indiquant si le thème est visible ou caché aux joueurs

---

## 8. Lien de join (contexte du bug résolu plus tôt)

- [ ] `https://quizz.eynwa.fr/join/<token>` en navigation privée (ou après vidage de cache) mène bien directement au choix du pseudo, connecté ou non en tant que host

---

## 9. Manche "Au plus proche"

- [ ] Créer une manche "Au plus proche", une question avec un texte + une valeur numérique exacte (ex: "Hauteur de la tour Eiffel (m)" / 330)
- [ ] Configurer "révélation automatique" — 2-3 joueurs soumettent une estimation numérique, le champ n'accepte que des nombres
- [ ] À la fermeture de la fenêtre de réponse (temps écoulé), le classement se calcule **automatiquement** : chaque joueur voit son résultat (plus proche = "🏆 Meilleure estimation !" + points, sinon "Pas cette fois")
- [ ] Reconfigurer en "révélation manuelle" — après que tout le monde ait répondu (ou que le temps soit écoulé), le bouton "Révéler le classement" apparaît côté host ; cliquer dessus déclenche le classement
- [ ] Activer le mode dégressif (1er = X points, puis -Y par rang) et vérifier que le 2e/3e plus proche reçoit bien moins que le 1er
- [ ] Désactiver le dégressif ("le plus proche seulement") — vérifie que seul le plus proche marque des points, les autres 0
- [ ] **Mode équipe** : activer le mode équipe, faire estimer 2+ joueurs de la même équipe des valeurs différentes → vérifier que le classement se fait sur la **moyenne** des estimations de l'équipe (pas sur chaque joueur individuellement), et que les points vont dans le pot d'équipe
- [ ] Une estimation vide ou non numérique ne casse rien (ne marque simplement jamais de points)

---

## 10. Manche "À quoi pense l'autre"

- [ ] Créer une manche "À quoi pense l'autre" avec 2-3 questions ouvertes (pas de réponse pré-écrite dans l'éditeur)
- [ ] Lancer la session jusqu'à cette manche → statut `AwaitingAnswerer`, le host voit la liste des joueurs pour désigner le répondant
- [ ] Désigner un joueur A → lui seul voit la question et peut y répondre (les autres voient un écran d'attente "spectateur")
- [ ] A soumet sa réponse en privé → message "Réponse enregistrée en privé. En attente que l'hôte lance la phase de devinette…" — **aucun autre joueur ni le host (côté joueur) ne voit cette réponse**
- [ ] Côté host, une fois A a répondu : le bouton "Passer à la devinette" est disponible, et la réponse de A s'affiche déjà en référence ("Réponse du répondant : ...") avant même de lancer la phase 2
- [ ] Cliquer "Passer à la devinette" ouvre le sélecteur de participants (A est exclu de la liste) — désigner un joueur B (ou une équipe)
- [ ] B (et lui seul, ou l'équipe désignée) voit la question et peut tenter de deviner — en mode buzzer (par défaut) : B buzze, répond à l'oral, le host valide correct/incorrect comme un buzzer classique
- [ ] Passer la manche en mode non-buzzer (`ValidationMode: Auto`, `BuzzerMode` décoché dans l'éditeur) : B tape sa réponse, elle est comparée automatiquement à la réponse de A (tolérance aux fautes incluse) sans intervention du host
- [ ] A ne peut pas être sélectionné comme devineur de sa propre réponse (vérifier que l'option est absente ou rejetée)
- [ ] Passer à la question suivante de la même manche → un **nouveau** répondant peut être désigné (peut être différent de A), le cycle recommence (`AwaitingAnswerer` → réponse privée → devinette)
- [ ] Refaire le test avec une **équipe entière** comme devineurs plutôt qu'un seul joueur B — tous les membres de l'équipe peuvent tenter, les points vont dans le pot d'équipe

---

## 11. Corrections de bugs (session du 03/08)

### 11.1 Création/affectation d'équipes
- [ ] Créer une équipe, cocher un premier joueur → OK
- [ ] Cocher un **second** joueur dans la **même** équipe → doit maintenant s'enregistrer correctement (c'était le bug : la case à cocher agissait sur le mauvais index en interne)
- [ ] Créer 2-3 équipes, cocher/décocher librement des joueurs dans chacune → plus de saut d'un joueur vers une équipe non voulue, plus de case bloquée
- [ ] Un joueur déjà pris par une autre équipe apparaît bien grisé/non cochable dans les autres

### 11.2 Timeline de prévisualisation audio
- [ ] Dans l'**éditeur de quiz**, sur une question blind-test, uploader/prévisualiser un son → la timeline (curseur de progression) est maintenant présente, comme elle l'était déjà en session live

### 11.3 Révélation "Au plus proche"
- [ ] Si tous les joueurs éligibles répondent avant la fin du temps → la fenêtre se ferme **immédiatement** (plus besoin d'attendre la fin du minuteur)
- [ ] Une fois la fenêtre fermée (par fin de temps réel ou clôture anticipée), **tous les joueurs** voient la liste de **tous les essais** (pseudo + valeur), avant même la révélation du gagnant
- [ ] Tant que non révélé : pas de valeur exacte affichée, pas de points, juste "en attente que l'hôte révèle le classement…"
- [ ] Le host clique "Révéler le classement" → la valeur exacte et les points par joueur apparaissent pour tout le monde, le(s) gagnant(s) marqué(s) 🏆
- [ ] Cet affichage reste visible tant que le host n'a pas cliqué "Question suivante" (plus d'avance automatique surprise sur cette manche, même avec l'ancienne case "auto-advance")

### 11.4 Double score "À quoi pense l'autre"
- [ ] Quand le devineur B trouve la bonne réponse (buzzer validé par le host, ou saisie auto-validée), **le répondant A gagne aussi les mêmes points**, pas seulement B — vérifier les deux scores augmentent
- [ ] En mode équipe côté devineurs, chaque devineur qui trouve correctement fait aussi gagner les points à A (en plus des siens)

---

## 12. Corrections de bugs (session du 04/08)

### 12.1 Suppression d'un quiz avec manche à thèmes
- [ ] Supprimer un quiz contenant une manche à thèmes (sous-manches) → doit maintenant réussir (c'était le bug : contrainte `FOREIGN KEY` bloquante sur `ParentRoundId`, corrigée en cascade)
- [ ] Vérifier que les sous-manches/questions associées disparaissent bien aussi (pas d'orphelins en base)
- [ ] Non-régression : supprimer un quiz classique (sans thèmes) fonctionne toujours

### 12.2 Espacement pseudo/score côté host
- [ ] Dans la liste des joueurs côté `host-live`, le pseudo et le score ne sont plus collés ("Eithing 0 pts" au lieu de "Eithing0 pts")

### 12.3 Liste des joueurs ayant trouvé côté joueur
- [ ] Sur une question avec buzzer/plusieurs trouveurs, la liste "ont trouvé" côté `/play` s'affiche maintenant sous forme de badges numérotés lisibles, plus de texte collé ("1.Eithing2.Freyja")

### 12.4 Choix du mode équipe avant le démarrage de chaque manche
- [ ] Créer au moins une équipe dans la session
- [ ] Lancer une manche (non restreinte, hors manche à thèmes/partner-guess) → le host arrive sur un écran "Cette manche se joue-t-elle en mode équipe ?" avec un aperçu de la 1ère question, **avant** que le minuteur démarre
- [ ] Les joueurs voient "En attente que l'hôte choisisse le mode de jeu…" pendant ce temps, impossible de répondre
- [ ] Cliquer "Mode équipe" → la manche démarre en mode équipe (points au pot), le minuteur démarre à cet instant seulement
- [ ] Cliquer "Mode solo" → la manche démarre en mode perso (points individuels)
- [ ] Le bouton de bascule mode équipe en cours de manche (`toggleTeamScoring`) reste disponible une fois la manche lancée, pour changer d'avis en route
- [ ] Sans équipe créée dans la session, ce nouvel écran n'apparaît jamais (comportement identique à avant)
- [ ] Non-régression : une manche à participants restreints (`AwaitingParticipants`) sélectionnant une équipe active toujours directement le mode équipe sans passer par ce nouvel écran (le choix de participants fait déjà office de choix de mode)

### 12.5 Isolation des données entre sessions rejouant le même quiz
- [ ] Jouer une manche "Au plus proche" dans une session, la terminer (ou la laisser en cours), puis démarrer une **nouvelle session** sur le **même quiz** et rejouer la même question
- [ ] Le classement/révélation de la nouvelle session ne doit afficher **que** les réponses des joueurs de cette nouvelle session (plus de mélange avec les essais d'une session précédente sur la même question)
- [ ] Idem pour une manche à scoring au rang classique (`qa-text`/`zoom-image` avec "scoring au rang" activé) rejouée dans 2 sessions différentes : le rang/les points ne doivent pas être influencés par l'autre session

### 12.6 Égalité sur "Au plus proche"
- [ ] Faire répondre 2 (ou plus) joueurs avec la **même distance exacte** à la valeur cible (ex : cible 800, joueur A répond 799, joueur B répond 801)
- [ ] À la révélation, les 2 joueurs à égalité doivent recevoir **les mêmes points** (tous deux 🏆 si c'est le meilleur score), pas un gagnant arbitraire
- [ ] Si le scoring est dégressif par rang (`RankBasedScoring`), le rang suivant après une égalité doit sauter du nombre de joueurs ex æquo (2 joueurs à égalité au rang 0 → le 3e joueur est classé rang 2, pas rang 1)
- [ ] Non-régression : sans égalité, le classement se comporte comme avant (un seul gagnant, points dégressifs normaux)

### 12.7 Restriction de participants sur un thème (manche à thèmes)
- [ ] Sur une manche à thèmes, "Choisir ce thème" en ne cochant qu'**un seul joueur**
- [ ] Côté joueur non sélectionné : écran spectateur ("👀 Vous êtes spectateur pour cette manche") — la question ne doit **plus** être jouable pour lui
- [ ] Seul le joueur sélectionné voit la question et peut y répondre/buzzer
- [ ] Les joueurs non sélectionnés qui répondraient quand même (via l'API) doivent être rejetés (403), et ne doivent surtout pas marquer de points
- [ ] Refaire le test en sélectionnant une équipe au lieu d'un joueur : seuls ses membres participent
- [ ] Non-régression : sur une manche à thèmes où l'on sélectionne "tout le monde", tous les joueurs restent éligibles normalement

### 12.8 Cooldown de retentative en réponse écrite classique
- [ ] Sur une manche `qa-text` (ou `image-guess`/`blind-test`) hors buzzer, cocher "Autoriser plusieurs tentatives en cas de réponse fausse" → un nouveau champ "Délai avant de pouvoir renvoyer une réponse (s)" apparaît
- [ ] Régler ce délai à 5s, jouer la question, répondre faux → le formulaire de réponse reste bloqué pendant 5s avant de permettre une nouvelle tentative
- [ ] Si tous les autres joueurs répondent avant la fin des 5s, le joueur redevient éligible immédiatement (comme pour le cooldown buzzer existant)
- [ ] Délai à 0 (ou champ non modifié) → comportement inchangé, nouvelle tentative immédiate
- [ ] Même test sur une manche `zoom-image` avec "Autoriser plusieurs tentatives" coché
- [ ] Non-régression : le cooldown buzzer existant ("Délai avant de pouvoir re-buzzer") continue de fonctionner indépendamment sur les manches en mode buzzer

### 12.9 Sortie de la manche à thèmes (blocage après skip/fin de tous les thèmes)
- [ ] Sur une manche à thèmes, jouer ou skipper **tous** les thèmes jusqu'au dernier → un bouton "Manche suivante" apparaît maintenant en haut à droite (le host n'était plus bloqué sur le plateau)
- [ ] Cliquer dessus → passe bien à la manche suivante (ou termine la partie si c'était la dernière manche)
- [ ] Avec des thèmes encore en attente (`Pending`), le bouton est quand même présent mais libellé "Passer à la manche suivante (thèmes restants ignorés)" — vérifier qu'il fonctionne aussi dans ce cas (sortie anticipée assumée par le GM)

---

## 13. Manche "Ordonne la liste" (nouvelle feature — pas testée manuellement, à valider en priorité)

Cette manche n'a été vérifiée que par les builds (`dotnet build` + `npx ng build`, tous deux propres) et un
démarrage réussi des deux serveurs — **aucun test de glisser-déposer réel n'a été fait**, à couvrir
entièrement ici avant de considérer la feature fiable.

### 13.1 Création dans l'éditeur
- [ ] `+ Ajouter une manche` → "Ordonne la liste" apparaît dans la liste des types
- [ ] Config de manche : choisir le type de contenu (Texte / Image / Son), temps de réponse, points par item bien enchaîné
- [ ] Créer une question avec 5-6 items en **Texte** : glisser-déposer dans l'éditeur pour fixer l'ordre correct, ajouter/retirer des items
- [ ] Refaire avec le type **Image** : upload par item, aperçu affiché, glisser-déposer fonctionne toujours pour réordonner
- [ ] Refaire avec le type **Son** : upload par item, lecteur `<audio>` avec contrôles (pas de lecture auto simultanée de tous les items, contrairement au lecteur blind-test)
- [ ] Sauvegarder le quiz, recharger la page → l'ordre correct et le contenu de chaque item sont conservés

### 13.2 Partie solo (sans équipe)
- [ ] Lancer la session, rejoindre avec 2 joueurs solo (pas d'équipe créée)
- [ ] Chaque joueur voit un ordre mélangé **différent** au départ (shuffle indépendant par joueur)
- [ ] Glisser-déposer fonctionne au clavier/souris (desktop) et **au tactile** (tester sur un vrai téléphone ou le mode responsive des devtools — `@angular/cdk/drag-drop` doit gérer ça nativement)
- [ ] Reproduire l'exemple de référence : ordre correct 1-2-3-4-5-6, un joueur soumet 1-3-4-5-6-2 → doit obtenir les points de 5 items (chaîne 1-3-4-5-6), pas juste 1
- [ ] Cliquer "Valider mon classement" → le score s'affiche, la comparaison ordre soumis / ordre correct apparaît, les items bien enchaînés sont surlignés en vert
- [ ] Laisser le temps s'écouler **sans** cliquer "Valider" → le classement se finalise quand même automatiquement (avec le dernier ordre glissé, ou l'ordre initial si jamais touché)
- [ ] Le bouton "Suivant" reste toujours manuel après la révélation (pas d'avance automatique, comme closest-guess)

### 13.3 Partie en équipe (sync temps réel)
- [ ] Créer une équipe de 2 joueurs, activer le mode équipe sur cette manche
- [ ] Les deux fenêtres affichent le **même** ordre mélangé au départ (pas un ordre différent par joueur)
- [ ] Joueur A glisse un item → après le drop, joueur B voit le nouvel ordre se synchroniser (quasi temps réel, pas continu pendant le drag lui-même)
- [ ] Le score final est bien attribué à l'**équipe** (pot commun), pas au joueur qui a cliqué "Valider"
- [ ] Un joueur non sélectionné / spectateur ne peut pas glisser-déposer (message spectateur affiché)

### 13.4 Vue GM (host-live)
- [ ] "Question en cours" affiche l'ordre correct (le GM le voit toujours, comme pour les réponses acceptées des autres features)
- [ ] Un bloc par groupe (joueur solo ou équipe) en train de jouer, avec son ordre courant qui se met à jour en direct
- [ ] Une fois un groupe résolu, ses points s'affichent dans son bloc

### 13.5 Non-régression
- [ ] Les autres types de manches (qa-text, zoom-image, closest-guess, etc.) n'ont pas été affectés par les changements génériques (`IFeatureEngine.FinalizesPendingAnswersOnAdvance`, nouveau champ `AdvanceToNextQuestionAsync`) — repasser rapidement sur une manche qa-text classique pour confirmer qu'elle avance toujours normalement (Next, auto-advance, fin de partie)

---

## 14. Réponses multiples par question + points personnalisés par réponse (lot 1 — pas testé manuellement, à valider en priorité)

Aucun test manuel fait à ce stade — uniquement `dotnet build`/`npx ng build` propres. Couvre qa-text,
zoom-image, blind-test, image-guess. **Exclu volontairement** : partner-guess (reste à 1 réponse toujours,
comportement inchangé) et closest-guess (pas concerné, feature numérique séparée).

### 14.1 Non-régression — question à 1 réponse (comportement historique)
- [ ] Une question qa-text/zoom-image/blind-test/image-guess existante (créée avant ce lot, format `acceptedAnswers` legacy) reste jouable sans aucune modification : 1 seul champ affiché côté joueur, correction identique à avant
- [ ] Créer une NOUVELLE question à 1 seule réponse dans l'éditeur (via le nouveau composant "réponses attendues") → toujours 1 seul champ côté joueur, comportement identique

### 14.2 Réponses multiples, points uniformes
- [ ] Round config en mode "Points fixes" (par défaut), créer une question avec 2-3 réponses attendues distinctes (ex: "cite 2 pays d'Europe" → France, Allemagne)
- [ ] Côté joueur : le bon nombre de champs s'affiche, avec le texte "(N réponses attendues, X et X points)" au-dessus du formulaire (même valeur répétée puisque uniforme)
- [ ] Répondre correctement à une seule des N réponses (l'autre champ vide ou faux) → crédit partiel reçu (points de la seule réponse trouvée), pas 0
- [ ] Répondre correctement à toutes les réponses (peu importe l'ordre des champs) → tous les points, marqué comme "trouvé" (apparaît dans la liste des finders)
- [ ] Taper la même bonne réponse dans 2 champs différents → un seul crédit compté (chaque réponse attendue n'est réclamable qu'une fois)

### 14.3 Points personnalisés par réponse
- [ ] Round config en mode "Personnalisés par réponse" → le champ "Points par bonne réponse" du round disparaît, chaque réponse de l'éditeur de question a maintenant son propre champ points
- [ ] Créer une question à 2 réponses avec des points différents (ex: 1 et 2 points) → côté joueur, le texte affiché correspond ("2 réponses attendues 1 et 2 points")
- [ ] Répondre correctement seulement à la réponse à 2 points → 2 points reçus (pas 1, pas 3)
- [ ] Ambiguïté : configurer 2 réponses dont les variantes se chevauchent presque (l'une matche approximativement les deux via la tolérance aux fautes) avec des points différents → la réponse la plus généreuse est retenue en cas de doute

### 14.4 zoom-image spécifique
- [ ] Mode "Points personnalisés par réponse" sur une manche zoom-image → le champ "Points" de chaque palier de dézoom disparaît (remplacé par le réglage par réponse dans l'éditeur de question), et le score obtenu ne dépend plus du moment où on répond dans le dézoom (uniquement du barème par réponse)
- [ ] Mode "Points fixes"/"Dégressif par rang" (Uniform) inchangé : le dézoom continue de fonctionner normalement, aucun texte de barème affiché côté joueur (pas de valeur statique à montrer puisque ça dépend du palier)

### 14.5 Non-régression scoring au rang et buzzer
- [ ] Une manche en mode "Dégressif par rang" (RankBasedScoring) continue de fonctionner comme avant (1er = max, décroissant) — le sélecteur "Mode de points" doit correctement forcer ce mode et masquer les deux autres
- [ ] Une manche en mode buzzer n'affiche pas le sélecteur "Mode de points" (pas de sens en buzzer, jugement oral par le GM) et fonctionne comme avant

### 14.6 Vue GM (host-live)
- [ ] "Question en cours" liste chaque réponse attendue (variantes + points si mode personnalisé) au lieu de l'ancienne ligne unique "Réponses acceptées : ..."

---

## 15. Manche Choix Multiple / QCM (lot 2 — pas testé manuellement, à valider en priorité)

Aucun test manuel fait à ce stade — uniquement `dotnet build`/`npx ng build` propres. Nouvelle feature
`multiple-choice`, aucune migration EF (payload/config JSON comme toutes les autres features).

### 15.1 Création et affichage
- [ ] La feature "Choix Multiple" apparaît dans le sélecteur de features à la création d'une manche
- [ ] Éditeur de manche : réglage temps de réponse, auto-advance, mode de points (Uniforme/Personnalisé), champ points si Uniforme
- [ ] Éditeur de question : ajouter/retirer des options, cocher "Correcte" sur plusieurs d'entre elles, champ points par option si mode Personnalisé
- [ ] Côté joueur, les options apparaissent dans un ordre mélangé (pas l'ordre de saisie de l'éditeur) et sans jamais révéler lesquelles sont correctes avant de répondre

### 15.2 Plafond de sélection (anti-triche)
- [ ] Créer une question à 3 options dont 2 correctes → côté joueur, impossible de cocher une 3e case (les cases non cochées se désactivent visuellement une fois le plafond atteint) ; décocher reste toujours possible
- [ ] Envoyer directement à l'API une sélection dépassant le plafond (contournement du plafond côté client, ex. via un appel réseau manuel) → réponse rejetée (`isCorrect: false`, `0 point`), jamais de crédit partiel dans ce cas

### 15.3 Scoring
- [ ] Mode Uniforme : cocher exactement les bonnes réponses → tous les points (barème uniforme × nombre de bonnes réponses), marqué "Correcte"
- [ ] Mode Uniforme : cocher seulement une partie des bonnes réponses (sans dépasser le plafond) → crédit partiel (barème × nombre de bonnes réponses cochées), marqué "Incorrecte" (pas l'ensemble exact)
- [ ] Mode Personnalisé : configurer une question à 3 réponses correctes avec des points différents (ex: 1, 2, 3 pts) → cocher seulement celle à 2 points → 2 points reçus, pas 1 ni 3
- [ ] Cocher une combinaison incluant une option incorrecte (mais restant sous le plafond) → l'option incorrecte ne rapporte rien, mais les correctes cochées rapportent quand même leurs points ; jamais marqué "Correcte" (ensemble pas exact)

### 15.4 Non-régression
- [ ] Les autres types de manches ne sont pas affectés (le QCM n'a pas de buzzer/retry/validation manuelle — vérifier qu'aucune de ces options n'apparaît dans son éditeur, contrairement à qa-text)

### 15.5 Vue GM (host-live)
- [ ] "Question en cours" liste chaque option avec sa correction (✓/✕) et ses points si correcte et mode personnalisé

---

## 16. Outils host — tirage aléatoire et sondage (lot 3 — pas testé manuellement, à valider en priorité)

Aucun test manuel fait à ce stade — uniquement `dotnet build`/`npx ng build` propres. Nouvelles tables
(`RandomDrawStates`, `RandomDrawGuesses`, `StrawPollStates`, `StrawPollVotes`), première vraie migration
EF depuis order-list — vérifier qu'elle s'est bien appliquée (`dotnet ef database update` ou démarrage en
Development, qui migre automatiquement).

### 16.1 Indépendance vis-à-vis de l'état de la partie
- [ ] Déclencher un tirage aléatoire ou un sondage pendant le lobby, entre deux questions, pendant une pause — fonctionne dans tous les cas, sans perturber la question/manche en cours
- [ ] Un seul outil actif à la fois : tenter de lancer un second outil (tirage ou sondage) pendant qu'un premier est actif → refusé, message clair côté host

### 16.2 Tirage aléatoire — mode Reveal
- [ ] Lancer un tirage en mode "Affichage direct" → la valeur est tirée et affichée immédiatement à tous les joueurs concernés (et à l'hôte), aucune phase de devinette
- [ ] "Qui est concerné" via le sélecteur de participants (joueurs ou équipes) → seuls les joueurs concernés voient l'overlay côté joueur ; les autres ne voient rien

### 16.3 Tirage aléatoire — modes Guess
- [ ] Mode "Deviner — un gagnant" avec 2+ joueurs concernés : chacun envoie une devinette dans les bornes Min/Max, l'hôte voit le décompte de devinettes reçues (pas les valeurs) avant de révéler
- [ ] Cliquer "Révéler" → tire la valeur, calcule le classement par proximité, le joueur le plus proche est marqué gagnant (`isWinner`)
- [ ] Égalité entre 2 devinettes équidistantes → les deux partagent le même rang (classement "olympique"), toutes deux marquées gagnantes si rang 0
- [ ] Mode "Deviner — classement complet" : mêmes mécanismes, mais tous les participants sont classés (pas seulement un gagnant)
- [ ] Une devinette hors des bornes Min/Max est rejetée côté serveur
- [ ] Un joueur non concerné qui tente d'envoyer une devinette est rejeté (403)

### 16.4 Sondage (strawpoll)
- [ ] Créer un sondage à vote unique (2+ options) → chaque joueur concerné ne peut cocher qu'une seule option ; en cochant une autre, la précédente se décoche
- [ ] Créer un sondage à votes multiples → un joueur peut cocher plusieurs options
- [ ] Avant révélation : ni les joueurs ni l'hôte ne voient le décompte (seulement qui a déjà voté)
- [ ] Cliquer "Révéler les résultats" (hôte) → le décompte par option apparaît pour tout le monde
- [ ] Un joueur peut revoter (change d'avis) tant que les résultats ne sont pas révélés — son vote précédent est remplacé, pas cumulé
- [ ] Voter à nouveau après la révélation des résultats est rejeté

### 16.5 Fermeture et non-régression
- [ ] "Fermer" (hôte) sur un outil actif → il disparaît immédiatement côté host ET côté joueurs (overlay se ferme), un nouvel outil peut être lancé
- [ ] Le déroulement normal d'une manche (qa-text, order-list, etc.) n'est pas affecté par la présence de ces nouvelles tables/DTOs

---

## Environnements à couvrir

- [ ] Un passage complet en **local** (`dotnet run` + `ng serve`) avant de considérer que c'est bon
- [ ] Un passage sur le **serveur de prod** (`quizz.eynwa.fr`) après déploiement — n'oublie pas `dotnet ef database update` sur le serveur avant de démarrer le service, sinon les nouvelles tables n'existeront pas
