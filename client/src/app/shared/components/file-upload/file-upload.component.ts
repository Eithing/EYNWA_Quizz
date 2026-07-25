import { Component, computed, input, output, signal } from '@angular/core';
import { MediaService } from '../../../core/services/media.service';
import { MediaAsset } from '../../../models/media.model';

@Component({
  selector: 'app-file-upload',
  imports: [],
  templateUrl: './file-upload.component.html',
  styleUrl: './file-upload.component.scss'
})
export class FileUploadComponent {
  readonly accept = input<string>('image/*,audio/*,video/*');
  readonly existingAssetId = input<number | undefined>(undefined);

  readonly assetUploaded = output<MediaAsset>();

  protected readonly uploading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly uploadedPreviewUrl = signal<string | null>(null);

  protected readonly previewUrl = computed(() => {
    const id = this.existingAssetId();
    return this.uploadedPreviewUrl() ?? (id ? this.mediaService.buildFileUrl(id) : null);
  });

  constructor(private readonly mediaService: MediaService) {}

  protected onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) {
      return;
    }

    this.uploading.set(true);
    this.error.set(null);

    this.mediaService.upload(file).subscribe({
      next: (asset) => {
        this.uploading.set(false);
        this.uploadedPreviewUrl.set(this.mediaService.buildFileUrl(asset.id));
        this.assetUploaded.emit(asset);
      },
      error: () => {
        this.uploading.set(false);
        this.error.set("Échec de l'envoi du fichier.");
      }
    });

    input.value = '';
  }
}
