import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { MyProfileResponse } from '../../core/models/me.models';
import { I18nService } from '../../core/services/i18n.service';
import { MeService } from '../../core/services/me.service';
import { getApiErrorMessage } from '../../core/utils/api-error.util';

@Component({
  selector: 'app-my-profile-page',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './my-profile-page.component.html',
  styleUrl: './my-profile-page.component.scss'
})
export class MyProfilePageComponent implements OnInit {
  private readonly meService = inject(MeService);
  readonly i18n = inject(I18nService);

  readonly loading = signal(false);
  readonly error = signal('');
  readonly profile = signal<MyProfileResponse | null>(null);

  ngOnInit(): void {
    this.load();
  }

  load() {
    this.loading.set(true);
    this.error.set('');

    this.meService.getProfile().subscribe({
      next: (response) => {
        this.profile.set(response);
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.error.set(getApiErrorMessage(err, 'Failed to load profile.'));
      }
    });
  }
}
