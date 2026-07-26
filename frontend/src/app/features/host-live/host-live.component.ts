import { Component, OnDestroy, OnInit, computed, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { GameSignalrService } from '../../core/services/game-signalr.service';
import { MediaService } from '../../core/services/media.service';
import { SessionService } from '../../core/services/session.service';
import { CurrentQuestionAdmin, GameSessionState, PendingAnswer } from '../../models/session.model';
import { UiCardComponent } from '../../shared/components/ui-card/ui-card.component';

const POLL_INTERVAL_MS = 1000;

@Component({
  selector: 'app-host-live',
  imports: [UiCardComponent],
  templateUrl: './host-live.component.html',
  styleUrl: './host-live.component.scss'
})
export class HostLiveComponent implements OnInit, OnDestroy {
  private sessionId!: number;
  private pollHandle?: ReturnType<typeof setInterval>;

  protected readonly state = signal<GameSessionState | null>(null);
  protected readonly currentQuestion = signal<CurrentQuestionAdmin | null>(null);
  protected readonly pendingAnswers = signal<PendingAnswer[]>([]);
  protected readonly copied = signal(false);

  protected readonly currentImageUrl = computed(() => {
    const question = this.currentQuestion();
    if (!question || question.featureTypeKey !== 'zoom-image') {
      return null;
    }
    try {
      const imageUrl = JSON.parse(question.payloadJson).imageUrl as string;
      return imageUrl ? this.mediaService.resolveUrl(imageUrl) : null;
    } catch {
      return null;
    }
  });

  protected readonly currentQaPayload = computed(() => {
    const question = this.currentQuestion();
    if (!question || question.featureTypeKey !== 'qa-text') {
      return null;
    }
    try {
      return JSON.parse(question.payloadJson) as { questionText: string; acceptedAnswers: string[] };
    } catch {
      return null;
    }
  });

  constructor(
    private readonly route: ActivatedRoute,
    private readonly sessionService: SessionService,
    private readonly signalrService: GameSignalrService,
    private readonly mediaService: MediaService
  ) {}

  ngOnInit(): void {
    this.sessionId = Number(this.route.snapshot.paramMap.get('sessionId'));

    this.sessionService.getStateAsGm(this.sessionId).subscribe(async (state) => {
      this.state.set(state);
      this.refreshCurrentQuestion();
      this.refreshPendingAnswers();

      await this.signalrService.connect(state.inviteToken);
      this.signalrService.onStateChanged((updated) => {
        this.state.set(updated);
        this.refreshCurrentQuestion();
      });
      this.signalrService.onPlayerJoined((player) => {
        this.state.update((s) => (s ? { ...s, players: [...s.players, player] } : s));
      });
      this.signalrService.onScoreUpdated((player) => {
        this.state.update((s) =>
          s
            ? {
                ...s,
                players: s.players.map((p) => (p.id === player.id ? player : p)).sort((a, b) => b.score - a.score)
              }
            : s
        );
        this.refreshPendingAnswers();
      });
      this.signalrService.onAnswerPendingValidation(() => this.refreshPendingAnswers());
    });

    // Filet de sécurité indépendant de SignalR : si un message temps réel est manqué, l'écran
    // se remet à jour tout seul au prochain sondage (nouveaux joueurs, scores, question courante).
    this.pollHandle = setInterval(() => this.refreshState(), POLL_INTERVAL_MS);
  }

  ngOnDestroy(): void {
    clearInterval(this.pollHandle);
    this.signalrService.disconnect();
  }

  private refreshState(): void {
    this.sessionService.getStateAsGm(this.sessionId).subscribe((state) => {
      this.state.set(state);
      this.refreshCurrentQuestion();
      this.refreshPendingAnswers();
    });
  }

  protected get inviteUrl(): string {
    const token = this.state()?.inviteToken;
    return token ? `${window.location.origin}/join/${token}` : '';
  }

  protected copyInviteLink(): void {
    navigator.clipboard.writeText(this.inviteUrl).then(() => {
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    });
  }

  protected begin(): void {
    this.sessionService.begin(this.sessionId).subscribe((state) => this.applyState(state));
  }

  protected pause(): void {
    this.sessionService.pause(this.sessionId).subscribe((state) => this.applyState(state));
  }

  protected resume(): void {
    this.sessionService.resume(this.sessionId).subscribe((state) => this.applyState(state));
  }

  protected next(): void {
    this.sessionService.next(this.sessionId).subscribe((state) => this.applyState(state));
  }

  protected toggleScoreboard(): void {
    const current = this.state()?.scoreboardVisible ?? false;
    this.sessionService.setScoreboardVisible(this.sessionId, !current).subscribe((state) => this.applyState(state));
  }

  protected selectTargetPlayer(playerId: number): void {
    this.sessionService.setRoundTargetPlayer(this.sessionId, playerId).subscribe((state) => this.applyState(state));
  }

  protected resolveBuzz(isCorrect: boolean): void {
    this.sessionService.resolveBuzz(this.sessionId, isCorrect).subscribe((state) => this.applyState(state));
  }

  protected validateAnswer(answer: PendingAnswer, isCorrect: boolean): void {
    this.sessionService.validateAnswer(this.sessionId, answer.id, isCorrect).subscribe(() => {
      this.pendingAnswers.update((answers) => answers.filter((a) => a.id !== answer.id));
    });
  }

  private applyState(state: GameSessionState): void {
    this.state.set(state);
    this.refreshCurrentQuestion();
  }

  private refreshCurrentQuestion(): void {
    const state = this.state();
    if (!state || state.status !== 'Running') {
      this.currentQuestion.set(null);
      return;
    }

    this.sessionService.getCurrentQuestionFull(this.sessionId).subscribe({
      next: (question) => this.currentQuestion.set(question),
      error: () => this.currentQuestion.set(null)
    });
  }

  private refreshPendingAnswers(): void {
    this.sessionService.getPendingAnswers(this.sessionId).subscribe((answers) => this.pendingAnswers.set(answers));
  }
}
