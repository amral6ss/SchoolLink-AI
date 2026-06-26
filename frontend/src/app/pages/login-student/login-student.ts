import { Component, inject, signal, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/services/auth.service';
import { SchoolProfileService } from '../../core/services/school-profile.service';

@Component({
  selector: 'app-login-student',
  imports: [FormsModule],
  templateUrl: './login-student.html',
  styleUrl: './login-student.css'
})
export class LoginStudent implements OnInit {
  private auth = inject(AuthService);
  private router = inject(Router);
  private schoolProfileSvc = inject(SchoolProfileService);
  schoolPhone = signal('');

  ngOnInit() {
    this.schoolProfileSvc.get().subscribe({
      next: p => { if (p?.phone) this.schoolPhone.set(p.phone); },
      error: () => {}
    });
  }

  togglePwd(pwd: HTMLInputElement) {
    pwd.type = pwd.type === 'password' ? 'text' : 'password';
  }
  handleLogin(f: any) {
    if (!f.valid) { alert('يرجى إدخال اسم المستخدم وكلمة المرور'); return; }
    const { username, password } = f.value;
    this.auth.login('student', username, password).subscribe({
      next: () => this.router.navigate(['/student']),
      error: (err) => alert(err.message)
    });
  }
}
