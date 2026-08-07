import { Component, computed, effect, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MediaService } from '../../../../core/services/media.service';
import { ExpectedAnswerDraft, ExpectedAnswersEditorComponent } from '../expected-answers-editor/expected-answers-editor.component';

interface ZoomQuestionPayload {
  imageUrl: string;
  /** Legacy, lu uniquement pour la rétrocompatibilité — voir toExpectedAnswers(). */
  acceptedAnswers: string[];
  expectedAnswers: ExpectedAnswerDraft[];
  zoomFocusPoint: { x: number; y: number };
}

function defaultPayload(): ZoomQuestionPayload {
  return { imageUrl: '', acceptedAnswers: [], expectedAnswers: [], zoomFocusPoint: { x: 0.5, y: 0.5 } };
}

function toExpectedAnswers(payload: ZoomQuestionPayload): ExpectedAnswerDraft[] {
  if (payload.expectedAnswers.length > 0) {
    return payload.expectedAnswers;
  }
  if (payload.acceptedAnswers.length > 0) {
    return [{ acceptedVariants: payload.acceptedAnswers, points: null }];
  }
  return [];
}

@Component({
  selector: 'app-zoom-question-editor',
  imports: [FormsModule, ExpectedAnswersEditorComponent],
  templateUrl: './zoom-question-editor.component.html',
  styleUrl: './zoom-question-editor.component.scss'
})
export class ZoomQuestionEditorComponent {
  readonly payloadJson = input.required<string>();
  readonly configJson = input<string>('{}');
  readonly payloadJsonChange = output<string>();

  protected readonly payload = signal<ZoomQuestionPayload>(defaultPayload());
  protected readonly uploading = signal(false);
  protected readonly uploadError = signal<string | null>(null);

  protected readonly pointsMode = computed<'Uniform' | 'PerAnswer'>(() => {
    try {
      const parsed = JSON.parse(this.configJson());
      return parsed.pointsMode === 'PerAnswer' ? 'PerAnswer' : 'Uniform';
    } catch {
      return 'Uniform';
    }
  });

  constructor(protected readonly mediaService: MediaService) {
    effect(() => {
      const parsed = this.parse(this.payloadJson());
      this.payload.set({ ...parsed, expectedAnswers: toExpectedAnswers(parsed) });
    });
  }

  private parse(json: string): ZoomQuestionPayload {
    try {
      return { ...defaultPayload(), ...JSON.parse(json) };
    } catch {
      return defaultPayload();
    }
  }

  private emit(): void {
    this.payloadJsonChange.emit(JSON.stringify(this.payload()));
  }

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) {
      return;
    }

    this.uploading.set(true);
    this.uploadError.set(null);

    this.mediaService.upload(file).subscribe({
      next: (response) => {
        this.uploading.set(false);
        this.payload.update((p) => ({ ...p, imageUrl: response.url }));
        this.emit();
      },
      error: () => {
        this.uploading.set(false);
        this.uploadError.set("Échec de l'envoi de l'image.");
      }
    });

    input.value = '';
  }

  protected onImageClick(event: MouseEvent): void {
    const target = event.currentTarget as HTMLElement;
    const rect = target.getBoundingClientRect();
    const x = (event.clientX - rect.left) / rect.width;
    const y = (event.clientY - rect.top) / rect.height;

    this.payload.update((p) => ({ ...p, zoomFocusPoint: { x, y } }));
    this.emit();
  }

  protected onExpectedAnswersChange(expectedAnswers: ExpectedAnswerDraft[]): void {
    this.payload.update((p) => ({ ...p, expectedAnswers, acceptedAnswers: [] }));
    this.emit();
  }

  protected resolveImageUrl(url: string): string {
    return this.mediaService.resolveUrl(url);
  }
}
