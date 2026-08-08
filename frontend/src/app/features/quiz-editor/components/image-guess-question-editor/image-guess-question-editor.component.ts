import { Component, computed, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MediaService } from '../../../../core/services/media.service';
import { MediaUploadFieldComponent } from '../../../../shared/components/media-upload-field/media-upload-field.component';
import { ExpectedAnswerDraft, ExpectedAnswersEditorComponent } from '../expected-answers-editor/expected-answers-editor.component';
import { PointsMode, roundPointsModeFrom, syncPayloadFromJson, toExpectedAnswers } from '../question-editor-payload.util';

interface ImageGuessQuestionPayload {
  imageUrl: string;
  /** Legacy, lu uniquement pour la rétrocompatibilité — voir toExpectedAnswers(). */
  acceptedAnswers: string[];
  expectedAnswers: ExpectedAnswerDraft[];
  /** Surcharge du mode de points de la manche pour CETTE question. Null = suit le réglage de la manche. */
  pointsModeOverride: 'Uniform' | 'PerAnswer' | null;
  /** Commentaire/consigne optionnel affiché à côté de l'image (ex: "Donnez le nom de l'objet et du jeu"). */
  comment: string;
}

function defaultPayload(): ImageGuessQuestionPayload {
  return { imageUrl: '', acceptedAnswers: [], expectedAnswers: [], pointsModeOverride: null, comment: '' };
}

@Component({
  selector: 'app-image-guess-question-editor',
  imports: [FormsModule, ExpectedAnswersEditorComponent, MediaUploadFieldComponent],
  templateUrl: './image-guess-question-editor.component.html',
  styleUrl: './image-guess-question-editor.component.scss'
})
export class ImageGuessQuestionEditorComponent {
  readonly payloadJson = input.required<string>();
  readonly configJson = input<string>('{}');
  readonly payloadJsonChange = output<string>();

  protected readonly payload = syncPayloadFromJson(this.payloadJson, defaultPayload, (parsed) => ({
    ...parsed,
    expectedAnswers: toExpectedAnswers(parsed)
  }));

  /** Réglage par défaut de la manche (round-config), avant surcharge éventuelle par cette question. */
  protected readonly roundPointsMode = computed<PointsMode>(() => roundPointsModeFrom(this.configJson()));

  /** Mode réellement appliqué à cette question : sa propre surcharge si renseignée, sinon celui de la manche. */
  protected readonly effectivePointsMode = computed<PointsMode>(() => this.payload().pointsModeOverride ?? this.roundPointsMode());

  constructor(protected readonly mediaService: MediaService) {}

  private emit(): void {
    this.payloadJsonChange.emit(JSON.stringify(this.payload()));
  }

  protected onImageUploaded(url: string): void {
    this.payload.update((p) => ({ ...p, imageUrl: url }));
    this.emit();
  }

  protected onCommentChange(value: string): void {
    this.payload.update((p) => ({ ...p, comment: value }));
    this.emit();
  }

  protected onExpectedAnswersChange(expectedAnswers: ExpectedAnswerDraft[]): void {
    this.payload.update((p) => ({ ...p, expectedAnswers, acceptedAnswers: [] }));
    this.emit();
  }

  protected onPointsModeOverrideChange(value: string): void {
    this.payload.update((p) => ({ ...p, pointsModeOverride: value === '' ? null : (value as 'Uniform' | 'PerAnswer') }));
    this.emit();
  }

  protected resolveImageUrl(url: string): string {
    return this.mediaService.resolveUrl(url);
  }
}
