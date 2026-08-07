import { CdkDrag, CdkDragDrop, CdkDragHandle, CdkDropList, moveItemInArray } from '@angular/cdk/drag-drop';
import { Component, OnDestroy, OnInit, computed, signal } from '@angular/core';
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
  JoinSessionResponse,
  OrderListItem,
  OrderListPublicPayload,
  PlayerQuestion,
  QaPublicPayload,
  SubmitAnswerResponse,
  ZoomPublicPayload
} from '../../models/session.model';
type PartnerGuessPublicPayload = QaPublicPayload;
import { AudioPlayerComponent } from '../../shared/components/audio-player/audio-player.component';
import { UiCardComponent } from '../../shared/components/ui-card/ui-card.component';
import { ZoomViewerComponent } from '../../shared/components/zoom-viewer/zoom-viewer.component';

const POLL_INTERVAL_MS = 800;

@Component({
  selector: 'app-play',
  imports: [FormsModule, UiCardComponent, ZoomViewerComponent, AudioPlayerComponent, CdkDropList, CdkDrag, CdkDragHandle],
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
  ) {}

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
        this.result.set(null);
        this.refreshQuestion();
      });
      this.signalrService.onScoreUpdated((player) => {
        this.state.update((s) =>
          s ? { ...s, players: s.players.map((p) => (p.id === player.id ? player : p)) } : s
        );
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
