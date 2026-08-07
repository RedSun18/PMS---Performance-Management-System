using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PerformanceManagement.Core.Data.Migrations
{
    /// <summary>
    /// Schema: adds the Arabic counterparts of the per-form KPI/Competency snapshot text
    /// columns (PmFormKpis.KpiDefinitionAr/FormulaMetricAr, PmFormCompetencies.DescriptionAr)
    /// — KpiMasters.DescriptionAr/FormulaAr and CompetencyMasters.DescriptionAr already existed
    /// as columns but were never populated for the Demo data, and the snapshot rows never had
    /// an Arabic column to copy them into at all. Found during the CAT: viewing any PM Form in
    /// Arabic showed the KPI Purpose/Definition and Formula/Metric columns (and Competency
    /// Description) in English regardless of UI language.
    ///
    /// Data: same "fix the code, not the already-created rows" pattern as
    /// BackfillKpiAndCompetencyContent — populates the 18 KpiMasters + 12 CompetencyMasters
    /// Arabic fields, then copies them into the already-existing PmFormKpis/PmFormCompetencies
    /// snapshot rows. Guarded to the Demo database by name (current_database() = 'pms_demo'):
    /// this migration also runs against the real AIC Development database via the same
    /// db.Database.MigrateAsync() call in Program.cs, and Development's own KPI/Competency
    /// master data is real, human-entered content that must never be overwritten by demo copy.
    /// Idempotent: KpiMasters/CompetencyMasters UPDATEs always set the same fixed values (safe
    /// to re-run); PmFormKpis/PmFormCompetencies UPDATEs only touch rows where the Arabic
    /// definition is still null/empty, so a row a real user has since edited by hand is never
    /// clobbered by a later re-run of this same migration.
    /// </summary>
    public partial class AddArabicKpiCompetencyContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FormulaMetricAr",
                table: "PmFormKpis",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KpiDefinitionAr",
                table: "PmFormKpis",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionAr",
                table: "PmFormCompetencies",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "KpiMasters" SET "DescriptionAr" = 'يقيس الزيادة السنوية في إجمالي أقساط التأمين المكتتبة.', "FormulaAr" = '((إجمالي الأقساط للفترة الحالية − إجمالي الأقساط للفترة السابقة) / إجمالي الأقساط للفترة السابقة) × 100' WHERE "KpiId" = 'KPI001' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "DescriptionAr" = 'يتتبع ربحية نشاط الاكتتاب قبل احتساب دخل الاستثمار.', "FormulaAr" = '(الأقساط المكتسبة − المطالبات المتكبدة − مصاريف الاكتتاب) / الأقساط المكتسبة × 100' WHERE "KpiId" = 'KPI002' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "DescriptionAr" = 'يقيس المصاريف التشغيلية كنسبة من الأقساط المكتسبة.', "FormulaAr" = 'إجمالي المصاريف التشغيلية / الأقساط المكتسبة × 100' WHERE "KpiId" = 'KPI003' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "DescriptionAr" = 'العائد المحقق من محفظة الأصول المستثمرة للشركة.', "FormulaAr" = 'صافي دخل الاستثمار / متوسط الأصول المستثمرة × 100' WHERE "KpiId" = 'KPI004' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "DescriptionAr" = 'يقيس مدى التزام الإنفاق الفعلي للإدارة بالموازنة السنوية المعتمدة.', "FormulaAr" = '(1 − |الإنفاق الفعلي − الإنفاق المعتمد| / الإنفاق المعتمد) × 100' WHERE "KpiId" = 'KPI005' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "DescriptionAr" = 'يعكس مستوى رضا العملاء الإجمالي من خلال استبيانات ما بعد التعامل.', "FormulaAr" = 'متوسط تقييم الاستبيان (على مقياس من 1 إلى 5)، محوّلاً إلى نسبة مئوية' WHERE "KpiId" = 'KPI006' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "DescriptionAr" = 'نسبة الوثائق المؤهلة التي تم تجديدها عند الاستحقاق.', "FormulaAr" = '(عدد الوثائق المجددة / عدد الوثائق المؤهلة للتجديد) × 100' WHERE "KpiId" = 'KPI007' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "DescriptionAr" = 'متوسط عدد الأيام اللازمة لتسوية المطالبة من الإخطار الأول وحتى الدفع.', "FormulaAr" = 'إجمالي أيام تسوية جميع المطالبات / عدد المطالبات المسواة' WHERE "KpiId" = 'KPI008' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "DescriptionAr" = 'يقيس مدى استعداد العملاء للتوصية بالشركة.', "FormulaAr" = 'نسبة المروجين % − نسبة المنتقدين %' WHERE "KpiId" = 'KPI009' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "DescriptionAr" = 'متوسط عدد الوثائق المختلفة التي يحملها العميل النشط الواحد.', "FormulaAr" = 'إجمالي الوثائق السارية / إجمالي العملاء النشطين' WHERE "KpiId" = 'KPI010' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "DescriptionAr" = 'نسبة المعاملات التشغيلية المؤهلة التي تمت دون تدخل يدوي.', "FormulaAr" = '(المعاملات المؤتمتة / إجمالي المعاملات المؤهلة) × 100' WHERE "KpiId" = 'KPI011' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "DescriptionAr" = 'نسبة المطالبات التي تمت معالجتها دون إعادة فتح الحالة أو تصحيح الدفعة.', "FormulaAr" = '(المطالبات المعالجة بشكل صحيح / إجمالي المطالبات المعالجة) × 100' WHERE "KpiId" = 'KPI012' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "DescriptionAr" = 'الانخفاض في إجمالي التعرض للمخاطر المكتتبة من خلال إعادة التأمين وإجراءات المحفظة.', "FormulaAr" = '((التعرض للفترة السابقة − التعرض للفترة الحالية) / التعرض للفترة السابقة) × 100' WHERE "KpiId" = 'KPI013' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "DescriptionAr" = 'نسبة نقاط الفحص الداخلية والتنظيمية التي تم اجتيازها دون ملاحظات.', "FormulaAr" = '(نقاط الفحص المجتازة / إجمالي نقاط الفحص المدققة) × 100' WHERE "KpiId" = 'KPI014' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "DescriptionAr" = 'نسبة إنجاز مراحل خارطة الطريق المعتمدة للتحول الرقمي.', "FormulaAr" = '(المراحل المنجزة / المراحل المخطط لها) × 100' WHERE "KpiId" = 'KPI015' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "DescriptionAr" = 'نسبة إتمام التدريب الإلزامي والتدريب الخاص بالدور الوظيفي في الوقت المحدد.', "FormulaAr" = '(التدريبات المكتملة / التدريبات المخصصة) × 100' WHERE "KpiId" = 'KPI016' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "DescriptionAr" = 'نسبة الموظفين المحتفظ بهم خلال فترة المراجعة، باستثناء التناقص المخطط له.', "FormulaAr" = '((عدد الموظفين في البداية − حالات المغادرة الطوعية) / عدد الموظفين في البداية) × 100' WHERE "KpiId" = 'KPI017' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "DescriptionAr" = 'نسبة المشاركة في برامج تطوير القيادات وتخطيط التعاقب الوظيفي.', "FormulaAr" = '(الموظفون المسجلون / الموظفون المؤهلون) × 100' WHERE "KpiId" = 'KPI018' AND current_database() = 'pms_demo';

                UPDATE "CompetencyMasters" SET "DescriptionAr" = 'القدرة على توجيه الآخرين وتحفيزهم وتطويرهم لتحقيق الأهداف المشتركة.' WHERE "CompId" = 'COM001' AND current_database() = 'pms_demo';
                UPDATE "CompetencyMasters" SET "DescriptionAr" = 'الوضوح والاحترافية والفعالية في التواصل الكتابي والشفهي مع الزملاء والعملاء.' WHERE "CompId" = 'COM002' AND current_database() = 'pms_demo';
                UPDATE "CompetencyMasters" SET "DescriptionAr" = 'العمل بفعالية مع الآخرين عبر الفرق والإدارات لتحقيق الأهداف المشتركة.' WHERE "CompId" = 'COM003' AND current_database() = 'pms_demo';
                UPDATE "CompetencyMasters" SET "DescriptionAr" = 'الاستجابة بفعالية للتغيرات في الأولويات والعمليات وظروف العمل.' WHERE "CompId" = 'COM004' AND current_database() = 'pms_demo';
                UPDATE "CompetencyMasters" SET "DescriptionAr" = 'التصرف باستمرار بأمانة وعدالة والتزام بالمعايير الأخلاقية للشركة والقطاع.' WHERE "CompId" = 'COM005' AND current_database() = 'pms_demo';
                UPDATE "CompetencyMasters" SET "DescriptionAr" = 'توقع احتياجات العملاء والاستجابة لها بمستوى خدمة عالٍ باستمرار.' WHERE "CompId" = 'COM006' AND current_database() = 'pms_demo';
                UPDATE "CompetencyMasters" SET "DescriptionAr" = 'تحليل المشكلات والبيانات المعقدة لتحديد الأسباب الجذرية والحلول العملية.' WHERE "CompId" = 'COM007' AND current_database() = 'pms_demo';
                UPDATE "CompetencyMasters" SET "DescriptionAr" = 'إظهار المعرفة والمهارة الفنية اللازمة للمهام الأساسية للدور الوظيفي.' WHERE "CompId" = 'COM008' AND current_database() = 'pms_demo';
                UPDATE "CompetencyMasters" SET "DescriptionAr" = 'تحديد وتقييم المخاطر التشغيلية والاكتتابية والتخفيف منها بشكل مناسب.' WHERE "CompId" = 'COM009' AND current_database() = 'pms_demo';
                UPDATE "CompetencyMasters" SET "DescriptionAr" = 'استخدام البيانات والأدلة ذات الصلة لدعم القرارات والأحكام.' WHERE "CompId" = 'COM010' AND current_database() = 'pms_demo';
                UPDATE "CompetencyMasters" SET "DescriptionAr" = 'التخطيط للمبادرات وتنفيذها وتسليمها في الوقت المحدد وضمن النطاق المحدد.' WHERE "CompId" = 'COM011' AND current_database() = 'pms_demo';
                UPDATE "CompetencyMasters" SET "DescriptionAr" = 'الحفاظ على معرفة محدّثة وعملية بأنظمة التأمين ومتطلبات الامتثال المعمول بها.' WHERE "CompId" = 'COM012' AND current_database() = 'pms_demo';

                UPDATE "PmFormKpis" pk
                SET "KpiDefinitionAr" = km."DescriptionAr", "FormulaMetricAr" = km."FormulaAr"
                FROM "KpiMasters" km
                WHERE pk."KpiCode" = km."KpiId"
                  AND (pk."KpiDefinitionAr" IS NULL OR pk."KpiDefinitionAr" = '')
                  AND current_database() = 'pms_demo';

                UPDATE "PmFormCompetencies" pc
                SET "DescriptionAr" = cm."DescriptionAr"
                FROM "CompetencyMasters" cm
                WHERE pc."CompCode" = cm."CompId"
                  AND (pc."DescriptionAr" IS NULL OR pc."DescriptionAr" = '')
                  AND current_database() = 'pms_demo';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FormulaMetricAr",
                table: "PmFormKpis");

            migrationBuilder.DropColumn(
                name: "KpiDefinitionAr",
                table: "PmFormKpis");

            migrationBuilder.DropColumn(
                name: "DescriptionAr",
                table: "PmFormCompetencies");
        }
    }
}
