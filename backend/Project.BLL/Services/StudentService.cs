using AutoMapper;
using Common.Results;
using Project.BLL.DTOs.Students;
using Project.BLL.Interfaces;
using Project.DAL.Interfaces;
using Project.Domain.Entities;
using Project.Domain.Enums;

namespace Project.BLL.Services;

public class StudentService : IStudentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public StudentService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<OperationResult<StudentDto>> CreateStudentAsync(CreateStudentRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.NationalId))
        {
            var existing = await _unitOfWork.Students.GetByNationalIdAsync(request.NationalId);
            if (existing != null && !existing.IsDeleted)
                return OperationResult<StudentDto>.Failure("الرقم القومي موجود بالفعل");
        }

        var student = _mapper.Map<Student>(request);
        await _unitOfWork.Students.AddAsync(student);
        await _unitOfWork.SaveChangesAsync();

        var dto = _mapper.Map<StudentDto>(student);
        return OperationResult<StudentDto>.Success(dto, "تم إنشاء الطالب بنجاح");
    }

    public async Task<OperationResult<StudentDto>> UpdateStudentAsync(UpdateStudentRequest request)
    {
        var student = await _unitOfWork.Students.GetByIdAsync(request.Id);
        if (student == null || student.IsDeleted)
            return OperationResult<StudentDto>.Failure("الطالب غير موجود");

        student.FullName = request.FullName;
        student.Gender = request.Gender;
        student.BirthDate = request.BirthDate;
        student.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Students.Update(student);
        await _unitOfWork.SaveChangesAsync();

        var dto = _mapper.Map<StudentDto>(student);
        return OperationResult<StudentDto>.Success(dto, "تم تحديث بيانات الطالب بنجاح");
    }

    public async Task<OperationResult> LinkUserAccountAsync(LinkStudentUserRequest request)
    {
        var student = await _unitOfWork.Students.GetByIdAsync(request.StudentId);
        if (student == null || student.IsDeleted)
            return OperationResult.Failure("الطالب غير موجود");

        var user = await _unitOfWork.Users.GetByIdAsync(request.UserId);
        if (user == null || user.IsDeleted)
            return OperationResult.Failure("المستخدم غير موجود");

        if (user.Role != UserRole.Student)
            return OperationResult.Failure("يمكن ربط حسابات الطلاب فقط");

        var linkedStudent = await _unitOfWork.Students.GetByUserIdAsync(request.UserId);
        if (linkedStudent != null && !linkedStudent.IsDeleted && linkedStudent.Id != request.StudentId)
            return OperationResult.Failure("هذا المستخدم مرتبط بطالب آخر بالفعل");

        student.UserId = request.UserId;
        _unitOfWork.Students.Update(student);
        await _unitOfWork.SaveChangesAsync();

        return OperationResult.Success("تم ربط حساب الطالب بنجاح");
    }

    public async Task<OperationResult<StudentDto>> GetStudentByIdAsync(int id)
    {
        var student = await _unitOfWork.Students.GetByIdAsync(id);
        if (student == null || student.IsDeleted)
            return OperationResult<StudentDto>.Failure("الطالب غير موجود");

        var dto = _mapper.Map<StudentDto>(student);
        return OperationResult<StudentDto>.Success(dto);
    }

    public async Task<OperationResult<IEnumerable<StudentDto>>> SearchStudentsAsync(StudentSearchFilter filter)
    {
        if (string.IsNullOrWhiteSpace(filter.SearchTerm) || filter.SearchTerm.Length < 2)
            return OperationResult<IEnumerable<StudentDto>>.Failure("يجب أن يتكون مصطلح البحث من حرفين على الأقل");

        var students = await _unitOfWork.Students.SearchByNameAsync(filter.SearchTerm);

        if (filter.ClassId.HasValue && filter.AcademicYearId.HasValue)
        {
            var enrollments = await _unitOfWork.StudentEnrollments.GetByClassAndYearAsync(
                filter.ClassId.Value, filter.AcademicYearId.Value);
            var enrolledStudentIds = enrollments
                .Where(e => !e.IsDeleted && e.LeftAt == null)
                .Select(e => e.StudentId)
                .ToHashSet();
            students = students.Where(s => enrolledStudentIds.Contains(s.Id)).ToList();
        }

        if (filter.IsActive.HasValue)
        {
            if (filter.IsActive.Value)
                students = students.Where(s => s.IsActive && s.LifecycleStatus == StudentLifecycleStatus.Active).ToList();
            else
                students = students.Where(s => !s.IsActive || s.LifecycleStatus != StudentLifecycleStatus.Active).ToList();
        }

        var dtos = _mapper.Map<IEnumerable<StudentDto>>(students);
        return OperationResult<IEnumerable<StudentDto>>.Success(dtos);
    }

    public async Task<OperationResult<IEnumerable<StudentDto>>> GetAllStudentsAsync()
    {
        var students = await _unitOfWork.Students.GetAllAsync();
        var active = students.Where(s => !s.IsDeleted).ToList();
        var dtos = _mapper.Map<IEnumerable<StudentDto>>(active);
        return OperationResult<IEnumerable<StudentDto>>.Success(dtos);
    }

    public async Task<OperationResult<StudentDto>> GetStudentByUserIdAsync(int userId)
    {
        var student = await _unitOfWork.Students.GetByUserIdAsync(userId);
        if (student == null || student.IsDeleted)
            return OperationResult<StudentDto>.Failure("الطالب غير موجود");

        var dto = _mapper.Map<StudentDto>(student);
        return OperationResult<StudentDto>.Success(dto);
    }

    public async Task<OperationResult> DeleteStudentAsync(int id)
    {
        var student = await _unitOfWork.Students.GetByIdAsync(id);
        if (student == null || student.IsDeleted)
            return OperationResult.Failure("الطالب غير موجود");

        _unitOfWork.Students.SoftDelete(student);
        await _unitOfWork.SaveChangesAsync();
        return OperationResult.Success("تم حذف الطالب بنجاح");
    }

    public async Task<OperationResult<BulkCreateStudentsResultDto>> BulkCreateStudentsAsync(BulkCreateStudentsRequest request)
    {
        if (request.Students == null || request.Students.Count == 0)
            return OperationResult<BulkCreateStudentsResultDto>.Failure("يجب توفير طالب واحد على الأقل");

        var result = new BulkCreateStudentsResultDto { TotalRequested = request.Students.Count };
        var createdStudents = new List<Student>();

        for (int i = 0; i < request.Students.Count; i++)
        {
            var req = request.Students[i];

            if (string.IsNullOrWhiteSpace(req.FullName))
            {
                result.FailureCount++;
                result.Failures.Add(new BulkCreateStudentFailureDto
                {
                    Index = i,
                    FullName = req.FullName ?? "",
                    Reason = "اسم الطالب مطلوب"
                });
                continue;
            }

            if (!string.IsNullOrWhiteSpace(req.NationalId))
            {
                var existing = await _unitOfWork.Students.GetByNationalIdAsync(req.NationalId);
                if (existing != null && !existing.IsDeleted)
                {
                    result.FailureCount++;
                    result.Failures.Add(new BulkCreateStudentFailureDto
                    {
                        Index = i,
                        FullName = req.FullName,
                        Reason = $"الرقم القومي '{req.NationalId}' موجود بالفعل للطالب {existing.FullName}"
                    });
                    continue;
                }
            }

            var student = _mapper.Map<Student>(req);
            await _unitOfWork.Students.AddAsync(student);
            createdStudents.Add(student);
            result.SuccessCount++;
        }

        await _unitOfWork.SaveChangesAsync();

        result.CreatedStudents = _mapper.Map<List<StudentDto>>(createdStudents);
        return OperationResult<BulkCreateStudentsResultDto>.Success(result,
            $"تم إنشاء {result.SuccessCount} من {result.TotalRequested} طالب بنجاح");
    }
}
