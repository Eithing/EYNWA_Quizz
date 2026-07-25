import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';
import { MediaAsset } from '../../models/media.model';

@Injectable({ providedIn: 'root' })
export class MediaService {
  private readonly baseUrl = `${environment.apiBaseUrl}/media`;

  constructor(private readonly http: HttpClient) {}

  upload(file: File) {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<MediaAsset>(this.baseUrl, formData);
  }

  getMine() {
    return this.http.get<MediaAsset[]>(this.baseUrl);
  }

  /** Le endpoint de fichier est public (les joueurs invités doivent pouvoir le charger sans compte). */
  buildFileUrl(assetId: number): string {
    return `${this.baseUrl}/${assetId}/file`;
  }
}
