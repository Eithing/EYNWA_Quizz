import { Component, OnInit, output, signal } from '@angular/core';
import { FeatureService } from '../../../../core/services/feature.service';
import { FeatureMeta } from '../../../../models/feature.model';
import { UiCardComponent } from '../../../../shared/components/ui-card/ui-card.component';

@Component({
  selector: 'app-feature-picker',
  imports: [UiCardComponent],
  templateUrl: './feature-picker.component.html',
  styleUrl: './feature-picker.component.scss'
})
export class FeaturePickerComponent implements OnInit {
  protected readonly features = signal<FeatureMeta[]>([]);

  readonly featureSelected = output<FeatureMeta>();

  constructor(private readonly featureService: FeatureService) {}

  ngOnInit(): void {
    this.featureService.getAll().subscribe((features) => this.features.set(features));
  }
}
