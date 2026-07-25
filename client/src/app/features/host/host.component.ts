import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { SessionService } from '../../core/services/session.service';
import { SignalrService } from '../../core/services/signalr.service';
import { PlayerStep, SessionState } from '../../models/session.model';
import { UiCardComponent } from '../../shared/components/ui-card/ui-card.component';

@Component({
  selector: 'app-host',
  imports: [UiCardComponent],
  templateUrl: './host.component.html',
  styleUrl: './host.component.scss'
})
export class HostComponent implements OnInit, OnDestroy {
  private sessionId!: number;

  protected readonly state = signal<SessionState | null>(null);
  protected readonly currentStep = signal<PlayerStep | null>(null);
  protected readonly copied = signal(false);

  constructor(
    private readonly route: ActivatedRoute,
    private readonly sessionService: SessionService,
    private readonly signalrService: SignalrService
  ) {}

  ngOnInit(): void {
    this.sessionId = Number(this.route.snapshot.paramMap.get('sessionId'));

    this.sessionService.getStateAsGm(this.sessionId).subscribe(async (state) => {
      this.state.set(state);
      this.refreshCurrentStep();

      await this.signalrService.connect(state.inviteCode);
      this.signalrService.onStepChanged((updated) => {
        this.state.set(updated);
        this.refreshCurrentStep();
      });
      this.signalrService.onPlayerJoined((player) => {
        this.state.update((s) => (s ? { ...s, players: [...s.players, player] } : s));
      });
      this.signalrService.onScoreUpdated((player) => {
        this.state.update((s) =>
          s
            ? {
                ...s,
                players: s.players
                  .map((p) => (p.id === player.id ? player : p))
                  .sort((a, b) => b.score - a.score)
              }
            : s
        );
      });
    });
  }

  ngOnDestroy(): void {
    this.signalrService.disconnect();
  }

  protected get inviteUrl(): string {
    const code = this.state()?.inviteCode;
    return code ? `${window.location.origin}/join/${code}` : '';
  }

  protected nextStep(): void {
    this.sessionService.nextStep(this.sessionId).subscribe((state) => {
      this.state.set(state);
      this.refreshCurrentStep();
    });
  }

  protected copyInviteLink(): void {
    navigator.clipboard.writeText(this.inviteUrl).then(() => {
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    });
  }

  private refreshCurrentStep(): void {
    const state = this.state();
    if (!state || state.currentStepIndex < 0 || state.currentStepIndex >= state.stepCount) {
      this.currentStep.set(null);
      return;
    }

    this.sessionService.getCurrentStepFull(this.sessionId).subscribe((step) => this.currentStep.set(step));
  }
}
