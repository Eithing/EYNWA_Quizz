import { Component, input } from '@angular/core';

@Component({
  selector: 'app-zoom-viewer',
  imports: [],
  templateUrl: './zoom-viewer.component.html',
  styleUrl: './zoom-viewer.component.scss'
})
export class ZoomViewerComponent {
  readonly imageUrl = input.required<string>();
  readonly focusX = input(0.5);
  readonly focusY = input(0.5);
  readonly level = input(1);
}
