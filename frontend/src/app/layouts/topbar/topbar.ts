import { Component, input, inject, computed, OnInit, HostListener, signal, effect } from '@angular/core';
import { Router } from '@angular/router';
import { RoleService } from '../../shared/role.service';
import { ROLE_MENUS, SidebarMenuItem } from '../../shared/menus';
import { NotificationService } from '../../core/services/notification.service';
import { AuthService } from '../../core/services/auth.service';
import { SearchService } from '../../core/services/search.service';
import { NotificationSignalRService } from '../../core/services/notification-signalr.service';
import { UserService } from '../../core/services/user.service';

@Component({
  selector: 'app-topbar',
  imports: [],
  templateUrl: './topbar.html',
  styleUrl: './topbar.css'
})
export class Topbar implements OnInit {
  private roleService = inject(RoleService);
  private notifService = inject(NotificationService);
  private authService = inject(AuthService);
  private searchService = inject(SearchService);
  private notifSignalR = inject(NotificationSignalRService);
  private userService = inject(UserService);
  router = inject(Router);
  userName = computed(() => this.authService.user()?.fullName || 'أحمد');
  searchQuery = this.searchService.query;
  showSearch = input(true);
  showNotifications = input(true);
  showSpark = input(true);
  /** Show settings only for admin role */
  showSettings = computed(() => {
    const role = this.roleService.currentRole();
    return role === 'admin';
  });
  showAvatar = input(true);
  notificationCount = input(0);
  searchOpen = signal(false);

  toggleSidebar() {
    document.getElementById('menuToggle')?.click();
  }

  profilePictureUrl = signal<string | null>(null);
  isLoadingAvatar = signal(false);

  displayName = computed(() => this.authService.user()?.fullName || this.userName());
  displayAvatar = computed(() => this.authService.user()?.profilePictureUrl || this.profilePictureUrl());

  userRole = computed(() => {
    const roleLabels: Record<string, string> = {
      admin: 'مدير النظام',
      teacher: 'مدرس',
      student: 'طالب',
      parent: 'ولي أمر',
    };
    const role = this.roleService.currentRole();
    return role ? roleLabels[role] ?? '' : '';
  });

  /** Flatten sections into a single items array for search & navigation lookup */
  private flatItems = computed(() => {
    const role = this.roleService.currentRole();
    const sections = role ? (ROLE_MENUS[role] ?? []) : [];
    const items: SidebarMenuItem[] = [];
    for (const section of sections) {
      for (const item of section.items) {
        items.push(item);
      }
    }
    return items;
  });

  navItems = computed(() => {
    const items = this.flatItems();
    const homeItem = items[0];
    const chatItem = items.find((i: SidebarMenuItem) => i.icon === 'chat');
    const notifItem = items.find((i: SidebarMenuItem) => i.icon === 'notifications');
    return { home: homeItem, chat: chatItem, notif: notifItem };
  });

  homeRoute = computed(() => this.navItems().home?.route || '/');

  realNotifCount = computed(() => this.notifService.unreadCount());

  searchResults = computed(() => {
    const q = this.searchQuery().trim().toLowerCase();
    if (!q) return [];

    const items = this.flatItems();
    if (!items.length) return [];

    return items
      .map(item => ({
        ...item,
        score: this.matchScore(item.label, q),
      }))
      .filter(x => x.score > 0)
      .sort((a, b) => b.score - a.score)
      .slice(0, 8);
  });

  private matchScore(label: string, query: string): number {
    const l = label.toLowerCase();
    if (l === query) return 100;
    if (l.startsWith(query)) return 80;
    if (l.includes(` ${query}`) || l.includes(query)) return 60;
    let matches = 0;
    let qi = 0;
    for (const ch of l) {
      if (qi < query.length && ch === query[qi]) {
        matches++;
        qi++;
      }
    }
    return qi > 0 ? (matches / query.length) * 40 : 0;
  }

  ngOnInit() {
    const user = this.authService.user();
    if (user) {
      this.notifService.getUnreadCount(user.userId).subscribe();
      this.loadProfilePicture();
    }

    // Start SignalR connection for real-time notifications
    this.notifSignalR.startConnection();
  }

  private loadProfilePicture() {
    const user = this.authService.user();
    if (!user) return;

    this.isLoadingAvatar.set(true);
    this.userService.getMyProfile().subscribe({
      next: res => {
        const data = res.data;
        this.profilePictureUrl.set(data?.profilePictureUrl ?? null);
        if (data?.profilePictureUrl || data?.fullName) {
          this.authService.updateUserInfo(
            data.fullName || user.fullName,
            data.profilePictureUrl
          );
        }
        this.isLoadingAvatar.set(false);
      },
      error: () => {
        this.profilePictureUrl.set(null);
        this.isLoadingAvatar.set(false);
      }
    });
  }

  constructor() {
    // React to new notifications from SignalR
    effect(() => {
      const notif = this.notifSignalR.newNotification();
      if (notif) {
        this.notifService.unreadCount.update(c => c + 1);
      }
    });
  }

  navigate(route: string) {
    this.searchOpen.set(false);
    this.searchQuery.set('');
    this.router.navigate([route]);
  }

  onSearchInput(event: Event) {
    const value = (event.target as HTMLInputElement).value;
    this.searchQuery.set(value);
    this.searchOpen.set(value.trim().length > 0);
  }

  onSearchFocus() {
    if (this.searchQuery().trim()) {
      this.searchOpen.set(true);
    }
  }

  clearSearch(input: HTMLInputElement) {
    input.value = '';
    this.searchQuery.set('');
    this.searchOpen.set(false);
    input.focus();
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent) {
    const target = event.target as HTMLElement;
    if (!target.closest('.search-box')) {
      this.searchOpen.set(false);
    }
  }

  @HostListener('window:scroll', [])
  onWindowScroll() {
    const header = document.querySelector('.topbar');
    if (header) {
      header.classList.toggle('scrolled', window.scrollY > 10);
    }
  }
}
