using Microsoft.EntityFrameworkCore;
using Project.BLL.Utils;
using Project.DAL.Context;
using Project.Domain.Entities;
using Project.Domain.Enums;

namespace Project.API.Extensions;

/// <summary>
/// One-time database seeder.
/// Runs on every startup but exits immediately if Users table already has data.
/// Covers every table in the schema with at least one record.
/// </summary>
public static class SeedData
{
    public static async Task Initialize(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // ── Apply any pending migrations ───────────────────────────────────
        try
        {
            if (await ctx.Database.CanConnectAsync())
            {
                var applied = (await ctx.Database.GetAppliedMigrationsAsync()).ToList();
                if (!applied.Any())
                    await ctx.Database.EnsureDeletedAsync();
            }
            await ctx.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Migration skipped — likely running in EF Core design-time mode");
            return;
        }

        // ── Guard: seed only once ──────────────────────────────────────────
        bool hasExistingData = await ctx.Users.IgnoreQueryFilters().AnyAsync();
        if (hasExistingData)
        {
            // Database has old data — run upgrade seeding for new sections
            // FIX: الـ guard ده بيتأكد من Users بس، فلو جدول AcademicYears اتفضّى لأي سبب
            // (تنظيف بيانات جزئي، اختبار، migration ناقص) بينما Users لسه فيه بيانات،
            // كان السيستم هيفضل بدون أي سنة دراسية للأبد — وده بيقفل أي صفحة بتعتمد عليها
            // زي بناء الجداول الدراسية. الدالة دي شبكة أمان تتأكد دايمًا من وجود سنة حالية.
            await EnsureCurrentAcademicYearExists(ctx);
            await SeedCurrentWeekActivity(ctx);
            await SeedUpgrades(ctx);
            return;
        }

        var now = DateTime.UtcNow;
        var rng = new Random(42);

        // ── timezone helper ─────────────────────────────────────────────
        // كل المواعيد بتتخزن UTC (نفس إصلاح التوقيت المطبّق على الـ services).
        // الـ helper ده بياخد وقت بتوقيت القاهرة + إزاحة بالأيام ويرجّع UTC
        // → الامتحانات والواجبات بتبقى بمواعيد مدرسية واقعية (مش حسب وقت تشغيل الـ seeder).
        var cairoZone = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "Egypt Standard Time" : "Africa/Cairo");
        DateTime CairoAt(int daysOffset, int hour, int minute)
        {
            var cairoToday = TimeZoneInfo.ConvertTimeFromUtc(now, cairoZone).Date;
            var localDt = cairoToday.AddDays(daysOffset).AddHours(hour).AddMinutes(minute);
            return TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(localDt, DateTimeKind.Unspecified), cairoZone);
        }

        // =================================================================
        // 1. AcademicYear
        // =================================================================
        var year = new AcademicYear
        {
            Name      = "2025/2026",
            StartDate = new DateOnly(2025, 9, 21),
            EndDate   = new DateOnly(2026, 7, 11),
            IsCurrent = true,
            FirstSemesterStartDate = new DateOnly(2025, 9, 21),
            FirstSemesterEndDate   = new DateOnly(2026, 1, 22),
            SecondSemesterStartDate = new DateOnly(2026, 4, 4),
            SecondSemesterEndDate   = new DateOnly(2026, 7, 11),
            CreatedAt = now, UpdatedAt = now
        };
        ctx.AcademicYears.Add(year);
        await ctx.SaveChangesAsync();

        // =================================================================
        // 2. SchoolProfile
        // =================================================================
        ctx.SchoolProfiles.Add(new SchoolProfile
        {
            SchoolName                = "مدرسة النيل الإعدادية بنين",
            Governorate               = "محافظة الجيزة",
            Directorate               = "مديرية التربية والتعليم بالجيزة",
            EducationalAdministration = "إدارة الجيزة التعليمية",
            Address    = "شارع النيل - وسط الجيزة",
            Phone      = "02-37654321",
            Email      = "nile.prep@sch.ed.eg",
            ManagerName= "أ/ محمد عبد الرحمن",
            IsActive   = true,
            CreatedAt = now, UpdatedAt = now
        });
        await ctx.SaveChangesAsync();

        // =================================================================
        // 3. GradeLevel
        // =================================================================
        var grade1 = new GradeLevel
        {
            Name = "الصف الأول الإعدادي", LevelOrder = 1, Stage = "إعدادي",
            CreatedAt = now, UpdatedAt = now
        };
        var grade2 = new GradeLevel
        {
            Name = "الصف الثاني الإعدادي", LevelOrder = 2, Stage = "إعدادي",
            CreatedAt = now, UpdatedAt = now
        };
        var grade3 = new GradeLevel
        {
            Name = "الصف الثالث الإعدادي", LevelOrder = 3, Stage = "إعدادي",
            CreatedAt = now, UpdatedAt = now
        };
        ctx.GradeLevels.AddRange(grade1, grade2, grade3);
        await ctx.SaveChangesAsync();

        // =================================================================
        // 4. Subjects  (7 subjects)
        // =================================================================
        var subjectDefs = new (string name, string code)[]
        {
            ("اللغة العربية",       "ARB"),
            ("اللغة الإنجليزية",    "ENG"),
            ("الرياضيات",           "MTH"),
            ("العلوم",              "SCI"),
            ("الدراسات الاجتماعية","SOC"),
            ("التربية الإسلامية",  "ISL"),
            ("الحاسب الآلي",       "CMP"),
        };
        var subjectEntities = subjectDefs
            .Select(d => new Subject { Name = d.name, Code = d.code, CreatedAt = now, UpdatedAt = now })
            .ToList();
        ctx.Subjects.AddRange(subjectEntities);
        await ctx.SaveChangesAsync();

        // Quick lookup: code → entity
        var S = subjectEntities.ToDictionary(s => s.Code!, s => s);

        // =================================================================
        // 5. Users — Admin
        // =================================================================
        var admin = new User
        {
            FullName = "Super Admin", Username = "admin",
            ContactEmail = "admin@school.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
            Role = UserRole.Admin, IsActive = true,
            CreatedAt = now, UpdatedAt = now
        };
        var director = new User
        {
            FullName = "مدير المدرسة", Username = "director",
            ContactEmail = "director@school.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Director@123"),
            Role = UserRole.Admin, IsActive = true,
            CreatedAt = now, UpdatedAt = now
        };
        ctx.Users.AddRange(admin, director);
        await ctx.SaveChangesAsync();

        // =================================================================
        // 6. Users — Teachers (one per subject)
        // =================================================================
        var teacherDefs = new (string fullName, string username, string email, string code)[]
        {
            ("أحمد علي حسن",        "ahmed.ali",       "ahmed.ali@school.com",       "ARB"),
            ("سارة محمود عبد الله", "sara.mahmoud",    "sara.mahmoud@school.com",    "ENG"),
            ("محمد كمال الدين",     "mohamed.kamal",   "mohamed.kamal@school.com",   "MTH"),
            ("إيمان عبد الفتاح",    "eman.abdelfattah","eman.abdelfattah@school.com","SCI"),
            ("خالد رشاد عثمان",     "khaled.reshad",   "khaled.reshad@school.com",   "SOC"),
            ("نادية جابر سليمان",   "nadia.gaber",     "nadia.gaber@school.com",     "ISL"),
            ("أيمن شعبان مصطفى",    "ayman.shaban",    "ayman.shaban@school.com",    "CMP"),
        };
        var teacherUsers = teacherDefs.Select(d => new User
        {
            FullName = d.fullName, Username = d.username, ContactEmail = d.email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Teacher@123"),
            Role = UserRole.Teacher, IsActive = true,
            CreatedAt = now, UpdatedAt = now
        }).ToList();
        ctx.Users.AddRange(teacherUsers);
        await ctx.SaveChangesAsync();

        // code → teacher User
        var T = teacherDefs.Zip(teacherUsers, (d, u) => (d.code, user: u))
                           .ToDictionary(x => x.code, x => x.user);

        // =================================================================
        // 7. Rooms
        // =================================================================
        var roomDefs = new (string name, string type, int cap)[]
        {
            ("فصل أ",               "Classroom", 35),
            ("فصل ب",               "Classroom", 35),
            ("فصل ج",               "Classroom", 35),
            ("فصل د",               "Classroom", 35),
            ("فصل هـ",              "Classroom", 35),
            ("فصل و",               "Classroom", 35),
            ("معمل علوم",           "Lab",       30),
            ("معمل حاسب",           "Lab",       30),
            ("قاعة متعددة الأغراض","Hall",      100),
        };
        var rooms = roomDefs.Select(d => new Room
        {
            Name = d.name, Type = d.type, Capacity = d.cap,
            CreatedAt = now, UpdatedAt = now
        }).ToList();
        ctx.Rooms.AddRange(rooms);
        await ctx.SaveChangesAsync();

        // =================================================================
        // 8. Classes  (2 classes, same grade)
        // =================================================================
        var class1 = new SchoolClass
        {
            GradeLevelId = grade1.Id, AcademicYearId = year.Id,
            Name = "1/1", CreatedAt = now, UpdatedAt = now
        };
        var class2 = new SchoolClass
        {
            GradeLevelId = grade1.Id, AcademicYearId = year.Id,
            Name = "1/2", CreatedAt = now, UpdatedAt = now
        };
        ctx.Classes.AddRange(class1, class2);
        await ctx.SaveChangesAsync();

        // =================================================================
        // 8b. Classes — grade2 & grade3
        // =================================================================
        var class3 = new SchoolClass
        {
            GradeLevelId = grade2.Id, AcademicYearId = year.Id,
            Name = "2/1", CreatedAt = now, UpdatedAt = now
        };
        var class4 = new SchoolClass
        {
            GradeLevelId = grade2.Id, AcademicYearId = year.Id,
            Name = "2/2", CreatedAt = now, UpdatedAt = now
        };
        var class5 = new SchoolClass
        {
            GradeLevelId = grade3.Id, AcademicYearId = year.Id,
            Name = "3/1", CreatedAt = now, UpdatedAt = now
        };
        var class6 = new SchoolClass
        {
            GradeLevelId = grade3.Id, AcademicYearId = year.Id,
            Name = "3/2", CreatedAt = now, UpdatedAt = now
        };
        ctx.Classes.AddRange(class3, class4, class5, class6);
        await ctx.SaveChangesAsync();

        // =================================================================
        // 9. TeacherSubjects + ClassSubjectTeachers
        // =================================================================
        var weeklyPeriods = new Dictionary<string, int>
        {
            ["ARB"] = 6, ["ENG"] = 4, ["MTH"] = 5,
            ["SCI"] = 3, ["SOC"] = 3, ["ISL"] = 2, ["CMP"] = 2
        };

        // TeacherSubject — one row per (teacher, subject)
        foreach (var (code, teacher) in T)
        {
            ctx.TeacherSubjects.Add(new TeacherSubject
            {
                TeacherId = teacher.Id, SubjectId = S[code].Id,
                CreatedAt = now, UpdatedAt = now
            });
        }
        await ctx.SaveChangesAsync();

        // ClassSubjectTeacher — each teacher × each class
        var cstList = new List<ClassSubjectTeacher>();
        foreach (var cls in new[] { class1, class2, class3, class4, class5, class6 })
        {
            foreach (var (code, teacher) in T)
            {
                var cst = new ClassSubjectTeacher
                {
                    ClassId = cls.Id, SubjectId = S[code].Id,
                    TeacherId = teacher.Id, AcademicYearId = year.Id,
                    WeeklyPeriods = weeklyPeriods[code],
                    CreatedAt = now, UpdatedAt = now
                };
                ctx.ClassSubjectTeachers.Add(cst);
                cstList.Add(cst);
            }
        }
        await ctx.SaveChangesAsync();

        // =================================================================
        // 10. Timetables + TimetableSlots (الصف الأول الإعدادي: فصلان)
        // =================================================================
        // توقيت الحصص اليومي (7 حصص). ملاحظة: فيه فاصل طابور ضمني بين الحصة 3 و 4.
        var schoolDays = new[] { SchoolDay.Sunday, SchoolDay.Monday, SchoolDay.Tuesday, SchoolDay.Wednesday, SchoolDay.Thursday };
        var slotTimes  = new (TimeOnly start, TimeOnly end)[]
        {
            (new TimeOnly(8,  0), new TimeOnly(8,  45)),
            (new TimeOnly(8, 45), new TimeOnly(9,  30)),
            (new TimeOnly(9, 30), new TimeOnly(10, 15)),
            (new TimeOnly(10,30), new TimeOnly(11, 15)), // بعد فاصل الطابور
            (new TimeOnly(11,15), new TimeOnly(12,  0)),
            (new TimeOnly(12,  0), new TimeOnly(12, 45)),
            (new TimeOnly(12,45), new TimeOnly(13, 30)),
        };
        var subjectCodes = new[] { "ARB", "ENG", "MTH", "SCI", "SOC", "ISL", "CMP" };

        // الصف الأول الإعدادي: فصلان + قاعتهما الخاصة (تفادي تعارض القاعات).
        var grade1Classes = new[] { (cls: class1, room: rooms[0]), (cls: class2, room: rooms[1]) };

        // ClassId → (subjectId → cstId)  (مرة واحدة لتفادي استعلامات LINQ متكررة)
        var cstMaps = grade1Classes.ToDictionary(
            x => x.cls.Id,
            x => cstList.Where(c => c.ClassId == x.cls.Id)
                        .ToDictionary(c => c.SubjectId, c => c.Id));

        // ── أنشئ Timetable واحد منشور لكل فصل ────────────────────────────
        var timetableByClass = new Dictionary<int, Timetable>();
        foreach (var (cls, _) in grade1Classes)
        {
            var tt = new Timetable
            {
                ClassId = cls.Id, AcademicYearId = year.Id,
                Status = TimetableStatus.Active,
                VersionNumber = 1,
                PublishedAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };
            ctx.Timetables.Add(tt);
            timetableByClass[cls.Id] = tt;
        }
        await ctx.SaveChangesAsync();

        // ── خوارزمية جشعة متوازنة لجدولة الحصص بلا أي تعارض ──────────────
        // القيود المضمونة:
        //   1) تعارض المعلم: في كل (يوم، حصة) كل معلم في فصل واحد فقط (busyTeachers).
        //   2) تعارض القاعة: كل فصل في قاعته الخاصة دائمًا.
        //   3) WeeklyPeriods: نختار المادة الأكثر احتياجًا (longest-remaining-first)
        //      لضمان توزيع متوازن وتحقيق عدد الحصص المطلوب لكل مادة.
        var remaining = grade1Classes.ToDictionary(
            x => x.cls.Id,
            x => subjectCodes.ToDictionary(c => c, c => weeklyPeriods[c]));

        var allSlots = new List<TimetableSlot>();

        for (int d = 0; d < schoolDays.Length; d++)
        {
            for (int p = 0; p < slotTimes.Length; p++)
            {
                // المعلمون المشغولون في هذه الفتحة الزمنية عبر كل الفصول
                var busyTeachers = new HashSet<int>();

                foreach (var (cls, room) in grade1Classes)
                {
                    // اختر المادة الأكثر احتياجًا ومعلمها غير مشغول في هذه الفتحة
                    var code = subjectCodes
                        .Where(c => remaining[cls.Id][c] > 0 && !busyTeachers.Contains(T[c].Id))
                        .OrderByDescending(c => remaining[cls.Id][c])
                        .FirstOrDefault();

                    if (code is null) continue; // فتحة فارغة (راحة)

                    remaining[cls.Id][code]--;
                    busyTeachers.Add(T[code].Id);

                    allSlots.Add(new TimetableSlot
                    {
                        TimetableId           = timetableByClass[cls.Id].Id,
                        DayOfWeek             = schoolDays[d],
                        PeriodNumber          = p + 1,
                        StartTime             = slotTimes[p].start,
                        EndTime               = slotTimes[p].end,
                        ClassSubjectTeacherId = cstMaps[cls.Id][S[code].Id],
                        RoomId                = room.Id,
                        CreatedAt = now, UpdatedAt = now
                    });
                }
            }
        }

        ctx.TimetableSlots.AddRange(allSlots);
        await ctx.SaveChangesAsync();

        // =================================================================
        // 11. EvaluationPeriods
        // =================================================================
        var periodsList = Project.Domain.Helpers.EvaluationPeriodGenerator
            .GeneratePeriods(year.Id, year.StartDate,
                year.FirstSemesterStartDate, year.FirstSemesterEndDate,
                year.SecondSemesterStartDate, year.SecondSemesterEndDate)
            .ToList();
        ctx.EvaluationPeriods.AddRange(periodsList);
        await ctx.SaveChangesAsync();

        // =================================================================
        // 12. EvaluationTemplates — all 7 subjects × 3 grade levels = 21 templates
        // =================================================================
        var subjectNames = new Dictionary<string, string>
        {
            ["ARB"] = "اللغة العربية", ["ENG"] = "اللغة الإنجليزية", ["MTH"] = "الرياضيات",
            ["SCI"] = "العلوم", ["SOC"] = "الدراسات الاجتماعية", ["ISL"] = "التربية الإسلامية", ["CMP"] = "الحاسب الآلي"
        };
        var gradeLevels = new[] { (level: grade1, suffix: "الأول الإعدادي"), (level: grade2, suffix: "الثاني الإعدادي"), (level: grade3, suffix: "الثالث الإعدادي") };

        var allTemplates = new List<EvaluationTemplate>();
        foreach (var (grade, suffix) in gradeLevels)
        {
            foreach (var code in subjectCodes)
            {
                allTemplates.Add(new EvaluationTemplate
                {
                    GradeLevelId = grade.Id,
                    SubjectId = S[code].Id,
                    AcademicYearId = year.Id,
                    Name = $"تقييم {subjectNames[code]} - {suffix}",
                    CalculationType = EvaluationCalculationType.MiddleSchool,
                    IsActive = true,
                    Weeks = 14,
                    Term = AcademicTerm.SecondSemester,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }
        ctx.EvaluationTemplates.AddRange(allTemplates);
        await ctx.SaveChangesAsync();

        // Build a lookup: (GradeLevelId, SubjectId) → Template
        var templateLookup = allTemplates.ToDictionary(t => (t.GradeLevelId, t.SubjectId), t => t);

        // ── EvaluationItems for every template (3 items each) ──────────
        var allEvalItems = new List<EvaluationItem>();
        foreach (var (grade, _) in gradeLevels)
        {
            foreach (var code in subjectCodes)
            {
                var tmpl = templateLookup[(grade.Id, S[code].Id)];
                allEvalItems.AddRange(BuildItems(tmpl.Id, now, new[]
                {
                    ("الأداء المنزلي",    10m, ItemType.Number, AutoCalcType.None),
                    ("التقييم الأسبوعي", 20m, ItemType.Number, AutoCalcType.None),
                    ("السلوك والمواظبة",  10m, ItemType.Number, AutoCalcType.Attendance),
                }));
            }
        }
        ctx.EvaluationItems.AddRange(allEvalItems);
        await ctx.SaveChangesAsync();

        // =================================================================
        // 13. ClassTemplateLinks  (each class × all 7 subject templates)
        // =================================================================
        var classGradeMap = new (SchoolClass cls, GradeLevel grade)[]
        {
            (class1, grade1), (class2, grade1),
            (class3, grade2), (class4, grade2),
            (class5, grade3), (class6, grade3),
        };
        foreach (var (cls, grade) in classGradeMap)
        {
            foreach (var code in subjectCodes)
            {
                var tmpl = templateLookup[(grade.Id, S[code].Id)];
                ctx.ClassTemplateLinks.Add(new ClassTemplateLink
                {
                    ClassId = cls.Id,
                    TemplateId = tmpl.Id,
                    AcademicYearId = year.Id,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }
        await ctx.SaveChangesAsync();

        // =================================================================
        // 14. Students (30 per class × 6 classes = 180) + User accounts + Enrollments
        // =================================================================
        var firstNames  = new[] { "أحمد","محمد","علي","عمر","خالد","يوسف","محمود","كريم","حسن","حسين","إبراهيم","عبد الله","أمير","بسام","جمال","حمزة","سامي","صالح","عادل","طارق","زياد","شادي","نادر","هاني","وائل","باهر","رامي","فادي","مازن","ياسر" };
        var middleNames = new[] { "عبد الرحمن","سامح","جابر","إسماعيل","فتحي","أنور","حمدي","رفعت","سمير","جلال","نبيل","كامل","رشاد","بهجت","نعيم","وجيه","قاسم","مبشر","رؤوف","وديع","بهاء","سعيد","لطفي","نزيه","هشام","أكرم","بطرس","ثروت","جورج","حبيب" };
        var lastNames   = new[] { "عبد العزيز","السيد","خليل","مرسي","فوزي","نصر","الشيخ","شحاتة","النجار","هاشم","الديب","رمضان","سلامة","بسيوني","عتريس","أبو زيد","الزهار","عرفة","جابر","خضر","زيتون","طنطاوي","عبده","غانم","قنديل","كمال","لقمة","موسى","نوح","هندي" };

        // ── Create Student rows ─────────────────────────────────────────
        var students = new List<Student>();
        int seq = 0;
        foreach (var cls in new[] { class1, class2, class3, class4, class5, class6 })
        {
            for (int i = 0; i < 30; i++)
            {
                var firstIdx = seq % 30;              // 0..29, rotates every student
                var middleIdx = (seq / 6) % 30;        // 0..29, rotates every 6 students
                var lastIdx = seq / 30;                // 0..5, rotates every 30 students
                seq++;
                var st = new Student
                {
                    FullName  = $"{firstNames[firstIdx]} {middleNames[middleIdx]} {lastNames[lastIdx]}",
                    Gender    = Gender.Male,
                    BirthDate = new DateOnly(2012, 1 + (seq % 12), 1 + (seq % 28)),
                    IsActive  = true,
                    CreatedAt = now, UpdatedAt = now
                };
                ctx.Students.Add(st);
                students.Add(st);
            }
        }
        await ctx.SaveChangesAsync();

        // ── Create Student User accounts (batch) ────────────────────────
        var studentUsers = students.Select(st => new User
        {
            FullName     = st.FullName,
            Username     = $"student{st.Id}",
            ContactEmail = $"student{st.Id}@school.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Student@123"),
            Role = UserRole.Student, IsActive = true,
            CreatedAt = now, UpdatedAt = now
        }).ToList();
        ctx.Users.AddRange(studentUsers);
        await ctx.SaveChangesAsync();

        // Link Student → User
        for (int i = 0; i < students.Count; i++)
            students[i].UserId = studentUsers[i].Id;
        await ctx.SaveChangesAsync();

        // ── Enrollments ─────────────────────────────────────────────────
        var enrollments = new List<StudentEnrollment>();
        seq = 0;
        foreach (var cls in new[] { class1, class2, class3, class4, class5, class6 })
        {
            for (int i = 0; i < 30; i++)
            {
                var enr = new StudentEnrollment
                {
                    StudentId      = students[seq].Id,
                    ClassId        = cls.Id,
                    AcademicYearId = year.Id,
                    EnrolledAt     = new DateOnly(2025, 9, 21),
                    CreatedAt = now, UpdatedAt = now
                };
                ctx.StudentEnrollments.Add(enr);
                enrollments.Add(enr);
                seq++;
            }
        }
        await ctx.SaveChangesAsync();

        // =================================================================
        // 15. Parents (one per student) + ParentStudents
        // =================================================================
        var parentUsers = students.Select(st => new User
        {
            FullName     = $"وليّ أمر {st.FullName}",
            Username     = $"parent{st.Id}",
            ContactEmail = $"parent{st.Id}@school.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Parent@123"),
            Role = UserRole.Parent, IsActive = true,
            CreatedAt = now, UpdatedAt = now
        }).ToList();
        ctx.Users.AddRange(parentUsers);
        await ctx.SaveChangesAsync();

        var parentStudentLinks = new List<ParentStudent>();
        for (int i = 0; i < students.Count; i++)
        {
            // parent1 (index 0) gets 2 children (students[0] + students[1]), parent2 gets students[2], etc.
            int parentIdx = i <= 1 ? 0 : i - 1;
            parentStudentLinks.Add(new ParentStudent
            {
                ParentId     = parentUsers[parentIdx].Id,
                StudentId    = students[i].Id,
                Relationship = RelationshipType.Father,
                CreatedAt = now, UpdatedAt = now
            });
        }
        ctx.ParentStudents.AddRange(parentStudentLinks);
        await ctx.SaveChangesAsync();

        // =================================================================
        // 16. StudentEvaluations (batch insert)
        //     + accumulate scores in memory for PeriodAverages (no N+1)
        // =================================================================
        var mathTeacherId = T["MTH"].Id;

        // (enrollmentId, periodId) → (earnedTotal, maxTotal)
        var periodTotals = new Dictionary<(int, int), (double earned, double max)>();

        var evalBatch = new List<StudentEvaluation>(500);
        // Build a mapping: ClassId → EvaluationItems for that class's templates
        var classEvalItems = new Dictionary<int, List<EvaluationItem>>();
        foreach (var (cls, grade) in classGradeMap)
        {
            var items = new List<EvaluationItem>();
            foreach (var code in subjectCodes)
            {
                var tmpl = templateLookup[(grade.Id, S[code].Id)];
                items.AddRange(allEvalItems.Where(ei => ei.TemplateId == tmpl.Id));
            }
            classEvalItems[cls.Id] = items;
        }
        foreach (var enr in enrollments)
        {
            var gradeItems = classEvalItems[enr.ClassId];
            foreach (var period in periodsList)
            {
                double periodEarned = 0;
                double periodMax    = 0;

                foreach (var item in gradeItems)
                {
                    var maxD  = (double)item.MaxScore;
                    var score = item.AutoCalcType == AutoCalcType.Attendance
                        ? maxD
                        : Math.Round((rng.NextDouble() * maxD * 0.3 + maxD * 0.7) * 2, MidpointRounding.AwayFromZero) / 2;

                    evalBatch.Add(new StudentEvaluation
                    {
                        EnrollmentId     = enr.Id,
                        EvaluationItemId = item.Id,
                        PeriodId         = period.Id,
                        Score            = (decimal)score,
                        EnteredById      = mathTeacherId,
                        EnteredAt        = now,
                        CreatedAt = now, UpdatedAt = now
                    });

                    periodEarned += score;
                    periodMax    += maxD;
                }

                periodTotals[(enr.Id, period.Id)] = (periodEarned, periodMax);

                if (evalBatch.Count >= 500)
                {
                    ctx.StudentEvaluations.AddRange(evalBatch);
                    await ctx.SaveChangesAsync();
                    evalBatch.Clear();
                }
            }
        }
        if (evalBatch.Count > 0)
        {
            ctx.StudentEvaluations.AddRange(evalBatch);
            await ctx.SaveChangesAsync();
        }

        // =================================================================
        // 17. PeriodAverages  (fully in-memory, zero extra queries)
        // =================================================================
        var avgBatch = periodTotals.Select(kv => new PeriodAverage
        {
            EnrollmentId = kv.Key.Item1,
            PeriodId     = kv.Key.Item2,
            AvgScore     = (decimal)Math.Round(kv.Value.earned, 2),
            MaxScore     = (decimal)Math.Round(kv.Value.max,    2),
            CalculatedAt = now,
            CreatedAt = now, UpdatedAt = now
        }).ToList();
        ctx.PeriodAverages.AddRange(avgBatch);
        await ctx.SaveChangesAsync();

        // =================================================================
        // 18. PeriodicAssessments (monthly + final exams)
        //     per enrollment × ALL subjects × term (1st + 2nd)
        // =================================================================
        var allAssessments = new List<PeriodicAssessment>();
        var assessmentRanges = new (string key, int e1Lo, int e1Hi, int e2Lo, int e2Hi, int semLo, int semHi)[]
        {
            ("MTH", 11, 16, 11, 16, 20, 31),
            ("ARB", 8,  15,  8, 15, 20, 31),
            ("ENG", 9,  15,  9, 15, 18, 30),
            ("SCI", 10, 15, 10, 15, 19, 30),
            ("SOC", 8,  14,  8, 14, 18, 28),
            ("ISL", 10, 15, 10, 15, 20, 30),
            ("CMP", 9,  14,  9, 14, 18, 28),
        };

        foreach (var enr in enrollments)
        {
            foreach (var term in new[] { AcademicTerm.FirstSemester, AcademicTerm.SecondSemester })
            {
                var isFirst = term == AcademicTerm.FirstSemester;
                foreach (var (subjectKey, e1Lo, e1Hi, e2Lo, e2Hi, semLo, semHi) in assessmentRanges)
                {
                    var subjectId = S[subjectKey].Id;
                    // ── تواريخ منطقية ضمن فترة الدراسة الفعلية لكل ترم ───────────────
                    // الترم الأول: 2025/9/21 ← 2026/1/22
                    //   Monthly1  = نوفمبر 2025
                    //   Monthly2  = ديسمبر 2025
                    //   Semester  = يناير 2026
                    // الترم الثاني: 2026/4/4 ← 2026/7/11
                    //   Monthly1  = مايو 2026
                    //   Monthly2  = يونيو 2026
                    //   Semester  = يوليو 2026
                    var (m1Date, m2Date, semDate) = isFirst
                        ? (new DateOnly(2025, 11, 15), new DateOnly(2025, 12, 20), new DateOnly(2026, 1, 20))
                        : (new DateOnly(2026, 5, 10),  new DateOnly(2026, 6, 5),   new DateOnly(2026, 7, 5));

                    // MonthlyExam1
                    allAssessments.Add(new PeriodicAssessment
                    {
                        EnrollmentId   = enr.Id,
                        SubjectId      = subjectId,
                        AssessmentType = PeriodicAssessmentType.MonthlyExam1,
                        Term           = term,
                        Score          = rng.Next(e1Lo, e1Hi),
                        MaxScore       = 15,
                        AssessmentDate = m1Date,
                        CreatedAt = now, UpdatedAt = now
                    });
                    // MonthlyExam2
                    allAssessments.Add(new PeriodicAssessment
                    {
                        EnrollmentId   = enr.Id,
                        SubjectId      = subjectId,
                        AssessmentType = PeriodicAssessmentType.MonthlyExam2,
                        Term           = term,
                        Score          = rng.Next(e2Lo, e2Hi),
                        MaxScore       = 15,
                        AssessmentDate = m2Date,
                        CreatedAt = now, UpdatedAt = now
                    });
                    // SemesterExam (final exam /30)
                    allAssessments.Add(new PeriodicAssessment
                    {
                        EnrollmentId   = enr.Id,
                        SubjectId      = subjectId,
                        AssessmentType = PeriodicAssessmentType.SemesterExam,
                        Term           = term,
                        Score          = rng.Next(semLo, semHi),
                        MaxScore       = 30,
                        AssessmentDate = semDate,
                        CreatedAt = now, UpdatedAt = now
                    });
                }
            }
        }
        ctx.PeriodicAssessments.AddRange(allAssessments);
        await ctx.SaveChangesAsync();

        // =================================================================
        // 19. FinalGrades  (one per subject per enrollment per term)
        //     PeriodAvgScore يُجمع من PeriodAverages الحقيقية (المحسوبة في القسم 17)
        // =================================================================
        var asmLookup = allAssessments
            .GroupBy(a => new { a.EnrollmentId, SubjectId = a.SubjectId ?? 0, Term = (int)a.Term! })
            .ToDictionary(g => g.Key, g => g.ToList());

        // ── lookup لمتوسطات الفترات حسب (enrollmentId, term) ──────────────
        // نجمّع كل PeriodAverage للطالب ونرجّعها لنسبة مئوية (0-100) ثم لمقياس
        // النصف (من 40 درجة - النسبة المعتمدة للأنشطة في الصف الإعدادي).
        // الفترة تنتمي لترم معين عبر SemesterNumber الموجود في EvaluationPeriod.
        var periodById = periodsList.ToDictionary(p => p.Id);
        var periodAvgByEnrTerm = avgBatch
            .GroupBy(a => a.EnrollmentId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(a => periodById[a.PeriodId].SemesterNumber ?? 0)
                      .ToDictionary(
                          sg => sg.Key,
                          sg => sg.Sum(x => (double)x.AvgScore)));

        // مقياس درجات الأنشطة/الفترات في FinalGrade (مجموع written دون الامتحان)
        const double periodMaxScore = 40.0;

        var allFinalGrades = new List<FinalGrade>();
        foreach (var enr in enrollments)
        {
            foreach (var term in new[] { AcademicTerm.FirstSemester, AcademicTerm.SecondSemester })
            {
                var semesterNum = term == AcademicTerm.FirstSemester ? 1 : 2;
                foreach (var subject in subjectEntities)
                {
                    var key = new { EnrollmentId = enr.Id, SubjectId = subject.Id, Term = (int)term };
                    var asms = asmLookup.GetValueOrDefault(key) ?? new();
                    var e1      = asms.FirstOrDefault(a => a.AssessmentType == PeriodicAssessmentType.MonthlyExam1)?.Score ?? 0;
                    var e2      = asms.FirstOrDefault(a => a.AssessmentType == PeriodicAssessmentType.MonthlyExam2)?.Score ?? 0;
                    var semExam = asms.FirstOrDefault(a => a.AssessmentType == PeriodicAssessmentType.SemesterExam)?.Score
                                  ?? rng.Next(20, 31);

                    // PeriodAvgScore مأخوذ من PeriodAverages الحقيقية المجمّعة
                    // تحويل النسبة من مقياس الفترات إلى مقياس periodMaxScore (40).
                    double? periodAvgD = null;
                    if (periodAvgByEnrTerm.TryGetValue(enr.Id, out var byTerm) &&
                        byTerm.TryGetValue(semesterNum, out var rawEarned))
                    {
                        periodAvgD = Math.Round(rawEarned, 0);
                        if (periodAvgD > periodMaxScore) periodAvgD = periodMaxScore;
                    }
                    var periodAvg = (int)Math.Round(periodAvgD ?? rng.Next(25, 39));

                    var written  = periodAvg + e1 + e2;
                    var total    = written + semExam;
                    var isComplete = e1 > 0 && e2 > 0 && semExam > 0;
                    allFinalGrades.Add(new FinalGrade
                    {
                        EnrollmentId     = enr.Id,
                        SubjectId        = subject.Id,
                        Term             = term,
                        PeriodAvgScore   = periodAvg,
                        Assessment1Score = e1,
                        Assessment2Score = e2,
                        WrittenTotal     = written,
                        FinalExamScore   = semExam,
                        Total            = total > 100 ? 100 : total,
                        MaxTotal         = 100,
                        IsComplete       = isComplete,
                        IsPublished      = true,
                        CreatedAt = now, UpdatedAt = now
                    });
                }
            }
        }
        ctx.FinalGrades.AddRange(allFinalGrades);
        await ctx.SaveChangesAsync();

        // =================================================================
        // 20. DailyAbsences  (~25% of students, 3 absences each)
        //     التواريخ ضمن فترات الترم الأول الفعلية (أكتوبر/نوفمبر/ديسمبر 2025)
        //     و PeriodId يربط بالفترة الصحيحة المطابقة لتاريخ الغياب.
        // =================================================================
        var mathCstClass1 = cstList.First(c => c.ClassId == class1.Id && c.SubjectId == S["MTH"].Id);

        // مواقع الدراسة في الترم الأول ضمنها الغياب (من بداية الدراسة حتى امتحانات الترم)
        var studyDates = new[]
        {
            new DateOnly(2025, 10, 5),
            new DateOnly(2025, 10, 26),
            new DateOnly(2025, 11, 16),
            new DateOnly(2025, 12, 7),
            new DateOnly(2025, 12, 28),
        };

        // helper محلي: يرجّع الفترة المناسبة لتاريخ معين (أو null لو بره أي فترة)
        int? PeriodIdForDate(DateOnly d) =>
            periodsList.FirstOrDefault(p => p.StartDate <= d && p.EndDate >= d)?.Id;

        var absences = new List<DailyAbsence>();
        var absenceKeys = new HashSet<(int, int?, DateOnly)>();  // تجنب الـ duplicate
        foreach (var enr in enrollments)
        {
            if (rng.NextDouble() >= 0.25) continue;
            var mathCstForClass = cstList.First(c => c.ClassId == enr.ClassId && c.SubjectId == S["MTH"].Id);
            for (int d = 0; d < 3; d++)
            {
                var absenceDate = studyDates[rng.Next(studyDates.Length)]
                    .AddDays(rng.Next(0, 5));  // إزاحة صغيرة ضمن نفس الأسبوع
                var key = (enr.Id, mathCstForClass.Id, absenceDate);
                if (!absenceKeys.Add(key)) continue;  // تخطي الـ duplicate
                absences.Add(new DailyAbsence
                {
                    EnrollmentId          = enr.Id,
                    ClassSubjectTeacherId = mathCstForClass.Id,
                    AbsenceDate           = absenceDate,
                    IsAbsent              = true,
                    Reason                = "مرض",
                    PeriodId              = PeriodIdForDate(absenceDate),
                    RecordedById          = mathTeacherId,
                    CreatedAt = now, UpdatedAt = now
                });
            }
        }
        ctx.DailyAbsences.AddRange(absences);
        await ctx.SaveChangesAsync();

        // =================================================================
        // 21. Units + Lessons  (all 7 subjects)
        // =================================================================
        await SeedUnitsAndLessons(ctx, S, now, grade1, grade2, grade3);

        // =================================================================
        // 22. QuestionBank  (sample questions for Math + Arabic, Grade 1)
        // =================================================================
        var qbSubjects = new[] { S["MTH"], S["ARB"] };
        var qbGrade = grade1;
        var qbQuestions = new (string text, QuestionType type, string correct, string? options, Subject subj)[]
        {
            // ── الرياضيات ───────────────────────────────────────────────
            ("ما ناتج 5 + 3؟",                       QuestionType.MultipleChoice, "8",  @"[""4"",""7"",""8"",""10""]", S["MTH"]),
            ("10 - 4 = ...",                         QuestionType.FillBlank,      "6",  null,                     S["MTH"]),
            ("العدد 12 هو عدد زوجي",                 QuestionType.TrueFalse,      "True", null,                    S["MTH"]),
            ("ما ناتج 2 × 6 = ...",                  QuestionType.MultipleChoice, "12", @"[""8"",""12"",""14"",""16""]", S["MTH"]),
            ("اجمع: 15 + 7 = ...",                   QuestionType.FillBlank,      "22", null,                     S["MTH"]),
            ("العدد الأولي هو عدد يقبل القسمة على 1 ونفسه فقط",
                                                      QuestionType.TrueFalse,      "True", null,                    S["MTH"]),
            ("ما ناتج 36 ÷ 6؟",                     QuestionType.MultipleChoice, "6",  @"[""4"",""6"",""8"",""9""]",  S["MTH"]),
            ("المربع له 4 أضلاع متساوية",           QuestionType.TrueFalse,      "True", null,                    S["MTH"]),
            // ── اللغة العربية ────────────────────────────────────────────
            ("الفعل الماضي من 'يكتب' هو 'كتب'",     QuestionType.TrueFalse,      "True", null,                    S["ARB"]),
            ("ما مرادف كلمة 'جميل'؟",               QuestionType.MultipleChoice, "حسن", @"[""قبيح"",""حسن"",""سريع"",""كبير""]", S["ARB"]),
            ("المبتدأ ... في أول الجملة الاسمية",    QuestionType.FillBlank,      "مرفوع", null,                   S["ARB"]),
            ("جمع كلمة 'كتاب' هو 'كُتب'",           QuestionType.TrueFalse,      "True", null,                    S["ARB"]),
            ("ما ضد كلمة 'طويل'؟",                  QuestionType.MultipleChoice, "قصير",@"[""جميل"",""قصير"",""عريض"",""ثقيل""]", S["ARB"]),
            ("الحرف الناسخ 'إنّ' ينصب ... ويرفع ...",
                                                      QuestionType.FillBlank,      "الاسم والخبر", null,           S["ARB"]),
        };

        foreach (var (text, type, correct, options, subj) in qbQuestions)
        {
            ctx.QuestionBank.Add(new QuestionBank
            {
                QuestionText  = text,
                QuestionType  = type,
                CorrectAnswer = correct,
                OptionsJson   = options,
                SubjectId     = subj.Id,
                GradeLevelId  = qbGrade.Id,
                CreatedAt = now, UpdatedAt = now
            });
        }
        await ctx.SaveChangesAsync();

        // =================================================================
        // 23. Assignment + Question + Option + Submission + Answer
        // =================================================================
        var mathCst1 = cstList.First(c => c.ClassId == class1.Id && c.SubjectId == S["MTH"].Id);
        var assignment = new Assignment
        {
            ClassSubjectTeacherId = mathCst1.Id,
            Title       = "واجب الرياضيات الأول",
            Description = "واجب أسبوعي — حل التمارين من الكتاب.",
            DueDate     = CairoAt(7, 23, 59),  // موعد التسليم بعد أسبوع منتصف الليل بتوقيت القاهرة
            MaxScore    = 10,
            IsAutoGraded= true,
            Category    = EvaluationCategory.Academic,
            IsPublished = true,
            CreatedAt = now, UpdatedAt = now
        };
        ctx.Assignments.Add(assignment);
        await ctx.SaveChangesAsync();

        var asgQ = new AssignmentQuestion
        {
            AssignmentId = assignment.Id,
            QuestionText = "ما ناتج 2 + 3؟",
            QuestionType = QuestionType.MultipleChoice,
            CorrectAnswer= "5",
            DisplayOrder = 1,
            Points       = 10,
            CreatedAt = now, UpdatedAt = now
        };
        ctx.AssignmentQuestions.Add(asgQ);
        await ctx.SaveChangesAsync();

        var asgOpt = new AssignmentQuestionOption
        {
            QuestionId   = asgQ.Id,
            OptionText   = "5",
            IsCorrect    = true,
            DisplayOrder = 1,
            CreatedAt = now, UpdatedAt = now
        };
        ctx.AssignmentQuestionOptions.Add(asgOpt);
        await ctx.SaveChangesAsync();

        var firstEnr = enrollments.First();
        var submission = new StudentAssignmentSubmission
        {
            EnrollmentId = firstEnr.Id,
            AssignmentId = assignment.Id,
            SubmittedAt  = now,
            Score        = 10,
            MaxScore     = 10,
            IsGraded     = true,
            AIFeedback   = "إجابة صحيحة.",
            CreatedAt = now, UpdatedAt = now
        };
        ctx.StudentAssignmentSubmissions.Add(submission);
        await ctx.SaveChangesAsync();

        ctx.StudentAssignmentAnswers.Add(new StudentAssignmentAnswer
        {
            SubmissionId     = submission.Id,
            QuestionId       = asgQ.Id,
            AnswerText       = "5",
            SelectedOptionId = asgOpt.Id,
            IsCorrect        = true,
            PointsEarned     = 10,
            CreatedAt = now, UpdatedAt = now
        });
        await ctx.SaveChangesAsync();

        // =================================================================
        // 24. Exam + Group + Question + Option + Attempt + Answer
        // =================================================================
        var exam = new Exam
        {
            ClassSubjectTeacherId = mathCst1.Id,
            GradeLevelId = grade1.Id,
            Title           = "اختبار الرياضيات القصير",
            StartTime       = CairoAt(1, 10, 0),               // بكرة 10 صباحًا بتوقيت القاهرة
            EndTime         = CairoAt(1, 10, 0).AddMinutes(30), // نفس النهار 10:30
            DurationMinutes = 30,
            TotalScore      = 10,
            Category        = EvaluationCategory.Academic,
            IsPublished     = true,
            CreatedAt = now, UpdatedAt = now
        };
        ctx.Exams.Add(exam);
        await ctx.SaveChangesAsync();

        var examGroup = new ExamQuestionGroup
        {
            ExamId       = exam.Id,
            DisplayType  = TemplateContentType.Passage,
            ContentTitle = "أسئلة عامة",
            ContentText  = "اختر الإجابة الصحيحة.",
            DisplayOrder = 1,
            CreatedAt = now, UpdatedAt = now
        };
        ctx.ExamQuestionGroups.Add(examGroup);
        await ctx.SaveChangesAsync();

        var examQ = new ExamQuestion
        {
            ExamId       = exam.Id,
            GroupId      = examGroup.Id,
            QuestionText = "ما ناتج 4 + 1؟",
            QuestionType = QuestionType.MultipleChoice,
            CorrectAnswer= "5",
            DisplayOrder = 1,
            Points       = 10,
            CreatedAt = now, UpdatedAt = now
        };
        ctx.ExamQuestions.Add(examQ);
        await ctx.SaveChangesAsync();

        var examOpt = new ExamQuestionOption
        {
            QuestionId   = examQ.Id,
            OptionText   = "5",
            IsCorrect    = true,
            DisplayOrder = 1,
            CreatedAt = now, UpdatedAt = now
        };
        ctx.ExamQuestionOptions.Add(examOpt);
        await ctx.SaveChangesAsync();

        var attempt = new StudentExamAttempt
        {
            EnrollmentId = firstEnr.Id,
            ExamId       = exam.Id,
            StartedAt    = now,
            SubmittedAt  = now.AddMinutes(12),
            Score        = 10,
            TotalScore   = 10,
            IsGraded     = true,
            CreatedAt = now, UpdatedAt = now
        };
        ctx.StudentExamAttempts.Add(attempt);
        await ctx.SaveChangesAsync();

        ctx.StudentExamAnswers.Add(new StudentExamAnswer
        {
            AttemptId        = attempt.Id,
            QuestionId       = examQ.Id,
            AnswerText       = "5",
            SelectedOptionId = examOpt.Id,
            IsCorrect        = true,
            PointsEarned     = 10,
            CreatedAt = now, UpdatedAt = now
        });
        await ctx.SaveChangesAsync();

        // ── Link QuestionBank questions to the exam ─────────────────────────
        var qbMathLinks = await ctx.QuestionBank
            .Where(q => q.SubjectId == S["MTH"].Id)
            .OrderBy(q => q.Id)
            .Take(3)
            .ToListAsync();
        int linkOrder = 1;
        foreach (var qb in qbMathLinks)
        {
            ctx.ExamQuestionBankItems.Add(new ExamQuestionBankItem
            {
                ExamId         = exam.Id,
                QuestionBankId = qb.Id,
                Points         = 10,
                DisplayOrder   = linkOrder++,
                CreatedAt = now, UpdatedAt = now
            });
        }
        await ctx.SaveChangesAsync();

        // =================================================================
        // 25. Exams + Assignments for all subjects (2 MCQ + 2 TF + 1 Essay each)
        // =================================================================

        // ── Helper: seed one exam ──────────────────────────────────────────
        async Task SeedOneExam(AppDbContext ctx, SchoolClass cls, string subjCode,
            string title, int duration, decimal totalScore,
            params (string text, QuestionType qType, string[] opts, string correct, decimal pts)[] qDefs)
        {
            var cst = cstList.First(c => c.ClassId == cls.Id && c.SubjectId == S[subjCode].Id);
            GradeLevel grade;
            if      (cls.GradeLevelId == grade1.Id) grade = grade1;
            else if (cls.GradeLevelId == grade2.Id) grade = grade2;
            else                                    grade = grade3;

            var exam = new Exam
            {
                ClassSubjectTeacherId = cst.Id,
                GradeLevelId = grade.Id,
                Title = title,
                StartTime = CairoAt(2, 10, 0),               // بعد يومين 10 صباحًا بتوقيت القاهرة
                EndTime = CairoAt(2, 10, 0).AddMinutes(duration),
                DurationMinutes = duration,
                TotalScore = totalScore,
                Category = EvaluationCategory.Academic,
                IsPublished = true,
                CreatedAt = now, UpdatedAt = now
            };
            ctx.Exams.Add(exam);
            await ctx.SaveChangesAsync();

            var createdQuestions = new List<ExamQuestion>();
            var createdOptions    = new List<ExamQuestionOption>();

            for (int i = 0; i < qDefs.Length; i++)
            {
                var qd = qDefs[i];
                var q = new ExamQuestion
                {
                    ExamId = exam.Id,
                    QuestionText = qd.text,
                    QuestionType = qd.qType,
                    CorrectAnswer = qd.correct,
                    DisplayOrder = i + 1,
                    Points = qd.pts,
                    CreatedAt = now, UpdatedAt = now
                };
                ctx.ExamQuestions.Add(q);
                await ctx.SaveChangesAsync();
                createdQuestions.Add(q);

                for (int j = 0; j < qd.opts.Length; j++)
                {
                    var opt = new ExamQuestionOption
                    {
                        QuestionId = q.Id,
                        OptionText = qd.opts[j],
                        IsCorrect = qd.opts[j] == qd.correct,
                        DisplayOrder = j + 1,
                        CreatedAt = now, UpdatedAt = now
                    };
                    ctx.ExamQuestionOptions.Add(opt);
                    await ctx.SaveChangesAsync();
                    createdOptions.Add(opt);
                }
            }

            // ── توليد محاولات لعدة طلاب (10 طلاب) لكل امتحان ──────────────────
            var classEnrollments = enrollments.Where(e => e.ClassId == cls.Id).Take(10).ToList();

            foreach (var enr in classEnrollments)
            {
                // كل طالب يخطئ في سؤال مختلف عشوائياً للحصول على تنوع في الدرجات
                int wrongIdx = rng.Next(1, createdQuestions.Count + 1);
                var attempt = new StudentExamAttempt
                {
                    EnrollmentId = enr.Id,
                    ExamId = exam.Id,
                    StartedAt = now,
                    SubmittedAt = now.AddMinutes(rng.Next(5, duration)),
                    Score = totalScore, // يُعاد حسابه من الإجابات أدناه
                    TotalScore = totalScore,
                    IsGraded = true,
                    CreatedAt = now, UpdatedAt = now
                };
                ctx.StudentExamAttempts.Add(attempt);
                await ctx.SaveChangesAsync();

                decimal earned = 0;
                foreach (var q in createdQuestions)
                {
                    bool isWrong = q.DisplayOrder == wrongIdx;
                    var ans = new StudentExamAnswer
                    {
                        AttemptId = attempt.Id,
                        QuestionId = q.Id,
                        IsCorrect = !isWrong,
                        PointsEarned = isWrong ? 0 : q.Points,
                        CreatedAt = now, UpdatedAt = now
                    };

                    if (q.QuestionType == QuestionType.MultipleChoice)
                    {
                        var correctOpt = createdOptions.First(o => o.QuestionId == q.Id && o.IsCorrect);
                        ans.SelectedOptionId = correctOpt.Id;
                        ans.AnswerText = correctOpt.OptionText;
                    }
                    else if (q.QuestionType == QuestionType.TrueFalse)
                    {
                        ans.BooleanAnswer = isWrong ? !bool.Parse(q.CorrectAnswer!) : bool.Parse(q.CorrectAnswer!);
                        ans.AnswerText = ans.BooleanAnswer.Value ? "True" : "False";
                    }
                    else
                    {
                        ans.AnswerText = "إجابة الطالب: " + q.CorrectAnswer;
                    }
                    if (!isWrong) earned += q.Points;
                    ctx.StudentExamAnswers.Add(ans);
                }
                attempt.Score = earned;
                await ctx.SaveChangesAsync();
            }
        }

        // ── Helper: seed one assignment ────────────────────────────────────
        async Task SeedOneAssignment(AppDbContext ctx, SchoolClass cls, string subjCode,
            string title, decimal maxScore,
            params (string text, QuestionType qType, string[] opts, string correct, decimal pts)[] qDefs)
        {
            var cst = cstList.First(c => c.ClassId == cls.Id && c.SubjectId == S[subjCode].Id);

            var assignment = new Assignment
            {
                ClassSubjectTeacherId = cst.Id,
                Title = title,
                Description = $"واجب في مادة {S[subjCode].Name}",
                DueDate = CairoAt(14, 23, 59),  // موعد التسليم بعد أسبوعين منتصف الليل بتوقيت القاهرة
                MaxScore = maxScore,
                IsAutoGraded = true,
                Category = EvaluationCategory.Academic,
                IsPublished = true,
                CreatedAt = now, UpdatedAt = now
            };
            ctx.Assignments.Add(assignment);
            await ctx.SaveChangesAsync();

            var createdQuestions = new List<AssignmentQuestion>();
            var createdOptions    = new List<AssignmentQuestionOption>();

            for (int i = 0; i < qDefs.Length; i++)
            {
                var qd = qDefs[i];
                var q = new AssignmentQuestion
                {
                    AssignmentId = assignment.Id,
                    QuestionText = qd.text,
                    QuestionType = qd.qType,
                    CorrectAnswer = qd.correct,
                    DisplayOrder = i + 1,
                    Points = qd.pts,
                    CreatedAt = now, UpdatedAt = now
                };
                ctx.AssignmentQuestions.Add(q);
                await ctx.SaveChangesAsync();
                createdQuestions.Add(q);

                for (int j = 0; j < qd.opts.Length; j++)
                {
                    var opt = new AssignmentQuestionOption
                    {
                        QuestionId = q.Id,
                        OptionText = qd.opts[j],
                        IsCorrect = qd.opts[j] == qd.correct,
                        DisplayOrder = j + 1,
                        CreatedAt = now, UpdatedAt = now
                    };
                    ctx.AssignmentQuestionOptions.Add(opt);
                    await ctx.SaveChangesAsync();
                    createdOptions.Add(opt);
                }
            }

            // ── توليد تسليمات لعدة طلاب (10 طلاب) لكل واجب ─────────────────────
            var classEnrollments = enrollments.Where(e => e.ClassId == cls.Id).Take(10).ToList();
            var feedbackOptions = new[]
            {
                "أحسنت! معظم الإجابات صحيحة.",
                "عمل جيد، راجع الأسئلة الخاطئة.",
                "ممتاز! إجابات نموذجية.",
                "تحتاج إلى مزيد من التركيز في المرة القادمة."
            };

            foreach (var enr in classEnrollments)
            {
                // كل طالب يخطئ في سؤال مختلف عشوائياً
                int wrongIdx = rng.Next(1, createdQuestions.Count + 1);
                var submission = new StudentAssignmentSubmission
                {
                    EnrollmentId = enr.Id,
                    AssignmentId = assignment.Id,
                    SubmittedAt = now,
                    Score = maxScore, // يُعاد حسابه من الإجابات
                    MaxScore = maxScore,
                    IsGraded = true,
                    AIFeedback = feedbackOptions[rng.Next(feedbackOptions.Length)],
                    CreatedAt = now, UpdatedAt = now
                };
                ctx.StudentAssignmentSubmissions.Add(submission);
                await ctx.SaveChangesAsync();

                decimal earned = 0;
                foreach (var q in createdQuestions)
                {
                    bool isWrong = q.DisplayOrder == wrongIdx;
                    var ans = new StudentAssignmentAnswer
                    {
                        SubmissionId = submission.Id,
                        QuestionId = q.Id,
                        IsCorrect = !isWrong,
                        PointsEarned = isWrong ? 0 : q.Points,
                        CreatedAt = now, UpdatedAt = now
                    };

                    if (q.QuestionType == QuestionType.MultipleChoice)
                    {
                        var correctOpt = createdOptions.First(o => o.QuestionId == q.Id && o.IsCorrect);
                        ans.SelectedOptionId = correctOpt.Id;
                        ans.AnswerText = correctOpt.OptionText;
                    }
                    else if (q.QuestionType == QuestionType.TrueFalse)
                    {
                        ans.BooleanAnswer = isWrong ? !bool.Parse(q.CorrectAnswer!) : bool.Parse(q.CorrectAnswer!);
                        ans.AnswerText = ans.BooleanAnswer.Value ? "True" : "False";
                    }
                    else
                    {
                        ans.AnswerText = "إجابة الطالب: " + q.CorrectAnswer;
                    }
                    if (!isWrong) earned += q.Points;
                    ctx.StudentAssignmentAnswers.Add(ans);
                }
                submission.Score = earned;
                await ctx.SaveChangesAsync();
            }
        }

        // ===================================================================
        // ╔══════════════════════════════════════════════════════════════════╗
        // ║  EXAMS                                                          ║
        // ╚══════════════════════════════════════════════════════════════════╝
        // ===================================================================

        // ARABIC — Class 1/1
        await SeedOneExam(ctx, class1, "ARB", "اختبار اللغة العربية الشامل", 45, 50,
            ("ما إعراب كلمة 'الكتاب' في جملة 'قرأت الكتاب'؟", QuestionType.MultipleChoice, new[]{"مفعول به","فاعل","مبتدأ","خبر"}, "مفعول به", 10m),
            ("اختر الجملة الصحيحة:", QuestionType.MultipleChoice, new[]{"التلميذ مجتهد","التلميذ مجتهداً","التلميذ مجتهدٌ","التلميذ مجتهدٍ"}, "التلميذ مجتهد", 10m),
            ("الفعل الماضي يدل على حدث وقع في الماضي.", QuestionType.TrueFalse, new[]{"True","False"}, "True", 10m),
            ("همزة الوصل تكتب تحت الألف.", QuestionType.TrueFalse, new[]{"True","False"}, "False", 10m),
            ("اكتب فقرة من 3 أسطر عن أهمية القراءة.", QuestionType.Essay, Array.Empty<string>(), "القراءة تنمي العقل وتوسع المدارك وتثري الثقافة", 10m)
        );

        // COMPUTER — Class 1/1
        await SeedOneExam(ctx, class1, "CMP", "اختبار الحاسب الآلي", 30, 50,
            ("من وحدات الإدخال في الحاسوب:", QuestionType.MultipleChoice, new[]{"لوحة المفاتيح","الشاشة","السماعات","الطابعة"}, "لوحة المفاتيح", 10m),
            ("الذاكرة RAM هي ذاكرة:", QuestionType.MultipleChoice, new[]{"متطايرة","دائمة","خارجية","بطيئة"}, "متطايرة", 10m),
            ("الحاسوب يستقبل الأوامر عن طريق وحدات الإدخال.", QuestionType.TrueFalse, new[]{"True","False"}, "True", 10m),
            ("الإنترنت من اختراعات القرن العشرين.", QuestionType.TrueFalse, new[]{"True","False"}, "True", 10m),
            ("صف أهم استخدامات الحاسوب في حياتنا اليومية.", QuestionType.Essay, Array.Empty<string>(), "يستخدم الحاسوب في التعليم والعمل والاتصالات والترفيه", 10m)
        );

        // ENGLISH — Class 1/2
        await SeedOneExam(ctx, class2, "ENG", "English Exam", 40, 50,
            ("Choose the correct answer: I ___ a student.", QuestionType.MultipleChoice, new[]{"am","is","are","be"}, "am", 10m),
            ("What is the capital of Egypt?", QuestionType.MultipleChoice, new[]{"Cairo","London","Paris","Rome"}, "Cairo", 10m),
            ("The sun rises in the east.", QuestionType.TrueFalse, new[]{"True","False"}, "True", 10m),
            ("There are 8 days in a week.", QuestionType.TrueFalse, new[]{"True","False"}, "False", 10m),
            ("Write 4 sentences about your daily routine.", QuestionType.Essay, Array.Empty<string>(), "I wake up at 7 am. I go to school. I study hard. I play with friends.", 10m)
        );

        // MATH — Class 2/1
        await SeedOneExam(ctx, class3, "MTH", "اختبار الرياضيات", 45, 50,
            ("ما ناتج 15 + 27؟", QuestionType.MultipleChoice, new[]{"42","32","52","62"}, "42", 10m),
            ("الجذر التربيعي للعدد 64 هو:", QuestionType.MultipleChoice, new[]{"6","7","8","9"}, "8", 10m),
            ("العدد الأولي هو عدد يقبل القسمة على نفسه وعلى الواحد فقط.", QuestionType.TrueFalse, new[]{"True","False"}, "True", 10m),
            ("حاصل ضرب 5 × 0 = 5.", QuestionType.TrueFalse, new[]{"True","False"}, "False", 10m),
            ("اشرح كيفية إيجاد مساحة المستطيل مع مثال.", QuestionType.Essay, Array.Empty<string>(), "مساحة المستطيل = الطول × العرض. مثال: مستطيل طوله 5 وعرضه 3 فمساحته 15", 10m)
        );

        // SCIENCE — Class 2/2
        await SeedOneExam(ctx, class4, "SCI", "اختبار العلوم", 40, 50,
            ("أي مما يلي مصدر متجدد للطاقة؟", QuestionType.MultipleChoice, new[]{"الطاقة الشمسية","الفحم","البترول","الغاز الطبيعي"}, "الطاقة الشمسية", 10m),
            ("جهاز التنفس في الإنسان يبدأ بـ:", QuestionType.MultipleChoice, new[]{"الأنف","الرئتين","القصبة الهوائية","الحجاب الحاجز"}, "الأنف", 10m),
            ("الماء يغلي عند درجة حرارة 100° مئوية.", QuestionType.TrueFalse, new[]{"True","False"}, "True", 10m),
            ("الأرض أكبر كواكب المجموعة الشمسية.", QuestionType.TrueFalse, new[]{"True","False"}, "False", 10m),
            ("اشرح عملية البناء الضوئي بإيجاز.", QuestionType.Essay, Array.Empty<string>(), "عملية تحول النبات الطاقة الضوئية إلى طاقة كيميائية باستخدام الماء وثاني أكسيد الكربون", 10m)
        );

        // SOCIAL STUDIES — Class 3/1
        await SeedOneExam(ctx, class5, "SOC", "اختبار الدراسات الاجتماعية", 45, 50,
            ("عاصمة مصر هي:", QuestionType.MultipleChoice, new[]{"القاهرة","الإسكندرية","الجيزة","أسوان"}, "القاهرة", 10m),
            ("يطل مصر على:", QuestionType.MultipleChoice, new[]{"البحر المتوسط والبحر الأحمر","البحر المتوسط فقط","البحر الأحمر فقط","المحيط الأطلسي"}, "البحر المتوسط والبحر الأحمر", 10m),
            ("نهر النيل أطول نهر في العالم.", QuestionType.TrueFalse, new[]{"True","False"}, "True", 10m),
            ("تقع مصر في قارة أوروبا.", QuestionType.TrueFalse, new[]{"True","False"}, "False", 10m),
            ("اكتب عن أهمية نهر النيل لمصر.", QuestionType.Essay, Array.Empty<string>(), "نهر النيل شريان الحياة لمصر حيث يوفر المياه للشرب والزراعة والطاقة الكهرومائية", 10m)
        );

        // ISLAMIC — Class 3/2
        await SeedOneExam(ctx, class6, "ISL", "اختبار التربية الإسلامية", 30, 50,
            ("أركان الإسلام هي:", QuestionType.MultipleChoice, new[]{"خمسة","ستة","سبعة","أربعة"}, "خمسة", 10m),
            ("الصلاة الثانية في اليوم هي:", QuestionType.MultipleChoice, new[]{"الظهر","العصر","المغرب","العشاء"}, "الظهر", 10m),
            ("الصوم من أركان الإسلام.", QuestionType.TrueFalse, new[]{"True","False"}, "True", 10m),
            ("الزكاة واجبة على كل مسلم.", QuestionType.TrueFalse, new[]{"True","False"}, "True", 10m),
            ("اكتب عن أهمية الصلاة في حياة المسلم.", QuestionType.Essay, Array.Empty<string>(), "الصلاة عماد الدين وهي أول ما يحاسب عليه العبد يوم القيامة وتنهى عن الفحشاء والمنكر", 10m)
        );

        // ===================================================================
        // ╔══════════════════════════════════════════════════════════════════╗
        // ║  ASSIGNMENTS                                                     ║
        // ╚══════════════════════════════════════════════════════════════════╝
        // ===================================================================

        // ARABIC — Class 1/1
        await SeedOneAssignment(ctx, class1, "ARB", "واجب اللغة العربية الأسبوعي", 50,
            ("اختر المعنى الصحيح لكلمة 'شجاعة':", QuestionType.MultipleChoice, new[]{"قوة القلب","الضعف","الجبن","الخوف"}, "قوة القلب", 10m),
            ("ما جمع كلمة 'كتاب'؟", QuestionType.MultipleChoice, new[]{"كتب","كتابة","كاتب","مكتب"}, "كتب", 10m),
            ("الفعل 'يكتب' فعل مضارع.", QuestionType.TrueFalse, new[]{"True","False"}, "True", 10m),
            ("الهمزة في كلمة 'أحمد' همزة قطع.", QuestionType.TrueFalse, new[]{"True","False"}, "True", 10m),
            ("اكتب 3 جمل عن أهمية العلم.", QuestionType.Essay, Array.Empty<string>(), "العلم يرفع الأمم. بالعلم تتقدم المجتمعات. العلم نور والجهل ظلام.", 10m)
        );

        // COMPUTER — Class 1/1
        await SeedOneAssignment(ctx, class1, "CMP", "واجب الحاسب الآلي", 50,
            ("من وحدات الإخراج في الحاسوب:", QuestionType.MultipleChoice, new[]{"الشاشة","الفأرة","لوحة المفاتيح","الماسح الضوئي"}, "الشاشة", 10m),
            ("نظام التشغيل من أمثلة:", QuestionType.MultipleChoice, new[]{"البرامج التطبيقية","برامج النظام","برامج التسلية","برامج الرسم"}, "برامج النظام", 10m),
            ("يمكن تخزين الملفات في مجلدات.", QuestionType.TrueFalse, new[]{"True","False"}, "True", 10m),
            ("الماوس من وحدات الإخراج.", QuestionType.TrueFalse, new[]{"True","False"}, "False", 10m),
            ("اذكر 3 أنواع من أجهزة الحاسوب.", QuestionType.Essay, Array.Empty<string>(), "الحاسوب الشخصي - الحاسوب المحمول - الحاسوب اللوحي", 10m)
        );

        // ENGLISH — Class 1/2
        await SeedOneAssignment(ctx, class2, "ENG", "English Homework", 50,
            ("Choose: She ___ to school every day.", QuestionType.MultipleChoice, new[]{"goes","go","going","went"}, "goes", 10m),
            ("The opposite of 'big' is:", QuestionType.MultipleChoice, new[]{"small","large","huge","tall"}, "small", 10m),
            ("Dogs can fly.", QuestionType.TrueFalse, new[]{"True","False"}, "False", 10m),
            ("Tuesday comes after Monday.", QuestionType.TrueFalse, new[]{"True","False"}, "True", 10m),
            ("Write 3 sentences about your family.", QuestionType.Essay, Array.Empty<string>(), "My family has 4 members. My father is a teacher. My mother is a doctor.", 10m)
        );

        // MATH — Class 2/1
        await SeedOneAssignment(ctx, class3, "MTH", "واجب الرياضيات الأسبوعي", 50,
            ("ما ناتج 100 − 37؟", QuestionType.MultipleChoice, new[]{"63","73","67","77"}, "63", 10m),
            ("أي الأعداد التالية عدد زوجي؟", QuestionType.MultipleChoice, new[]{"24","17","9","5"}, "24", 10m),
            ("العدد 7 هو عدد أولي.", QuestionType.TrueFalse, new[]{"True","False"}, "True", 10m),
            ("25 ÷ 5 = 4.", QuestionType.TrueFalse, new[]{"True","False"}, "False", 10m),
            ("كيف تجد محيط المستطيل؟ اشرح مع مثال.", QuestionType.Essay, Array.Empty<string>(), "محيط المستطيل = 2 × (الطول + العرض). مثال: مستطيل طوله 4 وعرضه 3 فمحيطه 14", 10m)
        );

        // SCIENCE — Class 2/2
        await SeedOneAssignment(ctx, class4, "SCI", "واجب العلوم", 50,
            ("أي مما يلي حيوان لاحم؟", QuestionType.MultipleChoice, new[]{"الأسد","البقرة","الغزال","الحصان"}, "الأسد", 10m),
            ("الجهاز المسؤول عن ضخ الدم في الإنسان هو:", QuestionType.MultipleChoice, new[]{"القلب","الكبد","المعدة","الكلى"}, "القلب", 10m),
            ("النباتات تصنع غذائها بنفسها.", QuestionType.TrueFalse, new[]{"True","False"}, "True", 10m),
            ("الإنسان يحتاج إلى الأكسجين ليعيش.", QuestionType.TrueFalse, new[]{"True","False"}, "True", 10m),
            ("اشرح دورة حياة الفراشة.", QuestionType.Essay, Array.Empty<string>(), "بيضة ← يرقة ← شرنقة ← فراشة مكتملة", 10m)
        );

        // SOCIAL STUDIES — Class 3/1
        await SeedOneAssignment(ctx, class5, "SOC", "واجب الدراسات الاجتماعية", 50,
            ("محافظة الجيزة تشتهر بـ:", QuestionType.MultipleChoice, new[]{"الأهرامات","قناة السويس","السد العالي","الصحراء الغربية"}, "الأهرامات", 10m),
            ("عاصمة محافظة الإسكندرية هي:", QuestionType.MultipleChoice, new[]{"الإسكندرية","القاهرة","طنطا","المنصورة"}, "الإسكندرية", 10m),
            ("مصر بلد عربي وإفريقي.", QuestionType.TrueFalse, new[]{"True","False"}, "True", 10m),
            ("الصحراء الكبرى تقع في جنوب مصر.", QuestionType.TrueFalse, new[]{"True","False"}, "False", 10m),
            ("اكتب عن السياحة في مصر.", QuestionType.Essay, Array.Empty<string>(), "مصر تتميز بمعالم سياحية رائعة مثل الأهرامات والمتاحف والشواطئ الجميلة", 10m)
        );

        // ISLAMIC — Class 3/2
        await SeedOneAssignment(ctx, class6, "ISL", "واجب التربية الإسلامية", 50,
            ("كم عدد أسماء الله الحسنى؟", QuestionType.MultipleChoice, new[]{"99","100","50","77"}, "99", 10m),
            ("أركان الإيمان هي:", QuestionType.MultipleChoice, new[]{"ستة","خمسة","سبعة","ثمانية"}, "ستة", 10m),
            ("صلاة الفجر ركعتان.", QuestionType.TrueFalse, new[]{"True","False"}, "True", 10m),
            ("شهر رمضان هو شهر الصيام.", QuestionType.TrueFalse, new[]{"True","False"}, "True", 10m),
            ("اكتب 3 أحاديث عن فضل الصدقة.", QuestionType.Essay, Array.Empty<string>(), "قال رسول الله ﷺ: الصدقة تطفئ الخطيئة. قال: يد الله مع المتصدق. قال: داووا مرضاكم بالصدقة.", 10m)
        );

        // =================================================================
        // 26. Conversation + Participants + Message + BlockedUser
        // =================================================================
        var conversation = new Conversation
        {
            Title         = "محادثة تجريبية",
            Type          = ConversationType.Direct,
            LastMessageAt = now,
            CreatedAt = now, UpdatedAt = now
        };
        ctx.Conversations.Add(conversation);
        await ctx.SaveChangesAsync();

        var firstTeacher     = teacherUsers.First();
        var firstStudentUser = studentUsers.First();
        ctx.ConversationParticipants.AddRange(
            new ConversationParticipant
            {
                ConversationId = conversation.Id, UserId = firstTeacher.Id,
                JoinedAt = now, LastReadAt = now, CreatedAt = now, UpdatedAt = now
            },
            new ConversationParticipant
            {
                ConversationId = conversation.Id, UserId = firstStudentUser.Id,
                JoinedAt = now, CreatedAt = now, UpdatedAt = now
            }
        );
        ctx.Messages.Add(new Message
        {
            ConversationId = conversation.Id,
            SenderId       = firstTeacher.Id,
            Content        = "مرحباً، هل أتممت الواجب؟",
            SentAt         = now,
            CreatedAt = now, UpdatedAt = now
        });
        await ctx.SaveChangesAsync();

        // =================================================================
        // 27. Notifications
        // =================================================================
        ctx.Notifications.Add(new Notification
        {
            UserId   = firstStudentUser.Id,
            Title    = "واجب جديد",
            Body     = "تم إضافة واجب رياضيات جديد.",
            Type     = NotificationType.NewAssignment,
            DataJson = "{\"source\":\"seed\"}",
            CreatedAt = now, UpdatedAt = now
        });
        await ctx.SaveChangesAsync();

        // =================================================================
        // 28. Announcements
        // =================================================================
        var annWelcome = new Announcement
        {
            AuthorId = admin.Id,
            Title    = "مرحباً بكم في العام الدراسي 2025/2026",
            Body     = "يسعد إدارة المدرسة أن ترحب بجميع الطلاب وأولياء الأمور. نتمنى للجميع عاماً دراسياً موفقاً ومثمراً.",
            TargetRole = null,
            ExpiresAt  = new DateTime(2025, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt  = new DateTime(2025,  9,21, 8, 0, 0, DateTimeKind.Utc),
            UpdatedAt  = now
        };
        var annExams = new Announcement
        {
            AuthorId = admin.Id,
            Title    = "موعد امتحانات نصف العام",
            Body     = "تُعلن الإدارة عن بدء امتحانات نصف العام في 15 يناير 2026. يُرجى الاطلاع على الجدول المعتمد.",
            TargetRole = null,
            ExpiresAt  = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt  = new DateTime(2026, 1,  5, 8, 0, 0, DateTimeKind.Utc),
            UpdatedAt  = now
        };
        var annTeachers = new Announcement
        {
            AuthorId   = admin.Id,
            Title      = "تنبيه للمعلمين: رفع الدرجات",
            Body       = "يُرجى من جميع المعلمين الانتهاء من رفع درجات الفصل الأول قبل 20 يناير 2026.",
            TargetRole = UserRole.Teacher,
            ExpiresAt  = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt  = new DateTime(2026, 1, 10, 8, 0, 0, DateTimeKind.Utc),
            UpdatedAt  = now
        };
        var annResults = new Announcement
        {
            AuthorId   = admin.Id,
            Title      = "نتائج الفصل الأول متاحة",
            Body       = "يمكن لأولياء الأمور الاطلاع على نتائج الفصل الأول لأبنائهم من خلال تطبيق SchoolLink.",
            TargetRole = UserRole.Parent,
            ExpiresAt  = new DateTime(2026, 3,  1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt  = new DateTime(2026, 1, 22, 8, 0, 0, DateTimeKind.Utc),
            UpdatedAt  = now
        };
        ctx.Announcements.AddRange(annWelcome, annExams, annTeachers, annResults);
        await ctx.SaveChangesAsync();

        // =================================================================
        // 28b. AnnouncementUsers  (تتبّع "من قرأ الإعلان")
        //     لكل إعلان نربط عينة من المستخدمين: بعضهم قرأه وبعضهم لا.
        // =================================================================
        var announcementUserRows = new List<AnnouncementUser>();

        // إعلان الترحيب: قرأه غالبية المعلمين + بعض الطلاب
        announcementUserRows.AddRange(teacherUsers.Select(t => new AnnouncementUser
        {
            AnnouncementId = annWelcome.Id, UserId = t.Id,
            CreatedAt = now, UpdatedAt = now
        }));
        announcementUserRows.AddRange(studentUsers.Take(30).Select(s => new AnnouncementUser
        {
            AnnouncementId = annWelcome.Id, UserId = s.Id,
            CreatedAt = now, UpdatedAt = now
        }));

        // إعلان الامتحانات: قرأه نصف الطلاب تقريباً + كل أولياء الأمور تقريباً
        announcementUserRows.AddRange(studentUsers.Take(90).Select(s => new AnnouncementUser
        {
            AnnouncementId = annExams.Id, UserId = s.Id,
            CreatedAt = now, UpdatedAt = now
        }));
        announcementUserRows.AddRange(parentUsers.Take(120).Select(p => new AnnouncementUser
        {
            AnnouncementId = annExams.Id, UserId = p.Id,
            CreatedAt = now, UpdatedAt = now
        }));

        // إعلان المعلمين: قرأه 4 من 7 معلمين
        announcementUserRows.AddRange(teacherUsers.Take(4).Select(t => new AnnouncementUser
        {
            AnnouncementId = annTeachers.Id, UserId = t.Id,
            CreatedAt = now, UpdatedAt = now
        }));

        // إعلان النتائج: قرأه 60 ولي أمر فقط (الأغلبية لسه ما قرأتهش)
        announcementUserRows.AddRange(parentUsers.Take(60).Select(p => new AnnouncementUser
        {
            AnnouncementId = annResults.Id, UserId = p.Id,
            CreatedAt = now, UpdatedAt = now
        }));

        ctx.AnnouncementUsers.AddRange(announcementUserRows);
        await ctx.SaveChangesAsync();

        // =================================================================
        // 29. LibraryItems
        // =================================================================
        ctx.LibraryItems.AddRange(
            new LibraryItem
            {
                Title        = "سجل الرصد",
                ItemType     = LibraryItemType.File,
                FileUrl      = "https://dl.dropboxusercontent.com/scl/fi/bma8k570mpck582jg1wuh/.docx?rlkey=1he3059cyyvw1gnf4cy0kqrqe&dl=0",
                UploadedById = admin.Id,
                IsActive     = true,
                CreatedAt    = new DateTime(2026, 6, 7, 21, 0, 20, DateTimeKind.Utc),
                UpdatedAt    = now
            },
            new LibraryItem
            {
                Title        = "تجربه",
                ItemType     = LibraryItemType.Video,
                FileUrl      = "https://www.youtube.com/watch?v=bythRt5CIkI",
                SubjectId    = S["SCI"].Id,
                UploadedById = admin.Id,
                IsActive     = true,
                CreatedAt    = new DateTime(2026, 6, 7, 21, 3, 0, DateTimeKind.Utc),
                UpdatedAt    = now
            }
        );
        await ctx.SaveChangesAsync();

        // =================================================================
        // 30. ParentMeetingRequests  (طلبات مقابلات أولياء الأمور)
        //     نولّد مجموعة طلبات بحالات مختلفة (Pending / Approved / Rejected / Completed)
        // =================================================================
        // عيّن عيّنة من الطلاب وأولياء أمورهم لربط الطلبات بهم
        var sampleStudentsForMeetings = students.Take(8).ToList();
        var sampleParentsForMeetings  = sampleStudentsForMeetings
            .Select(st => parentStudentLinks.First(ps => ps.StudentId == st.Id))
            .ToList();

        var meetingReasons = new[]
        {
            "مناقشة مستوى الطالب الدراسي",
            "الاستفسار عن سلوك الطالب",
            "مناقشة نتيجة امتحانات الفصل الأول",
            "تنسيق خطة دعم للمواد التي يحتاجها الطالب"
        };
        var meetingNotes = new[]
        {
            "تم الاتفاق على متابعة الواجبات يومياً.",
            "يُوصى بجلسات تقوية في الرياضيات.",
            "الأداء ممتاز، شكراً لتعاونكم.",
            null
        };

        var meetingRequests = new (ParentStudent link, int delayDays, MeetingRequestStatus status, int handlerIndex)[]
        {
            (sampleParentsForMeetings[0], -20, MeetingRequestStatus.Completed, 0),
            (sampleParentsForMeetings[1], -15, MeetingRequestStatus.Completed, 1),
            (sampleParentsForMeetings[2], -10, MeetingRequestStatus.Approved,  2),
            (sampleParentsForMeetings[3],  -7, MeetingRequestStatus.Approved,  3),
            (sampleParentsForMeetings[4],  -5, MeetingRequestStatus.Rejected,  4),
            (sampleParentsForMeetings[5],  -3, MeetingRequestStatus.Rejected,  5),
            (sampleParentsForMeetings[6],  -2, MeetingRequestStatus.Pending,   -1),
            (sampleParentsForMeetings[7],  -1, MeetingRequestStatus.Pending,   -1),
        };

        var teacherHandlers = teacherUsers.ToList();
        var meetingRows = new List<ParentMeetingRequest>();
        for (int i = 0; i < meetingRequests.Length; i++)
        {
            var (link, delayDays, status, handlerIndex) = meetingRequests[i];
            var preferred = now.AddDays(delayDays).Date;
            var row = new ParentMeetingRequest
            {
                ParentId      = link.ParentId,
                StudentId     = link.StudentId,
                Reason        = meetingReasons[i % meetingReasons.Length],
                PreferredDate = preferred,
                Status        = status,
                Notes         = status == MeetingRequestStatus.Pending ? null : meetingNotes[i % meetingNotes.Length],
                HandledById   = handlerIndex >= 0 ? teacherHandlers[handlerIndex % teacherHandlers.Count].Id : null,
                ScheduledDate = status == MeetingRequestStatus.Approved || status == MeetingRequestStatus.Completed
                                ? preferred.AddDays(3) : null,
                CreatedAt = now.AddDays(delayDays), UpdatedAt = now,
            };
            meetingRows.Add(row);
        }
        ctx.ParentMeetingRequests.AddRange(meetingRows);
        await ctx.SaveChangesAsync();

        // =================================================================
        // 31. ResultVisibilitySettings
        // =================================================================
        ctx.ResultVisibilitySettings.AddRange(
            new ResultVisibilitySetting
            {
                AcademicYearId = year.Id, Term = AcademicTerm.FirstSemester,
                IsVisible      = true,
                VisibleFrom    = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc),
                VisibleUntil   = new DateTime(2026, 3,  1, 0, 0, 0, DateTimeKind.Utc),
                ControlledById = admin.Id, CreatedAt = now, UpdatedAt = now
            },
            new ResultVisibilitySetting
            {
                AcademicYearId = year.Id, Term = AcademicTerm.SecondSemester,
                IsVisible      = false,
                ControlledById = admin.Id, CreatedAt = now, UpdatedAt = now
            },
            new ResultVisibilitySetting
            {
                AcademicYearId = year.Id, Term = AcademicTerm.Final,
                IsVisible      = false,
                ControlledById = admin.Id, CreatedAt = now, UpdatedAt = now
            }
        );
        await ctx.SaveChangesAsync();

        // =================================================================
        // 32. RefreshToken + EmailOtp
        // =================================================================
        ctx.RefreshTokens.Add(new RefreshToken
        {
            UserId    = admin.Id,
            Token     = $"seed-{Guid.NewGuid():N}",
            ExpiresAt = now.AddDays(7),
            CreatedAt = now, UpdatedAt = now
        });
        ctx.EmailOtps.Add(new EmailOtp
        {
            UserId       = admin.Id,
            Email        = admin.ContactEmail!,
            CodeHash     = BCrypt.Net.BCrypt.HashPassword("123456"),
            ExpiresAt    = now.AddMinutes(10),
            UsedAt       = now,
            AttemptCount = 0,
            CreatedAt = now, UpdatedAt = now
        });
        await ctx.SaveChangesAsync();

        // =================================================================
        // 33. StudyPlan + StudyPlanItem
        // =================================================================
        var studyPlan = new StudyPlan
        {
            EnrollmentId  = firstEnr.Id,
            GeneratedByAI = false,
            StartDate     = DateOnly.FromDateTime(now),
            EndDate       = DateOnly.FromDateTime(now.AddDays(14)),
            IsActive      = true,
            RestDay       = 5,
            CreatedAt = now, UpdatedAt = now
        };
        ctx.StudyPlans.Add(studyPlan);
        await ctx.SaveChangesAsync();

        ctx.StudyPlanItems.Add(new StudyPlanItem
        {
            StudyPlanId = studyPlan.Id,
            SubjectId   = S["MTH"].Id,
            DayOfWeek   = 0,
            StartTime   = new TimeOnly(17, 0),
            EndTime     = new TimeOnly(18, 0),
            Topic       = "مراجعة المعادلات التربيعية",
            Notes       = "حل تمارين الكتاب ص 45-50.",
            CreatedAt = now, UpdatedAt = now
        });
        await ctx.SaveChangesAsync();

        // =================================================================
        // 34. LessonFeedback
        // =================================================================
        ctx.LessonFeedbacks.Add(new LessonFeedback
        {
            EnrollmentId          = firstEnr.Id,
            ClassSubjectTeacherId = mathCstClass1.Id,
            LessonDate            = DateOnly.FromDateTime(now),
            Rating                = 5,
            Understanding         = LessonUnderstanding.Yes,
            Comment               = "الشرح كان واضحاً ومفيداً.",
            CreatedAt = now, UpdatedAt = now
        });
        await ctx.SaveChangesAsync();

        // =================================================================
        // 35. AIGenerationLog
        // =================================================================
        ctx.AIGenerationLogs.Add(new AIGenerationLog
        {
            UserId        = firstTeacher.Id,
            OperationType = "SeedPreview",
            InputSummary  = "بيانات تجريبية للتأكد من عمل سجل عمليات الذكاء الاصطناعي.",
            IsSuccess     = true,
            TokensUsed    = 120,
            LatencyMs     = 450,
            CreatedAt = now, UpdatedAt = now
        });
        await ctx.SaveChangesAsync();

        // =================================================================
        // 35b. AIReports  (تقارير الذكاء الاصطناعي للطلاب/الفصول)
        //     تقرير للطالب الأول + تقرير للفصل class1 + توصيات عامة
        // =================================================================
        var firstStudentForReport = students.First();
        var latestPeriod = periodsList.OrderByDescending(p => p.Id).FirstOrDefault();

        ctx.AIReports.Add(new AIReport
        {
            StudentId   = firstStudentForReport.Id,
            PeriodId    = latestPeriod?.Id,
            ClassId     = class1.Id,
            Term        = AcademicTerm.SecondSemester,
            ReportType  = "Student",
            Summary     = "أداء الطالب جيد جداً مع تفوّق في الرياضيات والعلوم، يحتاج متابعة في اللغة الإنجليزية.",
            Content     = "تقرير أداء فردي للطالب:\n\n" +
                          "• نقاط القوة: استيعاب سريع للمفاهيم الرياضية، مشاركة فعّالة في الحصص.\n" +
                          "• مجالات التحسين: مهارات الكتابة في اللغة الإنجليزية.\n" +
                          "• التوصيات: حل تمارين إضافية للغة الإنجليزية بمعدل 3 مرات أسبوعياً.\n" +
                          "• معدل الحضور: 95% — ممتاز.\n" +
                          "• متوسط الدرجات: 87%.",
            IsPublished = true,
            CreatedAt = now, UpdatedAt = now
        });
        ctx.AIReports.Add(new AIReport
        {
            StudentId   = firstStudentForReport.Id,
            PeriodId    = latestPeriod?.Id,
            ClassId     = class1.Id,
            Term        = AcademicTerm.SecondSemester,
            ReportType  = "Class",
            Summary     = "أداء الفصل 1/1 متوازن عموماً، مع ارتفاع ملحوظ في مادة الرياضيات.",
            Content     = "تقرير أداء الفصل 1/1:\n\n" +
                          "• متوسط الدرجة الكلية للفصل: 82%.\n" +
                          "• أفضل مادة: الرياضيات (89%).\n" +
                          "• أضعف مادة: الدراسات الاجتماعية (74%).\n" +
                          "• نسبة الحضور: 92%.\n" +
                          "• عدد الطلاب المتفوقين: 12 من 30.\n" +
                          "• التوصيات: تكثيف المراجعات في الدراسات الاجتماعية، وإطلاق مسابقة في الرياضيات.",
            IsPublished = true,
            CreatedAt = now, UpdatedAt = now
        });
        ctx.AIReports.Add(new AIReport
        {
            StudentId   = firstStudentForReport.Id,
            PeriodId    = latestPeriod?.Id,
            ClassId     = class1.Id,
            Term        = AcademicTerm.SecondSemester,
            ReportType  = "Recommendations",
            Summary     = "خطة تطوير مقترحة للطالب خلال الأسابيع الأربعة القادمة.",
            Content     = "توصيات مخصصة:\n\n" +
                          "1. جدول مراجعة مكثّف للغة الإنجليزية أيام السبت والثلاثاء.\n" +
                          "2. الانضمام لمجموعة تقوية في الرياضيات للحفاظ على التميّز.\n" +
                          "3. قراءة قصة قصيرة أسبوعياً لتحسين مهارات الاستيعاب القرائي.\n" +
                          "4. متابعة أولي الأمر أسبوعياً لتقارير التقدّم.",
            IsPublished = false,
            CreatedAt = now, UpdatedAt = now
        });
        await ctx.SaveChangesAsync();

        // =================================================================
        // 36. AgentConversationMessage
        // =================================================================
        ctx.AgentConversationMessages.Add(new AgentConversationMessage
        {
            ConversationId = $"seed-agent-{Guid.NewGuid():N}",
            Sender         = "Teacher",
            Content        = "رسالة تجريبية لمساعد المعلم الذكي.",
            AgentType      = "TeacherAssistant",
            Timestamp      = now,
            CreatedAt = now, UpdatedAt = now
        });
        await ctx.SaveChangesAsync();

        // ── Seed current-week activity ──────────────────────────────────────
        await SeedCurrentWeekActivity(ctx);
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    /// <summary>
    /// شبكة أمان: الـ guard الرئيسي بيتأكد من جدول Users بس عشان يقرر إن قاعدة البيانات
    /// متهيأة بالفعل. لو جدول AcademicYears اتفضّى لأي سبب (تنظيف بيانات جزئي، اختبار،
    /// migration ناقص) بينما Users لسه فيه بيانات، الدالة دي بتنشئ سنة دراسية افتراضية
    /// واحدة وتخليها current، عشان أي صفحة بتعتمد على /api/academic-years ما تفضلش مقفولة.
    /// لا تعمل حاجة لو فيه سنة دراسية واحدة على الأقل بالفعل (حتى لو IsDeleted).
    /// </summary>
    private static async Task EnsureCurrentAcademicYearExists(AppDbContext ctx)
    {
        bool hasAnyYear = await ctx.AcademicYears.IgnoreQueryFilters().AnyAsync();
        if (hasAnyYear) return;

        Serilog.Log.Warning("جدول AcademicYears فاضي رغم وجود بيانات مستخدمين — يتم إنشاء سنة دراسية افتراضية تلقائيًا");

        var now = DateTime.UtcNow;
        var fallbackYear = new AcademicYear
        {
            Name      = $"{now.Year}/{now.Year + 1}",
            StartDate = new DateOnly(now.Year, 9, 1),
            EndDate   = new DateOnly(now.Year + 1, 6, 30),
            IsCurrent = true,
            CreatedAt = now, UpdatedAt = now
        };
        ctx.AcademicYears.Add(fallbackYear);
        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds DailyAbsence rows for the current week so dashboard charts always have data.
    /// Safe to call on an empty DB (exits early) and fully idempotent (skips if week already seeded).
    /// </summary>
    private static async Task SeedCurrentWeekActivity(AppDbContext ctx)
    {
        // Guard: nothing to seed if no base data exists yet
        bool anyEnrollments = await ctx.StudentEnrollments.AnyAsync();
        if (!anyEnrollments) return;

        // Current week boundaries (Sunday → Saturday)
        var today       = DateOnly.FromDateTime(DateTime.UtcNow);
        var weekStart   = today.AddDays(-(int)today.DayOfWeek);          // Sunday
        var weekEnd     = weekStart.AddDays(4);                           // Thursday (school days)

        // Idempotency: skip if this week is already seeded
        bool alreadySeeded = await ctx.DailyAbsences
            .AnyAsync(a => a.AbsenceDate >= weekStart && a.AbsenceDate <= weekEnd);
        if (alreadySeeded) return;

        // Fetch a handful of enrollments across different classes
        var enrollments = await ctx.StudentEnrollments
            .OrderBy(e => e.ClassId).ThenBy(e => e.Id)
            .Take(15)
            .ToListAsync();

        // One CST per class (any subject)
        var classIds = enrollments.Select(e => e.ClassId).Distinct().ToList();
        var cstByClass = await ctx.ClassSubjectTeachers
            .Where(c => classIds.Contains(c.ClassId))
            .GroupBy(c => c.ClassId)
            .ToDictionaryAsync(g => g.Key, g => g.First());

        // Current evaluation period (if any)
        var currentPeriod = await ctx.EvaluationPeriods
            .Where(p => p.StartDate <= today && p.EndDate >= today)
            .FirstOrDefaultAsync();

        var now     = DateTime.UtcNow;
        var rng     = new Random();
        var batch   = new List<DailyAbsence>();

        // ~30 % of the fetched students get one absence somewhere in the current week
        foreach (var enr in enrollments)
        {
            if (rng.NextDouble() >= 0.30) continue;

            if (!cstByClass.TryGetValue(enr.ClassId, out var cst)) continue;

            var absenceDate = weekStart.AddDays(rng.Next(0, 5));   // Sun–Thu

            batch.Add(new DailyAbsence
            {
                EnrollmentId          = enr.Id,
                ClassSubjectTeacherId = cst.Id,
                AbsenceDate           = absenceDate,
                IsAbsent              = true,
                Reason                = "مرض",
                PeriodId              = currentPeriod?.Id,
                RecordedById          = cst.TeacherId,
                CreatedAt             = now,
                UpdatedAt             = now
            });
        }

        if (batch.Count > 0)
        {
            ctx.DailyAbsences.AddRange(batch);
            await ctx.SaveChangesAsync();
        }
    }

    /// <summary>Builds EvaluationItem list from a compact definition array.</summary>
    private static List<EvaluationItem> BuildItems(
        int templateId,
        DateTime now,
        (string name, decimal max, ItemType type, AutoCalcType auto)[] defs)
        => defs.Select((d, i) => new EvaluationItem
        {
            TemplateId   = templateId,
            Name         = d.name,
            MaxScore     = d.max,
            Weight       = 1,
            ItemType     = d.type,
            AutoCalcType = d.auto,
            DisplayOrder = i + 1,
            IsVisible    = true,
            CreatedAt = now, UpdatedAt = now
        }).ToList();

    /// <summary>Seeds Units and Lessons for all 7 subjects with rich real-world content.</summary>
    private static async Task SeedUnitsAndLessons(
        AppDbContext ctx,
        Dictionary<string, Subject> S,
        DateTime now,
        GradeLevel grade1,
        GradeLevel grade2,
        GradeLevel grade3)
    {
        // Helper: add a unit with optional lessons
        async Task<Unit> AddUnit(Subject subject, string name, int order,
                                  AcademicTerm? term = null,
                                  (string title, string content, int dispOrder)[]? lessons = null,
                                  GradeLevel? gradeLevel = null)
        {
            var unit = new Unit
            {
                SubjectId    = subject.Id,
                GradeLevelId = (gradeLevel ?? grade1).Id,
                Name         = name,
                DisplayOrder = order,
                Term         = term,
                CreatedAt    = now, UpdatedAt = now
            };
            ctx.Units.Add(unit);
            await ctx.SaveChangesAsync();

            if (lessons != null)
            {
                ctx.Lessons.AddRange(lessons.Select(l => new Lesson
                {
                    UnitId       = unit.Id,
                    Title        = l.title,
                    Content      = l.content,
                    DisplayOrder = l.dispOrder,
                    CreatedAt    = now, UpdatedAt = now
                }));
                await ctx.SaveChangesAsync();
            }
            return unit;
        }

        // ── الرياضيات ──────────────────────────────────────────────────────
        await AddUnit(S["MTH"], "الوحدة الأولى: الأعداد والجبر", 1,
            AcademicTerm.FirstSemester, new[]
        {
            ("المعادلات التربيعية",
             "المعادلة التربيعية هي معادلة من الدرجة الثانية تأخذ الصورة: ax² + bx + c = 0 حيث a ≠ 0.\n\n" +
             "طرق حل المعادلة التربيعية:\n" +
             "أولاً: التحليل إلى العوامل:\n" +
             "- نبحث عن عددين حاصل ضربهما = a × c ومجموعهما = b\n" +
             "- مثال: حل المعادلة x² + 5x + 6 = 0\n" +
             "  a=1, b=5, c=6 → العددان 2 و 3 (2×3=6, 2+3=5)\n" +
             "  (x+2)(x+3)=0 → x=-2 أو x=-3\n\n" +
             "ثانياً: القانون العام:\n" +
             "x = (-b ± √(b² - 4ac)) / 2a\n" +
             "المقدار (b² - 4ac) يسمى المميز ويرمز له بالرمز Δ\n\n" +
             "حالات المميز Δ:\n" +
             "- إذا كان Δ > 0: للمعادلة حلان حقيقيان مختلفان\n" +
             "- إذا كان Δ = 0: للمعادلة حل حقيقي واحد (مكرر)\n" +
             "- إذا كان Δ < 0: لا يوجد حلول حقيقية (حلان مركبان)\n\n" +
             "مثال تطبيقي:\n" +
             "حل المعادلة 2x² - 4x - 6 = 0\n" +
             "a=2, b=-4, c=-6\n" +
             "Δ = (-4)² - 4(2)(-6) = 16 + 48 = 64\n" +
             "x = (4 ± √64) / 4 = (4 ± 8) / 4\n" +
             "x₁ = (4+8)/4 = 3    ,    x₂ = (4-8)/4 = -1",
             1),
            ("الدوال الخطية",
             "الدالة الخطية هي دالة من الدرجة الأولى تأخذ الصورة: f(x) = mx + b\n" +
             "حيث m هو ميل الخط المستقيم و b هو المقطع الصادي (نقطة تقاطع الخط مع محور الصادات).\n\n" +
             "خصائص الدالة الخطية:\n" +
             "- تمثيلها البياني خط مستقيم\n" +
             "- إذا كان m > 0: الدالة متزايدة (تتجه لأعلى)\n" +
             "- إذا كان m < 0: الدالة متناقصة (تتجه لأسفل)\n" +
             "- إذا كان m = 0: الدالة ثابتة (خط أفقي)\n\n" +
             "حساب الميل:\n" +
             "ميل الخط المار بالنقطتين (x₁,y₁) و (x₂,y₂) هو:\n" +
             "m = (y₂ - y₁) / (x₂ - x₁)\n\n" +
             "مثال:\n" +
             "أوجد ميل الدالة الخطية المارة بالنقطتين (1,3) و (4,9):\n" +
             "m = (9-3) / (4-1) = 6/3 = 2\n" +
             "إذا كانت b = 1 فإن f(x) = 2x + 1\n\n" +
             "تطبيقات على الدوال الخطية:\n" +
             "- العلاقة بين المسافة والزمن في الحركة المنتظمة\n" +
             "- التحويل بين درجات الحرارة (فهرنهايت وسلسيوس)\n" +
             "- حساب التكلفة الكلية = تكلفة ثابتة + (سعر الوحدة × العدد)",
             2),
            ("الأعداد الحقيقية",
             "الأعداد الحقيقية تنقسم إلى:\n\n" +
             "أولاً: الأعداد النسبية (Q):\n" +
             "- هي الأعداد التي يمكن كتابتها على صورة كسر a/b حيث b ≠ 0\n" +
             "- تشمل: الأعداد الصحيحة (Z) والأعداد العشرية المنتهية والدورية\n" +
             "- مثال: ½, 0.75, -3, 2.333...\n\n" +
             "ثانياً: الأعداد غير النسبية (Q'):\n" +
             "- هي الأعداد التي لا يمكن كتابتها على صورة كسر\n" +
             "- أرقامها العشرية لا نهائية وغير دورية\n" +
             "- مثال: π = 3.14159..., √2 = 1.41421..., e = 2.71828...\n\n" +
             "خصائص الأعداد الحقيقية:\n" +
             "- خاصية الإبدال: a + b = b + a و a × b = b × a\n" +
             "- خاصية التجميع: (a+b)+c = a+(b+c) و (a×b)×c = a×(b×c)\n" +
             "- خاصية التوزيع: a × (b+c) = a×b + a×c\n" +
             "- العنصر المحايد: a + 0 = a و a × 1 = a\n" +
             "- النظير: a + (-a) = 0 و a × (1/a) = 1 حيث a ≠ 0",
             3),
        });
        await AddUnit(S["MTH"], "الوحدة الثانية: الهندسة", 2,
            AcademicTerm.SecondSemester, new[]
        {
            ("نظرية فيثاغورس",
             "نظرية فيثاغورس من أشهر النظريات في الرياضيات، وتتعلق بالمثلث قائم الزاوية.\n\n" +
             "النظرية:\n" +
             "في أي مثلث قائم الزاوية، مربع طول الوتر يساوي مجموع مربعي طولي الضلعين الآخرين.\n\n" +
             "الصيغة: a² + b² = c²\n" +
             "حيث c هو طول الوتر (أطول ضلع)، a و b هما طولا الضلعين القائمين.\n\n" +
             "مثال 1:\n" +
             "إذا كان a = 3 سم، b = 4 سم، فإن:\n" +
             "c² = 3² + 4² = 9 + 16 = 25\n" +
             "c = √25 = 5 سم\n\n" +
             "مثال 2:\n" +
             "سلم طوله 10 أمتار يستند إلى حائط، قاعدته تبعد 6 أمتار عن الحائط.\n" +
             "كم ارتفاع الحائط الذي يصل إليه السلم؟\n" +
             "a² + 6² = 10² → a² + 36 = 100 → a² = 64 → a = 8 أمتار\n\n" +
             "عكس نظرية فيثاغورس:\n" +
             "إذا كان في مثلث: a² + b² = c² فإن المثلث قائم الزاوية.\n\n" +
             "تطبيقات حياتية:\n" +
             "- حساب الأبعاد في البناء والهندسة المعمارية\n" +
             "- تحديد المسافات في الملاحة والخرائط\n" +
             "- تصميم السلالم والأسطح المائلة",
             1),
            ("مساحات الأشكال الهندسية",
             "المساحة هي المنطقة المحصورة داخل شكل هندسي، وتُقاس بالوحدات المربعة.\n\n" +
             "مساحات الأشكال الأساسية:\n\n" +
             "1. المربع:\n" +
             "   المساحة = طول الضلع × نفسه = s²\n" +
             "   مثال: مربع طول ضلعه 5 سم → المساحة = 25 سم²\n\n" +
             "2. المستطيل:\n" +
             "   المساحة = الطول × العرض = L × W\n" +
             "   مثال: مستطيل طوله 8 سم وعرضه 3 سم → المساحة = 24 سم²\n\n" +
             "3. المثلث:\n" +
             "   المساحة = ½ × القاعدة × الارتفاع\n" +
             "   مثال: مثلث قاعدته 10 سم وارتفاعه 6 سم → المساحة = 30 سم²\n\n" +
             "4. الدائرة:\n" +
             "   المساحة = π × r² (حيث π ≈ 3.14)\n" +
             "   مثال: دائرة نصف قطرها 7 سم → المساحة = 3.14 × 49 = 153.86 سم²\n\n" +
             "5. شبه المنحرف:\n" +
             "   المساحة = ½ × (مجموع القاعدتين) × الارتفاع\n\n" +
             "6. متوازي الأضلاع:\n" +
             "   المساحة = القاعدة × الارتفاع\n\n" +
             "مثال تطبيقي:\n" +
             "غرفة مستطيلة طولها 6 م وعرضها 4 م، نريد تغطية أرضيتها بالسجاد.\n" +
             "المساحة = 6 × 4 = 24 متراً مربعاً",
             2),
            ("حساب الحجم",
             "الحجم هو مقدار الفراغ الذي يشغله جسم ثلاثي الأبعاد، ويُقاس بالوحدات المكعبة.\n\n" +
             "أحجام المجسمات الأساسية:\n\n" +
             "1. المكعب:\n" +
             "   الحجم = طول الضلع³ = s³\n" +
             "   مثال: مكعب طول ضلعه 4 سم → الحجم = 64 سم³\n\n" +
             "2. متوازي المستطيلات:\n" +
             "   الحجم = الطول × العرض × الارتفاع\n" +
             "   مثال: صندوق طوله 10 سم، عرضه 6 سم، ارتفاعه 4 سم → الحجم = 240 سم³\n\n" +
             "3. الأسطوانة:\n" +
             "   الحجم = π × r² × h (حيث r نصف القطر، h الارتفاع)\n" +
             "   مثال: أسطوانة نصف قطرها 5 سم وارتفاعها 10 سم → الحجم = 3.14 × 25 × 10 = 785 سم³\n\n" +
             "4. الكرة:\n" +
             "   الحجم = 4/3 × π × r³\n\n" +
             "5. المخروط:\n" +
             "   الحجم = 1/3 × π × r² × h\n\n" +
             "تطبيقات حياتية:\n" +
             "- حساب سعة خزانات المياه\n" +
             "- تقدير كمية المواد اللازمة للبناء\n" +
             "- حساب حجم العبوات والتغليف",
             3),
        });

        // ── العلوم ─────────────────────────────────────────────────────────
        await AddUnit(S["SCI"], "الوحدة الأولى: الكيمياء", 1,
            AcademicTerm.FirstSemester, new[]
        {
            ("المادة وخصائصها",
             "المادة هي كل شيء له كتلة ويشغل حيزاً من الفراغ. تتكون المادة من جسيمات صغيرة جداً تسمى الذرات والجزيئات.\n\n" +
             "حالات المادة:\n" +
             "1. الحالة الصلبة: جسيماتها متراصة ومنتظمة، لها شكل وحجم ثابتان.\n" +
             "   - مثال: الثلج، الحديد، الخشب\n" +
             "2. الحالة السائلة: جسيماتها متحركة وغير منتظمة، تأخذ شكل الإناء الذي توضع فيه.\n" +
             "   - مثال: الماء، الزيت، الحليب\n" +
             "3. الحالة الغازية: جسيماتها متباعدة جداً وتتحرك بحرية، ليس لها شكل أو حجم ثابت.\n" +
             "   - مثال: الهواء، الأكسجين، ثاني أكسيد الكربون\n\n" +
             "الخصائص الفيزيائية للمادة:\n" +
             "- الكثافة = الكتلة ÷ الحجم (جم/سم³)\n" +
             "- درجة الانصهار: درجة الحرارة التي تتحول عندها المادة من صلبة إلى سائلة\n" +
             "- درجة الغليان: درجة الحرارة التي تتحول عندها المادة من سائلة إلى غازية\n" +
             "- التوصيل الكهربائي والحراري\n" +
             "- الذوبان: قدرة المادة على الذوبان في مذيب\n\n" +
             "الخصائص الكيميائية للمادة:\n" +
             "- القابلية للاشتعال\n" +
             "- التفاعل مع الأحماض والقواعد\n" +
             "- التأكسد والاختزال\n" +
             "- التحلل الكهربائي",
             1),
            ("التفاعلات الكيميائية",
             "التفاعل الكيميائي هو عملية تحوّل مادة أو أكثر (تُسمى المتفاعلات) إلى مادة أو أكثر جديدة (تُسمى النواتج) تختلف في خصائصها.\n\n" +
             "علامات حدوث التفاعل الكيميائي:\n" +
             "1. تغير في اللون\n" +
             "2. انبعاث غازات (ظهور فقاعات)\n" +
             "3. تغير في درجة الحرارة (امتصاص أو انبعاث حرارة)\n" +
             "4. تكون راسب (مادة صلبة)\n" +
             "5. انبعاث ضوء أو رائحة\n\n" +
             "أنواع التفاعلات الكيميائية:\n\n" +
             "1. تفاعل الاتحاد: A + B → AB\n" +
             "   مثال: 2H₂ + O₂ → 2H₂O (اتحاد الهيدروجين مع الأكسجين لتكوين الماء)\n\n" +
             "2. تفاعل التحلل: AB → A + B\n" +
             "   مثال: 2H₂O → 2H₂ + O₂ (تحلل الماء بالكهرباء)\n\n" +
             "3. تفاعل الإحلال البسيط: A + BC → AC + B\n" +
             "   مثال: Zn + 2HCl → ZnCl₂ + H₂\n\n" +
             "4. تفاعل الإحلال المزدوج: AB + CD → AD + CB\n" +
             "   مثال: NaCl + AgNO₃ → AgCl↓ + NaNO₃\n\n" +
             "قانون حفظ الكتلة:\n" +
             "في أي تفاعل كيميائي، مجموع كتل المتفاعلات = مجموع كتل النواتج.\n" +
             "اكتشف هذا القانون العالم الفرنسي لافوازييه.",
             2),
            ("الذرة والجزيئات",
             "الذرة هي أصغر وحدة بنائية للمادة وتحافظ على الخصائص الكيميائية للعنصر.\n\n" +
             "تركيب الذرة:\n" +
             "1. النواة: في مركز الذرة وتحتوي على:\n" +
             "   - البروتونات (شحنة موجبة +)\n" +
             "   - النيوترونات (شحنة متعادلة)\n" +
             "2. الإلكترونات (شحنة سالبة -): تدور حول النواة في مدارات (مستويات طاقة)\n\n" +
             "العدد الذري = عدد البروتونات\n" +
             "العدد الكتلي = عدد البروتونات + عدد النيوترونات\n\n" +
             "مثال: ذرة الكربون\n" +
             "- العدد الذري = 6 (6 بروتونات)\n" +
             "- العدد الكتلي = 12 (6 بروتونات + 6 نيوترونات)\n" +
             "- عدد الإلكترونات = 6\n\n" +
             "الجزيء:\n" +
             "- هو مجموعة من ذرتين أو أكثر مرتبطة بروابط كيميائية\n" +
             "- مثال: جزيء الماء H₂O (ذرتا هيدروجين + ذرة أكسجين)\n" +
             "- مثال: جزيء الأكسجين O₂ (ذرتا أكسجين)\n\n" +
             "الجدول الدوري:\n" +
             "- ترتيب العناصر حسب العدد الذري\n" +
             "- يحتوي على 118 عنصراً معروفاً\n" +
             "- العناصر في نفس العمود لها خصائص متشابهة",
             3),
        });
        await AddUnit(S["SCI"], "الوحدة الثانية: الأحياء", 2,
            AcademicTerm.SecondSemester, new[]
        {
            ("انقسام الخلايا: الميتوز والميوز",
             "الخلية هي الوحدة الأساسية للحياة، وتنقسم لنمو الكائن الحي وتعويض الخلايا التالفة.\n\n" +
             "أولاً: الانقسام الميتوزي (الانقسام المتساوي):\n" +
             "يحدث في الخلايا الجسدية وينتج خليتين متماثلتين (2n).\n\n" +
             "مراحل الميتوز:\n" +
             "1. الطور التمهيدي: تتكثف الكروموسومات، يختفي الغشاء النووي\n" +
             "2. الطور الاستوائي: تصطف الكروموسومات في منتصف الخلية\n" +
             "3. الطور الانفصالي: تنفصل الكروماتيدات الشقيقة وتتجه لأقطاب الخلية\n" +
             "4. الطور النهائي: يتكون غشاء نووي جديد، ينقسم السيتوبلازم\n\n" +
             "أهمية الميتوز:\n" +
             "- نمو الكائن الحي\n" +
             "- تعويض الخلايا التالفة\n" +
             "- التكاثر اللاجنسي\n\n" +
             "ثانياً: الانقسام الميوزي (الانقسام الاختزالي):\n" +
             "يحدث في الخلايا التناسلية وينتج أربع خلايا غير متماثلة (n).\n\n" +
             "مراحل الميوز:\n" +
             "- انقسام أول: يفصل الكروموسومات المتماثلة\n" +
             "- انقسام ثان: يفصل الكروماتيدات الشقيقة\n" +
             "- الناتج: 4 خلايا أحادية المجموعة الكروموسومية (n)\n\n" +
             "أهمية الميوز:\n" +
             "- إنتاج الأمشاج (الحيوانات المنوية والبويضات)\n" +
             "- زيادة التنوع الوراثي من خلال العبور الجيني\n\n" +
             "الفرق بين الميتوز والميوز:\n" +
             "- الميتوز: خليتين (2n) ← يستخدم للنمو\n" +
             "- الميوز: أربع خلايا (n) ← يستخدم للتكاثر الجنسي",
             1),
            ("التركيب الضوئي",
             "التركيب الضوئي (البناء الضوئي) هو العملية التي تستخدم فيها النباتات الخضراء الطاقة الضوئية لتحويل ثاني أكسيد الكربون والماء إلى غذاء (جلوكوز) وأكسجين.\n\n" +
             "المعادلة العامة:\n" +
             "6CO₂ + 6H₂O + طاقة ضوئية → C₆H₁₂O₆ + 6O₂\n\n" +
             "أهمية عملية التركيب الضوئي:\n" +
             "1. إنتاج الغذاء للنبات والكائنات الحية الأخرى\n" +
             "2. إنتاج الأكسجين اللازم للتنفس\n" +
             "3. استهلاك ثاني أكسيد الكربون من الجو\n" +
             "4. المصدر الأساسي للطاقة في السلسلة الغذائية\n\n" +
             "العوامل المؤثرة على التركيب الضوئي:\n" +
             "- شدة الإضاءة: كلما زادت شدة الإضاءة زاد معدل التركيب الضوئي حتى حد معين\n" +
             "- تركيز ثاني أكسيد الكربون: يزيد التركيز يزيد المعدل\n" +
             "- درجة الحرارة: المعدل الأمثل بين 25-30°م\n" +
             "- توفر الماء: نقص الماء يقلل المعدل\n\n" +
             "أجزاء النبات المسؤولة:\n" +
             "- البلاستيدات الخضراء (الكلوروفيل): تمتص الطاقة الضوئية\n" +
             "- الثغور: تدخل ثاني أكسيد الكربون وتخرج الأكسجين\n" +
             "- الجذور: تمتص الماء والأملاح المعدنية",
             2),
        });

        // ── اللغة العربية ──────────────────────────────────────────────────
        await AddUnit(S["ARB"], "الوحدة الأولى: القراءة والنصوص", 1,
            AcademicTerm.FirstSemester, new[]
        {
            ("النصوص الأدبية",
             "النص الأدبي هو عمل فني يهدف إلى التعبير عن المشاعر والأفكار بلغة جمالية مؤثرة.\n\n" +
             "خصائص النص الأدبي:\n" +
             "1. العاطفة: المشاعر التي يثيرها الكاتب في نفسه وينقلها للقارئ\n" +
             "2. الأفكار: المعاني والأهداف التي يسعى الكاتب لتوصيلها\n" +
             "3. الصور الفنية: استخدام التشبيه والاستعارة والكناية\n" +
             "4. الأسلوب: طريقة التعبير المميزة لكل كاتب\n" +
             "5. الإيقاع الموسيقي: الجرس الموسيقي للألفاظ والعبارات\n\n" +
             "أنواع النصوص الأدبية:\n" +
             "1. الشعر: كلام موزون مقفّى يعبر عن المشاعر\n" +
             "   مثال: قصيدة 'ولد الهدى' لأحمد شوقي\n" +
             "2. القصة: سرد أحداث متصلة بشخصيات ومكان وزمان\n" +
             "3. المسرحية: نص يُكتب ليمثَّل على المسرح\n" +
             "4. المقال: نص نثري يعرض فكرة أو رأياً بأسلوب منهجي\n\n" +
             "عناصر القصة:\n" +
             "- الشخصيات: أبطال القصة\n" +
             "- الأحداث: ما يحدث في القصة\n" +
             "- الزمان والمكان: زمن ومكان الأحداث\n" +
             "- العقدة: المشكلة الرئيسية\n" +
             "- الحل: نهاية القصة\n\n" +
             "مثال من الشعر العربي:\n" +
             "قال المتنبي:\n" +
             "إذا رأيت نيوب الليث بارزةً    فلا تظنن أن الليث يبتسم",
             1),
            ("القراءة التحليلية",
             "القراءة التحليلية هي قراءة متعمقة تهدف إلى فهم النص فهماً كاملاً وتذوقه واستخراج أفكاره.\n\n" +
             "خطوات القراءة التحليلية:\n" +
             "1. القراءة الاستطلاعية: قراءة سريعة للتعرف على موضوع النص\n" +
             "2. فهم المفردات: البحث عن معاني الكلمات الصعبة\n" +
             "3. تحديد الأفكار الرئيسية: الفكرة العامة التي يدور حولها النص\n" +
             "4. تحديد الأفكار الفرعية: الأفكار التي تدعم الفكرة الرئيسية\n" +
             "5. تحليل الصور الفنية: دراسة التشبيهات والاستعارات\n" +
             "6. استخلاص العبر والدروس: المغزى من النص\n\n" +
             "أدوات التحليل الأدبي:\n" +
             "- التشبيه: إشراك أمرين في صفة معينة (مثل: كأنه القمر في الجمال)\n" +
             "- الاستعارة: تشبيه حذف أحد طرفيه (مثال: زارني الأسد في مكتبه)\n" +
             "- الكناية: تعبير لا يقصد به المعنى الحرفي (مثال: فلان طويل النجاد = طويل القامة)\n" +
             "- الطباق: الجمع بين ضدين (مثال: الليل والنهار)\n" +
             "- المقابلة: الجمع بين معنيين متقابلين\n\n" +
             "تطبيق:\n" +
             "قال تعالى: {وَمَن يُؤْتَ الْحِكْمَةَ فَقَدْ أُوتِيَ خَيْراً كَثِيراً}\n" +
             "- الفكرة: فضل الحكمة\n" +
             "- الأسلوب: شرط وجواب شرط\n" +
             "- البلاغة: أسلوب القصر",
             2),
        });
        await AddUnit(S["ARB"], "الوحدة الثانية: النحو والصرف", 2,
            AcademicTerm.SecondSemester, new[]
        {
            ("أقسام الكلام",
             "الكلمة في اللغة العربية تنقسم إلى ثلاثة أقسام رئيسية:\n\n" +
             "أولاً: الاسم\n" +
             "- هو كلمة تدل على إنسان أو حيوان أو نبات أو جماد أو مكان أو زمان أو صفة أو معنى مجرد\n" +
             "- علامات الاسم: يقبل النداء (يا محمد)، يقبل التنوين (محمدٌ)، يقبل أل التعريف (الكتاب)\n" +
             "- أنواع الاسم: اسم علم (محمد)، اسم جنس (رجل)، اسم إشارة (هذا)، اسم موصول (الذي)\n\n" +
             "ثانياً: الفعل\n" +
             "- هو كلمة تدل على حدث مرتبط بزمن\n" +
             "- علامات الفعل: يقبل تاء الفاعل (ذهبتُ)، ويقبل سوف (سوف يكتب)\n" +
             "- أنواع الفعل:\n" +
             "  أ. الفعل الماضي: حدث في الزمن الماضي (ذهب، كتب، أكل)\n" +
             "  ب. الفعل المضارع: حدث في الزمن الحالي أو المستقبل (يذهب، يكتب)\n" +
             "  ج. فعل الأمر: طلب حدوث الفعل في المستقبل (اذهب، اكتب)\n\n" +
             "ثالثاً: الحرف\n" +
             "- هو كلمة تدل على معنى في غيرها\n" +
             "- لا تقبل علامات الاسم ولا الفعل\n" +
             "- أمثلة: من، إلى، عن، في، على، هل، لم، لعلّ\n" +
             "- الحروف العاملة: إنّ وأخواتها، كان وأخواتها، حروف الجر\n\n" +
             "مثال تطبيقي:\n" +
             "جملة: 'يكتب الطالب الدرس بقلمه'\n" +
             "- يكتب: فعل مضارع\n" +
             "- الطالب: اسم (فاعل)\n" +
             "- الدرس: اسم (مفعول به)\n" +
             "- بقلمه: الباء حرف جر، قلم اسم مجرور",
             1),
            ("المبتدأ والخبر",
             "الجملة الاسمية هي الجملة التي تبدأ باسم، وتتكون من ركنين أساسيين: المبتدأ والخبر.\n\n" +
             "المبتدأ:\n" +
             "- اسم مرفوع في أول الجملة الاسمية\n" +
             "- يأتي غالباً معرفة (بأل التعريف أو بالإضافة)\n" +
             "- مثال: العلمُ نورٌ (العلم: مبتدأ مرفوع بالضمة)\n\n" +
             "الخبر:\n" +
             "- ما يتم به المعنى مع المبتدأ\n" +
             "- يكون مرفوعاً\n" +
             "- مثال: العلمُ نورٌ (نور: خبر مرفوع بالضمة)\n\n" +
             "أنواع الخبر:\n" +
             "1. خبر مفرد: كلمة واحدة (السماء صافية)\n" +
             "2. خبر جملة (اسمية أو فعلية):\n" +
             "   - جملة اسمية: الطالب أخلاقه حسنة\n" +
             "   - جملة فعلية: الطالب يدرس بجد\n" +
             "3. خبر شبه جملة:\n" +
             "   - جار ومجرور: الكتاب في الحقيبة\n" +
             "   - ظرف: الحديقة أمام المنزل\n\n" +
             "حالات المبتدأ والخبر:\n" +
             "- يجب أن يتطابق المبتدأ والخبر في النوع (تذكير وتأنيث)\n" +
             "- يجب أن يتطابقا في العدد (مفرد، مثنى، جمع)\n\n" +
             "أمثلة تطبيقية:\n" +
             "1. المعلمُ مخلصٌ (معلم: مبتدأ مرفوع، مخلص: خبر مرفوع)\n" +
             "2. المدرسةُ واسعةٌ (مدرسة: مبتدأ مرفوع، واسعة: خبر مرفوع)\n" +
             "3. الفلاحون يعملون في الحقل (فلاحون: مبتدأ، جملة يعملون: خبر)",
             2),
        });

        // ── اللغة الإنجليزية ───────────────────────────────────────────────
        await AddUnit(S["ENG"], "Unit 1: Greetings and Introductions", 1,
            AcademicTerm.FirstSemester, new[]
        {
            ("Hello and Goodbye",
             "Greetings are the first step in any conversation. Learning how to greet people properly is essential.\n\n" +
             "Formal Greetings:\n" +
             "- Good morning (used before 12 PM)\n" +
             "- Good afternoon (used from 12 PM to 6 PM)\n" +
             "- Good evening (used after 6 PM)\n" +
             "- How do you do? (very formal)\n\n" +
             "Informal Greetings:\n" +
             "- Hello!\n" +
             "- Hi!\n" +
             "- Hey! (very casual)\n" +
             "- What's up? (casual)\n" +
             "- How's it going?\n\n" +
             "Farewells:\n" +
             "- Goodbye\n" +
             "- Bye!\n" +
             "- See you later!\n" +
             "- See you tomorrow!\n" +
             "- Take care!\n" +
             "- Have a nice day!\n\n" +
             "Example Dialogue:\n" +
             "Ahmed: Good morning, Mr. Smith!\n" +
             "Mr. Smith: Good morning, Ahmed! How are you today?\n" +
             "Ahmed: I'm fine, thank you. And you?\n" +
             "Mr. Smith: I'm very well, thanks.\n\n" +
             "Self-Introduction:\n" +
             "- My name is...\n" +
             "- I am from...\n" +
             "- Nice to meet you!\n" +
             "- Pleased to meet you!\n\n" +
             "Practice Exercise:\n" +
             "Fill in the blanks:\n" +
             "1. ___ morning! (Answer: Good)\n" +
             "2. Nice to ___ you! (Answer: meet)\n" +
             "3. See you ___! (Answer: later/tomorrow)",
             1),
            ("Introducing Yourself and Others",
             "Introducing yourself and others is an important social skill. Here's how to do it correctly.\n\n" +
             "Introducing Yourself:\n" +
             "- Hello, I'm Ahmed. I'm a student at Al-Nile School.\n" +
             "- Hi, my name is Sara. I'm from Cairo.\n" +
             "- I'm 13 years old. I live in Giza.\n\n" +
             "Introducing Others:\n" +
             "- This is my friend, Omar.\n" +
             "- I'd like you to meet my teacher, Mr. Hassan.\n" +
             "- Meet my classmate, Layla.\n" +
             "- Have you met Ahmed? He's in my class.\n\n" +
             "Personal Information Questions:\n" +
             "- What's your name?\n" +
             "- How old are you?\n" +
             "- Where are you from?\n" +
             "- What school do you go to?\n" +
             "- What grade are you in?\n" +
             "- What's your phone number?\n\n" +
             "Possessive Adjectives:\n" +
             "- I → my (my book)\n" +
             "- You → your (your pen)\n" +
             "- He → his (his bag)\n" +
             "- She → her (her desk)\n" +
             "- It → its (its color)\n" +
             "- We → our (our school)\n" +
             "- They → their (their classroom)\n\n" +
             "Example:\n" +
             "Ali: Hello! I'm Ali. What's your name?\n" +
             "Mona: Hi Ali! My name is Mona. Nice to meet you!\n" +
             "Ali: Nice to meet you too, Mona! This is my friend, Khaled.",
             2),
            ("The Verb 'To Be'",
             "The verb 'to be' is the most important verb in English. It is used to describe states, identities, and conditions.\n\n" +
             "Forms of 'To Be':\n" +
             "- I am (I'm)\n" +
             "- You are (You're)\n" +
             "- He is (He's)\n" +
             "- She is (She's)\n" +
             "- It is (It's)\n" +
             "- We are (We're)\n" +
             "- They are (They're)\n\n" +
             "Uses of 'To Be':\n\n" +
             "1. Identity: I am a student. / She is a doctor.\n" +
             "2. Description: The sky is blue. / He is tall.\n" +
             "3. Location: The book is on the table. / We are in the classroom.\n" +
             "4. Age: I am 12 years old. / She is 14.\n" +
             "5. Feeling: I am happy. / They are tired.\n\n" +
             "Negative Form:\n" +
             "- I am not (I'm not)\n" +
             "- You are not (You aren't)\n" +
             "- He/She/It is not (He isn't)\n" +
             "- We/They are not (We aren't)\n\n" +
             "Question Form:\n" +
             "- Am I...?\n" +
             "- Are you...?\n" +
             "- Is he/she/it...?\n" +
             "- Are we/they...?\n\n" +
             "Example:\n" +
             "Affirmative: He is a teacher.\n" +
             "Negative: He is not (isn't) a teacher.\n" +
             "Question: Is he a teacher? Yes, he is. / No, he isn't.\n\n" +
             "Practice:\n" +
             "Complete with am/is/are:\n" +
             "1. I ___ happy. (am)\n" +
             "2. She ___ a student. (is)\n" +
             "3. They ___ from Egypt. (are)\n" +
             "4. The cat ___ on the sofa. (is)\n" +
             "5. We ___ friends. (are)",
             3),
        });
        await AddUnit(S["ENG"], "Unit 2: Daily Routines", 2,
            AcademicTerm.FirstSemester, new[]
        {
            ("My Daily Schedule",
             "Talking about daily routines is essential for everyday communication. We use the Simple Present Tense to describe habits and routines.\n\n" +
             "Simple Present Tense:\n" +
             "- I/You/We/They + verb (I wake up at 6 AM)\n" +
             "- He/She/It + verb + s/es (He wakes up at 6 AM)\n\n" +
             "My Typical Day:\n" +
             "- I wake up at 6:00 AM\n" +
             "- I brush my teeth and wash my face\n" +
             "- I eat breakfast at 6:30 AM\n" +
             "- I go to school at 7:00 AM\n" +
             "- School starts at 7:30 AM\n" +
             "- I have lunch at 1:00 PM\n" +
             "- I go home at 2:00 PM\n" +
             "- I do my homework at 4:00 PM\n" +
             "- I play with my friends at 5:00 PM\n" +
             "- I have dinner at 7:00 PM\n" +
             "- I watch TV at 8:00 PM\n" +
             "- I go to bed at 9:00 PM\n\n" +
             "Telling Time:\n" +
             "- 6:00 → six o'clock\n" +
             "- 6:15 → quarter past six\n" +
             "- 6:30 → half past six\n" +
             "- 6:45 → quarter to seven\n" +
             "- 7:10 → ten past seven\n" +
             "- 7:50 → ten to eight\n\n" +
             "Adverbs of Frequency:\n" +
             "- always (100%): I always brush my teeth.\n" +
             "- usually (90%): I usually wake up at 6 AM.\n" +
             "- often (70%): I often play football.\n" +
             "- sometimes (50%): I sometimes watch TV.\n" +
             "- rarely (10%): I rarely eat fast food.\n" +
             "- never (0%): I never skip breakfast.\n\n" +
             "Example Paragraph:\n" +
             "Ahmed is a student. He wakes up at 6 o'clock every day. He usually eats eggs for breakfast. He always goes to school on time. He sometimes plays football after school. He never goes to bed late.",
             1),
        });
        await AddUnit(S["ENG"], "Unit 3: Food and Drinks", 3,
            AcademicTerm.SecondSemester, new[]
        {
            ("Food Vocabulary and Preferences",
             "Food is a common topic in everyday conversations. Learn how to talk about food and express your preferences.\n\n" +
             "Food Categories:\n\n" +
             "Fruits: apple, banana, orange, grape, strawberry, watermelon, mango\n" +
             "Vegetables: carrot, tomato, potato, onion, cucumber, lettuce, pepper\n" +
             "Meat & Protein: chicken, fish, beef, eggs, beans, lentils\n" +
             "Dairy: milk, cheese, yogurt, butter, cream\n" +
             "Grains: rice, bread, pasta, wheat, oats, corn\n" +
             "Drinks: water, juice, tea, coffee, milk, soda\n" +
             "Desserts: cake, ice cream, chocolate, cookies, pudding\n\n" +
             "Expressing Likes and Dislikes:\n" +
             "- I like... / I love... / I enjoy...\n" +
             "- I don't like... / I hate... / I can't stand...\n" +
             "- I prefer... / I'd rather have...\n\n" +
             "Examples:\n" +
             "- I like apples but I don't like bananas.\n" +
             "- She loves chocolate ice cream.\n" +
             "- He hates spicy food.\n" +
             "- We prefer orange juice to soda.\n\n" +
             "Countable and Uncountable Nouns:\n" +
             "Countable (can count): apple, egg, cookie, sandwich\n" +
             "  → one apple, two apples, many apples\n" +
             "Uncountable (cannot count): water, rice, milk, bread\n" +
             "  → some water, a glass of water, much water\n\n" +
             "Quantifiers:\n" +
             "- some (affirmative): I want some water.\n" +
             "- any (negative/question): Is there any milk? No, there isn't any.\n" +
             "- a lot of / many / much\n\n" +
             "Dialogue:\n" +
             "Waiter: What would you like to order?\n" +
             "Customer: I'd like a chicken sandwich and some orange juice, please.\n" +
             "Waiter: Would you like anything else?\n" +
             "Customer: Yes, a piece of chocolate cake, please.\n" +
             "Waiter: Certainly! Coming right up.",
             1),
        });
        await AddUnit(S["ENG"], "Unit 4: Travel and Tourism", 4,
            AcademicTerm.SecondSemester, new[]
        {
            ("Transportation and Directions",
             "Travel vocabulary helps you navigate places and ask for directions.\n\n" +
             "Modes of Transportation:\n" +
             "- car, bus, train, plane, ship, boat, bicycle, motorcycle, taxi, metro\n\n" +
             "Travel Verbs:\n" +
             "- go, travel, take, ride, drive, fly, sail, walk, catch, miss\n\n" +
             "Examples:\n" +
             "- I take the bus to school every day.\n" +
             "- We flew to London last summer.\n" +
             "- She rides her bicycle to the park.\n\n" +
             "Asking for Directions:\n" +
             "- Excuse me, how do I get to the museum?\n" +
             "- Where is the nearest hospital?\n" +
             "- Is there a bank near here?\n" +
             "- Could you tell me the way to the station?\n" +
             "- How far is the airport?\n\n" +
             "Giving Directions:\n" +
             "- Go straight ahead.\n" +
             "- Turn left / right at the corner.\n" +
             "- Take the first street on your left.\n" +
             "- It's next to the pharmacy.\n" +
             "- It's on the corner of Main Street and Park Avenue.\n" +
             "- It's across from the supermarket.\n" +
             "- It's between the school and the hospital.\n\n" +
             "Places in a City:\n" +
             "- hospital, school, bank, post office, supermarket, pharmacy, park, museum, library, restaurant, hotel, airport, bus station, train station\n\n" +
             "Example:\n" +
             "Tourist: Excuse me, how do I get to the library?\n" +
             "Local: Go straight ahead for two blocks. Turn left at the traffic lights. The library is on your right, next to the park.\n" +
             "Tourist: Thank you very much!\n" +
             "Local: You're welcome! Have a nice day!",
             1),
        });

        // ── الدراسات الاجتماعية ─────────────────────────────────────────────
        await AddUnit(S["SOC"], "الوحدة الأولى: الجغرافيا", 1,
            AcademicTerm.FirstSemester, new[]
        {
            ("الموقع الجغرافي لمصر",
             "تقع مصر في شمال شرق قارة أفريقيا، وتمتد أجزاء منها إلى قارة آسيا (شبه جزيرة سيناء).\n\n" +
             "حدود مصر:\n" +
             "- شمالاً: البحر المتوسط (بطول 995 كم)\n" +
             "- شرقاً: البحر الأحمر وفلسطين (قطاع غزة)\n" +
             "- غرباً: ليبيا\n" +
             "- جنوباً: السودان\n\n" +
             "أهمية الموقع الجغرافي لمصر:\n" +
             "1. موقع استراتيجي: تربط بين قارتي أفريقيا وآسيا\n" +
             "2. قناة السويس: أهم ممر ملاحي في العالم يربط البحر المتوسط بالبحر الأحمر\n" +
             "3. إشراف على بحرين: البحر المتوسط والبحر الأحمر\n" +
             "4. مناخ متنوع: من البحر المتوسط شمالاً إلى الصحراوي جنوباً\n\n" +
             "المساحة:\n" +
             "- تبلغ مساحة مصر حوالي 1,002,000 كم²\n" +
             "- يشكل وادي النيل والدلتا حوالي 4% من المساحة\n" +
             "- الصحراء الغربية تغطي حوالي 68% من المساحة\n" +
             "- الصحراء الشرقية وشبه جزيرة سيناء تغطي حوالي 28%\n\n" +
             "التقسيمات الإدارية:\n" +
             "- تنقسم مصر إلى 27 محافظة\n" +
             "- أكبر المحافظات: الوادي الجديد\n" +
             "- أصغر المحافظات: القاهرة (من حيث المساحة)\n" +
             "- عاصمة مصر: القاهرة (أكبر مدينة في أفريقيا والشرق الأوسط)",
             1),
            ("المناخ والنبات الطبيعي",
             "يتأثر مناخ مصر بعدة عوامل أهمها الموقع الفلكي والموقع الجغرافي والتضاريس.\n\n" +
             "العوامل المؤثرة في المناخ:\n" +
             "1. الموقع الفلكي: تقع مصر بين دائرتي عرض 22° و 32° شمالاً\n" +
             "2. الموقع الجغرافي: إشرافها على البحر المتوسط والبحر الأحمر\n" +
             "3. الرياح السائدة: الرياح الشمالية الغربية\n" +
             "4. التضاريس: وجود المنخفضات والمرتفعات\n\n" +
             "خصائص المناخ في مصر:\n" +
             "1. مناخ البحر المتوسط: في السواحل الشمالية (شتاء معتدل ممطر، صيف حار جاف)\n" +
             "2. مناخ صحراوي: في باقي المناطق (حار نهاراً، بارد ليلاً)\n" +
             "3. الأمطار: قليلة وتتركز في فصل الشتاء على السواحل الشمالية\n" +
             "4. درجات الحرارة: تتراوح بين 10°م شتاءً و 40°م صيفاً\n\n" +
             "النبات الطبيعي:\n" +
             "1. نباتات البحر المتوسط: الحلفاء، الخزامى، إكليل الجبل\n" +
             "2. نباتات وادي النيل: البردي، اللوتس، السنط\n" +
             "3. نباتات صحراوية: التين الشوكي، الأثل، السمر\n" +
             "4. نباتات ساحلية: المانجروف (على ساحل البحر الأحمر)",
             2),
        });
        await AddUnit(S["SOC"], "الوحدة الثانية: التاريخ", 2,
            AcademicTerm.SecondSemester, new[]
        {
            ("الحضارة المصرية القديمة",
             "تُعدّ الحضارة المصرية القديمة واحدة من أعرق الحضارات في تاريخ البشرية، واستمرت لأكثر من 3000 عام.\n\n" +
             "عوامل قيام الحضارة المصرية:\n" +
             "1. نهر النيل: مصدر المياه والخصوبة\n" +
             "2. الموقع الجغرافي: حماية طبيعية بالصحاري والبحار\n" +
             "3. المناخ المناسب: اعتدال المناخ\n" +
             "4. الموارد الطبيعية: الحجر والذهب والنحاس\n\n" +
             "العصور التاريخية:\n" +
             "1. العصر العتيق (3100-2686 ق.م): توحيد مصر على يد الملك مينا\n" +
             "2. الدولة القديمة (2686-2181 ق.م): عصر بناء الأهرامات\n" +
             "3. الدولة الوسطى (2055-1650 ق.م): عصر الازدهار الأدبي\n" +
             "4. الدولة الحديثة (1550-1069 ق.م): عصر الإمبراطورية المصرية\n\n" +
             "إنجازات الحضارة المصرية القديمة:\n\n" +
             "1. الهندسة والعمارة:\n" +
             "   - بناء الأهرامات (خوفو، خفرع، منقرع)\n" +
             "   - معابد الكرنك وأبو سمبل والأقصر\n" +
             "   - المسلات والتماثيل العملاقة\n\n" +
             "2. الكتابة:\n" +
             "   - اختراع الكتابة الهيروغليفية\n" +
             "   - استخدام ورق البردي\n" +
             "   - حجر رشيد: مفتاح فك رموز الكتابة المصرية القديمة\n\n" +
             "3. العلوم:\n" +
             "   - الطب: عمليات جراحية وعلاج العيون\n" +
             "   - الرياضيات: حساب المساحات والأحجام\n" +
             "   - الفلك: التقويم الشمسي (365 يوماً)",
             1),
            ("شخصيات تاريخية مصرية",
             "على مر العصور، أنجبت مصر شخصيات عظيمة تركت بصمة في التاريخ.\n\n" +
             "1. الملك مينا (نارمر):\n" +
             "- أول من وحد مصر الشمالية والجنوبية حوالي 3100 ق.م\n" +
             "- أسس أول أسرة حاكمة\n" +
             "- بنى العاصمة منف (قرب الجيزة)\n\n" +
             "2. الملك خوفو:\n" +
             "- باني الهرم الأكبر بالجيزة\n" +
             "- حكم في الأسرة الرابعة خلال الدولة القديمة\n" +
             "- الهرم الأكبر أحد عجائب الدنيا السبع\n\n" +
             "3. الملكة حتشبسوت:\n" +
             "- أول ملكة تحكم مصر (الأسرة الثامنة عشرة)\n" +
             "- أرسلت بعثة تجارية إلى بلاد بونت\n" +
             "- بنت معبد الدير البحري بالأقصر\n\n" +
             "4. الملك أخناتون:\n" +
             "- دعا إلى عبادة إله واحد (آتون)\n" +
             "- بنى مدينة أخيتاتون (تل العمارنة)\n" +
             "- عُرف بالفن الواقعي في النحت والتصوير\n\n" +
             "5. الملك رمسيس الثاني:\n" +
             "- أحد أشهر فراعنة مصر\n" +
             "- حكم 66 عاماً (الأسرة التاسعة عشرة)\n" +
             "- بنى معبد أبو سمبل وتوسع في بناء المعابد\n" +
             "- وقع أول معاهدة سلام في التاريخ مع الحيثيين",
             2),
        });

        // ── التربية الإسلامية ───────────────────────────────────────────────
        await AddUnit(S["ISL"], "الوحدة الأولى: القرآن الكريم", 1,
            AcademicTerm.FirstSemester, new[]
        {
            ("سورة الفاتحة",
             "سورة الفاتحة هي أعظم سورة في القرآن الكريم، وتُسمى السبع المثاني وأم الكتاب.\n\n" +
             "فضائل سورة الفاتحة:\n" +
             "- هي ركن أساسي في الصلاة (لا صلاة لمن لم يقرأ بفاتحة الكتاب)\n" +
             "- أعظم سورة في القرآن\n" +
             "- فيها الشفاء (الرقية الشرعية)\n" +
             "- نزلت من كنز تحت العرش\n\n" +
             "عدد آياتها: 7 آيات\n" +
             "ترتيبها في المصحف: الأولى\n" +
             "نوعها: مكية\n\n" +
             "نص السورة:\n" +
             "بِسْمِ اللَّهِ الرَّحْمَٰنِ الرَّحِيمِ\n" +
             "الْحَمْدُ لِلَّهِ رَبِّ الْعَالَمِينَ\n" +
             "الرَّحْمَٰنِ الرَّحِيمِ\n" +
             "مَالِكِ يَوْمِ الدِّينِ\n" +
             "إِيَّاكَ نَعْبُدُ وَإِيَّاكَ نَسْتَعِينُ\n" +
             "اهْدِنَا الصِّرَاطَ الْمُسْتَقِيمَ\n" +
             "صِرَاطَ الَّذِينَ أَنْعَمْتَ عَلَيْهِمْ غَيْرِ الْمَغْضُوبِ عَلَيْهِمْ وَلَا الضَّالِّينَ\n\n" +
             "تفسير الآيات:\n" +
             "1. {بِسْمِ اللَّهِ الرَّحْمَٰنِ الرَّحِيمِ}: أبدأ قراءتي مستعيناً بالله متبركاً باسمه\n" +
             "2. {الْحَمْدُ لِلَّهِ رَبِّ الْعَالَمِينَ}: الثناء على الله بصفات الكمال\n" +
             "3. {الرَّحْمَٰنِ الرَّحِيمِ}: ذو الرحمة الواسعة\n" +
             "4. {مَالِكِ يَوْمِ الدِّينِ}: المالك الحقيقي ليوم القيامة\n" +
             "5. {إِيَّاكَ نَعْبُدُ وَإِيَّاكَ نَسْتَعِينُ}: نخصك بالعبادة ونطلب منك العون\n" +
             "6. {اهْدِنَا الصِّرَاطَ الْمُسْتَقِيمَ}: دلنا على طريق الحق\n" +
             "7. {صِرَاطَ الَّذِينَ أَنْعَمْتَ عَلَيْهِمْ...}: طريق الأنبياء والصديقين",
             1),
            ("سورة الإخلاص",
             "سورة الإخلاص من أعظم سور القرآن، وتعدل ثلث القرآن.\n\n" +
             "سبب التسمية:\n" +
             "- سُميت بالإخلاص لأنها تخلّص القلب من الشرك\n" +
             "- وتسمى سورة التوحيد وسورة الصمد\n\n" +
             "عدد آياتها: 4 آيات\n" +
             "ترتيبها: 112\n" +
             "نوعها: مكية\n\n" +
             "نص السورة:\n" +
             "قُلْ هُوَ اللَّهُ أَحَدٌ\n" +
             "اللَّهُ الصَّمَدُ\n" +
             "لَمْ يَلِدْ وَلَمْ يُولَدْ\n" +
             "وَلَمْ يَكُن لَّهُ كُفُواً أَحَدٌ\n\n" +
             "التفسير:\n" +
             "1. {قُلْ هُوَ اللَّهُ أَحَدٌ}: الله واحد أحد لا شريك له\n" +
             "2. {اللَّهُ الصَّمَدُ}: السيد الذي يُصمد إليه في الحاجات\n" +
             "3. {لَمْ يَلِدْ وَلَمْ يُولَدْ}: ليس له والد ولا ولد\n" +
             "4. {وَلَمْ يَكُن لَّهُ كُفُواً أَحَدٌ}: لا مثيل له ولا شبيه\n\n" +
             "فضلها:\n" +
             "- قال النبي صلى الله عليه وسلم: 'والذي نفسي بيده إنها لتعدل ثلث القرآن'\n" +
             "- من قرأها في صلاة الوتر كانت له نوراً",
             2),
        });
        await AddUnit(S["ISL"], "الوحدة الثانية: الحديث النبوي", 2,
            AcademicTerm.SecondSemester, new[]
        {
            ("أحاديث الرحمة",
             "الرحمة من أعظم صفات الله تعالى، وقد حثّ النبي صلى الله عليه وسلم على التراحم بين الناس.\n\n" +
             "الحديث الأول:\n" +
             "قال رسول الله صلى الله عليه وسلم:\n" +
             "『الرَّاحِمُونَ يَرْحَمُهُمُ الرَّحْمَنُ، ارْحَمُوا مَنْ فِي الْأَرْضِ يَرْحَمْكُمْ مَنْ فِي السَّمَاءِ』\n" +
             "رواه أبو داود والترمذي\n\n" +
             "شرح الحديث:\n" +
             "- الراحمون: الذين يتحلون بصفة الرحمة\n" +
             "- يرحمهم الرحمن: جزاء من جنس العمل\n" +
             "- ارحموا من في الأرض: شملت جميع الخلق (الإنسان والحيوان)\n" +
             "- من في السماء: الملائكة أو الله تعالى\n\n" +
             "الحديث الثاني:\n" +
             "『مَنْ لَا يَرْحَمُ النَّاسَ لَا يَرْحَمْهُ اللَّهُ』\n" +
             "متفق عليه\n\n" +
             "الحديث الثالث:\n" +
             "『الرَّحْمَةُ مِائَةُ جُزْءٍ، أَنْزَلَ اللَّهُ مِنْهَا جُزْءاً وَاحِداً فِي الْأَرْضِ، فَبِذَلِكَ يَتَرَاحَمُ الْخَلْقُ』\n\n" +
             "تطبيقات عملية:\n" +
             "1. رحمة الوالدين: برهما والإحسان إليهما\n" +
             "2. رحمة الصغار: العطف على الأطفال\n" +
             "3. رحمة الحيوان: إطعامه وعدم إيذائه\n" +
             "4. رحمة الجار: تفقّد أحواله ومساعدته\n" +
             "5. رحمة الفقير: الصدقة ومساعدة المحتاجين",
             1),
            ("الأخلاق الحسنة",
             "الأخلاق الحسنة من أهم مقاصد الإسلام، وقد بُعث النبي ﷺ ليتمّم مكارم الأخلاق.\n\n" +
             "حديث: 『إنما بُعثت لأُتمّم مكارم الأخلاق』\n\n" +
             "حديث: 『أكمل المؤمنين إيماناً أحسنهم خُلُقاً』\n\n" +
             "أهم الأخلاق الحسنة:\n\n" +
             "1. الصدق:\n" +
             "- هو مطابقة القول للواقع\n" +
             "- قال النبي ﷺ: 『عليكم بالصدق، فإن الصدق يهدي إلى البرِّ، وإن البرَّ يهدي إلى الجنة』\n" +
             "- الصدق في الأقوال والأفعال والمعاملات\n\n" +
             "2. الأمانة:\n" +
             "- أداء الحقوق إلى أصحابها\n" +
             "- قال النبي ﷺ: 『آية المنافق ثلاث: إذا حدث كذب، وإذا اؤتمن خان، وإذا عاهد غدر』\n" +
             "- الأمانة في العمل والدراسة والمال\n\n" +
             "3. التسامح:\n" +
             "- العفو عند المقدرة\n" +
             "- قال تعالى: {فَاعْفُوا وَاصْفَحُوا حَتَّىٰ يَأْتِيَ اللَّهُ بِأَمْرِهِ}\n" +
             "- التسامح يزيد المحبة بين الناس\n\n" +
             "4. الكرم:\n" +
             "- بذل الخير للآخرين\n" +
             "- قال النبي ﷺ: 『السَّخِيُّ قَرِيبٌ مِنَ اللَّهِ قَرِيبٌ مِنَ الْجَنَّةِ』",
             2),
        });

        // ── الحاسب الآلي ────────────────────────────────────────────────────
        await AddUnit(S["CMP"], "الوحدة الأولى: أساسيات الحاسب", 1,
            AcademicTerm.FirstSemester, new[]
        {
            ("مكونات الحاسب",
             "الحاسب الآلي (الكمبيوتر) هو جهاز إلكتروني يمكن برمجته لمعالجة البيانات وتخزينها واسترجاعها.\n\n" +
             "المكونات المادية (Hardware):\n\n" +
             "أولاً: وحدة المعالجة المركزية (CPU):\n" +
             "- تُعتبر عقل الحاسب\n" +
             "- مسؤولة عن تنفيذ التعليمات والعمليات الحسابية والمنطقية\n" +
             "- تقاس سرعتها بالجيجاهرتز (GHz)\n" +
             "- أنواع: Intel Core i3/i5/i7/i9, AMD Ryzen\n\n" +
             "ثانياً: الذاكرة العشوائية (RAM):\n" +
             "- تخزين مؤقت للبيانات أثناء التشغيل\n" +
             "- تفقد محتوياتها عند إيقاف التشغيل\n" +
             "- كلما زادت سعتها زادت سرعة الحاسب\n" +
             "- تُقاس بالجيجابايت (GB)\n\n" +
             "ثالثاً: وحدات التخزين:\n" +
             "- القرص الصلب (HDD): سعة كبيرة، سرعة أقل\n" +
             "- القرص الصلب (SSD): سعة أقل، سرعة عالية جداً\n" +
             "- التخزين السحابي: Google Drive, OneDrive, iCloud\n\n" +
             "رابعاً: أجهزة الإدخال:\n" +
             "- لوحة المفاتيح (Keyboard)\n" +
             "- الفأرة (Mouse)\n" +
             "- الميكروفون (Microphone)\n" +
             "- الماسح الضوئي (Scanner)\n" +
             "- كاميرا الويب (Webcam)\n\n" +
             "خامساً: أجهزة الإخراج:\n" +
             "- الشاشة (Monitor)\n" +
             "- الطابعة (Printer)\n" +
             "- السماعات (Speakers)\n\n" +
             "سادساً: اللوحة الأم (Motherboard):\n" +
             "- تربط جميع المكونات ببعضها\n" +
             "- تحتوي على منافذ التوصيل والموصلات\n\n" +
             "البرمجيات (Software):\n" +
             "- نظام التشغيل (Windows, macOS, Linux)\n" +
             "- البرامج التطبيقية (Office, Photoshop, Games)\n" +
             "- لغات البرمجة (Python, Java, C++)",
             1),
            ("شبكات الحاسب",
             "شبكة الحاسب هي مجموعة من الأجهزة المتصلة ببعضها لتبادل البيانات والمشاركة في الموارد.\n\n" +
             "فوائد الشبكات:\n" +
             "1. مشاركة الملفات والبيانات\n" +
             "2. مشاركة الطابعات والأجهزة\n" +
             "3. الاتصال بين المستخدمين (البريد الإلكتروني، المحادثة)\n" +
             "4. الوصول إلى الإنترنت\n" +
             "5. التخزين المركزي للبيانات\n\n" +
             "أنواع الشبكات:\n\n" +
             "1. الشبكة المحلية (LAN):\n" +
             "- تربط أجهزة في مساحة محدودة (مدرسة، مكتب)\n" +
             "- سرعة عالية\n" +
             "- تستخدم كابلات Ethernet أو Wi-Fi\n\n" +
             "2. الشبكة الواسعة (WAN):\n" +
             "- تربط أجهزة عبر مسافات كبيرة (دول، قارات)\n" +
             "- الإنترنت أكبر مثال على WAN\n" +
             "- سرعة أقل نسبياً\n\n" +
             "3. الشبكة الشخصية (PAN):\n" +
             "- تربط أجهزة شخصية (هاتف مع سماعة بلوتوث)\n" +
             "- مجال تغطية صغير جداً (بضعة أمتار)\n\n" +
             "أجهزة الشبكات:\n" +
             "- المودم (Modem): يحول الإشارات الرقمية إلى تماثلية والعكس\n" +
             "- الراوتر (Router): يوجه البيانات بين الشبكات\n" +
             "- السويتش (Switch): يربط الأجهزة داخل الشبكة المحلية\n\n" +
             "الإنترنت:\n" +
             "- شبكة عالمية ضخمة تربط ملايين الأجهزة\n" +
             "- تستخدم بروتوكول TCP/IP للاتصال\n" +
             "- كل جهاز له عنوان IP فريد",
             2),
        });
        await AddUnit(S["CMP"], "الوحدة الثانية: البرمجة", 2,
            AcademicTerm.SecondSemester, new[]
        {
            ("مقدمة في البرمجة",
             "البرمجة هي عملية كتابة تعليمات (أوامر) للحاسب الآلي باستخدام لغة برمجة معينة لتنفيذ مهمة محددة.\n\n" +
             "مفاهيم أساسية:\n\n" +
             "1. الخوارزمية (Algorithm):\n" +
             "- مجموعة من الخطوات المتسلسلة لحل مشكلة معينة\n" +
             "- مثال: خوارزمية تحضير كوب شاي\n" +
             "   1. اغلي الماء\n" +
             "   2. ضع كيس الشاي في الكوب\n" +
             "   3. صب الماء المغلي\n" +
             "   4. انتظر 3 دقائق\n" +
             "   5. أزل كيس الشاي\n" +
             "   6. أضف السكر حسب الرغبة\n\n" +
             "2. المخطط الانسيابي (Flowchart):\n" +
             "- تمثيل بياني للخوارزمية باستخدام أشكال ورموز\n" +
             "- شكل بيضاوي: بداية/نهاية\n" +
             "- متوازي أضلاع: إدخال/إخراج\n" +
             "- مستطيل: عملية حسابية\n" +
             "- معين: قرار (نعم/لا)\n\n" +
             "3. لغات البرمجة:\n" +
             "- Python: سهلة للمبتدئين، تستخدم في الذكاء الاصطناعي\n" +
             "- Java: تستخدم في تطبيقات الأندرويد\n" +
             "- JavaScript: تستخدم في تطوير مواقع الويب\n" +
             "- C++: تستخدم في الألعاب والبرامج عالية الأداء\n\n" +
             "4. أنواع البيانات:\n" +
             "- عدد صحيح (int): 5, -3, 100\n" +
             "- عدد عشري (float): 3.14, -0.5\n" +
             "- نص (string): 'مرحباً', 'Hello'\n" +
             "- منطقي (boolean): True, False\n\n" +
             "5. الهياكل البرمجية الأساسية:\n" +
             "- التسلسل: تنفيذ الأوامر بالترتيب\n" +
             "- الشرط (if/else): اتخاذ قرار بناءً على شرط\n" +
             "- التكرار (loop): تكرار مجموعة أوامر",
             1),
            ("المتغيرات والثوابت",
             "المتغير (Variable): مكان في الذاكرة يُخزّن فيه قيمة يمكن تغييرها أثناء تنفيذ البرنامج.\n\n" +
             "خصائص المتغيرات:\n" +
             "- الاسم: معرف فريد للمتغير (يجب أن يبدأ بحرف أو شرطة سفلية)\n" +
             "- القيمة: البيانات المخزنة في المتغير\n" +
             "- النوع: نوع البيانات التي يمكن تخزينها\n\n" +
             "أمثلة:\n" +
             "- Python: age = 15      # متغير اسمه age قيمته 15\n" +
             "- Python: name = 'Ahmed'  # متغير نصي\n" +
             "- Python: PI = 3.14    # ثابت (قيمة لا تتغير)\n\n" +
             "قواعد تسمية المتغيرات:\n" +
             "- يمكن أن تحتوي على أحرف وأرقام وشرطة سفلية\n" +
             "- يجب أن تبدأ بحرف أو شرطة سفلية (ليس رقماً)\n" +
             "- حساسة لحالة الأحرف (age ≠ Age)\n" +
             "- لا يمكن استخدام الكلمات المحجوزة (if, for, while)\n\n" +
             "العمليات على المتغيرات:\n\n" +
             "عمليات حسابية:\n" +
             "- الجمع: + (a + b)\n" +
             "- الطرح: - (a - b)\n" +
             "- الضرب: * (a * b)\n" +
             "- القسمة: / (a / b)\n" +
             "- باقي القسمة: % (a % b)\n\n" +
             "عمليات المقارنة:\n" +
             "- يساوي: ==\n" +
             "- لا يساوي: !=\n" +
             "- أكبر: >\n" +
             "- أصغر: <\n" +
             "- أكبر أو يساوي: >=\n" +
             "- أصغر أو يساوي: <=\n\n" +
             "مثال تطبيقي:\n" +
             "# برنامج لحساب متوسط درجات الطالب\n" +
             "grade1 = 85\n" +
             "grade2 = 90\n" +
             "grade3 = 78\n" +
             "average = (grade1 + grade2 + grade3) / 3\n" +
             "print('المتوسط:', average)",
             2),
        });
    }

    /// <summary>
    /// Upgrade seeding: runs on existing databases to add newly created tables/columns.
    /// </summary>
    private static async Task SeedUpgrades(AppDbContext ctx)
    {
        var now = DateTime.UtcNow;

        // ── 1. QuestionBank (if empty) ──────────────────────────────────────
        if (!await ctx.QuestionBank.AnyAsync())
        {
            var subjects = await ctx.Subjects.ToDictionaryAsync(s => s.Code!, s => s);
            var grade1   = await ctx.GradeLevels.OrderBy(g => g.LevelOrder).FirstAsync();

            var qbQuestions = new (string text, QuestionType type, string correct, string? options, Subject subj)[]
            {
                ("ما ناتج 5 + 3؟",                       QuestionType.MultipleChoice, "8",  @"[""4"",""7"",""8"",""10""]", subjects["MTH"]),
                ("10 - 4 = ...",                         QuestionType.FillBlank,      "6",  null,                       subjects["MTH"]),
                ("العدد 12 هو عدد زوجي",                 QuestionType.TrueFalse,      "True", null,                     subjects["MTH"]),
                ("ما ناتج 2 × 6 = ...",                  QuestionType.MultipleChoice, "12", @"[""8"",""12"",""14"",""16""]", subjects["MTH"]),
                ("اجمع: 15 + 7 = ...",                   QuestionType.FillBlank,      "22", null,                       subjects["MTH"]),
                ("العدد الأولي هو عدد يقبل القسمة على 1 ونفسه فقط",
                                                          QuestionType.TrueFalse,      "True", null,                    subjects["MTH"]),
                ("ما ناتج 36 ÷ 6؟",                     QuestionType.MultipleChoice, "6",  @"[""4"",""6"",""8"",""9""]",  subjects["MTH"]),
                ("المربع له 4 أضلاع متساوية",           QuestionType.TrueFalse,      "True", null,                     subjects["MTH"]),
                ("الفعل الماضي من 'يكتب' هو 'كتب'",     QuestionType.TrueFalse,      "True", null,                     subjects["ARB"]),
                ("ما مرادف كلمة 'جميل'؟",               QuestionType.MultipleChoice, "حسن",@"[""قبيح"",""حسن"",""سريع"",""كبير""]", subjects["ARB"]),
                ("المبتدأ ... في أول الجملة الاسمية",    QuestionType.FillBlank,      "مرفوع", null,                    subjects["ARB"]),
                ("جمع كلمة 'كتاب' هو 'كُتب'",           QuestionType.TrueFalse,      "True", null,                     subjects["ARB"]),
                ("ما ضد كلمة 'طويل'؟",                  QuestionType.MultipleChoice, "قصير",@"[""جميل"",""قصير"",""عريض"",""ثقيل""]", subjects["ARB"]),
                ("الحرف الناسخ 'إنّ' ينصب ... ويرفع ...",
                                                          QuestionType.FillBlank,      "الاسم والخبر", null,            subjects["ARB"]),
            };

            foreach (var (text, type, correct, options, subj) in qbQuestions)
            {
                ctx.QuestionBank.Add(new QuestionBank
                {
                    QuestionText  = text,
                    QuestionType  = type,
                    CorrectAnswer = correct,
                    OptionsJson   = options,
                    SubjectId     = subj.Id,
                    GradeLevelId  = grade1.Id,
                    CreatedAt = now, UpdatedAt = now
                });
            }
            await ctx.SaveChangesAsync();

            // Link QB questions to an existing exam if any
            var anyExam = await ctx.Exams.FirstOrDefaultAsync();
            if (anyExam != null)
            {
                var qbLinks = await ctx.QuestionBank
                    .Where(q => q.SubjectId == subjects["MTH"].Id)
                    .OrderBy(q => q.Id)
                    .Take(3)
                    .ToListAsync();
                int order = 1;
                foreach (var qb in qbLinks)
                {
                    ctx.ExamQuestionBankItems.Add(new ExamQuestionBankItem
                    {
                        ExamId         = anyExam.Id,
                        QuestionBankId = qb.Id,
                        Points         = 10,
                        DisplayOrder   = order++,
                        CreatedAt = now, UpdatedAt = now
                    });
                }
                await ctx.SaveChangesAsync();
            }
        }

        // ── 2. Update AcademicYear dates (14-week second semester) ──────────
        var year = await ctx.AcademicYears.FirstOrDefaultAsync(y => y.IsCurrent);
        if (year != null && (year.SecondSemesterStartDate != new DateOnly(2026, 4, 4) ||
                             year.SecondSemesterEndDate   != new DateOnly(2026, 7, 11)))
        {
            year.SecondSemesterStartDate = new DateOnly(2026, 4, 4);
            year.SecondSemesterEndDate   = new DateOnly(2026, 7, 11);
            year.EndDate                 = new DateOnly(2026, 7, 11);
            year.UpdatedAt = DateTime.UtcNow;
            await ctx.SaveChangesAsync();
        }

        // ── 3. Regenerate EvaluationPeriods if old format (missing semester numbers) ──
        if (year != null)
        {
            bool anyOldPeriod = await ctx.EvaluationPeriods
                .AnyAsync(p => p.AcademicYearId == year.Id && p.PeriodType == PeriodType.Weekly && p.SemesterNumber == null);
            if (anyOldPeriod)
            {
                // Remove dependent data first
                var periodIds = await ctx.EvaluationPeriods
                    .Where(p => p.AcademicYearId == year.Id)
                    .Select(p => p.Id)
                    .ToListAsync();
                ctx.PeriodAverages.RemoveRange(
                    await ctx.PeriodAverages.Where(pa => periodIds.Contains(pa.PeriodId)).ToListAsync());
                ctx.StudentEvaluations.RemoveRange(
                    await ctx.StudentEvaluations.Where(se => periodIds.Contains(se.PeriodId)).ToListAsync());
                ctx.DailyAbsences.RemoveRange(
                    await ctx.DailyAbsences.Where(da => da.PeriodId != null && periodIds.Contains(da.PeriodId.Value)).ToListAsync());
                // Remove old periods
                ctx.EvaluationPeriods.RemoveRange(
                    await ctx.EvaluationPeriods.Where(p => p.AcademicYearId == year.Id).ToListAsync());
                await ctx.SaveChangesAsync();

                // Regenerate with current dates
                var newPeriods = Project.Domain.Helpers.EvaluationPeriodGenerator
                    .GeneratePeriods(year.Id, year.StartDate,
                        year.FirstSemesterStartDate, year.FirstSemesterEndDate,
                        year.SecondSemesterStartDate, year.SecondSemesterEndDate)
                    .ToList();
                ctx.EvaluationPeriods.AddRange(newPeriods);
                await ctx.SaveChangesAsync();
            }
        }

        // ── 4. Update Units with Term (if missing) ──────────────────────────
        bool anyUnitWithoutTerm = await ctx.Units.AnyAsync(u => u.Term == null);
        if (anyUnitWithoutTerm)
        {
            var subjects = await ctx.Subjects.ToDictionaryAsync(s => s.Code!, s => s);
            var allUnits = await ctx.Units
                .Where(u => u.Term == null)
                .OrderBy(u => u.SubjectId).ThenBy(u => u.DisplayOrder)
                .ToListAsync();

            foreach (var unit in allUnits)
            {
                // First unit of each subject → FirstSemester, second → SecondSemester
                unit.Term = unit.DisplayOrder <= 1
                    ? AcademicTerm.FirstSemester
                    : AcademicTerm.SecondSemester;
                unit.UpdatedAt = now;
            }
            await ctx.SaveChangesAsync();
        }

        // ── 5. Update templates to SecondSemester (if still on FirstSemester) ──
        bool anyTmplNeedsUpdate = await ctx.EvaluationTemplates
            .AnyAsync(t => t.Term == AcademicTerm.FirstSemester);
        if (anyTmplNeedsUpdate)
        {
            var tmpls = await ctx.EvaluationTemplates
                .Where(t => t.Term == AcademicTerm.FirstSemester)
                .ToListAsync();
            foreach (var t in tmpls)
            {
                t.Term = AcademicTerm.SecondSemester;
                t.Weeks = 14;
                t.UpdatedAt = now;
            }
            await ctx.SaveChangesAsync();
        }

        // ── 6. Update student names to be unique (fix duplicate names across classes) ──
        bool studentNamesNeedFix = await ctx.Students.AnyAsync(s => s.FullName == "أحمد عبد الرحمن عبد العزيز" && !s.IsDeleted);
        if (studentNamesNeedFix)
        {
            var firstNames  = new[] { "أحمد","محمد","علي","عمر","خالد","يوسف","محمود","كريم","حسن","حسين","إبراهيم","عبد الله","أمير","بسام","جمال","حمزة","سامي","صالح","عادل","طارق","زياد","شادي","نادر","هاني","وائل","باهر","رامي","فادي","مازن","ياسر" };
            var middleNames = new[] { "عبد الرحمن","سامح","جابر","إسماعيل","فتحي","أنور","حمدي","رفعت","سمير","جلال","نبيل","كامل","رشاد","بهجت","نعيم","وجيه","قاسم","مبشر","رؤوف","وديع","بهاء","سعيد","لطفي","نزيه","هشام","أكرم","بطرس","ثروت","جورج","حبيب" };
            var lastNames   = new[] { "عبد العزيز","السيد","خليل","مرسي","فوزي","نصر","الشيخ","شحاتة","النجار","هاشم","الديب","رمضان","سلامة","بسيوني","عتريس","أبو زيد","الزهار","عرفة","جابر","خضر","زيتون","طنطاوي","عبده","غانم","قنديل","كمال","لقمة","موسى","نوح","هندي" };

            var allStudents = await ctx.Students
                .OrderBy(s => s.Id)
                .Where(s => !s.IsDeleted)
                .ToListAsync();

            for (int seq = 0; seq < allStudents.Count; seq++)
            {
                var firstIdx = seq % 30;
                var middleIdx = (seq / 6) % 30;
                var lastIdx = seq / 30;
                allStudents[seq].FullName = $"{firstNames[firstIdx]} {middleNames[middleIdx]} {lastNames[lastIdx]}";
                allStudents[seq].UpdatedAt = now;
            }
            await ctx.SaveChangesAsync();

            // Also update related User accounts and Parent names
            var studentUserIds = allStudents.Where(s => s.UserId.HasValue).Select(s => s.UserId!.Value).ToHashSet();
            var studentUsers = await ctx.Users
                .Where(u => u.Role == UserRole.Student && studentUserIds.Contains(u.Id))
                .ToListAsync();
            var studentUserMap = allStudents
                .Where(s => s.UserId.HasValue)
                .ToDictionary(s => s.UserId!.Value, s => s.FullName);
            foreach (var user in studentUsers)
            {
                if (studentUserMap.TryGetValue(user.Id, out var fullName))
                {
                    user.FullName = fullName;
                    user.UpdatedAt = now;
                }
            }
            await ctx.SaveChangesAsync();

            // Update parent names (وليّ أمر {child1} و {child2} ...)
            var studentIdSet = allStudents.Select(s => s.Id).ToHashSet();
            var parentStudentLinks = await ctx.ParentStudents
                .Where(ps => studentIdSet.Contains(ps.StudentId))
                .ToListAsync();
            var parentIds = parentStudentLinks.Select(ps => ps.ParentId).ToHashSet();
            var parentUsers = await ctx.Users
                .Where(u => u.Role == UserRole.Parent && parentIds.Contains(u.Id))
                .ToListAsync();
            var parentStudentGroups = parentStudentLinks
                .GroupBy(ps => ps.ParentId)
                .ToDictionary(g => g.Key, g => g.Select(ps => ps.StudentId).ToList());
            var studentNameMap = allStudents.ToDictionary(s => s.Id, s => s.FullName);
            foreach (var user in parentUsers)
            {
                if (parentStudentGroups.TryGetValue(user.Id, out var studentIds) && studentIds.Count > 0)
                {
                    var names = studentIds
                        .Select(id => studentNameMap.GetValueOrDefault(id, ""))
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToList();
                    user.FullName = names.Count switch
                    {
                        0 => $"وليّ أمر",
                        1 => $"وليّ أمر {names[0]}",
                        2 => $"وليّ أمر {names[0]} و{names[1].Split(' ')[0]}",
                        _ => $"وليّ أمر {names[0]} وآخرون"
                    };
                    user.UpdatedAt = now;
                }
            }
            await ctx.SaveChangesAsync();
        }

        // ── 5. Ensure parent1 is linked to both student1 (Ahmed) and student2 (Mohamed) ──
        try
        {
            var p1 = await ctx.Users.FirstOrDefaultAsync(u => u.Username == "parent1" && !u.IsDeleted);
            if (p1 != null)
            {
                var u2 = await ctx.Users.FirstOrDefaultAsync(u => u.Username == "student2" && !u.IsDeleted);
                if (u2 != null)
                {
                    var s2 = await ctx.Students.FirstOrDefaultAsync(s => s.UserId == u2.Id && !s.IsDeleted);
                    if (s2 != null)
                    {
                        var linkExists = await ctx.ParentStudents.AnyAsync(ps => ps.ParentId == p1.Id && ps.StudentId == s2.Id && !ps.IsDeleted);
                        if (!linkExists)
                        {
                            ctx.ParentStudents.Add(new ParentStudent
                            {
                                ParentId = p1.Id,
                                StudentId = s2.Id,
                                Relationship = RelationshipType.Father,
                                CreatedAt = now,
                                UpdatedAt = now
                            });
                            await ctx.SaveChangesAsync();
                        }
                    }
                }
            }
        }
        catch { /* ignore */ }

        // ── 6. Give parent2's child a different family name —─────────────
        try
        {
            var p2 = await ctx.Users.FirstOrDefaultAsync(u => u.Username == "parent2" && !u.IsDeleted);
            if (p2 != null)
            {
                var psLink = await ctx.ParentStudents
                    .FirstOrDefaultAsync(ps => ps.ParentId == p2.Id && !ps.IsDeleted);
                if (psLink != null)
                {
                    var s = await ctx.Students.FirstOrDefaultAsync(st => st.Id == psLink.StudentId && !st.IsDeleted);
                    if (s != null)
                    {
                        s.FullName = "علي خالد السيد";
                        s.UpdatedAt = now;

                        // Also update the User account for this student
                        if (s.UserId.HasValue)
                        {
                            var userAcc = await ctx.Users.FirstOrDefaultAsync(u => u.Id == s.UserId.Value && !u.IsDeleted);
                            if (userAcc != null)
                            {
                                userAcc.FullName = s.FullName;
                                userAcc.UpdatedAt = now;
                            }
                        }

                        // Also update parent2's display name
                        p2.FullName = $"وليّ أمر {s.FullName}";
                        p2.UpdatedAt = now;

                        await ctx.SaveChangesAsync();
                    }
                }
            }
        }
        catch { /* ignore */ }

        // ── 7. Normalize True/False CorrectAnswer to canonical "True"/"False" ──
        // إصلاح بيانات صح/خطأ المخزّنة بصيغ غير موحّدة (مثل "صواب"، "نعم"، "yes"...)
        // إلى الصيغة الكانونية "True"/"False". Idempotent: الـ guard بيمنع التشغيل التكراري.
        await NormalizeTrueFalseAnswersAsync(ctx, now);
    }

    /// <summary>
    /// يطبّع كل صفوف TrueFalse في ExamQuestions/AssignmentQuestions/QuestionBank
    /// إلى الصيغة الكانونية "True"/"False"، ثم يُعيد تصحيح (re-grade) الإجابات المتأثرة فقط.
    /// </summary>
    private static async Task NormalizeTrueFalseAnswersAsync(AppDbContext ctx, DateTime now)
    {
        // Guard: لو مفيش أي صف TrueFalse مش canonical، نخرج فوراً (idempotent)
        var needsExamQuestions = await ctx.ExamQuestions.AnyAsync(q =>
            q.QuestionType == QuestionType.TrueFalse && !q.IsDeleted &&
            !(q.CorrectAnswer == "True" || q.CorrectAnswer == "False"));
        var needsAssignmentQuestions = await ctx.AssignmentQuestions.AnyAsync(q =>
            q.QuestionType == QuestionType.TrueFalse && !q.IsDeleted &&
            !(q.CorrectAnswer == "True" || q.CorrectAnswer == "False"));
        var needsQuestionBank = await ctx.QuestionBank.AnyAsync(q =>
            q.QuestionType == QuestionType.TrueFalse && !q.IsDeleted &&
            !(q.CorrectAnswer == "True" || q.CorrectAnswer == "False"));

        if (!needsExamQuestions && !needsAssignmentQuestions && !needsQuestionBank)
            return;

        // 7أ. تطبيع صفوف TrueFalse في الامتحانات
        if (needsExamQuestions)
        {
            var examTfQuestions = await ctx.ExamQuestions
                .Where(q => q.QuestionType == QuestionType.TrueFalse && !q.IsDeleted)
                .ToListAsync();

            foreach (var q in examTfQuestions)
            {
                var normalized = BooleanNormalizer.NormalizeBoolean(q.CorrectAnswer);
                if (normalized.HasValue)
                    q.CorrectAnswer = normalized.Value ? "True" : "False";
                // لو null (قيمة مش متعرّف عليها) → نسيبها زي ما هي (مبنلمسهاش)
            }
            await ctx.SaveChangesAsync();
        }

        // 7ب. تطبيع صفوف TrueFalse في الواجبات
        if (needsAssignmentQuestions)
        {
            var assignmentTfQuestions = await ctx.AssignmentQuestions
                .Where(q => q.QuestionType == QuestionType.TrueFalse && !q.IsDeleted)
                .ToListAsync();

            foreach (var q in assignmentTfQuestions)
            {
                var normalized = BooleanNormalizer.NormalizeBoolean(q.CorrectAnswer);
                if (normalized.HasValue)
                    q.CorrectAnswer = normalized.Value ? "True" : "False";
            }
            await ctx.SaveChangesAsync();
        }

        // 7ج. تطبيع صفوف TrueFalse في بنك الأسئلة
        if (needsQuestionBank)
        {
            var bankTfQuestions = await ctx.QuestionBank
                .Where(q => q.QuestionType == QuestionType.TrueFalse && !q.IsDeleted)
                .ToListAsync();

            foreach (var q in bankTfQuestions)
            {
                var normalized = BooleanNormalizer.NormalizeBoolean(q.CorrectAnswer);
                if (normalized.HasValue)
                    q.CorrectAnswer = normalized.Value ? "True" : "False";
            }
            await ctx.SaveChangesAsync();
        }

        // 7د. إعادة تصحيح (Re-grade) إجابات الطلاب المتأثرة بالـ bug بس.
        // نعيد حساب IsCorrect/PointsEarned للإجابات اللي سؤالها بقى canonical دلوقتي
        // (يعني كانت متأثرة بالـ bug فعلاً).
        await RegradeExamTrueFalseAnswersAsync(ctx, now);
        await RegradeAssignmentTrueFalseAnswersAsync(ctx, now);
    }

    /// <summary>
    /// يُعيد تصحيح إجابات أسئلة صح/خطأ في محاولات الامتحان المتأثرة بالـ bug،
    /// ويُحدّث درجة كل محاولة بشكل deterministic.
    /// </summary>
    private static async Task RegradeExamTrueFalseAnswersAsync(AppDbContext ctx, DateTime now)
    {
        // نجيب بس الإجابات اللي سؤالها TrueFalse canonical (يعني كانت محتاجة regrade)
        var affectedExamAnswers = await ctx.StudentExamAnswers
            .Include(a => a.Question)
            .Include(a => a.Attempt)
            .Where(a => a.Question.QuestionType == QuestionType.TrueFalse &&
                        (a.Question.CorrectAnswer == "True" || a.Question.CorrectAnswer == "False"))
            .ToListAsync();

        if (affectedExamAnswers.Count == 0)
            return;

        var affectedAttemptIds = affectedExamAnswers.Select(a => a.AttemptId).Distinct().ToHashSet();

        foreach (var answer in affectedExamAnswers)
        {
            var correct = BooleanNormalizer.NormalizeBoolean(answer.Question.CorrectAnswer);
            if (correct.HasValue && answer.BooleanAnswer.HasValue)
            {
                var isCorrect = correct.Value == answer.BooleanAnswer.Value;
                answer.IsCorrect = isCorrect;
                answer.PointsEarned = isCorrect ? answer.Question.Points : 0;
            }
        }

        // إعادة حساب الدرجة الكلية لكل محاولة متأثرة
        var attempts = await ctx.StudentExamAttempts
            .Include(t => t.Answers)
                .ThenInclude(a => a.Question)
            .Where(t => affectedAttemptIds.Contains(t.Id))
            .ToListAsync();

        foreach (var attempt in attempts)
        {
            decimal totalScore = 0;
            var hasManual = false;
            foreach (var answer in attempt.Answers)
            {
                if (answer.IsCorrect.HasValue)
                    totalScore += answer.PointsEarned;
                else
                    hasManual = true;
            }
            attempt.Score = totalScore;
            attempt.IsGraded = !hasManual;
            attempt.UpdatedAt = now;
        }

        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// يُعيد تصحيح إجابات أسئلة صح/خطأ في تسليمات الواجبات المتأثرة بالـ bug،
    /// ويُحدّث درجة كل تسليم بشكل deterministic.
    /// </summary>
    private static async Task RegradeAssignmentTrueFalseAnswersAsync(AppDbContext ctx, DateTime now)
    {
        var affectedAssignmentAnswers = await ctx.StudentAssignmentAnswers
            .Include(a => a.Question)
            .Include(a => a.Submission)
            .Where(a => a.Question.QuestionType == QuestionType.TrueFalse &&
                        (a.Question.CorrectAnswer == "True" || a.Question.CorrectAnswer == "False"))
            .ToListAsync();

        if (affectedAssignmentAnswers.Count == 0)
            return;

        var affectedSubmissionIds = affectedAssignmentAnswers.Select(a => a.SubmissionId).Distinct().ToHashSet();

        foreach (var answer in affectedAssignmentAnswers)
        {
            var correct = BooleanNormalizer.NormalizeBoolean(answer.Question.CorrectAnswer);
            if (correct.HasValue && answer.BooleanAnswer.HasValue)
            {
                var isCorrect = correct.Value == answer.BooleanAnswer.Value;
                answer.IsCorrect = isCorrect;
                answer.PointsEarned = isCorrect ? answer.Question.Points : 0;
            }
        }

        // إعادة حساب الدرجة الكلية لكل تسليم متأثر
        var submissions = await ctx.StudentAssignmentSubmissions
            .Include(s => s.Answers)
                .ThenInclude(a => a.Question)
            .Where(s => affectedSubmissionIds.Contains(s.Id))
            .ToListAsync();

        foreach (var submission in submissions)
        {
            decimal totalScore = 0;
            var hasManual = false;
            foreach (var answer in submission.Answers)
            {
                if (answer.IsCorrect.HasValue)
                    totalScore += answer.PointsEarned;
                else
                    hasManual = true;
            }
            submission.Score = totalScore;
            submission.IsGraded = !hasManual;
            submission.UpdatedAt = now;
        }

        await ctx.SaveChangesAsync();
    }
}
