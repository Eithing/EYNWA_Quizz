import { Component, effect, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MediaService } from '../../../../core/services/media.service';

interface ZoomQuestionPayload {
  imageUrl: string;
  acceptedAnswers: string[];
  zoomFocusPoint: { x: number; y: number };
}

function defaultPayload(): ZoomQuestionPayload {
  return { imageUrl: '', acceptedAnswers: [], zoomFocusPoint: { x: 0.5, y: 0.5 } };
}

@Component({
  selector: 'app-zoom-question-editor',
  imports: [FormsModule],
  templateUrl: './zoom-question-editor.component.html',
  styleUrl: './zoom-question-editor.component.scss'
})
export class ZoomQuestionEditorComponent {
  readonly payloadJson = input.required<string>();
  readonly payloadJsonChange = output<string>();

  protected readonly payload = signal<ZoomQuestionPayload>(defaultPayload());
  protected readonly acceptedAnswersText = signal('');
  protected readonly uploading = signal(false);
  protected readonly uploadError = signal<string | null>(null);

  constructor(protected readonly mediaService: MediaService) {
    effect(() => {
      const parsed = this.parse(this.payloadJson());
      this.payload.set(parsed);
      this.acceptedAnswersText.set(parsed.acceptedAnswers.join(', '));
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

  protected onAcceptedAnswersChange(value: string): void {
    this.acceptedAnswersText.set(value);
    const answers = value
      .split(',')
      .map((a) => a.trim())
      .filter((a) => a.length > 0);

    this.payload.update((p) => ({ ...p, acceptedAnswers: answers }));
    this.emit();
  }

  protected resolveImageUrl(url: string): string {
    return this.mediaService.resolveUrl(url);
  }
}
