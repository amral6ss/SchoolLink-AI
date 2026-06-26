using Common.Results;
using Project.BLL.DTOs.ClassStudentsBrowser;
using Project.BLL.DTOs.Common;
using Project.BLL.Interfaces;
using Project.DAL.Interfaces;
using Project.Domain.Enums;

namespace Project.BLL.Services;

public class ClassStudentsBrowserService : IClassStudentsBrowserService
{
    private readonly IUnitOfWork _unitOfWork;

    public ClassStudentsBrowserService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<OperationResult<ClassStudentsBrowserResultDto>> GetClassStudentsAsync(
        int classId,
        GetClassStudentsBrowserFilter filter)
    {
        if (filter.AcademicYearId <= 0)
            return OperationResult<ClassStudentsBrowserResultDto>.Failure("السنة الدراسية مطلوبة");

        var classEntity = await _unitOfWork.Classes.GetByIdWithIncludesAsync(classId);
        if (classEntity is null || classEntity.IsDeleted)
            return OperationResult<ClassStudentsBrowserResultDto>.Failure("الفصل غير موجود", 404);

        var academicYear = await _unitOfWork.AcademicYears.GetByIdAsync(filter.AcademicYearId);
        if (academicYear is null || academicYear.IsDeleted)
            return OperationResult<ClassStudentsBrowserResultDto>.Failure("السنة الدراسية غير موجودة", 404);

        var page = filter.Page < 1 ? 1 : filter.Page;
        var pageSize = filter.PageSize <= 0 ? 10 : filter.PageSize;
        var searchTerm = filter.SearchTerm?.Trim();

        var enrollments = await _unitOfWork.StudentEnrollments.GetByClassWithStudentAsync(
            classId,
            filter.AcademicYearId);

        var activeEnrollments = enrollments
            .Where(e => !e.IsDeleted
                && e.LeftAt is null
                && e.Student is not null
                && !(e.Student?.IsDeleted ?? false)
                && e.Student!.IsActive
                && e.Student.LifecycleStatus == StudentLifecycleStatus.Active)
            .ToList();

        var filteredEnrollments = activeEnrollments;
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            filteredEnrollments = activeEnrollments
                .Where(e => e.Student!.FullName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var pagedItems = filteredEnrollments
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new ClassStudentBrowserItemDto
            {
                EnrollmentId = e.Id,
                StudentId = e.StudentId,
                StudentName = e.Student!.FullName,
                Gender = e.Student.Gender,
                IsActive = e.Student.IsActive && e.Student.LifecycleStatus == StudentLifecycleStatus.Active,
                EnrolledAt = e.EnrolledAt
            })
            .ToList();

        var result = new ClassStudentsBrowserResultDto
        {
            ClassId = classEntity.Id,
            ClassName = classEntity.Name,
            AcademicYearId = academicYear.Id,
            AcademicYearName = academicYear.Name,
            GradeLevelName = classEntity.GradeLevel?.Name ?? string.Empty,
            TotalStudents = activeEnrollments.Count,
            FilteredStudentsCount = filteredEnrollments.Count,
            Students = new PagedResult<ClassStudentBrowserItemDto>
            {
                Items = pagedItems,
                TotalCount = filteredEnrollments.Count,
                Page = page,
                PageSize = pageSize
            }
        };

        return OperationResult<ClassStudentsBrowserResultDto>.Success(
            result,
            "تم جلب طلاب الفصل بنجاح");
    }
}
