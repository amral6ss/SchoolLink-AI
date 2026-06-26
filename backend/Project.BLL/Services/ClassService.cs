using AutoMapper;
using Common.Results;
using Project.BLL.DTOs;
using Project.BLL.Interfaces;
using Project.DAL.Interfaces;
using Project.Domain.Entities;
using Project.Domain.Enums;

namespace Project.BLL.Services;

public class ClassService : IClassService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper     _mapper;

    public ClassService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper     = mapper;
    }

    public async Task<OperationResult<ClassDto>> CreateClassAsync(
        CreateClassRequest request)
    {
        // 1. Validate GradeLevel
        var gradeLevel = await _unitOfWork.GradeLevels.GetByIdAsync(request.GradeLevelId);
        if (gradeLevel is null || gradeLevel.IsDeleted)
            return OperationResult<ClassDto>.Failure("الصف الدراسي غير موجود");

        // 2. Validate AcademicYear
        var academicYear = await _unitOfWork.AcademicYears.GetByIdAsync(request.AcademicYearId);
        if (academicYear is null || academicYear.IsDeleted)
            return OperationResult<ClassDto>.Failure("السنة الدراسية غير موجودة");

        // 3. Uniqueness (Name + GradeLevelId + AcademicYearId)
        if (await _unitOfWork.Classes.ExistsByNameGradeLevelAndYearAsync(
                request.Name, request.GradeLevelId, request.AcademicYearId))
            return OperationResult<ClassDto>.Failure(
                "اسم الفصل موجود بالفعل في هذا الصف وهذه السنة الدراسية");

        // 4. Create entity
        var entity = new SchoolClass
        {
            GradeLevelId   = request.GradeLevelId,
            AcademicYearId = request.AcademicYearId,
            Name           = request.Name.Trim(),
            Capacity       = request.Capacity,
            Status         = request.Status
        };

        // 5. Persist
        await _unitOfWork.Classes.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        // 6. Reload with navigation properties for mapping (GradeLevel + AcademicYear)
        var withIncludes = await _unitOfWork.Classes.GetByIdWithIncludesAsync(entity.Id);

        return OperationResult<ClassDto>.Success(
            _mapper.Map<ClassDto>(withIncludes),
            "تم إنشاء الفصل بنجاح");
    }

    public async Task<OperationResult<ClassDto>> UpdateClassAsync(
        UpdateClassRequest request)
    {
        // 1. Find entity
        var entity = await _unitOfWork.Classes.GetByIdAsync(request.Id);
        if (entity is null || entity.IsDeleted)
            return OperationResult<ClassDto>.Failure("الفصل غير موجود");

        // 2. Name uniqueness within same (GradeLevelId, AcademicYearId)
        var normalizedName = request.Name.Trim();
        if (!string.Equals(normalizedName, entity.Name, StringComparison.Ordinal) &&
            await _unitOfWork.Classes.ExistsByNameGradeLevelAndYearAsync(
                normalizedName, entity.GradeLevelId, entity.AcademicYearId))
            return OperationResult<ClassDto>.Failure("اسم الفصل مستخدم بالفعل");

        // 3. Apply update
        entity.Name      = normalizedName;
        entity.Capacity  = request.Capacity;
        entity.Status    = request.Status;
        entity.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Classes.Update(entity);
        await _unitOfWork.SaveChangesAsync();

        // 4. Reload with navigation properties for mapping
        var withIncludes = await _unitOfWork.Classes.GetByIdWithIncludesAsync(entity.Id);

        return OperationResult<ClassDto>.Success(
            _mapper.Map<ClassDto>(withIncludes),
            "تم تحديث الفصل بنجاح");
    }

    public async Task<OperationResult> DeleteClassAsync(int id)
    {
        // Classes with students, teacher assignments, or timetables are kept for history.
        var entity = await _unitOfWork.Classes.GetByIdAsync(id);
        if (entity is null || entity.IsDeleted)
            return OperationResult.Failure("الفصل غير موجود");

        if (await _unitOfWork.StudentEnrollments.AnyAsync(e => e.ClassId == id) ||
            await _unitOfWork.ClassSubjectTeachers.AnyAsync(cst => cst.ClassId == id) ||
            await _unitOfWork.Timetables.AnyAsync(t => t.ClassId == id))
            return OperationResult.Failure("لا يمكن حذف فصل مستخدم في بيانات أخرى");

        _unitOfWork.Classes.SoftDelete(entity);
        await _unitOfWork.SaveChangesAsync();

        return OperationResult.Success("تم حذف الفصل بنجاح");
    }

    public async Task<OperationResult<IEnumerable<ClassDto>>> GetAllClassesAsync(
        GetClassesFilter filter)
    {
        var classes = await _unitOfWork.Classes.GetFilteredWithIncludesAsync(
            filter.AcademicYearId,
            filter.GradeLevelId,
            filter.Status);

        return OperationResult<IEnumerable<ClassDto>>.Success(
            _mapper.Map<IEnumerable<ClassDto>>(classes),
            "تم جلب الفصول بنجاح");
    }

    public async Task<OperationResult<ClassDto>> GetClassByIdAsync(int id)
    {
        var entity = await _unitOfWork.Classes.GetByIdWithIncludesAsync(id);
        if (entity is null || entity.IsDeleted)
            return OperationResult<ClassDto>.Failure("الفصل غير موجود");

        return OperationResult<ClassDto>.Success(
            _mapper.Map<ClassDto>(entity),
            "تم جلب الفصل بنجاح");
    }

    public async Task<OperationResult<IEnumerable<ClassDto>>> GetClassesByGradeLevelAsync(int gradeLevelId)
    {
        var classes = await _unitOfWork.Classes.FindAsync(c => c.GradeLevelId == gradeLevelId && !c.IsDeleted);
        return OperationResult<IEnumerable<ClassDto>>.Success(
            _mapper.Map<IEnumerable<ClassDto>>(classes),
            "تم جلب الفصول بنجاح");
    }

    public async Task<OperationResult<ClassDto>> GetClassWithStudentsAsync(int classId)
    {
        var entity = await _unitOfWork.Classes.GetByIdWithIncludesAsync(classId);
        if (entity is null || entity.IsDeleted)
            return OperationResult<ClassDto>.Failure("الفصل غير موجود");

        return OperationResult<ClassDto>.Success(
            _mapper.Map<ClassDto>(entity),
            "تم جلب الفصل مع الطلاب بنجاح");
    }

    public async Task<OperationResult<IEnumerable<ClassDto>>> GetClassesByTeacherAsync(int teacherId, int academicYearId)
    {
        var classes = await _unitOfWork.ClassSubjectTeachers.GetClassesForTeacherAsync(teacherId, academicYearId);
        return OperationResult<IEnumerable<ClassDto>>.Success(
            _mapper.Map<IEnumerable<ClassDto>>(classes),
            "تم جلب فصول المعلم بنجاح");
    }

    public async Task<OperationResult<ClassDto>> CreateClassWithStudentsAsync(CreateClassWithStudentsRequest request)
    {
        var academicYear = await _unitOfWork.AcademicYears.GetCurrentAsync();
        if (academicYear is null)
            return OperationResult<ClassDto>.Failure("لا توجد سنة دراسية نشطة");

        var entity = new SchoolClass
        {
            GradeLevelId = 1,
            AcademicYearId = academicYear.Id,
            Name = request.Name.Trim()
        };

        await _unitOfWork.Classes.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        foreach (var studentName in request.Students.Select(s => s.Trim()).Where(s => s.Length > 0))
        {
            var student = (await _unitOfWork.Students.FindAsync(s => s.FullName == studentName)).FirstOrDefault();
            if (student is null)
            {
                student = new Student
                {
                    FullName = studentName,
                    IsActive = true,
                    LifecycleStatus = StudentLifecycleStatus.Active
                };
                await _unitOfWork.Students.AddAsync(student);
                await _unitOfWork.SaveChangesAsync();
            }

            var exists = await _unitOfWork.StudentEnrollments.AnyAsync(e =>
                e.StudentId == student.Id &&
                e.ClassId == entity.Id &&
                e.AcademicYearId == academicYear.Id);

            if (!exists)
            {
                await _unitOfWork.StudentEnrollments.AddAsync(new StudentEnrollment
                {
                    StudentId = student.Id,
                    ClassId = entity.Id,
                    AcademicYearId = academicYear.Id,
                    EnrolledAt = DateOnly.FromDateTime(DateTime.UtcNow)
                });
            }
        }

        await _unitOfWork.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(request.Subject) || !string.IsNullOrWhiteSpace(request.Teacher))
        {
            var subject = string.IsNullOrWhiteSpace(request.Subject)
                ? null
                : (await _unitOfWork.Subjects.FindAsync(s => s.Name == request.Subject.Trim())).FirstOrDefault();
            var teacher = string.IsNullOrWhiteSpace(request.Teacher)
                ? null
                : (await _unitOfWork.Users.FindAsync(u => u.FullName == request.Teacher.Trim() && u.Role == UserRole.Teacher)).FirstOrDefault();

            if (subject is not null && teacher is not null)
            {
                var exists = await _unitOfWork.ClassSubjectTeachers.AnyAsync(t =>
                    t.ClassId == entity.Id &&
                    t.SubjectId == subject.Id &&
                    t.TeacherId == teacher.Id &&
                    t.AcademicYearId == academicYear.Id);

                if (!exists)
                {
                    await _unitOfWork.ClassSubjectTeachers.AddAsync(new ClassSubjectTeacher
                    {
                        ClassId = entity.Id,
                        SubjectId = subject.Id,
                        TeacherId = teacher.Id,
                        AcademicYearId = academicYear.Id
                    });
                    await _unitOfWork.SaveChangesAsync();
                }
            }
        }

        var withIncludes = await _unitOfWork.Classes.GetByIdWithIncludesAsync(entity.Id);
        return OperationResult<ClassDto>.Success(
            _mapper.Map<ClassDto>(withIncludes),
            "تم إنشاء الفصل مع الطلاب بنجاح");
    }

    public async Task<OperationResult<CopyClassesFromYearResultDto>> PreviewCopyClassesFromYearAsync(
        CopyClassesFromYearRequest request)
    {
        var validation = await ValidateCopyClassesRequestAsync(request);
        if (!validation.IsSuccess)
            return validation;

        var result = await BuildCopyClassesPlanAsync(request);
        return OperationResult<CopyClassesFromYearResultDto>.Success(
            result,
            "تم تجهيز معاينة نسخ الفصول بنجاح");
    }

    public async Task<OperationResult<CopyClassesFromYearResultDto>> CopyClassesFromYearAsync(
        CopyClassesFromYearRequest request)
    {
        var validation = await ValidateCopyClassesRequestAsync(request);
        if (!validation.IsSuccess)
            return validation;

        var result = await BuildCopyClassesPlanAsync(request);
        var itemsToCreate = result.Items.Where(item => !item.AlreadyExists).ToList();

        foreach (var item in itemsToCreate)
        {
            await _unitOfWork.Classes.AddAsync(new SchoolClass
            {
                GradeLevelId = item.GradeLevelId,
                AcademicYearId = request.TargetAcademicYearId,
                Name = item.ClassName,
                Capacity = item.Capacity,
                Status = Project.Domain.Enums.ClassStatus.Active
            });
        }

        if (itemsToCreate.Count > 0)
            await _unitOfWork.SaveChangesAsync();

        result.CreatedCount = itemsToCreate.Count;
        result.SkippedExistingCount = result.Items.Count(item => item.AlreadyExists);

        return OperationResult<CopyClassesFromYearResultDto>.Success(
            result,
            result.CreatedCount == 0
                ? "كل الفصول موجودة بالفعل في السنة الهدف"
                : $"تم نسخ {result.CreatedCount} فصل بنجاح");
    }

    private async Task<OperationResult<CopyClassesFromYearResultDto>> ValidateCopyClassesRequestAsync(
        CopyClassesFromYearRequest request)
    {
        if (request.SourceAcademicYearId == request.TargetAcademicYearId)
            return OperationResult<CopyClassesFromYearResultDto>.Failure("السنة المصدر والهدف يجب أن تكونا مختلفتين");

        var sourceYear = await _unitOfWork.AcademicYears.GetByIdAsync(request.SourceAcademicYearId);
        if (sourceYear is null || sourceYear.IsDeleted)
            return OperationResult<CopyClassesFromYearResultDto>.Failure("السنة الدراسية المصدر غير موجودة");

        var targetYear = await _unitOfWork.AcademicYears.GetByIdAsync(request.TargetAcademicYearId);
        if (targetYear is null || targetYear.IsDeleted)
            return OperationResult<CopyClassesFromYearResultDto>.Failure("السنة الدراسية الهدف غير موجودة");

        if (targetYear.StartDate <= sourceYear.StartDate)
            return OperationResult<CopyClassesFromYearResultDto>.Failure("السنة الهدف يجب أن تبدأ بعد السنة المصدر");

        return OperationResult<CopyClassesFromYearResultDto>.Success(new CopyClassesFromYearResultDto());
    }

    private async Task<CopyClassesFromYearResultDto> BuildCopyClassesPlanAsync(
        CopyClassesFromYearRequest request)
    {
        var sourceClasses = await _unitOfWork.Classes.GetByAcademicYearAsync(request.SourceAcademicYearId);
        var targetClasses = await _unitOfWork.Classes.GetByAcademicYearAsync(request.TargetAcademicYearId);

        var targetLookup = targetClasses
            .GroupBy(c => BuildClassCopyKey(c.GradeLevelId, c.Name))
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var items = sourceClasses
            .OrderBy(c => c.GradeLevel.LevelOrder)
            .ThenBy(c => c.Name)
            .Select(sourceClass =>
            {
                targetLookup.TryGetValue(
                    BuildClassCopyKey(sourceClass.GradeLevelId, sourceClass.Name),
                    out var existingTargetClass);

                return new CopyClassesFromYearPreviewDto
                {
                    SourceClassId = sourceClass.Id,
                    GradeLevelId = sourceClass.GradeLevelId,
                    GradeLevelName = sourceClass.GradeLevel?.Name ?? string.Empty,
                    ClassName = sourceClass.Name,
                    Capacity = sourceClass.Capacity,
                    Status = (int)sourceClass.Status,
                    AlreadyExists = existingTargetClass is not null,
                    TargetClassId = existingTargetClass?.Id
                };
            })
            .ToList();

        return new CopyClassesFromYearResultDto
        {
            SourceAcademicYearId = request.SourceAcademicYearId,
            TargetAcademicYearId = request.TargetAcademicYearId,
            TotalSourceClasses = items.Count,
            CreatedCount = 0,
            SkippedExistingCount = items.Count(item => item.AlreadyExists),
            Items = items
        };
    }

    private static string BuildClassCopyKey(int gradeLevelId, string className)
        => $"{gradeLevelId}|{className.Trim()}";

    public async Task<OperationResult<int>> GetClassCountAsync(int? academicYearId = null)
    {
        if (academicYearId.HasValue)
        {
            var count = await _unitOfWork.Classes.CountAsync(c =>
                c.AcademicYearId == academicYearId.Value && !c.IsDeleted);
            return OperationResult<int>.Success(count, "تم جلب عدد الفصول بنجاح");
        }

        var totalCount = await _unitOfWork.Classes.CountAsync(c => !c.IsDeleted);
        return OperationResult<int>.Success(totalCount, "تم جلب عدد الفصول بنجاح");
    }

    public async Task<OperationResult<object>> GetClassStatsAsync(int? academicYearId = null)
    {
        var classes = academicYearId.HasValue
            ? await _unitOfWork.Classes.FindAsync(c => c.AcademicYearId == academicYearId.Value && !c.IsDeleted)
            : await _unitOfWork.Classes.FindAsync(c => !c.IsDeleted);

        var total = classes.Count;
        var gradeLevelGroups = classes.GroupBy(c => c.GradeLevelId);

        var stats = new
        {
            TotalClasses = total,
            GradeLevelDistribution = gradeLevelGroups.Select(g => new
            {
                GradeLevelId = g.Key,
                Count = g.Count()
            })
        };

        return OperationResult<object>.Success(stats, "تم جلب إحصائيات الفصول بنجاح");
    }
}
