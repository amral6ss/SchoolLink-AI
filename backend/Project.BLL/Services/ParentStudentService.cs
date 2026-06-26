using AutoMapper;
using Common.Results;
using Project.BLL.DTOs.ParentStudents;
using Project.BLL.DTOs.Students;
using Project.BLL.Interfaces;
using Project.DAL.Interfaces;
using Project.Domain.Entities;
using Project.Domain.Enums;

namespace Project.BLL.Services;

public class ParentStudentService : IParentStudentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public ParentStudentService(IUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<OperationResult<ParentStudentDto>> LinkParentToStudentAsync(LinkParentStudentRequest request)
    {
        var parent = await _unitOfWork.Users.GetByIdAsync(request.ParentId);
        if (parent == null || parent.IsDeleted)
            return OperationResult<ParentStudentDto>.Failure("ولي الأمر غير موجود");

        if (parent.Role != UserRole.Parent)
            return OperationResult<ParentStudentDto>.Failure("المستخدم ليس ولي أمر");

        var student = await _unitOfWork.Students.GetByIdAsync(request.StudentId);
        if (student == null || student.IsDeleted)
            return OperationResult<ParentStudentDto>.Failure("الطالب غير موجود");

        var exists = await _unitOfWork.ParentStudents.ExistsByParentAndStudentAsync(request.ParentId, request.StudentId);
        if (exists)
            return OperationResult<ParentStudentDto>.Failure("ولي الأمر مرتبط بالفعل بهذا الطالب");

        var link = _mapper.Map<ParentStudent>(request);
        await _unitOfWork.ParentStudents.AddAsync(link);
        await _unitOfWork.SaveChangesAsync();

        var dto = _mapper.Map<ParentStudentDto>(link);
        return OperationResult<ParentStudentDto>.Success(dto, "تم ربط ولي الأمر بالطالب بنجاح");
    }

    public async Task<OperationResult> UnlinkParentFromStudentAsync(int parentStudentId)
    {
        var link = await _unitOfWork.ParentStudents.GetByIdAsync(parentStudentId);
        if (link == null || link.IsDeleted)
            return OperationResult.Failure("الرابط غير موجود");

        _unitOfWork.ParentStudents.SoftDelete(link);
        await _unitOfWork.SaveChangesAsync();

        return OperationResult.Success("تم إلغاء ربط ولي الأمر بالطالب بنجاح");
    }

    public async Task<OperationResult<IEnumerable<StudentDto>>> GetStudentsByParentAsync(int parentId)
    {
        var parent = await _unitOfWork.Users.GetByIdAsync(parentId);
        if (parent == null || parent.IsDeleted)
            return OperationResult<IEnumerable<StudentDto>>.Failure("ولي الأمر غير موجود");

        var links = await _unitOfWork.ParentStudents.GetByParentIdAsync(parentId);
        var students = links
            .Where(l => !l.IsDeleted && !l.Student.IsDeleted)
            .Select(l => l.Student)
            .ToList();

        var dtos = _mapper.Map<IEnumerable<StudentDto>>(students);
        return OperationResult<IEnumerable<StudentDto>>.Success(dtos);
    }

    public async Task<OperationResult<IEnumerable<ParentDashboardChildDto>>> GetDashboardChildrenByParentAsync(int parentId)
    {
        var parent = await _unitOfWork.Users.GetByIdAsync(parentId);
        if (parent == null || parent.IsDeleted)
            return OperationResult<IEnumerable<ParentDashboardChildDto>>.Failure("ولي الأمر غير موجود");

        var links = await _unitOfWork.ParentStudents.GetWithStudentDetailsByParentAsync(parentId);
        var children = links
            .Where(l => !l.IsDeleted && !l.Student.IsDeleted)
            .Select(l =>
            {
                var activeEnrollment = l.Student.Enrollments
                    .FirstOrDefault(e => e.LeftAt == null && !e.IsDeleted);

                return new ParentDashboardChildDto
                {
                    StudentId = l.StudentId,
                    StudentName = l.Student.FullName,
                    ClassName = activeEnrollment?.Class?.Name,
                    GradeLevelName = activeEnrollment?.Class?.GradeLevel?.Name,
                    IsActive = activeEnrollment != null && l.Student.LifecycleStatus == StudentLifecycleStatus.Active,
                    Relationship = l.Relationship
                };
            })
            .ToList();

        return OperationResult<IEnumerable<ParentDashboardChildDto>>.Success(children);
    }

    public async Task<OperationResult<IEnumerable<ParentStudentDto>>> GetParentsByStudentAsync(int studentId)
    {
        var student = await _unitOfWork.Students.GetByIdAsync(studentId);
        if (student == null || student.IsDeleted)
            return OperationResult<IEnumerable<ParentStudentDto>>.Failure("الطالب غير موجود");

        var links = await _unitOfWork.ParentStudents.GetByStudentIdAsync(studentId);
        var filtered = links.Where(l => !l.IsDeleted);

        var dtos = _mapper.Map<IEnumerable<ParentStudentDto>>(filtered);
        return OperationResult<IEnumerable<ParentStudentDto>>.Success(dtos);
    }

    public async Task<OperationResult<ParentStudentDto>> UpdateRelationshipAsync(int parentStudentId, RelationshipType newRelationship)
    {
        var link = await _unitOfWork.ParentStudents.GetByIdAsync(parentStudentId);
        if (link == null || link.IsDeleted)
            return OperationResult<ParentStudentDto>.Failure("الرابط غير موجود");

        link.Relationship = newRelationship;
        _unitOfWork.ParentStudents.Update(link);
        await _unitOfWork.SaveChangesAsync();

        var dto = _mapper.Map<ParentStudentDto>(link);
        return OperationResult<ParentStudentDto>.Success(dto, "تم تحديث صلة القرابة بنجاح");
    }

    public async Task<OperationResult<bool>> CheckRelationshipAsync(int parentId, int studentId)
    {
        var links = await _unitOfWork.ParentStudents.GetByParentIdAsync(parentId);
        var exists = links.Any(l => !l.IsDeleted && l.StudentId == studentId);
        return OperationResult<bool>.Success(exists);
    }
}
