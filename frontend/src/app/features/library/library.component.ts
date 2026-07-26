import { Component, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { QuizService } from '../../core/services/quiz.service';
import { QuizSummary } from '../../models/quiz.model';
import { UiCardComponent } from '../../shared/components/ui-card/ui-card.component';

@Component({
  selector: 'app-library',
  imports: [RouterLink, UiCardComponent],
  templateUrl: './library.component.html',
  styleUrl: './library.component.scss'
})
export class LibraryComponent implements OnInit {
  protected readonly quizzes = signal<QuizSummary[]>([]);
  protected readonly loading = signal(true);

  constructor(
    private readonly quizService: QuizService,
    private readonly router: Router
  ) {}

  ngOnInit(): void {
    this.loadQuizzes();
  }

  private loadQuizzes(): void {
    this.loading.set(true);
    this.quizService.getMine().subscribe({
      next: (quizzes) => {
        this.quizzes.set(quizzes);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  protected editQuiz(id: number): void {
    this.router.navigate(['/quizzes', id]);
  }

  protected duplicateQuiz(id: number, event: Event): void {
    event.stopPropagation();
    this.quizService.duplicate(id).subscribe(() => this.loadQuizzes());
  }

  protected deleteQuiz(id: number, event: Event): void {
    event.stopPropagation();
    if (!confirm('Supprimer ce quiz ? Cette action est irréversible.')) {
      return;
    }

    this.quizService.delete(id).subscribe(() => {
      this.quizzes.update((quizzes) => quizzes.filter((q) => q.id !== id));
    });
  }
}
