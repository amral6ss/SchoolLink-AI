using Common.Results;
using Project.BLL.DTOs.Common;
using Project.BLL.DTOs.ClassEnrollmentPicker;
using Project.BLL.DTOs.Enrollments;
using Project.BLL.Interfaces;
using Project.DAL.Interfaces;
using Project.Domain.Entities;

namespace Project.BLL.Services;

/// <summary>
/// Service مستقل لميزة إضافة الطلاب لفصل عبر picker.
/// لا يلمس StudentEnrollmentService ولا IStudentEnrollmentService.
/// </summary>
public class ClassEnrollmentPickerService : IClassEnrollmentPickerService
{
    private readonly IUnitOfWork _unitOfWork;

    public ClassEnrollmentPickerService(IUnitOfWork unitOfWork)
        => _unitOfWork = unitOfWork;

    public async Task<OperationResult<PagedResult<AvailableStudentDto>>> GetAvailableStudentsAsync(
        int classId,
        GetAvailableStudentsFilter filter)
    {
        // التحقق من الفصل
        var classEntity = await _unitOfWork.Classes.GetByIdAsync(classId);
        if (classEntity == null || classEntity.IsDeleted)
            return OperationResult<PagedResult<AvailableStudentDto>>.Failure("الفصل غير موجود", 404);

        if (classEntity.Status != Project.Domain.Enums.ClassStatus.Active)
            return OperationResult<PagedResult<AvailableStudentDto>>.Failure("لا يمكن إضافة طلاب إلى فصل غير نشط");

        // Normalize pagination
        var page     = filter.Page     < 1  ? 1  : filter.Page;
        var pageSize = filter.PageSize <= 0 ? 20 : Math.Min(filter.PageSize, 100);

        // NOT EXISTS على مستوى DB
        var (students, totalCount) = await _unitOfWork.StudentEnrollments.GetUnenrolledStudentsAsync(
            academicYearId: classEntity.AcademicYearId,
            searchTerm:     filter.SearchTerm,
            birthDateFrom:  filter.BirthDateFrom,
            birthDateTo:    filter.BirthDateTo,
            sortBy:         filter.SortBy,
            sortDescending: filter.SortDescending,
            page:           page,
            pageSize:       pageSize);

        var items = students.Select(s => new AvailableStudentDto
        {
            Id         = s.Id,
            FullName   = s.FullName,
            NationalId = s.NationalId,
            Gender     = s.Gender.HasValue ? (int)s.Gender.Value : null,
            BirthDate  = s.BirthDate
        }).ToList();

        return OperationResult<PagedResult<AvailableStudentDto>>.Success(
            new PagedResult<AvailableStudentDto>
            {
                Items      = items,
                TotalCount = totalCount,
                Page       = page,
                PageSize   = pageSize
            },
            $"تم جلب {items.Count} من {totalCount} طالب متاح");
    }

    public async Task<OperationResult<BulkEnrollResultDto>> BulkEnrollAsync(
        ClassPickerBulkEnrollRequest request)
    {
        if (request.StudentIds == null || request.StudentIds.Count == 0)
            return OperationResult<BulkEnrollResultDto>.Failure("يجب توفير طالب واحد على الأقل");

        // جلب الفصل واستنتاج AcademicYearId
        var classEntity = await _unitOfWork.Classes.GetByIdAsync(request.ClassId);
        if (classEntity == null || classEntity.IsDeleted)
            return OperationResult<BulkEnrollResultDto>.Failure("الفصل غير موجود");

        if (classEntity.Status != Project.Domain.Enums.ClassStatus.Active)
            return OperationResult<BulkEnrollResultDto>.Failure("لا يمكن إضافة طلاب إلى فصل غير نشط");

        var academicYearId = classEntity.AcademicYearId;

        var year = await _unitOfWork.AcademicYears.GetByIdAsync(academicYearId);
        if (year == null || year.IsDeleted)
            return OperationResult<BulkEnrollResultDto>.Failure("السنة الدراسية المرتبطة بالفصل غير موجودة");

        if (request.EnrolledAt > DateOnly.FromDateTime(DateTime.UtcNow))
            return OperationResult<BulkEnrollResultDto>.Failure("تاريخ التسجيل لا يمكن أن يكون في المستقبل");

        var result = new BulkEnrollResultDto { TotalRequested = request.StudentIds.Count };
        var currentActiveCount = await _unitOfWork.StudentEnrollments.GetActiveCountByClassAsync(
            request.ClassId,
            academicYearId);

        foreach (var studentId in request.StudentIds)
        {
            var student = await _unitOfWork.Students.GetByIdAsync(studentId);
            if (student == null || student.IsDeleted)
            {
                result.FailureCount++;
                result.Failures.Add(new BulkEnrollFailureDto
                {
                    StudentId   = studentId,
                    StudentName = "غير معروف",
                    Reason      = "الطالب غير موجود"
                });
                continue;
            }

            if (await _unitOfWork.StudentEnrollments.HasActiveEnrollmentAsync(studentId, academicYearId))
            {
                result.FailureCount++;
                result.Failures.Add(new BulkEnrollFailureDto
                {
                    StudentId   = studentId,
                    StudentName = student.FullName,
                    Reason      = "الطالب مسجل بالفعل في فصل آخر خلال نفس السنة الدراسية"
                });
                continue;
            }

            if (classEntity.Capacity.HasValue && currentActiveCount >= classEntity.Capacity.Value)
            {
                result.FailureCount++;
                result.Failures.Add(new BulkEnrollFailureDto
                {
                    StudentId   = studentId,
                    StudentName = student.FullName,
                    Reason      = "الفصل وصل إلى السعة القصوى"
                });
                continue;
            }

            await _unitOfWork.StudentEnrollments.AddAsync(new StudentEnrollment
            {
                StudentId      = studentId,
                ClassId        = request.ClassId,
                AcademicYearId = academicYearId,
                EnrolledAt     = request.EnrolledAt
            });
            result.SuccessCount++;
            currentActiveCount++;
        }

        await _unitOfWork.SaveChangesAsync();

        return OperationResult<BulkEnrollResultDto>.Success(result,
            $"تم تسجيل {result.SuccessCount} من {result.TotalRequested} طالب بنجاح");
    }
}
