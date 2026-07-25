import { Component, OnInit, computed, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { QuizService } from '../../core/services/quiz.service';
import { SessionService } from '../../core/services/session.service';
import { StepType, defaultStepConfig } from '../../models/quiz-config.model';
import { SaveQuizRequest } from '../../models/quiz.model';
import { UiCardComponent } from '../../shared/components/ui-card/ui-card.component';
import { StepCatalogComponent } from './components/step-catalog/step-catalog.component';
import { StepEditorComponent } from './components/step-editor/step-editor.component';
import { StepListComponent } from './components/step-list/step-list.component';
import { QuizStepDraft, toDraft, toQuizStep } from './models/quiz-step-draft.model';

@Component({
  selector: 'app-quiz-builder',
  imports: [FormsModule, UiCardComponent, StepCatalogComponent, StepListComponent, StepEditorComponent],
  templateUrl: './quiz-builder.component.html',
  styleUrl: './quiz-builder.component.scss'
})
export class QuizBuilderComponent implements OnInit {
  private quizId: number | null = null;

  protected readonly title = signal('');
  protected readonly description = signal('');
  protected readonly steps = signal<QuizStepDraft[]>([]);
  protected readonly selectedClientId = signal<number | null>(null);
  protected readonly inviteCode = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly launching = signal(false);

  protected readonly selectedStep = computed(
    () => this.steps().find((s) => s.clientId === this.selectedClientId()) ?? null
  );

  protected get canLaunchSession(): boolean {
    return this.quizId !== null && this.steps().length > 0;
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
        this.inviteCode.set(quiz.inviteCode);
        this.steps.set(quiz.steps.map(toDraft));
        this.loading.set(false);
      },
      error: () => {
        this.error.set("Ce quiz n'existe pas ou ne t'appartient pas.");
        this.loading.set(false);
      }
    });
  }

  protected addStep(type: StepType): void {
    const draft = toDraft({
      orderIndex: this.steps().length,
      type,
      title: `Nouvelle épreuve`,
      configJson: JSON.stringify(defaultStepConfig())
    });
    this.steps.update((steps) => [...steps, draft]);
    this.selectedClientId.set(draft.clientId);
  }

  protected selectStep(clientId: number): void {
    this.selectedClientId.set(clientId);
  }

  protected onStepChange(updated: QuizStepDraft): void {
    this.steps.update((steps) => steps.map((s) => (s.clientId === updated.clientId ? updated : s)));
  }

  protected moveStepUp(clientId: number): void {
    this.swapSteps(clientId, -1);
  }

  protected moveStepDown(clientId: number): void {
    this.swapSteps(clientId, 1);
  }

  private swapSteps(clientId: number, offset: number): void {
    this.steps.update((steps) => {
      const index = steps.findIndex((s) => s.clientId === clientId);
      const targetIndex = index + offset;
      if (index === -1 || targetIndex < 0 || targetIndex >= steps.length) {
        return steps;
      }
      const reordered = [...steps];
      [reordered[index], reordered[targetIndex]] = [reordered[targetIndex], reordered[index]];
      return reordered.map((s, i) => ({ ...s, orderIndex: i }));
    });
  }

  protected removeStep(clientId: number): void {
    this.steps.update((steps) =>
      steps.filter((s) => s.clientId !== clientId).map((s, i) => ({ ...s, orderIndex: i }))
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
      steps: this.steps().map(toQuizStep)
    };

    const save$ = this.quizId ? this.quizService.update(this.quizId, request) : this.quizService.create(request);

    save$.subscribe({
      next: (quiz) => {
        this.saving.set(false);
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
