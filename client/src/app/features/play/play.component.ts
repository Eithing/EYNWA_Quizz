import { Component, OnDestroy, OnInit, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MediaService } from '../../core/services/media.service';
import { SessionService } from '../../core/services/session.service';
import { SignalrService } from '../../core/services/signalr.service';
import { JoinSessionResponse, PlayerStep, SessionState, SubmitAnswerResponse } from '../../models/session.model';
import { UiCardComponent } from '../../shared/components/ui-card/ui-card.component';
import { PlayerConfigView, parsePlayerConfig } from './models/player-config-view.model';

@Component({
  selector: 'app-play',
  imports: [FormsModule, UiCardComponent],
  templateUrl: './play.component.html',
  styleUrl: './play.component.scss'
})
export class PlayComponent implements OnInit, OnDestroy {
  private code!: string;
  private playerInfo!: JoinSessionResponse;

  protected readonly state = signal<SessionState | null>(null);
  protected readonly step = signal<PlayerStep | null>(null);
  protected readonly answer = signal('');
  protected readonly result = signal<SubmitAnswerResponse | null>(null);
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly config = computed<PlayerConfigView>(() => {
    const step = this.step();
    return step ? parsePlayerConfig(step.configJson) : {};
  });

  protected readonly myScore = computed(
    () => this.state()?.players.find((p) => p.id === this.playerInfo?.playerId)?.score ?? 0
  );

  constructor(
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly sessionService: SessionService,
    private readonly signalrService: SignalrService,
    protected readonly mediaService: MediaService
  ) {}

  ngOnInit(): void {
    this.code = this.route.snapshot.paramMap.get('code')!;

    const stored = localStorage.getItem(`eynwa_quizz_player_${this.code}`);
    if (!stored) {
      this.router.navigate(['/join', this.code]);
      return;
    }
    this.playerInfo = JSON.parse(stored);

    this.sessionService.getPublicState(this.code).subscribe(async (state) => {
      this.state.set(state);
      this.refreshStep();

      await this.signalrService.connect(this.code);
      this.signalrService.onStepChanged((updated) => {
        this.state.set(updated);
        this.answer.set('');
        this.result.set(null);
        this.refreshStep();
      });
      this.signalrService.onScoreUpdated((player) => {
        this.state.update((s) =>
          s ? { ...s, players: s.players.map((p) => (p.id === player.id ? player : p)) } : s
        );
      });
    });
  }

  ngOnDestroy(): void {
    this.signalrService.disconnect();
  }

  protected submitAnswer(): void {
    if (!this.answer().trim() || this.submitting()) {
      return;
    }

    this.submitting.set(true);
    this.error.set(null);

    this.sessionService.submitAnswer(this.code, this.playerInfo.clientToken, this.answer()).subscribe({
      next: (result) => {
        this.submitting.set(false);
        this.result.set(result);
        this.step.update((s) => (s ? { ...s, hasAnswered: true } : s));
      },
      error: () => {
        this.submitting.set(false);
        this.error.set("Échec de l'envoi de la réponse.");
      }
    });
  }

  private refreshStep(): void {
    const state = this.state();
    if (!state || state.currentStepIndex < 0 || state.currentStepIndex >= state.stepCount) {
      this.step.set(null);
      return;
    }

    this.sessionService.getCurrentStepForPlayer(this.code, this.playerInfo.clientToken).subscribe((step) => {
      this.step.set(step);
    });
  }
}
