import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { SessionService } from '../../core/services/session.service';
import { SessionState } from '../../models/session.model';
import { UiCardComponent } from '../../shared/components/ui-card/ui-card.component';

@Component({
  selector: 'app-join',
  imports: [FormsModule, UiCardComponent],
  templateUrl: './join.component.html',
  styleUrl: './join.component.scss'
})
export class JoinComponent implements OnInit {
  private code!: string;

  protected readonly quizState = signal<SessionState | null>(null);
  protected readonly name = signal('');
  protected readonly error = signal<string | null>(null);
  protected readonly joining = signal(false);

  constructor(
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly sessionService: SessionService
  ) {}

  ngOnInit(): void {
    this.code = this.route.snapshot.paramMap.get('code')!;

    this.sessionService.getPublicState(this.code).subscribe({
      next: (state) => this.quizState.set(state),
      error: () => this.error.set("Cette session n'existe pas ou n'a pas encore été lancée par l'hôte.")
    });
  }

  protected submit(): void {
    if (!this.name().trim()) {
      return;
    }

    this.joining.set(true);
    this.error.set(null);

    this.sessionService.join(this.code, this.name().trim()).subscribe({
      next: (response) => {
        localStorage.setItem(`eynwa_quizz_player_${this.code}`, JSON.stringify(response));
        this.router.navigate(['/play', this.code]);
      },
      error: () => {
        this.joining.set(false);
        this.error.set("Impossible de rejoindre la session.");
      }
    });
  }
}
