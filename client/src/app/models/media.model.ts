export type MediaKind = 'Image' | 'Audio' | 'Video';

export interface MediaAsset {
  id: number;
  fileName: string;
  url: string;
  kind: MediaKind;
  sizeBytes: number;
}
