using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Learning;

namespace OptimizeAll.Infrastructure.Persistence.Configurations;

internal sealed class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> b)
    {
        b.ToTable("learning_courses");
        b.Property(x => x.Slug).HasMaxLength(80).IsRequired();
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Subtitle).HasMaxLength(300).IsRequired();
        b.Property(x => x.BadgeName).HasMaxLength(120).IsRequired();
        b.Property(x => x.Skills).HasJsonList();
        // Catalog: published courses by category, featured first.
        b.HasIndex(x => new { x.Status, x.Category, x.SortOrder });
    }
}

internal sealed class CourseVersionConfiguration : IEntityTypeConfiguration<CourseVersion>
{
    public void Configure(EntityTypeBuilder<CourseVersion> b)
    {
        b.ToTable("learning_course_versions");
        b.Property(x => x.ContentJson).IsRequired(); // long text: unbounded (a course document with every lesson)
        b.Property(x => x.ContentSha256).HasMaxLength(64).IsRequired();
        b.Property(x => x.Note).HasMaxLength(500);
        b.HasIndex(x => new { x.CourseId, x.Number }).IsUnique();
        b.HasIndex(x => new { x.CourseId, x.PackVersion });
        b.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EnrolmentConfiguration : IEntityTypeConfiguration<Enrolment>
{
    public void Configure(EntityTypeBuilder<Enrolment> b)
    {
        b.ToTable("learning_enrolments");
        b.Property(x => x.LastLessonSlug).HasMaxLength(80);
        b.HasIndex(x => new { x.UserId, x.CourseId }).IsUnique();
        // Admin analytics per course.
        b.HasIndex(x => new { x.CourseId, x.EnrolledAt });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class LessonProgressConfiguration : IEntityTypeConfiguration<LessonProgress>
{
    public void Configure(EntityTypeBuilder<LessonProgress> b)
    {
        b.ToTable("learning_lesson_progress");
        b.Property(x => x.LessonSlug).HasMaxLength(80).IsRequired();
        b.HasIndex(x => new { x.EnrolmentId, x.LessonSlug }).IsUnique();
        b.HasOne<Enrolment>().WithMany().HasForeignKey(x => x.EnrolmentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class KnowledgeCheckAnswerConfiguration : IEntityTypeConfiguration<KnowledgeCheckAnswer>
{
    public void Configure(EntityTypeBuilder<KnowledgeCheckAnswer> b)
    {
        b.ToTable("learning_knowledge_check_answers");
        b.Property(x => x.LessonSlug).HasMaxLength(80).IsRequired();
        b.Property(x => x.Selected).HasJsonList();
        b.HasIndex(x => new { x.EnrolmentId, x.LessonSlug, x.QuestionIndex }).IsUnique();
        b.HasOne<Enrolment>().WithMany().HasForeignKey(x => x.EnrolmentId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ExamAttemptConfiguration : IEntityTypeConfiguration<ExamAttempt>
{
    public void Configure(EntityTypeBuilder<ExamAttempt> b)
    {
        b.ToTable("learning_exam_attempts");
        b.Property(x => x.ActiveKey).HasMaxLength(80);
        // One attempt in progress per learner and course (NULL once finished; NULLs never collide).
        b.HasIndex(x => x.ActiveKey).IsUnique();
        b.Property(x => x.DrawJson).IsRequired(); // long text: unbounded
        b.Property(x => x.AnswersJson).IsRequired(); // long text: unbounded
        // Attempts per learner and course in the rolling day, newest first.
        b.HasIndex(x => new { x.UserId, x.CourseId, x.StartedAt });
        // Admin analytics per course.
        b.HasIndex(x => new { x.CourseId, x.Status, x.SubmittedAt });
        b.HasOne<Enrolment>().WithMany().HasForeignKey(x => x.EnrolmentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<CourseVersion>().WithMany().HasForeignKey(x => x.CourseVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ExamAnswerConfiguration : IEntityTypeConfiguration<ExamAnswer>
{
    public void Configure(EntityTypeBuilder<ExamAnswer> b)
    {
        b.ToTable("learning_exam_answers");
        b.Property(x => x.QuestionId).HasMaxLength(64).IsRequired();
        b.Property(x => x.Selected).HasJsonList();
        b.HasIndex(x => new { x.AttemptId, x.QuestionId }).IsUnique();
        // Question analytics: % correct per question of a course.
        b.HasIndex(x => new { x.CourseId, x.QuestionId });
        b.HasOne<ExamAttempt>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CertificateConfiguration : IEntityTypeConfiguration<Certificate>
{
    public void Configure(EntityTypeBuilder<Certificate> b)
    {
        b.ToTable("learning_certificates");
        b.Property(x => x.VerificationCode).HasMaxLength(20).IsRequired();
        b.HasIndex(x => x.VerificationCode).IsUnique();
        b.Property(x => x.ActiveKey).HasMaxLength(80);
        // One valid certificate per learner and course (revoked ones keep their row with ActiveKey NULL).
        b.HasIndex(x => x.ActiveKey).IsUnique();
        b.Property(x => x.HolderName).HasMaxLength(200).IsRequired();
        b.Property(x => x.CourseSlug).HasMaxLength(80).IsRequired();
        b.Property(x => x.CourseTitle).HasMaxLength(200).IsRequired();
        b.Property(x => x.BadgeName).HasMaxLength(120).IsRequired();
        b.Property(x => x.Skills).HasJsonList();
        b.Property(x => x.RecipientSalt).HasMaxLength(64).IsRequired();
        b.Property(x => x.RevocationReason).HasMaxLength(500);
        b.Ignore(x => x.IsRevoked);
        b.HasIndex(x => new { x.UserId, x.IssuedAt });
        b.HasIndex(x => new { x.CourseId, x.IssuedAt });
        b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
    }
}
