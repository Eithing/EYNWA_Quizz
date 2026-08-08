import { Component, input, output, signal } from '@angular/core';
import { MediaService } from '../../../core/services/media.service';

@Component({
  selector: 'app-media-upload-field',
  imports: [],
  templateUrl: './media-upload-field.component.html',
  styleUrl: './media-upload-field.component.scss'
})
export class MediaUploadFieldComponent {
  readonly accept = input.required<string>();
  readonly hasFile = input(false);
  readonly chooseLabel = input.required<string>();
  readonly changeLabel = input.required<string>();
  readonly errorMessage = input("Échec de l'envoi du fichier.");
  readonly uploaded = output<string>();

  protected readonly uploading = signal(false);
  protected readonly uploadError = signal<string | null>(null);

  constructor(private readonly mediaService: MediaService) {}

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
        this.uploaded.emit(response.url);
      },
      error: () => {
        this.uploading.set(false);
        this.uploadError.set(this.errorMessage());
      }
    });

    input.value = '';
  }
}
