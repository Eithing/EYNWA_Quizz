import { Component, OnInit, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { QuizService } from '../../core/services/quiz.service';
import { SessionService } from '../../core/services/session.service';
import { FeatureMeta } from '../../models/feature.model';
import { SaveQuizRequest } from '../../models/quiz.model';
import { UiCardComponent } from '../../shared/components/ui-card/ui-card.component';
import { FeaturePickerComponent } from './components/feature-picker/feature-picker.component';
import { RoundEditorComponent } from './components/round-editor/round-editor.component';
import { RoundListComponent } from './components/round-list/round-list.component';
import { RoundDraft, toRound, toRoundDraft } from './models/round-draft.model';

@Component({
  selector: 'app-quiz-editor',
  imports: [FormsModule, RouterLink, UiCardComponent, RoundListComponent, FeaturePickerComponent, RoundEditorComponent],
  templateUrl: './quiz-editor.component.html',
  styleUrl: './quiz-editor.component.scss'
})
export class QuizEditorComponent implements OnInit {
  private quizId: number | null = null;

  protected readonly title = signal('');
  protected readonly description = signal('');
  protected readonly rounds = signal<RoundDraft[]>([]);
  protected readonly selectedClientId = signal<number | null>(null);
  protected readonly saving = signal(false);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly launching = signal(false);

  protected readonly selectedRound = computed(
    () => this.rounds().find((r) => r.clientId === this.selectedClientId()) ?? null
  );

  protected get canLaunchSession(): boolean {
    return this.quizId !== null && this.rounds().length > 0;
  }

  constructor(
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly quizService: QuizService,
    private readonly sessionService: SessionService
  ) {}

  ngOnInit(): void {
    const idParam = this.route.snapshot.paramMap.get('id');

    if (!idParam || idParam === 'new') {
      this.loading.set(false);
      return;
    }

    this.quizId = Number(idParam);
    this.quizService.getById(this.quizId).subscribe({
      next: (quiz) => {
        this.title.set(quiz.title);
        this.description.set(quiz.description ?? '');
        this.rounds.set(quiz.rounds.map(toRoundDraft));
        this.loading.set(false);
      },
      error: () => {
        this.error.set("Ce quiz n'existe pas ou ne t'appartient pas.");
        this.loading.set(false);
      }
    });
  }

  protected addRound(feature: FeatureMeta): void {
    const draft = toRoundDraft({
      order: this.rounds().length,
      featureTypeKey: feature.typeKey,
      title: `Nouvelle manche — ${feature.displayName}`,
      configJson: '{}',
      requiresTargetPlayer: false,
      questions: []
    });
    this.rounds.update((rounds) => [...rounds, draft]);
    this.selectedClientId.set(draft.clientId);
  }

  protected selectRound(clientId: number): void {
    this.selectedClientId.set(clientId);
  }

  protected onRoundChange(updated: RoundDraft): void {
    this.rounds.update((rounds) => rounds.map((r) => (r.clientId === updated.clientId ? updated : r)));
  }

  protected moveRoundUp(clientId: number): void {
    this.swapRounds(clientId, -1);
  }

  protected moveRoundDown(clientId: number): void {
    this.swapRounds(clientId, 1);
  }

  private swapRounds(clientId: number, offset: number): void {
    this.rounds.update((rounds) => {
      const index = rounds.findIndex((r) => r.clientId === clientId);
      const targetIndex = index + offset;
      if (index === -1 || targetIndex < 0 || targetIndex >= rounds.length) {
        return rounds;
      }
      const reordered = [...rounds];
      [reordered[index], reordered[targetIndex]] = [reordered[targetIndex], reordered[index]];
      return reordered.map((r, i) => ({ ...r, order: i }));
    });
  }

  protected removeRound(clientId: number): void {
    this.rounds.update((rounds) =>
      rounds.filter((r) => r.clientId !== clientId).map((r, i) => ({ ...r, order: i }))
    );
    if (this.selectedClientId() === clientId) {
      this.selectedClientId.set(null);
    }
  }

  protected save(): void {
    if (!this.title().trim()) {
      this.error.set('Le titre du quiz est requis.');
      return;
    }

    this.saving.set(true);
    this.error.set(null);

    const request: SaveQuizRequest = {
      title: this.title(),
      description: this.description() || null,
      rounds: this.rounds().map(toRound)
    };

    const save$ = this.quizId ? this.quizService.update(this.quizId, request) : this.quizService.create(request);

    save$.subscribe({
      next: (quiz) => {
        this.saving.set(false);
        this.quizId = quiz.id;
        this.router.navigate(['/quizzes', quiz.id]);
      },
      error: () => {
        this.saving.set(false);
        this.error.set("Échec de l'enregistrement du quiz.");
      }
    });
  }

  protected launchSession(): void {
    if (!this.quizId) {
      return;
    }

    this.launching.set(true);
    this.sessionService.start(this.quizId).subscribe({
      next: (session) => {
        this.launching.set(false);
        this.router.navigate(['/host', session.sessionId]);
      },
      error: () => {
        this.launching.set(false);
        this.error.set('Échec du lancement de la session.');
      }
    });
  }
}
