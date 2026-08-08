import { CdkDrag, CdkDragDrop, CdkDragHandle, CdkDropList, moveItemInArray } from '@angular/cdk/drag-drop';
import { Component, OnDestroy, OnInit, computed, effect, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { GameSignalrService } from '../../core/services/game-signalr.service';
import { MediaService } from '../../core/services/media.service';
import { SessionService } from '../../core/services/session.service';
import {
  BlindTestPublicPayload,
  ClosestGuessPublicPayload,
  GameSessionState,
  ImageGuessPublicPayload,
  JokerType,
  JokerUsedEvent,
  JoinSessionResponse,
  OrderListItem,
  OrderListPublicPayload,
  PlayerQuestion,
  QaPublicPayload,
  QcmPublicPayload,
  SubmitAnswerResponse,
  ZoomPublicPayload
} from '../../models/session.model';
type PartnerGuessPublicPayload = QaPublicPayload;
import { AudioPlayerComponent } from '../../shared/components/audio-player/audio-player.component';
import { UiCardComponent } from '../../shared/components/ui-card/ui-card.component';
import { ZoomViewerComponent } from '../../shared/components/zoom-viewer/zoom-viewer.component';
import { CopyPastePickerComponent } from './components/copy-paste-picker/copy-paste-picker.component';
import { JokerTrayComponent } from './components/joker-tray/joker-tray.component';
import { RandomDrawOverlayComponent } from './components/random-draw-overlay/random-draw-overlay.component';
import { StrawPollOverlayComponent } from './components/strawpoll-overlay/strawpoll-overlay.component';

const POLL_INTERVAL_MS = 800;

function joinWithEt(values: number[]): string {
  if (values.length <= 1) {
    return values.join('');
  }
  if (values.length === 2) {
    return `${values[0]} et ${values[1]}`;
  }
  return `${values.slice(0, -1).join(', ')} et ${values[values.length - 1]}`;
}

@Component({
  selector: 'app-play',
  imports: [
    FormsModule,
    UiCardComponent,
    ZoomViewerComponent,
    AudioPlayerComponent,
    CdkDropList,
    CdkDrag,
    CdkDragHandle,
    JokerTrayComponent,
    CopyPastePickerComponent,
    RandomDrawOverlayComponent,
    StrawPollOverlayComponent
  ],
  templateUrl: './play.component.html',
  styleUrl: './play.component.scss'
})
export class PlayComponent implements OnInit, OnDestroy {
  private token!: string;
  private playerInfo!: JoinSessionResponse;
  private pollHandle?: ReturnType<typeof setInterval>;

  protected readonly state = signal<GameSessionState | null>(null);
  protected readonly question = signal<PlayerQuestion | null>(null);
  protected readonly answer = signal('');
  protected readonly result = signal<SubmitAnswerResponse | null>(null);
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly buzzing = signal(false);

  protected readonly myScore = computed(
    () => this.state()?.players.find((p) => p.id === this.playerInfo?.playerId)?.totalScore ?? 0
  );

  protected readonly hasBuzzer = computed(() => this.state()?.currentBuzzHolderPlayerId === this.playerInfo?.playerId);

  protected readonly jokerUsing = signal(false);
  protected readonly jokerError = signal<string | null>(null);
  protected readonly jokerToast = signal<JokerUsedEvent | null>(null);

  /** Jokers m'appartenant en propre, ou appartenant à mon équipe (stock partagé) — voir
   * FindUsableJokerGrantAsync côté backend, même règle de propriété reproduite ici pour l'affichage. */
  protected readonly myJokerGrants = computed(() => {
    const s = this.state();
    if (!s) {
      return [];
    }
    const myPlayerId = this.playerInfo?.playerId;
    const myTeamId = s.players.find((p) => p.id === myPlayerId)?.teamId ?? null;
    return s.jokerGrants.filter((g) => g.ownerPlayerId === myPlayerId || (myTeamId !== null && g.ownerTeamId === myTeamId));
  });

  /** Manches où une "bonne réponse immédiate" a un sens — même liste que SimultaneousAnswerFeatures côté
   * backend (Seul au monde / Copier-coller), duplicuée ici pour piloter l'affichage du tiroir. */
  private readonly simultaneousAnswerFeatures = ['qa-text', 'zoom-image', 'blind-test', 'image-guess', 'multiple-choice', 'order-list'];

  /** Seuls les jokers dont la mécanique est déjà câblée côté client apparaissent dans le tiroir — les
   * autres restent invisibles tant que leur lot n'est pas livré, même si le GM leur a donné des charges. */
  private readonly implementedJokerTypes: JokerType[] = ['FiftyFifty', 'MeFirst', 'AloneInTheWorld', 'CopyPaste', 'Exchange'];

  /// Thème en attente de lancement (statut ThemeReadyToLaunch) : le seul du plateau révélé mais encore
  /// non résolu.
  protected readonly readyToLaunchTheme = computed(() => {
    const s = this.state();
    return s?.themeBoard?.find((t) => t.isRevealed && t.resolution === 'Pending') ?? null;
  });

  protected readonly isThemeParticipant = computed(() => {
    const s = this.state();
    if (!s) {
      return false;
    }
    const myPlayerId = this.playerInfo?.playerId;
    const myTeamId = s.players.find((p) => p.id === myPlayerId)?.teamId ?? null;
    return (
      s.currentRoundParticipantPlayerIds.includes(myPlayerId) ||
      (myTeamId !== null && s.currentRoundParticipantTeamIds.includes(myTeamId))
    );
  });

  protected readonly usableJokers = computed(() => {
    const q = this.question();
    return this.myJokerGrants().filter((g) => {
      if (!this.implementedJokerTypes.includes(g.type)) {
        return false;
      }
      if (g.type === 'FiftyFifty') {
        return this.qcmPayload() !== null && q?.isAnswerWindowOpen === true && !q?.hasAnswered;
      }
      if (g.type === 'MeFirst') {
        return (
          q?.isBuzzerMode === true &&
          q?.isAnswerWindowOpen === true &&
          (this.state()?.meFirstQuestionsRemaining ?? 0) === 0
        );
      }
      if (g.type === 'AloneInTheWorld') {
        const s = this.state();
        return (
          !!q &&
          this.simultaneousAnswerFeatures.includes(q.featureTypeKey) &&
          q.isAnswerWindowOpen &&
          !q.hasAnswered &&
          s?.aloneInTheWorldPlayerId == null &&
          s?.aloneInTheWorldTeamId == null
        );
      }
      if (g.type === 'CopyPaste') {
        return !!q && this.simultaneousAnswerFeatures.includes(q.featureTypeKey) && q.isAnswerWindowOpen && !q.hasAnswered;
      }
      if (g.type === 'Exchange') {
        return this.state()?.status === 'ThemeReadyToLaunch' && !this.isThemeParticipant();
      }
      return false;
    });
  });

  /** Le verrou Moi d'abord est actif et je ne suis ni le détenteur ni un membre de son équipe — mon
   * bouton buzzer doit rester grisé tant que le détenteur n'a pas lui-même buzzé sur cette question. */
  protected readonly buzzerLockedByMeFirst = computed(() => {
    const s = this.state();
    if (!s || s.meFirstQuestionsRemaining === 0 || s.meFirstConsumedThisQuestion) {
      return false;
    }
    const myPlayerId = this.playerInfo?.playerId;
    const myTeamId = s.players.find((p) => p.id === myPlayerId)?.teamId ?? null;
    const isHolder = s.meFirstHolderPlayerId === myPlayerId || (myTeamId !== null && s.meFirstHolderTeamId === myTeamId);
    return !isHolder;
  });

  protected readonly isConcernedByRandomDraw = computed(() => {
    const draw = this.state()?.activeRandomDraw;
    if (!draw) {
      return false;
    }
    return draw.concernedPlayerIds.length === 0 || draw.concernedPlayerIds.includes(this.playerInfo?.playerId);
  });

  protected readonly hasSubmittedRandomDrawGuess = computed(() => {
    const draw = this.state()?.activeRandomDraw;
    return draw ? draw.submittedPlayerIds.includes(this.playerInfo?.playerId) : false;
  });

  protected readonly isConcernedByStrawPoll = computed(() => {
    const poll = this.state()?.activeStrawPoll;
    if (!poll) {
      return false;
    }
    return poll.concernedPlayerIds.length === 0 || poll.concernedPlayerIds.includes(this.playerInfo?.playerId);
  });

  protected readonly hasVotedStrawPoll = computed(() => {
    const poll = this.state()?.activeStrawPoll;
    return poll ? poll.votedPlayerIds.includes(this.playerInfo?.playerId) : false;
  });

  protected readonly randomDrawGuessSubmitting = signal(false);
  protected readonly randomDrawGuessError = signal<string | null>(null);

  protected readonly strawPollVoteSubmitting = signal(false);
  protected readonly strawPollVoteError = signal<string | null>(null);

  protected readonly participantNames = computed(() => {
    const s = this.state();
    if (!s) {
      return null;
    }
    if (s.currentRoundParticipantTeamIds.length > 0) {
      const names = s.teams.filter((t) => s.currentRoundParticipantTeamIds.includes(t.id)).map((t) => t.name);
      return names.length > 0 ? names.join(', ') : null;
    }
    if (s.currentRoundParticipantPlayerIds.length > 0) {
      const names = s.players.filter((p) => s.currentRoundParticipantPlayerIds.includes(p.id)).map((p) => p.pseudo);
      return names.length > 0 ? names.join(', ') : null;
    }
    return null;
  });

  protected readonly zoomPayload = computed<ZoomPublicPayload | null>(() => {
    const question = this.question();
    return question?.featureTypeKey === 'zoom-image' ? JSON.parse(question.publicPayloadJson) : null;
  });

  protected readonly qaPayload = computed<QaPublicPayload | null>(() => {
    const question = this.question();
    return question?.featureTypeKey === 'qa-text' ? JSON.parse(question.publicPayloadJson) : null;
  });

  protected readonly blindTestPayload = computed<BlindTestPublicPayload | null>(() => {
    const question = this.question();
    return question?.featureTypeKey === 'blind-test' ? JSON.parse(question.publicPayloadJson) : null;
  });

  protected readonly imageGuessPayload = computed<ImageGuessPublicPayload | null>(() => {
    const question = this.question();
    return question?.featureTypeKey === 'image-guess' ? JSON.parse(question.publicPayloadJson) : null;
  });

  /** Nombre de réponses distinctes attendues pour la question courante (qa-text/zoom-image/blind-test/
   * image-guess — et partner-guess phase 2, qui reste toujours à 1 puisque son payload est synthétisé
   * dynamiquement à partir de la réponse privée de l'answerer, jamais du réglage multi-réponses du GM). */
  protected readonly currentExpectedAnswerCount = computed(
    () =>
      this.zoomPayload()?.expectedAnswerCount ??
      this.qaPayload()?.expectedAnswerCount ??
      this.blindTestPayload()?.expectedAnswerCount ??
      this.imageGuessPayload()?.expectedAnswerCount ??
      1
  );

  protected readonly currentExpectedAnswerPoints = computed<number[] | null>(
    () =>
      this.zoomPayload()?.expectedAnswerPoints ??
      this.qaPayload()?.expectedAnswerPoints ??
      this.blindTestPayload()?.expectedAnswerPoints ??
      this.imageGuessPayload()?.expectedAnswerPoints ??
      null
  );

  /** Ex: "2 réponses attendues (1 et 2 points)" — affiché au-dessus du formulaire pour que le joueur
   * sache combien de champs remplir et ce que rapporte chacun avant de répondre. */
  protected readonly expectedAnswerPointsLabel = computed(() => {
    const points = this.currentExpectedAnswerPoints();
    if (!points || points.length === 0) {
      return null;
    }
    const noun = points.length > 1 ? 'réponses attendues' : 'réponse attendue';
    const pointWord = points.length === 1 && points[0] <= 1 ? 'point' : 'points';
    return `${points.length} ${noun} (${joinWithEt(points)} ${pointWord})`;
  });

  /** Un champ par réponse attendue pour qa-text/zoom-image/blind-test/image-guess (et le formulaire
   * générique de partner-guess phase 2, toujours à 1 champ — voir currentExpectedAnswerCount). */
  protected readonly answers = signal<string[]>(['']);

  protected readonly closestGuessPayload = computed<ClosestGuessPublicPayload | null>(() => {
    const question = this.question();
    return question?.featureTypeKey === 'closest-guess' ? JSON.parse(question.publicPayloadJson) : null;
  });

  protected readonly partnerGuessPayload = computed<PartnerGuessPublicPayload | null>(() => {
    const question = this.question();
    return question?.featureTypeKey === 'partner-guess' ? JSON.parse(question.publicPayloadJson) : null;
  });

  protected readonly orderListPayload = computed<OrderListPublicPayload | null>(() => {
    const question = this.question();
    return question?.featureTypeKey === 'order-list' ? JSON.parse(question.publicPayloadJson) : null;
  });

  /** Items dans l'ordre courant du groupe (soi-même, ou toute son équipe en mode équipe), tant que non
   * résolu — l'ordre vient de question().orderListCurrentOrder (IDs), le contenu de orderListPayload(). */
  protected readonly orderListCurrentItems = computed<OrderListItem[]>(() => {
    const q = this.question();
    const payload = this.orderListPayload();
    if (!q?.orderListCurrentOrder || !payload) {
      return [];
    }
    const byId = new Map(payload.items.map((it) => [it.id, it]));
    return q.orderListCurrentOrder.map((id) => byId.get(id)).filter((it): it is OrderListItem => !!it);
  });

  /** Une fois résolu : ordre correct, dans le même format que orderListCurrentItems. */
  protected readonly orderListCorrectItems = computed<OrderListItem[]>(() => {
    const q = this.question();
    const payload = this.orderListPayload();
    if (!q?.orderListCorrectOrder || !payload) {
      return [];
    }
    const byId = new Map(payload.items.map((it) => [it.id, it]));
    return q.orderListCorrectOrder.map((id) => byId.get(id)).filter((it): it is OrderListItem => !!it);
  });

  protected readonly qcmPayload = computed<QcmPublicPayload | null>(() => {
    const question = this.question();
    return question?.featureTypeKey === 'multiple-choice' ? JSON.parse(question.publicPayloadJson) : null;
  });

  /** IDs des options cochées pour la question QCM courante — remis à zéro à chaque nouvelle question
   * via l'effet du constructeur (comme answers() pour qa-text/zoom-image/...). */
  protected readonly qcmSelectedOptionIds = signal<string[]>([]);

  protected readonly qcmPointsLabel = computed(() => {
    const payload = this.qcmPayload();
    if (!payload || payload.correctOptionPoints.length === 0) {
      return null;
    }
    const points = payload.correctOptionPoints;
    const noun = payload.maxSelectable > 1 ? 'bonnes réponses' : 'bonne réponse';
    const pointWord = points.length === 1 && points[0] <= 1 ? 'point' : 'points';
    return `${payload.maxSelectable} ${noun} (${joinWithEt(points)} ${pointWord})`;
  });

  protected readonly orderListSubmitting = signal(false);
  protected readonly orderListError = signal<string | null>(null);
  /** Vrai pendant un glisser-déposer order-list en cours (entre cdkDragStarted et cdkDragEnded) : le
   * poll (800ms) et les échos StateChanged de notre propre action arrivent parfois EN PLEIN milieu du
   * geste — sans ce garde-fou, ils remplacent le tableau lié au drag pendant qu'il bouge encore, ce qui
   * fait "sauter"/rafraîchir visuellement l'item en cours de déplacement. */
  protected readonly orderListDragging = signal(false);

  protected readonly isPartnerGuessAnswerer = computed(
    () => this.state()?.currentAnswererPlayerId === this.playerInfo?.playerId
  );

  protected readonly resolvedImageUrl = computed(() => {
    const payload = this.zoomPayload();
    return payload ? this.mediaService.resolveUrl(payload.imageUrl) : '';
  });

  protected readonly resolvedAudioUrl = computed(() => {
    const payload = this.blindTestPayload();
    return payload ? this.mediaService.resolveUrl(payload.audioUrl) : '';
  });

  protected readonly resolvedImageGuessUrl = computed(() => {
    const payload = this.imageGuessPayload();
    return payload ? this.mediaService.resolveUrl(payload.imageUrl) : '';
  });

  constructor(
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly sessionService: SessionService,
    private readonly signalrService: GameSignalrService,
    private readonly mediaService: MediaService
  ) {
    // Redimensionne/vide les champs de réponse dès que le nombre de réponses attendues change (nouvelle
    // question) — onStateChanged/refreshQuestion ci-dessous remettent déjà answers() à [''] à chaque
    // nouvelle question, cet effet ajuste juste la longueur si la question suivante en attend plusieurs.
    effect(() => {
      const count = this.currentExpectedAnswerCount();
      if (this.answers().length !== count) {
        this.answers.set(Array.from({ length: count }, () => ''));
      }
    });
  }

  ngOnInit(): void {
    this.token = this.route.snapshot.paramMap.get('token')!;

    const stored = localStorage.getItem(`quizparty_player_${this.token}`);
    if (!stored) {
      this.router.navigate(['/join', this.token]);
      return;
    }
    this.playerInfo = JSON.parse(stored);

    this.sessionService.getPublicState(this.token).subscribe(async (state) => {
      this.state.set(state);
      this.refreshQuestion();

      await this.signalrService.connect(this.token);
      this.signalrService.onStateChanged((updated) => {
        // Pendant un glisser-déposer order-list actif, on ignore complètement les mises à jour externes
        // (poll ET SignalR) : le moindre this.question.set(...)/this.state.set(...) en plein milieu du
        // geste fait "sauter" l'affichage (CDK re-mesure ses éléments à chaque passage de détection de
        // changements). On rattrape tout au cdkDragEnded une fois le geste terminé.
        if (this.orderListDragging()) {
          return;
        }
        this.state.set(updated);
        this.answer.set('');
        this.answers.set(['']);
        this.qcmSelectedOptionIds.set([]);
        this.result.set(null);
        this.refreshQuestion();
      });
      this.signalrService.onScoreUpdated((player) => {
        this.state.update((s) =>
          s ? { ...s, players: s.players.map((p) => (p.id === player.id ? player : p)) } : s
        );
      });
      this.signalrService.onJokerUsed((event) => {
        this.jokerToast.set(event);
        setTimeout(() => this.jokerToast.set(null), 4000);
      });
    });

    // Filet de sécurité indépendant de SignalR : si un message temps réel est manqué (connexion
    // pas encore établie, coupure réseau…), l'écran se remet à jour tout seul au prochain sondage
    // au lieu de rester bloqué tant que le joueur ne rafraîchit pas la page à la main.
    // Suspendu pendant un glisser-déposer order-list actif (voir onOrderListDragStarted).
    this.pollHandle = setInterval(() => {
      if (!this.orderListDragging()) {
        this.refreshState();
      }
    }, POLL_INTERVAL_MS);
  }

  ngOnDestroy(): void {
    clearInterval(this.pollHandle);
    this.signalrService.disconnect();
  }

  private refreshState(): void {
    this.sessionService.getPublicState(this.token).subscribe((state) => {
      this.state.set(state);
      this.refreshQuestion();
    });
  }

  protected submitAnswer(): void {
    if (!this.answer().trim() || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.error.set(null);

    this.sessionService.submitAnswer(this.token, this.playerInfo.connectionToken, this.answer()).subscribe({
      next: (result) => {
        this.submitting.set(false);
        this.result.set(result);
        this.question.update((q) => (q ? { ...q, hasAnswered: true } : q));
      },
      error: (err) => {
        this.submitting.set(false);
        this.error.set(err.status === 409 ? 'Réponse déjà envoyée.' : "Échec de l'envoi de la réponse.");
      }
    });
  }

  protected onAnswerFieldChange(index: number, value: string): void {
    this.answers.update((values) => {
      const updated = [...values];
      updated[index] = value;
      return updated;
    });
  }

  /** qa-text/zoom-image/blind-test/image-guess (et partner-guess phase 2, toujours à 1 champ) : une seule
   * réponse attendue envoie le texte brut tel quel (comportement historique inchangé) ; plusieurs réponses
   * envoient un tableau JSON — le backend sait lequel attendre via expectedAnswerCount. */
  protected submitQaAnswer(): void {
    const values = this.answers();
    if (values.every((v) => !v.trim()) || this.submitting()) {
      return;
    }

    const rawAnswer = values.length > 1 ? JSON.stringify(values) : values[0];

    this.submitting.set(true);
    this.error.set(null);

    this.sessionService.submitAnswer(this.token, this.playerInfo.connectionToken, rawAnswer).subscribe({
      next: (result) => {
        this.submitting.set(false);
        this.result.set(result);
        this.question.update((q) => (q ? { ...q, hasAnswered: true } : q));
      },
      error: (err) => {
        this.submitting.set(false);
        this.error.set(err.status === 409 ? 'Réponse déjà envoyée.' : "Échec de l'envoi de la réponse.");
      }
    });
  }

  /** Coche/décoche une option QCM. Décocher est toujours permis ; cocher est refusé une fois le plafond
   * (maxSelectable = nombre de bonnes réponses) atteint — même règle appliquée côté serveur dans
   * QcmEngine.Evaluate, ceci n'est qu'un confort d'UI. */
  protected toggleQcmOption(optionId: string): void {
    const payload = this.qcmPayload();
    if (!payload) {
      return;
    }
    this.qcmSelectedOptionIds.update((selected) => {
      if (selected.includes(optionId)) {
        return selected.filter((id) => id !== optionId);
      }
      if (selected.length >= payload.maxSelectable) {
        return selected;
      }
      return [...selected, optionId];
    });
  }

  protected submitQcmAnswer(): void {
    const selected = this.qcmSelectedOptionIds();
    if (selected.length === 0 || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.error.set(null);

    this.sessionService.submitAnswer(this.token, this.playerInfo.connectionToken, JSON.stringify(selected)).subscribe({
      next: (result) => {
        this.submitting.set(false);
        this.result.set(result);
        this.question.update((q) => (q ? { ...q, hasAnswered: true } : q));
      },
      error: (err) => {
        this.submitting.set(false);
        this.error.set(err.status === 409 ? 'Réponse déjà envoyée.' : "Échec de l'envoi de la réponse.");
      }
    });
  }

  /// Copier/coller a besoin d'un joueur cible : un clic sur ce joker ouvre le sélecteur au lieu d'appeler
  /// useJoker() directement (contrairement aux autres jokers déjà câblés, sans cible).
  protected onJokerClicked(type: JokerType): void {
    if (type === 'CopyPaste') {
      this.openCopyPastePicker();
    } else {
      this.useJoker(type);
    }
  }

  protected useJoker(type: JokerType, targetPlayerId?: number): void {
    if (this.jokerUsing()) {
      return;
    }

    this.jokerUsing.set(true);
    this.jokerError.set(null);

    this.sessionService.useJoker(this.token, this.playerInfo.connectionToken, type, targetPlayerId).subscribe({
      next: (state) => {
        this.jokerUsing.set(false);
        this.copyPastePickerOpen.set(false);
        this.state.set(state);
        // Le payload public filtré (options masquées côté serveur pour Cinquante-cinquante) vit dans
        // PlayerQuestionDto, pas dans GameSessionStateDto — il faut le refetch séparément.
        this.refreshQuestion();
      },
      error: (err) => {
        this.jokerUsing.set(false);
        this.jokerError.set(err.error ?? "Échec de l'utilisation du joker.");
      }
    });
  }

  protected readonly copyPastePickerOpen = signal(false);

  protected readonly copyPasteTargets = computed(() => {
    const s = this.state();
    const myPlayerId = this.playerInfo?.playerId;
    return s ? s.players.filter((p) => p.id !== myPlayerId) : [];
  });

  protected openCopyPastePicker(): void {
    this.copyPastePickerOpen.set(true);
  }

  protected cancelCopyPastePicker(): void {
    this.copyPastePickerOpen.set(false);
  }

  protected submitRandomDrawGuess(value: number): void {
    if (this.randomDrawGuessSubmitting()) {
      return;
    }

    this.randomDrawGuessSubmitting.set(true);
    this.randomDrawGuessError.set(null);

    this.sessionService.submitRandomDrawGuess(this.token, this.playerInfo.connectionToken, value).subscribe({
      next: (state) => {
        this.randomDrawGuessSubmitting.set(false);
        this.state.set(state);
      },
      error: (err) => {
        this.randomDrawGuessSubmitting.set(false);
        this.randomDrawGuessError.set(err.error ?? "Échec de l'envoi de la devinette.");
      }
    });
  }

  protected submitStrawPollVote(selectedOptionIds: string[]): void {
    if (selectedOptionIds.length === 0 || this.strawPollVoteSubmitting()) {
      return;
    }

    this.strawPollVoteSubmitting.set(true);
    this.strawPollVoteError.set(null);

    this.sessionService.submitStrawPollVote(this.token, this.playerInfo.connectionToken, selectedOptionIds).subscribe({
      next: (state) => {
        this.strawPollVoteSubmitting.set(false);
        this.state.set(state);
      },
      error: (err) => {
        this.strawPollVoteSubmitting.set(false);
        this.strawPollVoteError.set(err.error ?? "Échec de l'envoi du vote.");
      }
    });
  }

  protected buzz(): void {
    if (this.buzzing()) {
      return;
    }

    this.buzzing.set(true);
    this.sessionService.buzz(this.token, this.playerInfo.connectionToken).subscribe({
      next: (state) => {
        this.buzzing.set(false);
        this.state.set(state);
      },
      error: () => {
        this.buzzing.set(false);
        this.refreshState();
      }
    });
  }

  protected onOrderListDragEnded(): void {
    // Ne PAS re-fetcher ici : cdkDropListDropped (juste après) fait sa propre mise à jour optimiste puis
    // persiste le nouvel ordre — un fetch immédiat ici arriverait avant que ce POST ne soit reçu par le
    // serveur et réafficherait brièvement l'ANCIEN ordre. Le poll normal (qui reprend puisque
    // orderListDragging repasse à false) suffit à rattraper un éventuel changement d'un coéquipier.
    this.orderListDragging.set(false);
  }

  protected onOrderListDrop(event: CdkDragDrop<OrderListItem[]>): void {
    if (event.previousIndex === event.currentIndex) {
      return;
    }

    const q = this.question();
    if (!q?.orderListCurrentOrder) {
      return;
    }

    const newOrder = [...q.orderListCurrentOrder];
    moveItemInArray(newOrder, event.previousIndex, event.currentIndex);
    // Mise à jour optimiste : l'affichage réagit immédiatement, le prochain poll/StateChanged confirmera
    // (ou corrigera si un coéquipier a bougé quelque chose entre-temps).
    this.question.set({ ...q, orderListCurrentOrder: newOrder });

    this.sessionService.submitOrderDraft(this.token, this.playerInfo.connectionToken, newOrder).subscribe({
      error: () => this.refreshQuestion()
    });
  }

  protected submitOrderFinal(): void {
    if (this.orderListSubmitting()) {
      return;
    }

    this.orderListSubmitting.set(true);
    this.orderListError.set(null);

    this.sessionService.submitOrderFinal(this.token, this.playerInfo.connectionToken).subscribe({
      next: () => {
        this.orderListSubmitting.set(false);
        this.refreshQuestion();
      },
      error: () => {
        this.orderListSubmitting.set(false);
        this.orderListError.set('Échec de la validation du classement.');
      }
    });
  }

  protected resolveOrderListMediaUrl(url: string): string {
    return this.mediaService.resolveUrl(url);
  }

  private refreshQuestion(): void {
    const state = this.state();
    if (!state || state.status !== 'Running') {
      this.question.set(null);
      return;
    }

    this.sessionService.getCurrentQuestionForPlayer(this.token, this.playerInfo.connectionToken).subscribe({
      next: (question) => {
        const previous = this.question();
        // Une nouvelle tentative vient de s'ouvrir (réponse fausse + retry autorisé) : on efface
        // l'ancien résultat/la saisie précédente pour laisser la place à un nouvel essai.
        if (previous?.questionId === question.questionId && previous.hasAnswered && !question.hasAnswered) {
          this.answer.set('');
          this.answers.set(['']);
          this.qcmSelectedOptionIds.set([]);
          this.result.set(null);
        }

        // Glisser-déposer order-list en cours : on ne laisse pas ce fetch (poll ou écho StateChanged de
        // notre propre action) remplacer l'ordre affiché pendant que l'utilisateur est encore en train de
        // le manipuler — voir le commentaire sur orderListDragging.
        if (this.orderListDragging() && previous?.orderListCurrentOrder && question.featureTypeKey === 'order-list') {
          question = { ...question, orderListCurrentOrder: previous.orderListCurrentOrder };
        }

        this.question.set(question);
      },
      error: () => this.question.set(null)
    });
  }
}
