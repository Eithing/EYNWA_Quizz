import { Component, effect, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MediaService } from '../../../../core/services/media.service';
import { AudioPlayerComponent } from '../../../../shared/components/audio-player/audio-player.component';

interface BlindTestQuestionPayload {
  audioUrl: string;
  acceptedAnswers: string[];
}

function defaultPayload(): BlindTestQuestionPayload {
  return { audioUrl: '', acceptedAnswers: [] };
}

@Component({
  selector: 'app-blind-test-question-editor',
  imports: [FormsModule, AudioPlayerComponent],
  templateUrl: './blind-test-question-editor.component.html',
  styleUrl: './blind-test-question-editor.component.scss'
})
export class BlindTestQuestionEditorComponent {
  readonly payloadJson = input.required<string>();
  readonly payloadJsonChange = output<string>();

  protected readonly payload = signal<BlindTestQuestionPayload>(defaultPayload());
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

  private parse(json: string): BlindTestQuestionPayload {
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
        this.payload.update((p) => ({ ...p, audioUrl: response.url }));
        this.emit();
      },
      error: () => {
        this.uploading.set(false);
        this.uploadError.set("Échec de l'envoi du fichier audio.");
      }
    });

    input.value = '';
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

  protected resolveAudioUrl(url: string): string {
    return this.mediaService.resolveUrl(url);
  }
}
