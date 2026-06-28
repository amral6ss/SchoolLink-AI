import { CommonModule } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { Sidebar } from '../../layouts/sidebar/sidebar';
import { ParentStudentLink, ParentStudentService, RelationshipType } from '../../core/services/parent-student.service';
import { Student, StudentService } from '../../core/services/student.service';
import {
  GenerateBulkStudentAccountsResult,
  GenerateStudentAccountResult,
  ResetPasswordResult,
  StudentAccountCandidate,
  User,
  UserService
} from '../../core/services/user.service';

type AccountTab = 'all' | 'admins' | 'parents' | 'students';
type ManagedRole = 'Admin' | 'Parent' | 'Student';

@Component({
  selector: 'app-user-management',
  standalone: true,
  imports: [CommonModule, Sidebar],
  templateUrl: './user-management.html',
  styleUrl: './user-management.css'
})
export class UserManagement implements OnInit {
  private userService = inject(UserService);
  private studentService = inject(StudentService);
  private parentStudentService = inject(ParentStudentService);
  private router = inject(Router);

  sidebarOpen = signal(false);
  activeTab = signal<AccountTab>('all');
  searchQuery = signal('');
  candidateSearchQuery = signal('');
  currentPage = signal(1);
  itemsPerPage = signal(10);
  users = signal<User[]>([]);
  students = signal<Student[]>([]);
  studentCandidates = signal<StudentAccountCandidate[]>([]);
  parentLinkedStudents = signal<Array<{ student: Student; link: ParentStudentLink | null }>>([]);
  parentChildSearchResults = signal<Student[]>([]);
  selectedChildren = signal<Array<{ student: Student; relationship: RelationshipType }>>([]);
  parentPhoneCheckResult = signal<{ alreadyExists: boolean; existingParentId?: number | null; existingParentName?: string | null; existingParentUsername?: string | null } | null>(null);

  isLoading = signal(false);
  isGenerating = signal(false);
  isSearchingStudents = signal(false);

  errorMessage = signal<string | null>(null);
  successMessage = signal<string | null>(null);
  generateAllConfirmOpen = signal(false);
  deleteAccountConfirm = signal<User | null>(null);
  resetPasswordConfirm = signal<User | null>(null);
  unlinkStudentConfirm = signal<ParentStudentLink | null>(null);

  showCreateModal = signal(false);
  showEditModal = signal(false);
  showParentLinksModal = signal(false);
  showCredentialsModal = signal(false);
  showBulkResultModal = signal(false);
  showResetPasswordModal = signal(false);

  showFormPassword = signal(false);

  editingUserId = signal<number | null>(null);
  managingParent = signal<User | null>(null);
  selectedStudentToLink = signal<number | null>(null);
  selectedRelationship = signal<RelationshipType>(1);
  lastGeneratedCred = signal<GenerateStudentAccountResult | null>(null);
  bulkResult = signal<GenerateBulkStudentAccountsResult | null>(null);
  resetPasswordResult = signal<ResetPasswordResult | null>(null);
  parentChildSearch = signal('');

  formName = signal('');
  formUsername = signal('');
  formPassword = signal('');
  formPhone = signal('');
  formRole = signal<ManagedRole>('Parent');

  filteredUsers = computed(() => {
    const query = this.searchQuery().trim().toLowerCase();
    const tab = this.activeTab();

    return this.users().filter(user => {
      const role = user.role.toLowerCase();
      const matchesTab =
        tab === 'all' ||
        (tab === 'admins' && role === 'admin') ||
        (tab === 'parents' && role === 'parent') ||
        (tab === 'students' && role === 'student');

      const matchesQuery =
        !query ||
        user.fullName.toLowerCase().includes(query) ||
        user.username.toLowerCase().includes(query) ||
        (user.phone ?? '').toLowerCase().includes(query);

      return matchesTab && matchesQuery;
    });
  });

  filteredCandidates = computed(() => {
    const query = this.candidateSearchQuery().trim().toLowerCase();
    if (!query) {
      return this.studentCandidates();
    }

    return this.studentCandidates().filter(candidate =>
      candidate.fullName.toLowerCase().includes(query) ||
      (candidate.nationalId ?? '').toLowerCase().includes(query)
    );
  });

  paginatedUsers = computed(() => {
    const start = (this.currentPage() - 1) * this.itemsPerPage();
    return this.filteredUsers().slice(start, start + this.itemsPerPage());
  });

  totalPages = computed(() => Math.max(1, Math.ceil(this.filteredUsers().length / this.itemsPerPage())));
  rangeStart = computed(() => {
    if (this.filteredUsers().length === 0) return 0;
    return (this.currentPage() - 1) * this.itemsPerPage() + 1;
  });
  rangeEnd = computed(() => {
    return Math.min(this.currentPage() * this.itemsPerPage(), this.filteredUsers().length);
  });
  pages = computed<(number | string)[]>(() => {
    const total = this.totalPages();
    const current = this.currentPage();
    const result: (number | string)[] = [];

    result.push(1);
    if (current > 3) result.push('...');
    for (let i = Math.max(2, current - 1); i <= Math.min(total - 1, current + 1); i++) {
      result.push(i);
    }
    if (current < total - 2) result.push('...');
    if (total > 1) result.push(total);

    return result;
  });
  trackByPageIndex = (_: number, item: number | string) =>
    typeof item === 'string' ? `dot-${_}` : `page-${item}`;
  totalCount = computed(() => this.users().length);
  adminCount = computed(() => this.users().filter(user => user.role.toLowerCase() === 'admin').length);
  parentCount = computed(() => this.users().filter(user => user.role.toLowerCase() === 'parent').length);
  studentCount = computed(() => this.users().filter(user => user.role.toLowerCase() === 'student').length);
  activeCount = computed(() => this.users().filter(user => user.isActive).length);
  studentCandidatesCount = computed(() => this.studentCandidates().length);
  linkedStudentIdsForManagingParent = computed(() => new Set(this.parentLinkedStudents().map(item => item.student.id)));
  availableStudentsForParent = computed(() => this.students().filter(student => !this.linkedStudentIdsForManagingParent().has(student.id)));
  managedParentChildrenCount = computed(() => this.parentLinkedStudents().length);

  nextPage() {
    if (this.currentPage() < this.totalPages()) {
      this.currentPage.update(page => page + 1);
    }
  }

  prevPage() {
    if (this.currentPage() > 1) {
      this.currentPage.update(page => page - 1);
    }
  }

  goToPage(page: number) {
    this.currentPage.set(page);
  }

  ngOnInit() {
    this.loadUsers();
    this.loadStudents();
    this.loadStudentCandidates();
  }

  loadUsers() {
    this.isLoading.set(true);

    this.userService.getAll({ pageSize: 1000 }).subscribe({
      next: result => {
        this.users.set(result.data?.items ?? []);
        this.isLoading.set(false);
      },
      error: err => {
        this.showError(this.extractErrorMessage(err, 'تعذر تحميل الحسابات'));
        this.isLoading.set(false);
      }
    });
  }

  loadStudents() {
    this.studentService.getAll().subscribe({
      next: result => this.students.set(result.data ?? result),
      error: err => this.showError(this.extractErrorMessage(err, 'تعذر تحميل قائمة الطلاب'))
    });
  }

  loadStudentCandidates() {
    this.userService.getStudentAccountCandidates().subscribe({
      next: res => this.studentCandidates.set(res.data ?? []),
      error: err => this.showError(this.extractErrorMessage(err, 'تعذر تحميل الطلاب بدون حسابات'))
    });
  }

  openCreateModal(role?: ManagedRole) {
    this.resetForm();
    if (role) {
      this.setFormRole(role);
    }
    this.parentPhoneCheckResult.set(null);
    this.showCreateModal.set(true);
  }

  openEditModal(user: User) {
    this.editingUserId.set(user.id);
    this.formName.set(user.fullName);
    this.formUsername.set(user.username);
    this.formPhone.set(user.phone ?? '');
    this.formRole.set(this.toManagedRole(user.role));
    this.formPassword.set('');
    this.showEditModal.set(true);
  }

  closeModals() {
    this.showCreateModal.set(false);
    this.showEditModal.set(false);
    this.showCredentialsModal.set(false);
    this.showBulkResultModal.set(false);
    this.showResetPasswordModal.set(false);
    this.editingUserId.set(null);
    this.lastGeneratedCred.set(null);
    this.bulkResult.set(null);
    this.resetPasswordResult.set(null);
    this.showFormPassword.set(false);
    this.parentPhoneCheckResult.set(null);
    this.resetForm();
  }

  openParentLinksModal(user: User) {
    if (user.role.toLowerCase() !== 'parent') {
      return;
    }

    this.managingParent.set(user);
    this.selectedStudentToLink.set(null);
    this.selectedRelationship.set(1);
    this.showParentLinksModal.set(true);
    this.loadParentChildren(user.id);
  }

  closeParentLinksModal() {
    this.showParentLinksModal.set(false);
    this.managingParent.set(null);
    this.parentLinkedStudents.set([]);
    this.selectedStudentToLink.set(null);
    this.selectedRelationship.set(1);
  }

  loadParentChildren(parentId: number) {
    this.isLoading.set(true);

    this.parentStudentService.getStudentsByParent(parentId).subscribe({
      next: result => {
        const students = result.data ?? result;
        if (students.length === 0) {
          this.parentLinkedStudents.set([]);
          this.isLoading.set(false);
          return;
        }

        forkJoin(
          students.map((student: Student) =>
            this.parentStudentService.getParentsByStudent(student.id).pipe(
              map((links: any) => ({
                student,
                link: (links.data ?? links).find((link: ParentStudentLink) => link.parentId === parentId) ?? null
              })),
              catchError(() => of({ student, link: null }))
            )
          )
        ).subscribe({
          next: rows => {
            this.parentLinkedStudents.set(rows as Array<{ student: Student; link: ParentStudentLink | null }>);
            this.isLoading.set(false);
          },
          error: err => {
            this.showError(this.extractErrorMessage(err, 'تعذر تحميل أبناء ولي الأمر'));
            this.isLoading.set(false);
          }
        });
      },
      error: err => {
        this.showError(this.extractErrorMessage(err, 'تعذر تحميل أبناء ولي الأمر'));
        this.isLoading.set(false);
      }
    });
  }

  generateSingleStudentAccount(candidate: StudentAccountCandidate) {
    if (this.isGenerating()) {
      return;
    }

    this.isGenerating.set(true);
    this.userService.generateStudentAccount(candidate.studentId).subscribe({
      next: res => {
        this.lastGeneratedCred.set(res.data ?? null);
        this.showCredentialsModal.set(true);
        this.loadStudentCandidates();
        this.loadUsers();
        this.loadStudents();
        this.isGenerating.set(false);
      },
      error: err => {
        this.showError(this.extractErrorMessage(err, 'تعذر إنشاء الحساب'));
        this.isGenerating.set(false);
      }
    });
  }

  generateAllStudentAccounts() {
    const allIds = this.filteredCandidates().map(candidate => candidate.studentId);
    if (allIds.length === 0) return;
    this.generateAllConfirmOpen.set(true);
  }

  cancelGenerateAll() {
    this.generateAllConfirmOpen.set(false);
  }

  confirmGenerateAll() {
    this.generateAllConfirmOpen.set(false);
    const allIds = this.filteredCandidates().map(candidate => candidate.studentId);
    if (allIds.length === 0) return;
    this.isGenerating.set(true);
    this.userService.generateBulkStudentAccounts(allIds).subscribe({
      next: res => {
        this.bulkResult.set(res.data ?? null);
        this.showBulkResultModal.set(true);
        this.loadStudentCandidates();
        this.loadUsers();
        this.loadStudents();
        this.isGenerating.set(false);
      },
      error: err => {
        this.showError(this.extractErrorMessage(err, 'تعذر إنشاء الحسابات'));
        this.isGenerating.set(false);
      }
    });
  }

  searchStudentsForParent(query: string) {
    this.parentChildSearch.set(query);
    if (query.trim().length < 2) {
      this.parentChildSearchResults.set([]);
      return;
    }

    this.isSearchingStudents.set(true);
    this.studentService.search({ searchTerm: query, isActive: true }).subscribe({
      next: res => {
        const all: Student[] = res.data ?? res;
        const selectedIds = new Set(this.selectedChildren().map(child => child.student.id));
        this.parentChildSearchResults.set(all.filter(student => !selectedIds.has(student.id)));
        this.isSearchingStudents.set(false);
      },
      error: () => {
        this.parentChildSearchResults.set([]);
        this.isSearchingStudents.set(false);
      }
    });
  }

  addChildToParent(student: Student) {
    if (!this.selectedChildren().some(child => child.student.id === student.id)) {
      this.selectedChildren.update(children => [...children, { student, relationship: 1 }]);
    }

    this.parentChildSearch.set('');
    this.parentChildSearchResults.set([]);
  }

  removeChildFromParent(studentId: number) {
    this.selectedChildren.update(children => children.filter(child => child.student.id !== studentId));
  }

  updateChildRelationship(studentId: number, value: string) {
    const relationship = Number(value) as RelationshipType;
    this.selectedChildren.update(children =>
      children.map(child => child.student.id === studentId ? { ...child, relationship } : child)
    );
  }

  saveAccount() {
    if (this.formRole() === 'Parent' && this.selectedChildren().length > 0) {
      this.saveParentWithStudents();
      return;
    }

    this.saveAccountBasic();
  }

  updateAccount() {
    const userId = this.editingUserId();
    if (!userId || !this.formName()) {
      this.showError('يرجى إدخال الاسم الكامل');
      return;
    }

    this.isLoading.set(true);
    this.userService.updateUser(userId, {
      fullName: this.formName(),
      phone: this.formPhone() || undefined
    }).subscribe({
      next: () => {
        this.showSuccess('تم تحديث الحساب بنجاح');
        this.closeModals();
        this.loadUsers();
      },
      error: err => {
        this.showError(this.extractErrorMessage(err, 'تعذر تحديث الحساب'));
        this.isLoading.set(false);
      }
    });
  }

  toggleAccountStatus(user: User) {
    const nextStatus = !user.isActive;
    this.isLoading.set(true);

    this.userService.setActiveStatus(user.id, nextStatus).subscribe({
      next: () => {
        this.showSuccess(nextStatus ? 'تم تفعيل الحساب بنجاح' : 'تم تعطيل الحساب بنجاح');
        this.loadUsers();
      },
      error: err => {
        this.showError(this.extractErrorMessage(err, 'تعذر تغيير حالة الحساب'));
        this.isLoading.set(false);
      }
    });
  }

  deleteAccount(user: User) {
    this.deleteAccountConfirm.set(user);
  }

  cancelDeleteAccount() {
    this.deleteAccountConfirm.set(null);
  }

  confirmDeleteAccount() {
    const user = this.deleteAccountConfirm();
    if (!user) return;
    this.deleteAccountConfirm.set(null);
    this.isLoading.set(true);
    this.userService.deleteUser(user.id).subscribe({
      next: () => {
        this.showSuccess('تم حذف الحساب بنجاح');
        this.loadUsers();
      },
      error: err => {
        this.showError(this.extractErrorMessage(err, 'تعذر حذف الحساب'));
        this.isLoading.set(false);
      }
    });
  }

  resetPassword(user: User) {
    this.resetPasswordConfirm.set(user);
  }

  cancelResetPassword() {
    this.resetPasswordConfirm.set(null);
  }

  confirmResetPassword() {
    const user = this.resetPasswordConfirm();
    if (!user) return;
    this.resetPasswordConfirm.set(null);
    this.isLoading.set(true);
    this.userService.resetPassword(user.id).subscribe({
      next: res => {
        this.resetPasswordResult.set(res.data ?? null);
        this.showResetPasswordModal.set(true);
        this.isLoading.set(false);
      },
      error: err => {
        this.showError(this.extractErrorMessage(err, 'تعذر إعادة تعيين كلمة المرور'));
        this.isLoading.set(false);
      }
    });
  }

  navigateTo(route: string) {
    this.router.navigate([route]);
  }

  linkStudentToParent() {
    const parent = this.managingParent();
    const studentId = this.selectedStudentToLink();

    if (!parent || !studentId) {
      this.showError('اختر الطالب المراد ربطه أولًا');
      return;
    }

    this.isLoading.set(true);
    this.parentStudentService.link({
      parentId: parent.id,
      studentId,
      relationship: this.selectedRelationship()
    }).subscribe({
      next: () => {
        this.showSuccess('تم ربط الطالب بولي الأمر بنجاح');
        this.selectedStudentToLink.set(null);
        this.loadParentChildren(parent.id);
      },
      error: err => {
        this.showError(this.extractErrorMessage(err, 'تعذر ربط الطالب بولي الأمر'));
        this.isLoading.set(false);
      }
    });
  }

  unlinkStudentFromParent(link: ParentStudentLink) {
    if (!this.managingParent()) return;
    this.unlinkStudentConfirm.set(link);
  }

  cancelUnlinkStudentFromParent() {
    this.unlinkStudentConfirm.set(null);
  }

  confirmUnlinkStudentFromParent() {
    const link = this.unlinkStudentConfirm();
    const parent = this.managingParent();
    if (!link || !parent) return;
    this.unlinkStudentConfirm.set(null);
    this.isLoading.set(true);
    this.parentStudentService.unlink(link.id).subscribe({
      next: () => {
        this.showSuccess('تم فك الربط بنجاح');
        this.loadParentChildren(parent.id);
      },
      error: err => {
        this.showError(this.extractErrorMessage(err, 'تعذر فك الربط'));
        this.isLoading.set(false);
      }
    });
  }

  updateParentRelationship(link: ParentStudentLink, value: string) {
    const parent = this.managingParent();
    if (!parent) {
      return;
    }

    const relationship = Number(value) as RelationshipType;
    this.isLoading.set(true);
    this.parentStudentService.updateRelationship(link.id, relationship).subscribe({
      next: () => {
        this.showSuccess('تم تحديث صلة القرابة بنجاح');
        this.loadParentChildren(parent.id);
      },
      error: err => {
        this.showError(this.extractErrorMessage(err, 'تعذر تحديث صلة القرابة'));
        this.isLoading.set(false);
      }
    });
  }

  setSelectedRelationshipFromValue(value: string) {
    this.selectedRelationship.set(Number(value) as RelationshipType);
  }

  setFormRole(value: string) {
    this.formRole.set(value as ManagedRole);
    if (value !== 'Parent') {
      this.selectedChildren.set([]);
      this.parentChildSearch.set('');
      this.parentChildSearchResults.set([]);
      this.parentPhoneCheckResult.set(null);
    }
  }

  checkParentPhone() {
    const phone = this.formPhone().trim();
    if (!phone) {
      this.showError('يرجى إدخال رقم الهاتف أولًا');
      return;
    }

    this.isLoading.set(true);
    this.userService.checkParentPhone(phone).subscribe({
      next: res => {
        this.parentPhoneCheckResult.set(res.data ?? null);
        this.isLoading.set(false);
      },
      error: err => {
        this.showError(this.extractErrorMessage(err, 'تعذر فحص رقم الهاتف'));
        this.isLoading.set(false);
      }
    });
  }

  generateParentCredentials() {
    this.formRole.set('Parent');
    this.selectedChildren.set([]);
    this.showCreateModal.set(true);
  }

  copyToClipboard(text: string) {
    navigator.clipboard.writeText(text)
      .then(() => this.showSuccess('تم النسخ'))
      .catch(() => this.showError('تعذر النسخ إلى الحافظة'));
  }

  printSingleCredentials() {
    const credential = this.lastGeneratedCred();
    if (!credential) {
      return;
    }

    const html = `
      <html lang="ar" dir="rtl">
        <head>
          <title>بيانات دخول الطالب</title>
          <style>
            body { font-family: Arial, sans-serif; padding: 24px; direction: rtl; }
            .card { border: 1px solid #e5e7eb; border-radius: 16px; padding: 24px; max-width: 520px; margin: 0 auto; }
            h1 { margin-top: 0; color: #111827; }
            .row { margin-bottom: 12px; }
            .label { color: #6b7280; font-size: 13px; margin-bottom: 4px; }
            .value { font-size: 18px; color: #111827; font-weight: 700; }
          </style>
        </head>
        <body>
          <div class="card">
            <h1>بيانات دخول الطالب</h1>
            <div class="row"><div class="label">اسم الطالب</div><div class="value">${this.escapeHtml(credential.studentName)}</div></div>
            <div class="row"><div class="label">اسم المستخدم</div><div class="value">${this.escapeHtml(credential.generatedUsername)}</div></div>
            <div class="row"><div class="label">كلمة المرور</div><div class="value">${this.escapeHtml(credential.plainPassword)}</div></div>
          </div>
        </body>
      </html>`;

    this.printHtmlDocument(html);
  }

  printBulkCredentials() {
    const bulk = this.bulkResult();
    if (!bulk) {
      return;
    }

    const rows = bulk.results.map(result => `
      <tr>
        <td>${this.escapeHtml(result.studentName || 'غير معروف')}</td>
        <td>${this.escapeHtml(result.generatedUsername || '—')}</td>
        <td>${this.escapeHtml(result.plainPassword || '—')}</td>
        <td>${result.success ? 'تم' : this.escapeHtml(result.errorMessage || 'فشل')}</td>
      </tr>
    `).join('');

    const html = `
      <html lang="ar" dir="rtl">
        <head>
          <title>تقرير بيانات الدخول</title>
          <style>
            body { font-family: Arial, sans-serif; padding: 24px; direction: rtl; }
            table { width: 100%; border-collapse: collapse; margin-top: 16px; }
            th, td { border: 1px solid #e5e7eb; padding: 10px; text-align: right; }
            th { background: #f9fafb; }
            h1 { margin-top: 0; color: #111827; }
            .summary { display: flex; gap: 16px; margin-top: 16px; }
          </style>
        </head>
        <body>
          <h1>تقرير توليد حسابات الطلاب</h1>
          <div class="summary">
            <div>إجمالي: ${bulk.totalRequested}</div>
            <div>نجح: ${bulk.successCount}</div>
            <div>فشل: ${bulk.failureCount}</div>
          </div>
          <table>
            <thead>
              <tr>
                <th>الطالب</th>
                <th>اسم المستخدم</th>
                <th>كلمة المرور</th>
                <th>الحالة</th>
              </tr>
            </thead>
            <tbody>${rows}</tbody>
          </table>
        </body>
      </html>`;

    this.printHtmlDocument(html);
  }

  getRoleLabel(role: string): string {
    switch (role.toLowerCase()) {
      case 'admin':
        return 'أدمن';
      case 'parent':
        return 'ولي أمر';
      case 'student':
        return 'حساب طالب';
      case 'teacher':
        return 'معلم';
      default:
        return role;
    }
  }

  getRelationshipLabel(relationship?: RelationshipType | null): string {
    switch (relationship) {
      case 1:
        return 'الأب';
      case 2:
        return 'الأم';
      case 3:
        return 'الوصي';
      case 4:
        return 'أخ / أخت';
      case 5:
        return 'أخرى';
      default:
        return 'غير محدد';
    }
  }

  canSaveAccount(): boolean {
    return !!this.formName() && !!this.formUsername() && !!this.formPassword() && !this.isLoading();
  }

  canUpdateAccount(): boolean {
    return !!this.formName() && !this.isLoading();
  }

  private saveAccountBasic() {
    if (!this.formName() || !this.formUsername() || !this.formPassword()) {
      this.showError('يرجى إدخال الاسم والبريد الإلكتروني وكلمة المرور');
      return;
    }

    this.isLoading.set(true);
    this.userService.createUser({
      fullName: this.formName(),
      username: this.formUsername(),
      contactEmail: undefined,
      password: this.formPassword(),
      phone: this.formPhone() || undefined,
      role: this.formRole()
    }).subscribe({
      next: () => {
        this.showSuccess('تم إنشاء الحساب بنجاح');
        this.closeModals();
        this.loadUsers();
      },
      error: err => {
        this.showError(this.extractErrorMessage(err, 'تعذر إنشاء الحساب'));
        this.isLoading.set(false);
      }
    });
  }

  private saveParentWithStudents() {
    if (!this.formName() || !this.formUsername() || !this.formPassword()) {
      this.showError('يرجى إدخال جميع البيانات المطلوبة');
      return;
    }

    this.isLoading.set(true);
    this.userService.createParentWithStudents({
      fullName: this.formName(),
      username: this.formUsername(),
      contactEmail: this.formUsername(),
      password: this.formPassword(),
      phone: this.formPhone() || undefined,
      children: this.selectedChildren().map(child => ({
        studentId: child.student.id,
        relationship: child.relationship
      }))
    }).subscribe({
      next: res => {
        const data = res.data;
        const message = data && data.failedCount > 0
          ? `تم إنشاء الحساب، ربط ${data.linkedCount} طالب، وفشل ${data.failedCount}`
          : `تم إنشاء الحساب وربط ${data?.linkedCount ?? 0} طالب بنجاح`;

        this.isLoading.set(false);
        this.showSuccess(message);
        this.closeModals();
        this.loadUsers();
      },
      error: err => {
        this.showError(this.extractErrorMessage(err, 'تعذر إنشاء الحساب'));
        this.isLoading.set(false);
      }
    });
  }

  private resetForm() {
    this.formName.set('');
    this.formUsername.set('');
    this.formPassword.set('');
    this.formPhone.set('');
    this.formRole.set('Parent');
    this.selectedChildren.set([]);
    this.parentChildSearch.set('');
    this.parentChildSearchResults.set([]);
  }

  private toManagedRole(role: string): ManagedRole {
    switch (role.toLowerCase()) {
      case 'admin':
        return 'Admin';
      case 'student':
        return 'Student';
      default:
        return 'Parent';
    }
  }

  private extractErrorMessage(err: unknown, fallback: string): string {
    const httpError = err as any;
    if (httpError?.error?.message) {
      return httpError.error.message;
    }
    if (httpError?.error?.errors) {
      const errors = httpError.error.errors;
      const firstKey = Object.keys(errors)[0];
      if (firstKey && Array.isArray(errors[firstKey]) && errors[firstKey].length > 0) {
        return errors[firstKey][0];
      }
    }
    if (typeof httpError?.error === 'string') {
      return httpError.error;
    }
    return httpError?.message || fallback;
  }

  private escapeHtml(value: string): string {
    return value
      .replaceAll('&', '&amp;')
      .replaceAll('<', '&lt;')
      .replaceAll('>', '&gt;')
      .replaceAll('"', '&quot;')
      .replaceAll("'", '&#39;');
  }

  private printHtmlDocument(html: string) {
    const printWindow = window.open('', '_blank', 'width=900,height=700');
    if (!printWindow) {
      this.showError('تعذر فتح نافذة الطباعة');
      return;
    }

    printWindow.document.open();
    printWindow.document.write(html);
    printWindow.document.close();
    printWindow.focus();
    printWindow.print();
  }

  private showError(message: string) {
    this.errorMessage.set(message);
    this.successMessage.set(null);
    setTimeout(() => this.errorMessage.set(null), 5000);
  }

  private showSuccess(message: string) {
    this.successMessage.set(message);
    this.errorMessage.set(null);
    setTimeout(() => this.successMessage.set(null), 3000);
  }
}
