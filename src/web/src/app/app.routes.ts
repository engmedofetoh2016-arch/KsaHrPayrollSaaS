import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { roleGuard } from './core/guards/role.guard';
import { AttendancePageComponent } from './features/attendance/attendance-page.component';
import { LoginPageComponent } from './features/auth/login-page.component';
import { CompanyPageComponent } from './features/company/company-page.component';
import { CompliancePageComponent } from './features/compliance/compliance-page.component';
import { DashboardPageComponent } from './features/dashboard/dashboard-page.component';
import { EmployeesPageComponent } from './features/employees/employees-page.component';
import { FinalSettlementPageComponent } from './features/final-settlement/final-settlement-page.component';
import { LeaveApprovalsPageComponent } from './features/leave/leave-approvals-page.component';
import { MyLeavePageComponent } from './features/leave/my-leave-page.component';
import { PayrollPageComponent } from './features/payroll/payroll-page.component';
import { UsersPageComponent } from './features/users/users-page.component';
import { ShellLayoutComponent } from './layout/shell/shell-layout.component';

export const routes: Routes = [
  {
    path: 'login',
    component: LoginPageComponent
  },
  {
    path: '',
    component: ShellLayoutComponent,
    canActivate: [authGuard],
    children: [
      { path: 'dashboard', component: DashboardPageComponent },
      { path: 'compliance', component: CompliancePageComponent, canActivate: [roleGuard], data: { roles: ['Owner', 'Admin', 'HR', 'Manager'] } },
      { path: 'company', component: CompanyPageComponent, canActivate: [roleGuard], data: { roles: ['Owner', 'Admin', 'HR'] } },
      { path: 'employees', component: EmployeesPageComponent, canActivate: [roleGuard], data: { roles: ['Owner', 'Admin', 'HR'] } },
      { path: 'final-settlement', component: FinalSettlementPageComponent, canActivate: [roleGuard], data: { roles: ['Owner', 'Admin', 'HR'] } },
      { path: 'leave/my', component: MyLeavePageComponent },
      {
        path: 'leave/approvals',
        component: LeaveApprovalsPageComponent,
        canActivate: [roleGuard],
        data: { roles: ['Owner', 'Admin', 'HR', 'Manager'] }
      },
      { path: 'attendance', component: AttendancePageComponent, canActivate: [roleGuard], data: { roles: ['Owner', 'Admin', 'HR', 'Manager'] } },
      { path: 'payroll', component: PayrollPageComponent, canActivate: [roleGuard], data: { roles: ['Owner', 'Admin', 'HR'] } },
      { path: 'users', component: UsersPageComponent, canActivate: [roleGuard], data: { roles: ['Owner', 'Admin'] } },
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' }
    ]
  },
  { path: '**', redirectTo: '' }
];
