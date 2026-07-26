import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';
import { FeatureMeta } from '../../models/feature.model';

@Injectable({ providedIn: 'root' })
export class FeatureService {
  constructor(private readonly http: HttpClient) {}

  getAll() {
    return this.http.get<FeatureMeta[]>(`${environment.apiBaseUrl}/api/features`);
  }
}
