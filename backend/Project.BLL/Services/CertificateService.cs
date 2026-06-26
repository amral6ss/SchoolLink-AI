using AutoMapper;
using Common.Results;
using Project.BLL.DTOs.Certificates;
using Project.BLL.Interfaces;
using Project.DAL.Interfaces;
using Project.Domain.Entities;
using Project.Domain.Enums;

namespace Project.BLL.Services
{
    public class CertificateService : ICertificateService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;

        public CertificateService(IUnitOfWork unitOfWork, IMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task<OperationResult<CertificateDto>> CreateCertificateAsync(CreateCertificateRequest request)
        {
            var entity = new Certificate
            {
                Name = request.Name,
                GradeLevel = request.GradeLevel,
                Term = request.Term ?? string.Empty,
                ExamRole = request.ExamRole ?? string.Empty,
                Year = request.Year ?? string.Empty,
                Subjects = request.Subjects.Select((s, i) => new CertificateSubject
                {
                    SubjectName = s.SubjectName,
                    MaxScore = s.MaxScore,
                    MinScore = s.MinScore,
                    IsCountedInTotal = s.IsCountedInTotal,
                    SortOrder = i + 1,
                }).ToList()
            };

            await _unitOfWork.Certificates.AddAsync(entity);
            await _unitOfWork.SaveChangesAsync();

            var dto = MapToDto(entity);
            return OperationResult<CertificateDto>.Success(dto, "تم إنشاء الشهادة بنجاح");
        }

        public async Task<OperationResult<CertificateDto>> UpdateCertificateAsync(UpdateCertificateRequest request)
        {
            var entity = await _unitOfWork.Certificates.GetByIdAsync(request.Id);
            if (entity is null || entity.IsDeleted)
                return OperationResult<CertificateDto>.Failure("الشهادة غير موجودة");

            entity.Name = request.Name;
            entity.GradeLevel = request.GradeLevel;
            entity.Term = request.Term ?? string.Empty;
            entity.ExamRole = request.ExamRole ?? string.Empty;
            entity.Year = request.Year ?? string.Empty;
            entity.UpdatedAt = DateTime.UtcNow;

            var existingSubjects = await _unitOfWork.CertificateSubjects
                .FindAsync(s => s.CertificateId == entity.Id);

            foreach (var sub in existingSubjects)
                _unitOfWork.CertificateSubjects.SoftDelete(sub);

            foreach (var (s, i) in request.Subjects.Select((s, i) => (s, i)))
            {
                var subjectEntity = new CertificateSubject
                {
                    CertificateId = entity.Id,
                    SubjectName = s.SubjectName,
                    MaxScore = s.MaxScore,
                    MinScore = s.MinScore,
                    IsCountedInTotal = s.IsCountedInTotal,
                    SortOrder = i + 1,
                };
                await _unitOfWork.CertificateSubjects.AddAsync(subjectEntity);
            }

            _unitOfWork.Certificates.Update(entity);
            await _unitOfWork.SaveChangesAsync();

            var updated = await _unitOfWork.Certificates.GetByIdAsync(request.Id);
            var dto = MapToDto(updated!);
            return OperationResult<CertificateDto>.Success(dto, "تم تحديث الشهادة بنجاح");
        }

        public async Task<OperationResult> DeleteCertificateAsync(int id)
        {
            var entity = await _unitOfWork.Certificates.GetByIdAsync(id);
            if (entity is null || entity.IsDeleted)
                return OperationResult.Failure("الشهادة غير موجودة");

            _unitOfWork.Certificates.SoftDelete(entity);

            var subjects = await _unitOfWork.CertificateSubjects
                .FindAsync(s => s.CertificateId == id);
            foreach (var sub in subjects)
                _unitOfWork.CertificateSubjects.SoftDelete(sub);

            await _unitOfWork.SaveChangesAsync();
            return OperationResult.Success("تم حذف الشهادة بنجاح");
        }

        public async Task<OperationResult<CertificateDto>> GetCertificateByIdAsync(int id)
        {
            var entity = await _unitOfWork.Certificates.GetByIdAsync(id);
            if (entity is null || entity.IsDeleted)
                return OperationResult<CertificateDto>.Failure("الشهادة غير موجودة");

            var subjects = await _unitOfWork.CertificateSubjects
                .FindAsync(s => s.CertificateId == id && !s.IsDeleted);

            var dto = MapToDto(entity, subjects);
            return OperationResult<CertificateDto>.Success(dto, "تم جلب الشهادة بنجاح");
        }

        public async Task<OperationResult<IEnumerable<CertificateDto>>> GetAllCertificatesAsync()
        {
            var certificates = await _unitOfWork.Certificates.GetAllAsync();
            var active = certificates.Where(c => !c.IsDeleted).OrderByDescending(c => c.CreatedAt).ToList();

            var result = new List<CertificateDto>();
            foreach (var cert in active)
            {
                var subjects = await _unitOfWork.CertificateSubjects
                    .FindAsync(s => s.CertificateId == cert.Id && !s.IsDeleted);
                result.Add(MapToDto(cert, subjects));
            }

            return OperationResult<IEnumerable<CertificateDto>>.Success(result, "تم جلب الشهادات بنجاح");
        }

        // ════════════════════════════════════════════════════════════════
        //  CERTIFICATE DATA GENERATION (real data from database)
        // ════════════════════════════════════════════════════════════════

        public async Task<OperationResult<CertificateGenerateResponse>> GenerateCertificateDataAsync(
            int certificateId, List<int> classIds, int term)
        {
            var academicTerm = (AcademicTerm)term;

            // 1. Get certificate with subjects
            var cert = await _unitOfWork.Certificates.GetByIdAsync(certificateId);
            if (cert is null || cert.IsDeleted)
                return OperationResult<CertificateGenerateResponse>.Failure("الشهادة غير موجودة");

            var certSubjects = (await _unitOfWork.CertificateSubjects
                .FindAsync(s => s.CertificateId == certificateId && !s.IsDeleted))
                .OrderBy(s => s.SortOrder).ToList();
            if (!certSubjects.Any())
                return OperationResult<CertificateGenerateResponse>.Failure("لا توجد مواد في هذه الشهادة");

            // 2. Get all subjects for name matching
            var allSubjects = (await _unitOfWork.Subjects.GetAllAsync())
                .Where(s => !s.IsDeleted).ToList();

            var totalMaxBasic = certSubjects.Where(s => s.IsCountedInTotal).Sum(s => (decimal)s.MaxScore);
            var totalMaxActivities = certSubjects.Where(s => !s.IsCountedInTotal).Sum(s => (decimal)s.MaxScore);
            var totalMaxAll = totalMaxBasic + totalMaxActivities;

            // 3. Process each class
            var allStudentsData = new List<CertificateStudentData>();
            string? classNameHint = null;

            foreach (var classId in classIds)
            {
                var classEntity = await _unitOfWork.Classes.GetByIdAsync(classId);
                if (classEntity is null || classEntity.IsDeleted) continue;
                classNameHint ??= classEntity.Name;

                var enrollments = (await _unitOfWork.StudentEnrollments
                    .FindAsync(e => e.ClassId == classId && e.LeftAt == null && !e.IsDeleted))
                    .ToList();
                if (!enrollments.Any()) continue;

                var studentIds = enrollments.Select(e => e.StudentId).Distinct().ToList();
                var students = await _unitOfWork.Students.FindAsync(s => studentIds.Contains(s.Id));
                var studentDict = students.ToDictionary(s => s.Id, s => s);

                var finalGrades = await _unitOfWork.FinalGrades.GetByClassIdAsync(classId, academicTerm);
                var gradesByEnrollment = finalGrades
                    .Where(fg => !fg.IsDeleted && fg.SubjectId.HasValue)
                    .GroupBy(fg => fg.EnrollmentId)
                    .ToDictionary(g => g.Key, g => g.ToDictionary(fg => fg.SubjectId!.Value, fg => fg));

                foreach (var enrollment in enrollments)
                {
                    if (!studentDict.TryGetValue(enrollment.StudentId, out var student))
                        continue;

                    var studentGradeBySubject = gradesByEnrollment.GetValueOrDefault(enrollment.Id) ?? new();
                    var subjects = new List<CertificateStudentSubject>();
                    foreach (var cs in certSubjects)
                    {
                        decimal? score = null;
                        var matchedSubject = MatchSubject(cs.SubjectName, allSubjects);
                        if (matchedSubject != null && studentGradeBySubject.TryGetValue(matchedSubject.Id, out var fg))
                            score = fg.Total;

                        subjects.Add(new CertificateStudentSubject
                        {
                            SubjectName = cs.SubjectName,
                            MaxScore = cs.MaxScore,
                            MinScore = cs.MinScore,
                            Score = score,
                            IsCountedInTotal = cs.IsCountedInTotal,
                        });
                    }

                    var countedScore = subjects.Where(s => s.IsCountedInTotal).Sum(s => s.Score ?? 0);
                    var totalScore = subjects.Sum(s => s.Score ?? 0);

                    allStudentsData.Add(new CertificateStudentData
                    {
                        StudentName = student.FullName,
                        ClassName = classEntity.Name,
                        EnrollmentId = enrollment.Id,
                        SeatNumber = student.NationalId,
                        Subjects = subjects,
                        TotalBasic = countedScore,
                        TotalMaxBasic = totalMaxBasic,
                        TotalWithActivities = totalScore,
                        TotalMaxWithActivities = totalMaxAll,
                        Rank = 0,
                        Percentage = totalMaxBasic > 0 ? Math.Round(countedScore / totalMaxBasic * 100, 1) : 0,
                    });
                }
            }

            if (!allStudentsData.Any())
                return OperationResult<CertificateGenerateResponse>.Failure("لا يوجد طلاب في الفصول المحددة");

            // 4. Rank by total basic score
            allStudentsData = allStudentsData.OrderByDescending(s => s.TotalBasic).ToList();
            for (int i = 0; i < allStudentsData.Count; i++)
                allStudentsData[i].Rank = i + 1;

            var certDto = MapToDto(cert, certSubjects);
            var combinedName = classIds.Count == 1 ? classNameHint ?? "" : $"{classIds.Count} فصول";

            return OperationResult<CertificateGenerateResponse>.Success(
                new CertificateGenerateResponse
                {
                    Certificate = certDto,
                    ClassName = combinedName,
                    Students = allStudentsData,
                }, "تم تجهيز بيانات الشهادات بنجاح");
        }

        // ════════════════════════════════════════════════════════════════
        //  GRADE SHEET GENERATION (كشف بالدرجات)
        // ════════════════════════════════════════════════════════════════

        public async Task<OperationResult<CertificateGradeSheetResponse>> GenerateGradeSheetAsync(
            int certificateId, List<int> classIds, int term)
        {
            var academicTerm = (AcademicTerm)term;

            var cert = await _unitOfWork.Certificates.GetByIdAsync(certificateId);
            if (cert is null || cert.IsDeleted)
                return OperationResult<CertificateGradeSheetResponse>.Failure("الشهادة غير موجودة");

            var certSubjects = (await _unitOfWork.CertificateSubjects
                .FindAsync(s => s.CertificateId == certificateId && !s.IsDeleted))
                .OrderBy(s => s.SortOrder).ToList();

            var totalMaxBasic = certSubjects.Where(s => s.IsCountedInTotal).Sum(s => (decimal)s.MaxScore);
            var allSubjects = (await _unitOfWork.Subjects.GetAllAsync())
                .Where(s => !s.IsDeleted).ToList();

            var allRows = new List<GradeSheetStudentRow>();
            string? classNameHint = null;
            string? gradeLevelName = null;
            string? academicYearName = null;

            foreach (var classId in classIds)
            {
                var classEntity = await _unitOfWork.Classes.GetByIdAsync(classId);
                if (classEntity is null || classEntity.IsDeleted) continue;
                classNameHint ??= classEntity.Name;
                gradeLevelName ??= classEntity.GradeLevel?.Name ?? "";
                academicYearName ??= classEntity.AcademicYear?.Name ?? "";

                var enrollments = (await _unitOfWork.StudentEnrollments
                    .FindAsync(e => e.ClassId == classId && e.LeftAt == null && !e.IsDeleted))
                    .ToList();
                if (!enrollments.Any()) continue;

                var studentIds = enrollments.Select(e => e.StudentId).Distinct().ToList();
                var students = await _unitOfWork.Students.FindAsync(s => studentIds.Contains(s.Id));
                var studentDict = students.ToDictionary(s => s.Id, s => s);

                var finalGrades = await _unitOfWork.FinalGrades.GetByClassIdAsync(classId, academicTerm);
                var gradesByEnrollment = finalGrades
                    .Where(fg => !fg.IsDeleted)
                    .GroupBy(fg => fg.EnrollmentId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var enrollment in enrollments)
                {
                    if (!studentDict.TryGetValue(enrollment.StudentId, out var student))
                        continue;

                    var studentGrades = gradesByEnrollment.GetValueOrDefault(enrollment.Id) ?? new();
                    decimal totalScore = 0;
                    foreach (var cs in certSubjects.Where(s => s.IsCountedInTotal))
                    {
                        var matched = MatchSubject(cs.SubjectName, allSubjects);
                        if (matched != null)
                        {
                            var grade = studentGrades.FirstOrDefault(fg => fg.SubjectId == matched.Id);
                            if (grade != null)
                                totalScore += grade.Total;
                        }
                    }

                    allRows.Add(new GradeSheetStudentRow
                    {
                        SeatNumber = student.NationalId,
                        StudentName = student.FullName,
                        BirthDate = student.BirthDate,
                        TotalScore = totalScore,
                        MaxTotal = totalMaxBasic,
                        ClassName = classEntity.Name,
                    });
                }
            }

            if (!allRows.Any())
                return OperationResult<CertificateGradeSheetResponse>.Failure("لا يوجد طلاب في الفصول المحددة");

            allRows = allRows.OrderByDescending(r => r.TotalScore).ToList();
            for (int i = 0; i < allRows.Count; i++)
            {
                allRows[i].Rank = i + 1;
                allRows[i].RowNumber = i + 1;
            }

            var combinedName = classIds.Count == 1 ? classNameHint ?? "" : $"{classIds.Count} فصول";

            return OperationResult<CertificateGradeSheetResponse>.Success(
                new CertificateGradeSheetResponse
                {
                    Certificate = MapToDto(cert, certSubjects),
                    ClassName = combinedName,
                    GradeLevelName = gradeLevelName ?? "",
                    AcademicYearName = academicYearName ?? "",
                    Students = allRows,
                }, "تم تجهيز كشف الدرجات بنجاح");
        }

        // ════════════════════════════════════════════════════════════════
        //  HONOR ROLL GENERATION (كشف بأوائل الطلاب)
        // ════════════════════════════════════════════════════════════════

        public async Task<OperationResult<CertificateHonorRollResponse>> GenerateHonorRollAsync(
            int certificateId, List<int> classIds, int term, int topCount = 10)
        {
            var academicTerm = (AcademicTerm)term;

            var cert = await _unitOfWork.Certificates.GetByIdAsync(certificateId);
            if (cert is null || cert.IsDeleted)
                return OperationResult<CertificateHonorRollResponse>.Failure("الشهادة غير موجودة");

            var certSubjects = (await _unitOfWork.CertificateSubjects
                .FindAsync(s => s.CertificateId == certificateId && !s.IsDeleted))
                .OrderBy(s => s.SortOrder).ToList();

            var totalMaxBasic = certSubjects.Where(s => s.IsCountedInTotal).Sum(s => (decimal)s.MaxScore);
            var allSubjects = (await _unitOfWork.Subjects.GetAllAsync())
                .Where(s => !s.IsDeleted).ToList();

            var allRows = new List<GradeSheetStudentRow>();
            string? classNameHint = null;
            string? gradeLevelName = null;
            string? academicYearName = null;

            foreach (var classId in classIds)
            {
                var classEntity = await _unitOfWork.Classes.GetByIdAsync(classId);
                if (classEntity is null || classEntity.IsDeleted) continue;
                classNameHint ??= classEntity.Name;
                gradeLevelName ??= classEntity.GradeLevel?.Name ?? "";
                academicYearName ??= classEntity.AcademicYear?.Name ?? "";

                var enrollments = (await _unitOfWork.StudentEnrollments
                    .FindAsync(e => e.ClassId == classId && e.LeftAt == null && !e.IsDeleted))
                    .ToList();
                if (!enrollments.Any()) continue;

                var studentIds = enrollments.Select(e => e.StudentId).Distinct().ToList();
                var students = await _unitOfWork.Students.FindAsync(s => studentIds.Contains(s.Id));
                var studentDict = students.ToDictionary(s => s.Id, s => s);

                var finalGrades = await _unitOfWork.FinalGrades.GetByClassIdAsync(classId, academicTerm);
                var gradesByEnrollment = finalGrades
                    .Where(fg => !fg.IsDeleted)
                    .GroupBy(fg => fg.EnrollmentId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var enrollment in enrollments)
                {
                    if (!studentDict.TryGetValue(enrollment.StudentId, out var student))
                        continue;

                    var studentGrades = gradesByEnrollment.GetValueOrDefault(enrollment.Id) ?? new();
                    decimal totalScore = 0;
                    foreach (var cs in certSubjects.Where(s => s.IsCountedInTotal))
                    {
                        var matched = MatchSubject(cs.SubjectName, allSubjects);
                        if (matched != null)
                        {
                            var grade = studentGrades.FirstOrDefault(fg => fg.SubjectId == matched.Id);
                            if (grade != null)
                                totalScore += grade.Total;
                        }
                    }

                    allRows.Add(new GradeSheetStudentRow
                    {
                        SeatNumber = student.NationalId,
                        StudentName = student.FullName,
                        BirthDate = student.BirthDate,
                        TotalScore = totalScore,
                        MaxTotal = totalMaxBasic,
                        ClassName = classEntity.Name,
                    });
                }
            }

            if (!allRows.Any())
                return OperationResult<CertificateHonorRollResponse>.Failure("لا يوجد طلاب في الفصول المحددة");

            // Sort and take top N
            allRows = allRows.OrderByDescending(r => r.TotalScore).ToList();
            var topRows = allRows.Take(topCount).ToList();
            for (int i = 0; i < topRows.Count; i++)
            {
                topRows[i].Rank = i + 1;
                topRows[i].RowNumber = i + 1;
            }

            var combinedName = classIds.Count == 1 ? classNameHint ?? "" : $"{classIds.Count} فصول";

            return OperationResult<CertificateHonorRollResponse>.Success(
                new CertificateHonorRollResponse
                {
                    GradeLevelName = gradeLevelName ?? "",
                    AcademicYearName = academicYearName ?? "",
                    Term = cert.Term,
                    ExamRole = cert.ExamRole,
                    Year = cert.Year,
                    ClassName = combinedName,
                    TopCount = topCount,
                    MaxTotal = totalMaxBasic,
                    Students = topRows,
                }, "تم تجهيز كشف أوائل الطلاب بنجاح");
        }

        // ════════════════════════════════════════════════════════════════
        //  HELPERS
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Normalize Arabic text for flexible matching.
        /// Handles alif variants (أ,إ,آ → ا), ya variants (ى,ئ → ي),
        /// and ta marbouta (ة → ه), and removes tashkeel.
        /// </summary>
        private static string NormalizeArabic(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            var normalized = text
                .Replace("أ", "ا").Replace("إ", "ا").Replace("آ", "ا")
                .Replace("ى", "ي").Replace("ئ", "ي")
                .Replace("ة", "ه")
                .Replace("َّ", "").Replace("ُ", "").Replace("ِ", "")
                .Replace("ً", "").Replace("ٌ", "").Replace("ٍ", "")
                .Replace("ْ", "").Replace("ّ", "")
                .Trim();
            return normalized;
        }

        /// <summary>
        /// Try to match a certificate subject name to a DB subject using
        /// normalized Arabic comparison.
        /// </summary>
        private static Subject? MatchSubject(string certSubjectName, List<Subject> dbSubjects)
        {
            var normalizedCert = NormalizeArabic(certSubjectName);

            // 1. Exact normalized match
            var exact = dbSubjects.FirstOrDefault(s =>
                NormalizeArabic(s.Name).Equals(normalizedCert, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            // 2. Contains (one direction)
            var contain = dbSubjects.FirstOrDefault(s =>
                NormalizeArabic(s.Name).Contains(normalizedCert) ||
                normalizedCert.Contains(NormalizeArabic(s.Name)));
            if (contain != null) return contain;

            // 3. Remove leading "ال" from both and try
            var strippedCert = normalizedCert.StartsWith("ال") ? normalizedCert[2..] : normalizedCert;
            var stripped = dbSubjects.FirstOrDefault(s =>
            {
                var n = NormalizeArabic(s.Name);
                var strippedDb = n.StartsWith("ال") ? n[2..] : n;
                return strippedDb.Equals(strippedCert, StringComparison.OrdinalIgnoreCase) ||
                       strippedDb.Contains(strippedCert) ||
                       strippedCert.Contains(strippedDb);
            });
            if (stripped != null) return stripped;

            // 4. Fallback: original unnormalized contains (in case normalization hurt)
            return dbSubjects.FirstOrDefault(s =>
                s.Name.Contains(certSubjectName, StringComparison.OrdinalIgnoreCase) ||
                certSubjectName.Contains(s.Name, StringComparison.OrdinalIgnoreCase));
        }

        private CertificateDto MapToDto(Certificate entity, IReadOnlyList<CertificateSubject>? subjects = null)
        {
            var subs = subjects ?? entity.Subjects?.Where(s => !s.IsDeleted).ToList() ?? new();
            return new CertificateDto
            {
                Id = entity.Id,
                Name = entity.Name,
                GradeLevel = entity.GradeLevel,
                Term = entity.Term,
                ExamRole = entity.ExamRole,
                Year = entity.Year,
                Subjects = subs.OrderBy(s => s.SortOrder).Select(s => new CertificateSubjectDto
                {
                    Id = s.Id,
                    SubjectName = s.SubjectName,
                    MaxScore = s.MaxScore,
                    MinScore = s.MinScore,
                    IsCountedInTotal = s.IsCountedInTotal,
                    SortOrder = s.SortOrder,
                }).ToList()
            };
        }
    }
}
