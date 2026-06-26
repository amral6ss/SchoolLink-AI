using Common.Results;
using Project.BLL.DTOs.Certificates;

namespace Project.BLL.Interfaces
{
    public interface ICertificateService
    {
        Task<OperationResult<CertificateDto>> CreateCertificateAsync(CreateCertificateRequest request);
        Task<OperationResult<CertificateDto>> UpdateCertificateAsync(UpdateCertificateRequest request);
        Task<OperationResult> DeleteCertificateAsync(int id);
        Task<OperationResult<CertificateDto>> GetCertificateByIdAsync(int id);
        Task<OperationResult<IEnumerable<CertificateDto>>> GetAllCertificatesAsync();

        /// <summary>
        /// Generates per-student certificate data for given certificate template, classes, and term.
        /// Fills in real student scores from FinalGrades. classIds can contain one or more class IDs.
        /// </summary>
        Task<OperationResult<CertificateGenerateResponse>> GenerateCertificateDataAsync(int certificateId, List<int> classIds, int term);

        /// <summary>
        /// Generates a grade sheet listing all students, their totals, and rankings.
        /// classIds can contain one or more class IDs.
        /// </summary>
        Task<OperationResult<CertificateGradeSheetResponse>> GenerateGradeSheetAsync(int certificateId, List<int> classIds, int term);

        /// <summary>
        /// Generates an honor roll (كشف بأوائل الطلاب) — top N students ranked by total score.
        /// </summary>
        Task<OperationResult<CertificateHonorRollResponse>> GenerateHonorRollAsync(int certificateId, List<int> classIds, int term, int topCount = 10);
    }
}
