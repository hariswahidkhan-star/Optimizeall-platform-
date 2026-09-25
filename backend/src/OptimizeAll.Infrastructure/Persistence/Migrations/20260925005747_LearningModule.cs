using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OptimizeAll.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LearningModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "learning_courses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Slug = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Origin = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PublishedVersionId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    LatestVersionId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    PublishedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    IsFeatured = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Subtitle = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Category = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Level = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EstimatedMinutes = table.Column<int>(type: "int", nullable: false),
                    ModuleCount = table.Column<int>(type: "int", nullable: false),
                    LessonCount = table.Column<int>(type: "int", nullable: false),
                    BadgeName = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Skills = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_courses", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "learning_certificates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CourseId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CourseVersionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AttemptId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    VerificationCode = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActiveKey = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HolderName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CourseSlug = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CourseTitle = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BadgeName = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Skills = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Score = table.Column<int>(type: "int", nullable: true),
                    IssuedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    IssuedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    RecipientSalt = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RevokedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    RevokedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    RevocationReason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_certificates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_certificates_learning_courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "learning_courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_learning_certificates_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "learning_course_versions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CourseId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PackVersion = table.Column<int>(type: "int", nullable: true),
                    BasedOnVersionId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci"),
                    ContentJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContentSha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "char(36)", nullable: true, collation: "ascii_general_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_course_versions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_course_versions_learning_courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "learning_courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "learning_enrolments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CourseId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    EnrolledAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    LastLessonSlug = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastActivityAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    LessonsCompletedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    PassedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    BestScore = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_enrolments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_enrolments_learning_courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "learning_courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_learning_enrolments_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "learning_exam_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    EnrolmentId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CourseId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CourseVersionId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Status = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActiveKey = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    DeadlineAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true),
                    QuestionCount = table.Column<int>(type: "int", nullable: false),
                    PassingScore = table.Column<int>(type: "int", nullable: false),
                    DrawJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AnswersJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CorrectCount = table.Column<int>(type: "int", nullable: true),
                    Score = table.Column<int>(type: "int", nullable: true),
                    Passed = table.Column<bool>(type: "tinyint(1)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_exam_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_exam_attempts_learning_course_versions_CourseVersio~",
                        column: x => x.CourseVersionId,
                        principalTable: "learning_course_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_learning_exam_attempts_learning_enrolments_EnrolmentId",
                        column: x => x.EnrolmentId,
                        principalTable: "learning_enrolments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "learning_knowledge_check_answers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    EnrolmentId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    LessonSlug = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    QuestionIndex = table.Column<int>(type: "int", nullable: false),
                    Selected = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsCorrect = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_knowledge_check_answers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_knowledge_check_answers_learning_enrolments_Enrolme~",
                        column: x => x.EnrolmentId,
                        principalTable: "learning_enrolments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "learning_lesson_progress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    EnrolmentId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    LessonSlug = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_lesson_progress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_lesson_progress_learning_enrolments_EnrolmentId",
                        column: x => x.EnrolmentId,
                        principalTable: "learning_enrolments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "learning_exam_answers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    AttemptId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    CourseId = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    QuestionId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Selected = table.Column<string>(type: "json", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsCorrect = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AttemptPassed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "datetime(6)", precision: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_exam_answers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_exam_answers_learning_exam_attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalTable: "learning_exam_attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_learning_certificates_ActiveKey",
                table: "learning_certificates",
                column: "ActiveKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_learning_certificates_CourseId_IssuedAt",
                table: "learning_certificates",
                columns: new[] { "CourseId", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_certificates_UserId_IssuedAt",
                table: "learning_certificates",
                columns: new[] { "UserId", "IssuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_certificates_VerificationCode",
                table: "learning_certificates",
                column: "VerificationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_learning_course_versions_CourseId_Number",
                table: "learning_course_versions",
                columns: new[] { "CourseId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_learning_course_versions_CourseId_PackVersion",
                table: "learning_course_versions",
                columns: new[] { "CourseId", "PackVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_courses_Slug",
                table: "learning_courses",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_learning_courses_Status_Category_SortOrder",
                table: "learning_courses",
                columns: new[] { "Status", "Category", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_enrolments_CourseId_EnrolledAt",
                table: "learning_enrolments",
                columns: new[] { "CourseId", "EnrolledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_enrolments_UserId_CourseId",
                table: "learning_enrolments",
                columns: new[] { "UserId", "CourseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_learning_exam_answers_AttemptId_QuestionId",
                table: "learning_exam_answers",
                columns: new[] { "AttemptId", "QuestionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_learning_exam_answers_CourseId_QuestionId",
                table: "learning_exam_answers",
                columns: new[] { "CourseId", "QuestionId" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_exam_attempts_ActiveKey",
                table: "learning_exam_attempts",
                column: "ActiveKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_learning_exam_attempts_CourseId_Status_SubmittedAt",
                table: "learning_exam_attempts",
                columns: new[] { "CourseId", "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_exam_attempts_CourseVersionId",
                table: "learning_exam_attempts",
                column: "CourseVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_learning_exam_attempts_EnrolmentId",
                table: "learning_exam_attempts",
                column: "EnrolmentId");

            migrationBuilder.CreateIndex(
                name: "IX_learning_exam_attempts_UserId_CourseId_StartedAt",
                table: "learning_exam_attempts",
                columns: new[] { "UserId", "CourseId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_knowledge_check_answers_EnrolmentId_LessonSlug_Ques~",
                table: "learning_knowledge_check_answers",
                columns: new[] { "EnrolmentId", "LessonSlug", "QuestionIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_learning_lesson_progress_EnrolmentId_LessonSlug",
                table: "learning_lesson_progress",
                columns: new[] { "EnrolmentId", "LessonSlug" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "learning_certificates");

            migrationBuilder.DropTable(
                name: "learning_exam_answers");

            migrationBuilder.DropTable(
                name: "learning_knowledge_check_answers");

            migrationBuilder.DropTable(
                name: "learning_lesson_progress");

            migrationBuilder.DropTable(
                name: "learning_exam_attempts");

            migrationBuilder.DropTable(
                name: "learning_course_versions");

            migrationBuilder.DropTable(
                name: "learning_enrolments");

            migrationBuilder.DropTable(
                name: "learning_courses");
        }
    }
}
