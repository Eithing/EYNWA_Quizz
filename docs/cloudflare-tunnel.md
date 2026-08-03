# Cloudflare Tunnel — notes d'infra (serveur eynwa.fr)

Notes de configuration pour l'exposition internet des services auto-hébergés sur le serveur
Windows 11 Pro personnel, derrière la Livebox Orange. À relire (ou donner telle quelle à un
assistant IA) avant toute nouvelle config Cloudflare sur ce serveur.

## Contexte général

- Domaine `eynwa.fr` géré sur **Cloudflare, plan Free**.
- Serveur : Windows 11 Pro, machine perso, réseau local derrière une Livebox Orange.
- Choix d'architecture : **Cloudflare Tunnel** (`cloudflared`) plutôt que redirection de port
  classique — objectif : **zéro port entrant ouvert sur la box/le pare-feu** pour les services web.
  Décision prise après avoir hésité avec une alternative "Nginx Proxy Manager + port-forward
  443/80" (voir `npm.eynwa.fr` ci-dessous, créé à l'époque de cette hésitation).
- Le compte utilisateur Windows sur le serveur est `Eithing` → toute la config cloudflared vit
  dans `C:\Users\Eithing\.cloudflared\`.

## État des sous-domaines (zone DNS `eynwa.fr`)

| Sous-domaine | Type | État proxy | Usage | À toucher ? |
|---|---|---|---|---|
| `quizz.eynwa.fr` | CNAME (via tunnel) | Proxied | App Quiz Party (ce repo) | Géré par le tunnel `quizparty`, voir plus bas |
| `eynwa.fr` (racine) | A | Proxied | Pas encore utilisé | Ne pas toucher sans raison précise |
| `npm.eynwa.fr` | A | Proxied | Nginx Proxy Manager — **créé mais jamais déployé**, piste abandonnée au profit du tunnel | Laisser tel quel, ou nettoyer un jour si vraiment inutile |
| `portainer.eynwa.fr` | A | Proxied | Portainer (gestion Docker) — **pas encore déployé** | À migrer vers le tunnel le jour où Portainer tourne (voir procédure plus bas) |
| `cloud.eynwa.fr` | A | Proxied | Futur projet, non défini | Ne pas toucher |
| `play.eynwa.fr` | A | **DNS uniquement** (pas de proxy) | Serveurs de jeu (Satisfactory, Minecraft) — trafic **UDP**, non proxifiable par Cloudflare Free | **Ne jamais passer en "Proxied"**, ne jamais router vers le tunnel |
| `autoconfig` / `autodiscover` | CNAME → `mailconfig.ovh.net` | DNS uniquement | Config mail automatique (OVH) | Ne pas toucher |
| `ftp.eynwa.fr` | CNAME → `eynwa.fr` | DNS uniquement | Historique, usage incertain, un warning Cloudflare dessus | À vérifier un jour, pas urgent |
| MX / SRV `eynwa.fr` | — | DNS uniquement | Boîtes mail OVH | Ne pas toucher |

Rien à voir avec le tunnel : redirection de port sur la Livebox **`54321 (externe) → 3389 (interne, RDP)`**
pour l'accès bureau à distance au serveur. Indépendante de toute cette config, ne pas y toucher
en manipulant Cloudflare.

## Le tunnel `quizparty`

- Nom du tunnel : `quizparty`
- ID du tunnel (= nom du fichier de credentials) : `232f16f6-c28b-4afe-b051-a4ba3d7ad1a3`
- Fichier de credentials : `C:\Users\Eithing\.cloudflared\232f16f6-c28b-4afe-b051-a4ba3d7ad1a3.json`
- Config : `C:\Users\Eithing\.cloudflared\config.yml`

```yaml
tunnel: quizparty
credentials-file: C:\Users\Eithing\.cloudflared\232f16f6-c28b-4afe-b051-a4ba3d7ad1a3.json

ingress:
  - hostname: quizz.eynwa.fr
    service: http://localhost:5100
  - service: http_status:404
```

Un seul `cloudflared` peut gérer plusieurs hostnames : ajouter une entrée `hostname:`/`service:`
dans la liste `ingress` (avant la ligne `http_status:404`, qui doit toujours rester en dernier)
route un sous-domaine de plus vers un port local différent, **sans jamais toucher à la box**.

## ⚠️ Piège connu : ne pas utiliser `cloudflared service install`

Le wrapper de service Windows natif de `cloudflared` (`cloudflared service install` /
`cloudflared service uninstall`) a un bug reproductible sur la version installée en août 2026 :
le service démarre normalement, mais **ignore les signaux d'arrêt** (`Stop-Service` reste
bloqué indéfiniment sur "Attente de l'arrêt du service..."). Confirmé par
`sc.exe queryex Cloudflared` → flags `NOT_STOPPABLE, NOT_PAUSABLE, IGNORES_SHUTDOWN`.
Reproduit deux fois de suite après réinstallation complète (pas un problème de résidu/état
corrompu, c'est le binaire lui-même).

**Solution retenue : NSSM** à la place du wrapper natif. NSSM gère l'arrêt/le kill forcé de
façon beaucoup plus fiable.

```powershell
winget install nssm.nssm   # si winget ne trouve pas le package, télécharger sur nssm.cc (win64)

nssm install CloudflaredTunnel "C:\Program Files (x86)\cloudflared\cloudflared.exe"
nssm set CloudflaredTunnel AppParameters "tunnel --config C:\Users\Eithing\.cloudflared\config.yml run quizparty"
nssm set CloudflaredTunnel AppDirectory "C:\Users\Eithing\.cloudflared"
nssm set CloudflaredTunnel Start SERVICE_AUTO_START
nssm start CloudflaredTunnel
```

Le service Windows s'appelle **`CloudflaredTunnel`** (pas `Cloudflared` — ce nom-là était celui
du wrapper natif abandonné, à ne pas recréer).

Commandes utiles :
```powershell
Get-Service CloudflaredTunnel
nssm stop CloudflaredTunnel
nssm start CloudflaredTunnel
nssm restart CloudflaredTunnel   # après modif de config.yml
```

## Ajouter un nouveau sous-domaine au tunnel (ex. quand Portainer sera déployé)

1. Si le sous-domaine a déjà un A record "Proxied" pointant vers l'IP publique (cas de
   `portainer.eynwa.fr` et `npm.eynwa.fr` actuellement) : **le supprimer d'abord** dans le
   dashboard Cloudflare (DNS → poubelle sur la ligne concernée). Le tunnel doit être seul
   propriétaire du record.
2. Ajouter une entrée dans `ingress:` de `config.yml` (avant le `http_status:404` final) :
   ```yaml
     - hostname: portainer.eynwa.fr
       service: http://localhost:9000
   ```
3. Créer la route DNS :
   ```powershell
   cloudflared tunnel route dns quizparty portainer.eynwa.fr
   ```
4. Redémarrer le service pour recharger la config :
   ```powershell
   nssm restart CloudflaredTunnel
   ```

## Comment lire les erreurs Cloudflare pendant le debug

| Erreur vue dans le navigateur | Signification | Où regarder |
|---|---|---|
| **1033** | Le tunnel n'est pas connecté à l'edge Cloudflare — `cloudflared`/`CloudflaredTunnel` ne tourne pas ou plante | `Get-Service CloudflaredTunnel`, puis logs (voir plus bas) |
| **502 Bad Gateway** | Le tunnel est bien connecté, mais rien n'écoute sur le port local visé (`service:` dans `config.yml`) | Vérifier que le service applicatif (ex. backend .NET) tourne sur ce port |

NSSM écrit les erreurs de démarrage dans l'Observateur d'événements Windows comme le faisait le
wrapper natif :
```powershell
Get-EventLog -LogName Application -Source CloudflaredTunnel -Newest 20
```

## Historique — pourquoi `npm.eynwa.fr` et `portainer.eynwa.fr` existent déjà

Sous-domaines créés lors d'une réflexion antérieure sur l'architecture ("un hôte Docker unique
avec Nginx Proxy Manager en point d'entrée web, routant par nom d'hôte vers chaque conteneur").
Piste abandonnée au profit du Cloudflare Tunnel (plus simple, zéro port ouvert, pas de
dépendance à un reverse proxy supplémentaire). Les enregistrements DNS existent mais aucun des
deux services n'est déployé pour l'instant — à réutiliser ou nettoyer selon la suite du projet
Docker (voir le guide de déploiement principal, section Docker).
