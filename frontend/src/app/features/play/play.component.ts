import { Component, OnDestroy, OnInit, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { GameSignalrService } from '../../core/services/game-signalr.service';
import { MediaService } from '../../core/services/media.service';
import { SessionService } from '../../core/services/session.service';
import { GameSessionState, JoinSessionResponse, PlayerQuestion, SubmitAnswerResponse } from '../../models/session.model';
import { UiCardComponent } from '../../shared/components/ui-card/ui-card.component';
import { ZoomViewerComponent } from '../../shared/components/zoom-viewer/zoom-viewer.component';

const POLL_INTERVAL_MS = 800;

@Component({
  selector: 'app-play',
  imports: [FormsModule, UiCardComponent, ZoomViewerComponent],
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

  protected readonly myScore = computed(
    () => this.state()?.players.find((p) => p.id === this.playerInfo?.playerId)?.score ?? 0
  );

  protected readonly resolvedImageUrl = computed(() => {
    const question = this.question();
    return question ? this.mediaService.resolveUrl(question.imageUrl) : '';
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

  private refreshQuestion(): void {
    const state = this.state();
    if (!state || state.status !== 'Running') {
      this.question.set(null);
      return;
    }

    this.sessionService.getCurrentQuestionForPlayer(this.token, this.playerInfo.connectionToken).subscribe({
      next: (question) => this.question.set(question),
      error: () => this.question.set(null)
    });
  }
}
