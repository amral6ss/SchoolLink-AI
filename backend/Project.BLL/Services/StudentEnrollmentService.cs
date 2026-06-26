using AutoMapper;
using Common.Results;
using Project.BLL.DTOs.Common;
using Project.BLL.DTOs.Enrollments;
using Project.BLL.Interfaces;
using Project.DAL.Interfaces;
using Project.Domain.Entities;

namespace Project.BLL.Services;

public class StudentEnrollmentService : IStudentEnrollmentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public StudentEnrollmentService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<OperationResult<EnrollmentDto>> EnrollStudentAsync(EnrollStudentRequest request)
    {
        var student = await _unitOfWork.Students.GetByIdAsync(request.StudentId);
        if (student == null || student.IsDeleted)
            return OperationResult<EnrollmentDto>.Failure("الطالب غير موجود");

        var classEntity = await _unitOfWork.Classes.GetByIdAsync(request.ClassId);
        if (classEntity == null || classEntity.IsDeleted)
            return OperationResult<EnrollmentDto>.Failure("الفصل غير موجود");

        if (classEntity.Status != Project.Domain.Enums.ClassStatus.Active)
            return OperationResult<EnrollmentDto>.Failure("لا يمكن تسجيل الطالب في فصل غير نشط");

        var year = await _unitOfWork.AcademicYears.GetByIdAsync(request.AcademicYearId);
        if (year == null || year.IsDeleted)
            return OperationResult<EnrollmentDto>.Failure("السنة الدراسية غير موجودة");

        if (request.EnrolledAt > DateOnly.FromDateTime(DateTime.UtcNow))
            return OperationResult<EnrollmentDto>.Failure("تاريخ التسجيل لا يمكن أن يكون في المستقبل");

        if (await _unitOfWork.StudentEnrollments.HasActiveEnrollmentAsync(request.StudentId, request.AcademicYearId))
            return OperationResult<EnrollmentDto>.Failure("الطالب لديه بالفعل قيد نشط في هذه السنة الدراسية");

        if (classEntity.Capacity.HasValue)
        {
            var activeCount = await _unitOfWork.StudentEnrollments.GetActiveCountByClassAsync(
                request.ClassId,
                request.AcademicYearId);
            if (activeCount >= classEntity.Capacity.Value)
                return OperationResult<EnrollmentDto>.Failure("الفصل وصل إلى السعة القصوى");
        }

        var enrollment = _mapper.Map<StudentEnrollment>(request);
        await _unitOfWork.StudentEnrollments.AddAsync(enrollment);
        await _unitOfWork.SaveChangesAsync();

        var dto = _mapper.Map<EnrollmentDto>(enrollment);
        return OperationResult<EnrollmentDto>.Success(dto, "تم تسجيل الطالب في الفصل بنجاح");
    }

    public async Task<OperationResult<EnrollmentDto>> TransferStudentAsync(TransferStudentRequest request)
    {
        var currentEnrollment = await _unitOfWork.StudentEnrollments.GetByIdWithDetailsAsync(request.CurrentEnrollmentId);
        if (currentEnrollment == null || currentEnrollment.IsDeleted)
            return OperationResult<EnrollmentDto>.Failure("التسجيل الحالي غير موجود");

        if (currentEnrollment.LeftAt != null)
            return OperationResult<EnrollmentDto>.Failure("التسجيل الحالي مغلق بالفعل");

        var newClass = await _unitOfWork.Classes.GetByIdAsync(request.NewClassId);
        if (newClass == null || newClass.IsDeleted)
            return OperationResult<EnrollmentDto>.Failure("الفصل الجديد غير موجود");

        if (newClass.Status != Project.Domain.Enums.ClassStatus.Active)
            return OperationResult<EnrollmentDto>.Failure("لا يمكن نقل الطالب إلى فصل غير نشط");

        // Validation: Same Academic Year
        if (newClass.AcademicYearId != currentEnrollment.AcademicYearId)
            return OperationResult<EnrollmentDto>.Failure("لا يمكن نقل الطالب لسنة دراسية مختلفة. النقل يتم داخل نفس السنة الدراسية فقط.");

        // Validation: Same Grade Level
        var currentClass = currentEnrollment.Class;
        if (currentClass == null || currentClass.IsDeleted)
            return OperationResult<EnrollmentDto>.Failure("الفصل الحالي غير موجود");

        if (newClass.GradeLevelId != currentClass.GradeLevelId)
            return OperationResult<EnrollmentDto>.Failure("لا يمكن نقل الطالب لصف دراسي مختلف. النقل يتم داخل نفس الصف الدراسي فقط.");

        // Validation: TransferDate
        var transferDate = request.TransferDate;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (transferDate > today)
            return OperationResult<EnrollmentDto>.Failure("تاريخ النقل لا يمكن أن يكون في المستقبل.");

        if (transferDate < currentEnrollment.EnrolledAt)
            return OperationResult<EnrollmentDto>.Failure("تاريخ النقل لا يمكن أن يكون قبل تاريخ التسجيل الحالي في الفصل.");

        var academicYear = await _unitOfWork.AcademicYears.GetByIdAsync(currentEnrollment.AcademicYearId);
        if (academicYear != null && transferDate > academicYear.EndDate)
            return OperationResult<EnrollmentDto>.Failure("تاريخ النقل خارج نطاق السنة الدراسية الحالية.");

        // Check if student already enrolled in target class
        var existingEnrollments = await _unitOfWork.StudentEnrollments.GetHistoryByStudentAsync(currentEnrollment.StudentId);
        if (existingEnrollments.Any(e => !e.IsDeleted && e.LeftAt == null
            && e.ClassId == request.NewClassId && e.AcademicYearId == currentEnrollment.AcademicYearId))
            return OperationResult<EnrollmentDto>.Failure("الطالب مسجل بالفعل في الفصل الجديد");

        if (newClass.Capacity.HasValue)
        {
            var activeCount = await _unitOfWork.StudentEnrollments.GetActiveCountByClassAsync(
                request.NewClassId,
                currentEnrollment.AcademicYearId);
            if (activeCount >= newClass.Capacity.Value)
                return OperationResult<EnrollmentDto>.Failure("الفصل الهدف وصل إلى السعة القصوى");
        }

        // Perform transfer
        currentEnrollment.LeftAt = transferDate;
        currentEnrollment.TransferReason = request.TransferReason;
        _unitOfWork.StudentEnrollments.Update(currentEnrollment);

        var newEnrollment = new StudentEnrollment
        {
            StudentId = currentEnrollment.StudentId,
            ClassId = request.NewClassId,
            AcademicYearId = currentEnrollment.AcademicYearId,
            EnrolledAt = transferDate
        };

        await _unitOfWork.StudentEnrollments.AddAsync(newEnrollment);
        await _unitOfWork.SaveChangesAsync();

        var dto = _mapper.Map<EnrollmentDto>(newEnrollment);
        return OperationResult<EnrollmentDto>.Success(dto, "تم نقل الطالب إلى الفصل الجديد بنجاح");
    }

    public async Task<OperationResult<IEnumerable<EnrollmentDto>>> GetEnrollmentsByStudentAsync(int studentId)
    {
        var student = await _unitOfWork.Students.GetByIdAsync(studentId);
        if (student == null || student.IsDeleted)
            return OperationResult<IEnumerable<EnrollmentDto>>.Failure("الطالب غير موجود");

        var enrollments = await _unitOfWork.StudentEnrollments.GetHistoryByStudentAsync(studentId);
        var ordered = enrollments.Where(e => !e.IsDeleted)
            .OrderByDescending(e => e.EnrolledAt)
            .ToList();

        var dtos = _mapper.Map<IEnumerable<EnrollmentDto>>(ordered);
        foreach (var dto in dtos)
            dto.StudentName = student.FullName;

        return OperationResult<IEnumerable<EnrollmentDto>>.Success(dtos);
    }

    public async Task<OperationResult<IEnumerable<EnrollmentDto>>> GetEnrollmentsByClassAsync(int classId, int academicYearId, bool activeOnly)
    {
        var classEntity = await _unitOfWork.Classes.GetByIdAsync(classId);
        if (classEntity == null || classEntity.IsDeleted)
            return OperationResult<IEnumerable<EnrollmentDto>>.Failure("الفصل غير موجود");

        var year = await _unitOfWork.AcademicYears.GetByIdAsync(academicYearId);
        if (year == null || year.IsDeleted)
            return OperationResult<IEnumerable<EnrollmentDto>>.Failure("السنة الدراسية غير موجودة");

        IReadOnlyList<StudentEnrollment> enrollments;

        if (activeOnly)
            enrollments = await _unitOfWork.StudentEnrollments.GetByClassWithStudentAsync(classId, academicYearId);
        else
            enrollments = await _unitOfWork.StudentEnrollments.GetByClassAndYearAsync(classId, academicYearId);

        var filtered = enrollments.Where(e => !e.IsDeleted).ToList();
        var dtos = _mapper.Map<IEnumerable<EnrollmentDto>>(filtered);
        foreach (var dto in dtos)
        {
            dto.ClassName = classEntity.Name;
            dto.AcademicYearName = year.Name;
        }

        return OperationResult<IEnumerable<EnrollmentDto>>.Success(dtos);
    }

    public async Task<OperationResult<PagedResult<EnrollmentDto>>> GetEnrollmentsByClassPagedAsync(int classId, int academicYearId, int page, int pageSize, bool activeOnly = true, string? searchTerm = null)
    {
        var classEntity = await _unitOfWork.Classes.GetByIdAsync(classId);
        if (classEntity == null || classEntity.IsDeleted)
            return OperationResult<PagedResult<EnrollmentDto>>.Failure("الفصل غير موجود", 404);

        var year = await _unitOfWork.AcademicYears.GetByIdAsync(academicYearId);
        if (year == null || year.IsDeleted)
            return OperationResult<PagedResult<EnrollmentDto>>.Failure("السنة الدراسية غير موجودة", 404);

        page = page < 1 ? 1 : page;
        pageSize = pageSize <= 0 ? 20 : Math.Min(pageSize, 100);

        var enrollments = await _unitOfWork.StudentEnrollments.GetByClassWithStudentAsync(classId, academicYearId);

        var query = enrollments
            .Where(e => !e.IsDeleted && e.Student != null && !e.Student.IsDeleted);

        if (activeOnly)
            query = query.Where(e => e.LeftAt == null);

        if (!string.IsNullOrWhiteSpace(searchTerm))
            query = query.Where(e => e.Student!.FullName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));

        var totalCount = query.Count();
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        var pagedItems = query
            .OrderBy(e => e.Student!.FullName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new EnrollmentDto
            {
                Id = e.Id,
                StudentId = e.StudentId,
                StudentName = e.Student!.FullName,
                ClassId = e.ClassId,
                ClassName = classEntity.Name,
                AcademicYearId = e.AcademicYearId,
                AcademicYearName = year.Name,
                EnrolledAt = e.EnrolledAt,
                LeftAt = e.LeftAt,
                TransferReason = e.TransferReason
            })
            .ToList();

        var result = new PagedResult<EnrollmentDto>
        {
            Items = pagedItems,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };

        return OperationResult<PagedResult<EnrollmentDto>>.Success(result, "تم جلب الطلاب بنجاح");
    }

    public async Task<OperationResult<EnrollmentDto>> GetActiveEnrollmentByStudentAsync(int studentId, int academicYearId)
    {
        var student = await _unitOfWork.Students.GetByIdAsync(studentId);
        if (student == null || student.IsDeleted)
            return OperationResult<EnrollmentDto>.Failure("الطالب غير موجود");

        var enrollment = await _unitOfWork.StudentEnrollments.GetActiveByStudentAndYearAsync(studentId, academicYearId);
        if (enrollment == null || enrollment.IsDeleted)
            return OperationResult<EnrollmentDto>.Failure("لا يوجد تسجيل نشط للطالب في هذه السنة الدراسية");

        var classEntity = await _unitOfWork.Classes.GetByIdAsync(enrollment.ClassId);
        var year = await _unitOfWork.AcademicYears.GetByIdAsync(enrollment.AcademicYearId);

        var dto = _mapper.Map<EnrollmentDto>(enrollment);
        dto.StudentName = student.FullName;
        dto.ClassName = classEntity?.Name ?? string.Empty;
        dto.AcademicYearName = year?.Name ?? string.Empty;

        return OperationResult<EnrollmentDto>.Success(dto);
    }

    public async Task<OperationResult<PagedResult<TransferHistoryDto>>> GetTransferHistoryAsync(int academicYearId, int page = 1, int pageSize = 20)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize <= 0 ? 20 : Math.Min(pageSize, 100);

        var transfers = await _unitOfWork.StudentEnrollments.GetTransfersHistoryAsync(academicYearId, page, pageSize);

        // Fetch all active enrollments for this year in one query (avoid N+1)
        var activeEnrollments = await _unitOfWork.StudentEnrollments.GetActiveEnrollmentsByYearAsync(academicYearId);

        var activeEnrollmentLookup = activeEnrollments
            .Where(e => !e.IsDeleted && e.Student != null && !e.Student.IsDeleted)
            .ToDictionary(e => e.StudentId, e => e.Class?.Name ?? "غير معروف");

        var dtos = transfers
            .Where(t => !t.IsDeleted && t.Student != null && !t.Student.IsDeleted)
            .Select(t => new TransferHistoryDto
            {
                Id = t.Id,
                StudentName = t.Student!.FullName,
                FromClass = t.Class?.Name ?? string.Empty,
                ToClass = activeEnrollmentLookup.TryGetValue(t.StudentId, out var className) ? className : "غير معروف",
                TransferDate = t.LeftAt,
                Reason = t.TransferReason
            })
            .ToList();

        // Get total count for pagination (separate query)
        var totalCount = await _unitOfWork.StudentEnrollments.GetTransfersCountAsync(academicYearId);
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        var result = new PagedResult<TransferHistoryDto>
        {
            Items = dtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };

        return OperationResult<PagedResult<TransferHistoryDto>>.Success(result, "تم جلب سجل النقل بنجاح");
    }

    public async Task<OperationResult<BulkEnrollResultDto>> BulkEnrollStudentsAsync(BulkEnrollStudentsRequest request)
    {
        if (request.StudentIds == null || request.StudentIds.Count == 0)
            return OperationResult<BulkEnrollResultDto>.Failure("يجب توفير طالب واحد على الأقل");

        var classEntity = await _unitOfWork.Classes.GetByIdAsync(request.ClassId);
        if (classEntity == null || classEntity.IsDeleted)
            return OperationResult<BulkEnrollResultDto>.Failure("الفصل غير موجود");

        if (classEntity.Status != Project.Domain.Enums.ClassStatus.Active)
            return OperationResult<BulkEnrollResultDto>.Failure("لا يمكن تسجيل الطلاب في فصل غير نشط");

        var year = await _unitOfWork.AcademicYears.GetByIdAsync(request.AcademicYearId);
        if (year == null || year.IsDeleted)
            return OperationResult<BulkEnrollResultDto>.Failure("السنة الدراسية غير موجودة");

        if (classEntity.AcademicYearId != request.AcademicYearId)
            return OperationResult<BulkEnrollResultDto>.Failure("الفصل لا ينتمي إلى السنة الدراسية المحددة");

        if (request.EnrolledAt > DateOnly.FromDateTime(DateTime.UtcNow))
            return OperationResult<BulkEnrollResultDto>.Failure("تاريخ التسجيل لا يمكن أن يكون في المستقبل");

        var result = new BulkEnrollResultDto { TotalRequested = request.StudentIds.Count };

        foreach (var studentId in request.StudentIds)
        {
            var student = await _unitOfWork.Students.GetByIdAsync(studentId);
            if (student == null || student.IsDeleted)
            {
                result.FailureCount++;
                result.Failures.Add(new BulkEnrollFailureDto
                {
                    StudentId = studentId,
                    StudentName = "غير معروف",
                    Reason = "الطالب غير موجود"
                });
                continue;
            }

            if (await _unitOfWork.StudentEnrollments.HasActiveEnrollmentAsync(studentId, request.AcademicYearId))
            {
                result.FailureCount++;
                result.Failures.Add(new BulkEnrollFailureDto
                {
                    StudentId = studentId,
                    StudentName = student.FullName,
                    Reason = "الطالب لديه بالفعل قيد نشط في هذه السنة الدراسية"
                });
                continue;
            }

            if (classEntity.Capacity.HasValue)
            {
                var activeCount = await _unitOfWork.StudentEnrollments.GetActiveCountByClassAsync(
                    request.ClassId,
                    request.AcademicYearId);
                if (activeCount + result.SuccessCount >= classEntity.Capacity.Value)
                {
                    result.FailureCount++;
                    result.Failures.Add(new BulkEnrollFailureDto
                    {
                        StudentId = studentId,
                        StudentName = student.FullName,
                        Reason = "الفصل وصل إلى السعة القصوى"
                    });
                    continue;
                }
            }

            var enrollment = new StudentEnrollment
            {
                StudentId = studentId,
                ClassId = request.ClassId,
                AcademicYearId = request.AcademicYearId,
                EnrolledAt = request.EnrolledAt
            };

            await _unitOfWork.StudentEnrollments.AddAsync(enrollment);
            result.SuccessCount++;
        }

        await _unitOfWork.SaveChangesAsync();
        return OperationResult<BulkEnrollResultDto>.Success(result,
            $"تم تسجيل {result.SuccessCount} من {result.TotalRequested} طالب بنجاح");
    }

    public async Task<OperationResult<BulkTransferResultDto>> BulkTransferStudentsAsync(BulkTransferStudentsRequest request)
    {
        if (request.EnrollmentIds == null || request.EnrollmentIds.Count == 0)
            return OperationResult<BulkTransferResultDto>.Failure("يجب اختيار طالب واحد على الأقل");

        var newClass = await _unitOfWork.Classes.GetByIdAsync(request.NewClassId);
        if (newClass == null || newClass.IsDeleted)
            return OperationResult<BulkTransferResultDto>.Failure("الفصل الهدف غير موجود");

        if (newClass.Status != Project.Domain.Enums.ClassStatus.Active)
            return OperationResult<BulkTransferResultDto>.Failure("لا يمكن النقل إلى فصل غير نشط");

        var transferDate = request.TransferDate;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (transferDate > today)
            return OperationResult<BulkTransferResultDto>.Failure("تاريخ النقل لا يمكن أن يكون في المستقبل.");

        var result = new BulkTransferResultDto { TotalRequested = request.EnrollmentIds.Count };

        // Load all enrollments first
        var enrollments = await _unitOfWork.StudentEnrollments.GetByIdsWithDetailsAsync(request.EnrollmentIds);
        var enrollmentDict = enrollments.ToDictionary(e => e.Id);

        foreach (var enrollmentId in request.EnrollmentIds)
        {
            if (!enrollmentDict.TryGetValue(enrollmentId, out var currentEnrollment))
            {
                result.FailureCount++;
                result.Failures.Add(new BulkTransferFailureDto
                {
                    EnrollmentId = enrollmentId,
                    StudentName = "غير معروف",
                    Reason = "التسجيل غير موجود"
                });
                continue;
            }

            if (currentEnrollment.IsDeleted)
            {
                result.FailureCount++;
                result.Failures.Add(new BulkTransferFailureDto
                {
                    EnrollmentId = enrollmentId,
                    StudentName = currentEnrollment.Student?.FullName ?? "غير معروف",
                    Reason = "التسجيل محذوف"
                });
                continue;
            }

            if (currentEnrollment.LeftAt != null)
            {
                result.FailureCount++;
                result.Failures.Add(new BulkTransferFailureDto
                {
                    EnrollmentId = enrollmentId,
                    StudentName = currentEnrollment.Student?.FullName ?? "غير معروف",
                    Reason = "التسجيل الحالي مغلق بالفعل"
                });
                continue;
            }

            var currentClass = currentEnrollment.Class;
            if (currentClass == null || currentClass.IsDeleted)
            {
                result.FailureCount++;
                result.Failures.Add(new BulkTransferFailureDto
                {
                    EnrollmentId = enrollmentId,
                    StudentName = currentEnrollment.Student?.FullName ?? "غير معروف",
                    Reason = "الفصل الحالي غير موجود"
                });
                continue;
            }

            // Validation: Same Academic Year
            if (newClass.AcademicYearId != currentEnrollment.AcademicYearId)
            {
                result.FailureCount++;
                result.Failures.Add(new BulkTransferFailureDto
                {
                    EnrollmentId = enrollmentId,
                    StudentName = currentEnrollment.Student?.FullName ?? "غير معروف",
                    Reason = "لا يمكن النقل لسنة دراسية مختلفة"
                });
                continue;
            }

            // Validation: Same Grade Level
            if (newClass.GradeLevelId != currentClass.GradeLevelId)
            {
                result.FailureCount++;
                result.Failures.Add(new BulkTransferFailureDto
                {
                    EnrollmentId = enrollmentId,
                    StudentName = currentEnrollment.Student?.FullName ?? "غير معروف",
                    Reason = "لا يمكن النقل لصف دراسي مختلف"
                });
                continue;
            }

            // Validation: TransferDate >= EnrolledAt
            if (transferDate < currentEnrollment.EnrolledAt)
            {
                result.FailureCount++;
                result.Failures.Add(new BulkTransferFailureDto
                {
                    EnrollmentId = enrollmentId,
                    StudentName = currentEnrollment.Student?.FullName ?? "غير معروف",
                    Reason = "تاريخ النقل قبل تاريخ التسجيل الحالي"
                });
                continue;
            }

            // Check if student already enrolled in target class
            var existingEnrollments = await _unitOfWork.StudentEnrollments.GetHistoryByStudentAsync(currentEnrollment.StudentId);
            if (existingEnrollments.Any(e => !e.IsDeleted && e.LeftAt == null
                && e.ClassId == request.NewClassId && e.AcademicYearId == currentEnrollment.AcademicYearId))
            {
                result.FailureCount++;
                result.Failures.Add(new BulkTransferFailureDto
                {
                    EnrollmentId = enrollmentId,
                    StudentName = currentEnrollment.Student?.FullName ?? "غير معروف",
                    Reason = "الطالب مسجل بالفعل في الفصل الهدف"
                });
                continue;
            }

            if (newClass.Capacity.HasValue)
            {
                var activeCount = await _unitOfWork.StudentEnrollments.GetActiveCountByClassAsync(
                    request.NewClassId,
                    currentEnrollment.AcademicYearId);
                if (activeCount + result.SuccessCount >= newClass.Capacity.Value)
                {
                    result.FailureCount++;
                    result.Failures.Add(new BulkTransferFailureDto
                    {
                        EnrollmentId = enrollmentId,
                        StudentName = currentEnrollment.Student?.FullName ?? "غير معروف",
                        Reason = "الفصل الهدف وصل إلى السعة القصوى"
                    });
                    continue;
                }
            }

            // Perform transfer
            currentEnrollment.LeftAt = transferDate;
            currentEnrollment.TransferReason = request.TransferReason;
            _unitOfWork.StudentEnrollments.Update(currentEnrollment);

            var newEnrollment = new StudentEnrollment
            {
                StudentId = currentEnrollment.StudentId,
                ClassId = request.NewClassId,
                AcademicYearId = currentEnrollment.AcademicYearId,
                EnrolledAt = transferDate
            };

            await _unitOfWork.StudentEnrollments.AddAsync(newEnrollment);
            result.SuccessCount++;
        }

        await _unitOfWork.SaveChangesAsync();

        var message = result.FailureCount == 0
            ? $"تم نقل {result.SuccessCount} طالب بنجاح"
            : $"تم نقل {result.SuccessCount} من {result.TotalRequested} طالب، وفشل {result.FailureCount}";

        return OperationResult<BulkTransferResultDto>.Success(result, message);
    }
}
