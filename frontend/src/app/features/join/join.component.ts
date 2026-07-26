import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { SessionService } from '../../core/services/session.service';
import { GameSessionState } from '../../models/session.model';
import { UiCardComponent } from '../../shared/components/ui-card/ui-card.component';

@Component({
  selector: 'app-join',
  imports: [FormsModule, UiCardComponent],
  templateUrl: './join.component.html',
  styleUrl: './join.component.scss'
})
export class JoinComponent implements OnInit {
  private token!: string;

  protected readonly sessionState = signal<GameSessionState | null>(null);
  protected readonly pseudo = signal('');
  protected readonly error = signal<string | null>(null);
  protected readonly joining = signal(false);

  constructor(
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly sessionService: SessionService
  ) {}

  ngOnInit(): void {
    this.token = this.route.snapshot.paramMap.get('token')!;

    this.sessionService.getPublicState(this.token).subscribe({
      next: (state) => this.sessionState.set(state),
      error: () => this.error.set("Cette session n'existe pas ou est introuvable.")
    });
  }

  protected submit(): void {
    if (!this.pseudo().trim()) {
      return;
    }

    this.joining.set(true);
    this.error.set(null);

    this.sessionService.join(this.token, this.pseudo().trim()).subscribe({
      next: (response) => {
        localStorage.setItem(`quizparty_player_${this.token}`, JSON.stringify(response));
        this.router.navigate(['/play', this.token]);
      },
      error: () => {
        this.joining.set(false);
        this.error.set('Impossible de rejoindre la session.');
      }
    });
  }
}
