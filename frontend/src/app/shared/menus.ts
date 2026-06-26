export interface SidebarMenuItem {
  label: string;
  icon: string;
  route: string;
}

export interface SidebarMenuSection {
  title: string;
  items: SidebarMenuItem[];
}

// ─────────────────────────────────────────────
// قائمة المدير
// ─────────────────────────────────────────────
export const ADMIN_MENU: SidebarMenuSection[] = [
  {
    title: 'الرئيسية',
    items: [
      { label: 'لوحة القيادة',    icon: 'dashboard',      route: '/admin'       },
      { label: 'تحليل الأداء',    icon: 'analytics',      route: '/analysis' },
      { label: 'تحليل هيكل الكتاب',icon: 'auto_stories',  route: '/book-parser' },
      { label: 'حسابي',           icon: 'account_circle', route: '/profile'     },
    ],
  },
  {
    title: 'الشؤون الأكاديمية',
    items: [
      { label: 'الجدول الدراسي',  icon: 'calendar_month',            route: '/admin-schedule'     },
      { label: 'التعيينات',       icon: 'assignment_ind',            route: '/teacher-assignments'},
      { label: 'إدارة الفصول',    icon: 'meeting_room',              route: '/class-management'   },
      { label: 'إدارة المواد',    icon: 'menu_book',                 route: '/subject-management' },
      { label: 'إدارة القاعات',   icon: 'door_open',                 route: '/room-management'    },
      { label: 'إدارة الامتحانات',icon: 'fact_check',                route: '/exam-management'    },
      { label: 'الشهادات',       icon: 'badge',                     route: '/certificate-management' },
      { label: 'تقييم الحصص',     icon: 'feedback',                  route: '/lesson-feedback'    },
    ],
  },
  {
    title: 'التقارير',
    items: [
      { label: 'التقارير الأكاديمية', icon: 'auto_awesome',   route: '/reports-academic' },
      { label: 'التقارير التدريبية',  icon: 'model_training', route: '/reports-training' },
    ],
  },
  {
    title: 'إدارة المستخدمين',
    items: [
      { label: 'إدارة الحسابات',   icon: 'manage_accounts',          route: '/account-management' },
      { label: 'إدارة المعلمين',   icon: 'person_add',               route: '/teacher-management' },
      { label: 'إدارة الطلاب',     icon: 'school',                   route: '/student-management' },
      { label: 'استيراد الطلاب',   icon: 'file_upload',              route: '/import-students'    },
      { label: 'نقل طالب',         icon: 'transfer_within_a_station',route: '/transfer-student'   },
      { label: 'ترقية نهاية العام',icon: 'school',                   route: '/student-progression'},
    ],
  },
  {
    title: 'التواصل والإعلام',
    items: [
      { label: 'الإعلانات',        icon: 'campaign',      route: '/announcements'  },
      { label: 'طلبات الاجتماعات', icon: 'meeting_room',  route: '/admin-meetings' },
      { label: 'الإشعارات',        icon: 'notifications', route: '/notifications'  },
      { label: 'المحادثات',        icon: 'chat',          route: '/chat'           },
      { label: 'المكتبة الرقمية',  icon: 'library_books', route: '/digital-library'},
    ],
  },
  {
    title: 'الإعدادات',
    items: [
      { label: 'إعدادات النظام', icon: 'settings', route: '/settings' },
    ],
  },
];

// ─────────────────────────────────────────────
// قائمة المعلم
// ─────────────────────────────────────────────
export const TEACHER_MENU: SidebarMenuSection[] = [
  {
    title: 'الرئيسية',
    items: [
      { label: 'لوحة القيادة',    icon: 'dashboard',      route: '/teacher'     },
      { label: 'تحليل هيكل الكتاب',icon: 'auto_stories',  route: '/book-parser' },
      { label: 'حسابي',           icon: 'account_circle', route: '/profile'     },
    ],
  },
  {
    title: 'الشؤون الأكاديمية',
    items: [
      { label: 'فصولي',        icon: 'groups',         route: '/class-analysis'  },
      { label: 'جدول الحصص',   icon: 'calendar_month', route: '/teacher-schedule'},
      { label: 'رصد الدرجات',  icon: 'grading',        route: '/grade-monitor'   },
      { label: 'تقييم الحصص',  icon: 'feedback',       route: '/lesson-feedback' },
    ],
  },
  {
    title: 'المحتوى التعليمي',
    items: [
      { label: 'إدارة الامتحانات',icon: 'fact_check',     route: '/exam-management'      },
      { label: 'إنشاء امتحان',    icon: 'quiz',           route: '/exam-generator'       },
      { label: 'بنك الأسئلة',     icon: 'database',       route: '/question-bank'        },
      { label: 'إدارة الواجبات',  icon: 'assignment_add', route: '/assignment-management'},
      { label: 'المكتبة الرقمية', icon: 'library_books',  route: '/digital-library'      },
    ],
  },
  {
    title: 'التقارير',
    items: [
      { label: 'التقارير التدريبية', icon: 'model_training', route: '/reports-training' },
    ],
  },
  {
    title: 'التواصل',
    items: [
      { label: 'المحادثات', icon: 'chat',          route: '/chat'         },
      { label: 'الإشعارات', icon: 'notifications', route: '/notifications' },
    ],
  },
];

// ─────────────────────────────────────────────
// قائمة ولي الأمر
// ─────────────────────────────────────────────
export const PARENT_MENU: SidebarMenuSection[] = [
  {
    title: 'الرئيسية',
    items: [
      { label: 'لوحة القيادة', icon: 'dashboard',      route: '/parent'  },
      { label: 'حسابي',        icon: 'account_circle', route: '/profile' },
    ],
  },
  {
    title: 'متابعة الأبناء',
    items: [
      { label: 'جداول الأبناء', icon: 'calendar_month',   route: '/parent-schedule'},
      { label: 'متابعة ابني',   icon: 'supervisor_account',route: '/child-progress' },
      { label: 'التقارير',      icon: 'description',       route: '/reports'        },
      { label: 'طلب اجتماع',   icon: 'meeting_room',      route: '/parent-meeting-request' },
    ],
  },
  {
    title: 'التواصل',
    items: [
      { label: 'المحادثات', icon: 'chat',          route: '/chat'         },
      { label: 'الإشعارات', icon: 'notifications', route: '/notifications' },
    ],
  },
];

// ─────────────────────────────────────────────
// قائمة الطالب
// ─────────────────────────────────────────────
export const STUDENT_MENU: SidebarMenuSection[] = [
  {
    title: 'الرئيسية',
    items: [
      { label: 'لوحة القيادة', icon: 'dashboard',      route: '/student' },
      { label: 'حسابي',        icon: 'account_circle', route: '/profile' },
    ],
  },
  {
    title: 'شؤوني الدراسية',
    items: [
      { label: 'جدولي',       icon: 'calendar_month',    route: '/class-schedule' },
      { label: 'واجباتي',     icon: 'assignment',        route: '/my-assignments' },
      { label: 'امتحاناتي',   icon: 'quiz',              route: '/my-exams'       },
      { label: 'خطة المذاكرة',icon: 'calendar_view_month',route: '/study-planner' },
      { label: 'تقييم الحصص', icon: 'feedback',          route: '/lesson-feedback'},
    ],
  },
  {
    title: 'المحتوى التعليمي',
    items: [
      { label: 'المكتبة الرقمية', icon: 'library_books', route: '/digital-library' },
    ],
  },
  {
    title: 'التواصل',
    items: [
      { label: 'المحادثات', icon: 'chat',          route: '/chat'         },
      { label: 'الإشعارات', icon: 'notifications', route: '/notifications' },
    ],
  },
];

// ─────────────────────────────────────────────
// Map الأدوار
// ─────────────────────────────────────────────
export const ROLE_MENUS: Record<string, SidebarMenuSection[]> = {
  admin:   ADMIN_MENU,
  teacher: TEACHER_MENU,
  parent:  PARENT_MENU,
  student: STUDENT_MENU,
};
