import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';

export interface MediaUploadResponse {
  url: string;
}

@Injectable({ providedIn: 'root' })
export class MediaService {
  private readonly baseUrl = `${environment.apiBaseUrl}/api/media`;

  constructor(private readonly http: HttpClient) {}

  upload(file: File) {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<MediaUploadResponse>(this.baseUrl, formData);
  }

  resolveUrl(url: string): string {
    return url.startsWith('http') ? url : `${environment.apiBaseUrl}${url}`;
  }
}
