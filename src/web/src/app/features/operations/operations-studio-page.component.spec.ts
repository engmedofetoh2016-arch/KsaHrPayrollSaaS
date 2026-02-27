import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { OperationsStudioPageComponent } from './operations-studio-page.component';

describe('OperationsStudioPageComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [OperationsStudioPageComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
  });

  it('should create', () => {
    const fixture = TestBed.createComponent(OperationsStudioPageComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should map known technical code to arabic label', () => {
    const fixture = TestBed.createComponent(OperationsStudioPageComponent);
    const component = fixture.componentInstance;
    expect(component.codeLabel('WPS_MISSING')).toContain('حماية الأجور');
  });

  it('should toggle technical code visibility', () => {
    const fixture = TestBed.createComponent(OperationsStudioPageComponent);
    const component = fixture.componentInstance;
    expect(component.showTechnicalCodes()).toBe(false);
    component.toggleTechnicalCodes();
    expect(component.showTechnicalCodes()).toBe(true);
  });
});
