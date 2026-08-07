import { CdkDrag, CdkDragDrop, CdkDragHandle, CdkDropList, moveItemInArray } from '@angular/cdk/drag-drop';
import { Component, effect, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MediaService } from '../../../../core/services/media.service';

interface OrderListItem {
  id: string;
  content: string;
}

interface OrderListQuestionPayload {
  questionText: string;
  contentType: 'Text' | 'Image' | 'Audio';
  items: OrderListItem[];
}

function defaultPayload(): OrderListQuestionPayload {
  return { questionText: '', contentType: 'Text', items: [] };
}

@Component({
  selector: 'app-order-list-question-editor',
  imports: [FormsModule, CdkDropList, CdkDrag, CdkDragHandle],
  templateUrl: './order-list-question-editor.component.html',
  styleUrl: './order-list-question-editor.component.scss'
})
export class OrderListQuestionEditorComponent {
  readonly payloadJson = input.required<string>();
  readonly payloadJsonChange = output<string>();

  protected readonly payload = signal<OrderListQuestionPayload>(defaultPayload());
  protected readonly uploadingIndex = signal<number | null>(null);
  protected readonly uploadError = signal<string | null>(null);

  constructor(protected readonly mediaService: MediaService) {
    effect(() => this.payload.set(this.parse(this.payloadJson())));
  }

  private parse(json: string): OrderListQuestionPayload {
    try {
      return { ...defaultPayload(), ...JSON.parse(json) };
    } catch {
      return defaultPayload();
    }
  }

  private emit(): void {
    this.payloadJsonChange.emit(JSON.stringify(this.payload()));
  }

  protected onQuestionTextChange(value: string): void {
    this.payload.update((p) => ({ ...p, questionText: value }));
    this.emit();
  }

  protected onContentTypeChange(value: 'Text' | 'Image' | 'Audio'): void {
    // Le contenu (texte brut vs URL média) n'a plus de sens en changeant de type : on repart de zéro
    // par item plutôt que de garder des valeurs incohérentes avec le nouveau type choisi.
    this.payload.update((p) => ({ ...p, contentType: value, items: p.items.map((it) => ({ ...it, content: '' })) }));
    this.emit();
  }

  protected onItemContentChange(index: number, value: string): void {
    this.payload.update((p) => {
      const items = [...p.items];
      items[index] = { ...items[index], content: value };
      return { ...p, items };
    });
    this.emit();
  }

  protected onFileSelected(index: number, event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) {
      return;
    }

    this.uploadingIndex.set(index);
    this.uploadError.set(null);

    this.mediaService.upload(file).subscribe({
      next: (response) => {
        this.uploadingIndex.set(null);
        this.onItemContentChange(index, response.url);
      },
      error: () => {
        this.uploadingIndex.set(null);
        this.uploadError.set("Échec de l'envoi du fichier.");
      }
    });

    input.value = '';
  }

  protected resolveMediaUrl(url: string): string {
    return this.mediaService.resolveUrl(url);
  }

  protected addItem(): void {
    this.payload.update((p) => ({ ...p, items: [...p.items, { id: crypto.randomUUID(), content: '' }] }));
    this.emit();
  }

  protected removeItem(index: number): void {
    this.payload.update((p) => ({ ...p, items: p.items.filter((_, i) => i !== index) }));
    this.emit();
  }

  protected onDrop(event: CdkDragDrop<OrderListItem[]>): void {
    if (event.previousIndex === event.currentIndex) {
      return;
    }

    this.payload.update((p) => {
      const items = [...p.items];
      moveItemInArray(items, event.previousIndex, event.currentIndex);
      return { ...p, items };
    });
    this.emit();
  }
}
