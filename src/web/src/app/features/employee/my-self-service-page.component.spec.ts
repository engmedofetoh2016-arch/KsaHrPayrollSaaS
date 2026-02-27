import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { MySelfServicePageComponent } from './my-self-service-page.component';

describe('MySelfServicePageComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MySelfServicePageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
  });

  it('should create', () => {
    const fixture = TestBed.createComponent(MySelfServicePageComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });
});
