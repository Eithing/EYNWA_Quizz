import { Component, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { QuizService } from '../../core/services/quiz.service';
import { QuizSummary } from '../../models/quiz.model';
import { UiCardComponent } from '../../shared/components/ui-card/ui-card.component';

@Component({
  selector: 'app-dashboard',
  imports: [RouterLink, UiCardComponent],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent implements OnInit {
  protected readonly quizzes = signal<QuizSummary[]>([]);
  protected readonly loading = signal(true);

  constructor(
    private readonly quizService: QuizService,
    private readonly router: Router
  ) {}

  ngOnInit(): void {
    this.quizService.getMine().subscribe({
      next: (quizzes) => {
        this.quizzes.set(quizzes);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  protected openQuiz(id: number): void {
    this.router.navigate(['/quizzes', id]);
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
