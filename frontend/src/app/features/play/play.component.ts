import { Component, OnDestroy, OnInit, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { GameSignalrService } from '../../core/services/game-signalr.service';
import { MediaService } from '../../core/services/media.service';
import { SessionService } from '../../core/services/session.service';
import {
  BlindTestPublicPayload,
  GameSessionState,
  ImageGuessPublicPayload,
  JoinSessionResponse,
  PlayerQuestion,
  QaPublicPayload,
  SubmitAnswerResponse,
  ZoomPublicPayload
} from '../../models/session.model';
import { AudioPlayerComponent } from '../../shared/components/audio-player/audio-player.component';
import { UiCardComponent } from '../../shared/components/ui-card/ui-card.component';
import { ZoomViewerComponent } from '../../shared/components/zoom-viewer/zoom-viewer.component';

const POLL_INTERVAL_MS = 800;

@Component({
  selector: 'app-play',
  imports: [FormsModule, UiCardComponent, ZoomViewerComponent, AudioPlayerComponent],
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
    this.pollHandle = setInterval(() => this.refreshState(), POLL_INTERVAL_MS);
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
        this.question.set(question);
      },
      error: () => this.question.set(null)
    });
  }
}
