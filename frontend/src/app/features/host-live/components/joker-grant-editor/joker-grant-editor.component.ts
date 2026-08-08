import { Component, OnInit, computed, input, output, signal } from '@angular/core';
import { JOKER_ICONS, JOKER_LABELS, JOKER_TYPES, JokerGrant, JokerType, Player, Team } from '../../../../models/session.model';

interface JokerOwner {
  kind: 'player' | 'team';
  id: number;
  label: string;
}

@Component({
  selector: 'app-joker-grant-editor',
  imports: [],
  templateUrl: './joker-grant-editor.component.html',
  styleUrl: './joker-grant-editor.component.scss'
})
export class JokerGrantEditorComponent implements OnInit {
  readonly players = input.required<Player[]>();
  readonly teams = input.required<Team[]>();
  readonly existingGrants = input.required<JokerGrant[]>();
  readonly save = output<{ type: JokerType; playerId: number | null; teamId: number | null; charges: number }[]>();
  readonly cancel = output<void>();

  protected readonly jokerTypes = JOKER_TYPES;
  protected readonly jokerLabels = JOKER_LABELS;
  protected readonly jokerIcons = JOKER_ICONS;

  /** Clé "kind:id:type" -> charges en cours d'édition (pas encore enregistrées). */
  protected readonly jokerDraftCharges = signal<Record<string, number>>({});

  protected readonly jokerOwners = computed<JokerOwner[]>(() =>
    this.teams().length > 0
      ? this.teams().map((t) => ({ kind: 'team' as const, id: t.id, label: t.name }))
      : this.players().map((p) => ({ kind: 'player' as const, id: p.id, label: p.pseudo }))
  );

  ngOnInit(): void {
    const grants = this.existingGrants();
    const draft: Record<string, number> = {};

    for (const owner of this.jokerOwners()) {
      for (const type of this.jokerTypes) {
        const existing = grants.find(
          (g) => g.type === type && (owner.kind === 'player' ? g.ownerPlayerId === owner.id : g.ownerTeamId === owner.id)
        );
        draft[this.jokerDraftKey(owner, type)] = existing?.charges ?? 0;
      }
    }

    this.jokerDraftCharges.set(draft);
  }

  private jokerDraftKey(owner: JokerOwner, type: JokerType): string {
    return `${owner.kind}:${owner.id}:${type}`;
  }

  protected jokerDraftValue(owner: JokerOwner, type: JokerType): number {
    return this.jokerDraftCharges()[this.jokerDraftKey(owner, type)] ?? 0;
  }

  protected setJokerDraftValue(owner: JokerOwner, type: JokerType, value: number): void {
    this.jokerDraftCharges.update((draft) => ({ ...draft, [this.jokerDraftKey(owner, type)]: Math.max(0, value) }));
  }

  protected submit(): void {
    const grants = this.jokerOwners().flatMap((owner) =>
      this.jokerTypes.map((type) => ({
        type,
        playerId: owner.kind === 'player' ? owner.id : null,
        teamId: owner.kind === 'team' ? owner.id : null,
        charges: this.jokerDraftValue(owner, type)
      }))
    );
    this.save.emit(grants);
  }
}
