import { Component, effect, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterOutlet } from '@angular/router';
import { I18nService } from './core/services/i18n.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  private readonly i18n = inject(I18nService);
  private readonly title = inject(Title);

  constructor() {
    effect(() => {
      this.title.setTitle('KSA HR Payroll');
    });
  }
}

