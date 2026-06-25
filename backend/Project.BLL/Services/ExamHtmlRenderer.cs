using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Project.BLL.Interfaces;
using Project.DAL.Context;
using Project.Domain.Entities;
using Project.Domain.Enums;

namespace Project.BLL.Services
{
    public class ExamHtmlRenderer : IExamHtmlRenderer
    {
        private readonly AppDbContext _db;

        public ExamHtmlRenderer(AppDbContext db)
        {
            _db = db;
        }

        public async Task<string> RenderExamAsync(int examId, CancellationToken ct = default)
        {
            var exam = await _db.Exams
                .Include(e => e.ClassSubjectTeacher).ThenInclude(c => c.Subject)
                .Include(e => e.Subject)
                .FirstOrDefaultAsync(e => e.Id == examId, ct);

            if (exam == null || exam.IsDeleted)
                return "<p>الامتحان غير موجود</p>";

            var groups = await _db.Set<ExamQuestionGroup>()
                .Include(g => g.Questions.Where(q => !q.IsDeleted)
                    .OrderBy(q => q.DisplayOrder))
                    .ThenInclude(q => q.Options.Where(o => !o.IsDeleted)
                        .OrderBy(o => o.DisplayOrder))
                .Where(g => g.ExamId == examId && !g.IsDeleted)
                .OrderBy(g => g.DisplayOrder)
                .ToListAsync(ct);

            var standalone = await _db.ExamQuestions
                .Include(q => q.Options.Where(o => !o.IsDeleted).OrderBy(o => o.DisplayOrder))
                .Where(q => q.ExamId == examId && q.GroupId == null && !q.IsDeleted)
                .OrderBy(q => q.DisplayOrder)
                .ToListAsync(ct);

            var subj = exam.ClassSubjectTeacher?.Subject?.Name ?? exam.Subject?.Name ?? "–";
            var uid = exam.Uid;

            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html lang='ar' dir='rtl'><head><meta charset='utf-8'>");
            sb.Append("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            sb.Append("<title>").Append(Enc(exam.Title)).Append("</title>");
            sb.Append("<style>").Append(RenderCss()).Append("</style>");
            sb.Append("</head><body>");

            sb.Append("<div id='app'>");

            // ── Toolbar ──
            sb.Append(RenderToolbar());

            // ── Header ──
            sb.Append(RenderHeader(exam, subj));

            // ── Questions ──
            sb.Append("<main class='exam-body' id='examBody'>");

            // Instructions
            sb.Append("<div class='exam-instructions'>اقرأ الأسئلة بعناية، وأجب في المكان المخصص لكل سؤال</div>");

            int qNumber = 1;
            var blocks = BuildBlocks(groups, standalone);
            foreach (var block in blocks)
            {
                if (block.Group != null)
                    sb.Append(RenderGroup(block.Group, ref qNumber, uid));
                else if (block.Question != null)
                    sb.Append(RenderSingleQuestion(block.Question, ref qNumber, uid));
            }

            sb.Append("</main>");

            // ── Footer ──
            sb.Append(RenderFooter());

            sb.Append("</div>");

            // ── JavaScript ──
            sb.Append("<script>").Append(RenderJavaScript(uid)).Append("</script>");
            sb.Append("</body></html>");

            return sb.ToString();
        }

        private string RenderCss()
        {
            return @"
*{box-sizing:border-box;margin:0;padding:0}
@page{size:A4;margin:15mm 12mm}

body{
  font-family:'IBM Plex Sans Arabic','Segoe UI',sans-serif;
  background:#faf8ff;
  color:#1a1b21;
  line-height:1.8;
  font-size:14px;
  padding:20px;
}

/* ── Main Card ── */
#app{
  max-width:210mm;
  margin:0 auto;
  background:#fff;
  border:1px solid #e3e1e9;
  border-radius:20px;
  overflow:hidden;
  min-height:297mm;
  box-shadow:0 2px 16px rgba(0,35,111,0.06);
}

/* ── Toolbar ── */
.toolbar{
  display:flex;gap:8px;padding:12px 24px;
  background:#00236f;
  position:sticky;top:0;z-index:100;
  flex-wrap:wrap;
}
.toolbar button{
  padding:8px 18px;border:none;border-radius:10px;
  font-size:12px;font-weight:600;cursor:pointer;
  transition:all .25s ease;
  font-family:'IBM Plex Sans Arabic',sans-serif;
  display:inline-flex;align-items:center;gap:6px;
  letter-spacing:0.3px;
}
.btn-save{
  background:#059669;
  color:#fff;
  box-shadow:0 2px 12px rgba(5,150,105,0.3);
}
.btn-save:hover{transform:translateY(-1px);box-shadow:0 4px 16px rgba(5,150,105,0.4)}
.btn-print{
  background:rgba(255,255,255,0.15);
  color:#fff;
}
.btn-print:hover{background:rgba(255,255,255,0.25)}
.btn-edit-toggle{
  background:#f59e0b;
  color:#fff;
  box-shadow:0 2px 12px rgba(245,158,11,0.3);
}
.btn-edit-toggle:hover{transform:translateY(-1px);box-shadow:0 4px 16px rgba(245,158,11,0.4)}
.btn-cancel{
  background:rgba(255,255,255,0.12);
  color:#fca5a5;
}
.btn-cancel:hover{background:rgba(255,255,255,0.2)}
.toast{
  position:fixed;top:24px;left:50%;transform:translateX(-50%);
  padding:12px 28px;border-radius:12px;font-size:13px;font-weight:600;
  z-index:9999;display:none;
  box-shadow:0 8px 32px rgba(0,0,0,0.15);
  font-family:'IBM Plex Sans Arabic',sans-serif;
}
.toast.success{background:#059669;color:#fff}
.toast.error{background:#dc2626;color:#fff}
.toast.show{display:block;animation:fadeIn .3s ease}
@keyframes fadeIn{from{opacity:0;transform:translateX(-50%) translateY(-10px)}to{opacity:1;transform:translateX(-50%) translateY(0)}}

/* ── Header ── */
.exam-header{
  text-align:center;
  padding:40px 32px 24px;
  position:relative;
  background:linear-gradient(180deg,#f4f3fa 0%,#fff 100%);
}
.exam-header::after{
  content:'';
  position:absolute;
  bottom:0;left:10%;right:10%;
  height:1px;
  background:linear-gradient(90deg,transparent,#c5c5d3,transparent);
}
.exam-basmala{
  font-size:22px;
  font-weight:700;
  color:#f59e0b;
  margin-bottom:6px;
}
.exam-school-name{
  font-size:12px;
  font-weight:600;
  color:#757682;
  margin-bottom:10px;
  letter-spacing:1px;
}
.exam-header h1{
  font-size:24px;
  font-weight:800;
  margin:0 0 8px;
  color:#00236f;
}
.exam-header-divider{
  width:60px;
  height:3px;
  background:linear-gradient(90deg,transparent,#f59e0b,transparent);
  margin:6px auto;
  border-radius:2px;
}
.exam-meta-box{
  display:grid;
  grid-template-columns:repeat(4,1fr);
  gap:8px;
  margin-top:16px;
}
@media(max-width:500px){
  .exam-meta-box{grid-template-columns:repeat(2,1fr)}
}
.exam-meta-item{
  background:#f4f3fa;
  border:1px solid #e3e1e9;
  border-radius:10px;
  padding:10px 6px;
  text-align:center;
  font-weight:700;
  font-size:12px;
  color:#00236f;
}
.exam-meta-item span{
  font-weight:400;
  color:#6b7280;
  display:block;
  margin-top:2px;
  font-size:13px;
}

/* ── Exam Body ── */
.exam-body{padding:24px 28px 20px}
.exam-block{margin-bottom:24px;page-break-inside:avoid}

/* ── Passage ── */
.passage-box{
  background:#f4f3fa;
  border:1px solid #e3e1e9;
  border-radius:12px;
  padding:16px 20px;
  margin-bottom:14px;
  font-size:14px;
}
.passage-title{
  font-size:15px;
  font-weight:700;
  margin-bottom:8px;
  color:#00236f;
}
.passage-text{
  text-align:justify;
  color:#444651;
  line-height:2;
}
.exam-figure{text-align:center;margin:12px 0}
.exam-figure img{
  max-width:100%;max-height:350px;
  border-radius:12px;
  display:inline-block;
}
.exam-figure svg{max-width:100%;max-height:350px;display:inline-block}
.exam-figure figcaption{
  font-size:12px;margin-top:6px;
  color:#757682;
  font-weight:500;
}

/* Exam instructions */
.exam-instructions{
  margin-bottom:20px;
  padding:12px 20px;
  background:linear-gradient(135deg,#dce1ff,#f4f3fa);
  border:1px solid #b6c4ff;
  border-radius:12px;
  font-size:13px;
  color:#00164e;
  text-align:center;
  font-weight:600;
}

/* ── Question ── */
.q-item{
  margin:16px 0;
  padding:16px 20px;
  background:#faf8ff;
  border:1px solid #e3e1e9;
  border-radius:14px;
  transition:all .2s ease;
}
.q-item:hover{
  border-color:#b6c4ff;
  box-shadow:0 2px 8px rgba(0,35,111,0.04);
}
.q-header{
  display:flex;
  align-items:baseline;
  gap:8px;
  margin-bottom:8px;
}
.q-number{
  font-weight:800;
  font-size:14px;
  color:#00236f;
  min-width:36px;
}
.q-text{
  font-weight:600;
  margin:0;
  flex:1;
  line-height:1.8;
  font-size:14px;
  color:#1a1b21;
}
.q-points{
  font-size:11px;
  color:#757682;
  font-weight:600;
  white-space:nowrap;
  margin-right:4px;
}

/* ── Options (MCQ / True-False) ── */
.q-options{
  list-style:none;
  padding:0;
  margin:8px 0 4px;
  display:grid;
  grid-template-columns:1fr 1fr;
  gap:6px 16px;
}
@media(max-width:500px){
  .q-options{grid-template-columns:1fr}
}
.q-options li{
  padding:8px 14px;
  margin:0;
  font-size:13px;
  color:#1a1b21;
  display:flex;
  align-items:center;
  gap:10px;
  border-radius:10px;
  background:#f4f3fa;
  border:1px solid #e3e1e9;
  transition:all .2s;
}
.q-options li:hover{background:#e9e7ef}
.q-options li:before{
  content:counter(opt-counter,'') '';
  counter-increment:opt-counter;
  display:inline-flex;align-items:center;justify-content:center;
  width:24px;height:24px;
  border:1.5px solid #c5c5d3;
  border-radius:8px;
  font-size:11px;font-weight:700;
  color:#757682;
  flex-shrink:0;
  transition:all .2s;
}
.q-options{ counter-reset:opt-counter; }
.q-options li.correct{
  background:#dce1ff;
  border-color:#00236f;
}
.q-options li.correct:before{
  background:#00236f;
  border-color:#00236f;
  color:#fff;
}

/* Answer lines */
.answer-line{
  border-bottom:1.5px dashed #c5c5d3;
  min-height:32px;
  margin:8px 0;
  width:80%;
}
.answer-line.tall{min-height:60px}

/* Correct answer display */
.correct-answer{
  margin-top:6px;
  padding:6px 14px;
  background:#f0fdf4;
  border:1px solid #86efac;
  border-radius:8px;
  font-size:12px;
  color:#166534;
  font-weight:600;
  display:inline-block;
}
.editing .correct-answer{
  display:block;
  width:100%;
}
.editing .correct-answer textarea{width:100%!important}

/* ── Edit mode ── */
.editing .q-item{
  background:#fefce8;
  border-color:#fde047;
}
.edit-input{
  width:100%;padding:8px 12px;
  border:1px solid #c5c5d3;
  border-radius:8px;
  font-family:'IBM Plex Sans Arabic',sans-serif;
  font-size:14px;
  background:#fff;
  color:#1a1b21;
  transition:all .2s;
}
.edit-input:focus{outline:none;border-color:#00236f;box-shadow:0 0 0 2px rgba(0,35,111,0.1)}
.edit-textarea{
  width:100%;padding:8px 12px;
  border:1px solid #c5c5d3;
  border-radius:8px;
  font-family:'IBM Plex Sans Arabic',sans-serif;
  font-size:14px;resize:vertical;
  min-height:40px;
  background:#fff;
  color:#1a1b21;
  transition:all .2s;
}
.edit-textarea:focus{outline:none;border-color:#00236f;box-shadow:0 0 0 2px rgba(0,35,111,0.1)}
.edit-points{width:64px;text-align:center}
.edit-title-input{font-size:22px;font-weight:800;text-align:center;width:100%;max-width:400px}
.edit-opt-row{display:flex;align-items:center;gap:8px;margin:4px 0}
.edit-opt-row input[type=text]{flex:1}
.edit-opt-row input[type=checkbox]{width:16px;height:16px;cursor:pointer;accent-color:#059669}

/* ── Footer ── */
.exam-footer{
  text-align:center;
  padding:24px 32px 32px;
  color:#757682;
  font-size:14px;
  font-weight:600;
  border-top:1px solid #e3e1e9;
  margin-top:8px;
}

/* ── Spinner ── */
.spinner{display:inline-block;width:16px;height:16px;border:2px solid rgba(0,35,111,0.15);border-top-color:#00236f;border-radius:50%;animation:spin .6s linear}
@keyframes spin{to{transform:rotate(360deg)}}

/* ── Print ── */
@media print{
  body{background:#fff!important;padding:0}
  #app{max-width:100%;margin:0;border-radius:0;box-shadow:none;background:#fff!important;border:none}
  .toolbar{display:none!important}
  .exam-header{background:transparent!important}
  .exam-header::after{background:linear-gradient(90deg,transparent,#ccc,transparent)!important}
  .exam-header h1{color:#1a1b21!important}
  .exam-basmala{color:#1a1b21!important}
  .exam-school-name{color:#666!important}
  .exam-meta-item{background:#f5f5f5!important;border-color:#ddd!important;color:#333!important}
  .exam-meta-item span{color:#666!important}
  .exam-instructions{background:#f9f9f9!important;border-color:#ddd!important;color:#555!important}
  .q-item{background:#fff!important;border-color:#e5e7eb!important;box-shadow:none!important}
  .q-item:hover{background:#fff!important}
  .q-text{color:#1a1b21!important}
  .q-number{color:#00236f!important}
  .q-points{color:#999!important}
  .q-options li{background:#fafafa!important;border-color:#e5e7eb!important;color:#333!important}
  .q-options li.correct{background:#fafafa!important;border-color:#e5e7eb!important}
  .q-options li:before{border-color:#999!important;color:#666!important}
  .q-options li.correct:before{background:#f5f5f5!important;border-color:#999!important;color:#666!important}
  .passage-box{background:#fafafa!important;border-color:#ddd!important}
  .passage-text{color:#333!important}
  .passage-title{color:#00236f!important}
  .correct-answer{display:none!important}
  .answer-line{border-bottom-color:#ccc!important}
  .exam-footer{color:#999!important;border-top-color:#e5e7eb!important}
  .edit-input,.edit-textarea{border:none!important;background:transparent!important;padding:0!important;color:#1a1b21!important}
  .edit-points{display:none}
  .exam-body{padding:10px 20px}
  .exam-header{padding:24px 20px 16px}
}

/* ── Mobile ── */
@media(max-width:640px){
  body{padding:10px}
  #app{margin:0;border-radius:16px}
  .exam-body{padding:16px 16px}
  .exam-header{padding:28px 20px 18px}
  .exam-header h1{font-size:20px}
  .exam-meta-box{grid-template-columns:repeat(2,1fr);font-size:11px}
  .q-options{grid-template-columns:1fr}
  .toolbar{padding:10px 16px;justify-content:center;gap:6px}
  .toolbar button{font-size:11px;padding:6px 14px}
}
";
        }

        private string RenderToolbar()
        {
            return @"
<div class='toolbar' id='toolbar'>
  <button class='btn-edit-toggle' id='editToggleBtn' onclick='toggleEdit()'>
    <span>&#9998;&#65039;</span><span>تعديل الأسئلة</span>
  </button>
  <button class='btn-save' id='saveBtn' onclick='saveExam()' style='display:none'>
    <span>&#128190;</span><span>حفظ التعديلات</span>
  </button>
  <button class='btn-cancel' id='cancelBtn' onclick='cancelEdit()' style='display:none'>
    <span>&#10060;</span><span>إلغاء</span>
  </button>
  <button class='btn-print' onclick='window.print()'>
    <span>&#128424;</span><span>طباعة</span>
  </button>
</div>
<div class='toast' id='toast'></div>
";
        }

        private string RenderHeader(Exam exam, string subjectName)
        {
            return $@"
<header class='exam-header'>
  <div class='exam-basmala'>بسم الله الرحمن الرحيم</div>
  <div class='exam-school-name'>وزارة التربية والتعليم</div>
  <h1>{Enc(exam.Title)}</h1>
  <div class='exam-header-divider'></div>
  <div class='exam-meta-box'>
    <div class='exam-meta-item'>المادة: <span id='headerSubject'>{Enc(subjectName)}</span></div>
    <div class='exam-meta-item'>المدة: <span id='headerDuration'>{exam.DurationMinutes}</span> دقيقة</div>
    <div class='exam-meta-item'>الدرجة الكلية: <span id='headerTotal'>{exam.TotalScore:0.##}</span></div>
    <div class='exam-meta-item'>عدد الأسئلة: <span id='headerQuestionsCount'></span></div>
  </div>
</header>
";
        }

        private string RenderGroup(ExamQuestionGroup g, ref int qNumber, Guid uid)
        {
            var sb = new StringBuilder();
            sb.Append("<section class='exam-block'>");

            switch (g.DisplayType)
            {
                case TemplateContentType.Passage:
                    sb.Append("<table class='exam-content' style='width:100%'><tr><td>");
                    sb.Append("<div class='passage-box'>");
                    if (!string.IsNullOrWhiteSpace(g.ContentTitle))
                        sb.Append($"<div class='passage-title'>{Enc(g.ContentTitle)}</div>");
                    sb.Append($"<div class='passage-text'>{Enc(g.ContentText)}</div>");
                    sb.Append("</div>");
                    sb.Append("</td></tr></table>");
                    break;

                case TemplateContentType.Image:
                case TemplateContentType.Map:
                    sb.Append("<figure class='exam-figure'>");
                    if (!string.IsNullOrWhiteSpace(g.ImageUrl))
                        sb.Append($"<img src='{Enc(g.ImageUrl)}' alt='{Enc(g.ContentText)}' />");
                    if (!string.IsNullOrWhiteSpace(g.ContentText))
                        sb.Append($"<figcaption>{Enc(g.ContentText)}</figcaption>");
                    sb.Append("</figure>");
                    break;

                case TemplateContentType.Diagram:
                    sb.Append($"<div class='exam-figure'>{g.ContentText}</div>");
                    break;
            }

            foreach (var q in g.Questions.OrderBy(x => x.DisplayOrder))
                sb.Append(RenderQuestionBody(q, qNumber++, uid));

            sb.Append("</section>");
            return sb.ToString();
        }

        private string RenderSingleQuestion(ExamQuestion q, ref int qNumber, Guid uid)
        {
            var sb = new StringBuilder();
            sb.Append("<section class='exam-block'>");

            if (q.DisplayType == TemplateContentType.Diagram && !string.IsNullOrWhiteSpace(q.ContentText))
                sb.Append($"<div class='exam-figure'>{q.ContentText}</div>");

            sb.Append(RenderQuestionBody(q, qNumber++, uid));
            sb.Append("</section>");
            return sb.ToString();
        }

        private string RenderQuestionBody(ExamQuestion q, int n, Guid uid)
        {
            var sb = new StringBuilder();
            sb.Append($"<div class='q-item' data-qid='{q.Id}' data-qt='{(int)q.QuestionType}'>");
            sb.Append("<div class='q-header'>");
            sb.Append($"<span class='q-number'>س{n}:</span>");
            sb.Append($"<span class='q-text' id='qtext_{q.Id}'>{Enc(q.QuestionText)}</span>");
            if (q.Points > 0)
                sb.Append($"<span class='q-points' id='qpts_{q.Id}'>({q.Points:0.##} درجات)</span>");
            else
                sb.Append($"<span class='q-points' id='qpts_{q.Id}' style='display:none'>(0)</span>");
            sb.Append("</div>");

            switch (q.QuestionType)
            {
                case QuestionType.MultipleChoice:
                case QuestionType.TrueFalse:
                    sb.Append("<ol class='q-options' id='qopts_").Append(q.Id).Append("'>");
                    foreach (var opt in q.Options.OrderBy(o => o.DisplayOrder))
                    {
                        var cls = opt.IsCorrect ? " class='correct'" : "";
                        sb.Append($"<li{cls} data-optid='{opt.Id}'>{Enc(opt.OptionText)}</li>");
                    }
                    sb.Append("</ol>");
                    break;

                case QuestionType.FillBlank:
                    sb.Append("<div class='answer-line'></div>");
                    if (!string.IsNullOrWhiteSpace(q.CorrectAnswer))
                        sb.Append($"<div class='correct-answer' id='correct_{q.Id}' data-answer='{Enc(q.CorrectAnswer)}'>الإجابة: {Enc(q.CorrectAnswer)}</div>");
                    else
                        sb.Append($"<div class='correct-answer' id='correct_{q.Id}' data-answer='' style='display:none'></div>");
                    break;

                case QuestionType.Essay:
                    sb.Append("<div class='answer-line tall'></div>");
                    sb.Append("<div class='answer-line tall'></div>");
                    if (!string.IsNullOrWhiteSpace(q.CorrectAnswer))
                        sb.Append($"<div class='correct-answer' id='correct_{q.Id}' data-answer='{Enc(q.CorrectAnswer)}'>الإجابة النموذجية: {Enc(q.CorrectAnswer)}</div>");
                    else
                        sb.Append($"<div class='correct-answer' id='correct_{q.Id}' data-answer='' style='display:none'></div>");
                    break;
            }

            sb.Append("</div>");
            return sb.ToString();
        }

        private string RenderFooter()
        {
            return "<footer class='exam-footer'>— انتهت الأسئلة ، مع تمنياتنا بالتوفيق والنجاح —</footer>";
        }

        private string RenderJavaScript(Guid uid)
        {
            return $@"
(function(){{
  let editMode = false;
  let originalData = null;

  // Set questions count
  var qCount = document.querySelectorAll('.q-item').length;
  var qcEl = document.getElementById('headerQuestionsCount');
  if (qcEl) qcEl.textContent = qCount;

  window.toggleEdit = function(){{
    editMode = !editMode;
    if (editMode) enableEdit(); else disableEdit();
  }};

  window.cancelEdit = function(){{
    if (originalData) restoreData(originalData);
    editMode = false;
    disableEdit();
  }};

  function enableEdit(){{
    document.getElementById('editToggleBtn').innerHTML = '<span>&#128274;</span><span>إنهاء التعديل</span>';
    document.getElementById('saveBtn').style.display = '';
    document.getElementById('cancelBtn').style.display = '';
    document.body.classList.add('editing');

    // Save original data for cancel
    originalData = captureData();

    // ── Make header fields editable ──
    var titleEl = document.querySelector('.exam-header h1');
    if (titleEl){{
      var inp = document.createElement('input');
      inp.type = 'text';
      inp.className = 'edit-input edit-title-input';
      inp.value = titleEl.textContent.trim();
      inp.id = 'edit_title';
      titleEl.textContent = '';
      titleEl.appendChild(inp);
    }}
    var subjEl = document.getElementById('headerSubject');
    if (subjEl){{
      var inp = document.createElement('input');
      inp.type = 'text';
      inp.className = 'edit-input';
      inp.style.width = '120px';
      inp.value = subjEl.textContent.trim();
      inp.id = 'edit_headerSubject';
      subjEl.textContent = '';
      subjEl.appendChild(inp);
    }}
    var durEl = document.getElementById('headerDuration');
    if (durEl){{
      var inp = document.createElement('input');
      inp.type = 'number';
      inp.min = '1';
      inp.className = 'edit-input edit-points';
      inp.value = durEl.textContent.trim();
      inp.id = 'edit_headerDuration';
      durEl.textContent = '';
      durEl.appendChild(inp);
    }}
    var totalEl = document.getElementById('headerTotal');
    if (totalEl){{
      var inp = document.createElement('input');
      inp.type = 'number';
      inp.min = '0';
      inp.step = '0.5';
      inp.className = 'edit-input edit-points';
      inp.value = totalEl.textContent.trim();
      inp.id = 'edit_headerTotal';
      totalEl.textContent = '';
      totalEl.appendChild(inp);
    }}

    // Make questions editable
    document.querySelectorAll('.q-item').forEach(function(item){{
      var qid = item.dataset.qid;
      var qt = parseInt(item.dataset.qt); // question type
      var textEl = document.getElementById('qtext_' + qid);
      if (textEl){{
        var txt = textEl.textContent.replace(/^\d+\.\s*/, '');
        var inp = document.createElement('textarea');
        inp.className = 'edit-textarea';
        inp.value = txt;
        inp.dataset.original = txt;
        inp.id = 'edit_qtext_' + qid;
        textEl.innerHTML = '';
        textEl.appendChild(inp);
      }}

      var ptsEl = document.getElementById('qpts_' + qid);
      if (ptsEl){{
        var ptsText = ptsEl.textContent.trim();
        var match = ptsText.match(/[\d.]+/);
        var ptsNum = match ? parseFloat(match[0]) : 0;
        var inp = document.createElement('input');
        inp.type = 'number';
        inp.step = '0.5';
        inp.min = '0';
        inp.className = 'edit-input edit-points';
        inp.value = ptsNum;
        inp.dataset.original = ptsNum;
        inp.id = 'edit_qpts_' + qid;
        ptsEl.innerHTML = '';
        ptsEl.appendChild(inp);
      }}

      // Make correct answer editable for FillBlank/Essay
      if (qt === 3 || qt === 4) {{
        var caEl = document.getElementById('correct_' + qid);
        if (caEl) {{
          var answer = caEl.dataset.answer || '';
          caEl.style.display = '';
          caEl.innerHTML = '<div style=""font-size:12px;color:#166534;font-weight:600;margin-bottom:4px;"">الإجابة النموذجية:</div>' +
            '<textarea class=""edit-textarea"" style=""width:100%;min-height:80px;resize:vertical;"" id=""edit_correct_' + qid + '"">' + escHtml(answer) + '</textarea>';
        }}
      }}

      // Make options editable
      var optsEl = document.getElementById('qopts_' + qid);
      if (optsEl){{
        var lis = optsEl.querySelectorAll('li');
        lis.forEach(function(li){{
          var oid = li.dataset.optid;
          var txt = li.textContent.trim();
          var isCorrect = li.classList.contains('correct');
          var row = document.createElement('div');
          row.className = 'edit-opt-row';
          row.innerHTML = '<input type=""checkbox"" ' + (isCorrect ? 'checked' : '') + ' id=""edit_optcb_' + oid + '"">' +
            '<input type=""text"" class=""edit-input"" value=""' + escHtml(txt) + '"" id=""edit_opttxt_' + oid + '"" data-original=""' + escHtml(txt) + '"">';
          li.innerHTML = '';
          li.appendChild(row);
        }});
      }}
    }});
  }}

  function disableEdit(){{
    document.getElementById('editToggleBtn').innerHTML = '<span>&#9998;&#65039;</span><span>تعديل الأسئلة</span>';
    document.getElementById('saveBtn').style.display = 'none';
    document.getElementById('cancelBtn').style.display = 'none';
    document.body.classList.remove('editing');
    originalData = null;

    // Remove input elements and restore static display
    document.querySelectorAll('.q-item').forEach(function(item){{
      var qid = item.dataset.qid;
      var textEl = document.getElementById('qtext_' + qid);
      var inp = document.getElementById('edit_qtext_' + qid);
      if (textEl && inp){{
        var num = textEl.textContent.match(/^\d+/);
        textEl.textContent = (num ? num[0] + '. ' : '') + inp.value;
      }}

      var ptsEl = document.getElementById('qpts_' + qid);
      var ptsInp = document.getElementById('edit_qpts_' + qid);
      if (ptsEl && ptsInp){{
        var ptsVal = parseFloat(ptsInp.value) || 0;
        if (ptsVal > 0)
          ptsEl.textContent = '(' + ptsVal + ' درجات)';
        else
          ptsEl.textContent = '(0)';
      }}
    }});

    // Restore header fields
    var titleInp = document.getElementById('edit_title');
    if (titleInp){{
      var h1 = document.querySelector('.exam-header h1');
      if (h1) h1.textContent = titleInp.value;
    }}
    var subjInp = document.getElementById('edit_headerSubject');
    if (subjInp){{
      var el = document.getElementById('headerSubject');
      if (el) el.textContent = subjInp.value;
    }}
    var durInp = document.getElementById('edit_headerDuration');
    if (durInp){{
      var el = document.getElementById('headerDuration');
      if (el) el.textContent = durInp.value;
    }}
    var totalInp = document.getElementById('edit_headerTotal');
    if (totalInp){{
      var el = document.getElementById('headerTotal');
      if (el) el.textContent = totalInp.value;
    }}
  }}

  function captureData(){{
    var data = {{ questions: [] }};
    document.querySelectorAll('.q-item').forEach(function(item){{
      var q = {{ id: parseInt(item.dataset.qid), questionText: '', points: 0, displayOrder: 0, correctAnswer: null, options: [] }};
      data.questions.push(q);
    }});
    return data;
  }}

  function restoreData(data){{
    // Just reload the page for simplicity
    location.reload();
  }}

  function getToken() {{
    try {{
      return localStorage.getItem('access_token') || '';
    }} catch(e) {{
      return '';
    }}
  }}

  window.saveExam = function(){{
    var btn = document.getElementById('saveBtn');
    btn.disabled = true;
    btn.innerHTML = '<span class=""spinner""></span> جاري الحفظ...';

    var dto = {{
      uid: '{uid}',
      title: (document.getElementById('edit_title')?.value || document.querySelector('.exam-header h1')?.textContent || '').trim(),
      durationMinutes: parseInt(document.getElementById('edit_headerDuration')?.value || document.getElementById('headerDuration')?.textContent) || 30,
      totalScore: parseFloat(document.getElementById('edit_headerTotal')?.value || document.getElementById('headerTotal')?.textContent) || 0,
      questions: []
    }};

    document.querySelectorAll('.q-item').forEach(function(item){{
      var qid = parseInt(item.dataset.qid);
      var qt = parseInt(item.dataset.qt);
      var textEl = document.getElementById('edit_qtext_' + qid);
      var ptsEl = document.getElementById('edit_qpts_' + qid);
      var txt = textEl ? textEl.value : '';
      var pts = ptsEl ? parseFloat(ptsEl.value || '0') : 0;

      var correctAnswer = null;
      // For fill-blank and essay, get the correct answer
      if (qt === 3 || qt === 4) {{
        var caInp = document.getElementById('edit_correct_' + qid);
        correctAnswer = caInp ? caInp.value || null : null;
      }}

      var q = {{ id: qid, questionText: txt, points: pts, displayOrder: 0, correctAnswer: correctAnswer, options: [] }};

      var optsEl = document.getElementById('qopts_' + qid);
      if (optsEl){{
        optsEl.querySelectorAll('li').forEach(function(li){{
          var oid = parseInt(li.dataset.optid);
          var optTxt = li.querySelector('.edit-opt-row input[type=""text""]')?.value || '';
          var isCorrect = li.querySelector('.edit-opt-row input[type=""checkbox""]')?.checked || false;
          q.options.push({{ id: oid, optionText: optTxt, isCorrect: isCorrect, displayOrder: 0 }});
        }});
      }}

      dto.questions.push(q);
    }});

    var token = getToken();
    var headers = {{ 'Content-Type': 'application/json' }};
    if (token) headers['Authorization'] = 'Bearer ' + token;

    fetch('/api/exam/' + '{uid}' + '/save', {{
      method: 'PUT',
      headers: headers,
      body: JSON.stringify(dto)
    }})
    .then(function(r){{ return r.json() }})
    .then(function(result){{
      btn.disabled = false;
      btn.innerHTML = '<span>&#128190;</span><span>حفظ التعديلات</span>';
      if (result.isSuccess){{
        showToast('✅ تم حفظ التعديلات بنجاح', 'success');
        setTimeout(function(){{ location.reload(); }}, 1200);
      }} else {{
        showToast('❌ ' + (result.message || 'فشل الحفظ'), 'error');
      }}
    }})
    .catch(function(err){{
      btn.disabled = false;
      btn.innerHTML = '<span>&#128190;</span><span>حفظ التعديلات</span>';
      showToast('❌ خطأ في الاتصال: ' + err.message, 'error');
    }});
  }};

  function showToast(msg, type){{
    var el = document.getElementById('toast');
    el.textContent = msg;
    el.className = 'toast ' + type + ' show';
    setTimeout(function(){{ el.className = 'toast'; }}, 3000);
  }}

  function escHtml(s){{
    if (!s) return '';
    return s.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/""/g,'&quot;');
  }}
}})();
</script>
";
        }

        private static string Enc(string? s) => string.IsNullOrEmpty(s) ? "" : WebUtility.HtmlEncode(s);

        private static List<(ExamQuestionGroup? Group, ExamQuestion? Question)> BuildBlocks(
            List<ExamQuestionGroup> groups, List<ExamQuestion> standalone)
        {
            var list = new List<(ExamQuestionGroup? Group, ExamQuestion? Question)>();
            foreach (var g in groups)
                list.Add((g, null));
            foreach (var q in standalone)
                list.Add((null, q));
            return list.OrderBy(b => b.Group?.DisplayOrder ?? b.Question?.DisplayOrder ?? 0).ToList();
        }
    }
}
