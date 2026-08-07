<#
.SYNOPSIS
    Déploiement one-shot de QuizParty en prod (quizz.eynwa.fr).

.DESCRIPTION
    À lancer sur le serveur de prod (RDP), depuis n'importe quel dossier, avec des droits
    administrateur (nécessaires pour Stop-Service/Start-Service). Construit le frontend et le
    backend AVANT de toucher au service en cours pour minimiser la coupure, puis : arrête le
    service, sauvegarde la base, applique les migrations EF, copie les nouveaux binaires,
    redémarre le service.

    Le script se repère par rapport à sa propre position (backend/ et frontend/ sont ses
    dossiers voisins) : il n'a pas besoin d'être lancé depuis un chemin particulier, mais le
    repo doit être cloné en entier avec ce script à sa place d'origine (deploy/deploy.ps1).

.PARAMETER ServiceName
    Nom du service Windows du backend (natif, pas NSSM).

.PARAMETER AppDir
    Dossier où tourne l'app publiée (binaire + wwwroot).

.PARAMETER DbPath
    Chemin absolu du fichier SQLite de prod.

.PARAMETER SkipGitPull
    Ne pas faire git pull (utile si tu as déjà mis à jour le checkout toi-même).

.EXAMPLE
    .\deploy.ps1
    Déploiement standard avec les valeurs par défaut du serveur eynwa.fr.
#>

[CmdletBinding()]
param(
    [string]$ServiceName = "QuizPartyApi",
    [string]$AppDir = "C:\quizparty\app",
    [string]$DbPath = "C:\quizparty\data\quizparty.db",
    [switch]$SkipGitPull
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$backendDir = Join-Path $repoRoot "backend"
$frontendDir = Join-Path $repoRoot "frontend"
$wwwrootDir = Join-Path $backendDir "wwwroot"
$publishTmpDir = Join-Path $repoRoot "deploy\.publish-tmp"

$logDir = Join-Path $repoRoot "deploy\logs"
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$logFile = Join-Path $logDir "deploy-$(Get-Date -Format yyyyMMdd-HHmmss).log"
Start-Transcript -Path $logFile | Out-Null

function Write-Step($message) {
    Write-Host ""
    Write-Host "==> $message" -ForegroundColor Cyan
}

function Restart-ServiceOrWarn {
    try {
        Start-Service -Name $ServiceName
        Write-Host "Service $ServiceName redémarré." -ForegroundColor Green
    } catch {
        Write-Host "ÉCHEC du redémarrage de $ServiceName : $_" -ForegroundColor Red
        Write-Host "Vérifie manuellement : Get-Service $ServiceName / Get-EventLog -LogName Application -Source $ServiceName -Newest 20" -ForegroundColor Red
    }
}

try {
    if (-not $SkipGitPull) {
        Write-Step "Mise à jour du code (git pull)"
        Push-Location $repoRoot

        # Bruit connu et sans intérêt : l'Angular CLI s'auto-ajoute un ID analytics dans
        # angular.json au premier `ng` lancé sur une machine — jamais un vrai changement voulu.
        $status = git status --porcelain
        $unexpected = $status | Where-Object { $_ -notmatch "frontend[\\/]angular\.json$" }
        if ($unexpected) {
            Pop-Location
            throw "Modifications locales inattendues dans le repo, arrêt avant d'écraser quoi que ce soit :`n$($unexpected -join "`n")`nVérifie/committe/stash à la main avant de relancer."
        }
        if ($status) {
            git checkout -- frontend/angular.json
        }

        git pull
        Pop-Location
    }

    Write-Step "Build frontend (production)"
    Push-Location $frontendDir
    npm ci
    npx ng build
    Pop-Location

    Write-Step "Copie du build Angular dans wwwroot"
    Remove-Item $wwwrootDir -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $frontendDir "dist\frontend\browser") -Destination $wwwrootDir -Recurse

    Write-Step "Publish backend"
    Remove-Item $publishTmpDir -Recurse -Force -ErrorAction SilentlyContinue
    Push-Location $backendDir
    dotnet publish -c Release -o $publishTmpDir
    Pop-Location

    # Le build a réussi : à partir d'ici on touche au service en prod, la coupure commence.
    Write-Step "Arrêt du service $ServiceName"
    Stop-Service -Name $ServiceName

    Write-Step "Sauvegarde de la base"
    $backupPath = "$DbPath.bak-$(Get-Date -Format yyyyMMdd-HHmmss)"
    Copy-Item $DbPath $backupPath
    Write-Host "Sauvegarde : $backupPath"

    Write-Step "Application des migrations EF Core"
    Push-Location $backendDir
    dotnet ef database update --connection "Data Source=$DbPath"
    Pop-Location

    Write-Step "Copie des nouveaux binaires vers $AppDir"
    # Copie non-destructive (pas de /MIR) : appsettings.Production.json (secrets/connection
    # string réels, absent du build) n'est jamais dans $publishTmpDir donc jamais supprimé ici.
    Copy-Item (Join-Path $publishTmpDir "*") -Destination $AppDir -Recurse -Force

    Write-Step "Redémarrage du service $ServiceName"
    Restart-ServiceOrWarn

    Write-Step "Nettoyage"
    Remove-Item $publishTmpDir -Recurse -Force -ErrorAction SilentlyContinue

    Write-Step "Terminé"
    Get-Service -Name $ServiceName
    Write-Host "Log complet : $logFile"
}
catch {
    Write-Host ""
    Write-Host "ÉCHEC DU DÉPLOIEMENT : $_" -ForegroundColor Red

    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc -and $svc.Status -ne "Running") {
        Write-Host "Le service est arrêté suite à l'échec — tentative de redémarrage avec les binaires existants (anciens ou déjà copiés selon l'étape atteinte)." -ForegroundColor Yellow
        Restart-ServiceOrWarn
    }

    Write-Host "Log complet : $logFile"
    Stop-Transcript | Out-Null
    exit 1
}

Stop-Transcript | Out-Null
