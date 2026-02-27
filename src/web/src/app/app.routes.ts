import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { roleGuard } from './core/guards/role.guard';
import { AttendancePageComponent } from './features/attendance/attendance-page.component';
import { ChangePasswordPageComponent } from './features/auth/change-password-page.component';
import { LoginPageComponent } from './features/auth/login-page.component';
import { ResetPasswordPageComponent } from './features/auth/reset-password-page.component';
import { CompanyPageComponent } from './features/company/company-page.component';
import { CompliancePageComponent } from './features/compliance/compliance-page.component';
import { DashboardPageComponent } from './features/dashboard/dashboard-page.component';
import { EmployeesPageComponent } from './features/employees/employees-page.component';
import { MyPayslipsPageComponent } from './features/employee/my-payslips-page.component';
import { MyProfilePageComponent } from './features/employee/my-profile-page.component';
import { MySelfServicePageComponent } from './features/employee/my-self-service-page.component';
import { MySalaryCertificatePageComponent } from './features/employee/my-salary-certificate-page.component';
import { MyEosEstimatePageComponent } from './features/employee/my-eos-estimate-page.component';
import { FinalSettlementPageComponent } from './features/final-settlement/final-settlement-page.component';
import { ReferenceRegistryPageComponent } from './features/governance/reference-registry-page.component';
import { LeaveApprovalsPageComponent } from './features/leave/leave-approvals-page.component';
import { MyLeavePageComponent } from './features/leave/my-leave-page.component';
import { LoansPageComponent } from './features/loans/loans-page.component';
import { OffboardingChecklistPageComponent } from './features/offboarding/offboarding-checklist-page.component';
import { PayrollPageComponent } from './features/payroll/payroll-page.component';
import { OperationsStudioPageComponent } from './features/operations/operations-studio-page.component';
import { SmartAlertsPageComponent } from './features/smart-alerts/smart-alerts-page.component';
import { UsersPageComponent } from './features/users/users-page.component';
import { ShellLayoutComponent } from './layout/shell/shell-layout.component';

export const routes: Routes = [
  {
    path: 'login',
    component: LoginPageComponent
  },
  {
    path: 'reset-password',
    component: ResetPasswordPageComponent
  },
  {
    path: '',
    component: ShellLayoutComponent,
    canActivate: [authGuard],
    children: [
      { path: 'change-password', component: ChangePasswordPageComponent },
      { path: 'dashboard', component: DashboardPageComponent },
      { path: 'compliance', component: CompliancePageComponent, canActivate: [roleGuard], data: { roles: ['Owner', 'Admin', 'HR', 'Manager'] } },
      { path: 'company', component: CompanyPageComponent, canActivate: [roleGuard], data: { roles: ['Owner', 'Admin', 'HR'] } },
      { path: 'employees', component: EmployeesPageComponent, canActivate: [roleGuard], data: { roles: ['Owner', 'Admin', 'HR'] } },
      { path: 'final-settlement', component: FinalSettlementPageComponent, canActivate: [roleGuard], data: { roles: ['Owner', 'Admin', 'HR'] } },
      { path: 'loans', component: LoansPageComponent, canActivate: [roleGuard], data: { roles: ['Owner', 'Admin', 'HR'] } },
      { path: 'offboarding', component: OffboardingChecklistPageComponent, canActivate: [roleGuard], data: { roles: ['Owner', 'Admin', 'HR', 'Manager'] } },
      { path: 'leave/my', component: MyLeavePageComponent },
      { path: 'employee/profile', component: MyProfilePageComponent, canActivate: [roleGuard], data: { roles: ['Employee'] } },
      { path: 'employee/self-service', component: MySelfServicePageComponent, canActivate: [roleGuard], data: { roles: ['Employee'] } },
      { path: 'employee/payslips', component: MyPayslipsPageComponent, canActivate: [roleGuard], data: { roles: ['Employee'] } },
      { path: 'employee/salary-certificate', component: MySalaryCertificatePageComponent, canActivate: [roleGuard], data: { roles: ['Employee'] } },
      { path: 'employee/eos-estimate', component: MyEosEstimatePageComponent, canActivate: [roleGuard], data: { roles: ['Employee'] } },
      {
        path: 'leave/approvals',
        component: LeaveApprovalsPageComponent,
        canActivate: [roleGuard],
        data: { roles: ['Owner', 'Admin', 'HR', 'Manager'] }
      },
      { path: 'attendance', component: AttendancePageComponent, canActivate: [roleGuard], data: { roles: ['Owner', 'Admin', 'HR', 'Manager'] } },
      { path: 'payroll', component: PayrollPageComponent, canActivate: [roleGuard], data: { roles: ['Owner', 'Admin', 'HR'] } },
      { path: 'operations-studio', component: OperationsStudioPageComponent, canActivate: [roleGuard], data: { roles: ['Owner', 'Admin', 'HR', 'Manager'] } },
      { path: 'smart-alerts', component: SmartAlertsPageComponent, canActivate: [roleGuard], data: { roles: ['Owner', 'Admin', 'HR', 'Manager'] } },
      { path: 'governance/references', component: ReferenceRegistryPageComponent, canActivate: [roleGuard], data: { roles: ['Owner', 'Admin', 'HR', 'Manager'] } },
      { path: 'users', component: UsersPageComponent, canActivate: [roleGuard], data: { roles: ['Owner', 'Admin'] } },
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' }
    ]
  },
  { path: '**', redirectTo: '' }
];
