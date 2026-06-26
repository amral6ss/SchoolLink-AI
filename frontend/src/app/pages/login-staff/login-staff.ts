import { Component, inject, signal, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { RoleService } from '../../shared/role.service';
import { SchoolProfileService, SchoolProfile } from '../../core/services/school-profile.service';

@Component({
  selector: 'app-login-staff',
  imports: [FormsModule],
  templateUrl: './login-staff.html',
})
export class LoginStaff implements OnInit {
  private auth = inject(AuthService);
  private router = inject(Router);
  private roleService = inject(RoleService);
  private schoolProfileService = inject(SchoolProfileService);
  roleTab = signal<'admin' | 'teacher'>('admin');
  errorMsg = signal('');
  schoolPhone = signal('');

  ngOnInit() {
    this.schoolProfileService.get().subscribe({
      next: (profile) => this.schoolPhone.set(profile.phone || ''),
      error: () => {},
    });
  }

  togglePwd(pwd: HTMLInputElement) {
    pwd.type = pwd.type === 'password' ? 'text' : 'password';
  }

  handleLogin(f: any) {
    this.errorMsg.set('');
    if (!f.valid) { this.errorMsg.set('يرجى إدخال اسم المستخدم وكلمة المرور'); return; }
    const { username, password } = f.value;
    this.auth.login(this.roleTab(), username, password).subscribe({
      next: (session) => this.router.navigateByUrl(this.roleService.getHomeRoute(session.role)),
      error: (err) => this.errorMsg.set(err.message || 'اسم المستخدم أو كلمة المرور غير صحيحة')
    });
  }
}
