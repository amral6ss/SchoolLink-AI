import { Component, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { SchoolProfileService, SchoolProfile } from '../../core/services/school-profile.service';

@Component({
  selector: 'app-landing',
  imports: [],
  templateUrl: './landing.html',
  styleUrl: './landing.css'
})
export class Landing implements OnInit {
  private router = inject(Router);
  private schoolProfileService = inject(SchoolProfileService);

  schoolProfile = signal<SchoolProfile | null>(null);

  ngOnInit() {
    this.schoolProfileService.get().subscribe({
      next: (profile) => this.schoolProfile.set(profile),
      error: () => {},
    });
  }

  navigateToLogin() {
    this.router.navigate(['/login-guardian']);
  }

  navigateToAdmin() {
    this.router.navigate(['/login-staff']);
  }
}
